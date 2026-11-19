"""Captura de Material Request y soporte de conceptos sin SKU de catálogo."""
from html import escape, unescape

import frappe
from erpnext.setup.doctype.uom.uom import validate_uoms_are_enabled
from frappe.core.doctype.custom_docperm.custom_docperm import update_custom_docperm
from frappe.permissions import add_permission

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
        warehouse=row.get("warehouse"), project=row.get("project") or None,
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
