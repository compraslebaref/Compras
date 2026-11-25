"""Aprovisionamiento idempotente de las cuentas nominales reales del equipo Lebaref (ONB-01 / SEG-01):
1. oscar.gomez@lebaref.com      -> Oscar Gómez (Gerente de Compras · Lebaref Coordinador Operativo: autorización total y motor experimental)
2. airan.echeverria@lebaref.com -> Airan Echeverría (Coordinador de Almacén · Lebaref Coordinador de Almacén: almacén, inventario, herramientas y catálogo)
3. juan.cetina@lebaref.com      -> Juan Cetina (Auxiliar de Compras · Lebaref Auxiliar Operativo Mixto: solicitudes, cotizaciones, OCs/XML/pagos en borrador, logística)
4. salvia.sosa@lebaref.com      -> Salvia Sosa (Auxiliar de Compras · Lebaref Auxiliar Operativo Mixto: solicitudes, cotizaciones, OCs/XML/pagos en borrador, logística)
5. direccion@lebaref.com        -> Dirección General (Lebaref Coordinador Operativo: respaldo de Vo.Bo.)
"""
import json
import os
import secrets

os.chdir("/home/frappe/frappe-bench/sites")
import frappe
from frappe.utils.password import update_password
from lebaref_compras.desk_ui import configurar_todo_desk

SITE = "lebaref-pruebas.local"
SALIDA_TMP = "/tmp/credenciales_oficina_lebaref.json"

CUENTAS_OFICINA = (
    ("oscar.gomez@lebaref.com", "Oscar", "Gómez", "Gerente de Compras", "Lebaref Coordinador Operativo"),
    ("airan.echeverria@lebaref.com", "Airan", "Echeverría", "Coordinador de Almacén", "Lebaref Coordinador de Almacén"),
    ("juan.cetina@lebaref.com", "Juan", "Cetina", "Auxiliar de Compras", "Lebaref Auxiliar Operativo Mixto"),
    ("salvia.sosa@lebaref.com", "Salvia", "Sosa", "Auxiliar de Compras", "Lebaref Auxiliar Operativo Mixto"),
    ("direccion@lebaref.com", "Dirección", "General", "Dirección General", "Lebaref Coordinador Operativo"),
)

CUENTAS_GENERICAS_OBSOLETAS = (
    "compras@lebaref.com",
    "almacen@lebaref.com",
    "finanzas@lebaref.com",
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

        # Deshabilitar cuentas genéricas anteriores (sin tocar @lebaref.test de pruebas automatizadas)
        for obsoleto in CUENTAS_GENERICAS_OBSOLETAS:
            if frappe.db.exists("User", obsoleto):
                frappe.db.set_value("User", obsoleto, "enabled", 0, update_modified=False)

        resultado_cuentas = []
        for email, nombre, apellido, puesto, perfil in CUENTAS_OFICINA:
            pwd = credenciales_existentes.get(email) or _generar_clave_legible()
            if not frappe.db.exists("User", email):
                u = frappe.get_doc({
                    "doctype": "User",
                    "email": email,
                    "first_name": nombre,
                    "last_name": apellido,
                    "bio": puesto,
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
                u.first_name = nombre
                u.last_name = apellido
                u.bio = puesto
                u.language = "es"
                u.enabled = 1
                u.role_profile_name = perfil
                if u.meta.has_field("role_profiles"):
                    u.set("role_profiles", [{"role_profile": perfil}])
                u.populate_role_profile_roles()
                u.save(ignore_permissions=True)

            update_password(email, pwd)
            if frappe.db.has_column("User", "default_workspace"):
                frappe.db.set_value("User", email, "default_workspace", "Lebaref", update_modified=False)
            frappe.db.set_default("desktop:home_page", "lebaref", parent=email)
            resultado_cuentas.append({
                "email": email,
                "nombre": f"{nombre} {apellido}".strip(),
                "puesto": puesto,
                "perfil": perfil,
                "password": pwd,
            })

        frappe.db.commit()

        # Verificación estricta en servidor de permisos por cuenta (SEG-01 / ONB-01)
        for email_aux in ("juan.cetina@lebaref.com", "salvia.sosa@lebaref.com"):
            frappe.set_user(email_aux)
            assert frappe.has_permission("Material Request", "create"), f"{email_aux} debe crear Solicitudes"
            assert frappe.has_permission("Supplier Quotation", "create"), f"{email_aux} debe capturar Cotizaciones"
            assert frappe.has_permission("Purchase Order", "create"), f"{email_aux} debe preparar OC en borrador"
            assert frappe.has_permission("Purchase Receipt", "create"), f"{email_aux} debe registrar Recepciones"
            assert frappe.has_permission("Stock Entry", "create"), f"{email_aux} debe registrar movimientos de Almacén"
            assert frappe.has_permission("Purchase Invoice", "create"), f"{email_aux} debe cargar XML / Facturas en borrador"
            assert frappe.has_permission("Payment Entry", "create"), f"{email_aux} debe preparar Pagos en borrador"
            assert not frappe.has_permission("Purchase Order", "submit"), f"{email_aux} NO debe autorizar OCs"
            assert not frappe.has_permission("Stock Reconciliation", "submit"), f"{email_aux} NO debe autorizar Conteos/Mermas"
            assert not frappe.has_permission("Purchase Invoice", "submit"), f"{email_aux} NO debe autorizar Facturas"
            assert not frappe.has_permission("Payment Entry", "submit"), f"{email_aux} NO debe autorizar Pagos"

        frappe.set_user("airan.echeverria@lebaref.com")
        assert frappe.has_permission("Purchase Receipt", "create"), "Airan debe registrar entradas de almacén"
        assert frappe.has_permission("Stock Entry", "create"), "Airan debe registrar salidas y resguardos"
        assert frappe.has_permission("Stock Reconciliation", "submit"), "Airan (Coordinador de Almacén) debe gestionar conteos de inventario"
        assert frappe.has_permission("Item", "create"), "Airan debe apoyar en catálogo de artículos"
        assert not frappe.has_permission("Purchase Order", "submit"), "Airan NO debe autorizar Órdenes de Compra"
        assert not frappe.has_permission("Purchase Invoice", "submit"), "Airan NO debe autorizar Facturas"
        assert not frappe.has_permission("Payment Entry", "submit"), "Airan NO debe autorizar Pagos"

        for email_dir in ("oscar.gomez@lebaref.com", "direccion@lebaref.com"):
            frappe.set_user(email_dir)
            assert frappe.has_permission("Purchase Order", "submit"), f"{email_dir} debe autorizar OCs"
            assert frappe.has_permission("Stock Reconciliation", "submit"), f"{email_dir} debe autorizar Conteos/Mermas"
            assert frappe.has_permission("Purchase Invoice", "submit"), f"{email_dir} debe autorizar Facturas"
            assert frappe.has_permission("Payment Entry", "submit"), f"{email_dir} debe autorizar Pagos"

        frappe.set_user("Administrator")
        with open(SALIDA_TMP, "w", encoding="utf-8") as out:
            json.dump(resultado_cuentas, out, ensure_ascii=False, indent=2)

        print("USUARIOS_OFICINA_OK:" + json.dumps(resultado_cuentas, ensure_ascii=False))
    finally:
        frappe.destroy()


if __name__ == "__main__":
    main()
