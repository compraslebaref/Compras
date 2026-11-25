"""Suite transaccional de verificación de Fase 3 (EST-01, CAL-01, CAL-02, CAL-03). Revierte sus datos al finalizar."""
import json
from pathlib import Path
import uuid
import frappe
from frappe.utils import add_days, today
from lebaref_compras import comparativa, impresion_carta, operaciones, ux_lebaref


def verificar():
    if frappe.local.site != "lebaref-pruebas.local":
        raise RuntimeError("Solo se permite en el entorno de pruebas.")
    usuario_orig = frappe.session.user
    try:
        frappe.set_user(
            "oscar.gomez@lebaref.com"
            if frappe.db.exists("User", "oscar.gomez@lebaref.com")
            else "Administrator"
        )
        seed = frappe.get_doc("Material Request", "MAT-MR-2026-00001")
        company = seed.company
        item_base = seed.items[0]

        # 1. CAL-01: Smart Defaults de cabecera y de renglón
        defs = ux_lebaref.valores_por_defecto_lebaref()
        assert defs["company"] == company, "Smart Default de empresa incorrecto"
        assert defs["warehouse"], "Smart Default de almacén vacío"
        assert defs["currency"] == "MXN", "Smart Default de moneda debe ser MXN"
        assert defs["taxes_and_charges_iva16"], "Smart Default de plantilla IVA 16% vacío"
        assert defs["default_uom"] == "Pieza", "Smart Default de unidad debe ser Pieza"
        assert defs["schedule_date_7d"] == add_days(today(), 7)
        assert defs["valid_till_30d"] == add_days(today(), 30)

        info_libre = ux_lebaref.info_rapida_item_default("LEBAREF-CONCEPTO-LIBRE")
        assert info_libre.get("requiere_almacen") == 0, "Concepto libre no debe forzar almacén"
        info_stock = ux_lebaref.info_rapida_item_default(item_base.item_code)
        assert info_stock.get("uom"), "Debe sugerir unidad por artículo"

        # 2. EST-01: Ocultamiento de columna horizontal Partida en tablas hijas
        for dt_hijo in (
            "Material Request Item",
            "Purchase Order Item",
            "Supplier Quotation Item",
            "Purchase Receipt Item",
            "Purchase Invoice Item",
            "Stock Entry Detail",
        ):
            val_lv = frappe.db.get_value(
                "Property Setter",
                {"doc_type": dt_hijo, "field_name": "custom_lebaref_partida", "property": "in_list_view"},
                "value",
            )
            assert str(val_lv) == "0", f"{dt_hijo}.custom_lebaref_partida no debe ocupar columna horizontal (valor={val_lv})"

        # 3. EST-01: Agrupación en bloques separadores por Partida (con entidades HTML decodificadas)
        sup_group = frappe.db.get_value("Supplier Group", {"is_group": 0})
        sup = frappe.get_doc({
            "doctype": "Supplier",
            "supplier_name": "Proveedor Fase3 " + uuid.uuid4().hex[:6],
            "supplier_group": sup_group,
            "supplier_type": "Company",
            "tax_id": "FTB010101AAA",
        }).insert(ignore_permissions=True)

        p1_nom = "PARTIDA 01: EQUIPOS DE AIRE ACONDICIONADO"
        p2_nom = "PARTIDA 02: TUBERÍA DE INTERCONEXIÓN"
        mr_folio = operaciones.ejecutar(
            "solicitud",
            {
                "company": company,
                "schedule_date": add_days(today(), 7),
                "title": "Solicitud Fase 3 Bloques",
                "rows": [
                    {
                        "concepto_libre": "Unidad Fan &amp; Coil 4.00 T.R.",
                        "qty": "2",
                        "uom": "Pieza",
                        "project": item_base.project,
                        "custom_lebaref_partida": p1_nom,
                    },
                    {
                        "concepto_libre": "Tubería de cobre ACR diámetro 1/4&quot;",
                        "qty": "15",
                        "uom": "Pieza",
                        "project": item_base.project,
                        "custom_lebaref_partida": p2_nom,
                    },
                ],
            },
            str(uuid.uuid4()),
        )["folio"]
        mr = frappe.get_doc("Material Request", mr_folio)

        bloques = impresion_carta.agrupar_por_partida(mr.items)
        assert len(bloques) == 2, f"Se esperaban 2 bloques de partida, se obtuvieron {len(bloques)}"
        assert bloques[0]["partida"] == p1_nom and len(bloques[0]["items"]) == 1
        assert bloques[1]["partida"] == p2_nom and len(bloques[1]["items"]) == 1

        # Verificar que sin partidas no inyecta encabezado artificial
        mr_sin_p = [frappe._dict({"custom_lebaref_partida": "", "qty": 1})]
        bloques_sin_p = impresion_carta.agrupar_por_partida(mr_sin_p)
        assert len(bloques_sin_p) == 1 and bloques_sin_p[0]["partida"] == ""

        # Cotización directa y Orden de Compra sobre la solicitud multi-partida
        sq_res = ux_lebaref.registrar_cotizacion_directa(
            mr.name,
            json.dumps({
                "supplier": sup.name,
                "currency": "MXN",
                "taxes_and_charges": defs["taxes_and_charges_iva16"],
                "rows": [
                    {"mr_item": mr.items[0].name, "rate": 12500.0, "lead_time_days": 5},
                    {"mr_item": mr.items[1].name, "rate": 180.0, "lead_time_days": 2},
                ],
            }),
        )
        sq = frappe.get_doc("Supplier Quotation", sq_res["folio"])
        comp = comparativa.consultar(mr.name)
        seleccion = {r["id"]: r["opciones"][0]["id"] for r in comp["renglones"]}
        ordenes = comparativa.generar_ordenes(mr.name, json.dumps(seleccion))
        po = frappe.get_doc("Purchase Order", ordenes[0]["folio"])

        # Renderizar los 3 formatos Carta principales y comprobar separadores por bloque sin columna Partida
        for dt_print, doc_name, fmt_name in (
            ("Material Request", mr.name, "Lebaref Carta - Solicitud"),
            ("Supplier Quotation", sq.name, "Lebaref Carta - Cotización Proveedor"),
            ("Purchase Order", po.name, "Lebaref Carta - Orden de Compra"),
        ):
            html = frappe.get_print(dt_print, doc_name, fmt_name)
            assert 'class="leb-partida-sep"' in html, f"Falta fila separadora leb-partida-sep en {fmt_name}"
            assert p1_nom in html and p2_nom in html, f"Faltan encabezados de partida en {fmt_name}"
            assert "Obra / Partida" not in html, f"No debe existir columna horizontal Obra / Partida en {fmt_name}"

        # 4. CAL-02 y CAL-03: Verificar activos JS desplegados (Poka-Yoke, Skeletons y Debounce 220 ms)
        js_dir = Path(frappe.get_app_path("lebaref_compras", "public", "js"))
        desk_js = (js_dir / "lebaref_desk.js").read_text(encoding="utf-8")
        mr_js = (js_dir / "material_request.js").read_text(encoding="utf-8")
        for token in (
            "function lebDebounce(fn, wait = 220)",
            "function lebSkeletonHtml(",
            "function aplicarSmartDefaultsEnFormulario(",
            "function validarPokaYokeDocumentoCompras(",
            "function agruparFilasPorPartida(",
            "function renderizarBloquesPartidaEnFormulario(",
        ):
            assert token in desk_js, f"Falta {token} en lebaref_desk.js"
        for token in (
            "function agruparFilasPorPartidaLocal(",
            "function evaluarPokaYokePrecio(",
            "leb-partida-block-row",
        ):
            assert token in mr_js, f"Falta {token} en material_request.js"

        return {
            "correcto": True,
            "pruebas_fase3": [
                "EST-01 BloquesPartidaUI+Carta",
                "CAL-01 SmartDefaults",
                "CAL-02 PokaYokePrecios+USD+Expediente",
                "CAL-03 Skeletons+Debounce220ms",
            ],
        }
    finally:
        frappe.db.rollback()
        frappe.set_user(usuario_orig)
