"""Condiciones comerciales de compra, usando los maestros nativos de ERPNext."""
from datetime import date

import frappe
from erpnext.controllers.accounts_controller import get_taxes_and_charges


IMPUESTOS = "Purchase Taxes and Charges Template"
ENVIO = "Shipping Rule"
PAGO = "Payment Terms Template"
CONDICION = "custom_lebaref_condicion_pago"
FECHA = "custom_lebaref_fecha_programada_pago"


def opciones(doctype, filters=None):
    if not (frappe.has_permission(doctype, "read") or frappe.has_permission(doctype, "select")):
        return []
    return frappe.get_list(doctype, filters=filters, pluck="name", order_by="name", limit_page_length=0)


def _enlace(doctype, name):
    if not isinstance(name, str) or not name.strip():
        frappe.throw("Selecciona un valor válido para la condición comercial.")
    doc = frappe.get_doc(doctype, name)
    if not (doc.has_permission("read") or doc.has_permission("select")):
        doc.check_permission("select")
    return doc


def configurar_oferta(doc, data):
    if name := data.get("taxes_and_charges"):
        template = _enlace(IMPUESTOS, name)
        if template.disabled or template.company != doc.company:
            frappe.throw("Selecciona una plantilla fiscal activa de la empresa.")
        doc.taxes_and_charges = template.name
        doc.set("taxes", get_taxes_and_charges(IMPUESTOS, template.name))
    if name := data.get("shipping_rule"):
        shipping = _enlace(ENVIO, name)
        if shipping.disabled or shipping.shipping_rule_type != "Buying" or shipping.company != doc.company:
            frappe.throw("Selecciona una regla de envío de compra activa de la empresa.")
        doc.shipping_rule = shipping.name
    if name := data.get("payment_terms_template"):
        doc.set(CONDICION, _enlace(PAGO, name).name)
    if planned := data.get("fecha_programada_pago"):
        try:
            parsed = date.fromisoformat(planned)
            if parsed.isoformat() != planned:
                raise ValueError()
        except (TypeError, ValueError):
            frappe.throw("Captura la fecha programada de pago en formato AAAA-MM-DD.")
        doc.set(FECHA, parsed)


def condiciones_en_orden(order, quote, desde_comparativa=False):
    terms = quote.get(CONDICION)
    if terms and (desde_comparativa or not order.payment_terms_template):
        order.payment_terms_template = terms
        order.set("payment_schedule", [])
    if not order.get(FECHA):
        order.set(FECHA, quote.get(FECHA))


def desde_cotizacion(order, method=None):
    # Also supports orders created with the native Desk mapper.
    if not order.is_new():
        return
    names = {item.supplier_quotation for item in order.items}
    if len(names) != 1 or not next(iter(names)):
        return
    quote = frappe.get_doc("Supplier Quotation", names.pop())
    if quote.docstatus == 1 and quote.company == order.company and quote.supplier == order.supplier:
        quote.check_permission("read")
        condiciones_en_orden(order, quote)


def resumen(quote):
    return {
        "neto": quote.net_total, "impuestos_y_cargos": quote.total_taxes_and_charges,
        "total": quote.grand_total, "taxes_and_charges": quote.taxes_and_charges or "",
        "shipping_rule": quote.shipping_rule or "", "payment_terms_template": quote.get(CONDICION) or "",
        "fecha_programada_pago": quote.get(FECHA),
        "cargos": [
            {"descripcion": row.description, "tipo_calculo": row.charge_type, "importe": row.tax_amount}
            for row in quote.taxes if row.category in ("Total", "Valuation and Total")
        ],
    }
