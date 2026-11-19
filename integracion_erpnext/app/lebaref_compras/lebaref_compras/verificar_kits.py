import uuid
import frappe
from frappe.utils import today, add_days
from lebaref_compras.operaciones import ejecutar, esquema
from lebaref_compras.equipos import detalle


def verificar():
    if frappe.local.site != "lebaref-pruebas.local":
        raise RuntimeError("Solo se admite el sitio de pruebas.")
    original = frappe.session.user
    evidence = []
    def call(action, data, source="", token=None):
        return ejecutar(action, data, token or str(uuid.uuid4()), source)["folio"]
    def denied(fn, kind=frappe.ValidationError):
        point = "kit_"+uuid.uuid4().hex[:12]
        frappe.db.savepoint(point)
        try:
            fn()
        except kind:
            frappe.db.rollback(save_point=point)
        else:
            raise AssertionError("La operación inválida no fue rechazada")
    try:
        frappe.set_user("autoriza@lebaref.test")
        sample = frappe.get_doc("Material Request", "MAT-MR-2026-00001")
        suffix = uuid.uuid4().hex[:8].upper()
        tools = [call("herramienta", dict(codigo=f"TEST-K-{suffix}-{i}", company=sample.company, articulo=sample.items[0].item_code, ubicacion="Gabinete")) for i in range(3)]
        contents = [dict(herramienta=t) for t in tools[:2]]
        kit_data = dict(codigo="TEST-KIT-"+suffix, nombre="Kit de prueba", company=sample.company, ubicacion="Gabinete", rows=contents)
        token = str(uuid.uuid4())
        kit = call("kit", kit_data, token=token)
        assert call("kit", kit_data, token=token) == kit
        denied(lambda: call("kit", dict(kit_data, codigo="OTRO-"+suffix)))
        frappe.set_user("almacen@lebaref.test")
        denied(lambda: esquema("kit"), frappe.PermissionError)
        schema = esquema("prestamo_kit", kit)
        schema["rows"][1]["incluir"] = "No"
        data = dict(obra=sample.items[0].project, responsable="Responsable ficticio", recibe="Operario ficticio", compromiso=add_days(today(), 7), rows=schema["rows"])
        loan = call("prestamo_kit", data, kit)
        assert len(frappe.get_doc("Lebaref Prestamo", loan).items) == 1
        assert frappe.get_doc("Lebaref Prestamo", loan).kit == kit
        assert frappe.get_doc("Lebaref Herramienta", tools[1]).estado == "Disponible"
        denied(lambda: call("prestamo_kit", data, kit))
        frappe.set_user("autoriza@lebaref.test")
        denied(lambda: call("composicion_kit", dict(motivo="Cambio", rows=contents), kit))
        frappe.set_user("almacen@lebaref.test")
        row = esquema("retorno_herramienta", loan)["rows"][0]
        call("retorno_herramienta", dict(recibe="Almacén", rows=[dict(id=row["id"], estado="Bueno", observaciones="")]), loan)
        frappe.set_user("autoriza@lebaref.test")
        call("composicion_kit", dict(motivo="Sustitución de prueba", rows=[dict(herramienta=tools[0]), dict(herramienta=tools[2])]), kit)
        denied(lambda: call("prestamo_kit", data, kit))
        assert any(r["campo"] == tools[2] for r in detalle("Lebaref Kit", kit)["rows"])
        evidence.append("Kit único por herramienta; préstamo selectivo; cambio bloqueado durante préstamo; retorno e historial; captura obsoleta rechazada")
        warranty = dict(fecha_compra=add_days(today(), -30), tiene_garantia="Sí", fin_garantia=add_days(today(), 60), costo_adquisicion="1000.00", referencia="FACTURA-PRUEBA", motivo="Registro de prueba")
        call("garantia_herramienta", warranty, tools[0])
        denied(lambda: call("garantia_herramienta", dict(warranty, fin_garantia=add_days(today(), -31)), tools[0]))
        frappe.set_user("almacen@lebaref.test")
        denied(lambda: esquema("garantia_herramienta", tools[0]), frappe.PermissionError)
        service = dict(fecha=today(), proveedor="Taller ficticio", referencia="MANT-1", monto="600.00", bajo_garantia="No", motivo="Reparación ficticia")
        token = str(uuid.uuid4())
        maintenance = call("mantenimiento_herramienta", service, tools[0], token)
        assert call("mantenimiento_herramienta", service, tools[0], token) == maintenance
        denied(lambda: call("mantenimiento_herramienta", service, tools[0]))
        denied(lambda: call("mantenimiento_herramienta", dict(service, monto="NaN"), tools[0]))
        call("mantenimiento_herramienta", dict(service, referencia="GARANTIA-1", monto="0", bajo_garantia="Sí"), tools[0])
        denied(lambda: call("mantenimiento_herramienta", dict(service, referencia="SIN-GARANTIA", bajo_garantia="Sí"), tools[1]))
        rows = detalle("Lebaref Herramienta", tools[0])["rows"]
        assert next(r for r in rows if r["campo"] == "Mantenimiento / adquisición")["valor"] == "60.0%"
        denied(lambda: esquema("anular_mantenimiento", maintenance), frappe.PermissionError)
        frappe.set_user("autoriza@lebaref.test")
        cancel_data = dict(motivo="Corrección del costo ficticio")
        token = str(uuid.uuid4())
        cancel = call("anular_mantenimiento", cancel_data, maintenance, token)
        assert call("anular_mantenimiento", cancel_data, maintenance, token) == cancel
        assert frappe.get_doc("Lebaref Mantenimiento Herramienta", maintenance).anulado
        denied(lambda: call("anular_mantenimiento", cancel_data, maintenance))
        frappe.set_user("almacen@lebaref.test")
        call("mantenimiento_herramienta", dict(service, monto="200.00"), tools[0])
        rows = detalle("Lebaref Herramienta", tools[0])["rows"]
        assert next(r for r in rows if r["campo"] == "Mantenimiento / adquisición")["valor"] == "20.0%"
        assert any(r["valor"] == "ANULADO" for r in rows)
        raw = frappe.get_doc("Lebaref Kit", kit)
        raw.set("items", [])
        denied(raw.save)
        evidence.append("Garantía, fechas y permisos; mantenimiento pagado/gratuito; duplicados rechazados; anulación y recaptura sin inflar acumulados")
        return dict(correcto=True, pruebas=evidence, datos="Revertidos al finalizar")
    finally:
        frappe.db.rollback()
        frappe.set_user(original)
