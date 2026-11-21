"""Aprovisionamiento idempotente de cuentas operativas de oficina de Lebaref (OPS-03 / SEG-01):
Crea o actualiza las 5 cuentas nominales por puesto en español con separación estricta en servidor:
1. oscar.gomez@lebaref.com  -> Lebaref Coordinador Operativo (Autorización total)
2. direccion@lebaref.com    -> Lebaref Coordinador Operativo (Vo.Bo. OCs, Ajustes y Pagos)
3. compras@lebaref.com      -> Lebaref Auxiliar Operativo Mixto (Compras + Almacén, SIN autorizar OCs/Ajustes/Pagos)
4. almacen@lebaref.com      -> Lebaref Auxiliar Operativo Mixto (Almacén + Compras, SIN autorizar OCs/Ajustes/Pagos)
5. finanzas@lebaref.com     -> Lebaref Finanzas y Contabilidad (CFDI, Facturas/Notas y Pagos en borrador, SIN autorizar)
"""
import json
import os
import secrets
import string

os.chdir("/home/frappe/frappe-bench/sites")
import frappe
from frappe.utils.password import update_password
from lebaref_compras.desk_ui import configurar_todo_desk

SITE = "lebaref-pruebas.local"
SALIDA_TMP = "/tmp/credenciales_oficina_lebaref.json"

CUENTAS_OFICINA = (
    ("oscar.gomez@lebaref.com", "Oscar", "Gómez (Coordinador)", "Lebaref Coordinador Operativo"),
    ("direccion@lebaref.com", "Dirección", "General", "Lebaref Coordinador Operativo"),
    ("compras@lebaref.com", "Auxiliar 1", "Compras y Almacén", "Lebaref Auxiliar Operativo Mixto"),
    ("almacen@lebaref.com", "Auxiliar 2", "Almacén y Compras", "Lebaref Auxiliar Operativo Mixto"),
    ("finanzas@lebaref.com", "Auxiliar", "Finanzas y Facturación", "Lebaref Finanzas y Contabilidad"),
)


def _generar_clave_legible() -> str:
    alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789"
    bloque = "".join(secrets.choice(alfabeto) for _ in range(6))
    digitos = "".join(secrets.choice("23456789") for _ in range(2))
    return f"Leb-{bloque}-{digitos}!"


def main():
    frappe.init(SITE)
    frappe.connect()
    frappe.set_user("Administrator")

    try:
        configurar_todo_desk()
        frappe.db.commit()

        credenciales_existentes = {}
        if os.path.isfile(SALIDA_TMP):
            try:
                with open(SALIDA_TMP, "r", encoding="utf-8") as fh:
                    for row in json.load(fh):
                        credenciales_existentes[row["email"]] = row["password"]
            except Exception:
                pass

        resultado_cuentas = []
        for email, nombre, apellido, perfil in CUENTAS_OFICINA:
            pwd = credenciales_existentes.get(email) or _generar_clave_legible()
            if not frappe.db.exists("User", email):
                u = frappe.get_doc({
                    "doctype": "User",
                    "email": email,
                    "first_name": nombre,
                    "last_name": apellido,
                    "language": "es",
                    "enabled": 1,
                    "send_welcome_email": 0,
                    "user_type": "System User",
                    "role_profile_name": perfil,
                    "role_profiles": [{"role_profile": perfil}],
                })
                u.insert(ignore_permissions=True)
            else:
                u = frappe.get_doc("User", email)
                u.language = "es"
                u.enabled = 1
                u.role_profile_name = perfil
                if u.meta.has_field("role_profiles"):
                    u.set("role_profiles", [{"role_profile": perfil}])
                u.populate_role_profile_roles()
                u.save(ignore_permissions=True)

            update_password(email, pwd)
            frappe.db.set_default("desktop:home_page", "lebaref", parent=email)
            resultado_cuentas.append({
                "email": email,
                "nombre": f"{u.first_name} {u.last_name or ''}".strip(),
                "perfil": perfil,
                "password": pwd,
            })

        frappe.db.commit()

        # Verificación estricta en servidor de permisos por cuenta (SEG-01)
        for email_aux in ("compras@lebaref.com", "almacen@lebaref.com"):
            frappe.set_user(email_aux)
            assert frappe.has_permission("Material Request", "create"), f"{email_aux} debe crear Solicitudes"
            assert frappe.has_permission("Supplier Quotation", "create"), f"{email_aux} debe capturar Cotizaciones"
            assert frappe.has_permission("Purchase Order", "create"), f"{email_aux} debe preparar OC en borrador"
            assert frappe.has_permission("Purchase Receipt", "create"), f"{email_aux} debe recibir en Almacén"
            assert frappe.has_permission("Stock Entry", "create"), f"{email_aux} debe entregar/consumir en Almacén"
            assert frappe.has_permission("Stock Reconciliation", "create"), f"{email_aux} debe levantar Conteo/Merma en borrador"
            # Restricciones duras: NO autoriza OC, Conteo/Merma, Factura ni Pago
            assert not frappe.has_permission("Purchase Order", "submit"), f"{email_aux} NO debe autorizar OCs"
            assert not frappe.has_permission("Stock Reconciliation", "submit"), f"{email_aux} NO debe autorizar Conteos/Mermas"
            assert not frappe.has_permission("Purchase Invoice", "submit"), f"{email_aux} NO debe autorizar Facturas"
            assert not frappe.has_permission("Payment Entry", "submit"), f"{email_aux} NO debe autorizar Pagos"

        frappe.set_user("finanzas@lebaref.com")
        assert frappe.has_permission("Purchase Invoice", "create"), "finanzas debe crear Facturas/Notas en borrador"
        assert frappe.has_permission("Payment Entry", "create"), "finanzas debe preparar Pagos en borrador"
        assert not frappe.has_permission("Purchase Order", "submit"), "finanzas NO debe autorizar OCs"
        assert not frappe.has_permission("Purchase Invoice", "submit"), "finanzas NO debe autorizar Facturas"
        assert not frappe.has_permission("Payment Entry", "submit"), "finanzas NO debe autorizar Pagos"

        for email_dir in ("oscar.gomez@lebaref.com", "direccion@lebaref.com"):
            frappe.set_user(email_dir)
            assert frappe.has_permission("Purchase Order", "submit"), f"{email_dir} debe autorizar OCs"
            assert frappe.has_permission("Stock Reconciliation", "submit"), f"{email_dir} debe autorizar Conteos/Mermas"
            assert frappe.has_permission("Purchase Invoice", "submit"), f"{email_dir} debe autorizar Facturas"
            assert frappe.has_permission("Payment Entry", "submit"), f"{email_dir} debe autorizar Pagos"

        frappe.set_user("Administrator")
        with open(SALIDA_TMP, "w", encoding="utf-8") as out:
            json.dump(resultado_cuentas, out, ensure_ascii=False, indent=2)

        print("USUARIOS_OFICINA_OK:" + json.dumps([
            {"email": c["email"], "nombre": c["nombre"], "perfil": c["perfil"]}
            for c in resultado_cuentas
        ], ensure_ascii=False))
    finally:
        frappe.destroy()


if __name__ == "__main__":
    main()
