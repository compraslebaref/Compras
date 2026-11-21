"""Lista los archivos adjuntos en ERPNext que pertenecen al expediente de una Solicitud (solo lectura).
Salida: una línea  EXPEDIENTE_JSON:[...]  con file, tipo, documento, proveedor, solicitud y ruta dentro del contenedor."""
import json
import os

os.chdir("/home/frappe/frappe-bench/sites")
import frappe

SITE = "lebaref-pruebas.local"
TIPOS = {
    "Material Request": "Solicitud",
    "Supplier Quotation": "Cotización",
    "Request for Quotation": "Petición de cotización",
    "Purchase Order": "Orden de compra",
    "Purchase Receipt": "Recepción",
}


def solicitud_de(dt, nm):
    q = frappe.db.sql
    if dt == "Material Request":
        return nm
    if dt == "Supplier Quotation":
        r = q("""SELECT COALESCE(NULLIF(i.material_request,''), rf.material_request) FROM `tabSupplier Quotation Item` i
                 LEFT JOIN `tabRequest for Quotation Item` rf ON rf.name = i.request_for_quotation_item
                 WHERE i.parent=%s AND COALESCE(NULLIF(i.material_request,''), rf.material_request) IS NOT NULL LIMIT 1""", (nm,))
    elif dt == "Request for Quotation":
        r = q("SELECT material_request FROM `tabRequest for Quotation Item` WHERE parent=%s AND IFNULL(material_request,'')<>'' LIMIT 1", (nm,))
    elif dt == "Purchase Order":
        r = q("SELECT material_request FROM `tabPurchase Order Item` WHERE parent=%s AND IFNULL(material_request,'')<>'' LIMIT 1", (nm,))
    elif dt == "Purchase Receipt":
        r = q("""SELECT COALESCE(NULLIF(pri.material_request,''), poi.material_request) FROM `tabPurchase Receipt Item` pri
                 LEFT JOIN `tabPurchase Order Item` poi ON poi.name = pri.purchase_order_item
                 WHERE pri.parent=%s AND COALESCE(NULLIF(pri.material_request,''), poi.material_request) IS NOT NULL LIMIT 1""", (nm,))
    else:
        r = []
    return r[0][0] if r and r[0][0] else ""


def main():
    frappe.init(SITE)
    frappe.connect()
    try:
        salida = []
        for f in frappe.get_all(
            "File",
            filters={"attached_to_doctype": ["in", list(TIPOS)], "is_folder": 0},
            fields=["name", "file_name", "file_url", "is_private", "attached_to_doctype", "attached_to_name", "creation"],
            order_by="creation asc",
            limit_page_length=0,
        ):
            sol = solicitud_de(f.attached_to_doctype, f.attached_to_name)
            if not sol:
                continue
            proveedor = ""
            if f.attached_to_doctype == "Supplier Quotation":
                proveedor = frappe.db.get_value("Supplier Quotation", f.attached_to_name, "supplier_name") or ""
            elif f.attached_to_doctype == "Purchase Order":
                proveedor = frappe.db.get_value("Purchase Order", f.attached_to_name, "supplier_name") or ""
            url = f.file_url or ""
            if not url:
                continue
            base = frappe.get_site_path("private" if f.is_private else "public")
            ruta = os.path.join(base, url.lstrip("/")) if f.is_private or url.startswith("/files/") else ""
            if f.is_private and url.startswith("/private/"):
                ruta = os.path.join(frappe.get_site_path(), url.lstrip("/"))
            if not ruta or not os.path.isfile(ruta):
                continue
            ruta = os.path.abspath(ruta)
            salida.append({
                "id": f.name, "archivo": f.file_name, "tipo": TIPOS[f.attached_to_doctype],
                "documento": f.attached_to_name, "proveedor": proveedor, "solicitud": sol, "ruta": ruta,
                "departamento": frappe.db.get_value("Material Request", sol, "custom_lebaref_departamento") or "",
            })
        print("EXPEDIENTE_JSON:" + json.dumps(salida, ensure_ascii=False))
    finally:
        frappe.destroy()


if __name__ == "__main__":
    main()
