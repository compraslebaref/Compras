"""Anexos, enmiendas y surtido vinculados con persistencia nativa."""
import uuid

import frappe
from frappe.utils import add_days, today

from lebaref_compras.operaciones import ejecutar, esquema
from lebaref_compras.solicitudes import ARTICULO_LIBRE


def verificar_solicitudes(seed, supplier, destination):
    base = seed.items[0]
    schedule = add_days(today(), 7)
    sku = "TEST-STOCK-" + uuid.uuid4().hex[:8]
    item_data = {"item_code": sku, "item_name": "Pieza de surtido de prueba",
                 "item_group": frappe.db.get_value("Item", base.item_code, "item_group"), "stock_uom": base.stock_uom}
    frappe.set_user("autoriza@lebaref.test")
    item_code = ejecutar("articulo", item_data, str(uuid.uuid4()))["folio"]
    frappe.set_user("Administrator")
    receipt = frappe.get_doc({
        "doctype": "Stock Entry", "company": seed.company, "stock_entry_type": "Material Receipt",
        "items": [{"item_code": item_code, "qty": 10, "t_warehouse": base.warehouse, "basic_rate": 10}],
    })
    receipt.insert().submit()

    frappe.set_user("compras@lebaref.test")
    stock_row = {"item_code": item_code, "qty": 4, "warehouse": base.warehouse,
                 "project": base.project, "custom_lebaref_partida": "Surtido"}
    source_data = {"company": seed.company, "schedule_date": schedule, "title": "Prueba de solicitudes",
                   "solicitante": "Solicitante de prueba", "rows": [stock_row]}
    parent = ejecutar("solicitud", source_data, str(uuid.uuid4()))["folio"]
    assert esquema("anexo", parent)["add_rows"]
    annex_data = {"schedule_date": schedule, "title": "Anexo de prueba", "rows": [dict(stock_row, qty=1)]}
    annex_token = str(uuid.uuid4())
    annex = ejecutar("anexo", annex_data, annex_token, parent)["folio"]
    assert ejecutar("anexo", annex_data, annex_token, parent)["repetida"]
    doc = frappe.get_doc("Material Request", annex)
    assert doc.docstatus == 0 and doc.custom_lebaref_solicitud_origen == parent
    assert doc.custom_lebaref_solicitante == source_data["solicitante"]
    doc.submit()
    assert doc.docstatus == 1 and doc.custom_lebaref_solicitud_origen == parent

    blocked = ejecutar("solicitud", dict(source_data, rows=[{"concepto_libre": "Con RFQ", "qty": 1}]), str(uuid.uuid4()))["folio"]
    blocked_row = frappe.get_doc("Material Request", blocked).items[0]
    ejecutar("peticion", {"suppliers": supplier}, str(uuid.uuid4()), blocked)
    try:
        ejecutar("convertir_concepto", {"renglon": blocked_row.name, "item_code": item_code}, str(uuid.uuid4()), blocked)
    except frappe.ValidationError as error:
        assert "documentos de compra activos" in str(error)
    else:
        raise AssertionError("Se convirtió una solicitud con peticiones activas")
    assert frappe.db.get_value("Material Request", blocked, "docstatus") == 1

    concept = "Pieza por catalogar"
    free_data = dict(source_data, rows=[{"concepto_libre": concept, "qty": 4, "custom_lebaref_partida": "Surtido"}])
    original = ejecutar("solicitud", free_data, str(uuid.uuid4()))["folio"]
    origin = frappe.get_doc("Material Request", original).items[0]
    conversion_data = {"renglon": origin.name, "item_code": item_code, "warehouse": base.warehouse}
    token = str(uuid.uuid4())
    converted = ejecutar("convertir_concepto", conversion_data, token, original)["folio"]
    assert ejecutar("convertir_concepto", conversion_data, token, original)["repetida"]
    doc = frappe.get_doc("Material Request", converted)
    row = doc.items[0]
    assert doc.docstatus == 1 and doc.amended_from == original
    assert row.item_code == item_code and row.qty == 4 and row.custom_lebaref_renglon_origen == origin.name
    old = frappe.get_doc("Material Request", original)
    assert old.docstatus == 2 and old.items[0].item_code == ARTICULO_LIBRE
    assert old.items[0].custom_lebaref_concepto == concept

    new_source = ejecutar("solicitud", free_data, str(uuid.uuid4()))["folio"]
    new_origin = frappe.get_doc("Material Request", new_source).items[0]
    article = dict(item_data, item_code=sku + "-NUEVO")
    new_data = {"renglon": new_origin.name, "articulo": article, "warehouse": base.warehouse}
    try:
        ejecutar("convertir_concepto", new_data, str(uuid.uuid4()), new_source)
    except frappe.PermissionError:
        pass
    else:
        raise AssertionError("El auxiliar creó un SKU sin permiso")
    frappe.set_user("autoriza@lebaref.test")
    new_request = ejecutar("convertir_concepto", new_data, str(uuid.uuid4()), new_source)["folio"]
    new_row = frappe.get_doc("Material Request", new_request).items[0]
    assert frappe.db.get_value("Item", new_row.item_code, "is_stock_item") == 1

    mode = {"modo": "salida", "almacen_origen": base.warehouse}
    token = str(uuid.uuid4())
    issue_request = ejecutar("canalizar_stock", mode, token, converted)["folio"]
    assert ejecutar("canalizar_stock", mode, token, converted)["repetida"]
    transfer_source = ejecutar("solicitud", dict(source_data, rows=[dict(stock_row, qty=2)]), str(uuid.uuid4()))["folio"]
    transfer_request = ejecutar("canalizar_stock", dict(
        mode, modo="transferencia", almacen_destino=destination
    ), str(uuid.uuid4()), transfer_source)["folio"]

    frappe.set_user("almacen@lebaref.test")
    for request, kind, qty, pending, warehouse in (
        (issue_request, "Material Issue", 2, 4, None),
        (transfer_request, "Material Transfer", 2, 2, destination),
    ):
        req = frappe.get_doc("Material Request", request)
        row, = esquema("surtir", request)["rows"]
        assert row["pending"] == pending
        token = str(uuid.uuid4())
        data = {"rows": [{"id": row["id"], "qty": qty}]}
        entry = ejecutar("surtir", data, token, request)["folio"]
        assert ejecutar("surtir", data, token, request)["repetida"]
        move = frappe.get_doc("Stock Entry", entry)
        detail, = move.items
        assert move.docstatus == 1 and move.stock_entry_type == kind
        assert detail.material_request == request and detail.material_request_item == req.items[0].name
        assert detail.s_warehouse == base.warehouse and (detail.t_warehouse or None) == warehouse
        assert detail.custom_lebaref_partida == "Surtido"
        rows = esquema("surtir", request)["rows"]
        assert [r["pending"] for r in rows] == ([pending - qty] if pending > qty else [])
        try:
            ejecutar("surtir", {"rows": [{"id": row["id"], "qty": pending + 1}]}, str(uuid.uuid4()), request)
        except frappe.ValidationError:
            pass
        else:
            raise AssertionError("Se surtió más que el saldo")

    for warehouse, expected in ((base.warehouse, 6), (destination, 2)):
        assert frappe.db.get_value("Bin", {"item_code": item_code, "warehouse": warehouse}, "actual_qty") == expected
