"""Migración segura, idempotente y con puntos de restauración desde SQLite Lebaref hacia ERPNext 16."""
import hashlib
import html
import json
import os
import sqlite3
import uuid
from collections import defaultdict

os.chdir("/home/frappe/frappe-bench/sites")
import frappe
from frappe.utils import add_days, flt, getdate, today
from erpnext.controllers.accounts_controller import get_taxes_and_charges

SITE = "lebaref-pruebas.local"
SQLITE_PATH = "/tmp/lebaref_mig/snapshot.db"
CATALOGO_DIR = f"/home/frappe/frappe-bench/sites/{SITE}/public/files/catalogo"


def det_uuid(seed: str) -> str:
    h = hashlib.sha256(("LEBAREF_MIG_SAFE_V2:" + seed).encode("utf-8")).digest()[:16]
    b = bytearray(h)
    b[6] = (b[6] & 0x0F) | 0x40
    b[8] = (b[8] & 0x3F) | 0x80
    return str(uuid.UUID(bytes=bytes(b)))


def map_uom_name(raw_uom: str, fallback: str = "Nos") -> str:
    u = (raw_uom or "").strip().lower()
    mapping = {
        "pieza": "Nos",
        "pza": "Nos",
        "unidad": "Nos",
        "metro": "Meter",
        "m": "Meter",
        "litro": "Litre",
        "kilogramo": "Kg",
        "kg": "Kg",
        "caja": "Box",
        "par": "Pair",
        "juego": "Set",
        "kit": "Set",
        "paquete": "Paquete",
        "rollo": "Rollo",
        "blister": "Paquete",
        "servicio": "Nos",
        "tramo": "Nos",
        "cubeta": "Nos",
        "galon": "Nos",
        "galón": "Nos",
        "bolsa": "Paquete",
    }
    target = mapping.get(u, fallback)
    if not frappe.db.exists("UOM", target):
        frappe.get_doc({"doctype": "UOM", "uom_name": target, "enabled": 1}).insert(ignore_permissions=True)
    else:
        frappe.db.set_value("UOM", target, "enabled", 1, update_modified=False)
    return target


def map_condicion_pago(texto: str) -> str:
    t = (texto or "").lower()
    if "30" in t:
        return "Crédito 30 días"
    if "15" in t or "8" in t:
        return "Crédito 15 días"
    if "50%" in t or "anticipo" in t:
        return "Anticipo 50% / Contra entrega 50%"
    if "contado" in t or "transferencia" in t:
        return "Contado (Contra entrega)"
    return ""


def main():
    frappe.init(SITE)
    frappe.connect()
    frappe.set_user("Administrator")

    from lebaref_compras import operaciones, proveedores, solicitudes

    conn = sqlite3.connect(f"file:{SQLITE_PATH}?mode=ro", uri=True)
    conn.row_factory = sqlite3.Row

    company_name = frappe.get_all("Company", pluck="name")[0]
    company = frappe.get_doc("Company", company_name)
    abbr = company.abbr

    stats = {
        "categorias_aseguradas": 0,
        "departamentos_asegurados": 0,
        "almacen_general": "",
        "proveedores_enriquecidos": 0,
        "obras_aseguradas": 0,
        "articulos_enriquecidos": 0,
        "imagenes_vinculadas": 0,
        "codigos_barras_registrados": 0,
        "conversiones_uom": 0,
        "precios_compra_actualizados": 0,
        "mapeos_xml_proveedor_sku": 0,
        "kits_migrados": 0,
        "garantias_migradas": 0,
        "inventario_inicial_folio": "",
        "inventario_inicial_renglones": 0,
        "solicitudes_abiertas_migradas": 0,
        "cotizaciones_abiertas_migradas": 0,
        "ordenes_abiertas_migradas": 0,
        "prestamos_historicos_migrados": 0,
        "archivo_historico_exportado": {},
        "advertencias": [],
    }

    # Permitir fracciones (ej. 0.5 tramos/piezas/rollos en conceptos libres como SOL-2026-0024)
    if frappe.db.exists("UOM", "Nos"):
        frappe.db.set_value("UOM", "Nos", "must_be_whole_number", 0, update_modified=False)

    # Corregir nombre de almacén si se creó con codificación de consola Windows previa
    for wh_row in frappe.get_all("Warehouse", filters={"company": company_name, "is_group": 0}, fields=["name", "warehouse_name"]):
        if "Almac" in (wh_row.warehouse_name or "") and "?" in (wh_row.warehouse_name or ""):
            nuevo_id = f"Almacén General - {abbr}"
            if wh_row.name != nuevo_id and not frappe.db.exists("Warehouse", nuevo_id):
                frappe.rename_doc("Warehouse", wh_row.name, nuevo_id, force=True)
            frappe.db.set_value("Warehouse", nuevo_id, "warehouse_name", "Almacén General", update_modified=False)

    # 0. Almacén General operativo y lista de precios de compra
    parent_wh = frappe.db.get_value("Warehouse", {"company": company_name, "is_group": 1}, "name")
    almacen_gen = frappe.db.get_value("Warehouse", {"company": company_name, "warehouse_name": "Almacén General"}, "name")
    if not almacen_gen:
        almacen_gen = frappe.get_doc({
            "doctype": "Warehouse",
            "warehouse_name": "Almacén General",
            "company": company_name,
            "parent_warehouse": parent_wh,
            "is_group": 0,
        }).insert(ignore_permissions=True).name
    stats["almacen_general"] = almacen_gen

    buying_pl = frappe.db.get_single_value("Buying Settings", "buying_price_list") or "Standard Buying"
    if not frappe.db.exists("Price List", buying_pl):
        frappe.get_doc({
            "doctype": "Price List",
            "price_list_name": buying_pl,
            "currency": company.default_currency or "MXN",
            "buying": 1,
            "enabled": 1,
        }).insert(ignore_permissions=True)

    iva_tpl = frappe.db.get_value(
        "Purchase Taxes and Charges Template",
        {"company": company_name, "title": "IVA Compras 16%", "disabled": 0},
        "name",
    ) or ""

    # 1. Categorías -> Item Group
    root_ig = frappe.db.get_value("Item Group", {"is_group": 1}, "name") or "All Item Groups"
    cat_by_id = {}
    for row in conn.execute("SELECT Id, Nombre FROM Categorias ORDER BY Id"):
        nom = (row["Nombre"] or "").strip()
        if not nom:
            continue
        if not frappe.db.exists("Item Group", nom):
            frappe.get_doc({
                "doctype": "Item Group",
                "item_group_name": nom,
                "parent_item_group": root_ig,
                "is_group": 0,
            }).insert(ignore_permissions=True)
        cat_by_id[row["Id"]] = nom
        stats["categorias_aseguradas"] += 1

    # 2. Departamentos -> Department
    dept_by_id = {}
    for row in conn.execute("SELECT Id, Nombre FROM Departamentos ORDER BY Id"):
        nom = (row["Nombre"] or "").strip()
        if not nom:
            continue
        dep_erp = frappe.db.get_value("Department", {"company": company_name, "department_name": nom}, "name")
        if not dep_erp:
            dep_erp = frappe.get_doc({
                "doctype": "Department",
                "department_name": nom,
                "company": company_name,
                "is_group": 0,
            }).insert(ignore_permissions=True).name
        dept_by_id[row["Id"]] = dep_erp
        stats["departamentos_asegurados"] += 1

    encargado_by_id = {
        r["Id"]: (r["Nombre"] or "").strip()
        for r in conn.execute("SELECT Id, Nombre FROM Encargados")
    }

    # 3. Proveedores -> Supplier + Contact + CondicionesPago + Mapa RFC
    sup_group_default = frappe.db.get_value("Supplier Group", {"is_group": 0}, "name") or "All Supplier Groups"
    sup_by_id = {}
    rfc_to_sup = {}
    existing_sups = {
        (s.supplier_name or s.name).strip().lower(): s.name
        for s in frappe.get_all("Supplier", fields=["name", "supplier_name"])
    }
    for row in conn.execute("SELECT * FROM Proveedores ORDER BY Id"):
        nom = (row["NombreComercial"] or row["RazonSocial"] or "").strip()
        if not nom:
            continue
        rfc = (row["Rfc"] or "").strip().upper()
        if rfc == "XAXX010101000":
            rfc = ""
        sup_name = existing_sups.get(nom.lower())
        if not sup_name and frappe.db.exists("Supplier", nom):
            sup_name = nom
        if not sup_name:
            res = operaciones.ejecutar(
                "proveedor",
                {
                    "supplier_name": nom,
                    "supplier_group": sup_group_default,
                    "tax_id": rfc,
                    "contacto": (row["Contacto"] or "").strip(),
                    "telefono": (row["Telefono"] or "").strip(),
                    "correo": (row["Correo"] or "").strip(),
                },
                det_uuid(f"SUP:{nom}"),
            )
            sup_name = res["folio"]
            existing_sups[nom.lower()] = sup_name

        sup_doc = frappe.get_doc("Supplier", sup_name)
        changed_sup = False
        if rfc and not sup_doc.tax_id:
            sup_doc.tax_id = rfc
            changed_sup = True
        cond_tpl = map_condicion_pago(row["CondicionesPago"])
        if cond_tpl and frappe.db.exists("Payment Terms Template", cond_tpl) and not sup_doc.payment_terms:
            sup_doc.payment_terms = cond_tpl
            changed_sup = True
        notas_sup = (row["Notas"] or "").strip()
        if notas_sup and not sup_doc.supplier_details:
            sup_doc.supplier_details = notas_sup
            changed_sup = True
        cto = (row["Contacto"] or "").strip()
        tel = (row["Telefono"] or "").strip()
        cor = (row["Correo"] or "").strip()
        if (cto or tel or cor) and not sup_doc.supplier_primary_contact:
            try:
                proveedores.configurar(sup_doc, {"contacto": cto, "telefono": tel, "correo": cor})
                changed_sup = True
            except Exception as ex:
                stats["advertencias"].append(f"Contacto proveedor {nom}: {ex}")
        if changed_sup:
            sup_doc.save(ignore_permissions=True)
        sup_by_id[row["Id"]] = sup_name
        if rfc:
            rfc_to_sup[rfc] = sup_name
        stats["proveedores_enriquecidos"] += 1

    # 4. Proyectos / Obras -> Project
    proj_by_id = {}
    existing_projs = {
        (p.project_name or p.name).strip().lower(): p.name
        for p in frappe.get_all("Project", fields=["name", "project_name"])
    }
    for row in conn.execute("SELECT Id, Nombre, EsStockGeneral FROM Proyectos ORDER BY Id"):
        nom = (row["Nombre"] or "").strip()
        if not nom or row["EsStockGeneral"]:
            continue
        prj_name = existing_projs.get(nom.lower())
        if not prj_name:
            res = operaciones.ejecutar("obra", {"project_name": nom, "company": company_name}, det_uuid(f"PRJ:{nom}"))
            prj_name = res["folio"]
            existing_projs[nom.lower()] = prj_name
        proj_by_id[row["Id"]] = prj_name
        stats["obras_aseguradas"] += 1

    # Pre-cargar ProductoProveedorMapa (558 mapeos XML RFC+ClaveProveedor -> ProductoId)
    xml_map_by_prod = defaultdict(list)
    for m in conn.execute("SELECT ProductoId, EmisorRfc, ClaveProveedor FROM ProductoProveedorMapa WHERE ProductoId > 0"):
        rfc = (m["EmisorRfc"] or "").strip().upper()
        clave = (m["ClaveProveedor"] or "").strip()[:140]
        sup_m = rfc_to_sup.get(rfc)
        if sup_m and clave:
            xml_map_by_prod[m["ProductoId"]].append((sup_m, clave))

    # 5. Productos -> Item + Categoría + Marca + Imagen + Código de Barras + Conversión UOM + Precio + Mapeo XML
    seen_barcodes = set(frappe.get_all("Item Barcode", pluck="barcode"))
    existing_prices = {
        ip.item_code: ip.name
        for ip in frappe.get_all("Item Price", filters={"price_list": buying_pl, "buying": 1}, fields=["name", "item_code"])
    }
    prod_code_by_id = {}
    opening_stock_rows = []

    for row in conn.execute("SELECT * FROM Productos ORDER BY Id"):
        sp_name = f"mig_item_{row['Id']}"
        frappe.db.savepoint(sp_name)
        try:
            code = (row["CodigoInterno"] or f"PRD-{row['Id']:04d}").strip()
            nom = (row["Nombre"] or code).strip()[:140]
            prod_code_by_id[row["Id"]] = code
            stock_uom = map_uom_name(row["UnidadAlmacen"], "Nos")
            ig_name = cat_by_id.get(row["CategoriaId"]) or "Products"
            if not frappe.db.exists("Item Group", ig_name):
                ig_name = root_ig

            if not frappe.db.exists("Item", code):
                operaciones.ejecutar(
                    "articulo",
                    {"item_code": code, "item_name": nom, "item_group": ig_name, "stock_uom": stock_uom},
                    det_uuid(f"ITM:{code}"),
                )

            item = frappe.get_doc("Item", code)
            item.item_group = ig_name

            marca = (row["Marca"] or "").strip()[:140]
            if marca:
                if not frappe.db.exists("Brand", marca):
                    frappe.get_doc({"doctype": "Brand", "brand": marca}).insert(ignore_permissions=True)
                item.brand = marca

            # Descripción enriquecida con anaquel y especificaciones técnicas
            partes_desc = []
            ubic = (row["Ubicacion"] or "").strip()
            if ubic:
                partes_desc.append(f"[Anaquel: {ubic}]")
            for etiqueta, campo_sql in (
                ("", "Descripcion"),
                ("Modelo", "Modelo"),
                ("Medidas", "Medidas"),
                ("Capacidad", "CapacidadElectrica"),
                ("Espec. 1", "Especificacion1"),
                ("Espec. 2", "Especificacion2"),
            ):
                val = (row[campo_sql] or "").strip()
                if val:
                    partes_desc.append(f"{etiqueta + ': ' if etiqueta else ''}{val}")
            if partes_desc:
                item.description = " · ".join(partes_desc)

            # Imagen real del catálogo visual
            img_ruta = (row["ImagenRuta"] or "").strip()
            if img_ruta:
                base_img = os.path.basename(img_ruta.replace("\\", "/"))
                local_img = os.path.join(CATALOGO_DIR, base_img)
                if os.path.exists(local_img):
                    item.image = f"/files/catalogo/{base_img}"
                    stats["imagenes_vinculadas"] += 1

            # Código de barras único para pistola lectora Datamax S30W
            cb = (row["CodigoBarras"] or "").strip()[:140]
            existing_item_cbs = {b.barcode for b in (item.barcodes or [])}
            if cb and cb not in existing_item_cbs:
                cb_use = cb if cb not in seen_barcodes else (code if code not in seen_barcodes else "")
                if cb_use:
                    item.append("barcodes", {"barcode": cb_use})
                    seen_barcodes.add(cb_use)
                    stats["codigos_barras_registrados"] += 1
            elif cb in existing_item_cbs:
                stats["codigos_barras_registrados"] += 1

            # Conversión de unidades mayoreo -> menudeo (CAT-02)
            factor = flt(row["FactorConversion"] or 1.0)
            if factor > 1.0:
                u_compra = map_uom_name(row["UnidadCompra"], "Paquete")
                if u_compra == item.stock_uom:
                    u_compra = map_uom_name("Paquete", "Paquete")
                uoms_map = {u.uom: u for u in (item.uoms or [])}
                if u_compra in uoms_map:
                    uoms_map[u_compra].conversion_factor = factor
                else:
                    item.append("uoms", {"uom": u_compra, "conversion_factor": factor})
                item.purchase_uom = u_compra
                stats["conversiones_uom"] += 1

            # Mapeos XML NoIdentificacion por proveedor (ProductoProveedorMapa -> Item Supplier)
            existing_sup_parts = {(s.supplier, s.supplier_part_no) for s in (item.supplier_items or [])}
            for sup_m, clave_m in xml_map_by_prod.get(row["Id"], []):
                if (sup_m, clave_m) not in existing_sup_parts:
                    item.append("supplier_items", {"supplier": sup_m, "supplier_part_no": clave_m})
                    existing_sup_parts.add((sup_m, clave_m))
                    stats["mapeos_xml_proveedor_sku"] += 1

            item.save(ignore_permissions=True)
            stats["articulos_enriquecidos"] += 1

            # Precio de referencia de última compra (Item Price)
            costo = flt(row["UltimoCostoCompra"] or 0.0)
            if costo > 0:
                if code in existing_prices:
                    frappe.db.set_value("Item Price", existing_prices[code], "price_list_rate", costo, update_modified=False)
                else:
                    ip_doc = frappe.get_doc({
                        "doctype": "Item Price",
                        "item_code": code,
                        "price_list": buying_pl,
                        "buying": 1,
                        "currency": company.default_currency or "MXN",
                        "price_list_rate": costo,
                    }).insert(ignore_permissions=True)
                    existing_prices[code] = ip_doc.name
                stats["precios_compra_actualizados"] += 1

            # Acumular saldo físico positivo para inventario inicial
            stock_actual = flt(row["StockActual"] or 0.0)
            if stock_actual > 0 and item.is_stock_item:
                opening_stock_rows.append({
                    "item_code": code,
                    "qty": stock_actual,
                    "uom": item.stock_uom,
                    "stock_uom": item.stock_uom,
                    "conversion_factor": 1.0,
                    "t_warehouse": almacen_gen,
                    "basic_rate": costo if costo > 0 else 0.0,
                    "allow_zero_valuation_rate": 1 if costo <= 0 else 0,
                })
        except Exception as ex:
            frappe.db.rollback(save_point=sp_name)
            stats["advertencias"].append(f"Producto {row['CodigoInterno']}: {ex}")

    frappe.db.commit()

    # 6. Maletines / Kits reales (KIT-0001, KIT-0002) y Garantías de Herramientas (HER)
    her_code_by_id = {
        r["Id"]: (r["CodigoIndividual"] or "").strip()
        for r in conn.execute("SELECT Id, CodigoIndividual FROM HerramientasActivos")
    }
    for k in conn.execute("SELECT * FROM KitsHerramientas ORDER BY Id"):
        cod_kit = (k["CodigoKit"] or "").strip().upper()
        if not cod_kit:
            continue
        sp_k = f"mig_kit_{k['Id']}"
        frappe.db.savepoint(sp_k)
        try:
            herramientas_kit = [
                her_code_by_id.get(kp["HerramientaActivoId"], "")
                for kp in conn.execute("SELECT HerramientaActivoId FROM KitHerramientasPartidas WHERE KitId = ? ORDER BY Id", (k["Id"],))
            ]
            herramientas_kit = [h for h in herramientas_kit if h and frappe.db.exists("Lebaref Herramienta", h)]
            if not herramientas_kit:
                continue
            if not frappe.db.exists("Lebaref Kit", cod_kit):
                operaciones.ejecutar(
                    "kit",
                    {
                        "codigo": cod_kit,
                        "company": company_name,
                        "nombre": (k["Nombre"] or cod_kit).strip()[:140],
                        "especialidad": (k["Especialidad"] or "").strip()[:140],
                        "ubicacion": (k["Ubicacion"] or "GABINETE-1").strip()[:140],
                        "rows": [{"herramienta": h} for h in herramientas_kit],
                    },
                    det_uuid(f"KIT:{cod_kit}"),
                )
            else:
                kit_doc = frappe.get_doc("Lebaref Kit", cod_kit)
                actuales_kit = [r.herramienta for r in (kit_doc.items or [])]
                if set(herramientas_kit) != set(actuales_kit):
                    operaciones.ejecutar(
                        "composicion_kit",
                        {
                            "motivo": "Migración inicial de composición real desde SQLite Lebaref",
                            "rows": [{"herramienta": h} for h in herramientas_kit],
                        },
                        det_uuid(f"KIT-COMP:{cod_kit}"),
                        cod_kit,
                    )
            stats["kits_migrados"] += 1
        except Exception as ex:
            frappe.db.rollback(save_point=sp_k)
            stats["advertencias"].append(f"Kit {cod_kit}: {ex}")

    for h in conn.execute("SELECT CodigoIndividual, FechaCompra, FechaFinGarantia, CostoAdquisicion FROM HerramientasActivos WHERE CostoAdquisicion > 0 OR COALESCE(FechaCompra,'') <> ''"):
        cod_h = (h["CodigoIndividual"] or "").strip()
        if not cod_h or not frappe.db.exists("Lebaref Herramienta", cod_h):
            continue
        if frappe.db.exists("Lebaref Garantia Herramienta", {"herramienta": cod_h}):
            stats["garantias_migradas"] += 1
            continue
        sp_g = f"mig_gar_{cod_h.replace('-', '_')}"
        frappe.db.savepoint(sp_g)
        try:
            f_compra = (h["FechaCompra"] or "2026-07-09").strip()[:10]
            tiene_gar = "Sí" if (h["FechaFinGarantia"] or "").strip() else "No"
            f_vence = (h["FechaFinGarantia"] or add_days(f_compra, 365)).strip()[:10]
            operaciones.ejecutar(
                "garantia_herramienta",
                {
                    "fecha_compra": f_compra,
                    "tiene_garantia": tiene_gar,
                    "fin_garantia": f_vence,
                    "costo_adquisicion": f"{max(0.0, flt(h['CostoAdquisicion'] or 0.0)):.2f}",
                    "referencia": f"MIG-SQLITE-{cod_h}",
                    "motivo": "Registro inicial migrado de forma segura desde SQLite Lebaref",
                },
                det_uuid(f"GAR:{cod_h}"),
                cod_h,
            )
            stats["garantias_migradas"] += 1
        except Exception as ex:
            frappe.db.rollback(save_point=sp_g)
            stats["advertencias"].append(f"Garantía {cod_h}: {ex}")

    frappe.db.commit()

    # 7. Inventario Físico Inicial (340 SKUs con StockActual > 0) — Idempotente por token único
    op_stock_token = det_uuid("MIG-OPENING-STOCK-V1")
    existing_se = frappe.db.get_value(
        "Stock Entry",
        {"custom_lebaref_operacion": op_stock_token, "docstatus": 1},
        "name",
    )
    if existing_se:
        stats["inventario_inicial_folio"] = existing_se
        stats["inventario_inicial_renglones"] = len(opening_stock_rows)
    elif opening_stock_rows:
        sp_se = "mig_opening_stock"
        frappe.db.savepoint(sp_se)
        try:
            se = frappe.get_doc({
                "doctype": "Stock Entry",
                "stock_entry_type": "Material Receipt",
                "company": company_name,
                "to_warehouse": almacen_gen,
                "posting_date": today(),
                "remarks": "Inventario físico inicial migrado de forma segura desde SQLite Lebaref",
                "custom_lebaref_operacion": op_stock_token,
                "items": opening_stock_rows,
            })
            se.insert(ignore_permissions=True)
            se.submit()
            stats["inventario_inicial_folio"] = se.name
            stats["inventario_inicial_renglones"] = len(se.items)
        except Exception as ex:
            frappe.db.rollback(save_point=sp_se)
            stats["advertencias"].append(f"Inventario inicial: {ex}")

    frappe.db.commit()

    # 8. Solicitudes activas ('Pendiente' y 'En Comparativa') + sus Cotizaciones parciales/completas
    extra_quotes_by_sp = defaultdict(list)
    for cpp in conn.execute("SELECT SolicitudPartidaId, ProveedorId, PrecioSinIva, DiasEntrega FROM CotizacionesPartidaProveedor WHERE PrecioSinIva > 0"):
        sup_cpp = sup_by_id.get(cpp["ProveedorId"])
        if sup_cpp:
            extra_quotes_by_sp[cpp["SolicitudPartidaId"]].append((sup_cpp, flt(cpp["PrecioSinIva"]), int(cpp["DiasEntrega"] or 0)))

    for sol in conn.execute("SELECT * FROM Solicitudes WHERE Estatus IN ('Pendiente', 'En Comparativa') ORDER BY Id"):
        folio_sol = (sol["Folio"] or f"SOL-{sol['Id']}").strip()
        if frappe.db.exists("Material Request", {"custom_lebaref_operacion": det_uuid(f"MIG-SOL:{folio_sol}"), "docstatus": 1}):
            stats["solicitudes_abiertas_migradas"] += 1
            continue
        sp_s = f"mig_sol_{sol['Id']}"
        frappe.db.savepoint(sp_s)
        try:
            partidas = list(conn.execute("SELECT * FROM SolicitudPartidas WHERE SolicitudId = ? ORDER BY Id", (sol["Id"],)))
            if not partidas:
                continue

            obra_erp = proj_by_id.get(sol["ProyectoId"]) or ""
            depto_erp = dept_by_id.get(sol["DepartamentoId"]) or ""
            solicitante_txt = encargado_by_id.get(sol["EncargadoId"]) or ""
            fecha_req = max(getdate((sol["Fecha"] or today())[:10]), getdate(today())).isoformat()

            rows_payload = []
            quotes_by_sup = defaultdict(dict)  # sup_name -> {row_idx: (rate, days)}

            for idx, pt in enumerate(partidas):
                qty = flt(pt["CantidadSolicitada"] or 0)
                if qty <= 0:
                    continue
                row_idx = len(rows_payload)
                sku = prod_code_by_id.get(pt["ProductoId"]) if pt["ProductoId"] and pt["ProductoId"] > 0 else ""
                concepto = (pt["DescripcionLibre"] or pt["NotasPartida"] or "").strip()
                if sku and frappe.db.exists("Item", sku):
                    r_dict = {
                        "item_code": sku,
                        "qty": qty,
                        "warehouse": almacen_gen,
                        "project": obra_erp,
                        "custom_lebaref_partida": (pt["NombrePartida"] or "").strip()[:140],
                    }
                else:
                    r_dict = {
                        "concepto_libre": concepto or f"Partida #{idx + 1} de {folio_sol}",
                        "qty": qty,
                        "project": obra_erp,
                        "custom_lebaref_partida": (pt["NombrePartida"] or "").strip()[:140],
                    }
                rows_payload.append(r_dict)

                # Recolectar hasta 3 cotizaciones en línea + opciones extra
                for p_col, r_col, d_col in (
                    ("Proveedor1Id", "PrecioSinIva1", "DiasEntrega1"),
                    ("Proveedor2Id", "PrecioSinIva2", "DiasEntrega2"),
                    ("Proveedor3Id", "PrecioSinIva3", "DiasEntrega3"),
                ):
                    sup_q = sup_by_id.get(pt[p_col]) if pt[p_col] else None
                    rate_q = flt(pt[r_col] or 0)
                    if sup_q and rate_q > 0:
                        quotes_by_sup[sup_q][row_idx] = (rate_q, max(0, int(pt[d_col] or 0)))

                for sup_q, rate_q, days_q in extra_quotes_by_sp.get(pt["Id"], []):
                    if rate_q > 0:
                        quotes_by_sup[sup_q][row_idx] = (rate_q, max(0, days_q))

            if not rows_payload:
                continue

            obra_titulo = frappe.db.get_value("Project", obra_erp, "project_name") if obra_erp else ""
            sol_payload = {
                "company": company_name,
                "schedule_date": fecha_req,
                "title": f"{folio_sol}" + (f" · {obra_titulo}" if obra_titulo else ""),
                "departamento": depto_erp,
                "solicitante": solicitante_txt,
                "notas": f"Folio original SQLite: {folio_sol} ({(sol['Fecha'] or '')[:16]})." + (f" {sol['Notas']}" if sol["Notas"] else ""),
                "rows": rows_payload,
            }
            mr_res = operaciones.ejecutar("solicitud", sol_payload, det_uuid(f"MIG-SOL:{folio_sol}"))
            mr_name = mr_res["folio"]
            stats["solicitudes_abiertas_migradas"] += 1

            if quotes_by_sup:
                sup_list = sorted(quotes_by_sup.keys())
                rfq_res = operaciones.ejecutar(
                    "peticion",
                    {"suppliers": "\n".join(sup_list)},
                    det_uuid(f"MIG-RFQ:{folio_sol}"),
                    mr_name,
                )
                rfq_doc = frappe.get_doc("Request for Quotation", rfq_res["folio"])
                for sup_q in sup_list:
                    row_offers = []
                    for r_idx, rfq_item in enumerate(rfq_doc.items):
                        if r_idx in quotes_by_sup[sup_q]:
                            rate_q, days_q = quotes_by_sup[sup_q][r_idx]
                            row_offers.append({"id": rfq_item.name, "rate": rate_q, "lead_time_days": days_q})
                    if row_offers:
                        oferta_payload = {
                            "supplier": sup_q,
                            "valid_till": add_days(today(), 30),
                            "rows": row_offers,
                        }
                        if iva_tpl:
                            oferta_payload["taxes_and_charges"] = iva_tpl
                        operaciones.ejecutar(
                            "oferta",
                            oferta_payload,
                            det_uuid(f"MIG-SQ:{folio_sol}:{sup_q}"),
                            rfq_doc.name,
                        )
                        stats["cotizaciones_abiertas_migradas"] += 1
        except Exception as ex:
            frappe.db.rollback(save_point=sp_s)
            stats["advertencias"].append(f"Solicitud {folio_sol}: {ex}")

    frappe.db.commit()

    # 9. Órdenes de Compra Abiertas (3 en borrador por autorizar + 1 autorizada lista para recibir en almacén)
    uom_libre = frappe.db.get_value("Item", solicitudes.ARTICULO_LIBRE, "stock_uom") or "Nos"
    for oc in conn.execute("SELECT * FROM OrdenesCompra WHERE Estatus NOT IN ('Recibida Completa', 'Cancelada') ORDER BY Id"):
        folio_oc = (oc["Folio"] or f"OC-{oc['Id']}").strip()
        op_oc_token = det_uuid(f"MIG-OC:{folio_oc}")
        if frappe.db.exists("Purchase Order", {"custom_lebaref_operacion": op_oc_token, "docstatus": ["<", 2]}):
            stats["ordenes_abiertas_migradas"] += 1
            continue

        sp_o = f"mig_oc_{oc['Id']}"
        frappe.db.savepoint(sp_o)
        try:
            sup_oc = sup_by_id.get(oc["ProveedorId"])
            if not sup_oc:
                continue
            obra_oc = proj_by_id.get(oc["ProyectoId"]) or None
            partidas_oc = list(conn.execute("SELECT * FROM OrdenCompraPartidas WHERE OrdenCompraId = ? ORDER BY Id", (oc["Id"],)))
            if not partidas_oc:
                continue

            fecha_oc = (oc["Fecha"] or today())[:10]
            sched_oc = max(getdate(fecha_oc), getdate(today())).isoformat()
            po_items = []
            for pt in partidas_oc:
                pendiente = flt(pt["CantidadPedida"] or 0) - flt(pt["CantidadRecibida"] or 0)
                if pendiente <= 0:
                    continue
                rate_pt = flt(pt["PrecioUnitarioSinIva"] or 0)
                sku = prod_code_by_id.get(pt["ProductoId"]) if pt["ProductoId"] and pt["ProductoId"] > 0 else ""
                if sku and frappe.db.exists("Item", sku):
                    it_uom = frappe.db.get_value("Item", sku, "stock_uom") or "Nos"
                    po_items.append({
                        "item_code": sku,
                        "qty": pendiente,
                        "uom": it_uom,
                        "stock_uom": it_uom,
                        "conversion_factor": 1.0,
                        "rate": rate_pt,
                        "schedule_date": sched_oc,
                        "warehouse": almacen_gen,
                        "project": obra_oc,
                    })
                else:
                    desc_esc = html.escape((pt["DescripcionLibre"] or f"Partida {folio_oc}").strip())
                    po_items.append({
                        "item_code": solicitudes.ARTICULO_LIBRE,
                        "description": desc_esc,
                        "custom_lebaref_concepto": desc_esc,
                        "qty": pendiente,
                        "uom": uom_libre,
                        "stock_uom": uom_libre,
                        "conversion_factor": 1.0,
                        "rate": rate_pt,
                        "schedule_date": sched_oc,
                        "project": obra_oc,
                    })

            if not po_items:
                continue

            cond_oc = map_condicion_pago(oc["CondicionPago"])
            f_prog = (oc["FechaProgramadaPago"] or "").strip()[:10]
            po_doc = frappe.get_doc({
                "doctype": "Purchase Order",
                "company": company_name,
                "supplier": sup_oc,
                "transaction_date": fecha_oc,
                "schedule_date": sched_oc,
                "custom_lebaref_operacion": op_oc_token,
                "items": po_items,
            })
            if iva_tpl and flt(oc["Iva"] or 0) > 0:
                po_doc.taxes_and_charges = iva_tpl
                po_doc.set("taxes", get_taxes_and_charges("Purchase Taxes and Charges Template", iva_tpl))
            if cond_oc and frappe.db.exists("Payment Terms Template", cond_oc):
                po_doc.payment_terms_template = cond_oc
            if len(f_prog) == 10:
                # Evitar conflicto si due_date < transaction_date en programación
                po_doc.custom_lebaref_fecha_programada_pago = f_prog

            po_doc.insert(ignore_permissions=True)
            po_doc.add_comment("Info", f"Migrada desde SQLite Lebaref · Folio original: {folio_oc} ({oc['Estatus']})")

            if oc["Estatus"] == "Pagada / Lista para Almacén":
                po_doc.submit()

            stats["ordenes_abiertas_migradas"] += 1
        except Exception as ex:
            frappe.db.rollback(save_point=sp_o)
            stats["advertencias"].append(f"OC {folio_oc}: {ex}")

    frappe.db.commit()

    # 10. Préstamos históricos cerrados (PRE-2026-0002 y PRE-2026-0003) con su retorno completo
    for pr in conn.execute("SELECT * FROM PrestamosHerramienta ORDER BY Id"):
        folio_pre = (pr["Folio"] or f"PRE-{pr['Id']}").strip()
        op_pre = det_uuid(f"MIG-PRE:{folio_pre}")
        if frappe.db.exists("Lebaref Prestamo", {"custom_lebaref_operacion": op_pre}):
            stats["prestamos_historicos_migrados"] += 1
            continue
        herramientas_pre = [
            her_code_by_id.get(pp["HerramientaActivoId"], "")
            for pp in conn.execute("SELECT HerramientaActivoId FROM PrestamoPartidas WHERE PrestamoHerramientaId = ? ORDER BY Id", (pr["Id"],))
        ]
        herramientas_pre = [h for h in herramientas_pre if h and frappe.db.exists("Lebaref Herramienta", h)]
        if not herramientas_pre:
            continue
        sp_p = f"mig_pre_{pr['Id']}"
        frappe.db.savepoint(sp_p)
        try:
            obra_pre = proj_by_id.get(pr["ProyectoId"]) or next(iter(proj_by_id.values()), "")
            enc_pre = encargado_by_id.get(pr["EncargadoId"]) or "Responsable de Obra"
            rec_pre = (pr["PersonaRecibe"] or enc_pre).strip()
            res_pre = operaciones.ejecutar(
                "prestamo",
                {
                    "company": company_name,
                    "obra": obra_pre,
                    "responsable": enc_pre,
                    "recibe": rec_pre,
                    "departamento": "HVAC",
                    "compromiso": add_days(today(), 1),
                    "notas": f"Vale histórico migrado desde SQLite: {folio_pre} ({(pr['FechaSalida'] or '')[:16]})",
                    "rows": [{"herramienta": h} for h in herramientas_pre],
                },
                op_pre,
            )
            pre_doc = frappe.get_doc("Lebaref Prestamo", res_pre["folio"])
            if "Devuelto" in (pr["Estatus"] or ""):
                operaciones.ejecutar(
                    "retorno_herramienta",
                    {
                        "recibe": "Almacén General",
                        "notas": f"Retorno histórico migrado de {folio_pre}",
                        "rows": [
                            {"herramienta": item.herramienta, "devolver": "Sí", "estado": "Bueno", "observaciones": "Devuelto completo en registro original"}
                            for item in pre_doc.items
                        ],
                    },
                    det_uuid(f"MIG-RET:{folio_pre}"),
                    pre_doc.name,
                )
            stats["prestamos_historicos_migrados"] += 1
        except Exception as ex:
            frappe.db.rollback(save_point=sp_p)
            stats["advertencias"].append(f"Préstamo {folio_pre}: {ex}")

    frappe.db.commit()

    # 11. Exportar archivo histórico de solo lectura (1,019 precios, 220 CFDIs 4.0 y 229 OCs cerradas) sin alterar contabilidad ni stock
    sup_label_by_id = {
        r["Id"]: (r["NombreComercial"] or r["RazonSocial"] or "").strip()
        for r in conn.execute("SELECT Id, NombreComercial, RazonSocial FROM Proveedores")
    }
    precios_hist = [
        {
            "fecha": (r["FechaCotizacion"] or "")[:10],
            "codigo": prod_code_by_id.get(r["ProductoId"], "") if r["ProductoId"] else "",
            "concepto": (r["DescripcionConcepto"] or "").strip(),
            "familia": (r["Familia"] or "").strip(),
            "proveedor": sup_label_by_id.get(r["ProveedorId"], ""),
            "precio": flt(r["PrecioSinIva"] or 0),
            "dias": int(r["DiasEntrega"] or 0),
            "ganador": bool(r["FueGanador"]),
            "origen": (r["OrigenFolio"] or "").strip(),
        }
        for r in conn.execute("SELECT * FROM BancoPreciosHistorico ORDER BY Id DESC")
    ]

    resumen_cfdi_map = defaultdict(list)
    for fc in conn.execute("SELECT FacturaXmlId, Descripcion FROM FacturaConceptos ORDER BY Id"):
        if len(resumen_cfdi_map[fc["FacturaXmlId"]]) < 3 and fc["Descripcion"]:
            resumen_cfdi_map[fc["FacturaXmlId"]].append(fc["Descripcion"].strip()[:45])

    cfdis_hist = [
        {
            "uuid": (r["Uuid"] or "").strip().upper(),
            "serie_folio": (r["SerieFolio"] or "").strip(),
            "fecha": (r["FechaTimbrado"] or "").strip(),
            "rfc": (r["EmisorRfc"] or "").strip().upper(),
            "emisor": (r["EmisorNombre"] or "").strip(),
            "moneda": (r["Moneda"] or "MXN").strip(),
            "subtotal": flt(r["Subtotal"] or 0),
            "iva": flt(r["Iva"] or 0),
            "total": flt(r["Total"] or 0),
            "oc_folio": (r["OrdenCompraFolio"] or "").strip(),
            "resumen": " · ".join(resumen_cfdi_map.get(r["Id"], [])),
        }
        for r in conn.execute("SELECT * FROM FacturasXml ORDER BY FechaTimbrado DESC, Id DESC")
    ]

    obra_label_by_id = {
        r["Id"]: (r["Nombre"] or "").strip()
        for r in conn.execute("SELECT Id, Nombre FROM Proyectos")
    }
    ordenes_hist = [
        {
            "folio": (r["Folio"] or "").strip(),
            "fecha": (r["Fecha"] or "").strip(),
            "proveedor": sup_label_by_id.get(r["ProveedorId"], ""),
            "obra": obra_label_by_id.get(r["ProyectoId"], ""),
            "depto": (r["DepartamentoNombre"] or "").strip(),
            "encargado": (r["EncargadoNombre"] or "").strip(),
            "subtotal": flt(r["Subtotal"] or 0),
            "iva": flt(r["Iva"] or 0),
            "total": flt(r["TotalConIva"] or 0),
            "condicion": (r["CondicionPago"] or "").strip(),
        }
        for r in conn.execute("SELECT * FROM OrdenesCompra WHERE Estatus = 'Recibida Completa' ORDER BY Id DESC")
    ]

    priv_dir = frappe.get_site_path("private", "files")
    os.makedirs(priv_dir, exist_ok=True)
    hist_path = os.path.join(priv_dir, "historico_sqlite_lebaref.json")
    with open(hist_path, "w", encoding="utf-8") as f_hist:
        json.dump({"precios": precios_hist, "cfdis": cfdis_hist, "ordenes": ordenes_hist}, f_hist, ensure_ascii=False)

    stats["archivo_historico_exportado"] = {
        "precios": len(precios_hist),
        "cfdis": len(cfdis_hist),
        "ordenes_cerradas": len(ordenes_hist),
    }

    frappe.clear_cache()
    print("MIGRACION_RESUMEN_JSON:" + json.dumps(stats, ensure_ascii=False))


if __name__ == "__main__":
    main()
