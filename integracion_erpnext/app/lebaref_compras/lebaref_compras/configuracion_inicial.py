import frappe
from frappe.translate import set_default_language


SHORTCUTS = (
    ("Solicitudes de material", "Material Request"),
    ("Peticiones de cotización", "Request for Quotation"),
    ("Cotizaciones de proveedores", "Supplier Quotation"),
    ("Órdenes de compra", "Purchase Order"),
    ("Recepciones de compra", "Purchase Receipt"),
    ("Obras", "Project"),
)

PROFILES = {
    "Lebaref Auxiliar de Compras": "Purchase User",
    "Lebaref Auxiliar de Almacén": "Stock User",
}


def configurar():
    if frappe.local.site != "lebaref-pruebas.local":
        frappe.throw("Esta configuración inicial corresponde al sitio de pruebas de Lebaref.")

    settings = frappe.get_single("System Settings")
    if settings.language != "es":
        settings.language = "es"
        settings.save()
    set_default_language("es")
    if frappe.db.get_value("User", "Administrator", "language") != "es":
        frappe.get_doc("User", "Administrator").db_set("language", "es")

    for name, role in PROFILES.items():
        if not frappe.db.exists("Role Profile", name):
            frappe.get_doc(
                {"doctype": "Role Profile", "role_profile": name, "roles": [{"role": role}]}
            ).insert()

    if not frappe.db.exists("Workspace", "Lebaref"):
        frappe.get_doc(
            {
                "doctype": "Workspace",
                "label": "Lebaref",
                "title": "Lebaref",
                "module": "Lebaref Compras",
                "type": "Workspace",
                "public": 1,
                "shortcuts": [
                    {"label": label, "type": "DocType", "link_to": doctype, "doc_view": "List"}
                    for label, doctype in SHORTCUTS
                ],
                "content": frappe.as_json(
                    [
                        {
                            "id": f"lebaref-{index}",
                            "type": "shortcut",
                            "data": {"shortcut_name": label, "col": 4},
                        }
                        for index, (label, _) in enumerate(SHORTCUTS)
                    ]
                ),
            }
        ).insert()
    frappe.clear_cache()
    return {"idioma": settings.language, "espacio": "Lebaref", "perfiles": list(PROFILES)}
