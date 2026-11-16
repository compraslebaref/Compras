from frappe.custom.doctype.custom_field.custom_field import create_custom_fields


PURCHASE_ITEM_TYPES = (
    "Material Request Item",
    "Request for Quotation Item",
    "Supplier Quotation Item",
    "Purchase Order Item",
    "Purchase Receipt Item",
)


def configure_purchase_fields():
    fields = {
        doctype: [
            {
                "fieldname": "custom_lebaref_partida",
                "label": "Partida de obra",
                "fieldtype": "Data",
                "insert_after": "description",
            }
        ]
        for doctype in PURCHASE_ITEM_TYPES
    }
    fields["Request for Quotation Item"].append(
        {
            "fieldname": "custom_lebaref_obra",
            "label": "Obra",
            "fieldtype": "Link",
            "options": "Project",
            "insert_after": "custom_lebaref_partida",
        }
    )
    create_custom_fields(fields)
