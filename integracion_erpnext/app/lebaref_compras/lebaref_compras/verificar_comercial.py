"""Casos comerciales de ERPNext sobre datos que revierte la suite integral."""
import uuid

import frappe
from erpnext.buying.doctype.supplier_quotation.supplier_quotation import make_purchase_order
from frappe.utils import add_days, getdate, today

from lebaref_compras import comercial
from lebaref_compras.comparativa import consultar, generar_ordenes
from lebaref_compras.operaciones import ejecutar


def verificar_comercial(seed, supplier):
    frappe.set_user("Administrator")
    company = frappe.get_doc("Company", seed.company)
    suffix = uuid.uuid4().hex[:8]
    parent = frappe.db.get_value("Account", {
        "company": company.name, "root_type": "Liability", "is_group": 1, "parent_account": ["is", "set"],
    }, "name")
    tax_account = frappe.get_doc({
        "doctype": "Account", "account_name": f"IVA prueba {suffix}", "company": company.name,
        "parent_account": parent, "account_type": "Tax",
    }).insert().name
    tax_templates = {}
    for rate in (16, 0):
        tax_templates[rate] = frappe.get_doc({
            "doctype": comercial.IMPUESTOS, "title": f"IVA prueba {rate} {suffix}", "company": company.name,
            "taxes": [{"charge_type": "On Net Total", "account_head": tax_account,
                       "description": f"IVA {rate}%", "rate": rate, "category": "Total", "add_deduct_tax": "Add"}],
        }).insert().name
    shipping = frappe.get_doc({
        "doctype": comercial.ENVIO, "label": f"Flete prueba {suffix}", "company": company.name,
        "account": company.default_expense_account, "shipping_rule_type": "Buying",
        "calculate_based_on": "Fixed", "shipping_amount": 40,
    }).insert()
    term = frappe.get_doc({
        "doctype": "Payment Term", "payment_term_name": f"Crédito prueba {suffix}",
        "invoice_portion": 100, "due_date_based_on": "Day(s) after invoice date", "credit_days": 30,
    }).insert()
    terms = frappe.get_doc({
        "doctype": comercial.PAGO, "template_name": f"30 días prueba {suffix}",
        "terms": [{"payment_term": term.name, "invoice_portion": 100,
                   "due_date_based_on": term.due_date_based_on, "credit_days": 30}],
    }).insert()
    planned = getdate(add_days(today(), 12)).isoformat()
    row = seed.items[0]
    frappe.set_user("compras@lebaref.test")
    for rate, freight, total in ((16, False, 464), (0, True, 440), (16, True, 504)):
        request = ejecutar("solicitud", {
            "company": company.name, "title": "Prueba comercial", "schedule_date": add_days(today(), 7),
            "rows": [{"item_code": row.item_code, "qty": 4, "warehouse": row.warehouse}],
        }, str(uuid.uuid4()))["folio"]
        rfq = ejecutar("peticion", {"suppliers": supplier}, str(uuid.uuid4()), request)["folio"]
        item = frappe.get_doc("Request for Quotation", rfq).items[0]
        offer = {"supplier": supplier, "valid_till": add_days(today(), 10),
                 "rows": [{"id": item.name, "rate": 100}], "taxes_and_charges": tax_templates[rate],
                 "payment_terms_template": terms.name, "fecha_programada_pago": planned}
        if freight:
            offer["shipping_rule"] = shipping.name
        quote = ejecutar("oferta", offer, str(uuid.uuid4()), rfq)["folio"]
        doc = frappe.get_doc("Supplier Quotation", quote)
        assert (doc.net_total, doc.grand_total) == (400, total), (rate, freight, doc.grand_total)
        r = consultar(request)["renglones"][0]
        summary = r["opciones"][0]["comercial"]
        assert (summary["neto"], summary["impuestos_y_cargos"], summary["total"]) == (400, total - 400, total)
        assert getdate(summary["fecha_programada_pago"]).isoformat() == planned
        if freight and rate == 16:
            # El mapper nativo de Desk también debe trasladar las condiciones al guardar.
            order = make_purchase_order(quote).insert()
        else:
            folio = generar_ordenes(request, {r["id"]: r["opciones"][0]["id"]})[0]["folio"]
            order = frappe.get_doc("Purchase Order", folio)
        assert order.docstatus == 0 and (order.net_total, order.grand_total) == (400, total), (
            rate, freight, order.net_total, order.grand_total,
        )
        assert order.payment_terms_template == terms.name and getdate(order.get(comercial.FECHA)).isoformat() == planned
        payment, = order.payment_schedule
        assert payment.payment_term == term.name and payment.payment_amount == total
        assert getdate(payment.due_date) == getdate(add_days(order.transaction_date, 30))
