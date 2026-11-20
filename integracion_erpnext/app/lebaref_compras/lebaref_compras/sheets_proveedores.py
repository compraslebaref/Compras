"""Directorio '¿A quién cotizar?' y puente de sincronización con Google Sheets (CAT-03)."""
from collections import Counter
import frappe

SHEETS_ID = "1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I"
SHEETS_URL = f"https://docs.google.com/spreadsheets/d/{SHEETS_ID}/edit?gid=0#gid=0"


@frappe.whitelist(methods=["GET"])
def directorio():
    """Devuelve el directorio consolidado de proveedores de ERPNext con contacto, grupo, RFC e historial de cotización."""
    if not frappe.has_permission("Supplier", "read"):
        frappe.throw("No tienes permiso para consultar el directorio de proveedores.", frappe.PermissionError)

    proveedores = frappe.get_list(
        "Supplier",
        filters={"disabled": 0},
        fields=["name", "supplier_name", "supplier_group", "tax_id", "mobile_no", "email_id", "supplier_primary_contact"],
        order_by="supplier_group asc, supplier_name asc",
        limit_page_length=1000,
    )

    contactos_map = {}
    if frappe.has_permission("Contact", "read"):
        nombres_contacto = [p.supplier_primary_contact for p in proveedores if p.supplier_primary_contact]
        if nombres_contacto:
            for c in frappe.get_list(
                "Contact",
                filters={"name": ["in", nombres_contacto]},
                fields=["name", "full_name", "phone", "mobile_no", "email_id"],
                limit_page_length=1000,
            ):
                contactos_map[c.name] = c

    conteo_oc = Counter()
    if frappe.has_permission("Purchase Order", "read"):
        conteo_oc = Counter(
            frappe.get_list("Purchase Order", filters={"docstatus": 1}, pluck="supplier", limit_page_length=0)
        )

    conteo_cot = Counter()
    if frappe.has_permission("Supplier Quotation", "read"):
        conteo_cot = Counter(
            frappe.get_list("Supplier Quotation", filters={"docstatus": 1}, pluck="supplier", limit_page_length=0)
        )

    registros = []
    tsv_lineas = ["Especialidad / Grupo\tProveedor\tRFC\tContacto\tTeléfono\tCorreo\tCotizaciones\tÓrdenes"]
    for p in proveedores:
        cto = contactos_map.get(p.supplier_primary_contact) or {}
        nombre_cto = cto.get("full_name") or ""
        telefono = p.mobile_no or cto.get("mobile_no") or cto.get("phone") or ""
        correo = p.email_id or cto.get("email_id") or ""
        grupo = p.supplier_group or "General"
        nombre_prov = p.supplier_name or p.name
        rfc = p.tax_id or ""
        cots = conteo_cot.get(p.name, 0)
        ocs = conteo_oc.get(p.name, 0)

        registros.append({
            "id": p.name,
            "proveedor": nombre_prov,
            "especialidad": grupo,
            "rfc": rfc,
            "contacto": nombre_cto,
            "telefono": telefono,
            "correo": correo,
            "cotizaciones": cots,
            "ordenes": ocs,
        })
        tsv_lineas.append(
            "\t".join(
                str(v).replace("\t", " ").replace("\n", " ").strip()
                for v in (grupo, nombre_prov, rfc, nombre_cto, telefono, correo, cots, ocs)
            )
        )

    return {
        "sheets_id": SHEETS_ID,
        "sheets_url": SHEETS_URL,
        "total": len(registros),
        "proveedores": registros,
        "tsv": "\n".join(tsv_lineas),
    }
