"""Enriquecimiento idempotente y no destructivo de datos reales en ERPNext 16:
1. Ficha Técnica completa en los 665 Artículos (Clave/Cód. Barras, Ubicación, Modelo, Medidas, Capacidad, Espec. 1/2, Stock Mínimo, Último Costo).
2. Columnas visibles de Código/Clave y Descripción real en todas las Solicitudes, Cotizaciones, OCs y Recepciones + Nombre descriptivo de Solicitud.
3. Especialidades reales de cotización en Proveedores para la pestaña '¿A quién cotizar?'.
4. Carga directa de las 220 facturas XML históricas en 'CFDI 4.0 archivados' (Lebaref CFDI).
"""
from collections import Counter, defaultdict
import hashlib
import html
import json
import os
import sqlite3

os.chdir("/home/frappe/frappe-bench/sites")
import frappe
from frappe.utils import flt
from frappe.utils.file_manager import save_file
from lebaref_compras.ux_lebaref import configurar_ux_lebaref, _calcular_clave_y_descripcion

SITE = "lebaref-pruebas.local"
SQLITE_PATH = "/tmp/lebaref_mig/snapshot.db"
XML_DIR_TMP = "/tmp/lebaref_xmls"


def _inferir_especialidad(nombre: str, notas: str, familias: list[str]) -> str:
    if familias:
        top = [f for f, _ in Counter(familias).most_common(2) if f and f.lower() not in ("general", "otros", "sin familia")]
        if top:
            return " / ".join(top)[:140]
    txt = f"{nombre} {notas}".upper()
    if any(k in txt for k in ("REFRIG", "CLIMA", "AIR", "MIRAGE", "TRANE", "CARRIER", "FRIO", "HVAC", "BOHN")):
        return "HVAC, Aires Acondicionados y Refrigeración"
    if any(k in txt for k in ("ELECTR", "ILUMIN", "VOLT", "CABLE", "HUBBELL", "SOLAR", "TRANSFORM")):
        return "Material Eléctrico e Iluminación"
    if any(k in txt for k in ("AUTOZONE", "LLANT", "REFACC", "AUTOMOT", "LUBRIC", "GASOLIN", "COMBUST")):
        return "Automotriz, Refacciones y Flotilla"
    if any(k in txt for k in ("NIPLITO", "PLOMER", "HIDRAUL", "TUBER", "VALVUL", "BOMBA")):
        return "Plomería, Tubería y Ferretería"
    if any(k in txt for k in ("HOME DEPOT", "TRUPER", "FERRET", "TORNILL", "HERRAM", "MAKITA", "DEWALT", "MILWAUKEE")):
        return "Ferretería, Tornillería y Herramientas"
    if any(k in txt for k in ("SEGUR", "EPP", "UNIFORM", "EXTINT", "PROTECC")):
        return "EPP y Seguridad Industrial"
    if any(k in txt for k in ("ACERO", "PERFIL", "LAMINA", "SOLDAD", "CONCRET", "CONSTRUC", "PINTUR", "COMEX")):
        return "Construcción, Acero y Acabados"
    return "Abastecimiento General y Mantenimiento"


def main():
    frappe.init(SITE)
    frappe.connect()
    frappe.set_user("Administrator")

    try:
        configurar_ux_lebaref()
        frappe.db.commit()

        conn = sqlite3.connect(f"file:{SQLITE_PATH}?mode=ro", uri=True)
        conn.row_factory = sqlite3.Row

        company_name = frappe.get_all("Company", pluck="name")[0]
        company_rfc = (frappe.db.get_value("Company", company_name, "tax_id") or "LSM150727IP0").upper()

        resumen = {
            "items_ficha_actualizados": 0,
            "renglones_vista_actualizados": 0,
            "solicitudes_nombradas": 0,
            "proveedores_especialidad": 0,
            "cfdis_historicos_insertados": 0,
            "xml_adjuntos_vinculados": 0,
        }

        # 1. Enriquecer los 665 Artículos con los campos exactos de la Ficha Técnica original
        for p in conn.execute("SELECT * FROM Productos ORDER BY Id"):
            code = (p["CodigoInterno"] or f"PRD-{p['Id']:04d}").strip()
            if not frappe.db.exists("Item", code):
                continue
            cb = (p["CodigoBarras"] or code).strip()[:140]
            valores_item = {
                "custom_lebaref_codigo_barras": cb,
                "custom_lebaref_ubicacion": ((p["Ubicacion"] or "Almacén General").strip())[:140],
                "custom_lebaref_stock_minimo": flt(p["StockMinimo"] or 0.0),
                "custom_lebaref_ultimo_costo": flt(p["UltimoCostoCompra"] or 0.0),
                "custom_lebaref_modelo": ((p["Modelo"] or "").strip())[:140],
                "custom_lebaref_medidas": ((p["Medidas"] or "").strip())[:140],
                "custom_lebaref_capacidad": ((p["CapacidadElectrica"] or "").strip())[:140],
                "custom_lebaref_especificacion_1": ((p["Especificacion1"] or "").strip())[:140],
                "custom_lebaref_especificacion_2": ((p["Especificacion2"] or "").strip())[:140],
            }
            frappe.db.set_value("Item", code, valores_item, update_modified=False)
            resumen["items_ficha_actualizados"] += 1

        frappe.db.commit()

        # 2. Poblar Código/Clave y Descripción real en todas las tablas hijas de compras existentes
        tablas_hijas = (
            "Material Request Item",
            "Request for Quotation Item",
            "Supplier Quotation Item",
            "Purchase Order Item",
            "Purchase Receipt Item",
        )
        for dt_row in tablas_hijas:
            meta_r = frappe.get_meta(dt_row)
            campos_leer = ["name", "item_code", "item_name", "description"]
            if meta_r.has_field("custom_lebaref_concepto"):
                campos_leer.append("custom_lebaref_concepto")
            if meta_r.has_field("custom_lebaref_articulo_original"):
                campos_leer.append("custom_lebaref_articulo_original")

            for r in frappe.get_all(dt_row, fields=campos_leer, limit_page_length=0):
                clave_v, desc_v = _calcular_clave_y_descripcion(
                    r.get("item_code") or "",
                    r.get("item_name") or "",
                    r.get("description") or "",
                    r.get("custom_lebaref_concepto") or "",
                    r.get("custom_lebaref_articulo_original") or "",
                )
                frappe.db.set_value(
                    dt_row,
                    r.name,
                    {
                        "custom_lebaref_clave_vista": clave_v,
                        "custom_lebaref_descripcion_vista": desc_v,
                    },
                    update_modified=False,
                )
                resumen["renglones_vista_actualizados"] += 1

        # Asignar Nombre descriptivo claro a todas las Solicitudes existentes
        for mr in frappe.get_all("Material Request", fields=["name", "title", "custom_lebaref_nombre_solicitud", "custom_lebaref_notas"], limit_page_length=0):
            primeros = frappe.get_all(
                "Material Request Item",
                filters={"parent": mr.name},
                fields=["custom_lebaref_descripcion_vista", "project"],
                order_by="idx asc",
                limit_page_length=2,
            )
            muestra_art = ", ".join(
                (x.custom_lebaref_descripcion_vista or "")[:28].strip()
                for x in primeros
                if x.custom_lebaref_descripcion_vista
            )
            base_tit = (mr.title or "").strip()
            if base_tit in ("", "Purchase", "Material Transfer", "Material Issue"):
                obra_mr = (primeros[0].project if primeros and primeros[0].project else "")
                partes_nom = [p for p in (obra_mr, muestra_art) if p]
                nuevo_nom = " · ".join(partes_nom)[:140] if partes_nom else f"Solicitud {mr.name}"
            elif muestra_art and muestra_art.lower() not in base_tit.lower():
                nuevo_nom = f"{base_tit} ({muestra_art})"[:140]
            else:
                nuevo_nom = base_tit[:140]

            frappe.db.set_value(
                "Material Request",
                mr.name,
                {"custom_lebaref_nombre_solicitud": nuevo_nom, "title": nuevo_nom},
                update_modified=False,
            )
            resumen["solicitudes_nombradas"] += 1

        frappe.db.commit()

        # 3. Poblar Especialidad de Cotización en Proveedores
        familias_por_prov_id = defaultdict(list)
        for bph in conn.execute("SELECT ProveedorId, Familia FROM BancoPreciosHistorico WHERE COALESCE(Familia,'') <> ''"):
            familias_por_prov_id[bph["ProveedorId"]].append((bph["Familia"] or "").strip())

        rfc_to_sup = {}
        name_to_sup = {}
        for s in frappe.get_all("Supplier", fields=["name", "supplier_name", "tax_id"], limit_page_length=0):
            name_to_sup[(s.supplier_name or s.name).strip().lower()] = s.name
            if s.tax_id:
                rfc_to_sup[s.tax_id.strip().upper()] = s.name

        for sp in conn.execute("SELECT * FROM Proveedores ORDER BY Id"):
            nom_sp = (sp["NombreComercial"] or sp["RazonSocial"] or "").strip()
            rfc_sp = (sp["Rfc"] or "").strip().upper()
            erp_sup = name_to_sup.get(nom_sp.lower()) or (rfc_to_sup.get(rfc_sp) if rfc_sp else None)
            if not erp_sup:
                continue
            esp_actual = (frappe.db.get_value("Supplier", erp_sup, "custom_especialidad_cotizar") or "").strip()
            if not esp_actual:
                esp = _inferir_especialidad(nom_sp, sp["Notas"] or "", familias_por_prov_id.get(sp["Id"], []))
                frappe.db.set_value("Supplier", erp_sup, "custom_especialidad_cotizar", esp, update_modified=False)
                resumen["proveedores_especialidad"] += 1

        frappe.db.commit()

        # 4. Importar las 220 facturas XML históricas directamente a 'Lebaref CFDI' (CFDI 4.0 archivados)
        conceptos_por_xml = defaultdict(list)
        for fc in conn.execute("SELECT FacturaXmlId, Cantidad, Unidad, Descripcion, Importe FROM FacturaConceptos ORDER BY Id"):
            desc_fc = (fc["Descripcion"] or "").strip()
            if desc_fc:
                conceptos_por_xml[fc["FacturaXmlId"]].append(
                    f"• {flt(fc['Cantidad'] or 0):g} {(fc['Unidad'] or '').strip()} — {desc_fc} (${flt(fc['Importe'] or 0):,.2f})"
                )

        archivos_xml_tmp = {}
        if os.path.isdir(XML_DIR_TMP):
            for fn in os.listdir(XML_DIR_TMP):
                if fn.lower().endswith(".xml"):
                    archivos_xml_tmp[fn.lower()] = os.path.join(XML_DIR_TMP, fn)

        for fx in conn.execute("SELECT * FROM FacturasXml ORDER BY FechaTimbrado DESC, Id DESC"):
            uuid_str = (fx["Uuid"] or "").strip().upper()
            if not uuid_str:
                continue

            rfc_em = (fx["EmisorRfc"] or "").strip().upper()
            nom_em = (fx["EmisorNombre"] or "").strip()
            prov_link = rfc_to_sup.get(rfc_em) or name_to_sup.get(nom_em.lower())

            # Si el proveedor del XML histórico no estaba en Supplier, darlo de alta limpio para conservar vínculo
            if not prov_link and nom_em:
                sg = frappe.db.get_value("Supplier Group", {"is_group": 0}, "name") or "All Supplier Groups"
                sup_nuevo = frappe.get_doc({
                    "doctype": "Supplier",
                    "supplier_name": nom_em[:140],
                    "supplier_group": sg,
                    "supplier_type": "Company",
                    "tax_id": rfc_em or None,
                    "custom_especialidad_cotizar": _inferir_especialidad(nom_em, "", []),
                }).insert(ignore_permissions=True)
                prov_link = sup_nuevo.name
                name_to_sup[nom_em.lower()] = prov_link
                if rfc_em:
                    rfc_to_sup[rfc_em] = prov_link

            conceptos_txt = "\n".join(conceptos_por_xml.get(fx["Id"], [])[:15])[:1200]
            serie_f = (fx["SerieFolio"] or "").strip()[:140]
            f_timb = (fx["FechaTimbrado"] or "").strip()[:16]
            oc_orig = (fx["OrdenCompraFolio"] or "Histórico SQLite").strip()[:140]
            moneda = (fx["Moneda"] or "MXN").strip()[:10]
            subtotal = flt(fx["Subtotal"] or 0.0)
            iva = flt(fx["Iva"] or 0.0)
            total = flt(fx["Total"] or 0.0)

            ruta_win = (fx["RutaArchivoXml"] or "").strip()
            base_xml = os.path.basename(ruta_win.replace("\\", "/")).lower() if ruta_win else ""
            cand_xml = archivos_xml_tmp.get(base_xml) or archivos_xml_tmp.get(f"{uuid_str.lower()}.xml")
            raw_bytes = None
            if cand_xml and os.path.isfile(cand_xml):
                with open(cand_xml, "rb") as fxml:
                    raw_bytes = fxml.read()

            huella = hashlib.sha256(raw_bytes if raw_bytes else f"HIST-CFDI:{uuid_str}:{total}".encode("utf-8")).hexdigest()

            if frappe.db.exists("Lebaref CFDI", uuid_str):
                frappe.db.set_value(
                    "Lebaref CFDI",
                    uuid_str,
                    {
                        "proveedor": prov_link,
                        "custom_serie_folio": serie_f,
                        "custom_fecha_timbrado": f_timb,
                        "custom_oc_origen": oc_orig,
                        "custom_subtotal": subtotal,
                        "custom_iva": iva,
                        "custom_conceptos_xml": conceptos_txt,
                    },
                    update_modified=False,
                )
            else:
                doc_cfdi = frappe.get_doc({
                    "doctype": "Lebaref CFDI",
                    "uuid": uuid_str,
                    "proveedor": prov_link,
                    "rfc_emisor": rfc_em,
                    "rfc_receptor": company_rfc,
                    "moneda": moneda,
                    "total": total,
                    "huella": huella,
                    "custom_serie_folio": serie_f,
                    "custom_fecha_timbrado": f_timb,
                    "custom_oc_origen": oc_orig,
                    "custom_subtotal": subtotal,
                    "custom_iva": iva,
                    "custom_conceptos_xml": conceptos_txt,
                })
                doc_cfdi.flags.lebaref_xml_validado = True
                doc_cfdi.flags.ignore_mandatory = True
                doc_cfdi.insert(ignore_permissions=True)
                resumen["cfdis_historicos_insertados"] += 1

            if raw_bytes and not frappe.db.get_value("Lebaref CFDI", uuid_str, "xml"):
                att = save_file(f"{uuid_str}.xml", raw_bytes, "Lebaref CFDI", uuid_str, is_private=1)
                frappe.db.set_value("Lebaref CFDI", uuid_str, "xml", att.file_url, update_modified=False)
                resumen["xml_adjuntos_vinculados"] += 1

        frappe.db.commit()
        frappe.clear_cache()
        print("ENRIQUECIMIENTO_UX_OK:" + json.dumps(resumen, ensure_ascii=False))
    finally:
        frappe.destroy()


if __name__ == "__main__":
    main()
