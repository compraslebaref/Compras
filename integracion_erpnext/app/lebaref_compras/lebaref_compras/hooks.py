app_name = "lebaref_compras"
app_title = "Lebaref Compras"
app_publisher = "Lebaref"
app_description = "Procesos de compras y almacén de Lebaref para ERPNext"
app_logo_url = "/files/lebaref_isotipo.svg"
required_apps = ["erpnext"]

add_to_apps_screen = [
    {
        "name": "lebaref_compras",
        "logo": "/files/lebaref_isotipo.svg",
        "title": "Lebaref",
        "route": "/app/lebaref",
    }
]

after_install = [
    "lebaref_compras.setup.configure_purchase_fields",
    "lebaref_compras.desk_ui.configurar_todo_desk",
]
after_migrate = [
    "lebaref_compras.setup.configure_purchase_fields",
    "lebaref_compras.desk_ui.configurar_todo_desk",
]

import hashlib
import os


def _calcular_version_assets() -> str:
    h = hashlib.sha256()
    base_js = os.path.join(os.path.dirname(__file__), "public", "js")
    for nombre in ("lebaref_desk.js", "material_request.js"):
        ruta = os.path.join(base_js, nombre)
        if os.path.isfile(ruta):
            with open(ruta, "rb") as fh:
                h.update(fh.read())
    return h.hexdigest()[:10]


_ASSET_VER = _calcular_version_assets()

app_include_js = [
    f"/files/lebaref_desk.js?v={_ASSET_VER}",
    f"/files/material_request.js?v={_ASSET_VER}",
]

doctype_js = {
    "Material Request": "public/js/lebaref_desk.js",
    "Request for Quotation": "public/js/lebaref_desk.js",
    "Supplier Quotation": "public/js/lebaref_desk.js",
    "Purchase Order": "public/js/lebaref_desk.js",
    "Purchase Receipt": "public/js/lebaref_desk.js",
    "Lebaref Sustitucion": "public/js/lebaref_desk.js",
    "Lebaref CFDI": "public/js/lebaref_desk.js",
    "Lebaref Conciliacion CFDI": "public/js/lebaref_desk.js",
    "Purchase Invoice": "public/js/lebaref_desk.js",
    "Payment Entry": "public/js/lebaref_desk.js",
    "Stock Entry": "public/js/lebaref_desk.js",
    "Stock Reconciliation": "public/js/lebaref_desk.js",
    "Supplier": "public/js/lebaref_desk.js",
    "Customer": "public/js/lebaref_desk.js",
    "Project": "public/js/lebaref_desk.js",
    "Department": "public/js/lebaref_desk.js",
    "Item": "public/js/lebaref_desk.js",
    "Bin": "public/js/lebaref_desk.js",
    "Lebaref Herramienta": "public/js/lebaref_desk.js",
    "Lebaref Mantenimiento Herramienta": "public/js/lebaref_desk.js",
    "Lebaref Kit": "public/js/lebaref_desk.js",
    "Lebaref Prestamo": "public/js/lebaref_desk.js",
    "Lebaref Vehiculo": "public/js/lebaref_desk.js",
}

doctype_list_js = {
    "Material Request": "public/js/lebaref_desk.js",
    "Supplier Quotation": "public/js/lebaref_desk.js",
    "Purchase Order": "public/js/lebaref_desk.js",
    "Purchase Receipt": "public/js/lebaref_desk.js",
    "Lebaref CFDI": "public/js/lebaref_desk.js",
    "Lebaref Conciliacion CFDI": "public/js/lebaref_desk.js",
    "Purchase Invoice": "public/js/lebaref_desk.js",
    "Payment Entry": "public/js/lebaref_desk.js",
    "Stock Entry": "public/js/lebaref_desk.js",
    "Stock Reconciliation": "public/js/lebaref_desk.js",
    "Supplier": "public/js/lebaref_desk.js",
    "Customer": "public/js/lebaref_desk.js",
    "Project": "public/js/lebaref_desk.js",
    "Department": "public/js/lebaref_desk.js",
    "Item": "public/js/lebaref_desk.js",
    "Bin": "public/js/lebaref_desk.js",
    "Lebaref Herramienta": "public/js/lebaref_desk.js",
    "Lebaref Kit": "public/js/lebaref_desk.js",
    "Lebaref Prestamo": "public/js/lebaref_desk.js",
    "Lebaref Vehiculo": "public/js/lebaref_desk.js",
}

jinja = {
    "methods": [
        "lebaref_compras.impresion_carta.svg_code128",
        "lebaref_compras.impresion_carta.svg_logo_lebaref",
        "lebaref_compras.impresion_carta.agrupar_por_partida",
    ]
}

doc_events = {
    doctype: {
        "before_validate": "lebaref_compras.trazabilidad.copiar_obra_y_partida",
        "before_save": "lebaref_compras.ux_lebaref.sincronizar_vistas_partidas",
        "on_submit": "lebaref_compras.ux_lebaref.sincronizar_estado_solicitudes_vinculadas",
        "on_cancel": "lebaref_compras.ux_lebaref.sincronizar_estado_solicitudes_vinculadas",
    }
    for doctype in (
        "Request for Quotation",
        "Supplier Quotation",
        "Purchase Order",
        "Purchase Receipt",
    )
}

doc_events["Material Request"] = {
    "before_save": "lebaref_compras.ux_lebaref.sincronizar_vistas_partidas",
    "on_submit": "lebaref_compras.ux_lebaref.sincronizar_estado_solicitud_hook",
    "on_cancel": "lebaref_compras.ux_lebaref.sincronizar_estado_solicitud_hook",
    "on_update_after_submit": "lebaref_compras.ux_lebaref.sincronizar_estado_solicitud_hook",
}
doc_events["Item"] = {
    "before_save": "lebaref_compras.ux_lebaref.sincronizar_item",
}

doc_events["Purchase Order"]["before_validate"] = [
    "lebaref_compras.trazabilidad.copiar_obra_y_partida",
    "lebaref_compras.comercial.desde_cotizacion",
]
doc_events["Supplier Quotation"]["on_submit"] = [
    "lebaref_compras.ux_lebaref.sincronizar_estado_solicitudes_vinculadas",
    "lebaref_compras.motor_precios.registrar_modo_sombra_cotizacion",
]
doc_events["Supplier Quotation"]["on_cancel"] = [
    "lebaref_compras.ux_lebaref.sincronizar_estado_solicitudes_vinculadas",
    "lebaref_compras.motor_precios.limpiar_modo_sombra_cotizacion",
]

doc_events["Lebaref CFDI"] = {"validate": "lebaref_compras.operaciones.validar_archivo"}
for name in ("Lebaref Herramienta", "Lebaref Prestamo", "Lebaref Retorno", "Lebaref Estado Herramienta", "Lebaref Vehiculo", "Lebaref Gasto Vehiculo", "Lebaref Revision Vehiculo", "Lebaref Estado Vehiculo"):
    doc_events[name] = {"validate": "lebaref_compras.equipos.proteger", "on_trash": "lebaref_compras.equipos.proteger"}
for name in ("Lebaref Kit", "Lebaref Composicion Kit", "Lebaref Garantia Herramienta", "Lebaref Mantenimiento Herramienta", "Lebaref Anulacion Mantenimiento"):
    doc_events[name] = {"validate": "lebaref_compras.equipos.proteger", "on_trash": "lebaref_compras.equipos.proteger"}


extend_doctype_class = {
    "Purchase Receipt": ["lebaref_compras.sustituciones.CompraConSustituciones"],
    "Purchase Invoice": ["lebaref_compras.sustituciones.CompraConSustituciones"],
}
doc_events["Lebaref Sustitucion"] = {
    "validate": "lebaref_compras.sustituciones.validar_autorizacion",
    "before_cancel": "lebaref_compras.sustituciones.anular_autorizacion",
}


doc_events["Stock Entry"] = {
    "validate": ["lebaref_compras.consumos.validar_movimiento", "lebaref_compras.sobrantes.validar"],
    "before_cancel": ["lebaref_compras.consumos.antes_de_cancelar", "lebaref_compras.sobrantes.autorizar"],
    "before_submit": "lebaref_compras.sobrantes.autorizar",
    "on_submit": "lebaref_compras.ux_lebaref.sincronizar_estado_solicitudes_vinculadas",
    "on_cancel": ["lebaref_compras.sobrantes.liberar_referencia", "lebaref_compras.ux_lebaref.sincronizar_estado_solicitudes_vinculadas"],
}


doc_events["Stock Reconciliation"] = {
    "validate": "lebaref_compras.ajustes.validar",
    "before_submit": "lebaref_compras.ajustes.autorizar",
    "before_cancel": "lebaref_compras.ajustes.cancelar",
    "on_cancel": "lebaref_compras.ajustes.liberar",
}


doc_events["Lebaref Conciliacion CFDI"] = {"validate":"lebaref_compras.comprobantes.validar_registro"}
doc_events["Purchase Invoice"] = {"before_validate":"lebaref_compras.finanzas.preparar_factura", "validate":"lebaref_compras.finanzas.validar_factura", "on_cancel":"lebaref_compras.finanzas.liberar_factura"}
doc_events["Payment Entry"] = {"before_submit":"lebaref_compras.finanzas.validar_pago"}

scheduler_events = {
    "cron": {"*/5 * * * *": ["lebaref_compras.seguimiento_google.sincronizar"]}
}
