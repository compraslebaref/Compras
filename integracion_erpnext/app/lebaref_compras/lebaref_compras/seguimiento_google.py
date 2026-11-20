"""Replica snapshots de compras internas al receptor firmado de LEBAREF."""

from collections import Counter
from datetime import timedelta
from hashlib import sha256
from html import unescape
import hmac
import json
import math
import os
import re
import time
from urllib.parse import urlsplit

import frappe
import requests
from frappe.utils import now_datetime, strip_html

from lebaref_compras.comercial import CONDICION


QUEUE = "Lebaref Sync Solicitud"
AREAS = {"HVAC", "Obra Civil", "Eléctrico", "Corporativo", "Programación", "Comercializadora"}
LEGACY = re.compile(r"^Folio original SQLite: (SOL-\d{4}-\d{4,8})(?!\d)(?: \((\d{4}-\d{2}-\d{2}))?")
NATIVE = re.compile(r"MAT-MR-(\d{4})-(\d{1,7})")
DRIVE = re.compile(r"https://drive\.google\.com/\S{1,1975}")
MAX_SENDS = 5


def _text(value, limit=1000):
    return unescape(strip_html(str(value or ""))).strip()[:limit]


def _json(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False)


def _identity(doc):
    notes = _text(doc.get("custom_lebaref_notas"), 1600)
    match = LEGACY.match(notes)
    if match:
        return match[1], match[2] or str(doc.transaction_date)
    match = NATIVE.fullmatch(doc.name)
    if not match:
        raise ValueError("Serie de Material Request sin mapeo de folio")
    return f"SOL-{match[1]}-8{int(match[2]):07d}", str(doc.transaction_date)


def _quotes(doc):
    if not doc.items:
        return []
    links = frappe.get_all(
        "Supplier Quotation Item",
        filters={
            "parenttype": "Supplier Quotation",
            "material_request_item": ["in", [row.name for row in doc.items]],
        },
        pluck="parent",
        distinct=True,
        order_by="parent asc",
    )
    results = []
    source_rows = {item.name for item in doc.items}
    for name in links:
        quote = frappe.get_doc("Supplier Quotation", name)
        if quote.company != doc.company:
            continue
        matched = [row for row in quote.items if row.material_request_item in source_rows]
        if not matched:
            continue
        # El total con impuestos solo es atribuible si toda la oferta corresponde a esta solicitud.
        total = None
        if quote.currency == "MXN" and len(matched) == len(quote.items):
            amount = float(quote.grand_total or 0)
            total = amount if math.isfinite(amount) and amount >= 0 else None
        lead = max((row.lead_time_days or 0 for row in matched), default=0)
        url = frappe.db.get_value("File", {
            "attached_to_doctype": "Supplier Quotation",
            "attached_to_name": quote.name,
            "file_url": ["like", "https://drive.google.com/%"],
        }, "file_url", order_by="creation asc, name asc") or ""
        results.append([
            quote.name,
            {0: "Borrador", 1: "Recibida", 2: "Cancelada"}[quote.docstatus],
            total,
            url if DRIVE.fullmatch(url) else "",
            _text(quote.supplier_name or quote.supplier),
            str(quote.valid_till or ""),
            f"{lead:g} días" if lead else "",
            _text(quote.get(CONDICION)),
        ])
    return results


def _phase(doc, quotes):
    if doc.docstatus == 2 or doc.status == "Cancelled":
        return "Cancelada", ""
    if doc.status == "Stopped":
        return "Detenida", "Revisar solicitud con Compras"
    if doc.status in ("Received", "Transferred", "Issued"):
        return "Surtida", ""
    if doc.status in ("Ordered", "Partially Ordered"):
        return "En compra", "Dar seguimiento a la orden y recepción"
    if any(quote[1] == "Recibida" for quote in quotes):
        return "En comparativa", "Revisar cotizaciones recibidas"
    return "Pendiente de cotizar", "Solicitar o recabar precios de proveedores"


def _snapshot(doc, area):
    folio, date = _identity(doc)
    quotes = _quotes(doc)
    status, next_step = _phase(doc, quotes)
    projects = sorted({row.project for row in doc.items if row.project})
    project_names = [frappe.db.get_value("Project", name, "project_name") or name for name in projects]
    return {
        "id": folio, "area": area, "date": date,
        "project": _text(" / ".join(project_names)),
        "owner": _text(doc.get("custom_lebaref_solicitante")),
        "status": status, "next": next_step, "due": str(doc.schedule_date or ""),
        "items": [[
            _text(row.get("custom_lebaref_concepto") or row.item_name or row.description or row.item_code, 1500),
            str(row.qty), _text(row.uom, 1500),
            "Cancelada" if doc.docstatus == 2 else (
                "Ordenada" if row.ordered_qty >= row.stock_qty else (
                    "Ordenada parcialmente" if row.ordered_qty > 0 else "Pendiente"
                )
            ),
        ] for row in doc.items],
        "quotes": quotes,
        "history": [[str(doc.creation.date()), "Alta del registro en ERPNext"]],
    }


def _documents(company):
    filters = {"company": company, "material_request_type": "Purchase", "docstatus": ["in", [1, 2]]}
    return frappe.get_all("Material Request", filters=filters, fields=["name", "custom_lebaref_departamento"], order_by="name asc")


def _settings():
    config = frappe.conf.get("lebaref_seguimiento") or {}
    company = config.get("empresa")
    mapping = config.get("gerencias") or {}
    if not isinstance(company, str) or not company or not isinstance(mapping, dict) or not mapping:
        raise ValueError("Configura empresa y mapeo explícito de departamentos")
    if any(not dept or not isinstance(area, str) or area not in AREAS for dept, area in mapping.items()):
        raise ValueError("El mapeo de gerencias contiene una entrada inválida")
    folios = config.get("solo_solicitudes")
    if folios is not None and (not isinstance(folios, list) or not folios or any(not isinstance(x, str) or not x for x in folios)):
        raise ValueError("solo_solicitudes debe contener nombres de Material Request")
    excluded = config.get("excluir_solicitudes", [])
    if not isinstance(excluded, list) or any(not isinstance(x, str) or not x for x in excluded):
        raise ValueError("excluir_solicitudes debe contener nombres de Material Request")
    return company, mapping, folios, set(excluded)


def vista_previa(empresa, gerencias=None):
    """Informe local solo para bench. Lee ERPNext y omite el contenido de los snapshots."""
    mapping = gerencias or {}
    result = []
    for row in _documents(empresa):
        doc = frappe.get_doc("Material Request", row.name)
        area = mapping.get(row.custom_lebaref_departamento)
        try:
            folio, _ = _identity(doc)
        except ValueError:
            folio = None
        result.append({
            "solicitud": doc.name, "folio": folio, "departamento": row.custom_lebaref_departamento,
            "gerencia": area, "cotizaciones": len(_snapshot(doc, area)["quotes"]) if folio else None,
        })
    return result


def _stage(doc, area):
    payload = _snapshot(doc, area)
    fingerprint = sha256(_json(payload).encode()).hexdigest()
    name = frappe.db.get_value(QUEUE, {"solicitud": doc.name}, "name")
    state = frappe.get_doc(QUEUE, name) if name else frappe.new_doc(QUEUE)
    if state.huella == fingerprint:
        return
    if name and (state.folio != payload["id"] or state.gerencia != area):
        raise ValueError("El cambio de folio o gerencia requiere migrar el expediente")
    version = (state.version or 0) + 1
    body = _json(dict(payload, version=version))
    if len(body.encode("utf-16-le")) // 2 > 40000 or max(len(payload["items"]), len(payload["quotes"])) > 300:
        raise ValueError("El snapshot excede los límites del receptor")
    state.update({
        "solicitud": doc.name, "folio": payload["id"], "gerencia": area, "huella": fingerprint,
        "version": version, "snapshot": body, "pendiente": 1, "intentos": 0,
        "siguiente_intento": now_datetime(), "ultimo_error": "",
    })
    state.save(ignore_permissions=True)


def _transport(url, secret, body, post=None, get=None):
    timestamp = int(time.time())
    signature = hmac.new(secret.encode(), f"{timestamp}.{body}".encode(), sha256).hexdigest()
    response = (post or requests.post)(
        url, data=_json({"timestamp": timestamp, "payload": body, "signature": signature}).encode(),
        headers={"Content-Type": "application/json"},
        allow_redirects=False, timeout=(4, 15),
    )
    if response.status_code in (302, 303):
        location = response.headers.get("Location", "")
        target = urlsplit(location)
        if target.scheme != "https" or target.netloc != "script.googleusercontent.com" or target.path != "/macros/echo":
            raise ValueError("Redirección inesperada del receptor")
        response = (get or requests.get)(location, allow_redirects=False, timeout=(4, 15))
    if response.status_code != 200:
        raise ValueError(f"HTTP {response.status_code} del receptor")
    return response.json()


def _deliver(state, url, secret):
    try:
        reply = _transport(url, secret, state.snapshot)
        if not isinstance(reply, dict) or reply.get("ok") is not True:
            raise ValueError("Apps Script rechazó el snapshot; revisar Ejecuciones")
        if reply.get("folio") != state.folio or reply.get("version") != state.version:
            raise ValueError("Confirmación de folio o versión distinta; revisar _Datos")
        if reply.get("result") not in ("updated", "already_current"):
            raise ValueError("Confirmación desconocida de Apps Script")
    except requests.RequestException:
        error = "Error de conexión o respuesta de Apps Script"
    except ValueError as exc:
        error = str(exc)
    else:
        updates = {"pendiente": 0, "ultimo_error": "", "entregado": now_datetime()}
        folder = reply.get("folder_url")
        if isinstance(folder, str) and DRIVE.fullmatch(folder):
            updates["expediente"] = folder
        frappe.db.set_value(QUEUE, state.name, updates)
        frappe.db.commit()
        return
    attempts = (state.intentos or 0) + 1
    minutes = min(5 * 2 ** min(attempts - 1, 6), 240)
    frappe.db.set_value(QUEUE, state.name, {
        "intentos": attempts, "ultimo_error": error,
        "siguiente_intento": now_datetime() + timedelta(minutes=minutes),
    })
    frappe.db.commit()


def sincronizar():
    """Reconcilia documentos confirmados y entrega snapshots persistidos desde el worker."""
    if not (frappe.conf.get("lebaref_seguimiento") or {}).get("habilitado"):
        return
    company, mapping, folios, excluded = _settings()
    url = os.environ.get("LEBAREF_SYNC_URL") or frappe.conf.get("lebaref_sync_url") or ""
    secret = os.environ.get("LEBAREF_SYNC_SECRET") or frappe.conf.get("lebaref_sync_secret") or ""
    if not re.fullmatch(r"https://script\.google\.com/macros/s/[A-Za-z0-9_-]+/exec", url) or len(secret) < 32:
        raise ValueError("Falta configurar URL /exec y secreto de Apps Script")
    with frappe.cache.lock("lebaref_seguimiento_google", timeout=720, blocking_timeout=0):
        candidates = []
        for row in _documents(company):
            area = mapping.get(row.custom_lebaref_departamento)
            if not area or row.name in excluded:
                continue
            doc = frappe.get_doc("Material Request", row.name)
            try:
                folio, _ = _identity(doc)
            except ValueError:
                frappe.logger("lebaref_seguimiento").warning("%s: serie sin folio compatible", row.name)
                continue
            candidates.append((doc, area, folio))
        folio_counts = Counter(folio for _, _, folio in candidates)
        eligible = []
        for doc, area, folio in candidates:
            if folio_counts[folio] > 1:
                frappe.logger("lebaref_seguimiento").warning("%s: folio duplicado %s", doc.name, folio)
                continue
            if folios and doc.name not in folios:
                continue
            try:
                _stage(doc, area)
                frappe.db.commit()
                eligible.append(doc.name)
            except (ValueError, frappe.DuplicateEntryError, frappe.UniqueValidationError) as exc:
                frappe.db.rollback()
                frappe.logger("lebaref_seguimiento").warning("%s: %s", doc.name, type(exc).__name__)
        if not eligible:
            return
        pending = frappe.get_all(
            QUEUE, filters={"pendiente": 1, "siguiente_intento": ["<=", now_datetime()], "solicitud": ["in", eligible]},
            fields=["name", "folio", "version", "snapshot", "intentos"],
            order_by="siguiente_intento asc, name asc", limit=MAX_SENDS,
        )
        for state in pending:
            _deliver(state, url, secret)
