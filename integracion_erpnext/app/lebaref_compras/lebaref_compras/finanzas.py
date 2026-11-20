"""Factura y pago de compras mediante documentos contables nativos de ERPNext."""
import hashlib
import json
import frappe
from frappe.utils import getdate, today, flt
from lebaref_compras import comprobantes
from lebaref_compras.unidades import numero


def factura(receipt, data, archive=None):
    receipt.check_permission("read")
    if receipt.docstatus!=1 or receipt.is_return:
        frappe.throw("Selecciona una recepción confirmada de compra.")
    if frappe.db.exists("Purchase Invoice Item",{"purchase_receipt":receipt.name,"docstatus":["!=",2]}):
        frappe.throw("La recepción ya tiene una factura activa. Revisa su expediente.")
    from erpnext.stock.doctype.purchase_receipt.purchase_receipt import make_purchase_invoice
    doc=make_purchase_invoice(receipt.name)
    doc.custom_lebaref_recepcion_comprobante=receipt.name
    if archive:
        reconciled=comprobantes.exigir(archive)
        doc.custom_lebaref_tipo_comprobante="CFDI"
        doc.custom_lebaref_cfdi=archive.name
        doc.bill_no=archive.name
    else:
        reference=" ".join(str(data.get("referencia") or "").split()).upper()
        if not reference or not str(data.get("motivo") or "").strip():
            frappe.throw("La nota requiere referencia y motivo.")
        doc.custom_lebaref_tipo_comprobante="Nota/ticket"
        doc.custom_lebaref_motivo_nota=data["motivo"]
        doc.bill_no=reference
    try:
        doc.bill_date=getdate(data.get("fecha") or (reconciled["fecha_xml"][:10] if archive else "") or today())
    except (ValueError,TypeError):
        frappe.throw("Indica una fecha válida para el comprobante.")
    return doc


def preparar_factura(doc,method=None):
    # Native credit notes retain return_against as provenance and use their own fiscal document.
    if doc.is_return and doc.return_against:
        doc.custom_lebaref_tipo_comprobante = ""
        doc.custom_lebaref_cfdi = None
        doc.custom_lebaref_recepcion_comprobante = None
        doc.custom_lebaref_motivo_nota = None
        doc.custom_lebaref_clave_comprobante = None


def validar_factura(doc,method=None):
    kind=doc.custom_lebaref_tipo_comprobante
    if not kind:
        if doc.custom_lebaref_cfdi or doc.custom_lebaref_clave_comprobante:
            frappe.throw("Identifica el tipo de comprobante de compra.")
        return
    if kind not in ("CFDI","Nota/ticket") or doc.update_stock or not doc.custom_lebaref_recepcion_comprobante:
        frappe.throw("El comprobante debe facturarse desde su recepción confirmada.")
    receipt=frappe.get_doc("Purchase Receipt",doc.custom_lebaref_recepcion_comprobante,for_update=True)
    receipt.check_permission("read")
    if receipt.docstatus!=1 or receipt.is_return or any(doc.get(k)!=receipt.get(k) for k in ("company","supplier","currency")):
        frappe.throw("La factura debe corresponder a empresa, proveedor y moneda de su recepción.")
    if frappe.db.get_value("Purchase Invoice Item", {"purchase_receipt":receipt.name,"docstatus":["!=",2],"parent":["!=",doc.name or ""]},"name",for_update=True):
        frappe.throw("La recepción ya está vinculada a otra factura activa.")
    expected={r.name:r for r in receipt.items}
    amounts={key:[0.0,0.0] for key in expected}
    for row in doc.items:
        if row.purchase_receipt!=receipt.name or row.pr_detail not in expected:
            frappe.throw("El renglón de factura no pertenece a la recepción del comprobante.")
        values=amounts[row.pr_detail]
        values[0]+=row.qty*row.conversion_factor
        values[1]+=row.net_amount
    if (any(flt(amounts[k][0]-r.qty*r.conversion_factor,6) or flt(amounts[k][1]-r.net_amount,2) for k,r in expected.items())
            or flt(doc.grand_total-receipt.grand_total,2)):
        frappe.throw("Las cantidades e importes de la factura deben coincidir con la recepción conciliada.")
    if kind=="CFDI":
        archive=frappe.get_doc("Lebaref CFDI",doc.custom_lebaref_cfdi)
        if archive.recepcion!=receipt.name:
            frappe.throw("El CFDI pertenece a otra recepción.")
        comprobantes.exigir(archive)
        reference=archive.name
        doc.bill_no=reference
    else:
        if doc.custom_lebaref_cfdi or not doc.bill_no or not str(doc.custom_lebaref_motivo_nota or "").strip():
            frappe.throw("La nota requiere su referencia y motivo.")
        reference=" ".join(doc.bill_no.split()).upper()
        doc.bill_no=reference
    doc.custom_lebaref_clave_comprobante=hashlib.sha256(json.dumps([kind,doc.company,doc.supplier,reference]).encode()).hexdigest()


def liberar_factura(doc,method=None):
    if doc.custom_lebaref_tipo_comprobante:
        doc.db_set("custom_lebaref_clave_comprobante",None,update_modified=False)


def pago(invoice,data):
    if invoice.docstatus!=1 or invoice.is_return:
        frappe.throw("Selecciona una factura confirmada con saldo pendiente.")
    amount=numero(data.get("importe"))
    if flt(amount-invoice.outstanding_amount,2)>0 or not str(data.get("referencia") or "").strip():
        frappe.throw("Indica referencia de pago e importe dentro del saldo pendiente.")
    account=frappe.get_doc("Account",data.get("cuenta"))
    account.check_permission("read")
    if account.company!=invoice.company or account.is_group or account.disabled or account.account_type not in ("Bank","Cash"):
        frappe.throw("Selecciona una cuenta bancaria o de caja activa de la empresa.")
    from erpnext.accounts.doctype.payment_entry.payment_entry import get_payment_entry
    doc=get_payment_entry("Purchase Invoice",invoice.name,party_amount=amount,bank_account=account.name)
    doc.reference_no=str(data["referencia"]).strip()
    doc.reference_date=getdate(data.get("fecha") or today())
    doc.custom_lebaref_factura=invoice.name
    return doc


def validar_pago(doc,method=None):
    if not doc.custom_lebaref_factura:
        return
    if not str(doc.reference_no or "").strip():
        frappe.throw("Indica la referencia del pago.")
    invoice=frappe.get_doc("Purchase Invoice",doc.custom_lebaref_factura,for_update=True)
    invoice.check_permission("read")
    if (invoice.docstatus!=1 or doc.payment_type!="Pay" or doc.party_type!="Supplier" or doc.party!=invoice.supplier
            or doc.company!=invoice.company or len(doc.references)!=1):
        frappe.throw("El pago debe corresponder a la factura y proveedor indicados.")
    row=doc.references[0]
    if row.reference_doctype!="Purchase Invoice" or row.reference_name!=invoice.name or row.allocated_amount<=0:
        frappe.throw("Relaciona el pago con la factura de origen.")
    if flt(row.allocated_amount-invoice.outstanding_amount,2)>0:
        frappe.throw("El importe supera el saldo actual de la factura.")


def confirmar(doc,data,word):
    doc.check_permission("submit")
    if data.get("confirmar")!=word:
        frappe.throw("Revisa el documento y confirma la autorización.")
    if doc.docstatus==0:
        doc.submit()
    elif doc.docstatus!=1:
        frappe.throw("El documento está cancelado.")
    return dict(folio=doc.name,tipo=doc.doctype)
