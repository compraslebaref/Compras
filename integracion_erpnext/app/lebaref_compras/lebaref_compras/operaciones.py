"""API del cliente Windows. Siempre aplica permisos y validación de ERPNext."""
import hashlib
import json
import math
import re
import uuid
from xml.etree import ElementTree as ET

import frappe
from frappe.utils import today, add_days, flt

TARGETS = {"solicitud": "Material Request", "peticion": "Request for Quotation",
           "oferta": "Supplier Quotation", "autorizar": "Purchase Order",
           "recibir": "Purchase Receipt", "entregar": "Stock Entry", "devolver": "Stock Entry",
           "cfdi": "Lebaref CFDI", "articulo": "Item", "proveedor": "Supplier", "obra": "Project", "almacen": "Warehouse"}
MASTERS = {"articulo", "proveedor", "obra", "almacen"}
SOURCES = {"peticion": "Material Request", "oferta": "Request for Quotation",
           "autorizar": "Purchase Order", "recibir": "Purchase Order",
           "devolver": "Stock Entry", "cfdi": "Purchase Receipt"}


def permiso(accion):
    if accion not in TARGETS:
        frappe.throw("Operación desconocida.")
    dt = TARGETS[accion]
    for right in (("submit",) if accion == "autorizar" else (("create",) if accion == "cfdi" or accion in MASTERS else ("create", "submit"))):
        if not frappe.has_permission(dt, right):
            frappe.throw("Tu cuenta no tiene permiso para esta operación.", frappe.PermissionError)


def origen(accion, folio, lock=False):
    if accion not in SOURCES:
        return None
    doc = frappe.get_doc(SOURCES[accion], folio, for_update=lock)
    doc.check_permission("read")
    if accion != "autorizar" and doc.docstatus != 1:
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
    permiso(accion)
    doc = origen(accion, folio)
    fields, columns, rows = [], [], []
    note = "Revisa los datos: Guardar confirma el documento en ERPNext."
    if accion == "articulo":
        fields = [campo("item_code", "Código único"), campo("item_name", "Nombre"), campo("item_group", "Grupo", choices=catalogo("Item Group", {"is_group": 0})), campo("stock_uom", "Unidad de inventario", choices=catalogo("UOM"))]
        note = "Crea un artículo con control de inventario. Series, lotes y activos requieren configuración administrativa adicional."
    elif accion == "proveedor":
        fields = [campo("supplier_name", "Nombre"), campo("supplier_group", "Grupo", choices=catalogo("Supplier Group", {"is_group": 0})), campo("tax_id", "RFC")]
        note = "Alta de proveedor. Verifica el RFC para vincular sus XML posteriormente."
    elif accion == "obra":
        fields = [campo("project_name", "Nombre de obra"), campo("company", "Empresa", choices=catalogo("Company"))]
        note = "Crea la obra como proyecto de ERPNext."
    elif accion == "almacen":
        fields = [campo("warehouse_name", "Nombre del almacén"), campo("company", "Empresa", choices=catalogo("Company")), campo("parent_warehouse", "Almacén agrupador", choices=catalogo("Warehouse", {"is_group": 1}))]
        note = "Crea un almacén operativo bajo el agrupador de su empresa."
    elif accion == "solicitud":
        fields = [campo("company", "Empresa", choices=catalogo("Company")), campo("schedule_date", "Fecha requerida", add_days(today(), 7), kind="date"), campo("title", "Descripción")]
        columns = [columna("item_code", "Artículo", choices=catalogo("Item", {"disabled": 0})), columna("qty", "Cantidad"), columna("warehouse", "Almacén", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0})), columna("project", "Obra", choices=catalogo("Project")), columna("custom_lebaref_partida", "Partida")]
    elif accion == "peticion":
        fields = [campo("suppliers", "Proveedores (uno por línea)", kind="multiline")]
        note = "Copia nombres exactos del catálogo de proveedores. Se registra la petición; no se envían correos."
    elif accion == "oferta":
        fields = [campo("supplier", "Proveedor", choices=[s.supplier for s in doc.suppliers]), campo("valid_till", "Vigente hasta", add_days(today(), 30), kind="date")]
        columns = [columna("id", "Referencia", False), columna("item_code", "Artículo", False), columna("qty", "Cantidad", False), columna("rate", "Precio unitario sin impuestos"), columna("lead_time_days", "Días de entrega")]
        rows = [dict(id=r.name, item_code=r.item_code, qty=r.qty, rate=0, lead_time_days=0) for r in doc.items]
        note = "Registra todos los precios en la moneda de la empresa. Impuestos/condiciones especiales se revisan en ERPNext antes de autorizar."
    elif accion == "autorizar":
        doc.check_permission("submit")
        fields = [campo("confirmar", "Confirmación", choices=["", "AUTORIZAR"])]
        columns = [columna(k, label, False) for k, label in (("item_code", "Artículo"), ("qty", "Cantidad"), ("rate", "Precio"), ("project", "Obra"), ("custom_lebaref_partida", "Partida"))]
        rows = [r.as_dict() for r in doc.items]
        note = f"{doc.name} | {doc.supplier} | Total {doc.grand_total} {doc.currency}. AUTORIZAR confirma la orden."
    elif accion == "recibir":
        columns = [columna("id", "Referencia", False), columna("item_code", "Artículo", False), columna("pending", "Pendiente", False), columna("qty", "Recibir ahora")]
        rows = [dict(id=r.name, item_code=r.item_code, pending=max(0, r.qty-r.received_qty), qty=0) for r in doc.items if r.qty > r.received_qty]
        note = "Escribe solo lo recibido físicamente. Cero omite el renglón. Se conserva el almacén de la orden."
    elif accion == "entregar":
        fields = [campo("company", "Empresa", choices=catalogo("Company")), campo("from_warehouse", "Almacén origen", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0})), campo("to_warehouse", "Almacén de obra", choices=catalogo("Warehouse", {"is_group": 0, "disabled": 0})), campo("project", "Obra", choices=catalogo("Project")), campo("remarks", "Recibe / referencia")]
        columns = [columna("item_code", "Artículo", choices=catalogo("Item", {"is_stock_item": 1, "disabled": 0})), columna("qty", "Cantidad (unidad de inventario)"), columna("custom_lebaref_partida", "Partida")]
        note = "Transferencia de inventario: la existencia pasa del almacén central al almacén de obra; no representa consumo."
    elif accion == "devolver":
        if doc.stock_entry_type != "Material Transfer" or doc.custom_lebaref_origen:
            frappe.throw("Selecciona una entrega original de tipo Transferencia de materiales.")
        columns = [columna("id", "Referencia", False), columna("item_code", "Artículo", False), columna("pending", "Máximo retornable", False), columna("qty", "Devolver ahora")]
        returned = devoluciones(doc)
        rows = [dict(id=r.name, item_code=r.item_code, pending=r.qty-returned.get(r.name, 0), qty=0) for r in doc.items]
        note = "Retorno de sobrante al almacén original. El sistema verifica existencias y devoluciones anteriores."
    elif accion == "cfdi":
        fields = [campo("xml", "Archivo XML CFDI", kind="file")]
        note = "Archiva el XML contra esta recepción, valida UUID y RFC, y evita duplicados. No crea póliza/factura contable ni verifica el estado SAT."
    return dict(fields=fields, columns=columns, rows=rows, add_rows=accion in ("solicitud", "entregar"), note=note)


def numero(value, zero=False):
    try:
        result = float(value)
    except (ValueError, TypeError):
        frappe.throw("Escribe una cantidad o precio numérico válido.")
    if not math.isfinite(result) or result < 0 or (not zero and result == 0):
        frappe.throw("La cantidad o precio debe ser positivo.")
    return result


def elegidos(data, available):
    result = {}
    for row in data.get("rows", []):
        key = row.get("id")
        if key not in available or key in result:
            frappe.throw("El renglón no pertenece al documento o está repetido.")
        result[key] = numero(row.get("qty", 0), zero=True)
    if not any(result.values()):
        frappe.throw("Captura al menos una cantidad mayor que cero.")
    return result


def devoluciones(doc):
    # Internal aggregate only, after checking access to the original transfer.
    result = {}
    for r in frappe.get_all("Stock Entry Detail", filters={"custom_lebaref_renglon": ["in", [i.name for i in doc.items]], "docstatus": 1}, fields=["custom_lebaref_renglon", "qty"]):
        result[r.custom_lebaref_renglon] = result.get(r.custom_lebaref_renglon, 0) + r.qty
    return result


@frappe.whitelist(methods=["POST"])
def ejecutar(accion, datos, operacion, folio=""):
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
    if accion not in ("autorizar", "cfdi"):
        prior = frappe.db.get_value(TARGETS[accion], {"custom_lebaref_operacion": token}, ["name", "custom_lebaref_huella"], as_dict=True, for_update=True)
        if prior:
            frappe.get_doc(TARGETS[accion], prior.name).check_permission("read")
            if prior.custom_lebaref_huella != signature:
                frappe.throw("Este intento ya fue guardado con otros datos. Cierra y abre una nueva operación.")
            return dict(folio=prior.name, tipo=TARGETS[accion], repetida=True)
    if accion in MASTERS:
        allowed = {"articulo": ("item_code", "item_name", "item_group", "stock_uom"), "proveedor": ("supplier_name", "supplier_group", "tax_id"), "obra": ("project_name", "company"), "almacen": ("warehouse_name", "company", "parent_warehouse")}[accion]
        doc = frappe.get_doc(dict(doctype=TARGETS[accion], **{key: data.get(key) for key in allowed}))
        if accion == "articulo":
            doc.is_stock_item = 1
        if accion == "proveedor":
            doc.supplier_type = "Company"
    elif accion == "solicitud":
        items = []
        for r in data.get("rows", []):
            items.append(dict(item_code=r.get("item_code"), qty=numero(r.get("qty")), warehouse=r.get("warehouse"), project=r.get("project") or None, custom_lebaref_partida=r.get("custom_lebaref_partida"), schedule_date=data.get("schedule_date")))
        doc = frappe.get_doc(dict(doctype="Material Request", material_request_type="Purchase", company=data.get("company"), title=data.get("title"), transaction_date=today(), schedule_date=data.get("schedule_date"), items=items))
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
        doc = make_supplier_quotation_from_rfq(source.name, for_supplier=data["supplier"])
        doc.valid_till = data.get("valid_till")
        values = {r.get("id"): r for r in data.get("rows", [])}
        if len(values) != len(data.get("rows", [])) or set(values) != {r.name for r in source.items}:
            frappe.throw("Cotiza todos los renglones de la petición una sola vez.")
        for r in doc.items:
            value = values[r.request_for_quotation_item]
            r.rate = numero(value.get("rate"))
            r.lead_time_days = numero(value.get("lead_time_days", 0), zero=True)
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
        values = elegidos(data, {r.name for r in source.items})
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
    elif accion == "entregar":
        if not data.get("project") or data.get("from_warehouse") == data.get("to_warehouse"):
            frappe.throw("Selecciona una obra y dos almacenes distintos.")
        doc = frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Transfer", company=data.get("company"), project=data.get("project"), remarks=data.get("remarks"), items=[dict(item_code=r.get("item_code"), qty=numero(r.get("qty")), s_warehouse=data.get("from_warehouse"), t_warehouse=data.get("to_warehouse"), custom_lebaref_partida=r.get("custom_lebaref_partida")) for r in data.get("rows", [])]))
    elif accion == "devolver":
        if source.stock_entry_type != "Material Transfer" or source.custom_lebaref_origen:
            frappe.throw("Selecciona una entrega original.")
        values = elegidos(data, {r.name for r in source.items})
        returned = devoluciones(source)
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
    if accion not in MASTERS:
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
