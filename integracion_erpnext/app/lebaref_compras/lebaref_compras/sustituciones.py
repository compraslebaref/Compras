"""Autorizaciones de sustitución acotadas a una partida de compra."""
import frappe
from frappe.utils import flt
from lebaref_compras import unidades

DOCTYPE = "Lebaref Sustitucion"


def configurar():
    if frappe.db.exists("DocType", DOCTYPE):
        return
    fields = [
        ("orden", "Orden", "Link", "Purchase Order"),
        ("renglon", "Renglón de orden", "Data", None),
        ("original", "Artículo solicitado", "Link", "Item"),
        ("sustituto", "Artículo autorizado", "Link", "Item"),
        ("cantidad", "Cantidad autorizada", "Float", None),
        ("unidad", "Unidad de compra", "Link", "UOM"),
        ("factor", "Factor a inventario", "Float", None),
        ("precio", "Precio unitario autorizado", "Currency", None),
        ("motivo", "Motivo", "Small Text", None),
        ("custom_lebaref_operacion", "Operación", "Data", None),
        ("custom_lebaref_huella", "Huella", "Data", None),
    ]
    frappe.get_doc(dict(doctype="DocType", name=DOCTYPE, module="Lebaref Compras", custom=1,
        autoname="hash", is_submittable=1, track_changes=1,
        fields=[dict(fieldname=key, label=label, fieldtype=kind, options=options,
            reqd=key not in ("custom_lebaref_operacion", "custom_lebaref_huella"),
            unique=key == "custom_lebaref_operacion") for key, label, kind, options in fields],
        permissions=[dict(role="Purchase Manager", read=1, write=1, create=1, submit=1, cancel=1, select=1),
                     dict(role="Purchase User", read=1, select=1), dict(role="Stock User", read=1, select=1)])).insert(ignore_permissions=True)


def validar_autorizacion(doc, method=None):
    order = frappe.get_doc("Purchase Order", doc.orden, for_update=True)
    order.check_permission("write")
    order.check_permission("submit")
    if order.docstatus != 1 or order.status in ("Closed", "On Hold", "Cancelled"):
        frappe.throw("Selecciona una orden confirmada y abierta.")
    source = next((r for r in order.items if r.name == doc.renglon), None)
    if not source:
        frappe.throw("El renglón no pertenece a la orden.")
    item = frappe.get_doc("Item", doc.sustituto)
    item.check_permission("read")
    if item.name == source.item_code or item.disabled or not item.is_stock_item or not item.is_purchase_item:
        frappe.throw("Selecciona otro artículo activo, inventariable y de compra.")
    if not frappe.get_cached_value("Item", source.item_code, "is_stock_item"):
        frappe.throw("La sustitución aplica a material inventariable.")
    units = unidades.datos_articulo(item, source.uom)
    unidades.verificar_referencia(units, unidades.valores(source))
    doc.cantidad = unidades.numero(doc.cantidad)
    if not str(doc.motivo or "").strip():
        frappe.throw("Indica el motivo de la sustitución.")
    active = frappe.db.sql("""select name, cantidad from `tabLebaref Sustitucion`
        where renglon=%s and docstatus=1 and name!=%s for update""",
        (source.name, doc.name or ""), as_dict=True)
    allocated = sum(a.cantidad - recibido(a.name) for a in active)
    if flt(doc.cantidad + allocated + source.received_qty - source.qty, source.precision("qty")) > 0:
        frappe.throw("La autorización excede el saldo disponible del renglón.")
    doc.original, doc.unidad = source.item_code, source.uom
    doc.factor, doc.precio = source.conversion_factor, source.rate


def recibido(authorization, exclude=""):
    rows = frappe.db.sql("""select received_qty from `tabPurchase Receipt Item`
        where custom_lebaref_sustitucion=%s and docstatus=1 and parent!=%s for update""",
        (authorization, exclude))
    return sum(row[0] for row in rows)


def anular_autorizacion(doc, method=None):
    order = frappe.get_doc("Purchase Order", doc.orden, for_update=True)
    order.check_permission("write")
    order.check_permission("submit")
    if frappe.db.exists("Purchase Receipt Item", {"custom_lebaref_sustitucion": doc.name, "docstatus": 1}):
        frappe.throw("La autorización tiene recepciones vigentes y debe conservarse.")


def aplicar(doc, data):
    rows = {r["id"]: r for r in data["rows"]}
    for row in doc.items:
        authorization = rows[row.purchase_order_item].get("sustitucion")
        if not authorization:
            continue
        approval = frappe.get_doc(DOCTYPE, authorization, for_update=True)
        approval.check_permission("read")
        if approval.docstatus != 1 or approval.renglon != row.purchase_order_item:
            frappe.throw("Selecciona una autorización confirmada para este renglón.")
        item = frappe.get_doc("Item", approval.sustituto)
        row.item_code, row.item_name = item.name, item.item_name
        row.description = item.description or item.item_name
        row.brand = item.brand
        row.custom_lebaref_sustitucion = approval.name
        row.custom_lebaref_articulo_original = approval.original


def verificar_renglon(doc, row):
    order = frappe.get_doc("Purchase Order", row.purchase_order, for_update=True)
    approval = frappe.get_doc(DOCTYPE, row.custom_lebaref_sustitucion, for_update=True)
    reference = row.purchase_order_item if doc.doctype == "Purchase Receipt" else row.po_detail
    if (approval.docstatus != 1 or approval.orden != row.purchase_order or approval.renglon != reference
            or approval.sustituto != row.item_code or approval.original != row.custom_lebaref_articulo_original):
        frappe.throw("La sustitución no coincide con la autorización.")
    source = next((r for r in order.items if r.name == reference), None)
    if (not source or source.item_code != approval.original or source.uom != approval.unidad
            or flt(source.conversion_factor, 6) != flt(approval.factor, 6)
            or flt(source.rate, source.precision("rate")) != flt(approval.precio, source.precision("rate"))):
        frappe.throw("La orden cambió respecto de la autorización. Revisa la documentación de compra.")
    if row.uom != approval.unidad or row.stock_uom != frappe.get_cached_value("Item", approval.original, "stock_uom"):
        frappe.throw("La unidad de la sustitución no coincide con la autorizada.")
    if flt(row.conversion_factor, 6) != flt(approval.factor, 6) or flt(row.rate, row.precision("rate")) != flt(approval.precio, row.precision("rate")):
        frappe.throw("El factor y precio deben coincidir con la autorización.")
    if doc.doctype == "Purchase Invoice":
        receipt = frappe.db.get_value("Purchase Receipt Item", row.pr_detail,
            ["parent", "docstatus", "item_code", "custom_lebaref_sustitucion"], as_dict=True)
        if (doc.update_stock or not receipt or receipt.parent != row.purchase_receipt or receipt.docstatus != 1
                or receipt.item_code != row.item_code or receipt.custom_lebaref_sustitucion != approval.name):
            frappe.throw("Factura el sustituto desde su recepción confirmada.")
        return
    qty = sum(r.received_qty for r in doc.items if r.custom_lebaref_sustitucion == approval.name)
    if doc.is_return:
        original = frappe.db.get_value("Purchase Receipt Item", row.purchase_receipt_item,
            ["parent", "item_code", "custom_lebaref_sustitucion"], as_dict=True)
        if (not original or original.parent != doc.return_against or original.item_code != row.item_code
                or original.custom_lebaref_sustitucion != approval.name):
            frappe.throw("La devolución debe referir la recepción original del sustituto.")
    elif qty <= 0 or flt(qty + recibido(approval.name, doc.name) - approval.cantidad, row.precision("qty")) > 0:
        frappe.throw("La recepción excede la cantidad autorizada del sustituto.")


class CompraConSustituciones:
    def compare_prev_doc_values(self, prev_doc_detail_map, reference_doctype, reference_name, fields, doc=None):
        if reference_doctype == "Purchase Order Item" and doc and doc.get("custom_lebaref_sustitucion"):
            verificar_renglon(self, doc)
            fields = [field for field in fields if field[0] != "item_code"]
        return super().compare_prev_doc_values(prev_doc_detail_map, reference_doctype, reference_name, fields, doc)

    def validate(self):
        super().validate()
        for row in self.items:
            if row.get("custom_lebaref_sustitucion"):
                verificar_renglon(self, row)
