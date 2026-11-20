"""Casos ALM-02 ejecutados dentro de la transacción reversible de integración."""
import uuid
import frappe
from frappe.utils import today
from lebaref_compras.operaciones import ejecutar, esquema


def verificar_recepciones(seed, supplier):
    frappe.set_user("Administrator")
    base = seed.items[0]
    item = frappe.get_doc(dict(doctype="Item", item_code="TEST-ANT-" + uuid.uuid4().hex[:8],
        item_name="Recepción anticipada prueba", item_group=frappe.db.get_value("Item", base.item_code, "item_group"),
        stock_uom=base.stock_uom, is_stock_item=1, is_purchase_item=1)).insert()
    data = dict(company=seed.company, supplier=supplier, remarks="Entrega antes de autorización de compra",
        rows=[dict(item_code=item.name, qty=2, rate=100, warehouse=base.warehouse, project=base.project)])
    token = str(uuid.uuid4())
    receipt = ejecutar("entrada_sin_oc", data, token)["folio"]
    assert ejecutar("entrada_sin_oc", data, token)["repetida"]
    doc = frappe.get_doc("Purchase Receipt", receipt)
    assert doc.docstatus == 1 and doc.custom_lebaref_por_regularizar
    assert not doc.items[0].purchase_order
    order = frappe.get_doc(dict(doctype="Purchase Order", company=seed.company, supplier=supplier,
        transaction_date=today(), schedule_date=today(), items=[dict(item_code=item.name,
            qty=3, rate=100, warehouse=base.warehouse, project=base.project)])).insert()
    order.submit()
    row = doc.items[0]
    values = dict(purchase_order=order.name, motivo="Orden autorizada tras entrega física",
        rows=[dict(id=row.name, purchase_order_item=order.items[0].name)])
    assert esquema("regularizar_entrada", receipt)["rows"][0]["id"] == row.name
    # Material can already have been consumed when the purchase order arrives.
    issue = frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Issue", company=seed.company,
        items=[dict(item_code=item.name, qty=1, s_warehouse=base.warehouse)])).insert()
    issue.submit()
    before_stock = frappe.db.get_value("Bin", {"item_code": item.name, "warehouse": base.warehouse}, "actual_qty")
    before_sle = frappe.db.count("Stock Ledger Entry", {"voucher_no": receipt})
    before_gl = frappe.db.count("GL Entry", {"voucher_no": receipt})
    assert before_stock == 1 and before_sle > 0
    for invalid in (dict(values, rows=[dict(id=row.name, purchase_order_item="ajeno")]),
                    dict(values, rows=[]), dict(values, motivo=""), dict(values, rows=values["rows"] * 2),
                    dict(values, rows=[dict(id=row.name, purchase_order_item=[])])):
        try:
            ejecutar("regularizar_entrada", invalid, str(uuid.uuid4()), receipt)
        except frappe.ValidationError:
            assert not frappe.db.get_value("Purchase Receipt Item", row.name, "purchase_order")
        else:
            raise AssertionError("Se aceptó una regularización inválida")
    frappe.set_user("almacen@lebaref.test")
    try:
        ejecutar("regularizar_entrada", values, str(uuid.uuid4()), receipt)
    except frappe.PermissionError:
        pass
    else:
        raise AssertionError("Almacén regularizó una compra sin permiso de escritura de orden")
    frappe.set_user("Administrator")
    operation = str(uuid.uuid4())
    assert ejecutar("regularizar_entrada", values, operation, receipt)["folio"] == receipt
    assert ejecutar("regularizar_entrada", values, operation, receipt)["repetida"]
    assert ejecutar("entrada_sin_oc", data, token)["repetida"]
    order.reload()
    doc.reload()
    assert doc.items[0].purchase_order == order.name and not doc.custom_lebaref_por_regularizar
    assert order.items[0].received_qty == 2 and 66 < order.per_received < 67
    assert frappe.db.get_value("Bin", {"item_code": item.name, "warehouse": base.warehouse}, "actual_qty") == before_stock
    assert frappe.db.count("Stock Ledger Entry", {"voucher_no": receipt}) == before_sle
    assert frappe.db.count("GL Entry", {"voucher_no": receipt}) == before_gl
    pending = esquema("recibir", order.name)["rows"][0]
    assert pending["pending"] == 1
    try:
        ejecutar("regularizar_entrada", values, str(uuid.uuid4()), receipt)
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se regularizó dos veces la misma recepción")
    ejecutar("recibir", {"rows": [dict(id=order.items[0].name, qty=1)]}, str(uuid.uuid4()), order.name)
    assert frappe.db.get_value("Bin", {"item_code": item.name, "warehouse": base.warehouse}, "actual_qty") == 2
    assert frappe.db.get_value("Purchase Order", order.name, "per_received") == 100
