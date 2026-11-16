app_name = "lebaref_compras"
app_title = "Lebaref Compras"
app_publisher = "Lebaref"
app_description = "Procesos de compras y almacén de Lebaref para ERPNext"
required_apps = ["erpnext"]
after_install = "lebaref_compras.setup.configure_purchase_fields"
after_migrate = "lebaref_compras.setup.configure_purchase_fields"
