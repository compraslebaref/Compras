"""Configuración visual unificada de Lebaref en Frappe Desk 16 (Sidebar, Dock, Identidad Vectorial y Workspace)."""
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


def _construir_contenido_workspace():
    banner_html = (
        '<div style="display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px;'
        'padding:10px 16px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;margin-bottom:4px;">'
        '<img src="/files/lebaref_logo_horizontal.svg" alt="Lebaref" style="height:54px;width:auto;display:block;">'
        '<div style="text-align:right;font-size:12px;color:#475569;">'
        '<b style="color:#031e34;font-size:13px;">Plataforma Operativa de Compras, Almacén y Activos</b><br>'
        'Ingeniería · Construcción · HVAC · Electricidad · Remodelación'
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

    # Configurar la capa de sitio del Dock de ERPNext con Lebaref fijo en #1 y módulos ajenos ocultos
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
    assets_dir = os.path.join(frappe.get_site_path(".."), "assets")
    destino = os.path.join(assets_dir, "lebaref_compras")
    if os.path.isdir(public_dir) and os.path.isdir(assets_dir) and not os.path.exists(destino):
        try:
            os.symlink(public_dir, destino)
        except OSError:
            pass


def configurar_todo_desk():
    from lebaref_compras.impresion_carta import configurar_formatos_impresion
    _asegurar_enlace_assets()
    configurar_identidad_visual()
    configurar_workspace_lebaref()
    configurar_sidebar_y_dock_lebaref()
    configurar_formatos_impresion()
    frappe.clear_cache()


@frappe.whitelist(methods=["GET"])
def mapa_ubicaciones():
    """Agrupa herramientas individuales y existencias por ubicación / cuadrante para consulta rápida en Desk."""
    herramientas = []
    if frappe.has_permission("Lebaref Herramienta", "read"):
        herramientas = frappe.get_list(
            "Lebaref Herramienta",
            fields=["name", "codigo", "articulo", "marca_modelo", "ubicacion", "estado", "responsable", "obra"],
            order_by="ubicacion asc, codigo asc",
            limit_page_length=1000,
        )

    bins = []
    if frappe.has_permission("Bin", "read"):
        bins = frappe.get_list(
            "Bin",
            filters={"actual_qty": ["!=", 0]},
            fields=["item_code", "warehouse", "actual_qty", "stock_uom"],
            order_by="warehouse asc, item_code asc",
            limit_page_length=1000,
        )

    return {
        "sheets_url": SHEETS_URL,
        "herramientas": herramientas,
        "existencias": bins,
    }
