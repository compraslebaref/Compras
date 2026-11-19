"""Unidades de compra y validación de la conversión a inventario."""
import math

import frappe
from erpnext.setup.doctype.uom.uom import validate_uoms_are_enabled
from erpnext.stock.get_item_details import get_conversion_factor


def numero(value, zero=False):
    try:
        result = float(value)
    except (ValueError, TypeError, OverflowError):
        frappe.throw("Escribe una cantidad o precio numérico válido.")
    if not math.isfinite(result) or result < 0 or (not zero and result == 0):
        frappe.throw("La cantidad o precio debe ser positivo.")
    return result


CAMPOS = (("uom", "Unidad de compra"), ("stock_uom", "Unidad de inventario"),
          ("conversion_factor", "Factor a inventario"))


def valores(row):
    return {key: row.get(key) for key, _ in CAMPOS}


def _factor(value):
    if isinstance(value, bool):
        frappe.throw("Captura un factor de conversión numérico mayor que cero.")
    return numero(value)


def configurar_articulo(item, data):
    uom = data.get("uom") or item.stock_uom
    factor = data.get("conversion_factor")
    if uom != item.stock_uom:
        item.purchase_uom = uom
        item.append("uoms", {"uom": uom, "conversion_factor": _factor(factor)})
    elif factor not in (None, "") and _factor(factor) != 1:
        frappe.throw("El factor debe ser 1 cuando las dos unidades son iguales.")


def datos_articulo(item, uom=None):
    uom = uom or item.purchase_uom or item.stock_uom
    validate_uoms_are_enabled([uom])
    factor = get_conversion_factor(item.name, uom, throw_if_missing=True)["conversion_factor"]
    return {"uom": uom, "stock_uom": item.stock_uom, "conversion_factor": factor}


def verificar_referencia(data, expected):
    for key, _ in CAMPOS:
        value = data.get(key)
        if value in (None, ""):
            continue
        if key == "conversion_factor":
            value = _factor(value)
        if value != expected[key]:
            frappe.throw("La unidad o el factor no coincide con el documento de origen. Actualiza la captura.")


def datos_solicitud(row, item_code):
    if all(row.get(key) in (None, "") for key, _ in CAMPOS):
        return {}
    item = frappe.get_doc("Item", item_code)
    item.check_permission("read")
    data = datos_articulo(item, row.get("uom"))
    verificar_referencia(row, data)
    return data
