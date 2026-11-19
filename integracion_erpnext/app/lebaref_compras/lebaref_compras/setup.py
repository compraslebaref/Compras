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
            },
            {
                "fieldname": "custom_lebaref_concepto",
                "label": "Concepto libre",
                "fieldtype": "Small Text",
                "insert_after": "custom_lebaref_partida",
                "read_only": 1,
            },
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
    for doctype in ("Material Request", "Request for Quotation", "Supplier Quotation", "Purchase Receipt", "Stock Entry", "Item", "Supplier", "Project", "Warehouse"):
        fields[doctype] = [
            {"fieldname": "custom_lebaref_operacion", "label": "Operación Windows", "fieldtype": "Data", "unique": 1, "read_only": 1, "no_copy": 1},
            {"fieldname": "custom_lebaref_huella", "label": "Huella de operación", "fieldtype": "Data", "read_only": 1, "no_copy": 1},
        ]
    fields["Material Request Item"].append({
        "fieldname": "custom_lebaref_renglon_origen", "label": "Referencia del renglón enmendado",
        "fieldtype": "Data", "read_only": 1, "no_copy": 1, "insert_after": "custom_lebaref_concepto",
    })
    fields["Material Request"].extend([
        {"fieldname": "custom_lebaref_solicitud_origen", "label": "Solicitud de origen del anexo",
         "fieldtype": "Link", "options": "Material Request", "read_only": 1, "insert_after": "amended_from"},
        {"fieldname": "custom_lebaref_departamento", "label": "Departamento", "fieldtype": "Link", "options": "Department"},
        {"fieldname": "custom_lebaref_solicitante", "label": "Solicitante", "fieldtype": "Data"},
        {"fieldname": "custom_lebaref_notas", "label": "Notas", "fieldtype": "Small Text"},
    ])
    fields["Supplier Quotation"].append({
        "fieldname": "custom_lebaref_condicion_pago", "label": "Condición de pago",
        "fieldtype": "Link", "options": "Payment Terms Template", "insert_after": "valid_till",
    })
    for doctype, anchor in (("Supplier Quotation", "custom_lebaref_condicion_pago"), ("Purchase Order", "payment_terms_template")):
        fields.setdefault(doctype, []).append({
            "fieldname": "custom_lebaref_fecha_programada_pago", "label": "Fecha programada de pago",
            "fieldtype": "Date", "insert_after": anchor,
        })
    fields["Stock Entry"].append({"fieldname": "custom_lebaref_origen", "label": "Entrega de origen", "fieldtype": "Link", "options": "Stock Entry", "read_only": 1, "no_copy": 1})
    fields["Stock Entry Detail"] = [
        {"fieldname": "custom_lebaref_partida", "label": "Partida de obra", "fieldtype": "Data"},
        {"fieldname": "custom_lebaref_renglon", "label": "Renglón de entrega", "fieldtype": "Data", "read_only": 1, "no_copy": 1},
    ]
    create_custom_fields(fields)
    from lebaref_compras.solicitudes import configurar as configurar_solicitudes
    configurar_solicitudes()
    configure_cfdi()
    from lebaref_compras.equipos_setup import configurar
    configurar()


def configure_cfdi():
    import frappe
    if frappe.db.exists("DocType", "Lebaref CFDI"):
        doc = frappe.get_doc("DocType", "Lebaref CFDI")
        for rule in doc.permissions:
            for key in ("write", "delete", "submit", "cancel", "amend", "import"):
                rule.set(key, 0)
            rule.create = int(rule.role in ("Purchase User", "Purchase Manager", "System Manager"))
        doc.save()
        return
    frappe.get_doc({
        "doctype": "DocType", "name": "Lebaref CFDI", "module": "Lebaref Compras", "custom": 1,
        "autoname": "field:uuid", "track_changes": 1,
        "fields": [
            {"fieldname": name, "label": label, "fieldtype": kind, **extra}
            for name, label, kind, extra in (
                ("uuid", "UUID", "Data", {"reqd": 1, "unique": 1}),
                ("recepcion", "Recepción", "Link", {"options": "Purchase Receipt", "reqd": 1}),
                ("proveedor", "Proveedor", "Link", {"options": "Supplier"}),
                ("rfc_emisor", "RFC emisor", "Data", {}),
                ("rfc_receptor", "RFC receptor", "Data", {}),
                ("moneda", "Moneda", "Data", {}),
                ("total", "Total XML", "Currency", {"options": "moneda"}),
                ("xml", "XML privado", "Attach", {}),
                ("huella", "SHA256", "Data", {"hidden": 1}),
            )
        ],
        "permissions": [
            {"role": role, "read": 1, "create": int(role != "Stock User"), "write": 0, "delete": 0, "submit": 0, "cancel": 0, "amend": 0, "import": 0}
            for role in ("Purchase User", "Purchase Manager", "Stock User", "System Manager")
        ],
    }).insert()
