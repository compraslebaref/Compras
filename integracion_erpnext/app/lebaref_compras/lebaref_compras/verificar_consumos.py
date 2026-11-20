"""Entrega, consumo, sobrante y anulación sobre datos ficticios reversibles."""
import uuid
import frappe
from lebaref_compras.operaciones import ejecutar, esquema
from lebaref_compras.consumos import consultar


def verificar_consumos(seed, obra_warehouse):
    frappe.set_user("Administrator")
    base = seed.items[0]
    box = "Caja consumo " + uuid.uuid4().hex[:8]
    frappe.get_doc(dict(doctype="UOM", uom_name=box)).insert()
    sku = ejecutar("articulo", dict(item_code="TEST-CONS-"+uuid.uuid4().hex[:8], item_name="Consumo por caja",
        item_group=frappe.db.get_value("Item", base.item_code, "item_group"), stock_uom=base.stock_uom,
        uom=box, conversion_factor=10), str(uuid.uuid4()))["folio"]
    opening = frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Receipt", company=seed.company,
        items=[dict(item_code=sku, qty=50, t_warehouse=base.warehouse, basic_rate=10)])).insert()
    opening.submit()
    delivery = frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Transfer", company=seed.company,
        project=base.project, items=[dict(item_code=sku, qty=2, uom=box, conversion_factor=10,
            s_warehouse=base.warehouse, t_warehouse=obra_warehouse)])).insert()
    delivery.submit()
    row = delivery.items[0]
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": obra_warehouse}, "actual_qty") == 20
    assert consultar(delivery.name)["rows"][0]["costo_consumido"] == 0
    frappe.set_user("compras@lebaref.test")
    try:
        esquema("consumir", delivery.name)
    except frappe.PermissionError:
        pass
    else:
        raise AssertionError("Compras registró un consumo sin permiso de inventario")
    frappe.set_user("almacen@lebaref.test")
    values = dict(remarks="Instalación en obra", rows=[dict(id=row.name, qty=0.5)])
    token = str(uuid.uuid4())
    consumption = ejecutar("consumir", values, token, delivery.name)["folio"]
    assert ejecutar("consumir", values, token, delivery.name)["repetida"]
    doc = frappe.get_doc("Stock Entry", consumption)
    assert doc.purpose == "Material Issue" and doc.project == base.project
    assert doc.items[0].transfer_qty == 5
    info = consultar(delivery.name)
    assert info["rows"][0]["consumido"] == 0.5 and info["rows"][0]["saldo"] == 1.5
    if not frappe.has_permission("GL Entry", "read"):
        assert "costo_consumido" not in info["rows"][0]
    frappe.set_user("Administrator")
    assert consultar(delivery.name)["rows"][0]["costo_consumido"] == 50
    expense = frappe.db.sql("""select sum(g.debit-g.credit) from `tabGL Entry` g
        inner join `tabAccount` a on a.name=g.account
        where g.voucher_type='Stock Entry' and g.voucher_no=%s and g.is_cancelled=0
        and a.root_type='Expense' and g.project=%s""", (consumption, base.project))[0][0]
    assert expense == 50
    # Additional physical stock must not permit overusing this delivery's allocation.
    extra = frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Receipt", company=seed.company,
        items=[dict(item_code=sku, qty=10, t_warehouse=obra_warehouse, basic_rate=10)])).insert()
    extra.submit()
    frappe.set_user("almacen@lebaref.test")
    returned = ejecutar("devolver", dict(rows=[dict(id=row.name, qty=1)]), str(uuid.uuid4()), delivery.name)["folio"]
    info = consultar(delivery.name)["rows"][0]
    assert info["devuelto"] == 1 and info["saldo"] == 0.5
    assert esquema("devolver", delivery.name)["rows"][0]["pending"] == 0.5
    for tamper in ("cantidad", "almacen"):
        invalid = frappe.copy_doc(doc)
        invalid.custom_lebaref_origen = delivery.name
        invalid.items[0].custom_lebaref_renglon = row.name
        if tamper == "cantidad":
            invalid.items[0].qty = 1
        else:
            invalid.items[0].s_warehouse = base.warehouse
        try:
            invalid.insert()
        except frappe.ValidationError:
            pass
        else:
            raise AssertionError("La ruta nativa admitió un consumo vinculado alterado")
    for action, data in (("consumir", dict(values, rows=[dict(id=row.name, qty=1)])),
                         ("devolver", dict(rows=[dict(id=row.name, qty=1)]))):
        try:
            ejecutar(action, data, str(uuid.uuid4()), delivery.name)
        except frappe.ValidationError:
            pass
        else:
            raise AssertionError("Se excedió el saldo de la entrega con existencias de otra procedencia")
    frappe.set_user("Administrator")
    try:
        delivery.cancel()
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se anuló una entrega con movimientos vigentes")
    doc.cancel()
    info = consultar(delivery.name)["rows"][0]
    assert info["consumido"] == 0 and info["saldo"] == 1
    assert info["costo_consumido"] == 0
    final = ejecutar("consumir", dict(remarks="Consumo confirmado tras corrección", rows=[dict(id=row.name, qty=1)]),
        str(uuid.uuid4()), delivery.name)["folio"]
    info = consultar(delivery.name)["rows"][0]
    assert info["consumido"] == 1 and info["saldo"] == 0 and info["costo_consumido"] == 100
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": obra_warehouse}, "actual_qty") == 10
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 40
