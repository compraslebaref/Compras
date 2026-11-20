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


def verificar_destinos(seed, supplier, obra_warehouse):
    frappe.set_user("autoriza@lebaref.test")
    base = seed.items[0]
    group = frappe.db.get_value("Item", base.item_code, "item_group")
    common = dict(item_group=group, stock_uom=base.stock_uom)
    material = ejecutar("articulo", dict(common, item_code="TEST-OBR-" + uuid.uuid4().hex[:8],
        item_name="Material directo a obra"), str(uuid.uuid4()))["folio"]
    service = ejecutar("articulo", dict(common, item_code="TEST-SRV-" + uuid.uuid4().hex[:8],
        item_name="Servicio de instalación", tipo_articulo="Servicio"), str(uuid.uuid4()))["folio"]
    assert frappe.db.get_value("Item", service, "is_stock_item") == 0
    assert frappe.db.get_value("Item", material, "is_stock_item") == 1
    assert {f["name"]: f["value"] for f in esquema("articulo", service)["fields"]}["tipo_articulo"] == "Servicio"
    frappe.set_user("Administrator")
    order = frappe.get_doc(dict(doctype="Purchase Order", company=seed.company, supplier=supplier,
        transaction_date=today(), schedule_date=today(), items=[
            dict(item_code=material, qty=3, rate=100, warehouse=base.warehouse, project=base.project),
            dict(item_code=service, qty=1, rate=150, project=base.project)])).insert()
    order.submit()
    material_row, service_row = order.items
    data = dict(recibido_por="Ana de prueba", supplier_delivery_note="REM-ALM03",
        motivo_destino="Entrega del proveedor directamente en obra", rows=[
            dict(id=material_row.name, qty=2, warehouse=obra_warehouse), dict(id=service_row.name, qty=1)])
    frappe.set_user("almacen@lebaref.test")
    schema = esquema("recibir", order.name)
    assert next(r for r in schema["rows"] if r["id"] == service_row.name)["warehouse"] == ""
    for invalid in (dict(data, motivo_destino=""),
                    dict(data, rows=[dict(id=service_row.name, qty=1, warehouse=obra_warehouse)]),
                    dict(data, rows=[dict(id=material_row.name, qty=2, warehouse="All Warehouses - " + frappe.get_cached_value("Company", seed.company, "abbr"))])):
        try:
            ejecutar("recibir", invalid, str(uuid.uuid4()), order.name)
        except frappe.ValidationError:
            pass
        else:
            raise AssertionError("Se aceptó un destino de recepción inválido")
    token = str(uuid.uuid4())
    receipt = ejecutar("recibir", data, token, order.name)["folio"]
    assert ejecutar("recibir", data, token, order.name)["repetida"]
    doc = frappe.get_doc("Purchase Receipt", receipt)
    assert doc.custom_lebaref_recibido_por == "Ana de prueba" and doc.supplier_delivery_note == "REM-ALM03"
    assert data["motivo_destino"] in doc.remarks
    stock_row = next(r for r in doc.items if r.item_code == material)
    service_received = next(r for r in doc.items if r.item_code == service)
    assert stock_row.warehouse == obra_warehouse and stock_row.project == base.project
    assert service_received.purchase_order_item == service_row.name
    assert frappe.db.get_value("Bin", {"item_code": material, "warehouse": obra_warehouse}, "actual_qty") == 2
    assert not frappe.db.get_value("Bin", {"item_code": material, "warehouse": base.warehouse}, "actual_qty")
    assert not frappe.db.exists("Stock Ledger Entry", {"voucher_no": receipt, "item_code": service})
    assert not frappe.db.exists("Bin", {"item_code": service})
    order.reload()
    assert order.items[0].received_qty == 2 and order.items[1].received_qty == 1
    # Existing callers can omit every new field and receive the remainder in the original warehouse.
    ejecutar("recibir", {"rows": [dict(id=material_row.name, qty=1)]}, str(uuid.uuid4()), order.name)
    assert frappe.db.get_value("Bin", {"item_code": material, "warehouse": base.warehouse}, "actual_qty") == 1
    assert frappe.db.get_value("Bin", {"item_code": material, "warehouse": obra_warehouse}, "actual_qty") == 2
    assert frappe.db.get_value("Purchase Order", order.name, "per_received") == 100
