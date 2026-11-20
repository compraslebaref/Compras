import uuid
import frappe
from lebaref_compras.operaciones import ejecutar


def verificar_ajustes(seed):
    frappe.set_user("Administrator")
    base = seed.items[0]
    sku = ejecutar("articulo", dict(item_code="TEST-AJU-"+uuid.uuid4().hex[:8], item_name="Conteo prueba",
        item_group=frappe.db.get_value("Item", base.item_code, "item_group"), stock_uom=base.stock_uom), str(uuid.uuid4()))["folio"]
    opening = frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Receipt", company=seed.company,
        items=[dict(item_code=sku, qty=10, t_warehouse=base.warehouse, basic_rate=20)])).insert()
    opening.submit()
    common = dict(company=seed.company, motivo="Conteo físico verificado", referencia="CONTEO-"+uuid.uuid4().hex[:8], tipo="Conteo",
        rows=[dict(item_code=sku, warehouse=base.warehouse, qty=12)])
    token = str(uuid.uuid4())
    draft = ejecutar("ajuste", common, token)["folio"]
    assert ejecutar("ajuste", common, token)["repetida"]
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 10
    frappe.set_user("almacen@lebaref.test")
    try:
        ejecutar("autorizar_ajuste", {"confirmar":"AUTORIZAR"}, str(uuid.uuid4()), draft)
    except frappe.PermissionError:
        pass
    else:
        raise AssertionError("Auxiliar autorizó ajuste")
    frappe.set_user("Administrator")
    ejecutar("autorizar_ajuste", {"confirmar":"AUTORIZAR"}, str(uuid.uuid4()), draft)
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 12
    try:
        ejecutar("ajuste", dict(common,tipo="Merma",referencia="VALOR-"+uuid.uuid4().hex[:8],
            rows=[dict(item_code=sku,warehouse=base.warehouse,qty=9,valuation_rate=30)]),str(uuid.uuid4()))
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se mezcló merma con revaluación")
    loss = ejecutar("ajuste", dict(common, tipo="Merma", referencia="MERMA-"+uuid.uuid4().hex[:8],
        rows=[dict(item_code=sku, warehouse=base.warehouse, qty=9)]), str(uuid.uuid4()))["folio"]
    ejecutar("autorizar_ajuste", {"confirmar":"AUTORIZAR"}, str(uuid.uuid4()), loss)
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 9
    frappe.get_doc("Stock Reconciliation", loss).cancel()
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 12
    stale = ejecutar("ajuste", dict(common, referencia="CAMBIO-"+uuid.uuid4().hex[:8],
        rows=[dict(item_code=sku, warehouse=base.warehouse, qty=8)]), str(uuid.uuid4()))["folio"]
    extra = frappe.copy_doc(opening)
    extra.items[0].qty = 1
    extra.insert()
    extra.submit()
    try:
        ejecutar("autorizar_ajuste", {"confirmar":"AUTORIZAR"}, str(uuid.uuid4()), stale)
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se autorizó conteo desactualizado")
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 13
