"""Prueba transaccional sobre datos ficticios. Siempre revierte sus movimientos."""
import uuid
import frappe
from frappe.utils import add_days, today
from lebaref_compras.operaciones import esquema, ejecutar
from lebaref_compras.comparativa import consultar, generar_ordenes
from lebaref_compras.verificar_comercial import verificar_comercial
from lebaref_compras.verificar_consumos import verificar_consumos
from lebaref_compras.verificar_sobrantes import verificar_sobrantes
from lebaref_compras.verificar_ajustes import verificar_ajustes
from lebaref_compras.verificar_comprobantes import verificar_comprobantes
from lebaref_compras.verificar_sustituciones import verificar_sustituciones
from lebaref_compras.verificar_solicitudes import verificar_solicitudes
from lebaref_compras.verificar_recepciones import verificar_recepciones, verificar_destinos


def _verificar_ofertas_parciales(data, item_code, supplier, second_supplier):
    request_data = dict(data, rows=[*data["rows"], dict(data["rows"][0], item_code=item_code)])
    request = ejecutar("solicitud", request_data, str(uuid.uuid4()))["folio"]
    rfq = ejecutar("peticion", {"suppliers": f"{supplier}\n{second_supplier}"}, str(uuid.uuid4()), request)["folio"]
    first, second = esquema("oferta", rfq)["rows"]
    assert first["item_code"] != second["item_code"]

    offer = {"supplier": supplier, "valid_till": add_days(today(), 10)}
    priced = dict(first, rate="100", lead_time_days="2")
    for invalid_rows, message in (
        ([first, dict(second, rate=" ")], "Captura al menos un precio unitario mayor que cero."),
        ([priced, first], "El renglón no pertenece a la petición o está repetido."),
        ([priced, dict(second, id="otro-renglon")], "El renglón no pertenece a la petición o está repetido."),
        ([priced, dict(second, rate="-1")], "La cantidad o precio debe ser positivo."),
    ):
        try:
            ejecutar("oferta", dict(offer, rows=invalid_rows), str(uuid.uuid4()), rfq)
        except frappe.ValidationError as error:
            assert str(error) == message
        else:
            raise AssertionError("Se aceptó una oferta parcial inválida")

    token = str(uuid.uuid4())
    first_offer = dict(offer, rows=[priced])
    quote = ejecutar("oferta", first_offer, token, rfq)["folio"]
    assert ejecutar("oferta", first_offer, token, rfq) == {
        "folio": quote, "tipo": "Supplier Quotation", "repetida": True
    }
    assert [r.request_for_quotation_item for r in frappe.get_doc("Supplier Quotation", quote).items] == [first["id"]]

    by_code = {r["codigo"]: r for r in consultar(request)["renglones"]}
    assert [opt["cotizacion"] for opt in by_code[first["item_code"]]["opciones"]] == [quote]
    assert by_code[second["item_code"]]["opciones"] == []

    second_offer = dict(offer, supplier=second_supplier, rows=[first, dict(second, rate=125)])
    second_quote = ejecutar("oferta", second_offer, str(uuid.uuid4()), rfq)["folio"]
    assert [r.request_for_quotation_item for r in frappe.get_doc("Supplier Quotation", second_quote).items] == [second["id"]]
    renglones = consultar(request)["renglones"]
    by_code = {r["codigo"]: r for r in renglones}
    assert [opt["cotizacion"] for opt in by_code[first["item_code"]]["opciones"]] == [quote]
    assert [opt["cotizacion"] for opt in by_code[second["item_code"]]["opciones"]] == [second_quote]
    orders = generar_ordenes(request, {r["id"]: r["opciones"][0]["id"] for r in renglones})
    assert len(orders) == 2
    actual = {}
    for order in orders:
        doc = frappe.get_doc("Purchase Order", order["folio"])
        assert doc.docstatus == 0 and len(doc.items) == 1
        actual[doc.supplier] = (doc.items[0].item_code, doc.items[0].supplier_quotation)
    assert actual == {
        supplier: (first["item_code"], quote),
        second_supplier: (second["item_code"], second_quote),
    }


def _verificar_unidades(seed, supplier):
    base = seed.items[0]
    frappe.set_user("Administrator")
    uom = "Caja prueba " + uuid.uuid4().hex[:8]
    frappe.get_doc({"doctype": "UOM", "uom_name": uom}).insert()
    item_group = frappe.db.get_value("Item", base.item_code, "item_group")
    units = {"uom": uom, "stock_uom": base.stock_uom, "conversion_factor": 10}
    frappe.set_user("autoriza@lebaref.test")
    sku = "TEST-UOM-" + uuid.uuid4().hex[:8]
    item = ejecutar("articulo", dict(units, item_code=sku, item_name="Artículo por caja", item_group=item_group), str(uuid.uuid4()))["folio"]

    frappe.set_user("compras@lebaref.test")
    fields = {f["name"]: f["value"] for f in esquema("articulo", item)["fields"]}
    assert all(fields[key] == value for key, value in units.items())
    data = {
        "company": seed.company, "schedule_date": add_days(today(), 7), "title": "Prueba de compra por caja",
        "rows": [dict(units, item_code=item, qty=2, warehouse=base.warehouse)],
    }
    request = ejecutar("solicitud", data, str(uuid.uuid4()))["folio"]
    row = frappe.get_doc("Material Request", request).items[0]
    assert row.qty == 2 and row.stock_qty == 20
    assert all(row.get(key) == value for key, value in units.items())

    rfq = ejecutar("peticion", {"suppliers": supplier}, str(uuid.uuid4()), request)["folio"]
    row = esquema("oferta", rfq)["rows"][0]
    assert all(row[key] == value for key, value in units.items())
    quote_data = {"supplier": supplier, "valid_till": add_days(today(), 10), "rows": [dict(row, rate=100)]}
    try:
        invalid = dict(quote_data, rows=[dict(row, rate=100, conversion_factor=20)])
        ejecutar("oferta", invalid, str(uuid.uuid4()), rfq)
    except frappe.ValidationError as error:
        assert "no coincide con el documento de origen" in str(error)
    else:
        raise AssertionError("La oferta cambió el factor de la petición")
    quote = ejecutar("oferta", quote_data, str(uuid.uuid4()), rfq)["folio"]
    quoted = frappe.get_doc("Supplier Quotation", quote).items[0]
    assert quoted.stock_qty == 20 and all(quoted.get(key) == value for key, value in units.items())
    comparison = consultar(request)["renglones"][0]
    order = generar_ordenes(request, {comparison["id"]: comparison["opciones"][0]["id"]})[0]["folio"]
    frappe.set_user("autoriza@lebaref.test")
    ejecutar("autorizar", {"confirmar": "AUTORIZAR"}, str(uuid.uuid4()), order)

    frappe.set_user("almacen@lebaref.test")
    row = esquema("recibir", order)["rows"][0]
    assert row["pending"] == 2 and all(row[key] == value for key, value in units.items())
    receipt = ejecutar("recibir", {"rows": [dict(row, qty=1)]}, str(uuid.uuid4()), order)["folio"]
    received = frappe.get_doc("Purchase Receipt", receipt).items[0]
    assert received.qty == 1 and all(received.get(key) == value for key, value in units.items())
    assert frappe.db.get_value("Bin", {"item_code": item, "warehouse": base.warehouse}, "actual_qty") == 10


def _verificar_proveedor():
    frappe.set_user("autoriza@lebaref.test")
    data = {
        "supplier_name": "Contacto prueba " + uuid.uuid4().hex[:8],
        "supplier_group": frappe.db.get_value("Supplier Group", {"is_group": 0}),
        "tax_id": "AAA010101AAA", "telefono": "+52 55 1234 5678",
        "contacto": "Ana de Prueba", "correo": "cotizaciones@lebaref.test",
    }
    token = str(uuid.uuid4())
    supplier = ejecutar("proveedor", data, token)["folio"]
    assert ejecutar("proveedor", data, token)["repetida"]
    doc = frappe.get_doc("Supplier", supplier)
    contact = frappe.get_doc("Contact", doc.supplier_primary_contact)
    assert (contact.full_name, contact.mobile_no, contact.email_id) == (data["contacto"], data["telefono"], data["correo"])
    assert [(r.link_doctype, r.link_name) for r in contact.links] == [("Supplier", supplier)]
    assert len(contact.email_ids) == len(contact.phone_nos) == 1

    only_name = dict(data, supplier_name=data["supplier_name"] + " Sin canales", telefono="", correo="")
    named_supplier = ejecutar("proveedor", only_name, str(uuid.uuid4()))["folio"]
    fields = {f["name"]: f["value"] for f in esquema("proveedor", named_supplier)["fields"]}
    assert fields["contacto"] == data["contacto"] and not fields["correo"] and not fields["telefono"]

    frappe.set_user("compras@lebaref.test")
    fields = {f["name"]: f["value"] for f in esquema("proveedor", supplier)["fields"]}
    assert all(fields[key] == data[key] for key in ("telefono", "contacto", "correo"))

    # Verificar que todos los usuarios operativos (Compras, Almacén y cuentas nominales) pueden crear y modificar proveedores
    from lebaref_compras.ux_lebaref import guardar_proveedor_rapido
    for usr_eq in ("compras@lebaref.test", "almacen@lebaref.test", "juan.cetina@lebaref.com", "salvia.sosa@lebaref.com", "airan.echeverria@lebaref.com"):
        if not frappe.db.exists("User", usr_eq):
            continue
        frappe.set_user(usr_eq)
        creado_eq = guardar_proveedor_rapido({
            "supplier_name": f"Prov Equipo {usr_eq.split('@')[0]} {uuid.uuid4().hex[:5]}",
            "tax_id": "EQP010101AAA",
            "especialidad": "HVAC y Eléctrico",
            "contacto": "Contacto Operativo",
            "telefono": "9991112233",
            "correo": "ventas@proveedorequipo.test",
        })
        assert creado_eq["ok"] and creado_eq["nuevo"] == 1
        editado_eq = guardar_proveedor_rapido({
            "id": creado_eq["folio"],
            "supplier_name": frappe.db.get_value("Supplier", creado_eq["folio"], "supplier_name"),
            "tax_id": "EQP020202BBB",
            "especialidad": "HVAC, Cobre y Controles",
            "contacto": "Contacto Actualizado",
            "telefono": "9994445566",
            "correo": "actualizado@proveedorequipo.test",
        })
        assert editado_eq["ok"] and editado_eq["nuevo"] == 0
        assert frappe.db.get_value("Supplier", creado_eq["folio"], "tax_id") == "EQP020202BBB"


def _verificar_conceptos_libres(seed, supplier):
    from html import escape

    from lebaref_compras.solicitudes import ARTICULO_LIBRE

    frappe.set_user("Administrator")
    department = frappe.get_doc({
        "doctype": "Department", "department_name": "Depto prueba " + uuid.uuid4().hex[:8], "company": seed.company,
    }).insert()
    base = seed.items[0]
    concepts = ["Corte de placa <A> & montaje", "Rectificado de soporte"]
    headers = {"departamento": department.name, "solicitante": "Ana de Prueba", "notas": "Entregar en taller <A>.\nAvisar al recibir."}
    data = dict(headers, company=seed.company, schedule_date=add_days(today(), 7), title="Solicitud mixta", rows=[
        *(dict(concepto_libre=concept, qty=1, project=base.project, custom_lebaref_partida="Taller") for concept in concepts),
        dict(item_code=base.item_code, qty=1, warehouse=base.warehouse),
    ])

    frappe.set_user("compras@lebaref.test")
    schema = esquema("solicitud")
    fields = {f["name"]: f for f in schema["fields"]}
    assert department.name in fields["departamento"]["choices"]
    catalog = next(c["choices"] for c in schema["columns"] if c["name"] == "item_code")
    assert ARTICULO_LIBRE not in catalog
    for invalid, message in (
        (dict(data, rows=[{"qty": 1}]), "Selecciona un artículo de catálogo o escribe un concepto libre."),
        (dict(data, rows=[dict(data["rows"][0], item_code=base.item_code)]), "Selecciona un artículo de catálogo o escribe un concepto libre."),
        (dict(data, company="Empresa ajena"), "Selecciona un departamento activo de la empresa de la solicitud."),
    ):
        try:
            ejecutar("solicitud", invalid, str(uuid.uuid4()))
        except frappe.ValidationError as error:
            assert str(error) == message
        else:
            raise AssertionError("Se aceptó una solicitud inválida")

    token = str(uuid.uuid4())
    request = ejecutar("solicitud", data, token)["folio"]
    assert ejecutar("solicitud", data, token)["repetida"]
    doc = frappe.get_doc("Material Request", request)
    assert doc.docstatus == 1 and doc.custom_lebaref_departamento == department.name
    stored_concepts = [escape(concept) for concept in concepts]
    assert [r.custom_lebaref_concepto for r in doc.items[:2]] == stored_concepts
    assert [r.description for r in doc.items[:2]] == stored_concepts
    assert all(r.item_code == ARTICULO_LIBRE for r in doc.items[:2])
    assert doc.items[2].item_code == base.item_code and not doc.items[2].custom_lebaref_concepto
    saved = esquema("solicitud", request)
    assert not saved["add_rows"] and not any(c["editable"] for c in saved["columns"])
    values = {f["name"]: f["value"] for f in saved["fields"]}
    assert all(values[key] == value for key, value in headers.items())
    assert [(r["item_code"], r["concepto_libre"]) for r in saved["rows"]] == [
        ("", concepts[0]), ("", concepts[1]), (base.item_code, ""),
    ]

    rfq = ejecutar("peticion", {"suppliers": supplier}, str(uuid.uuid4()), request)["folio"]
    rows = esquema("oferta", rfq)["rows"]
    assert [r["concepto_libre"] for r in rows] == concepts + [""]
    offer = {"supplier": supplier, "valid_till": add_days(today(), 10), "rows": [dict(r, rate=100) for r in rows[:2]]}
    quote = ejecutar("oferta", offer, str(uuid.uuid4()), rfq)["folio"]
    quote_items = frappe.get_doc("Supplier Quotation", quote).items
    assert [r.custom_lebaref_concepto for r in quote_items] == stored_concepts
    assert [r.description for r in quote_items] == stored_concepts
    comparison = consultar(request)["renglones"]
    assert [(r["codigo"], r["articulo"]) for r in comparison[:2]] == [("", concept) for concept in concepts]
    assert comparison[2]["opciones"] == []
    orders = generar_ordenes(request, {r["id"]: r["opciones"][0]["id"] for r in comparison[:2]})
    assert len(orders) == 1
    order = orders[0]["folio"]
    assert [r.custom_lebaref_concepto for r in frappe.get_doc("Purchase Order", order).items] == stored_concepts

    frappe.set_user("autoriza@lebaref.test")
    ejecutar("autorizar", {"confirmar": "AUTORIZAR"}, str(uuid.uuid4()), order)
    frappe.set_user("almacen@lebaref.test")
    rows = esquema("recibir", order)["rows"]
    assert [r["concepto_libre"] for r in rows] == concepts
    receipt = ejecutar("recibir", {"rows": [dict(rows[0], qty=1)]}, str(uuid.uuid4()), order)["folio"]
    received = frappe.get_doc("Purchase Receipt", receipt).items[0]
    assert received.custom_lebaref_concepto == received.description == stored_concepts[0]
    assert not frappe.db.exists("Bin", {"item_code": ARTICULO_LIBRE})


def verificar():
    if frappe.local.site != "lebaref-pruebas.local":
        raise RuntimeError("Solo se permite en el entorno de pruebas.")
    original_user = frappe.session.user
    original_developer_mode = frappe.conf.developer_mode
    # Test the hooks from this image when the shared Desk backend runs another version.
    # This setting is process-local and is restored without writing site configuration.
    frappe.conf.developer_mode = 1
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
        for user, action in (("compras@lebaref.test", "articulo"), ("almacen@lebaref.test", "cfdi")):
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
        second_supplier = frappe.get_doc(dict(doctype="Supplier", supplier_name="Prueba parcial " + str(uuid.uuid4())[:8], supplier_group=supplier.supplier_group, supplier_type="Company")).insert()
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
        _verificar_ofertas_parciales(data, item["folio"], supplier.name, second_supplier.name)
        results.append("Ofertas parciales, exclusión de renglones sin precio y adjudicación a dos proveedores")
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
        _verificar_unidades(seed, supplier.name)
        results.append("CAT-02: dos cajas son 20 unidades de inventario y recibir una caja ingresa 10")
        _verificar_proveedor()
        results.append("CAT-03: teléfono, contacto y correo en el proveedor, consulta con permisos y reintento")
        _verificar_conceptos_libres(seed, supplier.name)
        results.append("SOL-02: encabezado y dos conceptos libres en solicitud mixta; cotización y recepción trazables sin movimiento de inventario")
        verificar_comercial(seed, supplier.name)
        results.append("COM-04 / COM-07: IVA 16 %, exento, flete y traslados nativos de condición y fecha de pago")
        verificar_solicitudes(seed, supplier.name, warehouse.name)
        results.append("SOL-03 / SOL-05 / CAT-04: anexos, SKU existente o nuevo, salida parcial y transferencia vinculada")
        verificar_recepciones(seed, supplier.name)
        results.append("ALM-02: entrada anticipada, regularización auditada sin nuevos movimientos, permisos, reintentos y recepción del saldo")
        verificar_destinos(seed, supplier.name, warehouse.name)
        results.append("ALM-03: material directo a obra, servicio sin existencias, persona/remisión, destinos inválidos y recepción compatible del saldo")
        verificar_sustituciones(seed, supplier.name)
        results.append("ALM-04: autorización por responsable, límite de sustitución, inventario correcto, factura y devolución nativas y saldo original")
        verificar_consumos(seed, warehouse.name)
        results.append("ALM-05: resguardo, consumo y devolución con factor 10, gasto por obra, límites compartidos, reintento y anulación")
        verificar_sobrantes(seed, warehouse.name)
        results.append("ALM-07: borrador de sobrante, autorización en API y ruta nativa, traslado sin duplicación, acta única, stock no contabilizado y anulación")
        verificar_ajustes(seed)
        results.append("ALM-08: conteo positivo, merma, permisos, reversión y conteo desactualizado rechazado")
        verificar_comprobantes(seed,supplier.name)
        results.append("XML-02..04: diferencias por concepto, conciliación, factura y nota sin doble stock, pago parcial/total, reversión y exceso rechazado")
        return {"correcto": True, "pruebas": results, "datos": "Revertidos al finalizar"}
    finally:
        frappe.db.rollback()
        frappe.set_user(original_user)
        frappe.conf.developer_mode = original_developer_mode
