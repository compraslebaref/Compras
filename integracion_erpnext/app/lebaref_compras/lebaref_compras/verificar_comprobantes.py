import uuid
import frappe
from frappe.utils import today
from lebaref_compras.operaciones import ejecutar
from lebaref_compras.comprobantes import consultar


def verificar_comprobantes(seed,supplier):
    frappe.set_user("Administrator")
    base=seed.items[0]
    def nueva_recepcion():
        doc=frappe.get_doc(dict(doctype="Purchase Receipt",company=seed.company,supplier=supplier,
            items=[dict(item_code=base.item_code,qty=2,rate=100,warehouse=base.warehouse,project=base.project)],
            taxes=[dict(charge_type="Actual",account_head=frappe.get_cached_value("Company",seed.company,"stock_adjustment_account"),
                description="Flete prueba",tax_amount=40,category="Total",add_deduct_tax="Add")])).insert()
        doc.submit()
        return doc
    receipt=nueva_recepcion()
    row=receipt.items[0]
    identifier=str(uuid.uuid4()).upper()
    xml=f'<c:Comprobante xmlns:c="http://www.sat.gob.mx/cfd/4" xmlns:t="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="I" SubTotal="{receipt.net_total+40}" Total="{receipt.grand_total}" Moneda="{receipt.currency}"><c:Emisor Rfc="AAA010101AAA"/><c:Receptor Rfc="BBB010101BBB"/><c:Conceptos><c:Concepto Cantidad="2" ValorUnitario="{row.net_rate}" Importe="{row.net_amount}" Descripcion="Material prueba" ClaveUnidad="H87"/><c:Concepto Cantidad="1" ValorUnitario="40" Importe="40" Descripcion="Flete" ClaveUnidad="E48"/></c:Conceptos><c:Complemento><t:TimbreFiscalDigital UUID="{identifier}"/></c:Complemento></c:Comprobante>'
    archive=ejecutar("cfdi",{"xml":xml},str(uuid.uuid4()),receipt.name)["folio"]
    selection=dict(rows=[dict(concepto="1",renglon=row.name,factor_xml=1),dict(concepto="2",cargo=receipt.taxes[0].name)])
    assert consultar(archive,frappe.as_json(selection))["coincide"]
    wrong=dict(rows=[dict(concepto="1",renglon=row.name,factor_xml=2)])
    assert not consultar(archive,frappe.as_json(wrong))["coincide"]
    try:
        ejecutar("conciliar_cfdi",wrong,str(uuid.uuid4()),archive)
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se concilió un XML con cantidades diferentes")
    operation=str(uuid.uuid4())
    reconciliation=ejecutar("conciliar_cfdi",selection,operation,archive)["folio"]
    assert ejecutar("conciliar_cfdi",selection,operation,archive)["repetida"]
    equivalent=dict(rows=[dict(concepto="1",renglon=row.name,factor_xml="1.00"),dict(concepto="2",cargo=receipt.taxes[0].name)])
    assert ejecutar("conciliar_cfdi",equivalent,str(uuid.uuid4()),archive)["repetida"]
    before=frappe.db.get_value("Bin",{"item_code":base.item_code,"warehouse":base.warehouse},"actual_qty")
    invoice_token=str(uuid.uuid4())
    invoice=ejecutar("facturar_cfdi",{},invoice_token,archive)["folio"]
    assert ejecutar("facturar_cfdi",{},invoice_token,archive)["repetida"]
    assert frappe.db.get_value("Purchase Invoice",invoice,"docstatus")==0
    frappe.set_user("almacen@lebaref.test")
    try:
        ejecutar("autorizar_factura",{"confirmar":"AUTORIZAR"},str(uuid.uuid4()),invoice)
    except frappe.PermissionError:
        pass
    else:
        raise AssertionError("Almacén confirmó una factura contable")
    frappe.set_user("Administrator")
    ejecutar("autorizar_factura",{"confirmar":"AUTORIZAR"},str(uuid.uuid4()),invoice)
    invoice_doc=frappe.get_doc("Purchase Invoice",invoice)
    assert invoice_doc.outstanding_amount==invoice_doc.grand_total and invoice_doc.custom_lebaref_cfdi==archive
    assert frappe.db.get_value("Bin",{"item_code":base.item_code,"warehouse":base.warehouse},"actual_qty")==before
    parent=frappe.db.get_value("Account",{"company":seed.company,"is_group":1,"root_type":"Asset"},"name")
    bank=frappe.get_doc(dict(doctype="Account",account_name="Banco prueba "+uuid.uuid4().hex[:8],company=seed.company,
        parent_account=parent,account_type="Bank",account_currency=invoice_doc.currency,is_group=0)).insert()
    amount=invoice_doc.outstanding_amount/2
    payment_data=dict(importe=amount,cuenta=bank.name,referencia="PRUEBA-"+uuid.uuid4().hex[:8],fecha=today())
    stale_payment=ejecutar("pagar_factura",dict(payment_data,importe=invoice_doc.grand_total),str(uuid.uuid4()),invoice)["folio"]
    payment_token=str(uuid.uuid4())
    payment=ejecutar("pagar_factura",payment_data,payment_token,invoice)["folio"]
    assert ejecutar("pagar_factura",payment_data,payment_token,invoice)["repetida"]
    ejecutar("autorizar_pago",{"confirmar":"PAGAR"},str(uuid.uuid4()),payment)
    assert frappe.db.get_value("Purchase Invoice",invoice,"outstanding_amount")==amount
    try:
        ejecutar("autorizar_pago",{"confirmar":"PAGAR"},str(uuid.uuid4()),stale_payment)
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se confirmó un pago pendiente mayor al saldo actualizado")
    frappe.get_doc("Payment Entry",payment).cancel()
    assert frappe.db.get_value("Purchase Invoice",invoice,"outstanding_amount")==invoice_doc.grand_total
    remaining=ejecutar("pagar_factura",dict(payment_data,importe=invoice_doc.grand_total),str(uuid.uuid4()),invoice)["folio"]
    ejecutar("autorizar_pago",{"confirmar":"PAGAR"},str(uuid.uuid4()),remaining)
    assert frappe.db.get_value("Purchase Invoice",invoice,"outstanding_amount")==0
    try:
        ejecutar("pagar_factura",payment_data,str(uuid.uuid4()),invoice)
    except frappe.ValidationError:
        pass
    else:
        raise AssertionError("Se pagó más del saldo de la factura")
    note_receipt=nueva_recepcion()
    note_token=str(uuid.uuid4())
    note_data=dict(referencia="TICKET-"+uuid.uuid4().hex[:8],motivo="Compra con nota interna",fecha=today())
    note=ejecutar("nota_compra",note_data,note_token,note_receipt.name)["folio"]
    assert ejecutar("nota_compra",note_data,note_token,note_receipt.name)["repetida"]
    ejecutar("autorizar_factura",{"confirmar":"AUTORIZAR"},str(uuid.uuid4()),note)
    assert frappe.db.get_value("Purchase Invoice",note,"custom_lebaref_tipo_comprobante")=="Nota/ticket"
    assert not frappe.db.get_value("Purchase Invoice",note,"custom_lebaref_cfdi")

    from erpnext.accounts.doctype.purchase_invoice.purchase_invoice import make_debit_note
    credit=make_debit_note(note)
    credit.insert()
    credit.submit()
    assert credit.return_against==note and not credit.custom_lebaref_tipo_comprobante
