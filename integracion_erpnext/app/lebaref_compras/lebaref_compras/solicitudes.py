"""Captura de Material Request y soporte de conceptos sin SKU de catálogo."""
from html import escape, unescape

import frappe
from erpnext.setup.doctype.uom.uom import validate_uoms_are_enabled
from frappe.core.doctype.custom_docperm.custom_docperm import update_custom_docperm
from frappe.permissions import add_permission
from frappe.utils import today

from lebaref_compras import unidades


ARTICULO_LIBRE = "LEBAREF-CONCEPTO-LIBRE"
CAMPOS = (
    ("departamento", "Departamento", "custom_lebaref_departamento"),
    ("solicitante", "Solicitante", "custom_lebaref_solicitante"),
    ("notas", "Notas", "custom_lebaref_notas"),
)


def _texto(data, key):
    value = data.get(key)
    if value is not None and not isinstance(value, str):
        frappe.throw(f"{key}: captura un texto válido.")
    return (value or "").strip()


def departamentos():
    if not (frappe.has_permission("Department", "read") or frappe.has_permission("Department", "select")):
        return []
    return frappe.get_list("Department", filters={"disabled": 0}, pluck="name", order_by="name", limit_page_length=0)


def cabecera(data):
    values = {field: _texto(data, key) or None for key, _, field in CAMPOS}
    department = values["custom_lebaref_departamento"]
    if department:
        doc = frappe.get_doc("Department", department)
        if not (doc.has_permission("read") or doc.has_permission("select")):
            doc.check_permission("select")
        if doc.disabled or doc.company != data.get("company"):
            frappe.throw("Selecciona un departamento activo de la empresa de la solicitud.")
    if values["custom_lebaref_notas"]:
        values["custom_lebaref_notas"] = escape(values["custom_lebaref_notas"])
    return values


def valores_cabecera(doc):
    values = {key: doc.get(field) or "" for key, _, field in CAMPOS}
    values["notas"] = unescape(values["notas"])
    return values


def concepto_visible(row):
    return unescape(row.custom_lebaref_concepto or "")


def renglon(row, schedule_date):
    if not isinstance(row, dict):
        frappe.throw("Captura un renglón válido.")
    sku = _texto(row, "item_code")
    concept = _texto(row, "concepto_libre")
    if bool(sku) == bool(concept) or sku == ARTICULO_LIBRE:
        frappe.throw("Selecciona un artículo de catálogo o escribe un concepto libre.")
    item = dict(
        item_code=sku or ARTICULO_LIBRE, qty=unidades.numero(row.get("qty")),
        warehouse=row.get("warehouse"), from_warehouse=row.get("from_warehouse"), project=row.get("project") or None,
        custom_lebaref_partida=row.get("custom_lebaref_partida"), schedule_date=schedule_date,
    )
    if sku:
        item.update(unidades.datos_solicitud(row, sku))
        return item

    generic = frappe.db.get_value(
        "Item", ARTICULO_LIBRE, ("stock_uom", "is_stock_item", "is_purchase_item", "disabled"), as_dict=True
    )
    if not generic or generic.disabled or generic.is_stock_item or not generic.is_purchase_item:
        frappe.throw("El artículo interno para conceptos libres no está disponible. Revisa la configuración.")
    validate_uoms_are_enabled([generic.stock_uom])
    units = dict(uom=generic.stock_uom, stock_uom=generic.stock_uom, conversion_factor=1)
    unidades.verificar_referencia(row, units)
    # Frappe sanitizes markup in Small Text; entities preserve literal characters.
    description = escape(concept)
    item.update(units, custom_lebaref_concepto=description, description=description)
    return item


def validar_vigente(doc, tipos=None):
    if doc.docstatus != 1 or doc.status in ("Stopped", "Cancelled"):
        frappe.throw("Abre una solicitud confirmada y vigente.")
    if tipos and doc.material_request_type not in tipos:
        frappe.throw("La solicitud no corresponde al tipo de operación seleccionado.")


def crear(data, source=None):
    company = source.company if source else data.get("company")
    material_type = source.material_request_type if source else "Purchase"
    schedule = data.get("schedule_date")
    rows = data.get("rows")
    if not isinstance(rows, list) or not rows:
        frappe.throw("Captura al menos un renglón válido.")
    headers = cabecera(dict(data, company=company))
    if source:
        validar_vigente(source, ("Purchase", "Material Issue", "Material Transfer"))
        headers.update(custom_lebaref_solicitud_origen=source.name)
        for key, _, field in CAMPOS:
            if key not in data:
                headers[field] = source.get(field)
    items = [renglon(row, schedule) for row in rows]
    if material_type in ("Material Issue", "Material Transfer") and any(
        item["item_code"] == ARTICULO_LIBRE for item in items
    ):
        frappe.throw("Selecciona artículos de catálogo para un anexo de stock.")
    return frappe.get_doc(dict(
        doctype="Material Request", material_request_type=material_type, company=company, title=data.get("title"),
        transaction_date=today(), schedule_date=schedule, items=items, **headers,
    ))


def nuevo_articulo(data):
    if not isinstance(data, dict):
        frappe.throw("Captura los datos del artículo.")
    allowed = ("item_code", "item_name", "item_group", "stock_uom")
    item = frappe.get_doc(dict(doctype="Item", is_stock_item=1, **{key: data.get(key) for key in allowed}))
    if item.item_code == ARTICULO_LIBRE:
        frappe.throw("El código está reservado para conceptos libres.")
    unidades.configurar_articulo(item, data)
    return item


def enmienda(source):
    validar_vigente(source, ("Purchase",))
    source.check_permission("cancel")
    source.check_permission("amend")
    for doctype in ("Request for Quotation Item", "Supplier Quotation Item", "Purchase Order Item", "Purchase Receipt Item"):
        if frappe.db.get_value(doctype, {
            "material_request": source.name, "docstatus": ["<", 2],
        }, "name", for_update=True):
            frappe.throw("Hay documentos de compra activos vinculados a esta solicitud. Revisa sus dependencias en ERPNext.")
    replacement = frappe.copy_doc(source)
    replacement.amended_from = source.name
    replacement.status = "Draft"
    for row, origin in zip(replacement.items, source.items, strict=True):
        row.custom_lebaref_renglon_origen = origin.name
    return replacement


def convertir(source, data):
    replacement = enmienda(source)
    row_id = _texto(data, "renglon")
    row = next((r for r in replacement.items if r.custom_lebaref_renglon_origen == row_id), None)
    if not row or row.item_code != ARTICULO_LIBRE or not row.custom_lebaref_concepto:
        frappe.throw("Selecciona un renglón de concepto libre de la solicitud.")
    sku = _texto(data, "item_code")
    article = data.get("articulo")
    if bool(sku) == (article is not None):
        frappe.throw("Selecciona un SKU existente o captura el nuevo artículo.")
    if sku:
        item = frappe.get_doc("Item", sku)
        item.check_permission("read")
    else:
        frappe.has_permission("Item", "create", throw=True)
        item = nuevo_articulo(article).insert()
    if item.disabled or not item.is_stock_item or not item.is_purchase_item:
        frappe.throw("Selecciona un artículo inventariable, activo y habilitado para compras.")
    # The requested quantity is interpreted in the selected SKU's requested or default purchase unit.
    units = unidades.datos_articulo(item, _texto(data, "uom"))
    unidades.verificar_referencia(data, units)
    row.update(dict(
        item_code=item.name, item_name=item.item_name, description=item.description,
        item_group=item.item_group, image=item.image, warehouse=data.get("warehouse") or row.warehouse,
        custom_lebaref_concepto=None, stock_qty=row.qty * units["conversion_factor"], **units,
    ))
    source.cancel()
    return replacement


def configurar():
    # Keep free-form purchases out of inventory and never create an Item per request.
    if not frappe.db.exists("Item", ARTICULO_LIBRE):
        stock_uom = frappe.db.get_single_value("Stock Settings", "stock_uom") or "Nos"
        stock_uom = (
            frappe.db.get_value("UOM", {"name": stock_uom, "enabled": 1})
            or frappe.db.get_value("UOM", {"enabled": 1}, order_by="name")
        )
        item_group = frappe.db.get_single_value("Stock Settings", "item_group")
        item_group = (
            frappe.db.get_value("Item Group", {"name": item_group or "Services", "is_group": 0})
            or frappe.db.get_value("Item Group", {"is_group": 0}, order_by="name")
        )
        if not stock_uom or not item_group:
            frappe.throw("Configura una unidad activa y un grupo de artículos para los conceptos libres.")
        frappe.get_doc({
            "doctype": "Item", "item_code": ARTICULO_LIBRE, "item_name": "Concepto libre Lebaref",
            "stock_uom": stock_uom, "item_group": item_group, "is_stock_item": 0,
            "is_purchase_item": 1, "is_sales_item": 0,
        }).insert(set_name=ARTICULO_LIBRE)

    # A link selector needs access to names, without exposing Department documents.
    for role in ("Purchase User", "Purchase Manager"):
        rule = frappe.db.get_value(
            "Custom DocPerm", {"parent": "Department", "role": role, "permlevel": 0, "if_owner": 0},
            ("name", "read", "select"), as_dict=True,
        )
        if not rule:
            add_permission("Department", role, ptype="select")
        elif not (rule.read or rule.select):
            update_custom_docperm(rule.name, {"select": 1})
