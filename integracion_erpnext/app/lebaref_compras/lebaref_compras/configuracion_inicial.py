import frappe
from frappe.core.doctype.custom_docperm.custom_docperm import update_custom_docperm
from frappe.core.doctype.doctype.doctype import validate_permissions_for_doctype
from frappe.permissions import setup_custom_perms
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
    "Lebaref Auxiliar de Compras": ("Purchase User",),
    "Lebaref Auxiliar de Almacén": ("Stock User",),
    "Lebaref Responsable de Compras": ("Purchase User", "Purchase Manager"),
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

    for name, roles in PROFILES.items():
        if not frappe.db.exists("Role Profile", name):
            frappe.get_doc(
                {
                    "doctype": "Role Profile",
                    "role_profile": name,
                    "roles": [{"role": role} for role in roles],
                }
            ).insert()
        else:
            profile = frappe.get_doc("Role Profile", name)
            assigned = {row.role for row in profile.roles}
            missing = [role for role in roles if role not in assigned]
            if missing:
                for role in missing:
                    profile.append("roles", {"role": role})
                profile.save()

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
    _configurar_permisos_compras()
    frappe.clear_cache()
    return {"idioma": settings.language, "espacio": "Lebaref", "perfiles": list(PROFILES)}

def _configurar_permisos_compras():
    changes = {
        "Request for Quotation": {"submit": 1},
        "Purchase Order": {"submit": 0, "cancel": 0, "amend": 0},
        "Purchase Receipt": {
            "create": 0,
            "write": 0,
            "submit": 0,
            "cancel": 0,
            "amend": 0,
            "delete": 0,
            "import": 0,
        },
    }
    for doctype, rights in changes.items():
        setup_custom_perms(doctype)
        filters = {"parent": doctype, "role": "Purchase User", "permlevel": 0, "if_owner": 0}
        rule = frappe.db.get_value("Custom DocPerm", filters)
        if not rule:
            frappe.throw(f"No se encontró la regla base de Compras para {doctype}.")
        current = frappe.db.get_value("Custom DocPerm", rule, list(rights), as_dict=True)
        if any(current.get(key) != value for key, value in rights.items()):
            update_custom_docperm(rule, rights)
            validate_permissions_for_doctype(doctype)
