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

doctype_js = {
    "Material Request": ["public/js/lebaref_desk.js", "public/js/material_request.js"],
    "Request for Quotation": "public/js/lebaref_desk.js",
    "Purchase Order": "public/js/lebaref_desk.js",
    "Purchase Receipt": "public/js/lebaref_desk.js",
    "Stock Entry": "public/js/lebaref_desk.js",
    "Supplier": "public/js/lebaref_desk.js",
    "Item": "public/js/lebaref_desk.js",
    "Lebaref Herramienta": "public/js/lebaref_desk.js",
    "Lebaref Mantenimiento Herramienta": "public/js/lebaref_desk.js",
    "Lebaref Kit": "public/js/lebaref_desk.js",
    "Lebaref Prestamo": "public/js/lebaref_desk.js",
    "Lebaref Vehiculo": "public/js/lebaref_desk.js",
}

doctype_list_js = {
    "Material Request": "public/js/lebaref_desk.js",
    "Purchase Order": "public/js/lebaref_desk.js",
    "Purchase Receipt": "public/js/lebaref_desk.js",
    "Lebaref CFDI": "public/js/lebaref_desk.js",
    "Stock Entry": "public/js/lebaref_desk.js",
    "Supplier": "public/js/lebaref_desk.js",
    "Item": "public/js/lebaref_desk.js",
    "Lebaref Herramienta": "public/js/lebaref_desk.js",
    "Lebaref Kit": "public/js/lebaref_desk.js",
    "Lebaref Prestamo": "public/js/lebaref_desk.js",
    "Lebaref Vehiculo": "public/js/lebaref_desk.js",
}

jinja = {
    "methods": [
        "lebaref_compras.impresion_carta.svg_code128",
        "lebaref_compras.impresion_carta.svg_logo_lebaref",
    ]
}

doc_events = {
    doctype: {"before_validate": "lebaref_compras.trazabilidad.copiar_obra_y_partida"}
    for doctype in (
        "Request for Quotation",
        "Supplier Quotation",
        "Purchase Order",
        "Purchase Receipt",
    )
}

doc_events["Purchase Order"]["before_validate"] = [
    "lebaref_compras.trazabilidad.copiar_obra_y_partida",
    "lebaref_compras.comercial.desde_cotizacion",
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
    "on_cancel": "lebaref_compras.sobrantes.liberar_referencia",
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
