"""Prueba de la cola local con respuestas simuladas. No usa red ni correo."""

from hashlib import sha256
import hmac
import json
from types import SimpleNamespace
from unittest.mock import patch
from uuid import uuid4

import frappe

from lebaref_compras import seguimiento_google as sync


def verificar():
    source = f"PRUEBA-SYNC-{uuid4().hex}"
    doc = SimpleNamespace(name=source)
    original = {"id": "SOL-2099-99999999", "area": "HVAC", "items": [], "quotes": [], "status": "En cotización"}
    secret = "secreto-de-verificacion-sin-red-000"
    signature_checks = []

    def post(url, **kwargs):
        envelope = json.loads(kwargs["data"])
        expected = hmac.new(secret.encode(), f"{envelope['timestamp']}.{envelope['payload']}".encode(), sha256).hexdigest()
        assert envelope["signature"] == expected
        signature_checks.append(envelope["payload"])
        return SimpleNamespace(status_code=302, headers={"Location": "https://script.googleusercontent.com/macros/echo?test=1"})

    def get(url, **kwargs):
        assert kwargs["allow_redirects"] is False
        ack = {"ok": True, "folio": original["id"], "version": 1, "result": "already_current"}
        return SimpleNamespace(status_code=200, json=lambda: ack)

    try:
        with patch.object(sync, "_snapshot", return_value=original.copy()) as snapshot, patch.object(frappe.db, "commit"):
            sync._stage(doc, "HVAC")
            state = frappe.get_doc(sync.QUEUE, source)
            assert state.version == 1 and state.pendiente
            first_body = state.snapshot
            with patch.object(sync.requests, "post", side_effect=sync.requests.ConnectionError):
                sync._deliver(state, "https://script.google.com/macros/s/test/exec", secret)
            state.reload()
            assert state.pendiente and state.intentos == 1 and state.siguiente_intento > sync.now_datetime()
            sync._stage(doc, "HVAC")
            state.reload()
            assert state.version == 1 and state.snapshot == first_body and state.intentos == 1
            with patch.object(sync.requests, "post", post), patch.object(sync.requests, "get", get):
                sync._deliver(state, "https://script.google.com/macros/s/test/exec", secret)
            state.reload()
            assert signature_checks == [first_body] and not state.pendiente and state.entregado
            snapshot.return_value = dict(original, status="En compra")
            sync._stage(doc, "HVAC")
            state.reload()
            assert state.version == 2 and state.pendiente and state.intentos == 0
            with patch.object(sync.requests, "post", post), patch.object(sync.requests, "get", get):
                sync._deliver(state, "https://script.google.com/macros/s/test/exec", secret)
            state.reload()
            assert state.pendiente and state.intentos == 1 and "versión" in state.ultimo_error
    finally:
        frappe.db.rollback()

    return {"firma": "ok", "reintento": "ok", "idempotencia": "ok", "version": "ok"}
