"""Directorio '¿A quién cotizar?', puente Google Sheets y consulta de Archivo Histórico SQLite (CAT-03 / OPS-HIST)."""
from collections import Counter
import json
import os
import frappe

SHEETS_ID = "1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I"
SHEETS_URL = f"https://docs.google.com/spreadsheets/d/{SHEETS_ID}/edit?gid=0#gid=0"


@frappe.whitelist(methods=["GET"])
def directorio():
    """Devuelve el directorio consolidado de proveedores de ERPNext con contacto, grupo, RFC e historial de cotización."""
    if not frappe.has_permission("Supplier", "read"):
        frappe.throw("No tienes permiso para consultar el directorio de proveedores.", frappe.PermissionError)

    tiene_esp = frappe.db.has_column("Supplier", "custom_especialidad_cotizar")
    campos_sup = ["name", "supplier_name", "supplier_group", "tax_id", "mobile_no", "email_id", "supplier_primary_contact"]
    if tiene_esp:
        campos_sup.append("custom_especialidad_cotizar")

    proveedores = frappe.get_list(
        "Supplier",
        filters={"disabled": 0},
        fields=campos_sup,
        order_by="supplier_name asc",
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
        grupo = (p.get("custom_especialidad_cotizar") or "").strip() or p.supplier_group or "General"
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


@frappe.whitelist(methods=["GET"])
def archivo_historico():
    """Consulta de solo lectura del archivo histórico migrado desde SQLite (Banco de Precios, 220 CFDIs y 229 OCs cerradas)."""
    if not (frappe.has_permission("Purchase Order", "read") or frappe.has_permission("Supplier Quotation", "read")):
        frappe.throw("No tienes permiso para consultar el archivo histórico de compras.", frappe.PermissionError)

    ruta = frappe.get_site_path("private", "files", "historico_sqlite_lebaref.json")
    base = {"precios": [], "cfdis": [], "ordenes": []}
    if os.path.exists(ruta):
        with open(ruta, encoding="utf-8") as f:
            base = json.load(f)

    if frappe.db.exists("DocType", "Lebaref Cotizacion Historica"):
        cot_rows = frappe.get_all(
            "Lebaref Cotizacion Historica",
            fields=[
                "name", "fecha_cotizacion", "item_code", "descripcion_original",
                "proveedor_texto", "precio_unitario_mxn", "moneda", "precio_unitario_sin_iva",
                "tc_dof", "dias_entrega_promesa", "gerencia", "se_compro_realmente",
            ],
            order_by="fecha_cotizacion desc, creation desc",
            limit_page_length=2000,
        )
        nuevos_precios = []
        for r in cot_rows:
            etq_mon = (
                f"Cotización histórica ({r.moneda} {r.precio_unitario_sin_iva:,.2f} · DOF {r.tc_dof:,.4f})"
                if r.moneda == "USD"
                else "Cotización histórica (no compra)"
            )
            nuevos_precios.append({
                "fecha": str(r.fecha_cotizacion or ""),
                "codigo": r.item_code or "SIN SKU",
                "concepto": r.descripcion_original or "",
                "familia": r.gerencia or "",
                "proveedor": r.proveedor_texto or "",
                "precio": float(r.precio_unitario_mxn or 0.0),
                "dias": int(r.dias_entrega_promesa or 0),
                "ganador": 0,
                "origen": etq_mon,
                "es_cotizacion_historica": 1,
            })
        base["precios"] = nuevos_precios + (base.get("precios") or [])

    return base
