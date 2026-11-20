"""Conciliación estructural de CFDI con la recepción, sin movimientos de inventario."""
import hashlib
import json
import uuid
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
from xml.etree import ElementTree as ET
import frappe

DT = "Lebaref Conciliacion CFDI"
NS = {"c": "http://www.sat.gob.mx/cfd/4", "t": "http://www.sat.gob.mx/TimbreFiscalDigital"}


def decimal(value):
    try:
        result = Decimal(str(value))
        if not result.is_finite() or result < 0:
            raise ValueError()
        return result
    except (InvalidOperation, ValueError, TypeError):
        frappe.throw("El comprobante contiene cantidades o importes inválidos.")


def configurar():
    if frappe.db.exists("DocType", DT):
        return
    fields = [dict(fieldname=k, label=label, fieldtype=kind, options=options, read_only=1,
        unique=k in ("cfdi", "custom_lebaref_operacion")) for k,label,kind,options in (
        ("cfdi","CFDI","Link","Lebaref CFDI"), ("recepcion","Recepción","Link","Purchase Receipt"),
        ("seleccion","Relación de conceptos","Code","JSON"), ("detalle","Resultado de conciliación","Code","JSON"),
        ("huella","Huella de comprobante","Data",None), ("custom_lebaref_operacion","Operación","Data",None),
        ("custom_lebaref_huella","Huella de operación","Data",None))]
    frappe.get_doc(dict(doctype="DocType", name=DT, module="Lebaref Compras", custom=1, autoname="hash",
        fields=fields, permissions=[dict(role=role, read=1, create=1) for role in
            ("Purchase User", "Purchase Manager", "Accounts User", "Accounts Manager", "System Manager")])).insert(ignore_permissions=True)
    # Accounting users need to read the immutable archived document to bill it.
    from frappe.permissions import add_permission
    for role in ("Accounts User", "Accounts Manager"):
        if not frappe.db.exists("DocPerm", {"parent":"Lebaref CFDI", "role":role}):
            add_permission("Lebaref CFDI", role, 0, "read")


def validar_registro(doc, method=None):
    if not doc.flags.get("lebaref_conciliacion_validada"):
        frappe.throw("Concilia el CFDI mediante su operación guiada.")


def leer(archive):
    archive.check_permission("read")
    receipt = frappe.get_doc("Purchase Receipt", archive.recepcion, for_update=True)
    receipt.check_permission("read")
    if receipt.docstatus != 1 or receipt.is_return:
        frappe.throw("Selecciona una recepción confirmada de compra.")
    file = frappe.get_doc("File", {"file_url": archive.xml})
    file.check_permission("read")
    if not file.is_private or file.attached_to_doctype != "Lebaref CFDI" or file.attached_to_name != archive.name:
        frappe.throw("El XML debe estar archivado de forma privada en su CFDI.")
    raw = file.get_content()
    if isinstance(raw, str):
        raw = raw.encode()
    if len(raw)>2_000_000 or b"<!DOCTYPE" in raw.upper() or b"<!ENTITY" in raw.upper():
        frappe.throw("XML no admitido.")
    if hashlib.sha256(raw).hexdigest() != archive.huella:
        frappe.throw("El archivo cambió respecto de su huella de archivo.")
    try:
        root = ET.fromstring(raw)
        identifier = str(uuid.UUID(root.find("c:Complemento/t:TimbreFiscalDigital", NS).get("UUID"))).upper()
        if root.tag != "{http://www.sat.gob.mx/cfd/4}Comprobante" or root.get("Version") != "4.0" or root.get("TipoDeComprobante") != "I" or identifier != archive.name:
            raise ValueError()
        issuer = root.find("c:Emisor", NS).get("Rfc").upper()
        receiver = root.find("c:Receptor", NS).get("Rfc").upper()
    except (ET.ParseError, AttributeError, ValueError, TypeError):
        frappe.throw("Se requiere un CFDI 4.0 de ingreso con UUID válido.")
    if (issuer != (frappe.db.get_value("Supplier", receipt.supplier,"tax_id") or "").upper() or
        receiver != (frappe.db.get_value("Company", receipt.company,"tax_id") or "").upper()):
        frappe.throw("Los RFC del XML deben coincidir con empresa y proveedor.")
    quantum = Decimal(1).scaleb(-receipt.precision("grand_total"))
    concepts = []
    for i,node in enumerate(root.findall("c:Conceptos/c:Concepto",NS),1):
        qty, rate, amount, discount = [decimal(node.get(k, "0")) for k in ("Cantidad","ValorUnitario","Importe","Descuento")]
        if qty <= 0 or discount>amount or (qty*rate).quantize(quantum,rounding=ROUND_HALF_UP)!=amount.quantize(quantum,rounding=ROUND_HALF_UP):
            frappe.throw("Revisa cantidades, precio, importe y descuento del concepto XML.")
        concepts.append(dict(id=str(i), descripcion=node.get("Descripcion",""), codigo=node.get("NoIdentificacion",""),
            unidad=node.get("Unidad") or node.get("ClaveUnidad"), cantidad=str(qty), importe_neto=str(amount-discount)))
    if not concepts or sum(decimal(c["importe_neto"]) for c in concepts).quantize(quantum,rounding=ROUND_HALF_UP) != (decimal(root.get("SubTotal"))-decimal(root.get("Descuento","0"))).quantize(quantum,rounding=ROUND_HALF_UP):
        frappe.throw("Los conceptos y el subtotal neto del XML deben cuadrar.")
    return receipt, root, concepts


def evaluar(archive, data=None):
    receipt, root, concepts = leer(archive)
    result = dict(cfdi=archive.name, recepcion=receipt.name, moneda=root.get("Moneda"), fecha_xml=root.get("Fecha", ""),
        total_xml=str(decimal(root.get("Total"))), total_recepcion=str(decimal(receipt.grand_total)), conceptos=concepts,
        renglones=[dict(id=r.name,item_code=r.item_code,qty=r.qty,conversion_factor=r.conversion_factor,net_amount=r.net_amount,uom=r.uom) for r in receipt.items])
    quantum = Decimal(1).scaleb(-receipt.precision("grand_total"))
    charges = {r.name:r for r in receipt.taxes if r.charge_type=="Actual"}
    result["cargos"] = [dict(id=r.name,descripcion=r.description,importe=r.tax_amount) for r in charges.values()]
    delta = decimal(root.get("Total"))-decimal(receipt.grand_total)
    result["diferencia_total"] = str(delta)
    result["coincide"] = False
    if data is None:
        return result
    rows = data.get("rows")
    if not isinstance(rows,list):
        frappe.throw("Relaciona los conceptos XML con renglones de recepción.")
    by_concept = {c["id"]:c for c in concepts}
    by_row = {r.name:r for r in receipt.items}
    totals = {r.name:[Decimal(0),Decimal(0)] for r in receipt.items}
    charge_totals = {}
    seen = set()
    selection = []
    for row in rows:
        if (not isinstance(row,dict) or not isinstance(row.get("concepto"),str)
            or row["concepto"] not in by_concept or row["concepto"] in seen):
            frappe.throw("Referencia de concepto inválida o repetida.")
        concept = by_concept[row["concepto"]]
        if row.get("cargo"):
            key = row["cargo"]
            if not isinstance(key,str) or key not in charges or row.get("renglon"):
                frappe.throw("Selecciona un cargo fijo de la recepción para el concepto de flete.")
            charge_totals[key] = charge_totals.get(key,Decimal(0))+decimal(concept["importe_neto"])
            selection.append(dict(concepto=row["concepto"],cargo=key))
        else:
            key = row.get("renglon")
            if not isinstance(key,str) or key not in by_row:
                frappe.throw("El renglón no pertenece a la recepción.")
            factor = decimal(row.get("factor_xml",1))
            if factor<=0:
                frappe.throw("Indica el factor XML a unidad de inventario.")
            values = totals[key]
            values[0] += decimal(concept["cantidad"])*factor
            values[1] += decimal(concept["importe_neto"])
            selection.append(dict(concepto=row["concepto"],renglon=key,factor_xml=format(factor.normalize(),"f")))
        seen.add(row["concepto"])
    differences = []
    for key,(qty,amount) in totals.items():
        origin = by_row[key]
        differences.append(dict(renglon=key, diferencia_cantidad=str(qty-decimal(origin.qty)*decimal(origin.conversion_factor)),
            diferencia_importe=str(amount-decimal(origin.net_amount))))
    for key,amount in charge_totals.items():
        differences.append(dict(cargo=key,diferencia_cantidad="0",diferencia_importe=str(amount-decimal(charges[key].tax_amount))))
    result["diferencias"] = differences
    result["seleccion"] = sorted(selection,key=lambda r:int(r["concepto"]))
    result["coincide"] = (len(seen)==len(concepts) and root.get("Moneda")==receipt.currency and delta.quantize(quantum,rounding=ROUND_HALF_UP)==0
        and all(Decimal(r["diferencia_cantidad"]).quantize(Decimal("0.000001"))==0 and Decimal(r["diferencia_importe"]).quantize(quantum,rounding=ROUND_HALF_UP)==0 for r in differences))
    return result


@frappe.whitelist(methods=["GET"])
def consultar(cfdi, seleccion=None):
    if not frappe.has_permission(DT,"read"):
        frappe.throw("Tu cuenta no tiene permiso para conciliar comprobantes.",frappe.PermissionError)
    data = frappe.parse_json(seleccion) if seleccion else None
    if data is not None and not isinstance(data,dict):
        frappe.throw("Selección inválida.")
    return evaluar(frappe.get_doc("Lebaref CFDI",cfdi),data)


def conciliar(archive,data,token,signature):
    result = evaluar(archive,data)
    if not result["coincide"]:
        frappe.throw("Hay diferencias de moneda, cantidad o importe. Revisa la consulta de conciliación.")
    prior = frappe.db.get_value(DT,{"cfdi":archive.name},"name",for_update=True)
    selected = json.dumps(result["seleccion"],sort_keys=True)
    if prior:
        doc = frappe.get_doc(DT,prior)
        doc.check_permission("read")
        if doc.seleccion != selected or doc.huella != archive.huella:
            frappe.throw("El CFDI ya tiene otra conciliación. Conserva su historial y revisa el expediente.")
        return dict(folio=doc.name,tipo=DT,repetida=True)
    doc = frappe.get_doc(dict(doctype=DT,cfdi=archive.name,recepcion=archive.recepcion,seleccion=selected,
        detalle=json.dumps(result),huella=archive.huella,custom_lebaref_operacion=token,custom_lebaref_huella=signature))
    doc.flags.lebaref_conciliacion_validada=True
    doc.insert()
    return dict(folio=doc.name,tipo=DT)


def exigir(archive):
    name = frappe.db.get_value(DT,{"cfdi":archive.name},"name")
    if not name:
        frappe.throw("Concilia los conceptos XML antes de preparar su factura.")
    doc = frappe.get_doc(DT,name)
    doc.check_permission("read")
    result = evaluar(archive,dict(rows=frappe.parse_json(doc.seleccion)))
    if not result["coincide"] or doc.huella!=archive.huella:
        frappe.throw("La recepción o comprobante difiere de la conciliación registrada.")
    return result
