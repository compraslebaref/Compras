"""Custodia individual y bitácora de flotilla. No genera asientos contables."""
from contextvars import ContextVar
from decimal import Decimal, InvalidOperation
import hashlib
import json
import uuid

import frappe
from frappe.utils import today, getdate, add_days
from lebaref_compras.operaciones import campo, columna, catalogo, numero

TARGETS = {
    "herramienta": "Lebaref Herramienta", "prestamo": "Lebaref Prestamo", "retorno_herramienta": "Lebaref Retorno",
    "estado_herramienta": "Lebaref Estado Herramienta", "vehiculo": "Lebaref Vehiculo", "gasto_vehiculo": "Lebaref Gasto Vehiculo",
    "revision_vehiculo": "Lebaref Revision Vehiculo", "estado_vehiculo": "Lebaref Estado Vehiculo",
    "kit": "Lebaref Kit", "composicion_kit": "Lebaref Composicion Kit", "prestamo_kit": "Lebaref Prestamo",
    "garantia_herramienta": "Lebaref Garantia Herramienta", "mantenimiento_herramienta": "Lebaref Mantenimiento Herramienta",
    "anular_mantenimiento": "Lebaref Anulacion Mantenimiento",
}
SOURCES = {"retorno_herramienta": "Lebaref Prestamo", "estado_herramienta": "Lebaref Herramienta",
           "gasto_vehiculo": "Lebaref Vehiculo", "revision_vehiculo": "Lebaref Vehiculo", "estado_vehiculo": "Lebaref Vehiculo"}
SOURCES.update({"composicion_kit": "Lebaref Kit", "prestamo_kit": "Lebaref Kit", "garantia_herramienta": "Lebaref Herramienta", "mantenimiento_herramienta": "Lebaref Herramienta", "anular_mantenimiento": "Lebaref Mantenimiento Herramienta"})
CONDITIONS = ["Bueno", "Dañada", "Incompleta", "Extraviada"]
EXPENSES = ["Combustible", "Mantenimiento preventivo", "Reparación", "Llantas / refacciones", "Seguro / peajes"]
CHECKS = ["OK", "Atención", "Falla"]
_writing = ContextVar("lebaref_equipos_writing", default=False)


def proteger(doc, method=None):
    # A request cannot opt into this flag by passing JSON document.flags.
    if not _writing.get():
        frappe.throw("Usa las operaciones de herramientas y flotilla para conservar su historial.")


def guardar(doc):
    token = _writing.set(True)
    try:
        return doc.insert() if doc.is_new() else doc.save()
    finally:
        _writing.reset(token)


def permiso(action):
    if action not in TARGETS or not frappe.has_permission(TARGETS[action], "create"):
        frappe.throw("Tu cuenta no tiene permiso para esta operación.", frappe.PermissionError)


def obtener(dt, name, lock=False):
    doc = frappe.get_doc(dt, name, for_update=lock)
    doc.check_permission("read")
    return doc


def texto(data, name, required=True):
    value = str(data.get(name) or "").strip()
    if (required and not value) or len(value) > 1000:
        frappe.throw(f"Revisa el campo {name}: falta un valor o es demasiado largo.")
    return value


def fecha(value):
    if not value:
        frappe.throw("Captura una fecha válida.")
    try:
        return getdate(value)
    except (ValueError, TypeError):
        frappe.throw("La fecha no es válida.")


def entero(value, positive=False):
    n = numero(value, zero=not positive)
    if n != int(n) or n > 2_000_000_000:
        frappe.throw("El kilometraje y el intervalo deben ser enteros válidos.")
    return int(n)


def proyecto(name, company, required=True):
    if not name:
        if required:
            frappe.throw("Selecciona una obra.")
        return
    doc = obtener("Project", name)
    if doc.company and doc.company != company:
        frappe.throw("La obra no corresponde a la empresa seleccionada.")


def esquema(accion, folio=""):
    permiso(accion)
    doc = obtener(SOURCES[accion], folio) if accion in SOURCES else None
    from lebaref_compras import kits
    if accion in kits.ACTIONS:
        return kits.esquema(accion, doc)
    fields, columns, rows = [], [], []
    note = "El registro se guarda en ERPNext con el usuario y la fecha."
    if accion == "herramienta":
        fields = [campo("codigo", "Código individual"), campo("company", "Empresa", choices=catalogo("Company")), campo("articulo", "Artículo", choices=catalogo("Item", {"disabled": 0})), campo("serie", "Número de serie"), campo("marca_modelo", "Marca y modelo"), campo("ubicacion", "Ubicación / gabinete")]
        note = "Alta de una herramienta física individual. Controla custodia; no agrega existencias ni registra su compra."
    elif accion == "prestamo":
        fields = [campo("company", "Empresa", choices=catalogo("Company")), campo("obra", "Obra", choices=catalogo("Project")), campo("responsable", "Responsable"), campo("recibe", "Persona que recibe"), campo("departamento", "Departamento"), campo("compromiso", "Fecha compromiso", add_days(today(), 7), kind="date"), campo("notas", "Notas")]
        columns = [columna("herramienta", "Herramienta individual", choices=catalogo("Lebaref Herramienta", {"estado": "Disponible"}))]
        note = "Selecciona herramientas disponibles. La salida es hoy; el préstamo conserva responsable, obra y fecha compromiso."
    elif accion == "retorno_herramienta":
        fields = [campo("recibe", "Recibe en almacén")]
        columns = [columna("id", "Renglón", False), columna("herramienta", "Herramienta", False), columna("estado", "Retorno ahora", choices=["Pendiente"] + CONDITIONS), columna("observaciones", "Observaciones / incidencia")]
        rows = [dict(id=r.name, herramienta=r.herramienta, estado="Pendiente", observaciones="") for r in doc.items if r.estado == "Pendiente"]
        note = "Pendiente omite el renglón. Las incidencias requieren observación y no dejan la herramienta disponible."
    elif accion == "estado_herramienta":
        fields = [campo("estado", "Estado", doc.estado, choices=["Disponible", "Dañada", "Incompleta", "Extraviada", "Baja"]), campo("ubicacion", "Ubicación", doc.ubicacion), campo("motivo", "Motivo / reparación", kind="multiline")]
        note = "Solo responsable. No se cambia una herramienta prestada: primero registra su retorno o extravío en el préstamo."
    elif accion == "vehiculo":
        fields = [campo("codigo", "Código del vehículo"), campo("placas", "Placas"), campo("company", "Empresa", choices=catalogo("Company")), campo("marca_modelo", "Marca y modelo"), campo("responsable", "Responsable habitual"), campo("km", "Kilometraje inicial", "0"), campo("fecha", "Fecha de lectura", today(), kind="date"), campo("intervalo", "Intervalo servicio (km)", "10000"), campo("proximo_servicio", "Próximo servicio (km)"), campo("capacidad", "Tanque (litros)", "60"), campo("combustible", "Combustible", choices=["Gasolina Regular", "Gasolina Premium", "Diésel"]), campo("seguro", "Seguro vigente hasta", today(), kind="date")]
    elif accion == "gasto_vehiculo":
        fields = [campo("fecha", "Fecha del gasto", today(), kind="date"), campo("tipo", "Tipo de gasto", choices=EXPENSES), campo("km", "Kilometraje", str(doc.km_actual)), campo("litros", "Litros (solo combustible)", "0"), campo("tanque_lleno", "Tanque lleno", choices=["No", "Sí"]), campo("monto", "Importe total (" + frappe.db.get_value("Company", doc.company, "default_currency") + ")"), campo("proveedor", "Proveedor / taller"), campo("referencia", "Folio de comprobante"), campo("obra", "Obra (opcional)", choices=[""]+catalogo("Project")), campo("descripcion", "Descripción", kind="multiline")]
        note = "Gasto operativo, sin contabilización. Captura en orden de fecha/km. Un mantenimiento preventivo reprograma el servicio."
    elif accion == "revision_vehiculo":
        fields = [campo("fecha", "Fecha revisión", today(), kind="date"), campo("km", "Kilometraje", str(doc.km_actual)), campo("inspector", "Inspector"), campo("nivel", "Nivel de combustible", choices=["Vacío", "1/4", "1/2", "3/4", "Lleno"])]
        fields += [campo(key, label, choices=CHECKS) for key, label in (("llantas", "Llantas"), ("fluidos", "Niveles / fluidos"), ("frenos_luces", "Frenos / luces"), ("carroceria", "Carrocería / limpieza"))]
        fields += [campo("observaciones", "Observaciones", kind="multiline")]
        note = "Registra lo observado. Una falla requiere descripción; la revisión no autoriza la circulación del vehículo."
    elif accion == "estado_vehiculo":
        fields = [campo("estado", "Estado", doc.estado, choices=["Activo", "En taller", "Fuera de servicio"]), campo("responsable", "Responsable", doc.responsable), campo("seguro", "Seguro vigente hasta", str(doc.seguro), kind="date"), campo("motivo", "Motivo / referencia", kind="multiline")]
    return dict(fields=fields, columns=columns, rows=rows, add_rows=accion == "prestamo", note=note)


def ejecutar(accion, datos, operacion, folio=""):
    permiso(accion)
    data = frappe.parse_json(datos)
    if not isinstance(data, dict):
        frappe.throw("Datos inválidos.")
    try:
        token = str(uuid.UUID(operacion))
    except (ValueError, TypeError, AttributeError):
        frappe.throw("Identificador de operación inválido.")
    signature = hashlib.sha256(json.dumps([accion, folio, data], sort_keys=True, ensure_ascii=False).encode()).hexdigest()
    frappe.get_doc("User", frappe.session.user, for_update=True)
    # Serialize these short custody transactions on an existing row. In MariaDB
    # REPEATABLE READ, missing UUID lookups hold gap locks: two users could each
    # hold such a lock and then wait for the same tool while inserting a loan.
    # This mutex is released by the HTTP transaction commit/rollback, not early.
    frappe.db.sql("select name from `tabDocType` where name=%s for update", ("Lebaref Herramienta",))
    previous = frappe.db.get_value(TARGETS[accion], {"operacion": token}, ["name", "huella"], as_dict=True, for_update=True)
    if previous:
        obtener(TARGETS[accion], previous.name)
        if previous.huella != signature:
            frappe.throw("Este intento ya se guardó con otros datos. Consulta el documento antes de abrir otra captura.")
        return dict(folio=previous.name, tipo=TARGETS[accion], repetida=True)
    source = obtener(SOURCES[accion], folio, lock=True) if accion in SOURCES else None
    doc = frappe.new_doc(TARGETS[accion])
    doc.operacion, doc.huella = token, signature
    from lebaref_compras import kits
    if accion in kits.ACTIONS:
        return kits.aplicar(accion, data, source, doc)
    if accion == "herramienta":
        company = texto(data, "company")
        obtener("Company", company)
        item = obtener("Item", texto(data, "articulo"))
        if item.disabled:
            frappe.throw("El artículo está deshabilitado.")
        doc.update(dict(codigo=texto(data, "codigo").upper(), company=company, articulo=item.name, serie=texto(data, "serie", False), marca_modelo=texto(data, "marca_modelo", False), ubicacion=texto(data, "ubicacion"), estado="Disponible"))
    elif accion == "prestamo":
        keys = [texto(row, "herramienta") for row in data.get("rows", [])]
        crear_prestamo(doc, data, keys)
        return dict(folio=doc.name, tipo=doc.doctype)
    elif accion == "retorno_herramienta":
        origins = {r.name: r for r in source.items}
        selected, seen = [], set()
        for row in data.get("rows", []):
            key = row.get("id")
            if key not in origins or key in seen:
                frappe.throw("Renglón repetido o ajeno al préstamo.")
            seen.add(key)
            condition = row.get("estado")
            if condition == "Pendiente":
                continue
            if origins[key].estado != "Pendiente" or condition not in CONDITIONS:
                frappe.throw("La herramienta ya fue devuelta o la condición no es válida.")
            note = texto(row, "observaciones", condition != "Bueno")
            selected.append((origins[key], condition, note))
        if not selected:
            frappe.throw("Selecciona al menos una herramienta para retornar.")
        doc.update(dict(prestamo=source.name, fecha=today(), recibe=texto(data, "recibe")))
        for row, condition, note in sorted(selected, key=lambda x: x[0].herramienta):
            tool = obtener("Lebaref Herramienta", row.herramienta, lock=True)
            if tool.prestamo != source.name or tool.estado != "Prestada":
                frappe.throw("El estado de la herramienta cambió. Actualiza el préstamo.")
            doc.append("items", dict(renglon=row.name, herramienta=tool.name, estado=condition, observaciones=note))
            row.update(dict(estado=condition, fecha_retorno=today(), observaciones=note))
            tool.update(dict(estado="Disponible" if condition == "Bueno" else condition, prestamo=None, responsable=None, obra=None, notas=note))
            guardar(tool)
        states = [r.estado for r in source.items]
        source.estado = "Devuelto parcial" if "Pendiente" in states else ("Devuelto completo" if all(s == "Bueno" for s in states) else "Devuelto con incidencia")
        guardar(source)
    elif accion == "estado_herramienta":
        if source.prestamo or source.estado == "Prestada":
            frappe.throw("Registra primero el retorno desde su préstamo.")
        state = data.get("estado")
        if state not in ("Disponible", "Dañada", "Incompleta", "Extraviada", "Baja"):
            frappe.throw("Estado inválido.")
        doc.update(dict(herramienta=source.name, fecha=today(), estado_anterior=source.estado, estado=state, ubicacion=texto(data, "ubicacion"), motivo=texto(data, "motivo")))
        source.update(dict(estado=state, ubicacion=doc.ubicacion, notas=doc.motivo))
        guardar(source)
    elif accion == "vehiculo":
        company = texto(data, "company")
        obtener("Company", company)
        km, interval, next_km = entero(data.get("km")), entero(data.get("intervalo"), True), entero(data.get("proximo_servicio"), True)
        measured = fecha(data.get("fecha"))
        if measured > getdate(today()):
            frappe.throw("La lectura de kilometraje no puede ser futura.")
        if next_km < km:
            frappe.throw("En el alta captura una meta de servicio igual o posterior al kilometraje inicial.")
        fuel = data.get("combustible")
        if fuel not in ("Gasolina Regular", "Gasolina Premium", "Diésel"):
            frappe.throw("Selecciona el combustible.")
        doc.update(dict(codigo=texto(data, "codigo").upper(), placas=texto(data, "placas").upper().replace(" ", "").replace("-", ""), company=company, marca_modelo=texto(data, "marca_modelo"), responsable=texto(data, "responsable"), km_inicial=km, km_actual=km, fecha_lectura=measured, intervalo=interval, proximo_servicio=next_km, capacidad=numero(data.get("capacidad")), combustible=fuel, seguro=fecha(data.get("seguro")), estado="Activo"))
    elif accion in ("gasto_vehiculo", "revision_vehiculo"):
        measured, km = fecha(data.get("fecha")), entero(data.get("km"))
        if measured > getdate(today()) or measured < getdate(source.fecha_lectura) or km < source.km_actual:
            frappe.throw("Captura en orden cronológico: fecha no futura ni anterior a la última lectura, y kilometraje no decreciente.")
        doc.update(dict(vehiculo=source.name, fecha=measured, km=km))
        if accion == "gasto_vehiculo":
            if data.get("tipo") not in EXPENSES:
                frappe.throw("Tipo de gasto inválido.")
            try:
                money = Decimal(str(data.get("monto")))
                if not money.is_finite() or money <= 0 or money > Decimal("999999999.99") or money != money.quantize(Decimal("0.01")):
                    raise ValueError()
            except (ValueError, InvalidOperation):
                frappe.throw("Captura un importe positivo con máximo dos decimales.")
            liters = numero(data.get("litros", 0), zero=True)
            if data["tipo"] == "Combustible":
                if liters <= 0 or liters > source.capacidad or data.get("tanque_lleno") not in ("Sí", "No"):
                    frappe.throw("Revisa litros, capacidad del tanque y si quedó lleno.")
            elif liters:
                frappe.throw("Los litros solo corresponden a combustible.")
            supplier, reference = texto(data, "proveedor"), texto(data, "referencia")
            key = hashlib.sha256(json.dumps([source.name, supplier.casefold(), reference.casefold()]).encode()).hexdigest()
            if frappe.db.exists("Lebaref Gasto Vehiculo", {"clave_referencia": key}):
                frappe.throw("Este comprobante ya tiene un gasto registrado para el vehículo.")
            proyecto(data.get("obra"), source.company, required=False)
            doc.update(dict(tipo=data["tipo"], litros=liters, tanque_lleno=data.get("tanque_lleno") if liters else "No", moneda=frappe.db.get_value("Company", source.company, "default_currency"), monto=money, proveedor=supplier, referencia=reference, clave_referencia=key, obra=data.get("obra") or None, descripcion=texto(data, "descripcion")))
            if doc.tipo == "Mantenimiento preventivo":
                source.ultimo_servicio, source.proximo_servicio = km, entero(km+source.intervalo, True)
        else:
            checks = {k: data.get(k) for k in ("llantas", "fluidos", "frenos_luces", "carroceria")}
            if any(v not in CHECKS for v in checks.values()) or data.get("nivel") not in ("Vacío", "1/4", "1/2", "3/4", "Lleno"):
                frappe.throw("Completa los puntos de la revisión.")
            doc.update(dict(inspector=texto(data, "inspector"), nivel=data["nivel"], observaciones=texto(data, "observaciones", any(v != "OK" for v in checks.values())), **checks))
        source.km_actual, source.fecha_lectura = km, measured
        guardar(source)
    elif accion == "estado_vehiculo":
        if data.get("estado") not in ("Activo", "En taller", "Fuera de servicio"):
            frappe.throw("Estado inválido.")
        doc.update(dict(vehiculo=source.name, fecha=today(), estado_anterior=source.estado, estado=data["estado"], responsable=texto(data, "responsable"), seguro=fecha(data.get("seguro")), motivo=texto(data, "motivo")))
        source.update(dict(estado=doc.estado, responsable=doc.responsable, seguro=doc.seguro))
        guardar(source)
    guardar(doc)
    return dict(folio=doc.name, tipo=doc.doctype)


def crear_prestamo(doc, data, keys):
    company = texto(data, "company")
    obtener("Company", company)
    proyecto(data.get("obra"), company)
    if fecha(data.get("compromiso")) < getdate(today()):
        frappe.throw("La fecha compromiso no puede ser anterior a hoy.")
    if not keys or len(keys) != len(set(keys)):
        frappe.throw("Selecciona herramientas sin repetirlas.")
    tools = [obtener("Lebaref Herramienta", key, lock=True) for key in sorted(keys)]
    if any(t.estado != "Disponible" or t.prestamo or t.company != company for t in tools):
        frappe.throw("Una herramienta ya está prestada, no está disponible o pertenece a otra empresa.")
    doc.update(dict(company=company, obra=data["obra"], fecha=today(), compromiso=fecha(data["compromiso"]), responsable=texto(data, "responsable"), recibe=texto(data, "recibe"), departamento=texto(data, "departamento", False), notas=texto(data, "notas", False), estado="Activo", items=[dict(herramienta=key, estado="Pendiente") for key in keys]))
    guardar(doc)
    for tool in tools:
        tool.update(dict(estado="Prestada", prestamo=doc.name, responsable=doc.responsable, obra=doc.obra))
        guardar(tool)


@frappe.whitelist(methods=["GET"])
def detalle(tipo, folio):
    if tipo not in set(TARGETS.values()):
        frappe.throw("Consulta no admitida.")
    doc = obtener(tipo, folio)
    rows = []
    def row(label, value, info=""):
        rows.append(dict(campo=label, valor=value, detalle=info))
    row("Registrado por", doc.owner, str(doc.creation))
    row("Última modificación", doc.modified_by, str(doc.modified))
    for field in doc.meta.fields:
        if field.fieldname in ("operacion", "huella", "clave_referencia") or field.fieldtype == "Table":
            continue
        value = doc.get(field.fieldname)
        if value is not None and value != "":
            row(field.label, value)
    if tipo in ("Lebaref Prestamo", "Lebaref Retorno"):
        for item in doc.items:
            row(item.herramienta, item.estado, item.observaciones or "")
        if tipo == "Lebaref Prestamo" and any(i.estado == "Pendiente" for i in doc.items):
            row("Vencimiento", "VENCIDO" if getdate(doc.compromiso) < getdate(today()) else "En plazo", str(doc.compromiso))
    if tipo == "Lebaref Herramienta":
        for item in frappe.get_all("Lebaref Prestamo Item", filters={"herramienta": doc.name}, fields=["parent", "estado", "fecha_retorno"], order_by="creation desc", limit_page_length=100):
            parent = frappe.get_doc("Lebaref Prestamo", item.parent)
            if parent.has_permission("read"):
                row("Préstamo "+parent.name, item.estado, f"{parent.responsable} | {parent.obra} | Salida {parent.fecha} | Retorno {item.fecha_retorno or 'pendiente'}")
    if tipo == "Lebaref Vehiculo":
        remaining = doc.proximo_servicio-doc.km_actual
        row("Servicio", "VENCIDO" if remaining <= 0 else ("PRÓXIMO" if remaining <= 800 else "En plazo"), f"{remaining} km hasta la meta")
        days = (getdate(doc.seguro)-getdate(today())).days
        row("Seguro", "VENCIDO" if days < 0 else ("PRÓXIMO" if days <= 30 else "En plazo"), f"{days} días restantes")
        expenses = frappe.get_list("Lebaref Gasto Vehiculo", filters={"vehiculo": doc.name}, fields=["name", "fecha", "tipo", "monto", "moneda", "litros", "referencia"], order_by="fecha desc, creation desc", limit_page_length=0)
        total = sum(Decimal(str(x.monto)) for x in expenses)
        row("Gasto total registrado", str(total), frappe.db.get_value("Company", doc.company, "default_currency"))
        for expense in expenses[:100]:
            row("Gasto "+expense.name, f"{expense.monto} {expense.moneda}", f"{expense.fecha} | {expense.tipo} | {expense.referencia}")
        for review in frappe.get_list("Lebaref Revision Vehiculo", filters={"vehiculo": doc.name}, fields=["name", "fecha", "inspector", "observaciones"], order_by="fecha desc, creation desc", limit_page_length=100):
            row("Revisión "+review.name, str(review.fecha), f"{review.inspector} | {review.observaciones or ''}")
    from lebaref_compras.kits import agregar_detalle
    agregar_detalle(doc, row)
    return dict(columns=[columna("campo", "Dato / evento", False), columna("valor", "Valor / estado", False), columna("detalle", "Detalle", False)], rows=rows, note=doc.name)
