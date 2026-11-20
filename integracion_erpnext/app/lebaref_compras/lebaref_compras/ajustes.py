"""Conteo y merma con conciliación nativa y revisión de existencias observadas."""
import hashlib
import json
import frappe
from frappe.utils import flt, today, nowtime
from lebaref_compras.unidades import numero


def autorizar(doc=None, method=None):
    if not frappe.has_permission("Stock Reconciliation", "submit"):
        frappe.throw("Tu cuenta no tiene permiso para autorizar ajustes.", frappe.PermissionError)
    if doc:
        comprobar_observacion(doc)


def bin_actual(item, warehouse):
    row = frappe.db.get_value("Bin", {"item_code": item, "warehouse": warehouse},
        ["actual_qty", "valuation_rate"], as_dict=True, for_update=True)
    return row or frappe._dict(actual_qty=0, valuation_rate=0)


def crear(data):
    if data.get("tipo") not in ("Conteo", "Merma"):
        frappe.throw("Selecciona Conteo o Merma.")
    rows = data.get("rows")
    if not isinstance(rows, list) or not rows:
        frappe.throw("Captura los renglones del conteo.")
    doc = frappe.get_doc(dict(doctype="Stock Reconciliation", company=data.get("company"), purpose="Stock Reconciliation",
        custom_lebaref_tipo_ajuste=data["tipo"], custom_lebaref_motivo_ajuste=data.get("motivo"),
        custom_lebaref_referencia_ajuste=data.get("referencia")))
    for row in rows:
        if not isinstance(row, dict):
            frappe.throw("Renglón de conteo inválido.")
        item = frappe.get_doc("Item", row.get("item_code"))
        item.check_permission("read")
        warehouse = frappe.get_doc("Warehouse", row.get("warehouse"))
        warehouse.check_permission("read")
        if item.disabled or not item.is_stock_item or warehouse.company != doc.company or warehouse.disabled or warehouse.is_group:
            frappe.throw("Selecciona artículo inventariable y almacén activo de detalle de la empresa.")
        current = bin_actual(item.name, warehouse.name)
        qty = numero(row.get("qty"), zero=True)
        rate = row.get("valuation_rate")
        rate = current.valuation_rate if rate in (None, "") else numero(rate, zero=True)
        if qty and rate <= 0:
            frappe.throw("Indica una valoración positiva para las existencias contadas.")
        doc.append("items", dict(item_code=item.name, warehouse=warehouse.name, qty=qty, valuation_rate=rate,
            custom_lebaref_observado=current.actual_qty, custom_lebaref_valor_observado=current.valuation_rate))
    return doc


def validar(doc, method=None):
    if not doc.custom_lebaref_tipo_ajuste:
        return
    if doc.custom_lebaref_tipo_ajuste not in ("Conteo", "Merma") or doc.purpose != "Stock Reconciliation":
        frappe.throw("Selecciona el tratamiento correcto del ajuste.")
    reference = " ".join(str(doc.custom_lebaref_referencia_ajuste or "").split()).upper()
    if not reference or not str(doc.custom_lebaref_motivo_ajuste or "").strip():
        frappe.throw("El ajuste requiere acta y motivo.")
    doc.custom_lebaref_referencia_ajuste = reference
    doc.custom_lebaref_clave_ajuste = hashlib.sha256(json.dumps([doc.company, reference]).encode()).hexdigest()
    for row in doc.items:
        numero(row.qty, zero=True)
        if row.custom_lebaref_observado is None or row.custom_lebaref_valor_observado is None:
            frappe.throw("Captura el ajuste por el flujo de conteo para conservar la observación inicial.")
        if doc.custom_lebaref_tipo_ajuste == "Merma" and row.qty >= row.custom_lebaref_observado:
            frappe.throw("La merma debe reducir las existencias observadas.")
        if doc.custom_lebaref_tipo_ajuste == "Merma" and flt(row.valuation_rate-row.custom_lebaref_valor_observado,6):
            frappe.throw("La merma conserva la valoración observada. Registra la revisión de valor mediante Conteo.")


def comprobar_observacion(doc):
    if not doc.custom_lebaref_tipo_ajuste:
        return
    for row in sorted(doc.items, key=lambda r: (r.item_code, r.warehouse)):
        current = bin_actual(row.item_code, row.warehouse)
        if (flt(current.actual_qty-row.custom_lebaref_observado, 6) or
                flt(current.valuation_rate-row.custom_lebaref_valor_observado, 6)):
            frappe.throw("Las existencias o su valoración cambiaron desde el conteo. Actualiza la captura antes de autorizar.")


def confirmar(doc):
    doc.check_permission("submit")
    autorizar()
    if doc.docstatus == 0:
        if not doc.custom_lebaref_tipo_ajuste:
            frappe.throw("Selecciona un ajuste de Lebaref.")
        comprobar_observacion(doc)
        doc.set_posting_time = 1
        doc.posting_date, doc.posting_time = today(), nowtime()
        doc.submit()
    elif doc.docstatus != 1:
        frappe.throw("El ajuste está cancelado.")
    return dict(folio=doc.name, tipo=doc.doctype)


def cancelar(doc, method=None):
    if doc.custom_lebaref_tipo_ajuste:
        autorizar()


def liberar(doc, method=None):
    if doc.custom_lebaref_tipo_ajuste:
        doc.db_set("custom_lebaref_clave_ajuste", None, update_modified=False)
