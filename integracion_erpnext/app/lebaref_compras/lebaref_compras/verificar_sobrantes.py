"""Sobrantes históricos con aprobación y control de duplicación por acta."""
import uuid
import frappe
from lebaref_compras.operaciones import ejecutar


def verificar_sobrantes(seed, obra_warehouse):
    base = seed.items[0]
    frappe.set_user("Administrator")
    sku = ejecutar("articulo", dict(item_code="TEST-SOB-"+uuid.uuid4().hex[:8], item_name="Sobrante prueba",
        item_group=frappe.db.get_value("Item", base.item_code, "item_group"), stock_uom=base.stock_uom), str(uuid.uuid4()))["folio"]
    opening = frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Receipt", company=seed.company,
        items=[dict(item_code=sku, qty=10, t_warehouse=obra_warehouse, basic_rate=20)])).insert()
    opening.submit()
    common = dict(company=seed.company, project=base.project, persona="Ana de prueba", motivo="Sobrante histórico verificado",
        to_warehouse=base.warehouse, referencia=" acta sobrante " + uuid.uuid4().hex[:8])
    data = dict(common, tipo="Traslado", from_warehouse=obra_warehouse, rows=[dict(item_code=sku, qty=3)])
    frappe.set_user("almacen@lebaref.test")
    token = str(uuid.uuid4())
    draft = ejecutar("sobrante", data, token)["folio"]
    assert ejecutar("sobrante", data, token)["repetida"]
    assert frappe.db.get_value("Stock Entry", draft, "docstatus") == 0
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": obra_warehouse}, "actual_qty") == 10
    try:
        ejecutar("autorizar_sobrante", {"confirmar": "AUTORIZAR"}, str(uuid.uuid4()), draft)
    except frappe.PermissionError:
        pass
    else:
        raise AssertionError("Almacén autorizó un sobrante sin permiso de conciliación")
    direct = frappe.get_doc("Stock Entry", draft)
    try:
        direct.submit()
    except frappe.PermissionError:
        pass
    else:
        raise AssertionError("La ruta nativa omitió la autorización del sobrante")
    frappe.set_user("Administrator")
    ejecutar("autorizar_sobrante", {"confirmar": "AUTORIZAR"}, str(uuid.uuid4()), draft)
    ejecutar("autorizar_sobrante", {"confirmar": "AUTORIZAR"}, str(uuid.uuid4()), draft)
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": obra_warehouse}, "actual_qty") == 7
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 3
    frappe.set_user("almacen@lebaref.test")
    try:
        frappe.get_doc("Stock Entry", draft).cancel()
    except frappe.PermissionError:
        assert frappe.db.get_value("Stock Entry", draft, "docstatus") == 1
        assert frappe.db.get_value("Stock Entry", draft, "custom_lebaref_clave_sobrante")
    else:
        raise AssertionError("Almacén anuló un sobrante sin autorización")
    frappe.set_user("Administrator")
    frappe.db.savepoint("duplicado_sobrante")
    try:
        ejecutar("sobrante", dict(data, referencia=data["referencia"].strip().upper()), str(uuid.uuid4()))
    except (frappe.UniqueValidationError, frappe.DuplicateEntryError):
        frappe.db.rollback(save_point="duplicado_sobrante")
    else:
        raise AssertionError("Se duplicó un acta con otro UUID")
    extra_data = dict(common, referencia="ACTA-NUEVA-"+uuid.uuid4().hex[:8], tipo="Entrada no contabilizada",
        rows=[dict(item_code=sku, qty=2, valor_unitario=20)])
    try:
        ejecutar("sobrante", extra_data, str(uuid.uuid4()))
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se ingresó stock sin verificar existencias anteriores")
    extra_data["confirmacion"] = "NO CONTABILIZADO"
    new_stock = ejecutar("sobrante", extra_data, str(uuid.uuid4()))["folio"]
    ejecutar("autorizar_sobrante", {"confirmar": "AUTORIZAR"}, str(uuid.uuid4()), new_stock)
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 5
    doc = frappe.get_doc("Stock Entry", new_stock)
    assert doc.purpose == "Material Receipt" and doc.items[0].basic_rate == 20
    doc.cancel()
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 3
    # A canceled movement releases its acta key and keeps the original reference and cancellation history.
    replacement = ejecutar("sobrante", extra_data, str(uuid.uuid4()))["folio"]
    ejecutar("autorizar_sobrante", {"confirmar": "AUTORIZAR"}, str(uuid.uuid4()), replacement)
    assert frappe.db.get_value("Bin", {"item_code": sku, "warehouse": base.warehouse}, "actual_qty") == 5
    assert frappe.db.get_value("Stock Entry", new_stock, "custom_lebaref_referencia_sobrante") == extra_data["referencia"].upper()
