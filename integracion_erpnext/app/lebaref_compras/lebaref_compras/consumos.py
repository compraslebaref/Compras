"""Consumo y saldo bajo resguardo ligados a entregas de material."""
import frappe
from frappe.utils import flt
from lebaref_compras.unidades import numero


def validar_entrega(source):
    source.check_permission("read")
    if source.docstatus != 1 or source.purpose != "Material Transfer" or source.custom_lebaref_origen or source.get("custom_lebaref_tipo_sobrante"):
        frappe.throw("Selecciona una entrega original confirmada de material.")


def movimientos(source, exclude="", lock=False):
    # The caller has read access to the original delivery. Aggregate its dependent rows.
    return frappe.db.sql("""select d.custom_lebaref_renglon as renglon,
        d.transfer_qty, d.amount, e.purpose
        from `tabStock Entry Detail` d inner join `tabStock Entry` e on e.name=d.parent
        where e.custom_lebaref_origen=%s and e.docstatus=1 and d.docstatus=1 and e.name!=%s"""
        + (" for update" if lock else ""), (source.name, exclude), as_dict=True)


def saldos(source, exclude="", lock=False):
    result = {r.name: dict(consumido=0.0, devuelto=0.0, costo_consumido=0.0) for r in source.items}
    for move in movimientos(source, exclude, lock):
        if move.renglon not in result:
            frappe.throw("Hay un movimiento vinculado con referencia inválida. Revisa la entrega.")
        values = result[move.renglon]
        if move.purpose == "Material Issue":
            values["consumido"] += move.transfer_qty
            values["costo_consumido"] += move.amount
        elif move.purpose == "Material Transfer":
            values["devuelto"] += move.transfer_qty
    for row in source.items:
        values = result[row.name]
        for key in ("consumido", "devuelto"):
            values[key] = flt(values[key] / row.conversion_factor, row.precision("qty"))
        values["saldo"] = flt(row.qty - values["consumido"] - values["devuelto"], row.precision("qty"))
    return result


def crear(source, data, selections):
    validar_entrega(source)
    if not str(data.get("remarks") or "").strip():
        frappe.throw("Indica el motivo o actividad del consumo en obra.")
    balances = saldos(source, lock=True)
    items = []
    for row in source.items:
        qty = selections.get(row.name, 0)
        if not qty:
            continue
        if qty > balances[row.name]["saldo"]:
            frappe.throw("El consumo excede el saldo bajo resguardo de la entrega.")
        project = row.get("project") or source.project
        if not project:
            frappe.throw("La entrega debe identificar la obra para registrar su consumo.")
        items.append(dict(item_code=row.item_code, qty=qty, uom=row.uom, conversion_factor=row.conversion_factor,
            s_warehouse=row.t_warehouse, project=project, custom_lebaref_partida=row.custom_lebaref_partida,
            custom_lebaref_renglon=row.name))
    return frappe.get_doc(dict(doctype="Stock Entry", stock_entry_type="Material Issue", company=source.company,
        project=source.project, custom_lebaref_origen=source.name, remarks=data["remarks"], items=items))


def validar_movimiento(doc, method=None):
    linked_rows = [r for r in doc.items if r.custom_lebaref_renglon]
    if not doc.custom_lebaref_origen and not linked_rows:
        return
    if not doc.custom_lebaref_origen or len(linked_rows) != len(doc.items):
        frappe.throw("Identifica la entrega y el renglón de origen en todo el movimiento vinculado.")
    source = frappe.get_doc("Stock Entry", doc.custom_lebaref_origen, for_update=True)
    validar_entrega(source)
    if doc.company != source.company or doc.purpose not in ("Material Issue", "Material Transfer"):
        frappe.throw("El movimiento vinculado debe ser consumo o devolución de la misma empresa.")
    available = {r.name: r for r in source.items}
    totals = {}
    balances = saldos(source, doc.name, lock=True)
    for row in doc.items:
        origin = available.get(row.custom_lebaref_renglon)
        if not origin or row.item_code != origin.item_code or row.s_warehouse != origin.t_warehouse:
            frappe.throw("El artículo o almacén no coincide con la entrega original.")
        if (row.custom_lebaref_partida or "") != (origin.custom_lebaref_partida or ""):
            frappe.throw("La partida debe coincidir con la entrega original.")
        expected_target = origin.s_warehouse if doc.purpose == "Material Transfer" else None
        if (row.t_warehouse or None) != expected_target:
            frappe.throw("El destino no corresponde a la devolución o consumo de la entrega.")
        project = origin.get("project") or source.project
        if (row.get("project") or doc.project or "") != (project or ""):
            frappe.throw("La obra debe coincidir con la entrega original.")
        if doc.purpose == "Material Issue" and (not project or not str(doc.remarks or "").strip()):
            frappe.throw("El consumo requiere obra y motivo o actividad.")
        qty = numero(row.transfer_qty) / origin.conversion_factor
        totals[origin.name] = totals.get(origin.name, 0) + qty
        if flt(totals[origin.name] - balances[origin.name]["saldo"], origin.precision("qty")) > 0:
            frappe.throw("El movimiento excede el saldo bajo resguardo de la entrega.")


def antes_de_cancelar(doc, method=None):
    if doc.custom_lebaref_origen:
        frappe.get_doc("Stock Entry", doc.custom_lebaref_origen, for_update=True)
    elif frappe.db.get_value("Stock Entry", {"custom_lebaref_origen": doc.name, "docstatus": 1}, "name", for_update=True):
        frappe.throw("La entrega tiene consumos o devoluciones vigentes. Revisa esos movimientos antes de anularla.")


@frappe.whitelist(methods=["GET"])
def consultar(entrega):
    source = frappe.get_doc("Stock Entry", entrega)
    validar_entrega(source)
    balances = saldos(source)
    show_cost = frappe.has_permission("GL Entry", "read")
    rows = []
    for row in source.items:
        values = balances[row.name]
        result = dict(id=row.name, item_code=row.item_code, uom=row.uom, entregado=row.qty,
            consumido=values["consumido"], devuelto=values["devuelto"], saldo=values["saldo"],
            warehouse=row.t_warehouse, project=row.get("project") or source.project)
        if show_cost:
            result["costo_consumido"] = values["costo_consumido"]
        rows.append(result)
    return dict(entrega=source.name, puede_ver_costos=show_cost,
        moneda=frappe.get_cached_value("Company", source.company, "default_currency"), rows=rows)
