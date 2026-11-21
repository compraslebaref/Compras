"""Normaliza el folio de las Solicitudes migradas: MAT-MR-2026-000NN -> SOL-AAAA-#### (folio original de Lebaref).

- Idempotente: solo renombra las que conservan la nota 'Folio original SQLite: SOL-....' y siguen con nombre MAT-MR-*.
- Seguro: usa frappe.rename_doc (actualiza todos los vínculos de cotizaciones, órdenes, recepciones y anexos),
  compara conteos antes/después y revierte todo si algo no cuadra.
- Deja el contador de la serie SOL-AAAA-#### en el mayor folio existente para que la siguiente sea consecutiva.
"""
import json
import os
import re

os.chdir("/home/frappe/frappe-bench/sites")
import frappe

SITE = "lebaref-pruebas.local"
PATRON_NOTA = re.compile(r"Folio original SQLite:\s*(SOL-\d{4}-\d{4}(?:-A\d+)?)")
PATRON_FOLIO = re.compile(r"^SOL-(\d{4})-(\d{4})$")
PREFIJO_NOMBRE = re.compile(r"^SOL-\d{4}-\d{4}(?:-A\d+)?\s*[?\u00b7\-\u2013:\s]*")


def _nombre_limpio(nombre: str) -> str:
    rest = PREFIJO_NOMBRE.sub("", (nombre or "").strip()).strip()
    m = re.match(r"^(.*?)\s*\((.+)\)\s*$", rest)
    if m:
        rest = m.group(1).strip() or m.group(2).strip()
    return (rest or nombre or "").strip()[:140]


def _conteos():
    q = frappe.db.sql
    return {
        "solicitudes": q("SELECT COUNT(*) FROM `tabMaterial Request`")[0][0],
        "renglones": q("SELECT COUNT(*) FROM `tabMaterial Request Item`")[0][0],
        "cot_vinculadas": q("SELECT COUNT(*) FROM `tabSupplier Quotation Item` WHERE IFNULL(material_request,'')<>''")[0][0],
        "oc_vinculadas": q("SELECT COUNT(*) FROM `tabPurchase Order Item` WHERE IFNULL(material_request,'')<>''")[0][0],
        "rfq_vinculadas": q("SELECT COUNT(*) FROM `tabRequest for Quotation Item` WHERE IFNULL(material_request,'')<>''")[0][0],
        "huerfanos": q(
            "SELECT (SELECT COUNT(*) FROM `tabSupplier Quotation Item` x WHERE IFNULL(x.material_request,'')<>'' AND x.material_request NOT IN (SELECT name FROM `tabMaterial Request`))"
            " + (SELECT COUNT(*) FROM `tabPurchase Order Item` x WHERE IFNULL(x.material_request,'')<>'' AND x.material_request NOT IN (SELECT name FROM `tabMaterial Request`))"
            " + (SELECT COUNT(*) FROM `tabRequest for Quotation Item` x WHERE IFNULL(x.material_request,'')<>'' AND x.material_request NOT IN (SELECT name FROM `tabMaterial Request`))"
        )[0][0],
    }


def normalizar_folios_solicitud():
    antes = _conteos()
    renombradas = []
    omitidas = []

    filas = frappe.db.sql(
        "SELECT name, IFNULL(custom_lebaref_notas,'') AS notas FROM `tabMaterial Request` WHERE name LIKE 'MAT-MR-%%' ORDER BY name",
        as_dict=True,
    )
    for fila in filas:
        m = PATRON_NOTA.search(fila.notas or "")
        if not m:
            omitidas.append(fila.name)
            continue
        nuevo = m.group(1)
        if frappe.db.exists("Material Request", nuevo):
            omitidas.append(f"{fila.name} (ya existe {nuevo})")
            continue
        frappe.rename_doc("Material Request", fila.name, nuevo, force=True, show_alert=False, rebuild_search=False)
        renombradas.append((fila.name, nuevo))

    # Nombre descriptivo limpio (sin repetir el folio) para todas las solicitudes con folio SOL-*
    limpiadas = 0
    for fila in frappe.db.sql(
        "SELECT name, IFNULL(custom_lebaref_nombre_solicitud,'') AS nom FROM `tabMaterial Request` WHERE name LIKE 'SOL-%%'",
        as_dict=True,
    ):
        limpio = _nombre_limpio(fila.nom)
        if limpio and limpio != fila.nom:
            frappe.db.set_value(
                "Material Request", fila.name,
                {"custom_lebaref_nombre_solicitud": limpio, "title": limpio},
                update_modified=False,
            )
            limpiadas += 1

    # Serie oficial SOL-AAAA-#### continua tras el mayor folio existente
    maximos = {}
    for (nombre,) in frappe.db.sql("SELECT name FROM `tabMaterial Request` WHERE name LIKE 'SOL-%%'"):
        g = PATRON_FOLIO.match(nombre)
        if g:
            maximos[g.group(1)] = max(maximos.get(g.group(1), 0), int(g.group(2)))
    for anio, maximo in maximos.items():
        serie = f"SOL-{anio}-"
        actual = frappe.db.sql("SELECT `current` FROM `tabSeries` WHERE name=%s", (serie,))
        if not actual:
            frappe.db.sql("INSERT INTO `tabSeries` (name, `current`) VALUES (%s, %s)", (serie, maximo))
        elif int(actual[0][0] or 0) < maximo:
            frappe.db.sql("UPDATE `tabSeries` SET `current`=%s WHERE name=%s", (maximo, serie))

    despues = _conteos()
    for clave in ("solicitudes", "renglones", "cot_vinculadas", "oc_vinculadas", "rfq_vinculadas"):
        assert antes[clave] == despues[clave], f"Conteo distinto en {clave}: {antes[clave]} -> {despues[clave]}"
    assert despues["huerfanos"] == 0, f"Vinculos huerfanos tras renombrar: {despues['huerfanos']}"
    return {"renombradas": renombradas, "omitidas": omitidas, "nombres_limpiados": limpiadas, "series": maximos, "antes": antes, "despues": despues}


def main():
    frappe.init(SITE)
    frappe.connect()
    frappe.set_user("Administrator")
    try:
        from lebaref_compras.ux_lebaref import configurar_ux_lebaref
        configurar_ux_lebaref()
        res = normalizar_folios_solicitud()
        frappe.db.commit()
        frappe.clear_cache()
        print("FOLIOS_SOL_OK:" + json.dumps(res, ensure_ascii=False))
    except Exception:
        frappe.db.rollback()
        raise
    finally:
        frappe.destroy()


if __name__ == "__main__":
    main()
