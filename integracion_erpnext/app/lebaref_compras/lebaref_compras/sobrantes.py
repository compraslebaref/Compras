"""Registro autorizado de sobrantes históricos sin entrega vinculada."""
import hashlib
import json
import frappe
from lebaref_compras import unidades

TIPOS = ("Traslado", "Entrada no contabilizada")


def comprobar_autorizacion():
    if not frappe.has_permission("Stock Reconciliation", "submit"):
        frappe.throw("Tu cuenta no tiene permiso para autorizar este ingreso de inventario.", frappe.PermissionError)


def crear(data):
    tipo = data.get("tipo")
    if tipo not in TIPOS:
        frappe.throw("Selecciona Traslado o Entrada no contabilizada.")
    if tipo == "Entrada no contabilizada" and data.get("from_warehouse"):
        frappe.throw("Para stock registrado en un almacén selecciona Traslado.")
    rows = data.get("rows")
    if not isinstance(rows, list) or not rows:
        frappe.throw("Captura los artículos del sobrante.")
    items = []
    for row in rows:
        if not isinstance(row, dict):
            frappe.throw("Renglón de sobrante inválido.")
        item = frappe.get_doc("Item", row.get("item_code"))
        item.check_permission("read")
        units = dict(uom=item.stock_uom, stock_uom=item.stock_uom, conversion_factor=1)
        unidades.verificar_referencia(row, units)
        items.append(dict(item_code=item.name, qty=unidades.numero(row.get("qty")), **units,
            s_warehouse=data.get("from_warehouse") if tipo == "Traslado" else None,
            t_warehouse=data.get("to_warehouse"), project=data.get("project"),
            basic_rate=unidades.numero(row.get("valor_unitario")) if tipo == "Entrada no contabilizada" else 0))
    return frappe.get_doc(dict(doctype="Stock Entry", company=data.get("company"), project=data.get("project"),
        stock_entry_type="Material Transfer" if tipo == "Traslado" else "Material Receipt",
        custom_lebaref_tipo_sobrante=tipo, custom_lebaref_referencia_sobrante=data.get("referencia"),
        custom_lebaref_persona_sobrante=data.get("persona"),
        custom_lebaref_confirmacion_sobrante=data.get("confirmacion"),
        remarks=data.get("motivo"), items=items))


def validar(doc, method=None):
    if not doc.custom_lebaref_tipo_sobrante:
        if doc.custom_lebaref_referencia_sobrante or doc.custom_lebaref_clave_sobrante:
            frappe.throw("Identifica el tratamiento del sobrante.")
        return
    tipo = doc.custom_lebaref_tipo_sobrante
    if tipo not in TIPOS or doc.purpose != ("Material Transfer" if tipo == "Traslado" else "Material Receipt"):
        frappe.throw("El movimiento no corresponde al tratamiento del sobrante.")
    if doc.custom_lebaref_origen or any(r.custom_lebaref_renglon for r in doc.items):
        frappe.throw("Usa la devolución vinculada cuando existe una entrega original.")
    if not doc.project or not str(doc.remarks or "").strip() or not str(doc.custom_lebaref_persona_sobrante or "").strip():
        frappe.throw("El sobrante requiere obra de procedencia, persona y motivo.")
    project = frappe.get_doc("Project", doc.project)
    project.check_permission("read")
    if project.company != doc.company:
        frappe.throw("La obra de procedencia debe pertenecer a la empresa.")
    reference = " ".join(str(doc.custom_lebaref_referencia_sobrante or "").split()).upper()
    if not reference:
        frappe.throw("Indica la referencia del acta o comprobante físico del sobrante.")
    doc.custom_lebaref_referencia_sobrante = reference
    doc.custom_lebaref_clave_sobrante = hashlib.sha256(json.dumps([doc.company, reference], ensure_ascii=False).encode()).hexdigest()
    if tipo == "Entrada no contabilizada" and doc.custom_lebaref_confirmacion_sobrante != "NO CONTABILIZADO":
        frappe.throw("Verifica las existencias migradas y confirma NO CONTABILIZADO antes de ingresar stock nuevo.")
    for row in doc.items:
        item = frappe.get_doc("Item", row.item_code)
        if item.disabled or not item.is_stock_item:
            frappe.throw("El sobrante requiere artículos activos e inventariables.")
        unidades.numero(row.qty)
        if row.uom != item.stock_uom or row.conversion_factor != 1:
            frappe.throw("Captura el sobrante en la unidad de inventario del artículo.")
        if tipo == "Entrada no contabilizada":
            unidades.numero(row.basic_rate)
        if (row.get("project") or doc.project) != doc.project:
            frappe.throw("La obra del renglón debe coincidir con la procedencia del sobrante.")
        for name in (row.s_warehouse, row.t_warehouse):
            if not name:
                continue
            warehouse = frappe.get_doc("Warehouse", name)
            warehouse.check_permission("read")
            if warehouse.disabled or warehouse.is_group or warehouse.company != doc.company:
                frappe.throw("Selecciona almacenes activos de detalle de la empresa.")
        if not row.t_warehouse or (tipo == "Traslado" and (not row.s_warehouse or row.s_warehouse == row.t_warehouse)):
            frappe.throw("El traslado requiere almacenes distintos; evita ingresar material ya incluido en el destino.")
        if tipo == "Entrada no contabilizada" and row.s_warehouse:
            frappe.throw("Para stock ya registrado selecciona Traslado.")


def autorizar(doc, method=None):
    if doc.custom_lebaref_tipo_sobrante:
        comprobar_autorizacion()


def liberar_referencia(doc, method=None):
    if doc.custom_lebaref_tipo_sobrante:
        doc.db_set("custom_lebaref_clave_sobrante", None, update_modified=False)
