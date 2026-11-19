"""Configuración visual del Workspace Lebaref y vistas operativas en Frappe Desk."""
import json
import frappe

SHEETS_URL = "https://docs.google.com/spreadsheets/d/1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I/edit?gid=0#gid=0"

WORKSPACE_SHORTCUTS = [
    # Ciclo de Compras
    {"label": "Solicitudes de material", "type": "DocType", "link_to": "Material Request", "doc_view": "List", "color": "Blue"},
    {"label": "Peticiones de cotización", "type": "DocType", "link_to": "Request for Quotation", "doc_view": "List", "color": "Cyan"},
    {"label": "Cotizaciones de proveedores", "type": "DocType", "link_to": "Supplier Quotation", "doc_view": "List", "color": "Orange"},
    {"label": "Órdenes de compra", "type": "DocType", "link_to": "Purchase Order", "doc_view": "List", "color": "Green"},
    {"label": "CFDI 4.0 archivados", "type": "DocType", "link_to": "Lebaref CFDI", "doc_view": "List", "color": "Grey"},
    {"label": "¿A quién cotizar? (Sheets)", "type": "URL", "url": SHEETS_URL, "color": "Green"},
    # Almacén y Catálogos
    {"label": "Recepciones de compra", "type": "DocType", "link_to": "Purchase Receipt", "doc_view": "List", "color": "Green"},
    {"label": "Entregas y sobrantes", "type": "DocType", "link_to": "Stock Entry", "doc_view": "List", "color": "Blue"},
    {"label": "Artículos (Catálogo)", "type": "DocType", "link_to": "Item", "doc_view": "List", "color": "Grey"},
    {"label": "Proveedores", "type": "DocType", "link_to": "Supplier", "doc_view": "List", "color": "Grey"},
    {"label": "Obras / Proyectos", "type": "DocType", "link_to": "Project", "doc_view": "List", "color": "Grey"},
    {"label": "Existencias por almacén", "type": "DocType", "link_to": "Bin", "doc_view": "List", "color": "Cyan"},
    # Herramientas, Kits y Flotilla
    {"label": "Herramientas (HER)", "type": "DocType", "link_to": "Lebaref Herramienta", "doc_view": "List", "color": "Orange"},
    {"label": "Préstamos (PRE)", "type": "DocType", "link_to": "Lebaref Prestamo", "doc_view": "List", "color": "Yellow"},
    {"label": "Maletines / Kits (KIT)", "type": "DocType", "link_to": "Lebaref Kit", "doc_view": "List", "color": "Blue"},
    {"label": "Mantenimientos (HER)", "type": "DocType", "link_to": "Lebaref Mantenimiento Herramienta", "doc_view": "List", "color": "Grey"},
    {"label": "Flotilla vehicular (VEH)", "type": "DocType", "link_to": "Lebaref Vehiculo", "doc_view": "List", "color": "Blue"},
    {"label": "Gastos y combustible", "type": "DocType", "link_to": "Lebaref Gasto Vehiculo", "doc_view": "List", "color": "Orange"},
    {"label": "Check-list vehicular", "type": "DocType", "link_to": "Lebaref Revision Vehiculo", "doc_view": "List", "color": "Green"},
]


def _construir_contenido_workspace():
    bloques = [
        {
            "id": "hdr-compras",
            "type": "header",
            "data": {"text": "<span class=\"h4\"><b>1. Abastecimiento y Compras</b></span>", "col": 12},
        },
    ]
    grupos = [
        ("compras", ["Solicitudes de material", "Peticiones de cotización", "Cotizaciones de proveedores", "Órdenes de compra", "CFDI 4.0 archivados", "¿A quién cotizar? (Sheets)"]),
        ("almacen", ["Recepciones de compra", "Entregas y sobrantes", "Artículos (Catálogo)", "Proveedores", "Obras / Proyectos", "Existencias por almacén"]),
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
    import os
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
    configurar_workspace_lebaref()
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
