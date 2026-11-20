"""Configuración visual unificada de Lebaref en Frappe Desk 16 (Sidebar, Dock, Identidad Vectorial, Plantillas Comerciales y Mapa A–F)."""
import json
import os
import shutil
import frappe

SHEETS_URL = "https://docs.google.com/spreadsheets/d/1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I/edit?gid=0#gid=0"

WORKSPACE_SHORTCUTS = [
    # 1. Abastecimiento y Compras
    {"label": "Solicitudes de material", "type": "DocType", "link_to": "Material Request", "doc_view": "List", "color": "Blue"},
    {"label": "Peticiones de cotización", "type": "DocType", "link_to": "Request for Quotation", "doc_view": "List", "color": "Cyan"},
    {"label": "Cotizaciones de proveedores", "type": "DocType", "link_to": "Supplier Quotation", "doc_view": "List", "color": "Orange"},
    {"label": "Órdenes de compra", "type": "DocType", "link_to": "Purchase Order", "doc_view": "List", "color": "Green"},
    {"label": "CFDI 4.0 archivados", "type": "DocType", "link_to": "Lebaref CFDI", "doc_view": "List", "color": "Grey"},
    {"label": "¿A quién cotizar? (Sheets)", "type": "URL", "url": SHEETS_URL, "color": "Green"},
    # 2. Almacén, Obras y Catálogos Maestros (Stock Entry al final para no dejar hueco en roles sin Stock User)
    {"label": "Recepciones de compra", "type": "DocType", "link_to": "Purchase Receipt", "doc_view": "List", "color": "Green"},
    {"label": "Artículos (Catálogo)", "type": "DocType", "link_to": "Item", "doc_view": "List", "color": "Grey"},
    {"label": "Proveedores", "type": "DocType", "link_to": "Supplier", "doc_view": "List", "color": "Grey"},
    {"label": "Obras / Proyectos", "type": "DocType", "link_to": "Project", "doc_view": "List", "color": "Grey"},
    {"label": "Existencias por almacén", "type": "DocType", "link_to": "Bin", "doc_view": "List", "color": "Cyan"},
    {"label": "Entregas y sobrantes", "type": "DocType", "link_to": "Stock Entry", "doc_view": "List", "color": "Blue"},
    # 3. Custodia de Herramientas, Kits y Flotilla
    {"label": "Herramientas (HER)", "type": "DocType", "link_to": "Lebaref Herramienta", "doc_view": "List", "color": "Orange"},
    {"label": "Préstamos (PRE)", "type": "DocType", "link_to": "Lebaref Prestamo", "doc_view": "List", "color": "Yellow"},
    {"label": "Maletines / Kits (KIT)", "type": "DocType", "link_to": "Lebaref Kit", "doc_view": "List", "color": "Blue"},
    {"label": "Mantenimientos (HER)", "type": "DocType", "link_to": "Lebaref Mantenimiento Herramienta", "doc_view": "List", "color": "Grey"},
    {"label": "Flotilla vehicular (VEH)", "type": "DocType", "link_to": "Lebaref Vehiculo", "doc_view": "List", "color": "Blue"},
    {"label": "Gastos y combustible", "type": "DocType", "link_to": "Lebaref Gasto Vehiculo", "doc_view": "List", "color": "Orange"},
    {"label": "Check-list vehicular", "type": "DocType", "link_to": "Lebaref Revision Vehiculo", "doc_view": "List", "color": "Green"},
]

SITE_DOCK_ERPNEXT = [
    {"link_type": "Sidebar", "link_to": "Lebaref Compras", "icon": "box-soft", "title": "Lebaref", "added": 0, "hidden": 0},
    {"link_type": "Sidebar", "link_to": "Buying", "icon": "shopping-cart-soft", "title": "Buying", "added": 0, "hidden": 0},
    {"link_type": "Sidebar", "link_to": "Stock", "icon": "shelf-rack-soft", "title": "Stock", "added": 0, "hidden": 0},
    {"link_type": "Sidebar", "link_to": "Projects", "icon": "gantt-chart-soft", "title": "Projects", "added": 0, "hidden": 0},
    {"link_type": "Sidebar", "link_to": "Accounts", "icon": "calculator-soft", "title": "Accounts", "added": 0, "hidden": 0},
    {"link_type": "Sidebar", "link_to": "Setup", "icon": "sliders-soft", "title": "Setup", "added": 0, "hidden": 0},
    {"link_type": "Sidebar", "link_to": "Selling", "icon": "storefront-soft", "title": "Selling", "added": 0, "hidden": 1},
    {"link_type": "Sidebar", "link_to": "Manufacturing", "icon": "factory-soft", "title": "Manufacturing", "added": 0, "hidden": 1},
    {"link_type": "Sidebar", "link_to": "Assets", "icon": "asset-hand-soft", "title": "Assets", "added": 0, "hidden": 1},
    {"link_type": "Sidebar", "link_to": "Quality", "icon": "quality-seal-soft", "title": "Quality", "added": 0, "hidden": 1},
    {"link_type": "Sidebar", "link_to": "CRM", "icon": "handshake-soft", "title": "CRM", "added": 0, "hidden": 1},
    {"link_type": "Sidebar", "link_to": "Support", "icon": "headset-soft", "title": "Support", "added": 0, "hidden": 1},
    {"link_type": "Sidebar", "link_to": "Portal", "icon": "portal-user-soft", "title": "Portal", "added": 0, "hidden": 1},
]

ZONAS_ALMACEN = (
    ("A", "Zona A · Herramienta Manual y Mecánica", "Anaqueles A1–A3 · Llaves, dados, corte y sujeción", "#0d5b94"),
    ("B", "Zona B · Equipo Eléctrico y Medición", "Gabinete B1–B3 · Taladros, rotomartillos, multímetros y pinzas", "#0284c7"),
    ("C", "Zona C · Tornillería y Consumibles", "Gavetas C1–C3 · Taquetes, pijas, cintas, discos y selladores", "#059669"),
    ("D", "Zona D · HVAC, Cobre y Tubería", "Racks D1–D3 · Refrigeración, aislamiento, válvulas y canalización", "#d97706"),
    ("E", "Zona E · EPP, Seguridad y Maletines (KIT)", "Resguardo E1–E3 · Cascos, arneses, guantes y kits armados", "#7c3aed"),
    ("F", "Zona F · Bodega General y Obras en Curso", "Patio / Cuadrillas · Material asignado o herramienta en obra", "#475569"),
)


def _clasificar_zona_herramienta(ubicacion, articulo, estado, obra):
    ub = (ubicacion or "").strip().upper()
    art = (articulo or "").upper()
    if estado == "Prestada" or (obra and not ub):
        return "F"
    for letra in ("A", "B", "C", "D", "E", "F"):
        if ub.startswith(f"ZONA {letra}") or ub.startswith(f"ANAQUEL {letra}") or ub.startswith(f"GABINETE {letra}") or ub.startswith(f"{letra}-") or ub.startswith(f"{letra}1") or ub.startswith(f"{letra}2") or ub.startswith(f"{letra}3"):
            return letra
    if any(k in art for k in ("AMPER", "MULTIM", "TALADRO", "ROTOMARTILLO", "ESMERIL", "SIERRA", "PISTOLA", "LASER", "TERMOM")):
        return "B"
    if any(k in art for k in ("MANOMETRO", "VACIO", "BOMBA", "DOBLADOR", "AVELLAN", "COBRE", "HVAC", "REFRIG")):
        return "D"
    if any(k in art for k in ("ARNES", "CASCO", "CHALECO", "ESCALERA", "CARETA", "KIT", "MALETIN")):
        return "E"
    return "A"


def _clasificar_zona_bin(warehouse, item_code, anaquel="", grupo=""):
    wh = (warehouse or "").upper()
    ic = (item_code or "").upper()
    anq = (anaquel or "").upper()
    grp = (grupo or "").upper()
    if "OBRA" in wh or "PROY" in wh:
        return "F"
    if anq.startswith("A-") or anq.startswith("A0") or "DETRAS-A" in anq:
        return "A"
    if "GABINETE" in anq or anq.startswith("B-"):
        return "B"
    if "REFRIG" in grp or "HVAC" in grp or any(k in ic for k in ("COBRE", "TUB", "GAS", "REFRIG", "AISLA", "HVAC", "DREN")):
        return "D"
    if "ELÉCTRIC" in grp or "ELECTRIC" in grp or any(k in ic for k in ("CABLE", "PASTILLA", "INTERRUP", "CONTACTO", "CANALETA", "CONDUIT")):
        return "B"
    if "SEGURIDAD" in grp or "EPP" in grp or any(k in ic for k in ("GUANTE", "CASCO", "LENTE", "EPP", "CHALECO", "BOTA")):
        return "E"
    return "C"


def _construir_contenido_workspace():
    pill_base = (
        "display:inline-block;padding:5px 11px;border-radius:6px;font-size:11.5px;font-weight:700;"
        "text-decoration:none;border:1px solid #cbd5e1;margin-left:6px;margin-top:4px;"
    )
    banner_html = (
        '<div style="display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px;'
        'padding:10px 16px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;margin-bottom:4px;">'
        '<img src="/files/lebaref_logo_horizontal.svg" alt="Lebaref" style="height:54px;width:auto;display:block;">'
        '<div style="text-align:right;font-size:12px;color:#475569;">'
        '<b style="color:#031e34;font-size:13px;">Plataforma Operativa de Compras, Almacén y Activos</b><br>'
        '<span>Ingeniería · Construcción · HVAC · Electricidad · Remodelación</span><br>'
        f'<a href="#leb-escanear-s30w" style="{pill_base}background:#031e34;color:#ffffff;border-color:#031e34;">🔫 Escanear S30W (F2)</a>'
        f'<a href="#leb-codigos-carta" style="{pill_base}background:#0d5b94;color:#ffffff;border-color:#0d5b94;">🏷️ Hojas de Códigos Carta</a>'
        f'<a href="#leb-mapa-af" style="{pill_base}background:#ffffff;color:#031e34;">🗺️ Mapa Zonas A–F</a>'
        f'<a href="#leb-historico" style="{pill_base}background:#ffffff;color:#031e34;">📜 Archivo Histórico</a>'
        '</div></div>'
    )
    bloques = [
        {"id": "leb-brand-banner", "type": "paragraph", "data": {"text": banner_html, "col": 12}},
        {
            "id": "hdr-compras",
            "type": "header",
            "data": {"text": "<span class=\"h4\"><b>1. Abastecimiento y Compras</b></span>", "col": 12},
        },
    ]
    grupos = [
        ("compras", ["Solicitudes de material", "Peticiones de cotización", "Cotizaciones de proveedores", "Órdenes de compra", "CFDI 4.0 archivados", "¿A quién cotizar? (Sheets)"]),
        ("almacen", ["Recepciones de compra", "Artículos (Catálogo)", "Proveedores", "Obras / Proyectos", "Existencias por almacén", "Entregas y sobrantes"]),
        ("equipos", ["Herramientas (HER)", "Préstamos (PRE)", "Maletines / Kits (KIT)", "Mantenimientos (HER)", "Flotilla vehicular (VEH)", "Gastos y combustible", "Check-list vehicular"]),
    ]
    titulos = {
        "almacen": "<span class=\"h4\"><b>2. Almacén, Obras y Catálogos Maestros</b></span>",
        "equipos": "<span class=\"h4\"><b>3. Custodia de Herramientas, Kits y Flotilla</b></span>",
    }
    idx = 0
    for clave, etiquetas in grupos:
        if clave in titulos:
            bloques.append({"id": f"spc-{clave}", "type": "spacer", "data": {"col": 12}})
            bloques.append({"id": f"hdr-{clave}", "type": "header", "data": {"text": titulos[clave], "col": 12}})
        for etq in etiquetas:
            idx += 1
            bloques.append({"id": f"leb-sc-{idx}", "type": "shortcut", "data": {"shortcut_name": etq, "col": 4}})
    return json.dumps(bloques, ensure_ascii=False)


def configurar_plantillas_comerciales():
    """Configura por empresa las plantillas de IVA 16%, Compra Exenta 0%, Reglas de Flete, Condiciones de Pago y perfil Coordinador (COM-04 / COM-07 / ALM-02)."""
    from frappe.permissions import add_permission
    from frappe.core.doctype.custom_docperm.custom_docperm import update_custom_docperm

    # Permitir recepciones anticipadas sin OC en Buying Settings (ALM-02)
    if frappe.db.exists("DocType", "Buying Settings"):
        frappe.db.set_single_value("Buying Settings", "po_required", "No")

    # Perfil Coordinador Operativo (Compras + Almacén) para permitir regularización ALM-02 y operación integral sin alterar usuarios de prueba
    perfil_coord = "Lebaref Coordinador Operativo"
    roles_coord = ("Purchase User", "Purchase Manager", "Stock User", "Stock Manager")
    if not frappe.db.exists("Role Profile", perfil_coord):
        frappe.get_doc({
            "doctype": "Role Profile",
            "role_profile": perfil_coord,
            "roles": [{"role": r} for r in roles_coord if frappe.db.exists("Role", r)],
        }).insert(ignore_permissions=True)

    # Asegurar cuenta operativa real de Oscar en español con perfil Coordinador Operativo
    correo_oscar = "oscar.gomez@lebaref.com"
    if not frappe.db.exists("User", correo_oscar):
        u_doc = frappe.get_doc({
            "doctype": "User",
            "email": correo_oscar,
            "first_name": "Oscar",
            "last_name": "Gómez",
            "language": "es",
            "enabled": 1,
            "send_welcome_email": 0,
            "role_profile_name": perfil_coord,
        })
        u_doc.insert(ignore_permissions=True)

    # Asegurar permiso de lectura/selección en maestros comerciales para Compras
    for dt in ("Purchase Taxes and Charges Template", "Shipping Rule", "Payment Terms Template"):
        if not frappe.db.exists("DocType", dt):
            continue
        for role in ("Purchase User", "Purchase Manager"):
            rule = frappe.db.get_value(
                "Custom DocPerm",
                {"parent": dt, "role": role, "permlevel": 0, "if_owner": 0},
                ("name", "read", "select"),
                as_dict=True,
            )
            if not rule:
                try:
                    add_permission(dt, role, ptype="read")
                except Exception:
                    pass
            elif not (rule.read or rule.select):
                update_custom_docperm(rule.name, {"read": 1, "select": 1})

    # 1. Términos y Plantillas de Pago globales
    terminos_base = (
        ("Lebaref Contado", 100.0, 0),
        ("Lebaref Crédito 15 días", 100.0, 15),
        ("Lebaref Crédito 30 días", 100.0, 30),
        ("Lebaref Anticipo 50%", 50.0, 0),
        ("Lebaref Contra Entrega 50%", 50.0, 7),
    )
    for nombre_term, porcion, dias in terminos_base:
        if not frappe.db.exists("Payment Term", nombre_term):
            frappe.get_doc({
                "doctype": "Payment Term",
                "payment_term_name": nombre_term,
                "invoice_portion": porcion,
                "due_date_based_on": "Day(s) after invoice date",
                "credit_days": dias,
            }).insert(ignore_permissions=True)

    plantillas_pago = (
        ("Contado (Contra entrega)", [("Lebaref Contado", 100.0, 0)]),
        ("Crédito 15 días", [("Lebaref Crédito 15 días", 100.0, 15)]),
        ("Crédito 30 días", [("Lebaref Crédito 30 días", 100.0, 30)]),
        ("Anticipo 50% / Contra entrega 50%", [("Lebaref Anticipo 50%", 50.0, 0), ("Lebaref Contra Entrega 50%", 50.0, 7)]),
    )
    for nombre_tpl, filas in plantillas_pago:
        if not frappe.db.exists("Payment Terms Template", nombre_tpl):
            frappe.get_doc({
                "doctype": "Payment Terms Template",
                "template_name": nombre_tpl,
                "terms": [
                    {
                        "payment_term": t_name,
                        "invoice_portion": porc,
                        "due_date_based_on": "Day(s) after invoice date",
                        "credit_days": d,
                    }
                    for t_name, porc, d in filas
                ],
            }).insert(ignore_permissions=True)

    # 2. Plantillas de Impuestos (IVA 16% y Exento 0%) y Reglas de Flete por Empresa
    for emp_name in frappe.get_all("Company", pluck="name"):
        emp = frappe.get_doc("Company", emp_name)
        cuenta_iva = frappe.db.get_value(
            "Account",
            {"company": emp.name, "account_type": "Tax", "is_group": 0, "disabled": 0},
            "name",
        )
        if not cuenta_iva:
            padre_pasivo = frappe.db.get_value(
                "Account",
                {"company": emp.name, "root_type": "Liability", "is_group": 1, "parent_account": ["is", "set"]},
                "name",
            )
            if padre_pasivo:
                cuenta_iva = frappe.get_doc({
                    "doctype": "Account",
                    "account_name": "IVA Acreditable Compras",
                    "company": emp.name,
                    "parent_account": padre_pasivo,
                    "account_type": "Tax",
                }).insert(ignore_permissions=True).name

        if cuenta_iva:
            for titulo, tasa, descripcion, es_def in (
                ("IVA Compras 16%", 16.0, "IVA 16%", 0),
                ("Compra Exenta 0%", 0.0, "Exento de IVA (0%)", 0),
            ):
                existente = frappe.db.get_value(
                    "Purchase Taxes and Charges Template",
                    {"company": emp.name, "title": titulo},
                    "name",
                )
                if not existente:
                    frappe.get_doc({
                        "doctype": "Purchase Taxes and Charges Template",
                        "title": titulo,
                        "company": emp.name,
                        "is_default": es_def,
                        "taxes": [{
                            "charge_type": "On Net Total",
                            "account_head": cuenta_iva,
                            "description": descripcion,
                            "rate": tasa,
                            "category": "Total",
                            "add_deduct_tax": "Add",
                        }],
                    }).insert(ignore_permissions=True)

        if emp.default_expense_account:
            for etq_base, monto in (
                (f"Flete Local Estándar ({emp.abbr})", 250.0),
                (f"Flete Foráneo / Paquetería ({emp.abbr})", 650.0),
            ):
                if not frappe.db.exists("Shipping Rule", etq_base):
                    frappe.get_doc({
                        "doctype": "Shipping Rule",
                        "label": etq_base,
                        "company": emp.name,
                        "account": emp.default_expense_account,
                        "shipping_rule_type": "Buying",
                        "calculate_based_on": "Fixed",
                        "shipping_amount": monto,
                    }).insert(ignore_permissions=True)


def configurar_identidad_visual():
    """Publica los logotipos vectorizados SVG de Lebaref en /files/ y los aplica en Desk, Login, Favicon y Membrete."""
    from lebaref_compras.impresion_carta import svg_logo_lebaref

    origen_dir = frappe.get_app_path("lebaref_compras", "public", "img")
    destino_dir = frappe.get_site_path("public", "files")
    os.makedirs(destino_dir, exist_ok=True)

    archivos_svg = (
        "lebaref_logo_completo.svg",
        "lebaref_logo_horizontal.svg",
        "lebaref_isotipo.svg",
    )
    for nombre in archivos_svg:
        src = os.path.join(origen_dir, nombre)
        dst = os.path.join(destino_dir, nombre)
        if not os.path.exists(src):
            continue
        shutil.copyfile(src, dst)
        url = f"/files/{nombre}"
        if not frappe.db.exists("File", {"file_url": url, "is_private": 0}):
            frappe.get_doc({
                "doctype": "File",
                "file_name": nombre,
                "file_url": url,
                "is_private": 0,
                "folder": "Home",
            }).db_insert()

    if frappe.db.exists("DocType", "Website Settings"):
        for campo, valor in (
            ("app_name", "Lebaref"),
            ("app_logo", "/files/lebaref_isotipo.svg"),
            ("splash_image", "/files/lebaref_logo_completo.svg"),
            ("banner_image", "/files/lebaref_logo_horizontal.svg"),
            ("favicon", "/files/lebaref_isotipo.svg"),
        ):
            frappe.db.set_single_value("Website Settings", campo, valor)

    if frappe.db.exists("DocType", "Navbar Settings"):
        frappe.db.set_single_value("Navbar Settings", "app_logo", "/files/lebaref_isotipo.svg")

    if frappe.db.exists("DocType", "System Settings"):
        frappe.db.set_single_value("System Settings", "app_name", "Lebaref")

    for emp in frappe.get_all("Company", pluck="name"):
        frappe.db.set_value("Company", emp, "company_logo", "/files/lebaref_logo_horizontal.svg", update_modified=False)

    if frappe.db.exists("DocType", "Letter Head"):
        nombre_lh = "Membrete Oficial Lebaref"
        html_lh = (
            '<div style="display:flex;align-items:center;justify-content:space-between;'
            'border-bottom:2px solid #031e34;padding-bottom:8px;margin-bottom:10px;">'
            f'{svg_logo_lebaref(210)}'
            '<div style="text-align:right;font-size:10.5px;color:#475569;font-family:\'Segoe UI\',Arial,sans-serif;">'
            '<b style="color:#031e34;">LEBAREF SERVICIO DE MANTENIMIENTO GENERAL, S.A. DE C.V.</b><br>'
            'Mérida, Yucatán · corporativo@lebaref.com'
            '</div></div>'
        )
        frappe.db.sql("UPDATE `tabLetter Head` SET is_default = 0 WHERE name != %s", (nombre_lh,))
        valores_lh = {
            "letter_head_name": nombre_lh,
            "source": "HTML",
            "content": html_lh,
            "is_default": 1,
            "disabled": 0,
        }
        if frappe.db.exists("Letter Head", nombre_lh):
            lh = frappe.get_doc("Letter Head", nombre_lh)
            lh.update(valores_lh)
            lh.save(ignore_permissions=True)
        else:
            frappe.get_doc({"doctype": "Letter Head", **valores_lh}).insert(ignore_permissions=True)


def configurar_sidebar_y_dock_lebaref():
    """Unifica el Sidebar en español de Lebaref y el riel izquierdo (Dock) sin saltos entre módulos."""
    from frappe.modules.import_file import import_file_by_path

    rutas_json = (
        frappe.get_app_path("lebaref_compras", "lebaref_compras", "sidebar", "lebaref_compras", "lebaref_compras.json"),
        frappe.get_app_path("lebaref_compras", "dock", "lebaref_compras", "lebaref_compras.json"),
    )
    for ruta in rutas_json:
        if os.path.exists(ruta):
            import_file_by_path(ruta, force=True, ignore_version=True)

    if frappe.db.exists("DocType", "Dock"):
        existente = frappe.db.get_value("Dock", {"app": "erpnext", "user": "", "standard": 0}, "name")
        if existente:
            dock_sitio = frappe.get_doc("Dock", existente)
            dock_sitio.set("items", SITE_DOCK_ERPNEXT)
            dock_sitio.save(ignore_permissions=True)
        else:
            frappe.get_doc({
                "doctype": "Dock",
                "app": "erpnext",
                "user": "",
                "standard": 0,
                "items": SITE_DOCK_ERPNEXT,
            }).insert(ignore_permissions=True)

        try:
            from frappe.desk.doctype.dock.dock import drop_dock_caches
            drop_dock_caches(None)
        except Exception:
            pass


def configurar_workspace_lebaref():
    if not frappe.db.exists("Module Def", "Lebaref Compras"):
        return

    contenido = _construir_contenido_workspace()
    if frappe.db.exists("Workspace", "Lebaref"):
        ws = frappe.get_doc("Workspace", "Lebaref")
        ws.title = "Lebaref"
        ws.label = "Lebaref"
        ws.public = 1
        ws.is_hidden = 0
        ws.set("shortcuts", WORKSPACE_SHORTCUTS)
        ws.content = contenido
        ws.save(ignore_permissions=True)
    else:
        frappe.get_doc({
            "doctype": "Workspace",
            "name": "Lebaref",
            "label": "Lebaref",
            "title": "Lebaref",
            "module": "Lebaref Compras",
            "type": "Workspace",
            "public": 1,
            "shortcuts": WORKSPACE_SHORTCUTS,
            "content": contenido,
        }).insert(ignore_permissions=True)


def _asegurar_enlace_assets():
    public_dir = frappe.get_app_path("lebaref_compras", "public")
    js_src = os.path.join(public_dir, "js", "lebaref_desk.js")
    files_dir = frappe.get_site_path("public", "files")
    os.makedirs(files_dir, exist_ok=True)
    if os.path.isfile(js_src):
        shutil.copyfile(js_src, os.path.join(files_dir, "lebaref_desk.js"))
    bench_sites = os.path.abspath(frappe.get_site_path(".."))
    assets_dir = os.path.join(bench_sites, "assets")
    destino = os.path.join(assets_dir, "lebaref_compras")
    if os.path.isdir(public_dir) and os.path.isdir(assets_dir):
        try:
            if os.path.islink(destino):
                os.unlink(destino)
            shutil.copytree(public_dir, destino, dirs_exist_ok=True)
        except OSError:
            pass


def configurar_todo_desk():
    from lebaref_compras.impresion_carta import configurar_formatos_impresion
    _asegurar_enlace_assets()
    configurar_identidad_visual()
    configurar_plantillas_comerciales()
    configurar_workspace_lebaref()
    configurar_sidebar_y_dock_lebaref()
    configurar_formatos_impresion()
    frappe.clear_cache()


@frappe.whitelist(methods=["GET"])
def mapa_ubicaciones():
    """Agrupa herramientas individuales, maletines y existencias en una cuadrícula visual 3x2 de Zonas A–F (ALM-MAPA)."""
    herramientas = []
    if frappe.has_permission("Lebaref Herramienta", "read"):
        raw_her = frappe.get_list(
            "Lebaref Herramienta",
            fields=["name", "codigo", "articulo", "marca_modelo", "ubicacion", "estado", "responsable", "obra"],
            order_by="ubicacion asc, codigo asc",
            limit_page_length=1000,
        )
        for h in raw_her:
            h["zona"] = _clasificar_zona_herramienta(h.get("ubicacion"), h.get("articulo"), h.get("estado"), h.get("obra"))
            herramientas.append(h)

    bins = []
    if frappe.has_permission("Bin", "read"):
        raw_bins = frappe.get_list(
            "Bin",
            filters={"actual_qty": ["!=", 0]},
            fields=["item_code", "warehouse", "actual_qty", "stock_uom"],
            order_by="warehouse asc, item_code asc",
            limit_page_length=1000,
        )
        codigos_bin = [b.item_code for b in raw_bins if b.item_code]
        info_items = {}
        if codigos_bin and frappe.has_permission("Item", "read"):
            for it in frappe.get_list(
                "Item",
                filters={"name": ["in", codigos_bin]},
                fields=["name", "item_name", "item_group", "description"],
                limit_page_length=1000,
            ):
                desc = it.get("description") or ""
                anaquel = ""
                if "[Anaquel:" in desc:
                    anaquel = desc.split("[Anaquel:", 1)[1].split("]", 1)[0].strip()
                info_items[it.name] = {
                    "item_name": it.get("item_name") or it.name,
                    "item_group": it.get("item_group") or "",
                    "anaquel": anaquel,
                }
        for b in raw_bins:
            meta_it = info_items.get(b.get("item_code"), {})
            anaquel = meta_it.get("anaquel", "")
            b["item_name"] = meta_it.get("item_name") or b.get("item_code")
            b["anaquel"] = anaquel
            b["zona"] = _clasificar_zona_bin(b.get("warehouse"), b.get("item_code"), anaquel, meta_it.get("item_group", ""))
            bins.append(b)

    kits = []
    if frappe.has_permission("Lebaref Kit", "read"):
        kits = frappe.get_list(
            "Lebaref Kit",
            fields=["name", "codigo", "nombre", "ubicacion", "estado"],
            order_by="codigo asc",
            limit_page_length=500,
        )

    zonas = []
    for clave, titulo, subtitulo, color in ZONAS_ALMACEN:
        n_her = sum(1 for h in herramientas if h["zona"] == clave)
        n_bin = sum(1 for b in bins if b["zona"] == clave)
        n_kit = len(kits) if clave == "E" else 0
        zonas.append({
            "clave": clave,
            "titulo": titulo,
            "subtitulo": subtitulo,
            "color": color,
            "herramientas": n_her,
            "articulos_stock": n_bin,
            "kits": n_kit,
        })

    return {
        "sheets_url": SHEETS_URL,
        "zonas": zonas,
        "herramientas": herramientas,
        "existencias": bins,
        "kits": kits,
    }
