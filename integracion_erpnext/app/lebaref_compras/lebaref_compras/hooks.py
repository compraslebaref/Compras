app_name = "lebaref_compras"
app_title = "Lebaref Compras"
app_publisher = "Lebaref"
app_description = "Procesos de compras y almacén de Lebaref para ERPNext"
required_apps = ["erpnext"]
after_install = "lebaref_compras.setup.configure_purchase_fields"
after_migrate = "lebaref_compras.setup.configure_purchase_fields"

doc_events = {
    doctype: {"before_validate": "lebaref_compras.trazabilidad.copiar_obra_y_partida"}
    for doctype in (
        "Request for Quotation",
        "Supplier Quotation",
        "Purchase Order",
        "Purchase Receipt",
    )
}
