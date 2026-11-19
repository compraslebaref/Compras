"""Datos de contacto del proveedor en los documentos nativos Supplier y Contact."""
import frappe
from erpnext.selling.doctype.customer.customer import make_contact
from frappe.utils import validate_email_address


CAMPOS = (("telefono", "Teléfono"), ("contacto", "Contacto"), ("correo", "Correo"))


def configurar(supplier, data):
    values = {}
    for key, label in CAMPOS:
        value = data.get(key)
        if value is not None and not isinstance(value, str):
            frappe.throw(f"{label}: captura un texto válido.")
        values[key] = (value or "").strip()
    if any(values.values()):
        frappe.has_permission("Contact", "create", throw=True)
    email = values["correo"]
    if email and validate_email_address(email, throw=True) != email:
        frappe.throw("Captura una sola dirección de correo electrónico.")
    # Supplier.create_primary_contact uses first_name when it builds the Contact.
    supplier.first_name = values["contacto"]
    supplier.mobile_no = values["telefono"]
    supplier.email_id = email


def completar(supplier):
    if supplier.first_name and not supplier.supplier_primary_contact:
        contact = make_contact(supplier)
        supplier.db_set("supplier_primary_contact", contact.name)


def valores(supplier):
    contact_name = ""
    if supplier.supplier_primary_contact:
        contact = frappe.get_doc("Contact", supplier.supplier_primary_contact)
        if contact.has_permission("read") and contact.first_name:
            contact_name = contact.full_name
    return {"telefono": supplier.mobile_no or "", "contacto": contact_name, "correo": supplier.email_id or ""}
