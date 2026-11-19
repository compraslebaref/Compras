from collections import defaultdict

import frappe
from erpnext.buying.doctype.supplier_quotation.supplier_quotation import make_purchase_order
from frappe.utils import flt, getdate, today

from lebaref_compras.solicitudes import concepto_visible


def _solicitud(nombre, bloquear=False):
    request = frappe.get_doc("Material Request", nombre, for_update=bloquear)
    request.check_permission("read")
    if request.docstatus != 1 or request.material_request_type != "Purchase" or request.status in ("Stopped", "Cancelled"):
        frappe.throw("Abre una solicitud de compra enviada y vigente.")
    return request


def _renglones_ordenados(request, bloquear=False):
    return set(
        frappe.db.get_values(
            "Purchase Order Item",
            filters={"material_request_item": ["in", [item.name for item in request.items]], "docstatus": ["<", 2]},
            fieldname="material_request_item",
            pluck=True,
            for_update=bloquear,
        )
    )


def _cotizaciones(request):
    rows = {item.name: item for item in request.items}
    quotation_names = frappe.get_all(
        "Supplier Quotation Item",
        filters={"material_request_item": ["in", list(rows)], "docstatus": 1},
        pluck="parent",
        distinct=True,
    )
    if not quotation_names:
        return {}

    visible = frappe.get_list(
        "Supplier Quotation",
        filters={"name": ["in", quotation_names], "company": request.company, "docstatus": 1},
        pluck="name",
        limit_page_length=0,
    )
    result = {}
    current_date = getdate(today())
    for name in visible:
        quote = frappe.get_doc("Supplier Quotation", name)
        if quote.valid_till and getdate(quote.valid_till) < current_date:
            continue
        for item in quote.items:
            origin = rows.get(item.material_request_item)
            if not origin or origin.item_code != item.item_code or not item.conversion_factor or flt(item.rate) <= 0:
                continue
            precision = origin.precision("stock_qty")
            if flt(item.stock_qty, precision) != flt(origin.stock_qty, precision):
                continue
            result[item.name] = (quote, item)
    return result


@frappe.whitelist(methods=["GET"])
def consultar(solicitud):
    request = _solicitud(solicitud)
    ordered = _renglones_ordenados(request)
    options = defaultdict(list)
    origins = {item.name: item for item in request.items}
    for quote, item in _cotizaciones(request).values():
        origin = origins[item.material_request_item]
        options[origin.name].append(
            {
                "id": item.name,
                "cotizacion": quote.name,
                "proveedor": quote.supplier_name or quote.supplier,
                "precio": flt(item.rate) * flt(origin.conversion_factor) / flt(item.conversion_factor),
                "moneda": quote.currency,
                "entrega": item.lead_time_days,
            }
        )
    return {
        "puede_generar": frappe.has_permission("Purchase Order", "create"),
        "renglones": [
            {
                "id": item.name,
                "articulo": concepto_visible(item) or item.item_name,
                "codigo": "" if item.custom_lebaref_concepto else item.item_code,
                "partida": item.custom_lebaref_partida,
                "obra": item.project,
                "cantidad": item.qty,
                "unidad": item.uom,
                "ordenado": item.name in ordered,
                "opciones": options[item.name],
            }
            for item in request.items
        ],
    }


@frappe.whitelist(methods=["POST"])
def generar_ordenes(solicitud, seleccion):
    frappe.has_permission("Purchase Order", "create", throw=True)
    request = _solicitud(solicitud, bloquear=True)
    winners = frappe.parse_json(seleccion)
    if not isinstance(winners, dict) or not winners or not all(isinstance(value, str) for value in winners.values()):
        frappe.throw("Selecciona al menos una cotización válida.")

    ordered = _renglones_ordenados(request, bloquear=True)
    quotes = _cotizaciones(request)
    by_supplier = defaultdict(list)
    for request_row, quote_row in winners.items():
        if request_row in ordered:
            frappe.throw("Uno de los renglones ya tiene una orden activa. Actualiza la comparativa.")
        if quote_row not in quotes or quotes[quote_row][1].material_request_item != request_row:
            frappe.throw("Una cotización seleccionada ya no es válida para ese renglón. Actualiza la comparativa.")
        quote, item = quotes[quote_row]
        by_supplier[quote.supplier].append((quote.name, item.name))

    groups = []
    for entries in by_supplier.values():
        sources = {quote for quote, _ in entries}
        if len(sources) != 1:
            frappe.throw("Elige renglones de una sola cotización vigente por proveedor para conservar sus condiciones.")
        groups.append((entries[0][0], [item for _, item in entries]))

    source_rows = {item.name: item for item in request.items}
    orders = []
    for source, selected in groups:
        order = make_purchase_order(source, args={"filtered_children": selected})
        if {item.supplier_quotation_item for item in order.items} != set(selected):
            frappe.throw("Una cotización cambió durante la operación. Actualiza la comparativa.")
        for item in order.items:
            origin = source_rows[item.material_request_item]
            precision = origin.precision("stock_qty")
            if flt(item.stock_qty, precision) != flt(origin.stock_qty, precision):
                frappe.throw("La cantidad disponible en una cotización cambió. Actualiza la comparativa.")
            if not item.schedule_date:
                item.schedule_date = max(getdate(origin.schedule_date), getdate(today()))
        order.insert()
        orders.append({"folio": order.name, "proveedor": order.supplier_name or order.supplier})
    return orders
