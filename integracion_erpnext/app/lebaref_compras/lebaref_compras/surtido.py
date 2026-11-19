"""Surtido físico a partir de Material Request y Stock Entry nativos."""
import frappe
from erpnext.stock.doctype.material_request.material_request import make_stock_entry
from frappe.utils import flt

from lebaref_compras import solicitudes


TIPOS = ("Material Issue", "Material Transfer")


def validar(source):
    solicitudes.validar_vigente(source, TIPOS)
    if source.work_order or source.job_card:
        frappe.throw("Usa el flujo de manufactura para surtir esta solicitud.")


def pendiente(row):
    precision = row.precision("ordered_qty")
    stock_qty = flt(row.stock_qty, precision)
    ordered = flt(row.ordered_qty, precision)
    return flt(max(0, stock_qty - ordered) / row.conversion_factor, row.precision("qty"))


def surtir(source, selections):
    validar(source)
    entry = make_stock_entry(source.name)
    items = []
    request_rows = {r.name: r for r in source.items}
    for row in entry.items:
        qty = selections.get(row.material_request_item, 0)
        if not qty:
            continue
        origin = request_rows[row.material_request_item]
        if qty > pendiente(origin):
            frappe.throw("La cantidad excede el saldo por surtir. Actualiza la solicitud.")
        row.qty = qty
        row.custom_lebaref_partida = origin.custom_lebaref_partida
        row.project = origin.project
        items.append(row)
    if {row.material_request_item for row in items} != {key for key, qty in selections.items() if qty}:
        frappe.throw("Hay renglones sin saldo pendiente de surtir.")
    entry.set("items", items)
    return entry


def _almacen(name, company):
    if not isinstance(name, str) or not name.strip():
        frappe.throw("Selecciona un almacén válido.")
    doc = frappe.get_doc("Warehouse", name)
    doc.check_permission("read")
    if doc.disabled or doc.is_group or doc.company != company:
        frappe.throw("Selecciona un almacén operativo de la empresa de la solicitud.")
    return doc.name


def canalizar(source, data):
    order = solicitudes.enmienda(source)
    mode = data.get("modo")
    if mode not in ("salida", "transferencia"):
        frappe.throw("Selecciona salida o transferencia desde stock.")
    warehouse = _almacen(data.get("almacen_origen"), source.company)
    destination = None
    if mode == "transferencia":
        destination = _almacen(data.get("almacen_destino"), source.company)
        if destination == warehouse:
            frappe.throw("El almacén de destino debe ser distinto del de origen.")
    for row in order.items:
        item = frappe.get_doc("Item", row.item_code)
        item.check_permission("read")
        if not item.is_stock_item or item.disabled:
            frappe.throw("Canaliza a stock únicamente solicitudes con artículos inventariables activos.")
        row.from_warehouse = warehouse if destination else None
        row.warehouse = destination or warehouse
    order.material_request_type = "Material Transfer" if destination else "Material Issue"
    order.set_from_warehouse = warehouse if destination else None
    order.set_warehouse = destination or warehouse
    source.cancel()
    return order
