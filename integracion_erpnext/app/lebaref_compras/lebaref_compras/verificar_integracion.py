"""Prueba transaccional sobre datos ficticios. Siempre revierte sus movimientos."""
import uuid
import frappe
from frappe.utils import add_days, today
from lebaref_compras.operaciones import esquema, ejecutar
from lebaref_compras.comparativa import consultar, generar_ordenes


def verificar():
    if frappe.local.site != "lebaref-pruebas.local":
        raise RuntimeError("Solo se permite en el entorno de pruebas.")
    original_user = frappe.session.user
    results = []
    try:
        frappe.set_user("Administrator")
        seed = frappe.get_doc("Material Request", "MAT-MR-2026-00001")
        company = seed.company
        source = seed.items[0]
        frappe.set_user("autoriza@lebaref.test")
        item_schema = esquema("articulo")
        item = ejecutar("articulo", dict(item_code="TEST-"+str(uuid.uuid4())[:8], item_name="Artículo de verificación", item_group=item_schema["fields"][2]["choices"][0], stock_uom=item_schema["fields"][3]["choices"][0]), str(uuid.uuid4()))
        project = ejecutar("obra", dict(project_name="Obra verificación "+str(uuid.uuid4())[:8], company=company), str(uuid.uuid4()))
        assert frappe.db.exists("Item", item["folio"]) and frappe.db.exists("Project", project["folio"])
        for user, action in (("compras@lebaref.test", "proveedor"), ("almacen@lebaref.test", "cfdi")):
            frappe.set_user(user)
            try:
                esquema(action)
            except frappe.PermissionError:
                pass
            else:
                raise AssertionError((user, action))
        results.append("Alta de artículo/obra por responsable; auxiliares sin alta de catálogo ni archivo CFDI desde Almacén")
        frappe.set_user("Administrator")
        supplier = frappe.get_doc(dict(doctype="Supplier", supplier_name="Prueba integral " + str(uuid.uuid4())[:8], supplier_group=frappe.db.get_value("Supplier Group", {"is_group": 0}), supplier_type="Company", tax_id="AAA010101AAA")).insert()
        warehouse = frappe.get_doc(dict(doctype="Warehouse", warehouse_name="Obra prueba " + str(uuid.uuid4())[:8], company=company)).insert()
        frappe.db.set_value("Company", company, "tax_id", "BBB010101BBB")
        frappe.set_user("compras@lebaref.test")
        data = dict(company=company, schedule_date=add_days(today(), 7), title="Prueba transaccional integración", rows=[dict(item_code=source.item_code, qty="4", warehouse=source.warehouse, project=source.project, custom_lebaref_partida="Prueba integral")])
        token = str(uuid.uuid4())
        request = ejecutar("solicitud", data, token)["folio"]
        assert ejecutar("solicitud", data, token)["folio"] == request
        results.append("Solicitud confirmada y reintento sin duplicados")
        rfq = ejecutar("peticion", dict(suppliers=supplier.name), str(uuid.uuid4()), request)["folio"]
        schema = esquema("oferta", rfq)
        for row in schema["rows"]:
            row.update(rate="100", lead_time_days="2")
        quote = ejecutar("oferta", dict(supplier=supplier.name, valid_till=add_days(today(), 10), rows=schema["rows"]), str(uuid.uuid4()), rfq)["folio"]
        comparison = consultar(request)
        order = generar_ordenes(request, {r["id"]: r["opciones"][0]["id"] for r in comparison["renglones"]})[0]["folio"]
        results.append("Petición → oferta → comparativa → orden en borrador")
        denied = False
        try:
            ejecutar("autorizar", {"confirmar": "AUTORIZAR"}, str(uuid.uuid4()), order)
        except frappe.PermissionError:
            denied = True
        assert denied
        frappe.set_user("autoriza@lebaref.test")
        ejecutar("autorizar", {"confirmar": "AUTORIZAR"}, str(uuid.uuid4()), order)
        results.append("Auxiliar no autoriza; responsable sí")
        frappe.set_user("almacen@lebaref.test")
        schema = esquema("recibir", order)
        values = dict(rows=[dict(id=schema["rows"][0]["id"], qty="2")])
        token = str(uuid.uuid4())
        receipt = ejecutar("recibir", values, token, order)["folio"]
        assert ejecutar("recibir", values, token, order)["folio"] == receipt
        assert 0 < frappe.get_doc("Purchase Order", order).per_received < 100
        entry = frappe.get_doc("Purchase Receipt", receipt)
        assert entry.items[0].custom_lebaref_partida == "Prueba integral"
        assert entry.items[0].project == source.project
        rejected = False
        try:
            ejecutar("recibir", dict(rows=[dict(id=schema["rows"][0]["id"], qty="99")]), str(uuid.uuid4()), order)
        except frappe.ValidationError:
            rejected = True
        assert rejected
        results.append("Recepción parcial, saldo, trazabilidad, reintento y exceso rechazado")
        transfer = ejecutar("entregar", dict(company=company, from_warehouse=source.warehouse, to_warehouse=warehouse.name, project=source.project, remarks="Entrega de prueba", rows=[dict(item_code=source.item_code, qty="1", custom_lebaref_partida="Prueba integral")]), str(uuid.uuid4()))["folio"]
        schema = esquema("devolver", transfer)
        returned = ejecutar("devolver", dict(rows=[dict(id=schema["rows"][0]["id"], qty="1")]), str(uuid.uuid4()), transfer)["folio"]
        assert frappe.get_doc("Stock Entry", returned).custom_lebaref_origen == transfer
        rejected = False
        try:
            ejecutar("devolver", dict(rows=[dict(id=schema["rows"][0]["id"], qty="1")]), str(uuid.uuid4()), transfer)
        except frappe.ValidationError:
            rejected = True
        assert rejected
        assert frappe.db.get_value("Bin", {"item_code": source.item_code, "warehouse": warehouse.name}, "actual_qty") == 0
        results.append("Entrega y retorno: existencias cuadran y exceso de devolución rechazado")
        frappe.set_user("compras@lebaref.test")
        identifier = str(uuid.uuid4()).upper()
        xml = f'<c:Comprobante xmlns:c="http://www.sat.gob.mx/cfd/4" xmlns:t="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="I" Total="200" Moneda="MXN"><c:Emisor Rfc="AAA010101AAA"/><c:Receptor Rfc="BBB010101BBB"/><c:Complemento><t:TimbreFiscalDigital UUID="{identifier}"/></c:Complemento></c:Comprobante>'
        result = ejecutar("cfdi", {"xml": xml}, str(uuid.uuid4()), receipt)
        assert result["folio"] == identifier
        assert ejecutar("cfdi", {"xml": xml}, str(uuid.uuid4()), receipt)["repetida"]
        assert frappe.db.get_value("File", {"attached_to_doctype": "Lebaref CFDI", "attached_to_name": identifier}, "is_private") == 1
        try:
            frappe.get_doc(dict(doctype="Lebaref CFDI", uuid=str(uuid.uuid4()), recepcion=receipt)).insert()
        except frappe.ValidationError:
            pass
        else:
            raise AssertionError("Se registró CFDI sin pasar por la validación del XML")
        results.append("XML privado registrado y UUID sin duplicación (validación estructural, no SAT)")
        return {"correcto": True, "pruebas": results, "datos": "Revertidos al finalizar"}
    finally:
        frappe.db.rollback()
        frappe.set_user(original_user)
