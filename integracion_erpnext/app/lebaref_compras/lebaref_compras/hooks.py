app_name = "lebaref_compras"
app_title = "Lebaref Compras"
app_publisher = "Lebaref"
app_description = "Procesos de compras y almacén de Lebaref para ERPNext"
required_apps = ["erpnext"]

after_install = [
    "lebaref_compras.setup.configure_purchase_fields",
    "lebaref_compras.desk_ui.configurar_todo_desk",
]
after_migrate = [
    "lebaref_compras.setup.configure_purchase_fields",
    "lebaref_compras.desk_ui.configurar_todo_desk",
]

app_include_js = "/assets/lebaref_compras/js/lebaref_desk.js"
doctype_js = {"Material Request": "public/js/material_request.js"}

jinja = {
    "methods": [
        "lebaref_compras.impresion_carta.svg_code128",
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

doc_events["Lebaref CFDI"] = {"validate": "lebaref_compras.operaciones.validar_archivo"}
for name in ("Lebaref Herramienta", "Lebaref Prestamo", "Lebaref Retorno", "Lebaref Estado Herramienta", "Lebaref Vehiculo", "Lebaref Gasto Vehiculo", "Lebaref Revision Vehiculo", "Lebaref Estado Vehiculo"):
    doc_events[name] = {"validate": "lebaref_compras.equipos.proteger", "on_trash": "lebaref_compras.equipos.proteger"}
for name in ("Lebaref Kit", "Lebaref Composicion Kit", "Lebaref Garantia Herramienta", "Lebaref Mantenimiento Herramienta", "Lebaref Anulacion Mantenimiento"):
    doc_events[name] = {"validate": "lebaref_compras.equipos.proteger", "on_trash": "lebaref_compras.equipos.proteger"}
