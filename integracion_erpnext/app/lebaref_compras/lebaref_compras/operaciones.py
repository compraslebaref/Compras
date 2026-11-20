"""Operaciones transaccionales de Lebaref con permisos de ERPNext."""
import hashlib
import json
import re
import uuid
from xml.etree import ElementTree as ET

import frappe
from frappe.utils import today, add_days
from lebaref_compras import comercial, consumos, proveedores, recepciones, solicitudes, sobrantes, sustituciones, surtido, unidades
from lebaref_compras.unidades import numero

TARGETS = {"solicitud": "Material Request", "anexo": "Material Request",
           "convertir_concepto": "Material Request", "canalizar_stock": "Material Request",
           "sobrante": "Stock Entry", "autorizar_sobrante": "Stock Entry", "consumir": "Stock Entry", "surtir": "Stock Entry", "peticion": "Request for Quotation",
           "oferta": "Supplier Quotation", "autorizar": "Purchase Order",
           "recibir": "Purchase Receipt", "entrada_sin_oc": "Purchase Receipt",
           "autorizar_sustitucion": "Lebaref Sustitucion", "regularizar_entrada": "Purchase Receipt", "entregar": "Stock Entry", "devolver": "Stock Entry",
           "cfdi": "Lebaref CFDI", "articulo": "Item", "proveedor": "Supplier", "obra": "Project", "almacen": "Warehouse"}
MASTERS = {"articulo", "proveedor", "obra", "almacen"}
ENMIENDAS = {"convertir_concepto", "canalizar_stock"}
SOURCES = {action: "Material Request" for action in (*ENMIENDAS, "anexo", "surtir", "peticion")}
SOURCES.update({"oferta": "Request for Quotation",
           "autorizar": "Purchase Order", "autorizar_sustitucion": "Purchase Order", "recibir": "Purchase Order",
           "autorizar_sobrante": "Stock Entry", "devolver": "Stock Entry", "consumir": "Stock Entry", "cfdi": "Purchase Receipt", "regularizar_entrada": "Purchase Receipt"})


def permiso(accion):
    if accion not in TARGETS:
        frappe.throw("Operación desconocida.")
    dt = TARGETS[accion]
    rights = ("create", "submit")
    if accion == "sobrante":
        rights = ("create",)
    elif accion == "autorizar_sobrante":
        sobrantes.comprobar_autorizacion()
        rights = ("submit",)
    elif accion == "regularizar_entrada":
        rights = ("write",)
    elif accion == "autorizar":
        rights = ("submit",)
    elif accion in MASTERS or accion in ("cfdi", "anexo"):
        rights = ("create",)
    for right in rights:
        if not frappe.has_permission(dt, right):
            frappe.throw("Tu cuenta no tiene permiso para esta operación.", frappe.PermissionError)


def origen(accion, folio, lock=False):
    if accion not in SOURCES:
        return None
    doc = frappe.get_doc(SOURCES[accion], folio, for_update=lock)
    doc.check_permission("read")
    if accion not in ENMIENDAS | {"autorizar", "autorizar_sobrante"} and doc.docstatus != 1:
        frappe.throw("Selecciona un documento confirmado.")
    return doc


def catalogo(dt, filters=None):
    if not frappe.has_permission(dt, "read"):
        return []
    return frappe.get_list(dt, filters=filters or {}, pluck="name", order_by="name", limit_page_length=1000)


def campo(name, label, value="", choices=None, kind="text"):
    return dict(name=name, label=label, value=value, choices=choices, kind=kind)


def columna(name, label, editable=True, choices=None):
    return dict(name=name, label=label, editable=editable, choices=choices)


@frappe.whitelist(methods=["GET"])
def esquema(accion, folio=""):
    from lebaref_compras import equipos
    if accion in equipos.TARGETS:
        return equipos.esquema(accion, folio)
    if accion in ("articulo", "proveedor", "solicitud") and folio:
        doc = frappe.get_doc(TARGETS[accion], folio)
        doc.check_permission("read")
    else:
        permiso(accion)
        doc = origen(accion, folio)
    fields, columns, rows = [], [], []
    note = "Revisa los datos: Guardar confirma el documento en ERPNext."
    if accion == "articulo":
        uoms = catalogo("UOM", {"enabled": 1})
        values = unidades.datos_articulo(doc) if doc else {}
        fields = [
            campo("item_code", "Código único", doc.item_code if doc else ""),
            campo("item_name", "Nombre", doc.item_name if doc else ""),
            campo("item_group", "Grupo", doc.item_group if doc else "", catalogo("Item Group", {"is_group": 0})),
            campo("stock_uom", "Unidad de inventario", values.get("stock_uom", ""), uoms),
            campo("uom", "Unidad de compra", values.get("uom", ""), uoms),
            campo("conversion_factor", "Factor a inventario", values.get("conversion_factor", "")),
            campo("tipo_articulo", "Tipo de artículo", ("Material" if doc.is_stock_item else "Servicio") if doc else "Material", ["Material", "Servicio"]),
        ]
        note = "Consulta unidades del artículo." if doc else (
            "Selecciona Material para controlar existencias o Servicio para compras sin inventario. El factor convierte la unidad de compra a la unidad base."
        )
    elif accion == "proveedor":
        values = proveedores.valores(doc) if doc else {}
        fields = [
            campo("supplier_name", "Nombre", doc.supplier_name if doc else ""),
            campo("supplier_group", "Grupo", doc.supplier_group if doc else "", catalogo("Supplier Group", {"is_group": 0})),
            campo("tax_id", "RFC", (doc.tax_id or "") if doc else ""),
        ]
        fields.extend(campo(key, label, values.get(key, "")) for key, label in proveedores.CAMPOS)
        note = "Consulta los datos del proveedor." if doc else "Alta de proveedor. Verifica el RFC para vincular sus XML posteriormente."
    elif accion == "obra":
        fields = [campo("project_name", "Nombre de obra"), campo("company", "Empresa", choices=catalogo("Company"))]
        note = "Crea la obra como proyecto de ERPNext."
    elif accion == "almacen":
        fields = [campo("warehouse_name", "Nombre del almacén"), campo("company", "Empresa", choices=catalogo("Company")), campo("parent_warehouse", "Almacén agrupador", choices=catalogo("Warehouse", {"is_group": 1}))]
        note = "Crea un almacén operativo bajo el agrupador de su empresa."
    elif accion in ("solicitud", "anexo"):
        annex = doc if accion == "anexo" else None
        if annex:
            solicitudes.validar_vigente(annex, ("Purchase", *surtido.TIPOS))
            doc = None
        fields = [
            campo("company", "Empresa", doc.company if doc else "", catalogo("Company")),
            campo("schedule_date", "Fecha requerida", doc.schedule_date if doc else add_days(today(), 7), kind="date"),
            campo("title", "Descripción", doc.title if doc else ""),
        ]
        values = solicitudes.valores_cabecera(doc or annex) if doc or annex else {}
        fields.extend(
            campo(key, label, values.get(key, ""),
                  choices=solicitudes.departamentos() if key == "departamento" and not doc else None,
                  kind="multiline" if key == "notas" else "text")
            for key, label, _ in solicitudes.CAMPOS
        )
        columns = [
            columna("item_code", "Artículo", choices=catalogo("Item", {"disabled": 0, "name": ["!=", solicitudes.ARTICULO_LIBRE]}) if not doc else None),
            columna("concepto_libre", "Concepto libre"), columna("qty", "Cantidad"),
            columna("warehouse", "Almacén", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0}) if not doc else None),
            columna("project", "Obra", choices=catalogo("Project") if not doc else None),
            columna("custom_lebaref_partida", "Partida"),
        ]
        columns.extend(columna(key, label, editable=key == "uom") for key, label in unidades.CAMPOS)
        if doc:
            for column in columns:
                column["editable"] = False
            rows = [
                dict(id=r.name, item_code="" if r.custom_lebaref_concepto else r.item_code,
                     concepto_libre=solicitudes.concepto_visible(r), qty=r.qty, warehouse=r.warehouse,
                     project=r.project, custom_lebaref_partida=r.custom_lebaref_partida, **unidades.valores(r))
                for r in doc.items
            ]
            note = "Consulta de la solicitud guardada."
        elif annex:
            fields[0]["value"] = annex.company
            columns.append(columna("from_warehouse", "Almacén origen", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0, "company": annex.company})))
            note = f"Crea un anexo en borrador vinculado a {annex.name}. Confírmalo en Desk."
        else:
            note = "Por renglón, selecciona un artículo o captura un concepto libre. Guardar confirma la solicitud."
    elif accion == "convertir_concepto":
        solicitudes.validar_vigente(doc, ("Purchase",))
        fields = [campo("renglon", "Renglón", choices=[r.name for r in doc.items if r.item_code == solicitudes.ARTICULO_LIBRE]),
                  campo("item_code", "SKU inventariable"), campo("uom", "Unidad de compra"), campo("warehouse", "Almacén")]
        note = "Confirma una enmienda con el SKU y conserva la solicitud original en el historial."
    elif accion == "canalizar_stock":
        solicitudes.validar_vigente(doc, ("Purchase",))
        warehouses = catalogo("Warehouse", {"is_group": 0, "disabled": 0, "company": doc.company})
        fields = [campo("modo", "Movimiento", choices=["salida", "transferencia"]),
                  campo("almacen_origen", "Almacén origen", choices=warehouses),
                  campo("almacen_destino", "Almacén destino", choices=warehouses)]
        note = "Confirma una enmienda con tipo de surtido desde stock. El movimiento físico se confirma por separado."
    elif accion == "surtir":
        surtido.validar(doc)
        columns = [columna("id", "Referencia", False), columna("item_code", "Artículo", False),
                   columna("s_warehouse", "Almacén origen", False), columna("t_warehouse", "Almacén destino", False),
                   columna("pending", "Pendiente", False), columna("qty", "Surtir ahora")]
        columns.extend(columna(key, label, False) for key, label in unidades.CAMPOS)
        for r in doc.items:
            pending = surtido.pendiente(r)
            if pending > 0:
                transfer = doc.material_request_type == "Material Transfer"
                rows.append(dict(id=r.name, item_code=r.item_code, pending=pending, qty=0,
                                 s_warehouse=r.from_warehouse if transfer else r.warehouse,
                                 t_warehouse=r.warehouse if transfer else None, **unidades.valores(r)))
        note = "Captura la cantidad en la unidad de la solicitud. Guardar confirma el movimiento de stock."
    elif accion == "peticion":
        fields = [campo("suppliers", "Proveedores (uno por línea)", kind="multiline")]
        note = "Copia nombres exactos del catálogo de proveedores. Se registra la petición; no se envían correos."
    elif accion == "oferta":
        fields = [campo("supplier", "Proveedor", choices=[s.supplier for s in doc.suppliers]), campo("valid_till", "Vigente hasta", add_days(today(), 30), kind="date")]
        fields.extend([
            campo("taxes_and_charges", "Plantilla de impuestos", choices=comercial.opciones(comercial.IMPUESTOS, {"company": doc.company, "disabled": 0})),
            campo("shipping_rule", "Regla de flete", choices=comercial.opciones(comercial.ENVIO, {"company": doc.company, "disabled": 0, "shipping_rule_type": "Buying"})),
            campo("payment_terms_template", "Condición de pago", choices=comercial.opciones(comercial.PAGO)),
            campo("fecha_programada_pago", "Fecha programada de pago", kind="date"),
        ])
        columns = [columna("id", "Referencia", False), columna("item_code", "Artículo", False), columna("qty", "Cantidad", False), columna("rate", "Precio unitario sin impuestos"), columna("lead_time_days", "Días de entrega")]
        columns.extend(columna(key, label, False) for key, label in unidades.CAMPOS)
        columns.append(columna("concepto_libre", "Concepto libre", False))
        rows = [dict(id=r.name, item_code=r.item_code, qty=r.qty, rate=0, lead_time_days=0,
                     concepto_libre=solicitudes.concepto_visible(r), **unidades.valores(r)) for r in doc.items]
        note = "Captura al menos un precio en la moneda de la empresa. Deja en blanco o en cero los renglones sin cotizar."
    elif accion == "autorizar":
        doc.check_permission("submit")
        fields = [campo("confirmar", "Confirmación", choices=["", "AUTORIZAR"])]
        columns = [columna(k, label, False) for k, label in (("item_code", "Artículo"), ("qty", "Cantidad"), ("rate", "Precio"), ("project", "Obra"), ("custom_lebaref_partida", "Partida"))]
        rows = [r.as_dict() for r in doc.items]
        note = f"{doc.name} | {doc.supplier} | Total {doc.grand_total} {doc.currency}. AUTORIZAR confirma la orden."
    elif accion == "autorizar_sustitucion":
        doc.check_permission("write")
        doc.check_permission("submit")
        fields = [campo("renglon", "Renglón de orden", choices=[r.name for r in doc.items]),
                  campo("sustituto", "Artículo sustituto", choices=catalogo("Item", {"disabled": 0, "is_stock_item": 1, "is_purchase_item": 1})),
                  campo("cantidad", "Cantidad a autorizar"), campo("motivo", "Motivo", kind="multiline")]
        columns = [columna(key, label, False) for key, label in (("id", "Referencia"), ("item_code", "Original"), ("pending", "Pendiente"), ("uom", "Unidad"), ("rate", "Precio"))]
        rows = [dict(id=r.name, item_code=r.item_code, pending=r.qty-r.received_qty, uom=r.uom, rate=r.rate) for r in doc.items]
        note = "Confirma una autorización por cantidad. El sustituto debe conservar unidad, factor y precio de la orden."
    elif accion == "recibir":
        fields = [campo("recibido_por", "Persona que recibe"), campo("supplier_delivery_note", "Remisión del proveedor"),
                  campo("motivo_destino", "Motivo de cambio de almacén", kind="multiline")]
        columns = [columna("id", "Referencia", False), columna("item_code", "Artículo", False), columna("pending", "Pendiente", False), columna("qty", "Recibir ahora")]
        columns.extend(columna(key, label, False) for key, label in unidades.CAMPOS)
        columns.append(columna("concepto_libre", "Concepto libre", False))
        columns.append(columna("sustitucion", "Autorización de sustitución", choices=catalogo("Lebaref Sustitucion", {"orden": doc.name, "docstatus": 1})))
        columns.extend([columna("warehouse", "Almacén destino", choices=catalogo("Warehouse", {"company": doc.company, "is_group": 0, "disabled": 0})),
                        columna("project", "Obra", False), columna("is_stock_item", "Controla inventario", False)])
        rows = [dict(id=r.name, item_code=r.item_code, pending=max(0, r.qty-r.received_qty), qty=0,
                     project=r.project, is_stock_item=frappe.get_cached_value("Item", r.item_code, "is_stock_item"),
                     warehouse=r.warehouse if frappe.get_cached_value("Item", r.item_code, "is_stock_item") else "",
                     concepto_libre=solicitudes.concepto_visible(r), **unidades.valores(r))
                for r in doc.items if r.qty > r.received_qty]
        note = "Cero omite el renglón. Puedes recibir material directamente en el almacén de la obra indicando el motivo. Los servicios se registran sin existencias."
    elif accion == "entregar":
        fields = [campo("company", "Empresa", choices=catalogo("Company")), campo("from_warehouse", "Almacén origen", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0})), campo("to_warehouse", "Almacén de obra", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0})), campo("project", "Obra", choices=catalogo("Project")), campo("remarks", "Recibe / referencia")]
        columns = [columna("item_code", "Artículo", choices=catalogo("Item", {"is_stock_item": 1, "disabled": 0})), columna("qty", "Cantidad (unidad de inventario)"), columna("custom_lebaref_partida", "Partida")]
        note = "Transferencia de inventario: la existencia pasa del almacén central al almacén de obra; no representa consumo."
    elif accion == "sobrante":
        fields = [campo("company", "Empresa", choices=catalogo("Company")), campo("project", "Obra de procedencia", choices=catalogo("Project")),
                  campo("referencia", "Acta o comprobante de origen"), campo("persona", "Persona que entrega"),
                  campo("tipo", "Tratamiento", "Traslado", list(sobrantes.TIPOS)),
                  campo("from_warehouse", "Almacén donde ya está registrado", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0})),
                  campo("to_warehouse", "Almacén de destino", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0})),
                  campo("motivo", "Motivo", kind="multiline"), campo("confirmacion", "Verificación para stock nuevo")]
        columns = [columna("item_code", "Artículo", choices=catalogo("Item", {"disabled": 0, "is_stock_item": 1})),
                   columna("qty", "Cantidad en unidad de inventario"), columna("valor_unitario", "Valor por unidad para stock nuevo")]
        note = "Guardar crea un borrador para autorización. Traslada las existencias ya registradas. El ingreso nuevo requiere verificar NO CONTABILIZADO."
    elif accion == "autorizar_sobrante":
        doc.check_permission("submit")
        fields = [campo("confirmar", "Confirmación", choices=["", "AUTORIZAR"])]
        columns = [columna(key, label, False) for key, label in (("item_code", "Artículo"), ("qty", "Cantidad"), ("s_warehouse", "Origen"), ("t_warehouse", "Destino"))]
        rows = [r.as_dict() for r in doc.items]
        note = "Revisa el acta, procedencia y verificación de existencias antes de autorizar el sobrante."
    elif accion == "consumir":
        consumos.validar_entrega(doc)
        fields = [campo("remarks", "Motivo o actividad del consumo", kind="multiline")]
        columns = [columna("id", "Referencia", False), columna("item_code", "Artículo", False),
                   columna("pending", "Bajo resguardo", False), columna("qty", "Consumir ahora"), columna("uom", "Unidad", False)]
        balances = consumos.saldos(doc)
        rows = [dict(id=r.name, item_code=r.item_code, pending=balances[r.name]["saldo"], qty=0, uom=r.uom) for r in doc.items]
        note = "Confirma el consumo desde el almacén de obra, con costo y vínculo a esta entrega."
    elif accion == "devolver":
        if doc.stock_entry_type != "Material Transfer" or doc.custom_lebaref_origen or doc.get("custom_lebaref_tipo_sobrante"):
            frappe.throw("Selecciona una entrega original de tipo Transferencia de materiales.")
        columns = [columna("id", "Referencia", False), columna("item_code", "Artículo", False), columna("pending", "Máximo retornable", False), columna("qty", "Devolver ahora")]
        returned = devoluciones(doc)
        rows = [dict(id=r.name, item_code=r.item_code, pending=r.qty-returned.get(r.name, 0), qty=0) for r in doc.items]
        note = "Retorno de sobrante al almacén original. El sistema verifica existencias y devoluciones anteriores."
    elif accion == "entrada_sin_oc":
        fields = [campo("company", "Empresa", choices=catalogo("Company")),
                  campo("supplier", "Proveedor", choices=catalogo("Supplier")),
                  campo("remarks", "Motivo de entrada anticipada", kind="multiline")]
        columns = [columna("item_code", "Artículo", choices=catalogo("Item", {"disabled": 0, "is_stock_item": 1, "is_purchase_item": 1})),
                   columna("qty", "Cantidad"), columna("rate", "Precio unitario"),
                   columna("warehouse", "Almacén", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0})),
                   columna("uom", "Unidad de compra", choices=catalogo("UOM", {"enabled": 1})),
                   columna("project", "Obra", choices=catalogo("Project")), columna("custom_lebaref_partida", "Partida")]
        note = "Confirma una recepción anticipada. ERPNext debe permitir recepciones sin orden en Buying Settings."
    elif accion == "regularizar_entrada":
        fields = [campo("purchase_order", "Orden de compra", choices=catalogo("Purchase Order", {"docstatus": 1, "supplier": doc.supplier, "company": doc.company})),
                  campo("motivo", "Motivo de regularización", kind="multiline")]
        columns = [columna("id", "Referencia recepción", False), columna("item_code", "Artículo", False),
                   columna("qty", "Cantidad", False), columna("purchase_order_item", "Referencia renglón de orden")]
        rows = [dict(id=r.name, item_code=r.item_code, qty=r.qty, purchase_order_item="") for r in doc.items]
        note = "Vincula todos los renglones a una orden autorizada. Consulta sus referencias en el documento de orden."
    elif accion == "cfdi":
        fields = [campo("xml", "Archivo XML CFDI", kind="file")]
        note = "Archiva el XML contra esta recepción, valida UUID y RFC, y evita duplicados. No crea póliza/factura contable ni verifica el estado SAT."
    return dict(fields=fields, columns=columns, rows=rows,
                add_rows=accion in ("entregar", "anexo", "entrada_sin_oc", "sobrante") or (accion == "solicitud" and not folio), note=note)


def elegidos(data, available):
    result = {}
    rows = data.get("rows", [])
    if not isinstance(rows, list):
        frappe.throw("Envía los renglones como una lista.")
    for row in rows:
        key = row.get("id") if isinstance(row, dict) else None
        if not isinstance(key, str) or key not in available or key in result:
            frappe.throw("El renglón no pertenece al documento o está repetido.")
        result[key] = numero(row.get("qty", 0), zero=True)
    if not any(result.values()):
        frappe.throw("Captura al menos una cantidad mayor que cero.")
    return result


def devoluciones(doc, lock=False):
    # Quantities already consumed or returned are unavailable for another return.
    return {key: values["consumido"] + values["devuelto"] for key, values in consumos.saldos(doc, lock=lock).items()}


@frappe.whitelist(methods=["POST"])
def ejecutar(accion, datos, operacion, folio=""):
    from lebaref_compras import equipos
    if accion in equipos.TARGETS:
        return equipos.ejecutar(accion, datos, operacion, folio)
    permiso(accion)
    data = frappe.parse_json(datos)
    if not isinstance(data, dict):
        frappe.throw("Datos inválidos.")
    try:
        token = str(uuid.UUID(operacion))
    except (ValueError, TypeError, AttributeError):
        frappe.throw("Identificador de operación inválido.")
    signature = hashlib.sha256(json.dumps([accion, folio, data], sort_keys=True, ensure_ascii=False).encode()).hexdigest()
    # Serializes retries, including requests that have no source document.
    # Lock is held until the request transaction commits/rolls back.
    frappe.get_doc("User", frappe.session.user, for_update=True)
    source = origen(accion, folio, lock=True)
    if accion not in ("autorizar", "autorizar_sobrante", "cfdi"):
        operation_field = "custom_lebaref_regularizacion_operacion" if accion == "regularizar_entrada" else "custom_lebaref_operacion"
        hash_field = "custom_lebaref_regularizacion_huella" if accion == "regularizar_entrada" else "custom_lebaref_huella"
        prior = frappe.db.get_value(TARGETS[accion], {operation_field: token}, ["name", hash_field], as_dict=True, for_update=True)
        if prior:
            frappe.get_doc(TARGETS[accion], prior.name).check_permission("read")
            if prior.get(hash_field) != signature:
                frappe.throw("Este intento ya fue guardado con otros datos. Cierra y abre una nueva operación.")
            return dict(folio=prior.name, tipo=TARGETS[accion], repetida=True)
    if accion == "articulo":
        doc = solicitudes.nuevo_articulo(data)
    elif accion in MASTERS:
        allowed = {"proveedor": ("supplier_name", "supplier_group", "tax_id"), "obra": ("project_name", "company"), "almacen": ("warehouse_name", "company", "parent_warehouse")}[accion]
        doc = frappe.get_doc(dict(doctype=TARGETS[accion], **{key: data.get(key) for key in allowed}))
        if accion == "proveedor":
            doc.supplier_type = "Company"
            proveedores.configurar(doc, data)
    elif accion in ("solicitud", "anexo"):
        doc = solicitudes.crear(data, source)
    elif accion == "convertir_concepto":
        doc = solicitudes.convertir(source, data)
    elif accion == "canalizar_stock":
        doc = surtido.canalizar(source, data)
    elif accion == "surtir":
        doc = surtido.surtir(source, elegidos(data, {r.name for r in source.items}))
    elif accion == "peticion":
        from erpnext.stock.doctype.material_request.material_request import make_request_for_quotation
        doc = make_request_for_quotation(source.name)
        suppliers = list(dict.fromkeys(str(data.get("suppliers", "")).splitlines()))
        for supplier in suppliers:
            if supplier.strip():
                frappe.get_doc("Supplier", supplier.strip()).check_permission("read")
                doc.append("suppliers", {"supplier": supplier.strip(), "send_email": 0})
    elif accion == "oferta":
        from erpnext.buying.doctype.request_for_quotation.request_for_quotation import make_supplier_quotation_from_rfq
        if data.get("supplier") not in [r.supplier for r in source.suppliers]:
            frappe.throw("El proveedor no está invitado en esta petición.")
        rows = data.get("rows", [])
        if not isinstance(rows, list):
            frappe.throw("Envía los renglones de la oferta como una lista.")
        available = {r.name: r for r in source.items}
        seen = set()
        quoted = {}
        for row in rows:
            key = row.get("id") if isinstance(row, dict) else None
            if not isinstance(key, str) or key not in available or key in seen:
                frappe.throw("El renglón no pertenece a la petición o está repetido.")
            seen.add(key)
            rate = row.get("rate")
            if rate is None or (isinstance(rate, str) and not rate.strip()):
                continue
            if isinstance(rate, bool):
                frappe.throw("Escribe un precio numérico válido.")
            rate = numero(rate, zero=True)
            if rate > 0:
                unidades.verificar_referencia(row, unidades.valores(available[key]))
                quoted[key] = (rate, numero(row.get("lead_time_days", 0), zero=True))
        if not quoted:
            frappe.throw("Captura al menos un precio unitario mayor que cero.")

        doc = make_supplier_quotation_from_rfq(source.name, for_supplier=data["supplier"])
        doc.valid_till = data.get("valid_till")
        items = [r for r in doc.items if r.request_for_quotation_item in quoted]
        if {r.request_for_quotation_item for r in items} != quoted.keys():
            frappe.throw("Algunos renglones cotizados ya no están disponibles. Actualiza la petición.")
        for r in items:
            r.rate, r.lead_time_days = quoted[r.request_for_quotation_item]
        doc.set("items", items)
        comercial.configurar_oferta(doc, data)
    elif accion == "autorizar":
        source.check_permission("submit")
        if data.get("confirmar") != "AUTORIZAR":
            frappe.throw("Selecciona AUTORIZAR después de revisar la orden.")
        if source.docstatus == 0:
            source.submit()
        elif source.docstatus != 1:
            frappe.throw("La orden está cancelada.")
        return dict(folio=source.name, tipo=source.doctype)
    elif accion == "recibir":
        from erpnext.buying.doctype.purchase_order.purchase_order import make_purchase_receipt
        available = {r.name: r for r in source.items}
        values = elegidos(data, available)
        for row in data["rows"]:
            if values[row["id"]]:
                unidades.verificar_referencia(row, unidades.valores(available[row["id"]]))
        doc = make_purchase_receipt(source.name)
        kept = []
        for r in doc.items:
            qty = values.get(r.purchase_order_item, 0)
            if not qty:
                continue
            if qty > r.qty:
                frappe.throw("La cantidad excede el saldo por recibir. Actualiza la orden.")
            r.qty = r.received_qty = qty
            kept.append(r)
        if {r.purchase_order_item for r in kept} != {k for k, v in values.items() if v}:
            frappe.throw("Hay renglones sin saldo pendiente.")
        doc.set("items", kept)
        sustituciones.aplicar(doc, data)
        recepciones.configurar_destinos(doc, data)
    elif accion == "autorizar_sustitucion":
        doc = frappe.get_doc(dict(doctype="Lebaref Sustitucion", orden=source.name,
            renglon=data.get("renglon"), sustituto=data.get("sustituto"), cantidad=data.get("cantidad"), motivo=data.get("motivo")))
    elif accion == "entrada_sin_oc":
        doc = recepciones.crear(data)
    elif accion == "regularizar_entrada":
        return recepciones.regularizar(source, data, token, signature)
    elif accion == "entregar":
        if not data.get("project") or data.get("from_warehouse") == data.get("to_warehouse"):
            frappe.throw("Selecciona una obra y dos almacenes distintos.")
        doc = frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Transfer", company=data.get("company"), project=data.get("project"), remarks=data.get("remarks"), items=[dict(item_code=r.get("item_code"), qty=numero(r.get("qty")), s_warehouse=data.get("from_warehouse"), t_warehouse=data.get("to_warehouse"), custom_lebaref_partida=r.get("custom_lebaref_partida")) for r in data.get("rows", [])]))
    elif accion == "sobrante":
        doc = sobrantes.crear(data)
    elif accion == "autorizar_sobrante":
        source.check_permission("submit")
        if not source.custom_lebaref_tipo_sobrante or data.get("confirmar") != "AUTORIZAR":
            frappe.throw("Revisa el sobrante y selecciona AUTORIZAR.")
        if source.docstatus == 0:
            source.submit()
        elif source.docstatus != 1:
            frappe.throw("El sobrante está cancelado.")
        return dict(folio=source.name, tipo=source.doctype)
    elif accion == "consumir":
        doc = consumos.crear(source, data, elegidos(data, {r.name for r in source.items}))
    elif accion == "devolver":
        if source.stock_entry_type != "Material Transfer" or source.custom_lebaref_origen or source.get("custom_lebaref_tipo_sobrante"):
            frappe.throw("Selecciona una entrega original.")
        values = elegidos(data, {r.name for r in source.items})
        returned = devoluciones(source, lock=True)
        items = []
        for r in source.items:
            qty = values.get(r.name, 0)
            if not qty:
                continue
            if qty > r.qty-returned.get(r.name, 0):
                frappe.throw("La devolución excede el saldo de la entrega.")
            items.append(dict(item_code=r.item_code, qty=qty, uom=r.uom, conversion_factor=r.conversion_factor, s_warehouse=r.t_warehouse, t_warehouse=r.s_warehouse, custom_lebaref_partida=r.custom_lebaref_partida, custom_lebaref_renglon=r.name))
        doc = frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Transfer", company=source.company, project=source.project, custom_lebaref_origen=source.name, items=items))
    elif accion == "cfdi":
        return registrar_cfdi(source, data.get("xml", ""))
    doc.custom_lebaref_operacion = token
    doc.custom_lebaref_huella = signature
    doc.insert()
    if accion == "proveedor":
        proveedores.completar(doc)
    if accion not in MASTERS and accion not in ("anexo", "sobrante"):
        doc.submit()
    return dict(folio=doc.name, tipo=doc.doctype)


def registrar_cfdi(receipt, xml):
    if len(xml.encode("utf-8")) > 2_000_000 or "<!DOCTYPE" in xml.upper() or "<!ENTITY" in xml.upper():
        frappe.throw("XML no admitido o mayor de 2 MB.")
    try:
        root = ET.fromstring(xml)
        ns = {"c": "http://www.sat.gob.mx/cfd/4", "t": "http://www.sat.gob.mx/TimbreFiscalDigital"}
        if root.tag != "{http://www.sat.gob.mx/cfd/4}Comprobante" or root.get("Version") != "4.0" or root.get("TipoDeComprobante") != "I":
            raise ValueError()
        stamp = root.find("c:Complemento/t:TimbreFiscalDigital", ns)
        identifier = str(uuid.UUID(stamp.attrib["UUID"])).upper()
        issuer = root.find("c:Emisor", ns).attrib["Rfc"].upper()
        receiver = root.find("c:Receptor", ns).attrib["Rfc"].upper()
        total = numero(root.attrib["Total"])
        currency = root.attrib["Moneda"]
    except (ET.ParseError, KeyError, ValueError, AttributeError):
        frappe.throw("Se necesita un CFDI 4.0 de ingreso con UUID, RFC y total válidos.")
    supplier_rfc = frappe.db.get_value("Supplier", receipt.supplier, "tax_id")
    company_rfc = frappe.db.get_value("Company", receipt.company, "tax_id")
    if not supplier_rfc or not company_rfc or issuer != supplier_rfc.upper() or receiver != company_rfc.upper():
        frappe.throw("Configura y verifica los RFC de la empresa y proveedor; deben coincidir con el XML.")
    signature = hashlib.sha256(xml.encode()).hexdigest()
    if frappe.db.exists("Lebaref CFDI", identifier):
        existing = frappe.get_doc("Lebaref CFDI", identifier)
        existing.check_permission("read")
        if existing.recepcion != receipt.name or existing.huella != signature:
            frappe.throw("Este UUID ya está registrado; no se duplicó.")
        return dict(folio=existing.name, tipo=existing.doctype, repetida=True)
    doc = frappe.get_doc(dict(doctype="Lebaref CFDI", uuid=identifier, recepcion=receipt.name, proveedor=receipt.supplier, rfc_emisor=issuer, rfc_receptor=receiver, moneda=currency, total=total, huella=signature))
    doc.flags.lebaref_xml_validado = True
    doc.insert()
    from frappe.utils.file_manager import save_file
    attachment = save_file(identifier+".xml", xml.encode(), doc.doctype, doc.name, is_private=1)
    doc.db_set("xml", attachment.file_url)
    return dict(folio=doc.name, tipo=doc.doctype)


def validar_archivo(doc, method=None):
    if not doc.flags.get("lebaref_xml_validado"):
        frappe.throw("Registra el CFDI mediante Archivar CFDI para validar su XML.")
