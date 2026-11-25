import frappe
from frappe.utils import add_days, today


TITLE = "Lebaref · Solicitud de prueba por partidas"
PROJECT = "Obra ficticia Lebaref"
ITEMS = (
    ("LB-DEMO-CABLE", "Cable eléctrico de prueba", "Eléctrico", 10),
    ("LB-DEMO-FILTRO", "Filtro de aire de prueba", "HVAC", 2),
)


def crear_solicitud():
    if frappe.local.site != "lebaref-pruebas.local":
        frappe.throw("El ejemplo se puede cargar únicamente en el sitio de pruebas de Lebaref.")

    companies = frappe.get_all("Company", pluck="name", limit=2)
    if len(companies) != 1:
        frappe.throw("Completa la configuración inicial con una sola empresa ficticia.")
    company = companies[0]
    warehouse = frappe.db.get_value("Warehouse", {"company": company, "is_group": 0})
    item_group = frappe.db.get_value("Item Group", {"is_group": 0})
    uom_base = "Pieza" if frappe.db.exists("UOM", "Pieza") else ("Nos" if frappe.db.exists("UOM", "Nos") else None)
    if not warehouse or not item_group or not uom_base:
        frappe.throw("El sitio necesita un almacén, un grupo de artículos y la unidad Pieza/Nos.")

    request_name = frappe.db.get_value(
        "Material Request", {"company": company, "title": TITLE, "docstatus": 0}
    )
    if not request_name:
        project = frappe.db.get_value("Project", {"company": company, "project_name": PROJECT})
        if not project:
            project = frappe.get_doc(
                {"doctype": "Project", "company": company, "project_name": PROJECT}
            ).insert().name

        required_date = add_days(today(), 7)
        request = frappe.get_doc(
            {
                "doctype": "Material Request",
                "title": TITLE,
                "company": company,
                "material_request_type": "Purchase",
                "schedule_date": required_date,
            }
        )
        for code, name, group, quantity in ITEMS:
            if not frappe.db.exists("Item", code):
                frappe.get_doc(
                    {
                        "doctype": "Item",
                        "item_code": code,
                        "item_name": name,
                        "item_group": item_group,
                        "stock_uom": uom_base,
                        "is_stock_item": 1,
                    }
                ).insert()
            request.append(
                "items",
                {
                    "item_code": code,
                    "qty": quantity,
                    "uom": uom_base,
                    "conversion_factor": 1,
                    "schedule_date": required_date,
                    "warehouse": warehouse,
                    "project": project,
                    "custom_lebaref_partida": group,
                },
            )
        request_name = request.insert().name

    saved = frappe.get_doc("Material Request", request_name)
    expected = {code: group for code, _, group, _ in ITEMS}
    actual = {row.item_code: row.custom_lebaref_partida for row in saved.items}
    if actual != expected:
        frappe.throw("Las partidas guardadas difieren del ejemplo esperado.")
    return {"solicitud": saved.name, "partidas": actual}
