"""Prueba de sustituciones sobre datos ficticios de la suite reversible."""
import uuid
import frappe
from frappe.utils import today
from lebaref_compras.operaciones import ejecutar


def verificar_sustituciones(seed, supplier):
    base = seed.items[0]
    frappe.set_user("autoriza@lebaref.test")
    common = dict(item_group=frappe.db.get_value("Item", base.item_code, "item_group"), stock_uom=base.stock_uom)
    codes = [ejecutar("articulo", dict(common, item_code="TEST-SUS-"+uuid.uuid4().hex[:8], item_name=name), str(uuid.uuid4()))["folio"]
        for name in ("Artículo solicitado", "Sustituto aprobado")]
    frappe.set_user("Administrator")
    order = frappe.get_doc(dict(doctype="Purchase Order", company=seed.company, supplier=supplier,
        transaction_date=today(), schedule_date=today(), items=[dict(item_code=codes[0], qty=3, rate=100,
            warehouse=base.warehouse, project=base.project)])).insert()
    order.submit()
    row = order.items[0]
    values = dict(renglon=row.name, sustituto=codes[1], cantidad=2, motivo="Marca equivalente aprobada por Compras")
    for user in ("almacen@lebaref.test", "compras@lebaref.test"):
        frappe.set_user(user)
        try:
            ejecutar("autorizar_sustitucion", values, str(uuid.uuid4()), order.name)
        except frappe.PermissionError:
            pass
        else:
            raise AssertionError("Un auxiliar autorizó una sustitución")
    frappe.set_user("autoriza@lebaref.test")
    token = str(uuid.uuid4())
    approval = ejecutar("autorizar_sustitucion", values, token, order.name)["folio"]
    assert ejecutar("autorizar_sustitucion", values, token, order.name)["repetida"]
    authorization = frappe.get_doc("Lebaref Sustitucion", approval)
    assert authorization.docstatus == 1 and authorization.original == codes[0] and authorization.precio == 100
    frappe.set_user("almacen@lebaref.test")
    data = dict(rows=[dict(id=row.name, qty=1, sustitucion=approval)])
    operation = str(uuid.uuid4())
    receipt = ejecutar("recibir", data, operation, order.name)["folio"]
    assert ejecutar("recibir", data, operation, order.name)["repetida"]
    doc = frappe.get_doc("Purchase Receipt", receipt)
    assert doc.items[0].item_code == codes[1] and doc.items[0].custom_lebaref_articulo_original == codes[0]
    assert doc.items[0].purchase_order_item == row.name and doc.items[0].rate == 100
    assert frappe.db.get_value("Bin", {"item_code": codes[1], "warehouse": base.warehouse}, "actual_qty") == 1
    assert not frappe.db.get_value("Bin", {"item_code": codes[0], "warehouse": base.warehouse}, "actual_qty")
    try:
        ejecutar("recibir", dict(rows=[dict(id=row.name, qty=2, sustitucion=approval)]), str(uuid.uuid4()), order.name)
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se recibió más sustituto que lo autorizado")
    for tamper in ("sin_autorizacion", "precio"):
        invalid = frappe.copy_doc(doc)
        if tamper == "sin_autorizacion":
            invalid.items[0].custom_lebaref_sustitucion = None
            invalid.items[0].custom_lebaref_articulo_original = None
        else:
            invalid.items[0].rate = 999
        try:
            invalid.insert()
        except frappe.ValidationError:
            pass
        else:
            raise AssertionError("Se admitió una recepción alterada por REST nativo")
    frappe.set_user("autoriza@lebaref.test")
    try:
        authorization.cancel()
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se canceló una autorización con recepción vigente")
    # The ordinary native invoice mapper must keep the approval and bill the substitute.
    frappe.set_user("Administrator")
    from erpnext.stock.doctype.purchase_receipt.purchase_receipt import make_purchase_invoice, make_purchase_return
    invoice = make_purchase_invoice(receipt)
    invoice.insert()
    invoice.submit()
    assert invoice.items[0].item_code == codes[1] and invoice.items[0].custom_lebaref_sustitucion == approval
    assert invoice.items[0].po_detail == row.name and invoice.items[0].purchase_receipt == receipt
    assert not invoice.update_stock
    invoice.cancel()
    returned = make_purchase_return(receipt)
    returned.insert()
    returned.submit()
    assert returned.items[0].custom_lebaref_sustitucion == approval
    assert frappe.db.get_value("Bin", {"item_code": codes[1], "warehouse": base.warehouse}, "actual_qty") == 0
    frappe.set_user("almacen@lebaref.test")
    ejecutar("recibir", dict(rows=[dict(id=row.name, qty=2, sustitucion=approval)]), str(uuid.uuid4()), order.name)
    ejecutar("recibir", dict(rows=[dict(id=row.name, qty=1)]), str(uuid.uuid4()), order.name)
    assert frappe.db.get_value("Purchase Order", order.name, "per_received") == 100
    assert frappe.db.get_value("Purchase Order Item", row.name, "item_code") == codes[0]
    assert frappe.db.get_value("Bin", {"item_code": codes[1], "warehouse": base.warehouse}, "actual_qty") == 2
    assert frappe.db.get_value("Bin", {"item_code": codes[0], "warehouse": base.warehouse}, "actual_qty") == 1
