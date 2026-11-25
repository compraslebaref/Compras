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
    "Purchase Invoice Item",
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

    if doc.doctype == "Material Request":
        doc.custom_lebaref_estado_vista = calcular_estado_vista_solicitud(doc)


def calcular_estado_vista_solicitud(doc_or_dict) -> str:
    """Traduce docstatus + status + en_espera_de + concluida al estado real filtrable en español."""
    docstatus = int(doc_or_dict.get("docstatus") or 0)
    status = (doc_or_dict.get("status") or "").strip()
    concluida = int(doc_or_dict.get("custom_lebaref_concluida") or 0)
    en_espera = (doc_or_dict.get("custom_lebaref_en_espera_de") or "").strip()

    if docstatus == 2 or status == "Cancelled":
        return "Cancelada"
    if docstatus == 0 or status == "Draft":
        return "Borrador"
    if concluida == 1 or status == "Stopped":
        return "Concluida"
    if en_espera:
        return "En espera"
    if status == "Partially Ordered":
        return "Parcialmente pedida"
    if status == "Ordered":
        return "Pedida"
    if status == "Partially Received":
        return "Parcialmente recibida"
    if status in ("Received", "Issued", "Transferred"):
        return "Recibida"
    return "Pendiente"


def recalcular_estados_vista_solicitudes(folios=None):
    """Sincroniza en BD la columna custom_lebaref_estado_vista para una lista de solicitudes o para todas."""
    if not frappe.db.has_column("Material Request", "custom_lebaref_estado_vista"):
        return
    filtros = {"name": ["in", list(folios)]} if folios else {}
    rows = frappe.get_all(
        "Material Request",
        filters=filtros,
        fields=["name", "docstatus", "status", "custom_lebaref_concluida", "custom_lebaref_en_espera_de", "custom_lebaref_estado_vista"],
        limit_page_length=0,
    )
    for r in rows:
        nuevo = calcular_estado_vista_solicitud(r)
        if (r.get("custom_lebaref_estado_vista") or "") != nuevo:
            frappe.db.set_value("Material Request", r.name, "custom_lebaref_estado_vista", nuevo, update_modified=False)


def sincronizar_estado_solicitud_hook(doc, method=None):
    recalcular_estados_vista_solicitudes([doc.name])
    from lebaref_compras.seguimiento_google import encolar_sincronizacion
    encolar_sincronizacion()


def sincronizar_estado_solicitudes_vinculadas(doc, method=None):
    mrs = {
        r.get("material_request")
        for r in (getattr(doc, "items", None) or [])
        if r.get("material_request")
    }
    if mrs:
        recalcular_estados_vista_solicitudes(mrs)
    from lebaref_compras.seguimiento_google import encolar_sincronizacion
    encolar_sincronizacion()


def _asegurar_permiso_rol(doctype, role, perm_dict):
    from frappe.permissions import setup_custom_perms

    if not frappe.db.exists("DocType", doctype) or not frappe.db.exists("Role", role):
        return
    setup_custom_perms(doctype)
    existente = frappe.db.get_value(
        "Custom DocPerm",
        {"parent": doctype, "role": role, "permlevel": 0, "if_owner": 0},
        "name",
    )
    if existente:
        update_custom_docperm(existente, perm_dict)
    else:
        frappe.get_doc({
            "doctype": "Custom DocPerm",
            "parent": doctype,
            "parenttype": "DocType",
            "parentfield": "permissions",
            "role": role,
            "permlevel": 0,
            "if_owner": 0,
            **perm_dict,
        }).insert(ignore_permissions=True)


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
            {
                "fieldname": "custom_lebaref_estado_vista",
                "label": "Estado",
                "fieldtype": "Select",
                "options": "\nPendiente\nEn espera\nBorrador\nParcialmente pedida\nPedida\nParcialmente recibida\nRecibida\nConcluida\nCancelada",
                "insert_after": "custom_lebaref_nombre_solicitud",
                "read_only": 1,
                "allow_on_submit": 1,
                "no_copy": 1,
                "in_standard_filter": 1,
                "print_hide": 1,
            },
            {
                "fieldname": "custom_lebaref_en_espera_de",
                "label": "En espera de",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_nombre_solicitud",
                "read_only": 1,
                "allow_on_submit": 1,
                "no_copy": 1,
            },
            {
                "fieldname": "custom_lebaref_en_espera_desde",
                "label": "En espera desde",
                "fieldtype": "Datetime",
                "insert_after": "custom_lebaref_en_espera_de",
                "read_only": 1,
                "allow_on_submit": 1,
                "no_copy": 1,
            },
            {
                "fieldname": "custom_lebaref_concluida",
                "label": "Concluida",
                "fieldtype": "Check",
                "insert_after": "custom_lebaref_en_espera_desde",
                "read_only": 1,
                "allow_on_submit": 1,
                "no_copy": 1,
            },
            {
                "fieldname": "custom_lebaref_concluida_fecha",
                "label": "Concluida el",
                "fieldtype": "Datetime",
                "insert_after": "custom_lebaref_concluida",
                "read_only": 1,
                "allow_on_submit": 1,
                "no_copy": 1,
            },
        ],
        "Supplier": [
            {
                "fieldname": "custom_especialidad_cotizar",
                "label": "Especialidad / Qué cotizarle",
                "fieldtype": "Data",
                "insert_after": "supplier_group",
                "in_list_view": 1,
                "in_standard_filter": 1,
            },
            {
                "fieldname": "custom_tab_quien_cotizar",
                "label": "¿A quién cotizar? (Directorio)",
                "fieldtype": "Tab Break",
                "insert_after": ultimo_campo_sup,
            },
            {
                "fieldname": "custom_quien_cotizar_html",
                "label": "Directorio ¿A quién cotizar?",
                "fieldtype": "HTML",
                "insert_after": "custom_tab_quien_cotizar",
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
        "Purchase Invoice",
        "Payment Entry",
        "Stock Entry",
        "Stock Reconciliation",
        "Item",
        "Supplier",
        "Customer",
        "Project",
        "Department",
        "Lebaref CFDI",
        "Lebaref Conciliacion CFDI",
        "Lebaref Herramienta",
        "Lebaref Kit",
        "Lebaref Prestamo",
        "Lebaref Vehiculo",
        "Lebaref Equivalencia Articulo",
        "Lebaref Cotizacion Historica",
    )
    for dt in doctypes_id_primero:
        _ps(dt, None, "title_field", "", "Data")
        _ps(dt, None, "show_title_field_in_link", 0, "Check")

    # Buscadores amigables al desplegar Links aunque title_field esté vacío
    _ps("Item", None, "search_fields", "custom_lebaref_codigo_barras,item_name,item_group", "Data")
    _ps("Project", None, "search_fields", "project_name,customer", "Data")
    _ps("Customer", None, "search_fields", "customer_name,tax_id", "Data")
    _ps("Supplier", None, "search_fields", "supplier_name,tax_id,custom_especialidad_cotizar", "Data")
    _ps("Supplier", "tax_id", "label", "RFC", "Data")

    # Ubicar RFC, CSF, Carátula Bancaria, CLABE y Especialidad en la primera pestaña (Detalles) de Supplier
    if frappe.db.exists("DocType", "Supplier"):
        frappe.clear_cache(doctype="Supplier")
        meta_s = frappe.get_meta("Supplier")
        reubicar_sup = [
            "custom_especialidad_cotizar",
            "custom_sec_expediente_pago",
            "tax_id",
            "custom_lebaref_csf_archivo",
            "custom_lebaref_csf_fecha",
            "custom_cb_expediente_pago",
            "custom_lebaref_caratula_archivo",
            "custom_lebaref_clabe",
        ]
        presentes_sup = {f.fieldname for f in meta_s.fields if f.fieldname}
        base_order = [f.fieldname for f in meta_s.fields if f.fieldname and f.fieldname not in reubicar_sup]
        nuevo_orden_sup = []
        for fn_s in base_order:
            nuevo_orden_sup.append(fn_s)
            if fn_s == "supplier_group" and "custom_especialidad_cotizar" in presentes_sup:
                nuevo_orden_sup.append("custom_especialidad_cotizar")
            elif fn_s == "payment_terms":
                for fn_exp in reubicar_sup[1:]:
                    if fn_exp in presentes_sup:
                        nuevo_orden_sup.append(fn_exp)
        _ps("Supplier", None, "field_order", json.dumps(nuevo_orden_sup), "Text")
    _ps("Material Request", "status", "in_standard_filter", 0, "Check")
    _ps("Material Request", "company", "in_standard_filter", 0, "Check")
    _ps("Material Request", "material_request_type", "in_standard_filter", 0, "Check")
    _ps("Material Request", "custom_lebaref_proposito", "in_standard_filter", 0, "Check")
    _ps("Material Request", "custom_lebaref_estado_vista", "in_standard_filter", 1, "Check")
    _ps("Material Request", "custom_lebaref_nombre_solicitud", "in_standard_filter", 1, "Check")
    _ps("Material Request", "custom_lebaref_departamento", "in_standard_filter", 1, "Check")
    _ps("Material Request", "custom_lebaref_solicitante", "in_standard_filter", 1, "Check")
    recalcular_estados_vista_solicitudes()
    try:
        frappe.db.sql("DELETE FROM `__UserSettings` WHERE doctype = 'Material Request'")
    except Exception:
        pass

    # Folio oficial de solicitudes: SOL-AAAA-#### (igual que el ERP original y la hoja/enlace de Google)
    _ps("Material Request", "naming_series", "options", "SOL-.YYYY.-.####", "Text")
    _ps("Material Request", "naming_series", "default", "SOL-.YYYY.-.####", "Text")

    # 2. Distribución limpia de columnas en Listas (evitando truncamiento y ocultando 'company' redundante)
    config_listas = {
        "Material Request": {
            "mostrar": ["custom_lebaref_nombre_solicitud", "custom_lebaref_solicitante", "schedule_date"],
            "ocultar": ["title", "material_request_type", "company", "transaction_date", "per_ordered", "per_received", "custom_lebaref_departamento"],
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

    # 3. Columnas claras en tablas hijas (sin usar columna horizontal para Partida; se agrupa en filas de bloque EST-01)
    _ps("Material Request Item", "item_code", "in_list_view", 0, "Check")
    _ps("Material Request Item", "item_name", "in_list_view", 0, "Check")
    _ps("Material Request Item", "description", "in_list_view", 0, "Check")
    _ps("Material Request Item", "schedule_date", "in_list_view", 0, "Check")
    _ps("Material Request Item", "warehouse", "in_list_view", 0, "Check")
    _ps("Material Request Item", "custom_lebaref_partida", "in_list_view", 0, "Check")
    _ps("Material Request Item", "custom_lebaref_no_recotizar", "in_list_view", 0, "Check")
    _ps("Material Request Item", "custom_lebaref_clave_vista", "in_list_view", 1, "Check")
    _ps("Material Request Item", "custom_lebaref_clave_vista", "columns", 2, "Int")
    _ps("Material Request Item", "custom_lebaref_descripcion_vista", "in_list_view", 1, "Check")
    _ps("Material Request Item", "custom_lebaref_descripcion_vista", "columns", 6, "Int")
    _ps("Material Request Item", "qty", "in_list_view", 1, "Check")
    _ps("Material Request Item", "qty", "columns", 1, "Int")
    _ps("Material Request Item", "uom", "in_list_view", 1, "Check")
    _ps("Material Request Item", "uom", "columns", 1, "Int")

    for dt_po in ("Purchase Order Item", "Supplier Quotation Item", "Purchase Receipt Item", "Purchase Invoice Item", "Stock Entry Detail"):
        _ps(dt_po, "custom_lebaref_partida", "in_list_view", 0, "Check")

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

    # Todos los perfiles operativos pueden dar de alta y modificar Proveedores, sus Contactos y Direcciones
    roles_proveedores_equipo = (
        "Purchase User",
        "Stock Manager",
        "Stock User",
        "Item Manager",
        "Accounts User",
        "Accounts Manager",
    )
    for rol_sup in roles_proveedores_equipo:
        _asegurar_permiso_rol("Supplier", rol_sup, {"read": 1, "write": 1, "create": 1, "select": 1})
        _asegurar_permiso_rol("Contact", rol_sup, {"read": 1, "write": 1, "create": 1, "select": 1})
        _asegurar_permiso_rol("Address", rol_sup, {"read": 1, "write": 1, "create": 1, "select": 1})
    for rol_ref in ("Purchase Manager", *roles_proveedores_equipo):
        _asegurar_permiso_rol("Supplier Group", rol_ref, {"read": 1, "select": 1})
        _asegurar_permiso_rol("Country", rol_ref, {"read": 1, "select": 1})

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

    _configurar_unidades_espanol()
    _ocultar_campos_tecnicos_internos()


def _ocultar_campos_tecnicos_internos():
    """Oculta en todos los formularios, reportes e impresiones los campos técnicos de operación y huella (UX-01)."""
    campos_ocultos = (
        "custom_lebaref_operacion",
        "custom_lebaref_huella",
        "custom_lebaref_regularizacion_operacion",
        "custom_lebaref_regularizacion_huella",
        "custom_lebaref_clave_sobrante",
        "custom_lebaref_clave_ajuste",
        "custom_lebaref_clave_comprobante",
        "custom_lebaref_renglon_origen",
    )
    frappe.db.sql(
        """
        UPDATE `tabCustom Field`
        SET hidden = 1, print_hide = 1, report_hide = 1
        WHERE fieldname IN %s
        """,
        (campos_ocultos,),
    )
    # También en DocTypes propios de Lebaref (Herramientas, Kits, Préstamos, Flotilla, CFDI)
    frappe.db.sql(
        """
        UPDATE `tabDocField`
        SET hidden = 1, print_hide = 1, report_hide = 1
        WHERE parent LIKE 'Lebaref%%'
          AND fieldname IN ('operacion', 'huella', 'clave_referencia', 'custom_lebaref_operacion', 'custom_lebaref_huella')
        """
    )


UNIDADES_ESPANOL = (
    "Pieza",
    "Metro",
    "Paquete",
    "Par",
    "Caja",
    "Galón",
    "Litro",
    "Servicio",
    "Rollo",
    "Kilogramo",
    "Juego",
    "Cubeta",
    "Cartucho",
    "Bolsa",
    "Barra",
    "Bulto",
    "Tramo",
    "Lote",
    "m2",
    "m3",
    "Gramo",
    "Mililitro",
    "Tonelada",
    "Hora",
    "Día",
    "Jornada",
)

RENOMBRES_UOM_INGLES = (
    ("Nos", "Pieza"),
    ("Unit", "Pieza"),
    ("Meter", "Metro"),
    ("Pair", "Par"),
    ("Box", "Caja"),
    ("Litre", "Litro"),
    ("Liter", "Litro"),
    ("Kg", "Kilogramo"),
    ("Kilogram", "Kilogramo"),
    ("Set", "Juego"),
    ("Pack", "Paquete"),
    ("Roll", "Rollo"),
    ("Gallon", "Galón"),
    ("Bag", "Bolsa"),
    ("Service", "Servicio"),
    ("Hour", "Hora"),
    ("Day", "Día"),
)


def _configurar_unidades_espanol():
    """Traduce en cascada las UdM en inglés a español, habilita el catálogo operativo y oculta las irrelevantes."""
    from lebaref_compras.solicitudes import ARTICULO_LIBRE

    if not frappe.db.exists("DocType", "UOM"):
        return

    # 1. Renombrar / fusionar en cascada las UdM en inglés hacia su equivalente en español
    for eng, esp in RENOMBRES_UOM_INGLES:
        if frappe.db.exists("UOM", eng) and eng != esp:
            ya_existe_esp = bool(frappe.db.exists("UOM", esp))
            frappe.rename_doc("UOM", eng, esp, force=True, merge=ya_existe_esp, show_alert=False)

    # 2. Asegurar que todas las unidades operativas en español existan, estén activas y permitan decimales
    for u_esp in UNIDADES_ESPANOL:
        if not frappe.db.exists("UOM", u_esp):
            frappe.get_doc({
                "doctype": "UOM",
                "uom_name": u_esp,
                "enabled": 1,
                "must_be_whole_number": 0,
            }).insert(ignore_permissions=True)
        else:
            frappe.db.set_value("UOM", u_esp, {"enabled": 1, "must_be_whole_number": 0}, update_modified=False)

    # 3. Desactivar unidades preinstaladas en inglés que no estén en uso real
    en_uso = set(frappe.db.sql_list("SELECT DISTINCT stock_uom FROM `tabItem` WHERE stock_uom IS NOT NULL"))
    en_uso.update(frappe.db.sql_list("SELECT DISTINCT purchase_uom FROM `tabItem` WHERE purchase_uom IS NOT NULL AND purchase_uom != ''"))
    en_uso.update(frappe.db.sql_list("SELECT DISTINCT uom FROM `tabUOM Conversion Detail` WHERE uom IS NOT NULL"))
    permitidas = set(UNIDADES_ESPANOL) | en_uso
    for u_row in frappe.get_all("UOM", filters={"enabled": 1}, pluck="name"):
        if u_row not in permitidas:
            frappe.db.set_value("UOM", u_row, "enabled", 0, update_modified=False)

    # 4. Ajustar unidad base de Stock Settings y conversiones 1:1 en LEBAREF-CONCEPTO-LIBRE
    if frappe.db.exists("DocType", "Stock Settings") and frappe.db.exists("UOM", "Pieza"):
        frappe.db.set_single_value("Stock Settings", "stock_uom", "Pieza")

    if frappe.db.exists("Item", ARTICULO_LIBRE):
        it_lib = frappe.get_doc("Item", ARTICULO_LIBRE)
        existentes_uoms = {u.uom for u in (it_lib.uoms or [])}
        cambiado = False
        if it_lib.stock_uom != "Pieza":
            it_lib.stock_uom = "Pieza"
            cambiado = True
        for u_esp in UNIDADES_ESPANOL:
            if u_esp not in existentes_uoms:
                it_lib.append("uoms", {"uom": u_esp, "conversion_factor": 1.0})
                cambiado = True
        if cambiado:
            it_lib.save(ignore_permissions=True)


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

    from lebaref_compras import catalogo_gobierno, motor_precios
    fam_actual = (doc.get("custom_lebaref_familia") or "").strip() or catalogo_gobierno.normalizar_nombre_familia("", doc.item_name or "", doc.item_group or "")
    ger_lista = catalogo_gobierno.parsear_lista_gerencias(doc.get("custom_lebaref_gerencias") or "")
    if not ger_lista:
        ger_lista = catalogo_gobierno.parsear_lista_gerencias(
            catalogo_gobierno.inferir_gerencias_articulo(doc.item_name or "", doc.item_group or "", int(doc.get("custom_lebaref_consumo_directo") or 0))
        )
    familias_disp = sorted({
        f for f in frappe.get_all("Item", filters={"custom_lebaref_familia": ["is", "set"]}, pluck="custom_lebaref_familia", limit_page_length=500)
        if f
    }) if frappe.db.has_column("Item", "custom_lebaref_familia") else []
    est_oscar = motor_precios.obtener_estimacion_privada_articulo(doc.name)

    return {
        "codigo_interno": doc.name,
        "codigo_barras": cb_render,
        "nombre": doc.item_name or doc.name,
        "categoria": doc.item_group or "—",
        "familia": fam_actual,
        "familias_disponibles": familias_disp,
        "gerencias": ", ".join(ger_lista),
        "gerencias_lista": ger_lista,
        "gerencias_disponibles": list(catalogo_gobierno.GERENCIAS_OFICIALES),
        "recotizar_siempre": int(doc.get("custom_lebaref_recotizar_siempre") or 0),
        "consumo_directo": int(doc.get("custom_lebaref_consumo_directo") or 0),
        "estimacion_oscar": est_oscar,
        "plantilla_sugerida": catalogo_gobierno.obtener_plantilla_tecnica_sugerida(doc.item_group or ""),
        "plantillas_por_categoria": catalogo_gobierno.PLANTILLAS_TECNICAS_POR_CATEGORIA,
        "alias_equivalencias": catalogo_gobierno.obtener_alias_de_articulo(doc.name),
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
        "uom_almacen": doc.stock_uom or "Pieza",
        "uom_compra": uom_compra or doc.stock_uom or "Pieza",
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


@frappe.whitelist(methods=["POST"])
def confirmar_solicitud_borrador(solicitud: str):
    """Confirma una solicitud en borrador directamente desde la vista dividida de Solicitudes."""
    doc = frappe.get_doc("Material Request", solicitud)
    doc.check_permission("submit")
    if doc.docstatus != 0:
        frappe.throw("Esta solicitud ya está confirmada o cancelada.")
    if len(doc.items) == 1 and doc.items[0].item_code == "LEBAREF-CONCEPTO-LIBRE" and _CONCEPTO_PLACEHOLDER_SOL in (doc.items[0].get("custom_lebaref_concepto") or ""):
        frappe.throw("Agrega o importa al menos un artículo en la solicitud antes de confirmarla.")
    doc.submit()
    return {"ok": True, "folio": doc.name}


@frappe.whitelist(methods=["POST"])
def cambiar_estado_manual(solicitud: str, accion: str, detalle: str = ""):
    """Marca una solicitud como 'En espera' (con quién), 'Concluida' o la reanuda, con bitácora de quién y cuándo."""
    doc = frappe.get_doc("Material Request", solicitud)
    doc.check_permission("write")
    if doc.docstatus == 2:
        frappe.throw("La solicitud está cancelada y no admite cambios de estado.")
    accion = (accion or "").strip().lower()
    detalle = (detalle or "").strip()[:140]
    ahora = frappe.utils.now_datetime()
    usuario = frappe.utils.get_fullname(frappe.session.user) or frappe.session.user

    if accion == "espera":
        if not detalle:
            frappe.throw("Indica de quién o de qué se está esperando respuesta (ej. Gerente / Proveedor).")
        doc.db_set({
            "custom_lebaref_en_espera_de": detalle,
            "custom_lebaref_en_espera_desde": ahora,
            "custom_lebaref_concluida": 0,
            "custom_lebaref_concluida_fecha": None,
        }, update_modified=True)
        doc.add_comment("Info", f"⏸ En espera de: {detalle} (marcado por {usuario})")
    elif accion == "concluir":
        doc.db_set({
            "custom_lebaref_concluida": 1,
            "custom_lebaref_concluida_fecha": ahora,
            "custom_lebaref_en_espera_de": "",
            "custom_lebaref_en_espera_desde": None,
        }, update_modified=True)
        doc.add_comment("Info", f"✓ Marcada como concluida por {usuario}" + (f": {detalle}" if detalle else ""))
    elif accion in ("reanudar", "reabrir"):
        doc.db_set({
            "custom_lebaref_en_espera_de": "",
            "custom_lebaref_en_espera_desde": None,
            "custom_lebaref_concluida": 0,
            "custom_lebaref_concluida_fecha": None,
        }, update_modified=True)
        doc.add_comment("Info", f"↩ Reanudada por {usuario}")
    else:
        frappe.throw("Acción de estado no válida.")
    recalcular_estados_vista_solicitudes([doc.name])
    try:
        from lebaref_compras.seguimiento_google import encolar_sincronizacion

        encolar_sincronizacion()
    except Exception:
        pass
    return {"ok": True, "solicitud": doc.name, "accion": accion}


@frappe.whitelist(methods=["GET"])
def ficha_solicitud(solicitud: str):
    """Devuelve la Ficha y el Expediente trazable de una Solicitud para el panel lateral en Solicitudes (Lista + Ficha)."""
    doc = frappe.get_doc("Material Request", solicitud)
    doc.check_permission("read")

    codigos = list({r.item_code for r in doc.items if r.item_code and r.item_code != "LEBAREF-CONCEPTO-LIBRE"})
    img_map = {}
    ubi_map = {}
    stock_map = {}

    if codigos:
        campos_it = ["name", "image", "stock_uom"]
        if frappe.db.has_column("Item", "custom_lebaref_ubicacion"):
            campos_it.append("custom_lebaref_ubicacion")
        for it in frappe.get_all("Item", filters={"name": ["in", codigos]}, fields=campos_it):
            img_map[it.name] = it.get("image") or ""
            ubi_map[it.name] = it.get("custom_lebaref_ubicacion") or ""

        if frappe.has_permission("Bin", "read"):
            for b in frappe.db.sql(
                """
                SELECT item_code, COALESCE(SUM(actual_qty), 0) AS qty
                FROM `tabBin`
                WHERE item_code IN %s
                GROUP BY item_code
                """,
                (tuple(codigos),),
                as_dict=True,
            ):
                stock_map[b.item_code] = flt(b.qty)

    obras_set = []
    almacenes_set = []
    partidas = []
    tiene_conceptos_libres = False

    for r in doc.items:
        es_libre = r.item_code == "LEBAREF-CONCEPTO-LIBRE" or bool(r.get("custom_lebaref_concepto"))
        if es_libre and len(doc.items) == 1 and _CONCEPTO_PLACEHOLDER_SOL in (r.get("custom_lebaref_concepto") or ""):
            if r.project and r.project not in obras_set:
                obras_set.append(r.project)
            continue
        if es_libre:
            tiene_conceptos_libres = True
        clave, desc = _calcular_clave_y_descripcion(
            r.item_code or "",
            r.item_name or "",
            r.description or "",
            r.get("custom_lebaref_concepto") or "",
        )
        if r.project and r.project not in obras_set:
            obras_set.append(r.project)
        if r.warehouse and r.warehouse not in almacenes_set:
            almacenes_set.append(r.warehouse)

        partidas.append({
            "name": r.name,
            "idx": r.idx,
            "item_code": r.item_code or "",
            "es_libre": es_libre,
            "clave": clave,
            "descripcion": desc,
            "custom_lebaref_concepto": r.get("custom_lebaref_concepto") or "",
            "description": r.description or "",
            "imagen": "" if es_libre else img_map.get(r.item_code, ""),
            "ubicacion": "" if es_libre else ubi_map.get(r.item_code, ""),
            "qty": flt(r.qty),
            "uom": r.uom or r.stock_uom or "Pieza",
            "ordered_qty": flt(r.ordered_qty or 0),
            "received_qty": flt(r.received_qty or 0),
            "stock_actual": 0.0 if es_libre else stock_map.get(r.item_code, 0.0),
            "uom_almacen": r.stock_uom or r.uom or "Pieza",
            "obra": r.project or "",
            "partida": r.get("custom_lebaref_partida") or "",
        })

    from lebaref_compras import motor_precios
    motor_precios.enriquecer_partidas_solicitud_segun_usuario(doc, partidas)

    # Trazabilidad / Expediente vinculado
    anexos = []
    if frappe.db.has_column("Material Request", "custom_lebaref_solicitud_origen"):
        for ax in frappe.get_list(
            "Material Request",
            filters={"custom_lebaref_solicitud_origen": doc.name, "docstatus": ["<", 2]},
            fields=["name", "custom_lebaref_nombre_solicitud", "title", "status", "transaction_date"],
            order_by="creation asc",
            limit_page_length=30,
        ):
            anexos.append({
                "folio": ax.name,
                "nombre": ax.custom_lebaref_nombre_solicitud or ax.title or ax.name,
                "estado": ax.status or "",
                "fecha": str(ax.transaction_date or ""),
            })

    cotizaciones = []
    if frappe.has_permission("Supplier Quotation", "read"):
        rows_sq = frappe.db.sql(
            """
            SELECT DISTINCT sq.name, sq.supplier, sq.supplier_name, sq.transaction_date,
                            sq.grand_total, sq.net_total, sq.currency, sq.docstatus, sq.status
            FROM `tabSupplier Quotation` sq
            INNER JOIN `tabSupplier Quotation Item` sqi ON sqi.parent = sq.name
            LEFT JOIN `tabRequest for Quotation Item` rfqi ON rfqi.name = sqi.request_for_quotation_item
            WHERE sq.docstatus < 2
              AND (sqi.material_request = %s OR rfqi.material_request = %s)
            ORDER BY sq.creation DESC
            """,
            (doc.name, doc.name),
            as_dict=True,
        )
        for sq in rows_sq:
            cotizaciones.append({
                "folio": sq.name,
                "proveedor": sq.supplier_name or sq.supplier,
                "fecha": str(sq.transaction_date or ""),
                "neto": flt(sq.net_total),
                "total": flt(sq.grand_total),
                "moneda": sq.currency or "MXN",
                "docstatus": int(sq.docstatus or 0),
                "estado": "Borrador" if int(sq.docstatus or 0) == 0 else "Vigente",
            })

    ordenes = []
    po_names = []
    if frappe.has_permission("Purchase Order", "read"):
        rows_po = frappe.db.sql(
            """
            SELECT DISTINCT po.name, po.supplier, po.supplier_name, po.transaction_date,
                            po.grand_total, po.net_total, po.currency, po.docstatus, po.status, po.per_received
            FROM `tabPurchase Order` po
            INNER JOIN `tabPurchase Order Item` poi ON poi.parent = po.name
            LEFT JOIN `tabSupplier Quotation Item` sqi ON sqi.name = poi.supplier_quotation_item
            LEFT JOIN `tabRequest for Quotation Item` rfqi ON rfqi.name = sqi.request_for_quotation_item
            WHERE po.docstatus < 2
              AND (poi.material_request = %s OR rfqi.material_request = %s)
            ORDER BY po.creation DESC
            """,
            (doc.name, doc.name),
            as_dict=True,
        )
        for po in rows_po:
            po_names.append(po.name)
            ordenes.append({
                "folio": po.name,
                "proveedor": po.supplier_name or po.supplier,
                "fecha": str(po.transaction_date or ""),
                "neto": flt(po.net_total),
                "total": flt(po.grand_total),
                "moneda": po.currency or "MXN",
                "docstatus": int(po.docstatus or 0),
                "estado": "Por autorizar (Borrador)" if int(po.docstatus or 0) == 0 else (po.status or "Autorizada"),
                "pct_recibido": round(flt(po.per_received or 0), 1),
            })

    movimientos_almacen = []
    if frappe.has_permission("Purchase Receipt", "read"):
        cond_po = "OR pri.purchase_order IN %s" if po_names else ""
        params_pr = (doc.name, tuple(po_names)) if po_names else (doc.name,)
        rows_pr = frappe.db.sql(
            f"""
            SELECT DISTINCT pr.name, pr.supplier_name, pr.supplier, pr.posting_date, pr.docstatus, pr.status, pr.grand_total
            FROM `tabPurchase Receipt` pr
            INNER JOIN `tabPurchase Receipt Item` pri ON pri.parent = pr.name
            WHERE pr.docstatus < 2
              AND (pri.material_request = %s {cond_po})
            ORDER BY pr.posting_date DESC, pr.creation DESC
            """,
            params_pr,
            as_dict=True,
        )
        for pr in rows_pr:
            movimientos_almacen.append({
                "doctype": "Purchase Receipt",
                "tipo": "Recepción",
                "folio": pr.name,
                "detalle": pr.supplier_name or pr.supplier or "",
                "fecha": str(pr.posting_date or ""),
                "estado": "Borrador" if int(pr.docstatus or 0) == 0 else "Recibida",
            })

    if frappe.has_permission("Stock Entry", "read"):
        rows_se = frappe.db.sql(
            """
            SELECT DISTINCT se.name, se.stock_entry_type, se.posting_date, se.docstatus, se.to_warehouse, se.project
            FROM `tabStock Entry` se
            INNER JOIN `tabStock Entry Detail` sed ON sed.parent = se.name
            WHERE se.docstatus < 2
              AND sed.material_request = %s
            ORDER BY se.posting_date DESC, se.creation DESC
            """,
            (doc.name,),
            as_dict=True,
        )
        for se in rows_se:
            movimientos_almacen.append({
                "doctype": "Stock Entry",
                "tipo": se.stock_entry_type or "Movimiento",
                "folio": se.name,
                "detalle": se.project or se.to_warehouse or "",
                "fecha": str(se.posting_date or ""),
                "estado": "Borrador" if int(se.docstatus or 0) == 0 else "Confirmado",
            })

    # Clasificación de tipo, estatus en español y siguiente paso guiado (estilo ERP original)
    mapa_tipos = {
        "Purchase": "Solicitud de Compra",
        "Material Issue": "Salida de Almacén",
        "Material Transfer": "Surtido / Traslado de Stock",
    }
    tipo_etq = mapa_tipos.get(doc.material_request_type, doc.material_request_type or "Solicitud")

    n_cot_activas = sum(1 for c in cotizaciones if c["docstatus"] == 1)
    n_oc_borrador = sum(1 for o in ordenes if o["docstatus"] == 0)
    n_oc_activas = sum(1 for o in ordenes if o["docstatus"] == 1)
    pendientes_ordenar = sum(1 for p in partidas if (p["ordered_qty"] + 1e-6) < p["qty"])

    if doc.docstatus == 2:
        est_etq, est_color = "Cancelada", "red"
        sig = {
            "tono": "gray",
            "titulo": "SOLICITUD CANCELADA",
            "mensaje": "Este documento fue cancelado y no requiere seguimiento.",
            "accion": "",
            "boton": "",
        }
    elif doc.docstatus == 0:
        est_etq, est_color = "En Captura (Borrador)", "orange"
        if not partidas:
            sig = {
                "tono": "amber",
                "titulo": "SIGUIENTE PASO",
                "mensaje": "Solicitud registrada. Ahora agrega los materiales con Importar Excel o + Partidas aquí abajo.",
                "accion": "",
                "boton": "",
            }
        else:
            sig = {
                "tono": "amber",
                "titulo": "SIGUIENTE PASO",
                "mensaje": "Revisa los materiales solicitados y confirma el borrador para pasar a cotización o surtido.",
                "accion": "confirmar_borrador" if doc.has_permission("submit") else "abrir_form",
                "boton": "Confirmar solicitud" if doc.has_permission("submit") else "Abrir borrador",
            }
    elif doc.status == "Stopped":
        est_etq, est_color = "Cerrada / Detenida", "gray"
        sig = {
            "tono": "green",
            "titulo": "✓ CONCLUIDA",
            "mensaje": "Solicitud marcada como concluida sin remanente activo.",
            "accion": "",
            "boton": "",
        }
    elif doc.material_request_type in ("Material Issue", "Material Transfer"):
        if flt(doc.per_ordered or 0) < 99.9:
            est_etq, est_color = "Por Surtir en Almacén", "blue"
            sig = {
                "tono": "blue",
                "titulo": "➜ SIGUIENTE PASO",
                "mensaje": "Pendiente de entregar o trasladar el material físico desde Almacén.",
                "accion": "surtir",
                "boton": "📦 Surtir desde Almacén",
            }
        else:
            est_etq, est_color = "Surtida", "green"
            sig = {
                "tono": "green",
                "titulo": "✓ SURTIDO COMPLETADO",
                "mensaje": "Todos los materiales de esta solicitud ya fueron entregados desde Almacén.",
                "accion": "",
                "boton": "",
            }
    else:
        # Solicitud de Compra confirmada
        if pendientes_ordenar > 0 and n_cot_activas == 0 and not ordenes:
            est_etq, est_color = "Pendiente de Cotizar", "orange"
            sig = {
                "tono": "blue",
                "titulo": "➜ SIGUIENTE PASO",
                "mensaje": "Captura las cotizaciones de proveedores para comparar precios (o canaliza a stock si ya hay existencia).",
                "accion": "cotizar",
                "boton": "💰 Capturar Cotización",
            }
        elif pendientes_ordenar > 0 and n_cot_activas > 0:
            est_etq, est_color = f"Con Cotizaciones ({n_cot_activas})", "blue"
            sig = {
                "tono": "blue",
                "titulo": "➜ SIGUIENTE PASO",
                "mensaje": f"Hay {n_cot_activas} oferta(s) lista(s). Abre el Cuadro Comparativo para adjudicar y generar la Orden de Compra.",
                "accion": "comparativa",
                "boton": f"⚖️ Abrir Comparativa ({n_cot_activas})",
            }
        elif n_oc_borrador > 0:
            est_etq, est_color = f"OC por Autorizar ({n_oc_borrador})", "orange"
            sig = {
                "tono": "amber",
                "titulo": "➜ SIGUIENTE PASO",
                "mensaje": f"Se generaron {n_oc_borrador} Orden(es) de Compra en borrador pendientes de visto bueno del Coordinador / Dirección.",
                "accion": "ver_expediente",
                "boton": "📄 Ver Órdenes en Expediente",
            }
        elif flt(doc.per_received or 0) < 99.9 and n_oc_activas > 0:
            est_etq, est_color = "En Tránsito / Por Recibir", "blue"
            sig = {
                "tono": "blue",
                "titulo": "➜ EN SEGUIMIENTO DE ENTREGA",
                "mensaje": f"{n_oc_activas} Orden(es) de Compra autorizada(s). Pendiente de recibir la mercancía en Almacén.",
                "accion": "ver_expediente",
                "boton": "🚚 Ver Órdenes y Recepciones",
            }
        else:
            est_etq, est_color = "Atendida / Recibida", "green"
            sig = {
                "tono": "green",
                "titulo": "✓ SOLICITUD COMPLETADA",
                "mensaje": "Todas las partidas solicitadas fueron ordenadas y recibidas en Almacén.",
                "accion": "",
                "boton": "",
            }

    en_espera_de = (doc.get("custom_lebaref_en_espera_de") or "").strip()
    concluida = bool(int(doc.get("custom_lebaref_concluida") or 0))
    if doc.docstatus != 2:
        if concluida:
            est_etq, est_color = "Concluida", "green"
            sig = {
                "tono": "green",
                "titulo": "✓ CONCLUIDA",
                "mensaje": f"Marcada como concluida el {str(doc.get('custom_lebaref_concluida_fecha') or '')[:16]}. No requiere más pasos.",
                "accion": "reanudar",
                "boton": "↩ Reabrir",
            }
        elif en_espera_de:
            est_etq, est_color = f"En espera: {en_espera_de}", "purple"
            sig = {
                "tono": "purple",
                "titulo": "EN ESPERA",
                "mensaje": f"Pausada desde {str(doc.get('custom_lebaref_en_espera_desde') or '')[:16]}: depende de {en_espera_de}. No cuenta como pendiente propio.",
                "accion": "reanudar",
                "boton": "Reanudar solicitud",
            }

    destino_partes = []
    if obras_set:
        destino_partes.append("Obra: " + ", ".join(obras_set[:3]))
    if almacenes_set:
        destino_partes.append(", ".join(almacenes_set[:2]))
    destino_txt = " · ".join(destino_partes) if destino_partes else "Almacén General"

    origen_anexo = (doc.get("custom_lebaref_solicitud_origen") or "").strip()
    origen_txt = f"Anexo de {origen_anexo}" if origen_anexo else "Captura directa en ERP"

    nombre_mostrar = (doc.get("custom_lebaref_nombre_solicitud") or "").strip()
    if not nombre_mostrar:
        t_raw = (doc.title or "").strip()
        nombre_mostrar = t_raw if t_raw and t_raw not in ("Purchase", "Material Transfer", "Material Issue") else "Sin nombre asignado"

    solicitante_txt = (doc.get("custom_lebaref_solicitante") or "").strip()
    if not solicitante_txt:
        solicitante_txt = frappe.utils.get_fullname(doc.owner) or doc.owner

    puede_mod_part, motivo_bloq_part = _evaluar_edicion_partidas_solicitud(doc)

    return {
        "folio": doc.name,
        "nombre": nombre_mostrar,
        "company": doc.company,
        "tipo_raw": doc.material_request_type,
        "tipo_etiqueta": tipo_etq,
        "docstatus": int(doc.docstatus or 0),
        "status_raw": doc.status or "",
        "estatus_etiqueta": est_etq,
        "estatus_color": est_color,
        "fecha": str(doc.transaction_date or ""),
        "fecha_requerida": str(doc.schedule_date or ""),
        "solicitante": solicitante_txt,
        "departamento": doc.get("custom_lebaref_departamento") or "—",
        "destino": destino_txt,
        "origen_anexo": origen_anexo,
        "origen_texto": origen_txt,
        "notas": (doc.get("custom_lebaref_notas") or doc.get("terms") or "").strip(),
        "puede_editar": bool(doc.has_permission("write")),
        "puede_modificar_partidas": puede_mod_part,
        "motivo_bloqueo_partidas": motivo_bloq_part,
        "puede_confirmar": bool(doc.docstatus == 0 and doc.has_permission("submit")),
        "tiene_conceptos_libres": tiene_conceptos_libres,
        "en_espera_de": en_espera_de,
        "concluida": concluida,
        "es_motor_oscar": 1 if motor_precios.es_usuario_motor_precios() else 0,
        "siguiente_paso": sig,
        "partidas": partidas,
        "expediente": {
            "anexos": anexos,
            "cotizaciones": cotizaciones,
            "ordenes": ordenes,
            "almacen": movimientos_almacen,
        },
    }


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
            "uom": r.uom or r.stock_uom or "Pieza",
            "obra": r.project or "",
            "partida": r.custom_lebaref_partida or "",
            "no_recotizar": int(r.get("custom_lebaref_no_recotizar") or 0),
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
    for campo_opc in (
        "taxes_and_charges",
        "shipping_rule",
        "payment_terms_template",
        "fecha_programada_pago",
        "currency",
        "tc_dof",
        "fecha_dof",
        "modalidad_entrega",
        "dias_entrega_promesa",
        "flete_sin_iva",
    ):
        val_opc = payload.get(campo_opc)
        if val_opc is not None and str(val_opc).strip() != "":
            oferta_datos[campo_opc] = str(val_opc).strip() if isinstance(val_opc, str) else val_opc

    if "incluir_flete_en_total" in payload:
        oferta_datos["incluir_flete_en_total"] = payload.get("incluir_flete_en_total")

    res_sq = operaciones.ejecutar(
        "oferta",
        json.dumps(oferta_datos, ensure_ascii=False),
        str(uuid.uuid4()),
        rfq_usar.name,
    )

    adjuntos_in = payload.get("adjuntos") or []
    adjuntos_guardados = 0
    if adjuntos_in and res_sq and res_sq.get("folio"):
        import base64
        import os
        from frappe.utils.file_manager import save_file

        for adj in adjuntos_in[:10]:
            nombre_raw = os.path.basename(str((adj or {}).get("filename") or "").strip())
            b64_raw = str((adj or {}).get("content_base64") or "").strip()
            if not nombre_raw or not b64_raw:
                continue
            b64_clean = b64_raw.split(",", 1)[1] if "," in b64_raw else b64_raw
            datos_bin = base64.b64decode(b64_clean)
            if not datos_bin:
                continue
            if len(datos_bin) > 25 * 1024 * 1024:
                frappe.throw(f"El archivo '{nombre_raw}' supera el límite de 25 MB.")
            save_file(nombre_raw, datos_bin, "Supplier Quotation", res_sq["folio"], is_private=1)
            adjuntos_guardados += 1

    if isinstance(res_sq, dict):
        res_sq["adjuntos_guardados"] = adjuntos_guardados
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


@frappe.whitelist(methods=["GET"])
def obtener_ficha_edicion_proveedor(proveedor: str = ""):
    """Devuelve datos actuales del proveedor (incluyendo RFC, CSF y Carátula) para alta o edición rápida por cualquier usuario."""
    grupos = frappe.get_list("Supplier Group", filters={"is_group": 0}, pluck="name", order_by="name asc", limit_page_length=200)
    if not proveedor:
        frappe.has_permission("Supplier", "create", throw=True)
        return {
            "nuevo": 1,
            "grupos": grupos,
            "datos": {
                "supplier_name": "",
                "supplier_group": grupos[0] if grupos else "",
                "especialidad": "",
                "tax_id": "",
                "contacto": "",
                "telefono": "",
                "correo": "",
                "clabe": "",
                "csf_archivo": "",
                "csf_fecha": "",
                "caratula_archivo": "",
            },
        }

    doc = frappe.get_doc("Supplier", proveedor)
    doc.check_permission("read")
    from lebaref_compras import proveedores as mod_prov
    vals_cto = mod_prov.valores(doc)
    return {
        "nuevo": 0,
        "id": doc.name,
        "grupos": grupos,
        "datos": {
            "supplier_name": doc.supplier_name or doc.name,
            "supplier_group": doc.supplier_group or (grupos[0] if grupos else ""),
            "especialidad": (doc.get("custom_especialidad_cotizar") or "").strip(),
            "tax_id": doc.tax_id or "",
            "contacto": vals_cto.get("contacto") or "",
            "telefono": vals_cto.get("telefono") or "",
            "correo": vals_cto.get("correo") or "",
            "clabe": (doc.get("custom_lebaref_clabe") or "").strip(),
            "csf_archivo": (doc.get("custom_lebaref_csf_archivo") or "").strip(),
            "csf_fecha": str(doc.get("custom_lebaref_csf_fecha") or ""),
            "caratula_archivo": (doc.get("custom_lebaref_caratula_archivo") or "").strip(),
        },
    }


@frappe.whitelist(methods=["POST"])
def guardar_proveedor_rapido(datos: str):
    """Permite a cualquier usuario del equipo crear un proveedor nuevo o modificar sus datos, RFC, CSF y Carátula Bancaria."""
    import base64
    from frappe.utils import today, validate_email_address
    from frappe.utils.file_manager import save_file
    from lebaref_compras import proveedores as mod_prov

    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(d, dict):
        frappe.throw("Datos de proveedor inválidos.")

    prov_id = (d.get("id") or "").strip()
    nombre = (d.get("supplier_name") or "").strip()
    if not nombre:
        frappe.throw("Indica el nombre o razón social del proveedor.")

    grupo = (d.get("supplier_group") or "").strip() or (
        frappe.db.get_value("Supplier Group", {"is_group": 0}, "name") or "All Supplier Groups"
    )
    rfc = (d.get("tax_id") or "").strip().upper()
    especialidad = (d.get("especialidad") or "").strip()[:140]
    clabe = (d.get("clabe") or "").strip()[:40]
    csf_fecha = (d.get("csf_fecha") or "").strip()
    contacto_nom = (d.get("contacto") or "").strip()
    telefono = (d.get("telefono") or "").strip()
    correo = (d.get("correo") or "").strip()
    if correo and validate_email_address(correo, throw=True) != correo:
        frappe.throw("Captura una sola dirección de correo electrónico válida.")

    if not prov_id:
        frappe.has_permission("Supplier", "create", throw=True)
        doc = frappe.get_doc({
            "doctype": "Supplier",
            "supplier_name": nombre,
            "supplier_group": grupo,
            "supplier_type": "Company",
            "tax_id": rfc or None,
        })
        if frappe.db.has_column("Supplier", "custom_especialidad_cotizar"):
            doc.custom_especialidad_cotizar = especialidad
        if frappe.db.has_column("Supplier", "custom_lebaref_clabe"):
            doc.custom_lebaref_clabe = clabe
        if csf_fecha and frappe.db.has_column("Supplier", "custom_lebaref_csf_fecha"):
            doc.custom_lebaref_csf_fecha = csf_fecha
        mod_prov.configurar(doc, {"contacto": contacto_nom, "telefono": telefono, "correo": correo})
        doc.insert()
        mod_prov.completar(doc)
        es_nuevo = 1
    else:
        doc = frappe.get_doc("Supplier", prov_id)
        doc.check_permission("write")
        doc.supplier_name = nombre
        if grupo and frappe.db.exists("Supplier Group", grupo):
            doc.supplier_group = grupo
        doc.tax_id = rfc or None
        doc.mobile_no = telefono
        doc.email_id = correo
        if frappe.db.has_column("Supplier", "custom_especialidad_cotizar"):
            doc.custom_especialidad_cotizar = especialidad
        if frappe.db.has_column("Supplier", "custom_lebaref_clabe"):
            doc.custom_lebaref_clabe = clabe
        if csf_fecha and frappe.db.has_column("Supplier", "custom_lebaref_csf_fecha"):
            doc.custom_lebaref_csf_fecha = csf_fecha
        doc.save()

        if contacto_nom or telefono or correo:
            if doc.supplier_primary_contact and frappe.db.exists("Contact", doc.supplier_primary_contact):
                ct = frappe.get_doc("Contact", doc.supplier_primary_contact)
                ct.check_permission("write")
                if contacto_nom:
                    ct.first_name = contacto_nom[:80]
                    ct.last_name = ""
                ct.set("email_ids", [{"email_id": correo, "is_primary": 1}] if correo else [])
                ct.set("phone_nos", [{"phone": telefono, "is_primary_mobile_no": 1, "is_primary_phone": 1}] if telefono else [])
                ct.save()
            else:
                frappe.has_permission("Contact", "create", throw=True)
                ct = frappe.get_doc({
                    "doctype": "Contact",
                    "first_name": (contacto_nom or nombre)[:80],
                    "is_primary_contact": 1,
                    "links": [{"link_doctype": "Supplier", "link_name": doc.name}],
                })
                if correo:
                    ct.append("email_ids", {"email_id": correo, "is_primary": 1})
                if telefono:
                    ct.append("phone_nos", {"phone": telefono, "is_primary_mobile_no": 1, "is_primary_phone": 1})
                ct.insert()
                doc.db_set("supplier_primary_contact", ct.name, update_modified=False)
        es_nuevo = 0

    # Adjuntar CSF y/o Carátula Bancaria si se enviaron archivos en el mismo formulario
    for clave_nom, clave_b64, prefijo, campo_dest in (
        ("csf_nombre", "csf_base64", "CSF", "custom_lebaref_csf_archivo"),
        ("caratula_nombre", "caratula_base64", "Caratula_Bancaria", "custom_lebaref_caratula_archivo"),
    ):
        fname = (d.get(clave_nom) or "").strip()
        fb64 = (d.get(clave_b64) or "").strip()
        if fname and fb64 and frappe.db.has_column("Supplier", campo_dest):
            raw = base64.b64decode(fb64.split(",", 1)[1] if "," in fb64 else fb64)
            fdoc = save_file(f"{prefijo}_{doc.name}_{fname}", raw, "Supplier", doc.name, is_private=1)
            cambios_f = {campo_dest: fdoc.file_url}
            if campo_dest == "custom_lebaref_csf_archivo" and not csf_fecha:
                cambios_f["custom_lebaref_csf_fecha"] = today()
            frappe.db.set_value("Supplier", doc.name, cambios_f, update_modified=False)

    return {"ok": True, "folio": doc.name, "supplier_name": doc.supplier_name, "nuevo": es_nuevo}


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

    from lebaref_compras import catalogo_gobierno
    if "familia" in d and frappe.db.has_column("Item", "custom_lebaref_familia"):
        fam_in = (d.get("familia") or "").strip()
        doc.custom_lebaref_familia = catalogo_gobierno.normalizar_nombre_familia(fam_in, doc.item_name or "", doc.item_group or "")[:140]

    if "gerencias" in d and frappe.db.has_column("Item", "custom_lebaref_gerencias"):
        raw_g = d.get("gerencias")
        if isinstance(raw_g, list):
            lista_g = [str(x).strip() for x in raw_g if str(x).strip()]
        else:
            lista_g = catalogo_gobierno.parsear_lista_gerencias(str(raw_g or ""))
        doc.custom_lebaref_gerencias = ", ".join(lista_g)[:140]

    if "recotizar_siempre" in d and frappe.db.has_column("Item", "custom_lebaref_recotizar_siempre"):
        doc.custom_lebaref_recotizar_siempre = 1 if int(flt(d.get("recotizar_siempre") or 0)) else 0

    if "consumo_directo" in d and frappe.db.has_column("Item", "custom_lebaref_consumo_directo"):
        doc.custom_lebaref_consumo_directo = 1 if int(flt(d.get("consumo_directo") or 0)) else 0

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


PERFILES_OFICINA_PERMITIDOS = (
    "Lebaref Auxiliar Operativo Mixto",
    "Lebaref Finanzas y Contabilidad",
    "Lebaref Coordinador Operativo",
    "Lebaref Auxiliar de Compras",
    "Lebaref Auxiliar de Almacén",
    "Lebaref Responsable de Compras",
)


def _exigir_coordinador_usuarios():
    roles = set(frappe.get_roles())
    if "System Manager" in roles:
        return
    if {"Purchase Manager", "Stock Manager", "Accounts Manager"}.issubset(roles):
        return
    frappe.throw("Solo el Coordinador Operativo o Dirección puede administrar cuentas de usuarios.", frappe.PermissionError)


@frappe.whitelist(methods=["GET"])
def listar_usuarios_oficina():
    """Devuelve las cuentas operativas de la oficina para renombrar auxiliares o actualizar su contraseña (OPS-03 / SEG-01)."""
    _exigir_coordinador_usuarios()
    excluidos = ("Guest", "Administrator")
    usuarios = frappe.get_all(
        "User",
        filters={"name": ["not in", excluidos], "user_type": "System User"},
        fields=["name", "email", "first_name", "last_name", "full_name", "enabled", "role_profile_name", "last_login"],
        order_by="enabled desc, creation asc",
    )
    filtrados = [u for u in usuarios if not str(u.name).endswith("@lebaref.test")]
    return {
        "usuarios": filtrados,
        "perfiles": [p for p in PERFILES_OFICINA_PERMITIDOS if frappe.db.exists("Role Profile", p)],
    }


@frappe.whitelist(methods=["POST"])
def guardar_usuario_oficina(datos: str):
    """Crea o actualiza el nombre real, perfil operativo, estado activo y contraseña de un usuario de oficina."""
    from frappe.utils.password import update_password

    _exigir_coordinador_usuarios()
    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(d, dict):
        frappe.throw("Datos de usuario inválidos.")

    email = (d.get("email") or "").strip().lower()
    nombre = (d.get("first_name") or "").strip()
    apellido = (d.get("last_name") or "").strip()
    perfil = (d.get("role_profile_name") or "").strip()
    nueva_pwd = (d.get("new_password") or "").strip()
    activo = 1 if int(d.get("enabled", 1)) else 0

    if not email or "@" not in email:
        frappe.throw("Indica un correo válido para iniciar sesión (ej. juan.perez@lebaref.com).")
    if not nombre:
        frappe.throw("Indica el nombre de la persona o puesto.")
    if perfil not in PERFILES_OFICINA_PERMITIDOS or not frappe.db.exists("Role Profile", perfil):
        frappe.throw("Selecciona un perfil operativo válido de Lebaref.")

    es_nuevo = not frappe.db.exists("User", email)
    if es_nuevo:
        if len(nueva_pwd) < 8:
            frappe.throw("Asigna una contraseña inicial de al menos 8 caracteres para la cuenta nueva.")
        u = frappe.get_doc({
            "doctype": "User",
            "email": email,
            "first_name": nombre[:80],
            "last_name": apellido[:80],
            "language": "es",
            "enabled": activo,
            "send_welcome_email": 0,
            "user_type": "System User",
            "role_profile_name": perfil,
            "role_profiles": [{"role_profile": perfil}],
        })
        u.insert(ignore_permissions=True)
    else:
        u = frappe.get_doc("User", email)
        u.first_name = nombre[:80]
        u.last_name = apellido[:80]
        u.language = "es"
        u.enabled = activo
        u.role_profile_name = perfil
        if u.meta.has_field("role_profiles"):
            u.set("role_profiles", [{"role_profile": perfil}])
        u.populate_role_profile_roles()
        u.save(ignore_permissions=True)

    if nueva_pwd:
        if len(nueva_pwd) < 8:
            frappe.throw("La contraseña nueva debe tener al menos 8 caracteres.")
        update_password(u.name, nueva_pwd)

    frappe.db.set_default("desktop:home_page", "lebaref", parent=u.name)
    return {"ok": True, "email": u.name, "full_name": u.full_name, "nuevo": es_nuevo}


_CONCEPTO_PLACEHOLDER_SOL = "Pendiente de cargar partidas"


def _evaluar_edicion_partidas_solicitud(doc):
    """Determina si las partidas/artículos de una Solicitud pueden editarse o cargarse desde Excel."""
    if not doc.has_permission("write"):
        return False, "Tu cuenta no tiene permiso de edición en Solicitudes."
    if int(doc.docstatus or 0) == 2 or doc.status == "Cancelled":
        return False, "La solicitud está cancelada."
    if doc.status == "Stopped" or int(doc.get("custom_lebaref_concluida") or 0) == 1:
        return False, "La solicitud está concluida / cerrada."
    if int(doc.docstatus or 0) == 0:
        return True, ""
    for dt_child, etq in (
        ("Purchase Order Item", "Orden de Compra"),
        ("Purchase Receipt Item", "Recepción de Almacén"),
        ("Supplier Quotation Item", "Cotización de Proveedor"),
        ("Request for Quotation Item", "Petición de Cotización"),
        ("Stock Entry Detail", "Movimiento de Almacén"),
    ):
        if frappe.db.exists("DocType", dt_child) and frappe.db.exists(dt_child, {"material_request": doc.name, "docstatus": ["<", 2]}):
            return False, f"Ya tiene {etq} vinculada; usa '+ Anexo (SOL-03)' para pedir material adicional."
    return True, ""


@frappe.whitelist(methods=["GET"])
def estado_edicion_solicitud(solicitud: str):
    """Devuelve para el formulario o modal las partidas actuales y si son editables."""
    from lebaref_compras import solicitudes as mod_sol

    doc = frappe.get_doc("Material Request", solicitud)
    doc.check_permission("read")
    puede, motivo = _evaluar_edicion_partidas_solicitud(doc)
    partidas = []
    for r in doc.items:
        es_libre = r.item_code == mod_sol.ARTICULO_LIBRE or bool(r.get("custom_lebaref_concepto"))
        conc = mod_sol.concepto_visible(r) if es_libre else ""
        if es_libre and conc == _CONCEPTO_PLACEHOLDER_SOL and len(doc.items) == 1:
            continue
        partidas.append({
            "name": r.name,
            "es_libre": es_libre,
            "item_code": "" if es_libre else r.item_code,
            "concepto_libre": conc if es_libre else (r.item_name or r.description or ""),
            "qty": flt(r.qty),
            "uom": r.uom or r.stock_uom or "Pieza",
            "project": r.project or "",
            "partida": r.get("custom_lebaref_partida") or "",
        })
    uoms = frappe.get_list("UOM", filters={"enabled": 1}, pluck="name", order_by="name asc", limit_page_length=500)
    obras = frappe.get_list("Project", pluck="name", order_by="name asc", limit_page_length=500) if frappe.has_permission("Project", "read") else []
    skus = frappe.get_list(
        "Item",
        filters={"disabled": 0, "name": ["!=", mod_sol.ARTICULO_LIBRE]},
        fields=["name", "item_name", "purchase_uom", "stock_uom"],
        order_by="name asc",
        limit_page_length=1000,
    ) if frappe.has_permission("Item", "read") else []
    return {
        "folio": doc.name,
        "docstatus": int(doc.docstatus or 0),
        "puede_modificar": puede,
        "motivo_bloqueo": motivo,
        "partidas": partidas,
        "uoms": uoms,
        "obras": obras,
        "skus": skus,
        "departamentos": mod_sol.departamentos(),
    }


@frappe.whitelist(methods=["POST"])
def crear_solicitud_solo_cabecera(datos: str):
    """Crea una Solicitud en Borrador únicamente con sus datos principales para luego cargar partidas por Excel o manual."""
    from lebaref_compras import solicitudes as mod_sol
    from frappe.utils import add_days, today

    if not frappe.has_permission("Material Request", "create"):
        frappe.throw("Tu cuenta no tiene permiso para crear Solicitudes de Material.", frappe.PermissionError)
    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(d, dict):
        frappe.throw("Datos inválidos.")

    company = (d.get("company") or "").strip() or frappe.get_all("Company", pluck="name")[0]
    titulo = (d.get("titulo") or d.get("title") or "").strip()[:140]
    if not titulo:
        frappe.throw("Escribe el nombre o referencia principal de la solicitud.")
    sched = (d.get("schedule_date") or "").strip() or add_days(today(), 7)
    headers = mod_sol.cabecera({
        "company": company,
        "departamento": (d.get("departamento") or "").strip(),
        "solicitante": (d.get("solicitante") or "").strip(),
        "notas": (d.get("notas") or "").strip(),
    })
    obra_def = (d.get("obra_default") or "").strip() or None
    fila_inicial = mod_sol.renglon({
        "concepto_libre": _CONCEPTO_PLACEHOLDER_SOL,
        "qty": 1,
        "uom": "Pieza",
        "project": obra_def,
        "custom_lebaref_partida": "",
    }, sched)

    proposito = (d.get("proposito") or "Compra operativa").strip()
    if proposito not in ("Compra operativa", "Solo presupuesto para ingeniero"):
        proposito = "Compra operativa"

    doc = frappe.get_doc({
        "doctype": "Material Request",
        "material_request_type": "Purchase",
        "company": company,
        "title": titulo,
        "custom_lebaref_nombre_solicitud": titulo,
        "custom_lebaref_proposito": proposito,
        "transaction_date": today(),
        "schedule_date": sched,
        "items": [fila_inicial],
        **headers,
    })
    doc.insert()
    return {"ok": True, "folio": doc.name, "docstatus": doc.docstatus}


@frappe.whitelist(methods=["POST"])
def guardar_partidas_solicitud(solicitud: str, datos: str):
    """Agrega o reemplaza artículos y partidas en una Solicitud disponible (desde Excel/CSV o tabla manual)."""
    from lebaref_compras import analitica_operativa, solicitudes as mod_sol
    from frappe.utils import today

    doc = frappe.get_doc("Material Request", solicitud, for_update=True)
    puede, motivo = _evaluar_edicion_partidas_solicitud(doc)
    if not puede:
        frappe.throw(motivo or "Esta solicitud ya no permite modificar sus partidas.")

    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(d, dict):
        frappe.throw("Datos inválidos.")

    modo = (d.get("modo") or "agregar").strip().lower()
    obra_def = (d.get("obra_default") or "").strip()
    almacen_def = frappe.db.get_value("Warehouse", {"company": doc.company, "is_group": 0, "disabled": 0}, "name") or ""
    sched = str(doc.schedule_date or today())

    nuevos_renglones = []
    if d.get("texto_csv") or d.get("content_base64"):
        prev = analitica_operativa.previsualizar_carga_masiva_solicitud(
            texto_csv=d.get("texto_csv") or "",
            filename=d.get("filename") or "",
            content_base64=d.get("content_base64") or "",
            obra_default=obra_def,
        )
        if prev["errores"] and not d.get("ignorar_errores"):
            frappe.throw("Corrige primero las filas con error:\n• " + "\n• ".join(prev["errores"][:8]))
        if not prev["filas_validas"]:
            frappe.throw("No se encontraron partidas válidas en el archivo o texto.")
        for f in prev["filas_validas"]:
            obra_f = f["obra"] or obra_def
            if f["es_sku"]:
                reng_sku = mod_sol.renglon({
                    "item_code": f["item_code"],
                    "qty": f["qty"],
                    "uom": f["uom"],
                    "warehouse": almacen_def,
                    "project": obra_f,
                    "custom_lebaref_partida": f["partida"],
                }, sched)
                if f.get("descripcion"):
                    reng_sku["description"] = f["descripcion"]
                nuevos_renglones.append(reng_sku)
            else:
                nuevos_renglones.append(mod_sol.renglon({
                    "concepto_libre": f["descripcion"],
                    "qty": f["qty"],
                    "uom": f["uom"],
                    "project": obra_f,
                    "custom_lebaref_partida": f["partida"],
                }, sched))
    else:
        filas_raw = d.get("rows") or []
        if not isinstance(filas_raw, list) or not filas_raw:
            frappe.throw("Agrega al menos una partida válida.")
        for idx_r, r in enumerate(filas_raw, start=1):
            sku = (r.get("item_code") or "").strip()
            conc = (r.get("concepto_libre") or "").strip()
            if not sku and not conc:
                continue
            if sku and not frappe.db.exists("Item", sku):
                # Si escribieron texto en código y no es SKU, tratar como concepto libre
                conc = f"{sku} {conc}".strip()
                sku = ""
            if sku:
                conc = ""
            it_uom = ""
            if sku:
                it_uom = frappe.db.get_value("Item", sku, "purchase_uom") or frappe.db.get_value("Item", sku, "stock_uom") or "Pieza"
            uom_r = (r.get("uom") or "").strip()
            if not uom_r or not frappe.db.exists("UOM", uom_r):
                uom_r = it_uom or "Pieza"
            nuevos_renglones.append(mod_sol.renglon({
                "item_code": sku,
                "concepto_libre": conc,
                "qty": r.get("qty") or 1,
                "uom": uom_r,
                "warehouse": almacen_def if sku else None,
                "project": (r.get("project") or obra_def or "").strip() or None,
                "custom_lebaref_partida": (r.get("partida") or r.get("custom_lebaref_partida") or "").strip(),
            }, sched))

    if not nuevos_renglones:
        frappe.throw("Captura al menos una partida con artículo o concepto libre.")

    # Conservar partidas existentes si el modo es 'agregar', salvo el placeholder inicial vacío
    existentes_limpios = []
    if modo == "agregar":
        for r in doc.items:
            es_ph = (
                r.item_code == mod_sol.ARTICULO_LIBRE
                and mod_sol.concepto_visible(r) == _CONCEPTO_PLACEHOLDER_SOL
                and len(doc.items) == 1
            )
            if not es_ph:
                d_row = {
                    "item_code": r.item_code,
                    "item_name": r.item_name,
                    "description": r.description,
                    "qty": r.qty,
                    "uom": r.uom,
                    "stock_uom": r.stock_uom,
                    "conversion_factor": r.conversion_factor,
                    "warehouse": r.warehouse,
                    "from_warehouse": r.from_warehouse,
                    "project": r.project,
                    "schedule_date": r.schedule_date or sched,
                    "custom_lebaref_concepto": r.get("custom_lebaref_concepto"),
                    "custom_lebaref_partida": r.get("custom_lebaref_partida"),
                }
                existentes_limpios.append(d_row)

    todos = existentes_limpios + nuevos_renglones
    doc.set("items", todos)
    sincronizar_vistas_partidas(doc)

    if int(doc.docstatus or 0) == 0:
        doc.flags.ignore_permissions = False
        doc.save()
        if d.get("confirmar") and doc.has_permission("submit"):
            doc.submit()
    else:
        # Solicitud docstatus=1 sin documentos aguas abajo: actualizar tabla hija preservando el folio SOL-AAAA-####
        for r in doc.items:
            r.docstatus = 1
            if not r.item_name:
                r.item_name = frappe.db.get_value("Item", r.item_code, "item_name") or r.item_code
            if not r.description:
                r.description = r.get("custom_lebaref_concepto") or r.item_name
            r.stock_qty = flt(r.qty) * flt(r.conversion_factor or 1.0)
        doc.update_children()
        doc.db_set("modified", frappe.utils.now(), update_modified=False)
        try:
            from lebaref_compras import seguimiento_google
            if hasattr(seguimiento_google, "encolar_solicitud"):
                seguimiento_google.encolar_solicitud(doc.name)
            elif hasattr(seguimiento_google, "marcar_pendiente"):
                seguimiento_google.marcar_pendiente(doc.name)
        except Exception:
            pass

    return {
        "ok": True,
        "folio": doc.name,
        "total_partidas": len(doc.items),
        "agregadas": len(nuevos_renglones),
        "docstatus": int(doc.docstatus or 0),
    }


@frappe.whitelist(methods=["POST"])
def parsear_excel_cotizacion(solicitud: str = "", texto_csv: str = "", filename: str = "", content_base64: str = ""):
    """Lee un archivo .xlsx/.csv o texto pegado desde Excel para llenar precios/partidas de una Cotización."""
    import csv
    import io
    from lebaref_compras import analitica_operativa, solicitudes as mod_sol

    if content_base64 and not (texto_csv or "").strip():
        texto_csv = analitica_operativa.extraer_texto_desde_archivo_base64(filename, content_base64)
    raw = (texto_csv or "").strip()
    if not raw:
        frappe.throw("Selecciona un archivo Excel (.xlsx) / CSV o pega las filas desde Excel.")

    delim = "\t" if "\t" in raw.splitlines()[0] else (";" if ";" in raw.splitlines()[0] and "," not in raw.splitlines()[0] else ",")
    filas_csv = list(csv.reader(io.StringIO(raw), delimiter=delim))
    if not filas_csv:
        frappe.throw("No se encontraron filas legibles.")

    inicio = 0
    hdr0 = " ".join((c or "").lower() for c in filas_csv[0])
    if any(k in hdr0 for k in ("precio", "rate", "codigo", "código", "clave", "concepto", "cantidad", "renglon", "renglón", "mr_item")):
        inicio = 1

    def _num(val, default=0.0):
        s = str(val or "").replace("$", "").replace(",", "").strip()
        if not s:
            return default
        try:
            return float(s)
        except ValueError:
            return default

    # Caso A: Cotización Directa vinculada a una Solicitud (mapear precios a mr_item)
    if solicitud and frappe.db.exists("Material Request", solicitud):
        mr = frappe.get_doc("Material Request", solicitud)
        mr.check_permission("read")
        por_id = {r.name: r for r in mr.items}
        por_idx = {int(r.idx): r for r in mr.items}
        por_clave = {}
        for r in mr.items:
            clave, desc = _calcular_clave_y_descripcion(r.item_code, r.item_name, r.description, r.get("custom_lebaref_concepto"))
            por_clave[clave.strip().upper()] = r
            por_clave[desc.strip().upper()[:80]] = r

        precios = {}
        for cols in filas_csv[inicio:]:
            if not cols or not any((c or "").strip() for c in cols):
                continue
            # Soportar plantilla: Renglón/# | MR_Item_ID | Clave/Concepto | Cantidad | Unidad | Precio_Sin_IVA | Días_Entrega
            # O formato corto: Clave_o_# | Precio_Sin_IVA | Días_Entrega
            target = None
            rate_val = 0.0
            dias_val = 1
            c0 = (cols[0] or "").strip()
            c1 = (cols[1] or "").strip() if len(cols) > 1 else ""
            if c1 in por_id and len(cols) >= 6:
                target = por_id[c1]
                rate_val = _num(cols[5], 0.0)
                dias_val = max(0, int(_num(cols[6] if len(cols) > 6 else "1", 1)))
            elif c0 in por_id:
                target = por_id[c0]
                rate_val = _num(cols[1] if len(cols) > 1 else 0)
                dias_val = max(0, int(_num(cols[2] if len(cols) > 2 else 1, 1)))
            else:
                if c0.isdigit() and int(c0) in por_idx:
                    target = por_idx[int(c0)]
                elif c0.upper() in por_clave:
                    target = por_clave[c0.upper()]
                elif c1.upper() in por_clave:
                    target = por_clave[c1.upper()]
                if target:
                    if len(cols) >= 5:
                        rate_val = _num(cols[4] if len(cols) == 5 else cols[5], 0.0)
                        dias_val = max(0, int(_num(cols[5] if len(cols) == 6 else (cols[6] if len(cols) > 6 else "1"), 1)))
                    elif len(cols) >= 4:
                        rate_val = _num(cols[3], 0.0)
                        dias_val = max(0, int(_num(cols[4] if len(cols) > 4 else "1", 1)))
                    else:
                        rate_val = _num(cols[1] if len(cols) > 1 else 0.0)
                        dias_val = max(0, int(_num(cols[2] if len(cols) > 2 else "1", 1)))
            if target and rate_val > 0:
                precios[target.name] = {"rate": rate_val, "lead_time_days": dias_val}
        return {"modo": "solicitud", "precios": precios, "coincidencias": len(precios)}

    # Caso B: Formulario libre de Supplier Quotation (Clave_o_Concepto | Cantidad | Unidad | Precio_Sin_IVA | Días_Entrega | Partida)
    almacen_def = frappe.db.get_value("Warehouse", {"is_group": 0, "disabled": 0}, "name") or ""
    filas_sq = []
    for cols in filas_csv[inicio:]:
        if not cols or not any((c or "").strip() for c in cols):
            continue
        c_cod = (cols[0] or "").strip()
        if not c_cod:
            continue
        qty = _num(cols[1] if len(cols) > 1 else 1, 1.0)
        if qty <= 0:
            qty = 1.0
        uom_in = (cols[2] if len(cols) > 2 else "").strip()
        rate = _num(cols[3] if len(cols) > 3 else 0.0, 0.0)
        dias = max(0, int(_num(cols[4] if len(cols) > 4 else 1, 1)))
        partida = (cols[5] if len(cols) > 5 else "").strip()

        it_match, _ = analitica_operativa._resolver_sku_o_concepto(c_cod)
        if it_match:
            uom_ok = uom_in if (uom_in and frappe.db.exists("UOM", uom_in)) else (it_match.purchase_uom or it_match.stock_uom or "Pieza")
            filas_sq.append({
                "item_code": it_match.name,
                "item_name": it_match.item_name,
                "description": it_match.item_name,
                "custom_lebaref_concepto": "",
                "qty": qty,
                "uom": uom_ok,
                "stock_uom": it_match.stock_uom or uom_ok,
                "conversion_factor": 1.0,
                "rate": rate,
                "lead_time_days": dias,
                "warehouse": almacen_def,
                "custom_lebaref_partida": partida,
            })
        else:
            uom_ok = uom_in if (uom_in and frappe.db.exists("UOM", uom_in)) else "Pieza"
            filas_sq.append({
                "item_code": mod_sol.ARTICULO_LIBRE,
                "item_name": c_cod[:140],
                "description": c_cod,
                "custom_lebaref_concepto": c_cod,
                "qty": qty,
                "uom": uom_ok,
                "stock_uom": "Pieza",
                "conversion_factor": 1.0,
                "rate": rate,
                "lead_time_days": dias,
                "warehouse": "",
                "custom_lebaref_partida": partida,
            })
    return {"modo": "libre", "items": filas_sq, "coincidencias": len(filas_sq)}


@frappe.whitelist(methods=["GET"])
def valores_por_defecto_lebaref():
    """Devuelve valores inteligentes por defecto (CAL-01) para prellenar formularios nuevos sin fricción."""
    from lebaref_compras import comercial

    company = (
        frappe.defaults.get_user_default("Company")
        or frappe.db.get_single_value("Global Defaults", "default_company")
        or frappe.db.get_value("Company", {}, "name")
        or ""
    )
    filtros_alm = {"is_group": 0, "disabled": 0}
    if company:
        filtros_alm["company"] = company
    warehouse = (
        frappe.db.get_value("Warehouse", dict(filtros_alm, warehouse_name=["like", "%Almacén%"]), "name")
        or frappe.db.get_value("Warehouse", filtros_alm, "name")
        or ""
    )
    filtros_imp = {"disabled": 0}
    if company:
        filtros_imp["company"] = company
    impuestos = comercial.opciones(comercial.IMPUESTOS, filtros_imp)
    taxes_iva16 = next((x for x in impuestos if "16%" in x), impuestos[0] if impuestos else "")
    user_id = frappe.session.user
    fullname = frappe.utils.get_fullname(user_id) if user_id and user_id != "Guest" else ""
    uom_def = "Pieza" if frappe.db.exists("UOM", "Pieza") else (frappe.db.get_value("UOM", {}, "name") or "Nos")

    return {
        "company": company,
        "warehouse": warehouse,
        "taxes_and_charges_iva16": taxes_iva16,
        "currency": "MXN",
        "user_fullname": fullname,
        "default_uom": uom_def,
        "schedule_date_7d": add_days(today(), 7),
        "valid_till_30d": add_days(today(), 30),
    }


@frappe.whitelist(methods=["GET"])
def info_rapida_item_default(item_code: str):
    """Consulta ligera de unidad, control de stock y consumo directo al elegir un artículo en renglón."""
    codigo = (item_code or "").strip()
    if not codigo or not frappe.db.exists("Item", codigo):
        return {}
    campos = ["name", "item_name", "stock_uom", "purchase_uom", "is_stock_item"]
    for extra in ("custom_lebaref_consumo_directo", "custom_lebaref_ultimo_costo"):
        if frappe.db.has_column("Item", extra):
            campos.append(extra)
    datos = frappe.db.get_value("Item", codigo, campos, as_dict=True) or {}
    uom_sugerida = datos.get("purchase_uom") or datos.get("stock_uom") or "Pieza"
    es_consumo_dir = int(datos.get("custom_lebaref_consumo_directo") or 0)
    es_stock = int(datos.get("is_stock_item") or 0)
    return {
        "item_code": datos.get("name"),
        "item_name": datos.get("item_name") or "",
        "uom": uom_sugerida,
        "stock_uom": datos.get("stock_uom") or uom_sugerida,
        "is_stock_item": es_stock,
        "consumo_directo": es_consumo_dir,
        "requiere_almacen": 1 if (es_stock and not es_consumo_dir and codigo != "LEBAREF-CONCEPTO-LIBRE") else 0,
        "ultimo_costo": flt(datos.get("custom_lebaref_ultimo_costo") or 0.0),
    }
