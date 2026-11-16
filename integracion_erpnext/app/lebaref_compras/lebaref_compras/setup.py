from frappe.custom.doctype.custom_field.custom_field import create_custom_fields


def configure_purchase_fields():
    create_custom_fields(
        {
            "Material Request Item": [
                {
                    "fieldname": "custom_lebaref_partida",
                    "label": "Partida de obra",
                    "fieldtype": "Data",
                    "insert_after": "description",
                }
            ]
        }
    )
