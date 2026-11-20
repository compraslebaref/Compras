"""Recepción anticipada y vinculación posterior sin volver a mover inventario."""
import frappe
from frappe.utils import flt
from lebaref_compras import unidades


def crear(data):
    if not str(data.get("remarks") or "").strip():
        frappe.throw("Indica el motivo de la entrada sin orden.")
    rows = data.get("rows")
    if not isinstance(rows, list) or not rows:
        frappe.throw("Captura al menos un artículo recibido.")
    doc = frappe.get_doc(dict(doctype="Purchase Receipt", company=data.get("company"),
        supplier=data.get("supplier"), remarks=data["remarks"],
        custom_lebaref_por_regularizar=1))
    for row in rows:
        if not isinstance(row, dict):
            frappe.throw("Renglón de recepción inválido.")
        item = frappe.get_doc("Item", row.get("item_code"))
        item.check_permission("read")
        if item.disabled or not item.is_stock_item or not item.is_purchase_item:
            frappe.throw("Selecciona un artículo activo, inventariable y de compra.")
        warehouse = frappe.get_doc("Warehouse", row.get("warehouse"))
        warehouse.check_permission("read")
        if warehouse.company != doc.company or warehouse.is_group or warehouse.disabled:
            frappe.throw("Selecciona un almacén activo de la empresa.")
        units = unidades.datos_articulo(item, row.get("uom"))
        unidades.verificar_referencia(row, units)
        doc.append("items", dict(item_code=item.name, qty=unidades.numero(row.get("qty")),
            rate=unidades.numero(row.get("rate")), warehouse=warehouse.name,
            project=row.get("project"), custom_lebaref_partida=row.get("custom_lebaref_partida"), **units))
    return doc


def regularizar(receipt, data, token, signature):
    receipt.check_permission("write")
    order = frappe.get_doc("Purchase Order", data.get("purchase_order"), for_update=True)
    order.check_permission("read")
    order.check_permission("write")
    if receipt.docstatus != 1 or receipt.is_return or not receipt.custom_lebaref_por_regularizar:
        frappe.throw("Selecciona una recepción confirmada pendiente de regularizar.")
    if frappe.db.exists("Purchase Invoice Item", {"purchase_receipt": receipt.name, "docstatus": ["!=", 2]}) or frappe.db.exists(
        "Purchase Receipt", {"return_against": receipt.name, "docstatus": ["!=", 2]}):
        frappe.throw("Regulariza la entrada antes de crear facturas o devoluciones vinculadas.")
    if order.docstatus != 1 or order.status in ("Closed", "On Hold", "Cancelled"):
        frappe.throw("Selecciona una orden confirmada y abierta.")
    if any(receipt.get(key) != order.get(key) for key in ("supplier", "company", "currency")):
        frappe.throw("La empresa, proveedor y moneda deben coincidir con la recepción.")
    if not str(data.get("motivo") or "").strip():
        frappe.throw("Indica el motivo de la regularización.")
    rows = data.get("rows")
    if not isinstance(rows, list):
        frappe.throw("Envía la relación de renglones como una lista.")
    links = {}
    for row in rows:
        if (not isinstance(row, dict) or not isinstance(row.get("id"), str)
                or not isinstance(row.get("purchase_order_item"), str) or row["id"] in links):
            frappe.throw("Referencia de recepción inválida o repetida.")
        links[row["id"]] = row.get("purchase_order_item")
    if set(links) != {r.name for r in receipt.items}:
        frappe.throw("Relaciona todos los renglones de la recepción.")
    available = {r.name: r for r in order.items}
    quantities = {}
    for row in receipt.items:
        target = available.get(links[row.name])
        if target is None or row.purchase_order or row.purchase_order_item:
            frappe.throw("El renglón de orden es ajeno o la recepción ya tiene orden.")
        for key in ("item_code", "uom", "project"):
            if (row.get(key) or "") != (target.get(key) or ""):
                frappe.throw("Artículo, unidad y obra deben coincidir con la orden.")
        for key in ("conversion_factor", "rate"):
            if flt(row.get(key), row.precision(key)) != flt(target.get(key), row.precision(key)):
                frappe.throw("El precio y el factor deben coincidir con la recepción original.")
        quantities[target.name] = quantities.get(target.name, 0) + row.received_qty
        row.purchase_order = order.name
        row.purchase_order_item = target.name
        row.material_request = target.material_request
        row.material_request_item = target.material_request_item
    for key, qty in quantities.items():
        target = available[key]
        if flt(qty + target.received_qty - target.qty, target.precision("qty")) > 0:
            frappe.throw("La recepción excede el saldo de la orden.")
    receipt.validate_with_previous_doc()
    receipt.check_purchase_order_on_hold_or_close("purchase_order")
    # Only provenance changes. Native aggregators recalculate purchase/request balances.
    # Stock and accounting ledgers retain the original receipt and valuation.
    frappe.db.savepoint("regularizar_recepcion")
    try:
        for row in receipt.items:
            for key in ("purchase_order", "purchase_order_item", "material_request", "material_request_item"):
                row.db_set(key, row.get(key), update_modified=False)
        receipt.update_prevdoc_status()
        order.reload()
        order.update_ordered_qty()
        receipt.db_set({"custom_lebaref_por_regularizar": 0,
            "custom_lebaref_regularizacion_operacion": token,
            "custom_lebaref_regularizacion_huella": signature})
        receipt.add_comment("Comment", text=frappe.utils.escape_html(
            f"Regularizada con {order.name}. Motivo: {data['motivo']}"))
        order.add_comment("Comment", text=frappe.utils.escape_html(f"Recepción anticipada vinculada: {receipt.name}"))
    except Exception:
        frappe.db.rollback(save_point="regularizar_recepcion")
        raise
    return dict(folio=receipt.name, tipo=receipt.doctype)


def configurar_destinos(doc, data):
    rows = {r["id"]: r for r in data["rows"]}
    changed = []
    for item in doc.items:
        row = rows[item.purchase_order_item]
        stock = frappe.get_cached_value("Item", item.item_code, "is_stock_item")
        if not stock:
            if row.get("warehouse"):
                frappe.throw("Deja vacío el almacén para recibir un servicio.")
            item.warehouse = None
            continue
        destination = row.get("warehouse") or item.warehouse
        if destination == item.warehouse:
            continue
        if not item.project:
            frappe.throw("La orden debe indicar la obra antes de cambiar el almacén de recepción.")
        warehouse = frappe.get_doc("Warehouse", destination)
        warehouse.check_permission("read")
        if warehouse.company != doc.company or warehouse.disabled or warehouse.is_group:
            frappe.throw("Selecciona un almacén activo de detalle de la misma empresa.")
        project = frappe.get_doc("Project", item.project)
        project.check_permission("read")
        if project.company != doc.company:
            frappe.throw("La obra debe pertenecer a la empresa de la recepción.")
        changed.append(f"{item.item_code}: {item.warehouse or ''} → {warehouse.name}")
        item.warehouse = warehouse.name
    if changed:
        motivo = str(data.get("motivo_destino") or "").strip()
        if not motivo:
            frappe.throw("Indica el motivo para recibir en un almacén distinto al de la orden.")
        doc.remarks = "\n".join([doc.remarks or "", "Recepción directa. " + motivo, *changed]).strip()
    doc.custom_lebaref_recibido_por = data.get("recibido_por") or ""
    if data.get("supplier_delivery_note"):
        doc.supplier_delivery_note = data["supplier_delivery_note"]
