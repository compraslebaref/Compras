"""Composición de kits, garantía y costos operativos de herramientas."""
from decimal import Decimal, InvalidOperation
import hashlib
import json

import frappe
from frappe.utils import today, add_days, getdate
from lebaref_compras.operaciones import campo, columna, catalogo
from lebaref_compras.equipos import obtener, guardar, texto, fecha, crear_prestamo

ACTIONS = {"kit", "composicion_kit", "prestamo_kit", "garantia_herramienta", "mantenimiento_herramienta", "anular_mantenimiento"}


def esquema(action, source):
    fields, columns, rows = [], [], []
    note = "Registro operativo; no genera movimientos contables."
    if action in ("kit", "composicion_kit"):
        if action == "kit":
            fields = [campo("codigo", "Código del kit"), campo("nombre", "Nombre"), campo("company", "Empresa", choices=catalogo("Company")), campo("especialidad", "Especialidad"), campo("ubicacion", "Ubicación")]
        else:
            fields = [campo("motivo", "Motivo del cambio", kind="multiline")]
            rows = [dict(herramienta=r.herramienta) for r in source.items]
        columns = [columna("herramienta", "Herramienta individual", choices=catalogo("Lebaref Herramienta"))]
        note = "Una herramienta pertenece a un solo kit. No se cambia el contenido mientras una de sus herramientas esté prestada."
    elif action == "prestamo_kit":
        fields = [campo("obra", "Obra", choices=catalogo("Project")), campo("responsable", "Responsable"), campo("recibe", "Persona que recibe"), campo("departamento", "Departamento"), campo("compromiso", "Fecha compromiso", add_days(today(), 7), kind="date"), campo("notas", "Notas")]
        columns = [columna("herramienta", "Herramienta", False), columna("estado", "Estado actual", False), columna("incluir", "Incluir en préstamo", choices=["Sí", "No"])]
        for r in source.items:
            tool = obtener("Lebaref Herramienta", r.herramienta)
            rows.append(dict(herramienta=tool.name, estado=tool.estado, incluir="Sí" if tool.estado == "Disponible" else "No"))
        note = "Revisa cada renglón. Solo se prestan los marcados Sí; se vuelven a comprobar al guardar. Las devoluciones se hacen desde el préstamo generado."
    elif action == "garantia_herramienta":
        fields = [campo("fecha_compra", "Fecha de compra", str(source.fecha_compra or today()), kind="date"), campo("tiene_garantia", "Garantía registrada", "Sí" if source.fin_garantia else "No", choices=["No", "Sí"]), campo("fin_garantia", "Garantía hasta (si aplica)", str(source.fin_garantia or add_days(today(), 365)), kind="date"), campo("costo_adquisicion", "Costo de adquisición", str(source.costo_adquisicion or 0)), campo("referencia", "Comprobante / referencia"), campo("motivo", "Motivo del registro / ajuste", kind="multiline")]
        note = "Solo responsable. El costo usa la moneda de la empresa; cero indica costo no registrado. No comprueba la cobertura con el proveedor."
    elif action == "mantenimiento_herramienta":
        fields = [campo("fecha", "Fecha del trabajo", today(), kind="date"), campo("proveedor", "Proveedor / taller"), campo("referencia", "Folio de comprobante"), campo("monto", "Costo pagado (moneda empresa)", "0"), campo("bajo_garantia", "Atendido bajo garantía", choices=["No", "Sí"]), campo("motivo", "Trabajo realizado", kind="multiline")]
        note = "Un trabajo gratuito puede llevar costo cero. Este registro no libera una herramienta dañada o prestada; su estado se controla por separado."
    elif action == "anular_mantenimiento":
        fields = [campo("motivo", "Motivo de anulación", kind="multiline")]
        note = f"Anula el costo de {source.name}: {source.monto} {source.moneda}. Conserva el documento y su historial; no cambia la custodia."
    return dict(fields=fields, columns=columns, rows=rows, add_rows=action in ("kit", "composicion_kit"), note=note)


def importe(value):
    try:
        result = Decimal(str(value))
        if not result.is_finite() or result < 0 or result > Decimal("999999999.99") or result != result.quantize(Decimal("0.01")):
            raise ValueError()
        return result
    except (ValueError, InvalidOperation):
        frappe.throw("Captura un importe no negativo, con máximo dos decimales.")


def herramientas(data, company, kit=None, empty=False):
    keys = [texto(r, "herramienta") for r in data.get("rows", [])]
    if len(keys) != len(set(keys)) or (not empty and not keys):
        frappe.throw("Selecciona herramientas sin repetirlas.")
    tools = [obtener("Lebaref Herramienta", key, lock=True) for key in sorted(keys)]
    if any(t.company != company or t.prestamo or t.estado == "Prestada" for t in tools):
        frappe.throw("Las herramientas deben pertenecer a la empresa y estar fuera de préstamo.")
    if keys:
        memberships = frappe.get_all("Lebaref Kit Item", filters={"parenttype": "Lebaref Kit", "herramienta": ["in", keys]}, fields=["parent", "herramienta"])
        if any(m.parent != kit for m in memberships):
            frappe.throw("Una herramienta ya pertenece a otro kit. Retírala primero de ese kit.")
    return keys


def aplicar(action, data, source, doc):
    if action == "kit":
        company = texto(data, "company")
        obtener("Company", company)
        keys = herramientas(data, company)
        doc.update(dict(codigo=texto(data, "codigo").upper(), nombre=texto(data, "nombre"), company=company, especialidad=texto(data, "especialidad", False), ubicacion=texto(data, "ubicacion"), items=[dict(herramienta=k) for k in keys]))
    elif action == "composicion_kit":
        for r in source.items:
            tool = obtener("Lebaref Herramienta", r.herramienta, lock=True)
            if tool.prestamo or tool.estado == "Prestada":
                frappe.throw("El kit tiene herramientas prestadas. Registra sus retornos antes de cambiar el contenido.")
        keys = herramientas(data, source.company, source.name, empty=True)
        doc.update(dict(kit=source.name, fecha=today(), motivo=texto(data, "motivo"), anterior=", ".join(r.herramienta for r in source.items), items=[dict(herramienta=k) for k in keys]))
        source.set("items", [dict(herramienta=k) for k in keys])
        guardar(source)
    elif action == "prestamo_kit":
        rows = data.get("rows", [])
        keys = [r.get("herramienta") for r in rows]
        if len(keys) != len(set(keys)) or set(keys) != {r.herramienta for r in source.items} or any(r.get("incluir") not in ("Sí", "No") for r in rows):
            frappe.throw("El contenido del kit cambió o la selección es inválida. Cierra y vuelve a abrir la operación.")
        selected = [r["herramienta"] for r in rows if r["incluir"] == "Sí"]
        doc.kit = source.name
        crear_prestamo(doc, dict(data, company=source.company), selected)
        source.ultimo_prestamo = doc.name
        guardar(source)
        return dict(folio=doc.name, tipo=doc.doctype)
    elif action == "garantia_herramienta":
        purchased = fecha(data.get("fecha_compra"))
        if purchased > getdate(today()):
            frappe.throw("La compra no puede tener fecha futura.")
        if data.get("tiene_garantia") not in ("No", "Sí"):
            frappe.throw("Indica si la garantía está registrada.")
        end = fecha(data.get("fin_garantia")) if data["tiene_garantia"] == "Sí" else None
        if end and end < purchased:
            frappe.throw("La garantía no puede terminar antes de la compra.")
        amount = importe(data.get("costo_adquisicion"))
        currency = frappe.db.get_value("Company", source.company, "default_currency")
        doc.update(dict(herramienta=source.name, fecha=today(), fecha_compra=purchased, fin_garantia=end, moneda=currency, costo_adquisicion=amount, referencia=texto(data, "referencia"), motivo=texto(data, "motivo"), anterior=frappe.as_json({k: source.get(k) for k in ("fecha_compra", "fin_garantia", "costo_adquisicion", "moneda")})))
        source.update(dict(fecha_compra=purchased, fin_garantia=end, moneda=currency, costo_adquisicion=amount))
        guardar(source)
    elif action == "mantenimiento_herramienta":
        day = fecha(data.get("fecha"))
        if day > getdate(today()) or (source.fecha_compra and day < getdate(source.fecha_compra)):
            frappe.throw("La fecha del mantenimiento debe estar entre la compra y hoy.")
        under_warranty = data.get("bajo_garantia")
        if under_warranty not in ("Sí", "No"):
            frappe.throw("Indica si fue atendido bajo garantía.")
        if under_warranty == "Sí" and (not source.fin_garantia or day > getdate(source.fin_garantia)):
            frappe.throw("No hay garantía registrada vigente en la fecha del trabajo.")
        provider, reference = texto(data, "proveedor"), texto(data, "referencia")
        key = hashlib.sha256(json.dumps([source.name, provider.casefold(), reference.casefold()]).encode()).hexdigest()
        if frappe.db.exists("Lebaref Mantenimiento Herramienta", {"clave_referencia": key, "anulado": 0}):
            frappe.throw("Ya existe ese comprobante de mantenimiento para la herramienta.")
        doc.update(dict(herramienta=source.name, fecha=day, proveedor=provider, referencia=reference, clave_referencia=key, moneda=frappe.db.get_value("Company", source.company, "default_currency"), monto=importe(data.get("monto")), bajo_garantia=under_warranty, motivo=texto(data, "motivo"), anulado=0))
    elif action == "anular_mantenimiento":
        if source.anulado:
            frappe.throw("El mantenimiento ya está anulado.")
        doc.update(dict(mantenimiento=source.name, herramienta=source.herramienta, fecha=today(), motivo=texto(data, "motivo")))
        source.anulado = 1
        # Releases the unique active-reference slot; the original reference and
        # audit document remain, so a corrected entry can reuse the receipt.
        source.clave_referencia = None
        guardar(source)
    guardar(doc)
    return dict(folio=doc.name, tipo=doc.doctype)


def agregar_detalle(doc, row):
    if doc.doctype in ("Lebaref Kit", "Lebaref Composicion Kit"):
        available = 0
        for item in doc.items:
            tool = obtener("Lebaref Herramienta", item.herramienta)
            available += int(tool.estado == "Disponible" and not tool.prestamo)
            row(tool.name, tool.estado, f"{tool.responsable or 'En almacén'} | {tool.obra or tool.ubicacion} | {tool.prestamo or ''}")
        row("Disponibilidad actual", f"{available}/{len(doc.items)}", "El préstamo permite seleccionar los renglones que salen.")
    if doc.doctype == "Lebaref Kit":
        for loan in frappe.get_list("Lebaref Prestamo", filters={"kit": doc.name}, fields=["name", "estado", "responsable", "fecha"], order_by="creation desc", limit_page_length=100):
            row("Préstamo "+loan.name, loan.estado, f"{loan.fecha} | {loan.responsable}")
    if doc.doctype == "Lebaref Herramienta":
        if doc.fin_garantia:
            remaining = (getdate(doc.fin_garantia)-getdate(today())).days
            row("Garantía", "Vigente" if remaining >= 0 else "Vencida", f"Hasta {doc.fin_garantia}; plazo registrado, no validado con el proveedor")
        else:
            row("Garantía", "Sin registro")
        entries = frappe.get_list("Lebaref Mantenimiento Herramienta", filters={"herramienta": doc.name}, fields=["name", "fecha", "monto", "moneda", "anulado", "motivo"], order_by="creation desc", limit_page_length=0)
        active = [r for r in entries if not r.anulado]
        currency = frappe.db.get_value("Company", doc.company, "default_currency")
        if any(r.moneda != currency for r in active) or (doc.moneda and doc.moneda != currency):
            row("Costo acumulado", "Revisar monedas", "No se sumaron importes en monedas distintas.")
        else:
            total = sum((Decimal(str(r.monto)) for r in active), Decimal("0"))
            row("Mantenimiento acumulado", str(total), currency)
            acquisition = Decimal(str(doc.costo_adquisicion or 0))
            if acquisition > 0:
                ratio = total/acquisition*100
                row("Mantenimiento / adquisición", f"{ratio:.1f}%", "Revisar conveniencia de reparación" if ratio >= 50 else "Costo registrado; no es depreciación contable")
        for entry in entries[:100]:
            row("Mantenimiento "+entry.name, "ANULADO" if entry.anulado else f"{entry.monto} {entry.moneda}", f"{entry.fecha} | {entry.motivo}")
