"""Mejoras de experiencia operativa Lebaref en Frappe Desk 16:
- Identificador en primera columna y distribución limpia de tablas
- Columnas reales de Código/Clave y Descripción en Solicitudes, Cotizaciones, OCs y Recepciones
- Flujo directo Solicitud -> Capturar Cotización -> Comparativa (ocultando RFQ al auxiliar)
- Ficha Técnica Visual del Producto estilo ERP original con foto grande y especificaciones
- Pestaña '¿A quién cotizar?' integrada dentro de Proveedores + baja/fusión/ocultamiento de proveedores
- Módulo de Clientes vinculado a Obras/Proyectos y botón Habilitar/Deshabilitar Departamentos
"""
import html
import json
import uuid
import frappe
from frappe.custom.doctype.custom_field.custom_field import create_custom_fields
from frappe.custom.doctype.property_setter.property_setter import make_property_setter
from frappe.permissions import add_permission
from frappe.core.doctype.custom_docperm.custom_docperm import update_custom_docperm
from frappe.utils import add_days, flt, today

TIPOS_ITEMS_COMPRA = (
    "Material Request Item",
    "Request for Quotation Item",
    "Supplier Quotation Item",
    "Purchase Order Item",
    "Purchase Receipt Item",
)


def sincronizar_item(doc, method=None):
    """Mantiene sincronizado el campo principal custom_lebaref_codigo_barras con la tabla barcodes."""
    cb_principal = (doc.get("custom_lebaref_codigo_barras") or "").strip()
    barcodes = doc.get("barcodes") or []
    if not cb_principal and barcodes:
        primero = (barcodes[0].barcode or "").strip()
        if primero:
            doc.custom_lebaref_codigo_barras = primero
    elif cb_principal:
        existentes = {(b.barcode or "").strip() for b in barcodes}
        if cb_principal not in existentes:
            ya_usado = frappe.db.get_value("Item Barcode", {"barcode": cb_principal}, "parent")
            if not ya_usado or ya_usado == doc.name:
                doc.append("barcodes", {"barcode": cb_principal})


def _calcular_clave_y_descripcion(item_code, item_name="", description="", concepto_libre="", articulo_original=""):
    es_libre = item_code == "LEBAREF-CONCEPTO-LIBRE" or bool(concepto_libre)
    if es_libre:
        txt = html.unescape(concepto_libre or description or item_name or "Concepto libre").strip()
        return "SIN SKU (Libre)", txt[:140]

    if not item_code:
        return "", (item_name or description or "")[:140]

    datos_it = frappe.db.get_value(
        "Item",
        item_code,
        ["item_name", "custom_lebaref_codigo_barras"],
        as_dict=True,
    ) if frappe.db.has_column("Item", "custom_lebaref_codigo_barras") else frappe.db.get_value(
        "Item",
        item_code,
        ["item_name"],
        as_dict=True,
    )
    cb = ((datos_it or {}).get("custom_lebaref_codigo_barras") or "").strip()
    nom = ((datos_it or {}).get("item_name") or item_name or description or item_code).strip()

    if articulo_original:
        clave = f"{cb or item_code} (Sust. {articulo_original})"
    elif cb and cb.upper() != item_code.upper():
        clave = f"{cb} ({item_code})"
    else:
        clave = item_code
    return clave[:140], nom[:140]


def sincronizar_vistas_partidas(doc, method=None):
    """Puebla custom_lebaref_clave_vista y custom_lebaref_descripcion_vista en cada renglón."""
    for row in getattr(doc, "items", None) or []:
        clave, desc = _calcular_clave_y_descripcion(
            row.get("item_code") or "",
            row.get("item_name") or "",
            row.get("description") or "",
            row.get("custom_lebaref_concepto") or "",
            row.get("custom_lebaref_articulo_original") or "",
        )
        row.custom_lebaref_clave_vista = clave
        row.custom_lebaref_descripcion_vista = desc

    if doc.doctype == "Material Request" and not (doc.get("custom_lebaref_nombre_solicitud") or "").strip():
        titulo_base = (doc.get("title") or "").strip()
        if titulo_base and titulo_base not in ("Purchase", "Material Transfer", "Material Issue"):
            doc.custom_lebaref_nombre_solicitud = titulo_base[:140]


def _asegurar_permiso_rol(doctype, role, perm_dict):
    if not frappe.db.exists("DocType", doctype) or not frappe.db.exists("Role", role):
        return
    existente = frappe.db.get_value(
        "Custom DocPerm",
        {"parent": doctype, "role": role, "permlevel": 0, "if_owner": 0},
        "name",
    )
    if not existente:
        try:
            add_permission(doctype, role, ptype="read")
        except Exception:
            pass
        existente = frappe.db.get_value(
            "Custom DocPerm",
            {"parent": doctype, "role": role, "permlevel": 0, "if_owner": 0},
            "name",
        )
    if existente:
        try:
            update_custom_docperm(existente, perm_dict)
        except Exception:
            pass


def _ps(doctype, fieldname, prop, value, prop_type="Check"):
    if not frappe.db.exists("DocType", doctype):
        return
    if fieldname:
        meta = frappe.get_meta(doctype)
        if not meta.has_field(fieldname):
            return
    make_property_setter(
        doctype,
        fieldname,
        prop,
        value,
        prop_type,
        for_doctype=not bool(fieldname),
        validate_fields_for_doctype=False,
    )


def configurar_ux_lebaref():
    """Instala campos, pestañas, permisos y columnas limpias con ID en primera posición."""
    ultimo_campo_sup = "supplier_details"
    if frappe.db.exists("DocType", "Supplier"):
        meta_sup = frappe.get_meta("Supplier")
        propios_sup = {"custom_tab_quien_cotizar", "custom_especialidad_cotizar", "custom_quien_cotizar_html"}
        nativos_sup = [f.fieldname for f in meta_sup.fields if f.fieldname and f.fieldname not in propios_sup]
        if nativos_sup:
            ultimo_campo_sup = nativos_sup[-1]

    campos = {
        "Item": [
            {
                "fieldname": "custom_ficha_visual_html",
                "label": "Ficha Técnica del Producto",
                "fieldtype": "HTML",
                "insert_after": "naming_series",
            },
            {
                "fieldname": "custom_sec_especificaciones_lebaref",
                "label": "Ficha Técnica y Ubicación Lebaref",
                "fieldtype": "Section Break",
                "insert_after": "stock_uom",
            },
            {
                "fieldname": "custom_lebaref_codigo_barras",
                "label": "Clave / Código de Barras",
                "fieldtype": "Data",
                "insert_after": "custom_sec_especificaciones_lebaref",
                "in_list_view": 1,
                "in_standard_filter": 1,
            },
            {
                "fieldname": "custom_lebaref_ubicacion",
                "label": "Ubicación / Anaquel",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_codigo_barras",
                "in_list_view": 1,
                "in_standard_filter": 1,
            },
            {
                "fieldname": "custom_lebaref_stock_minimo",
                "label": "Stock Mínimo",
                "fieldtype": "Float",
                "insert_after": "custom_lebaref_ubicacion",
            },
            {
                "fieldname": "custom_lebaref_ultimo_costo",
                "label": "Último Costo Compra (s/IVA)",
                "fieldtype": "Currency",
                "insert_after": "custom_lebaref_stock_minimo",
            },
            {
                "fieldname": "custom_cb_espec_lebaref",
                "fieldtype": "Column Break",
                "insert_after": "custom_lebaref_ultimo_costo",
            },
            {
                "fieldname": "custom_lebaref_modelo",
                "label": "Modelo",
                "fieldtype": "Data",
                "insert_after": "custom_cb_espec_lebaref",
            },
            {
                "fieldname": "custom_lebaref_medidas",
                "label": "Medidas / Dimensiones",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_modelo",
            },
            {
                "fieldname": "custom_lebaref_capacidad",
                "label": "Capacidad Eléctrica / Uso",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_medidas",
            },
            {
                "fieldname": "custom_lebaref_especificacion_1",
                "label": "Especificación 1",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_capacidad",
            },
            {
                "fieldname": "custom_lebaref_especificacion_2",
                "label": "Especificación 2",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_especificacion_1",
            },
        ],
        "Material Request": [
            {
                "fieldname": "custom_lebaref_nombre_solicitud",
                "label": "Nombre de la Solicitud",
                "fieldtype": "Data",
                "insert_after": "title",
                "allow_on_submit": 1,
                "in_list_view": 1,
                "in_standard_filter": 1,
            },
        ],
        "Supplier": [
            {
                "fieldname": "custom_tab_quien_cotizar",
                "label": "¿A quién cotizar? (Especialidades)",
                "fieldtype": "Tab Break",
                "insert_after": ultimo_campo_sup,
            },
            {
                "fieldname": "custom_especialidad_cotizar",
                "label": "Especialidad / Qué cotizarle",
                "fieldtype": "Data",
                "insert_after": "custom_tab_quien_cotizar",
                "in_list_view": 1,
                "in_standard_filter": 1,
            },
            {
                "fieldname": "custom_quien_cotizar_html",
                "label": "Directorio ¿A quién cotizar?",
                "fieldtype": "HTML",
                "insert_after": "custom_especialidad_cotizar",
            },
        ],
    }

    for dt_item in TIPOS_ITEMS_COMPRA:
        campos[dt_item] = [
            {
                "fieldname": "custom_lebaref_clave_vista",
                "label": "Código / Clave",
                "fieldtype": "Data",
                "read_only": 1,
                "in_list_view": 1,
                "columns": 2,
                "insert_after": "item_code",
            },
            {
                "fieldname": "custom_lebaref_descripcion_vista",
                "label": "Descripción del Artículo / Concepto",
                "fieldtype": "Data",
                "read_only": 1,
                "in_list_view": 1,
                "columns": 4,
                "insert_after": "custom_lebaref_clave_vista",
            },
        ]

    if frappe.db.exists("DocType", "Lebaref CFDI"):
        campos["Lebaref CFDI"] = [
            {
                "fieldname": "custom_serie_folio",
                "label": "Serie / Folio",
                "fieldtype": "Data",
                "read_only": 1,
                "in_list_view": 1,
                "insert_after": "uuid",
            },
            {
                "fieldname": "custom_fecha_timbrado",
                "label": "Fecha Timbrado",
                "fieldtype": "Data",
                "read_only": 1,
                "in_list_view": 1,
                "insert_after": "custom_serie_folio",
            },
            {
                "fieldname": "custom_oc_origen",
                "label": "OC Origen",
                "fieldtype": "Data",
                "read_only": 1,
                "in_list_view": 1,
                "insert_after": "recepcion",
            },
            {
                "fieldname": "custom_subtotal",
                "label": "Subtotal s/IVA",
                "fieldtype": "Currency",
                "options": "moneda",
                "read_only": 1,
                "insert_after": "moneda",
            },
            {
                "fieldname": "custom_iva",
                "label": "IVA",
                "fieldtype": "Currency",
                "options": "moneda",
                "read_only": 1,
                "insert_after": "custom_subtotal",
            },
            {
                "fieldname": "custom_conceptos_xml",
                "label": "Conceptos Facturados",
                "fieldtype": "Small Text",
                "read_only": 1,
                "insert_after": "total",
            },
        ]

    create_custom_fields(campos, update=True)

    # Permitir CFDIs históricos sin Purchase Receipt en Lebaref CFDI
    if frappe.db.exists("DocType", "Lebaref CFDI"):
        _ps("Lebaref CFDI", "recepcion", "reqd", 0, "Check")

    # 1. Identificador / Folio SIEMPRE como 1.ª columna en todas las tablas del ERP
    doctypes_id_primero = (
        "Material Request",
        "Request for Quotation",
        "Supplier Quotation",
        "Purchase Order",
        "Purchase Receipt",
        "Stock Entry",
        "Item",
        "Supplier",
        "Customer",
        "Project",
        "Department",
        "Lebaref CFDI",
        "Lebaref Herramienta",
        "Lebaref Kit",
        "Lebaref Prestamo",
        "Lebaref Vehiculo",
    )
    for dt in doctypes_id_primero:
        _ps(dt, None, "title_field", "", "Data")
        _ps(dt, None, "show_title_field_in_link", 0, "Check")

    # Buscadores amigables al desplegar Links aunque title_field esté vacío
    _ps("Item", None, "search_fields", "custom_lebaref_codigo_barras,item_name,item_group", "Data")
    _ps("Project", None, "search_fields", "project_name,customer", "Data")
    _ps("Customer", None, "search_fields", "customer_name,tax_id", "Data")
    _ps("Supplier", None, "search_fields", "supplier_name,tax_id,custom_especialidad_cotizar", "Data")

    # 2. Distribución limpia de columnas en Listas (evitando truncamiento y ocultando 'company' redundante)
    config_listas = {
        "Material Request": {
            "mostrar": ["custom_lebaref_nombre_solicitud", "custom_lebaref_solicitante", "custom_lebaref_departamento", "schedule_date"],
            "ocultar": ["title", "material_request_type", "company", "transaction_date", "per_ordered", "per_received"],
        },
        "Item": {
            "mostrar": ["custom_lebaref_codigo_barras", "item_name", "custom_lebaref_ubicacion"],
            "ocultar": ["image", "item_group", "stock_uom", "is_stock_item", "has_variants", "variant_of"],
        },
        "Supplier": {
            "mostrar": ["custom_especialidad_cotizar", "tax_id", "mobile_no"],
            "ocultar": ["supplier_name", "supplier_group", "country", "supplier_type"],
        },
        "Customer": {
            "mostrar": ["tax_id", "customer_group", "mobile_no"],
            "ocultar": ["customer_name", "territory", "customer_type"],
        },
        "Project": {
            "mostrar": ["project_name", "customer", "status"],
            "ocultar": ["company", "project_type", "is_active", "percent_complete", "expected_end_date", "estimated_costing"],
        },
        "Department": {
            "mostrar": ["department_name", "disabled"],
            "ocultar": ["company", "parent_department", "is_group"],
        },
        "Purchase Order": {
            "mostrar": ["supplier_name", "transaction_date", "grand_total"],
            "ocultar": ["supplier", "company", "base_grand_total", "per_received", "per_billed", "is_subcontracted"],
        },
        "Purchase Receipt": {
            "mostrar": ["supplier_name", "posting_date", "grand_total"],
            "ocultar": ["supplier", "company", "base_grand_total", "per_billed", "is_return"],
        },
        "Supplier Quotation": {
            "mostrar": ["supplier_name", "transaction_date", "grand_total"],
            "ocultar": ["supplier", "company", "base_grand_total", "valid_till"],
        },
        "Stock Entry": {
            "mostrar": ["stock_entry_type", "project", "posting_date"],
            "ocultar": ["company", "purpose", "from_warehouse", "to_warehouse", "per_transferred", "is_return"],
        },
        "Lebaref CFDI": {
            "mostrar": ["custom_serie_folio", "proveedor", "custom_oc_origen", "total"],
            "ocultar": ["uuid", "recepcion", "rfc_emisor", "rfc_receptor", "moneda"],
        },
    }
    for dt, reglas in config_listas.items():
        for fn in reglas.get("ocultar", ()):
            _ps(dt, fn, "in_list_view", 0, "Check")
        for fn in reglas.get("mostrar", ()):
            _ps(dt, fn, "in_list_view", 1, "Check")

    # 3. Columnas claras en tablas hijas de Solicitudes, Cotizaciones, Órdenes y Recepciones
    _ps("Material Request Item", "item_code", "in_list_view", 0, "Check")
    _ps("Material Request Item", "item_name", "in_list_view", 0, "Check")
    _ps("Material Request Item", "description", "in_list_view", 0, "Check")
    _ps("Material Request Item", "schedule_date", "in_list_view", 0, "Check")
    _ps("Material Request Item", "warehouse", "in_list_view", 0, "Check")
    _ps("Material Request Item", "custom_lebaref_clave_vista", "in_list_view", 1, "Check")
    _ps("Material Request Item", "custom_lebaref_clave_vista", "columns", 2, "Int")
    _ps("Material Request Item", "custom_lebaref_descripcion_vista", "in_list_view", 1, "Check")
    _ps("Material Request Item", "custom_lebaref_descripcion_vista", "columns", 4, "Int")
    _ps("Material Request Item", "qty", "in_list_view", 1, "Check")
    _ps("Material Request Item", "qty", "columns", 1, "Int")
    _ps("Material Request Item", "uom", "in_list_view", 1, "Check")
    _ps("Material Request Item", "uom", "columns", 1, "Int")
    _ps("Material Request Item", "custom_lebaref_partida", "in_list_view", 1, "Check")
    _ps("Material Request Item", "custom_lebaref_partida", "columns", 2, "Int")

    for dt_po in ("Purchase Order Item", "Supplier Quotation Item"):
        _ps(dt_po, "item_code", "in_list_view", 0, "Check")
        _ps(dt_po, "schedule_date", "in_list_view", 0, "Check")
        _ps(dt_po, "custom_lebaref_clave_vista", "in_list_view", 1, "Check")
        _ps(dt_po, "custom_lebaref_clave_vista", "columns", 2, "Int")
        _ps(dt_po, "custom_lebaref_descripcion_vista", "in_list_view", 1, "Check")
        _ps(dt_po, "custom_lebaref_descripcion_vista", "columns", 4, "Int")
        _ps(dt_po, "qty", "in_list_view", 1, "Check")
        _ps(dt_po, "qty", "columns", 1, "Int")
        _ps(dt_po, "rate", "in_list_view", 1, "Check")
        _ps(dt_po, "rate", "columns", 1, "Int")
        _ps(dt_po, "amount", "in_list_view", 1, "Check")
        _ps(dt_po, "amount", "columns", 2, "Int")

    # 4. Ocultar pestañas irrelevantes en Item (Ventas, Fabricación, Calidad) para lucir la Ficha Técnica
    for tab_hide in ("sales_details", "manufacturing", "quality_tab", "deferred_revenue"):
        _ps("Item", tab_hide, "hidden", 1, "Check")

    # Limpiar List View Settings cacheados para que Frappe tome las columnas nuevas
    if frappe.db.exists("DocType", "List View Settings"):
        frappe.db.sql("DELETE FROM `tabList View Settings` WHERE name IN %s", (doctypes_id_primero,))

    # 5. Permisos operativos para Departamento, Eliminar Proveedores y Clientes + Obras
    _asegurar_permiso_rol("Department", "Purchase Manager", {"read": 1, "write": 1, "create": 1, "select": 1})
    _asegurar_permiso_rol("Department", "Stock Manager", {"read": 1, "write": 1, "create": 1, "select": 1})
    _asegurar_permiso_rol("Department", "Purchase User", {"read": 1, "select": 1})
    _asegurar_permiso_rol("Department", "Stock User", {"read": 1, "select": 1})

    _asegurar_permiso_rol("Supplier", "Purchase Manager", {"read": 1, "write": 1, "create": 1, "delete": 1, "select": 1})
    _asegurar_permiso_rol("Contact", "Purchase Manager", {"read": 1, "write": 1, "create": 1, "delete": 1, "select": 1})
    _asegurar_permiso_rol("Address", "Purchase Manager", {"read": 1, "write": 1, "create": 1, "delete": 1, "select": 1})

    for dt_cli in ("Customer", "Customer Group", "Territory", "Project"):
        _asegurar_permiso_rol(dt_cli, "Purchase Manager", {"read": 1, "write": 1, "create": 1, "delete": 1, "select": 1})
        _asegurar_permiso_rol(dt_cli, "Purchase User", {"read": 1, "select": 1})
        _asegurar_permiso_rol(dt_cli, "Stock User", {"read": 1, "select": 1})

    # Grupos de cliente en español y territorio base
    if frappe.db.exists("DocType", "Customer Group"):
        raiz_cg = frappe.db.get_value("Customer Group", {"is_group": 1}, "name") or "All Customer Groups"
        for cg_nom in ("Comercial / Privado", "Industrial", "Gobierno", "Residencial"):
            if not frappe.db.exists("Customer Group", cg_nom):
                frappe.get_doc({
                    "doctype": "Customer Group",
                    "customer_group_name": cg_nom,
                    "parent_customer_group": raiz_cg,
                    "is_group": 0,
                }).insert(ignore_permissions=True)

    if frappe.db.exists("DocType", "Territory"):
        raiz_tr = frappe.db.get_value("Territory", {"is_group": 1}, "name") or "All Territories"
        for tr_nom in ("México", "Yucatán / Sureste"):
            if not frappe.db.exists("Territory", tr_nom):
                frappe.get_doc({
                    "doctype": "Territory",
                    "territory_name": tr_nom,
                    "parent_territory": raiz_tr,
                    "is_group": 0,
                }).insert(ignore_permissions=True)


@frappe.whitelist(methods=["GET"])
def ficha_producto(item_code: str):
    """Devuelve todos los datos para renderizar la Ficha Técnica Visual estilo ERP original."""
    from lebaref_compras.impresion_carta import svg_code128

    doc = frappe.get_doc("Item", item_code)
    doc.check_permission("read")

    bins = []
    stock_total = 0.0
    if frappe.has_permission("Bin", "read"):
        for b in frappe.get_list(
            "Bin",
            filters={"item_code": doc.name, "actual_qty": ["!=", 0]},
            fields=["warehouse", "actual_qty"],
            order_by="actual_qty desc",
            limit_page_length=50,
        ):
            qty_b = flt(b.actual_qty)
            stock_total += qty_b
            bins.append({"almacen": b.warehouse, "cantidad": qty_b})

    cb = (doc.get("custom_lebaref_codigo_barras") or "").strip()
    if not cb and doc.get("barcodes"):
        cb = (doc.barcodes[0].barcode or "").strip()
    cb_render = cb or doc.name

    factor_compra = 1.0
    uom_compra = doc.purchase_uom or doc.stock_uom
    for u in doc.get("uoms") or []:
        if u.uom == uom_compra and flt(u.conversion_factor) > 0:
            factor_compra = flt(u.conversion_factor)
            break

    ultimo_costo = flt(doc.get("custom_lebaref_ultimo_costo") or 0.0)
    if ultimo_costo <= 0:
        ultimo_costo = flt(
            frappe.db.get_value(
                "Item Price",
                {"item_code": doc.name, "buying": 1},
                "price_list_rate",
            ) or 0.0
        )

    claves_prov = [
        {"proveedor": s.supplier, "clave": s.supplier_part_no}
        for s in (doc.get("supplier_items") or [])
        if s.supplier_part_no
    ]

    grupos_disp = []
    if frappe.has_permission("Item Group", "read"):
        grupos_disp = frappe.get_list("Item Group", filters={"is_group": 0}, pluck="name", order_by="name asc", limit_page_length=200)

    return {
        "codigo_interno": doc.name,
        "codigo_barras": cb_render,
        "nombre": doc.item_name or doc.name,
        "categoria": doc.item_group or "—",
        "grupos_disponibles": grupos_disp,
        "puede_editar": bool(doc.has_permission("write")),
        "marca": doc.brand or "—",
        "modelo": doc.get("custom_lebaref_modelo") or "—",
        "medidas": doc.get("custom_lebaref_medidas") or "—",
        "capacidad": doc.get("custom_lebaref_capacidad") or "—",
        "espec1": doc.get("custom_lebaref_especificacion_1") or "—",
        "espec2": doc.get("custom_lebaref_especificacion_2") or "—",
        "ubicacion": doc.get("custom_lebaref_ubicacion") or "Almacén General",
        "imagen": doc.image or "",
        "es_inventariable": bool(doc.is_stock_item),
        "stock_total": stock_total,
        "stock_minimo": flt(doc.get("custom_lebaref_stock_minimo") or 0.0),
        "ultimo_costo": ultimo_costo,
        "uom_almacen": doc.stock_uom or "Nos",
        "uom_compra": uom_compra or doc.stock_uom or "Nos",
        "factor_compra": factor_compra,
        "bins": bins,
        "claves_proveedores": claves_prov[:8],
        "svg_barcode": svg_code128(cb_render, alto=34, ancho_modulo=1.35).replace("margin-left:auto;", "margin:0 auto;"),
    }


@frappe.whitelist(methods=["POST"])
def guardar_nombre_solicitud(solicitud: str, nombre: str):
    doc = frappe.get_doc("Material Request", solicitud)
    doc.check_permission("write")
    limpio = (nombre or "").strip()[:140]
    if not limpio:
        frappe.throw("Escribe un nombre descriptivo para identificar la solicitud.")
    doc.db_set("custom_lebaref_nombre_solicitud", limpio)
    doc.db_set("title", limpio)
    return {"ok": True, "nombre": limpio}


@frappe.whitelist(methods=["GET"])
def esquema_cotizacion_directa(solicitud: str):
    """Prepara la captura directa de cotización desde una Solicitud sin pasar manualmente por Peticiones (RFQ)."""
    from lebaref_compras import comercial

    mr = frappe.get_doc("Material Request", solicitud)
    mr.check_permission("read")
    if mr.docstatus != 1 or mr.material_request_type != "Purchase":
        frappe.throw("Abre una solicitud de compra confirmada para capturar cotizaciones.")

    proveedores = frappe.get_list(
        "Supplier",
        filters={"disabled": 0},
        pluck="name",
        order_by="name asc",
        limit_page_length=1000,
    )
    impuestos = comercial.opciones(comercial.IMPUESTOS, {"company": mr.company, "disabled": 0})
    fletes = comercial.opciones(comercial.ENVIO, {"company": mr.company, "disabled": 0, "shipping_rule_type": "Buying"})
    pagos = comercial.opciones(comercial.PAGO)

    partidas = []
    for r in mr.items:
        clave, desc = _calcular_clave_y_descripcion(
            r.item_code,
            r.item_name,
            r.description,
            r.custom_lebaref_concepto,
        )
        partidas.append({
            "mr_item": r.name,
            "idx": r.idx,
            "clave": clave,
            "descripcion": desc,
            "qty": flt(r.qty),
            "uom": r.uom or r.stock_uom or "Nos",
            "obra": r.project or "",
            "partida": r.custom_lebaref_partida or "",
        })

    default_iva = next((x for x in impuestos if "16%" in x), "")
    return {
        "solicitud": mr.name,
        "nombre_solicitud": mr.get("custom_lebaref_nombre_solicitud") or mr.title or mr.name,
        "proveedores": proveedores,
        "impuestos": impuestos,
        "default_iva": default_iva,
        "fletes": fletes,
        "pagos": pagos,
        "valid_till": add_days(today(), 30),
        "partidas": partidas,
    }


@frappe.whitelist(methods=["POST"])
def registrar_cotizacion_directa(solicitud: str, datos: str):
    """Crea/reutiliza internamente la RFQ y registra la Supplier Quotation directamente desde la Solicitud."""
    from lebaref_compras import operaciones

    mr = frappe.get_doc("Material Request", solicitud)
    mr.check_permission("read")
    payload = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(payload, dict):
        frappe.throw("Datos de cotización inválidos.")

    proveedor = (payload.get("supplier") or "").strip()
    if not proveedor:
        frappe.throw("Selecciona un proveedor.")

    precios_por_mri = {}
    for row in payload.get("rows") or []:
        mri = (row.get("mr_item") or "").strip()
        rate = flt(row.get("rate") or 0)
        dias = max(0, int(flt(row.get("lead_time_days") or 0)))
        if mri and rate > 0:
            precios_por_mri[mri] = (rate, dias)

    if not precios_por_mri:
        frappe.throw("Captura al menos un precio unitario mayor que cero.")

    # Buscar si ya existe una RFQ confirmada de esta solicitud que incluya a este proveedor y esos renglones
    rfq_usar = None
    rfq_candidatas = frappe.db.sql(
        """
        SELECT DISTINCT rfq.name
        FROM `tabRequest for Quotation` rfq
        INNER JOIN `tabRequest for Quotation Item` rfqi ON rfqi.parent = rfq.name
        INNER JOIN `tabRequest for Quotation Supplier` rfqs ON rfqs.parent = rfq.name
        WHERE rfq.docstatus = 1
          AND rfqi.material_request = %s
          AND rfqs.supplier = %s
        ORDER BY rfq.creation DESC
        """,
        (mr.name, proveedor),
        as_dict=True,
    )
    for cand in rfq_candidatas:
        rfq_doc = frappe.get_doc("Request for Quotation", cand.name)
        mris_rfq = {ri.material_request_item for ri in rfq_doc.items if ri.material_request_item}
        if set(precios_por_mri.keys()).issubset(mris_rfq):
            rfq_usar = rfq_doc
            break

    if not rfq_usar:
        res_rfq = operaciones.ejecutar(
            "peticion",
            json.dumps({"suppliers": proveedor}, ensure_ascii=False),
            str(uuid.uuid4()),
            mr.name,
        )
        rfq_usar = frappe.get_doc("Request for Quotation", res_rfq["folio"])

    filas_oferta = []
    for ri in rfq_usar.items:
        if ri.material_request_item in precios_por_mri:
            rate_val, dias_val = precios_por_mri[ri.material_request_item]
            filas_oferta.append({
                "id": ri.name,
                "rate": rate_val,
                "lead_time_days": dias_val,
            })

    oferta_datos = {
        "supplier": proveedor,
        "valid_till": payload.get("valid_till") or add_days(today(), 30),
        "rows": filas_oferta,
    }
    for campo_opc in ("taxes_and_charges", "shipping_rule", "payment_terms_template", "fecha_programada_pago"):
        val_opc = (payload.get(campo_opc) or "").strip()
        if val_opc:
            oferta_datos[campo_opc] = val_opc

    res_sq = operaciones.ejecutar(
        "oferta",
        json.dumps(oferta_datos, ensure_ascii=False),
        str(uuid.uuid4()),
        rfq_usar.name,
    )
    return res_sq


@frappe.whitelist(methods=["POST"])
def alternar_estado_departamento(departamento: str):
    """Habilita o deshabilita un Departamento sin errores de nodo raíz/árbol."""
    doc = frappe.get_doc("Department", departamento)
    doc.check_permission("write")
    if doc.is_group:
        frappe.throw("Selecciona un departamento operativo específico (no el nodo agrupador).")
    nuevo = 0 if int(doc.disabled or 0) else 1
    doc.db_set("disabled", nuevo)
    frappe.clear_cache(doctype="Department")
    return {"ok": True, "disabled": nuevo}


@frappe.whitelist(methods=["GET"])
def info_baja_proveedor(proveedor: str):
    """Consulta si un proveedor tiene historial transaccional para ofrecer Borrado definitivo, Fusión u Ocultamiento."""
    doc = frappe.get_doc("Supplier", proveedor)
    doc.check_permission("read")
    conteos = {
        "cotizaciones": len(frappe.get_all("Supplier Quotation", filters={"supplier": doc.name}, pluck="name")),
        "ordenes": len(frappe.get_all("Purchase Order", filters={"supplier": doc.name}, pluck="name")),
        "recepciones": len(frappe.get_all("Purchase Receipt", filters={"supplier": doc.name}, pluck="name")),
        "facturas": len(frappe.get_all("Purchase Invoice", filters={"supplier": doc.name}, pluck="name")),
        "cfdis": len(frappe.get_all("Lebaref CFDI", filters={"proveedor": doc.name}, pluck="name")) if frappe.db.exists("DocType", "Lebaref CFDI") else 0,
    }
    total_movs = sum(conteos.values())
    otros_proveedores = [
        p for p in frappe.get_list("Supplier", filters={"disabled": 0}, pluck="name", order_by="name asc", limit_page_length=1000)
        if p != doc.name
    ]
    return {
        "proveedor": doc.name,
        "disabled": int(doc.disabled or 0),
        "conteos": conteos,
        "tiene_historial": total_movs > 0,
        "total_movimientos": total_movs,
        "otros_proveedores": otros_proveedores,
    }


@frappe.whitelist(methods=["POST"])
def gestionar_baja_proveedor(proveedor: str, modo: str, proveedor_destino: str = ""):
    """Ejecuta borrado definitivo (si no tiene compras), fusión de duplicado u ocultamiento/reactivación."""
    doc = frappe.get_doc("Supplier", proveedor)
    modo = (modo or "").strip().lower()

    if modo in ("ocultar", "reactivar"):
        doc.check_permission("write")
        nuevo_dis = 1 if modo == "ocultar" else 0
        doc.db_set("disabled", nuevo_dis)
        return {"ok": True, "modo": modo, "disabled": nuevo_dis}

    if modo == "fusionar":
        doc.check_permission("delete")
        destino = (proveedor_destino or "").strip()
        if not destino or destino == doc.name or not frappe.db.exists("Supplier", destino):
            frappe.throw("Selecciona un proveedor existente válido hacia el cual transferir el historial.")
        frappe.rename_doc("Supplier", doc.name, destino, merge=True)
        return {"ok": True, "modo": "fusionar", "destino": destino}

    if modo == "eliminar":
        doc.check_permission("delete")
        info = info_baja_proveedor(doc.name)
        if info["tiene_historial"]:
            frappe.throw("Este proveedor ya tiene cotizaciones, órdenes o CFDIs registrados. Usa 'Fusionar duplicado' u 'Ocultar / Deshabilitar'.")

        # Limpiar vínculos auxiliares (Contact, Address, Item Supplier, Request for Quotation Supplier en borrador)
        if doc.get("supplier_primary_contact"):
            doc.db_set("supplier_primary_contact", None, update_modified=False)
        if doc.get("supplier_primary_address"):
            doc.db_set("supplier_primary_address", None, update_modified=False)

        frappe.db.sql("DELETE FROM `tabItem Supplier` WHERE supplier = %s", (doc.name,))

        for dl in frappe.get_all(
            "Dynamic Link",
            filters={"link_doctype": "Supplier", "link_name": doc.name},
            fields=["name", "parenttype", "parent"],
        ):
            if dl.parenttype in ("Contact", "Address"):
                otros_links = frappe.get_all(
                    "Dynamic Link",
                    filters={"parenttype": dl.parenttype, "parent": dl.parent, "name": ["!=", dl.name]},
                    pluck="name",
                )
                if not otros_links:
                    frappe.delete_doc(dl.parenttype, dl.parent, ignore_permissions=True, force=True)
                else:
                    frappe.db.delete("Dynamic Link", {"name": dl.name})

        frappe.delete_doc("Supplier", doc.name, force=False)
        return {"ok": True, "modo": "eliminar"}

    frappe.throw("Selecciona una acción válida para el proveedor.")


@frappe.whitelist(methods=["POST"])
def crear_cliente_rapido(datos: str):
    """Registra un Cliente con RFC, contacto principal y dirección, listo para vincularse a Obras."""
    frappe.has_permission("Customer", "create", throw=True)
    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(d, dict):
        frappe.throw("Datos inválidos.")

    nombre = (d.get("customer_name") or "").strip()
    if not nombre:
        frappe.throw("Indica el nombre o razón social del cliente.")

    grupo = (d.get("customer_group") or "").strip() or (
        frappe.db.get_value("Customer Group", {"is_group": 0}, "name") or "All Customer Groups"
    )
    territorio = frappe.db.get_value("Territory", {"territory_name": "México"}, "name") or (
        frappe.db.get_value("Territory", {"is_group": 0}, "name") or "All Territories"
    )
    rfc = (d.get("tax_id") or "").strip().upper()
    tipo = (d.get("customer_type") or "Company").strip()

    cli = frappe.get_doc({
        "doctype": "Customer",
        "customer_name": nombre,
        "customer_type": tipo if tipo in ("Company", "Individual") else "Company",
        "customer_group": grupo,
        "territory": territorio,
        "tax_id": rfc or None,
    }).insert()

    contacto_nom = (d.get("contacto") or "").strip()
    telefono = (d.get("telefono") or "").strip()
    correo = (d.get("correo") or "").strip()
    if contacto_nom or telefono or correo:
        ct = frappe.get_doc({
            "doctype": "Contact",
            "first_name": contacto_nom or nombre[:60],
            "is_primary_contact": 1,
            "links": [{"link_doctype": "Customer", "link_name": cli.name}],
        })
        if correo:
            ct.append("email_ids", {"email_id": correo, "is_primary": 1})
        if telefono:
            ct.append("phone_nos", {"phone": telefono, "is_primary_mobile_no": 1, "is_primary_phone": 1})
        ct.insert(ignore_permissions=True)
        cli.db_set("customer_primary_contact", ct.name, update_modified=False)
        if telefono:
            cli.db_set("mobile_no", telefono, update_modified=False)
        if correo:
            cli.db_set("email_id", correo, update_modified=False)

    direccion = (d.get("direccion") or "").strip()
    ciudad = (d.get("ciudad") or "Mérida").strip()
    if direccion:
        addr = frappe.get_doc({
            "doctype": "Address",
            "address_title": nombre[:80],
            "address_type": "Billing",
            "address_line1": direccion[:140],
            "city": ciudad,
            "state": "Yucatán",
            "country": frappe.db.get_value("Country", "Mexico", "name") or "Mexico",
            "links": [{"link_doctype": "Customer", "link_name": cli.name}],
        }).insert(ignore_permissions=True)
        cli.db_set("customer_primary_address", addr.name, update_modified=False)

    return {"folio": cli.name, "tipo": "Customer"}


@frappe.whitelist(methods=["POST"])
def asignar_cliente_obra(obra: str, cliente: str = ""):
    """Asigna o actualiza el Cliente vinculado a una Obra / Proyecto."""
    prj = frappe.get_doc("Project", obra)
    prj.check_permission("write")
    cli = (cliente or "").strip()
    if cli and not frappe.db.exists("Customer", cli):
        frappe.throw("Selecciona un cliente existente.")
    prj.db_set("customer", cli or None)
    return {"ok": True, "obra": prj.name, "cliente": cli}


@frappe.whitelist(methods=["POST"])
def actualizar_ficha_producto(item_code: str, datos: str):
    """Guarda los cambios de la pestaña 'Editar' en el panel derecho del Catálogo sin abandonar la lista."""
    doc = frappe.get_doc("Item", item_code)
    doc.check_permission("write")
    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(d, dict):
        frappe.throw("Datos de edición inválidos.")

    nombre = (d.get("nombre") or "").strip()
    if nombre:
        doc.item_name = nombre[:140]

    cat = (d.get("categoria") or "").strip()
    if cat and frappe.db.exists("Item Group", cat):
        doc.item_group = cat

    marca = (d.get("marca") or "").strip()[:140]
    if marca and marca != "—":
        if not frappe.db.exists("Brand", marca):
            frappe.get_doc({"doctype": "Brand", "brand": marca}).insert(ignore_permissions=True)
        doc.brand = marca
    elif marca == "":
        doc.brand = None

    mapa_campos = {
        "codigo_barras": "custom_lebaref_codigo_barras",
        "ubicacion": "custom_lebaref_ubicacion",
        "modelo": "custom_lebaref_modelo",
        "medidas": "custom_lebaref_medidas",
        "capacidad": "custom_lebaref_capacidad",
        "espec1": "custom_lebaref_especificacion_1",
        "espec2": "custom_lebaref_especificacion_2",
    }
    for k_in, fn_doc in mapa_campos.items():
        if k_in in d:
            val = (d.get(k_in) or "").strip()
            doc.set(fn_doc, "" if val == "—" else val[:140])

    if "stock_minimo" in d:
        doc.custom_lebaref_stock_minimo = max(0.0, flt(d.get("stock_minimo") or 0.0))

    if "ultimo_costo" in d:
        costo = max(0.0, flt(d.get("ultimo_costo") or 0.0))
        doc.custom_lebaref_ultimo_costo = costo
        if costo > 0:
            pl = frappe.db.get_single_value("Buying Settings", "buying_price_list") or "Standard Buying"
            ip_name = frappe.db.get_value("Item Price", {"item_code": doc.name, "buying": 1, "price_list": pl}, "name")
            if ip_name:
                frappe.db.set_value("Item Price", ip_name, "price_list_rate", costo, update_modified=False)

    doc.save()
    return ficha_producto(doc.name)


@frappe.whitelist(methods=["POST"])
def subir_imagen_catalogo(item_code: str, filename: str, content_base64: str):
    """Guarda una imagen pegada (Ctrl+V) o seleccionada desde el panel lateral del Catálogo."""
    import base64
    from frappe.utils.file_manager import save_file

    doc = frappe.get_doc("Item", item_code)
    doc.check_permission("write")

    b64 = (content_base64 or "").strip()
    if "," in b64:
        b64 = b64.split(",", 1)[1]
    raw = base64.b64decode(b64)
    if len(raw) > 8_000_000:
        frappe.throw("La imagen supera el límite de 8 MB.")

    safe_name = (filename or f"{doc.name}.png").replace("/", "_").replace("\\", "_")
    fdoc = save_file(safe_name, raw, "Item", doc.name, is_private=0)
    doc.db_set("image", fdoc.file_url)
    return {"ok": True, "image": fdoc.file_url}


@frappe.whitelist(methods=["GET"])
def opciones_agregar_a_solicitud():
    """Devuelve solicitudes abiertas y catálogos para agregar un artículo directamente desde el panel del Catálogo."""
    borradores = frappe.get_list(
        "Material Request",
        filters={"docstatus": 0, "material_request_type": "Purchase"},
        fields=["name", "custom_lebaref_nombre_solicitud", "title"],
        order_by="modified desc",
        limit_page_length=30,
    )
    confirmadas = frappe.get_list(
        "Material Request",
        filters={"docstatus": 1, "material_request_type": "Purchase", "status": ["not in", ["Stopped", "Cancelled"]]},
        fields=["name", "custom_lebaref_nombre_solicitud", "title"],
        order_by="modified desc",
        limit_page_length=30,
    )
    obras = frappe.get_list("Project", pluck="name", order_by="name asc", limit_page_length=200)
    deptos = frappe.get_list("Department", filters={"disabled": 0, "is_group": 0}, pluck="name", order_by="name asc", limit_page_length=100)
    return {
        "borradores": [
            {"id": b.name, "etiqueta": f"{b.name} · {(b.custom_lebaref_nombre_solicitud or b.title or 'Borrador')[:65]}"}
            for b in borradores
        ],
        "confirmadas": [
            {"id": c.name, "etiqueta": f"{c.name} · {(c.custom_lebaref_nombre_solicitud or c.title or '')[:65]}"}
            for c in confirmadas
        ],
        "obras": obras,
        "departamentos": deptos,
    }


@frappe.whitelist(methods=["POST"])
def agregar_item_a_solicitud(
    modo: str,
    item_code: str,
    cantidad: float,
    solicitud: str = "",
    obra: str = "",
    departamento: str = "",
    solicitante: str = "",
    nombre_solicitud: str = "",
):
    """Agrega un SKU del catálogo a un borrador existente, como anexo de solicitud activa o creando una nueva."""
    from lebaref_compras import operaciones

    qty = flt(cantidad or 0)
    if qty <= 0:
        frappe.throw("Indica una cantidad mayor que cero.")

    item = frappe.get_doc("Item", item_code)
    item.check_permission("read")
    company = frappe.get_all("Company", pluck="name")[0]
    almacen = frappe.db.get_value("Warehouse", {"company": company, "is_group": 0, "disabled": 0}, "name") or ""

    if modo == "borrador" and solicitud:
        mr = frappe.get_doc("Material Request", solicitud)
        mr.check_permission("write")
        if mr.docstatus != 0:
            frappe.throw("Selecciona una solicitud en borrador.")
        mr.append("items", {
            "item_code": item.name,
            "qty": qty,
            "uom": item.purchase_uom or item.stock_uom,
            "stock_uom": item.stock_uom,
            "conversion_factor": 1.0,
            "schedule_date": mr.schedule_date or today(),
            "warehouse": almacen,
            "project": obra or None,
        })
        mr.save()
        return {"folio": mr.name, "tipo": "Material Request"}

    if modo == "anexo" and solicitud:
        res = operaciones.ejecutar(
            "anexo",
            json.dumps({
                "title": f"Anexo {solicitud} ({item.item_name[:40]})",
                "schedule_date": today(),
                "rows": [{"item_code": item.name, "qty": qty, "warehouse": almacen, "project": obra}],
            }, ensure_ascii=False),
            str(uuid.uuid4()),
            solicitud,
        )
        return res

    # Nueva solicitud confirmada
    nom = (nombre_solicitud or "").strip() or f"Solicitud {item.item_name[:60]}"
    payload = {
        "company": company,
        "schedule_date": today(),
        "title": nom[:140],
        "departamento": (departamento or "").strip(),
        "solicitante": (solicitante or "").strip(),
        "rows": [{"item_code": item.name, "qty": qty, "warehouse": almacen, "project": (obra or "").strip()}],
    }
    res_new = operaciones.ejecutar(
        "solicitud",
        json.dumps(payload, ensure_ascii=False),
        str(uuid.uuid4()),
    )
    frappe.db.set_value("Material Request", res_new["folio"], "custom_lebaref_nombre_solicitud", nom[:140], update_modified=False)
    return res_new
