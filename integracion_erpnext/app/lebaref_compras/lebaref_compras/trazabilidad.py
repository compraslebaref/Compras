import frappe


# Order matters when ERPNext carries multiple references on the same row.
SOURCE_ROWS = {
    "Request for Quotation": (
        ("Material Request Item", "material_request_item", "material_request"),
    ),
    "Supplier Quotation": (
        ("Material Request Item", "material_request_item", "material_request"),
        ("Request for Quotation Item", "request_for_quotation_item", "request_for_quotation"),
    ),
    "Purchase Order": (
        ("Material Request Item", "material_request_item", "material_request"),
        ("Supplier Quotation Item", "supplier_quotation_item", "supplier_quotation"),
    ),
    "Purchase Receipt": (
        ("Purchase Order Item", "purchase_order_item", "purchase_order"),
        ("Material Request Item", "material_request_item", "material_request"),
    ),
}


def copiar_obra_y_partida(doc, method=None):
    target_project = "custom_lebaref_obra" if doc.doctype == "Request for Quotation" else "project"
    for item in doc.items:
        for doctype, row_field, parent_field in SOURCE_ROWS[doc.doctype]:
            row_name = item.get(row_field)
            if not row_name:
                continue
            project_field = "custom_lebaref_obra" if doctype == "Request for Quotation Item" else "project"
            source = frappe.db.get_value(
                doctype, row_name, ("parent", project_field, "custom_lebaref_partida", "custom_lebaref_concepto"),
                as_dict=True
            )
            if not source:
                continue
            expected_parent = item.get(parent_field) or doc.get(parent_field)
            if expected_parent and source.parent != expected_parent:
                frappe.throw(
                    f"Renglón {item.idx}: la referencia {row_name} no pertenece a {expected_parent}."
                )
            item.custom_lebaref_partida = source.custom_lebaref_partida
            if source.custom_lebaref_concepto:
                item.custom_lebaref_concepto = source.custom_lebaref_concepto
                item.description = source.custom_lebaref_concepto
            if source.get(project_field):
                item.set(target_project, source.get(project_field))
            break
