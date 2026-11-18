"""Prueba de integración con roles reales. Todo se revierte al terminar."""
import uuid
import frappe
from frappe.utils import today, add_days
from lebaref_compras.operaciones import ejecutar, esquema
from lebaref_compras.equipos import detalle


def verificar():
    if frappe.local.site != "lebaref-pruebas.local":
        raise RuntimeError("La prueba solo admite el sitio ficticio de Lebaref.")
    user = frappe.session.user
    evidence = []
    def call(action, data, source="", token=None):
        return ejecutar(action, data, token or str(uuid.uuid4()), source)["folio"]
    def denied(action, error=frappe.ValidationError):
        point = "test_"+uuid.uuid4().hex[:12]
        frappe.db.savepoint(point)
        try:
            action()
        except error:
            frappe.db.rollback(save_point=point)
        else:
            raise AssertionError("Se aceptó una operación que debía rechazarse")
    try:
        frappe.set_user("Administrator")
        seed = frappe.get_doc("Material Request", "MAT-MR-2026-00001")
        company, item, project = seed.company, seed.items[0].item_code, seed.items[0].project
        suffix = uuid.uuid4().hex[:8].upper()
        frappe.set_user("compras@lebaref.test")
        denied(lambda: esquema("herramienta"), frappe.PermissionError)
        denied(lambda: esquema("prestamo"), frappe.PermissionError)
        frappe.set_user("autoriza@lebaref.test")
        tools = [call("herramienta", dict(codigo=f"TEST-HER-{suffix}-{i}", company=company, articulo=item, ubicacion="Gabinete de prueba", serie=f"SERIE-{suffix}-{i}")) for i in range(2)]
        frappe.set_user("almacen@lebaref.test")
        denied(lambda: esquema("herramienta"), frappe.PermissionError)
        data = dict(company=company, obra=project, responsable="Responsable ficticio", recibe="Operario ficticio", compromiso=add_days(today(), 7), rows=[dict(herramienta=name) for name in tools])
        token = str(uuid.uuid4())
        loan = call("prestamo", data, token=token)
        assert call("prestamo", data, token=token) == loan
        denied(lambda: call("prestamo", data))
        assert all(frappe.get_doc("Lebaref Herramienta", name).prestamo == loan for name in tools)
        raw = frappe.get_doc("Lebaref Herramienta", tools[0])
        raw.estado = "Disponible"
        raw.flags.lebaref_equipos_writing = True
        denied(raw.save)
        denied(lambda: frappe.delete_doc("Lebaref Herramienta", tools[0]), (frappe.PermissionError, frappe.ValidationError))
        evidence.append("Roles correctos; préstamo exclusivo; repetición sin duplicado; edición directa y borrado rechazados")
        schema = esquema("retorno_herramienta", loan)
        first, second = schema["rows"]
        values = dict(recibe="Almacén ficticio", rows=[dict(id=first["id"], estado="Bueno", observaciones="Revisada")])
        token = str(uuid.uuid4())
        returned = call("retorno_herramienta", values, loan, token)
        assert call("retorno_herramienta", values, loan, token) == returned
        assert frappe.get_doc("Lebaref Prestamo", loan).estado == "Devuelto parcial"
        assert frappe.get_doc("Lebaref Herramienta", tools[0]).estado == "Disponible"
        assert frappe.get_doc("Lebaref Herramienta", tools[1]).estado == "Prestada"
        denied(lambda: call("retorno_herramienta", values, loan))
        denied(lambda: call("retorno_herramienta", dict(recibe="Almacén", rows=[dict(id=second["id"], estado="Dañada", observaciones="")]), loan))
        call("retorno_herramienta", dict(recibe="Almacén", rows=[dict(id=second["id"], estado="Dañada", observaciones="Cable dañado durante la prueba")]), loan)
        assert frappe.get_doc("Lebaref Prestamo", loan).estado == "Devuelto con incidencia"
        assert frappe.get_doc("Lebaref Herramienta", tools[1]).estado == "Dañada"
        frappe.set_user("autoriza@lebaref.test")
        call("estado_herramienta", dict(estado="Disponible", ubicacion="Gabinete", motivo="Reparación y revisión ficticias"), tools[1])
        assert frappe.get_doc("Lebaref Herramienta", tools[1]).estado == "Disponible"
        assert any(r["campo"].startswith("Préstamo") for r in detalle("Lebaref Herramienta", tools[0])["rows"])
        evidence.append("Retorno parcial y con daño; rechazo del retorno repetido; recuperación por responsable e historial")
        vehicle_data = dict(codigo="TEST-VEH-"+suffix, placas="TEST"+suffix, company=company, marca_modelo="Vehículo ficticio", responsable="Conductor ficticio", km="1000", fecha=today(), intervalo="10000", proximo_servicio="1100", capacidad="60", combustible="Gasolina Regular", seguro=add_days(today(), 20))
        vehicle = call("vehiculo", vehicle_data)
        denied(lambda: call("vehiculo", dict(vehicle_data, codigo="OTRO-"+suffix)), frappe.UniqueValidationError)
        frappe.set_user("almacen@lebaref.test")
        expense = dict(fecha=today(), tipo="Combustible", km="1100", litros="10", tanque_lleno="Sí", monto="240.00", proveedor="Proveedor ficticio", referencia="TICKET-1", descripcion="Carga de prueba")
        token = str(uuid.uuid4())
        charge = call("gasto_vehiculo", expense, vehicle, token)
        assert call("gasto_vehiculo", expense, vehicle, token) == charge
        denied(lambda: call("gasto_vehiculo", expense, vehicle))
        denied(lambda: call("gasto_vehiculo", dict(expense, referencia="KM-INVALIDO", km="1099"), vehicle))
        denied(lambda: call("gasto_vehiculo", dict(expense, referencia="LITROS-INVALIDOS", litros="99"), vehicle))
        denied(lambda: call("gasto_vehiculo", dict(expense, referencia="MONTO-INVALIDO", monto="NaN"), vehicle))
        review = dict(fecha=today(), km="1200", inspector="Inspector ficticio", nivel="3/4", llantas="OK", fluidos="OK", frenos_luces="Falla", carroceria="OK", observaciones="Luz de prueba averiada")
        call("revision_vehiculo", review, vehicle)
        call("gasto_vehiculo", dict(expense, tipo="Mantenimiento preventivo", km="1200", litros="0", monto="1000.00", referencia="SERVICIO-1"), vehicle)
        record = frappe.get_doc("Lebaref Vehiculo", vehicle)
        assert record.km_actual == 1200 and record.ultimo_servicio == 1200 and record.proximo_servicio == 11200
        history = detalle("Lebaref Vehiculo", vehicle)["rows"]
        assert next(r for r in history if r["campo"] == "Gasto total registrado")["valor"] in ("1240.00", "1240.0", "1240")
        assert any(r["campo"].startswith("Revisión") for r in history)
        frappe.set_user("autoriza@lebaref.test")
        call("estado_vehiculo", dict(estado="En taller", responsable="Responsable ficticio", seguro=add_days(today(), 365), motivo="Revisión de falla ficticia"), vehicle)
        assert frappe.get_doc("Lebaref Vehiculo", vehicle).estado == "En taller"
        evidence.append("Placas únicas; gastos sin duplicado; km/litros/importes válidos; revisión, próximo servicio, seguro e historial")
        return dict(correcto=True, pruebas=evidence, datos="Todos los registros de esta prueba se revierten")
    finally:
        frappe.db.rollback()
        frappe.set_user(user)
