"""Fase 5 — Motor Experimental de Precio Estimado, Índice de Confianza, Modo Sombra y Serie Tiempo-Precio:
- Exclusivo para Oscar Gómez (oscar.gomez@lebaref.com) y Administrator. Invisible y bloqueado en servidor para auxiliares y almacén.
- PRE-01: Cálculo transparente de Precio Estimado en MXN (ponderado por frescura temporal y fuente real vs. cotización,
          resistente a valores atípicos, normalizado con TC DOF histórico y factor de unidad), Volatilidad/Tendencia
          e Índice de Confianza (0–100%) con semáforo editable y regla de 0% en 'Exige recotizar siempre'.
- PRE-02: Triaje gerencial de 1 clic en Solicitudes ('Validado por Gerencia — No recotizar', visible operativamente para
          auxiliares sin revelar jamás el precio estimado ni % de confianza) y Modo Sombra automático al confirmar nuevas
          cotizaciones reales ('Lebaref Evaluacion Sombra').
- PRE-03: Tablero general de confianza por artículo, auditoría de precisión del Modo Sombra y serie detallada
          Tiempo (X) vs. Precio Limpio MXN (Y) separando Compras Reales y Cotizaciones + tabla documental histórica.
"""
import json
import math
import statistics
from datetime import date

import frappe
from frappe.custom.doctype.custom_field.custom_field import create_custom_fields
from frappe.utils import cint, date_diff, flt, getdate, now_datetime, today

USUARIOS_AUTORIZADOS_MOTOR = frozenset({
    "oscar.gomez@lebaref.com",
    "Administrator",
})

ARTICULO_LIBRE = "LEBAREF-CONCEPTO-LIBRE"

DEFAULTS_CONFIG_MOTOR = {
    "umbral_alta": 80,
    "umbral_media": 50,
    "dias_vigencia_optima": 45,
    "dias_caducidad_maxima": 365,
    "peso_compra_real": 1.5,
    "peso_cotizacion": 1.0,
    "cv_maximo_estable": 12.0,
}


def es_usuario_motor_precios(user: str | None = None) -> bool:
    """Devuelve True exclusivamente para Oscar Gómez y Administrator."""
    usr = (user or frappe.session.user or "").strip()
    return usr in USUARIOS_AUTORIZADOS_MOTOR


def exigir_acceso_motor_precios():
    """Bloquea en servidor cualquier lectura o acción del motor experimental fuera de la cuenta de Oscar."""
    if not es_usuario_motor_precios():
        frappe.throw(
            "Acceso exclusivo para Gerencia de Compras.",
            frappe.PermissionError,
        )


def _asegurar_doctype_config_motor():
    permisos = [
        {
            "role": "System Manager",
            "read": 1,
            "write": 1,
            "create": 1,
        }
    ]
    campos_dt = [
        {
            "fieldname": "sec_umbrales",
            "label": "Semáforo de Índice de Confianza",
            "fieldtype": "Section Break",
        },
        {
            "fieldname": "umbral_alta",
            "label": "Umbral Confianza Alta (%)",
            "fieldtype": "Int",
            "default": 80,
            "reqd": 1,
        },
        {
            "fieldname": "umbral_media",
            "label": "Umbral Confianza Media (%)",
            "fieldtype": "Int",
            "default": 50,
            "reqd": 1,
        },
        {"fieldname": "cb_cfg_1", "fieldtype": "Column Break"},
        {
            "fieldname": "dias_vigencia_optima",
            "label": "Días de frescura óptima",
            "fieldtype": "Int",
            "default": 45,
            "reqd": 1,
        },
        {
            "fieldname": "dias_caducidad_maxima",
            "label": "Días de caducidad máxima",
            "fieldtype": "Int",
            "default": 365,
            "reqd": 1,
        },
        {
            "fieldname": "sec_pesos",
            "label": "Ponderación y Estabilidad",
            "fieldtype": "Section Break",
        },
        {
            "fieldname": "peso_compra_real",
            "label": "Peso de Compra Real (OC / Factura)",
            "fieldtype": "Float",
            "precision": "2",
            "default": 1.5,
            "reqd": 1,
        },
        {
            "fieldname": "peso_cotizacion",
            "label": "Peso de Cotización (ERP / Histórica)",
            "fieldtype": "Float",
            "precision": "2",
            "default": 1.0,
            "reqd": 1,
        },
        {"fieldname": "cb_cfg_2", "fieldtype": "Column Break"},
        {
            "fieldname": "cv_maximo_estable",
            "label": "Coeficiente de variación estable (%)",
            "fieldtype": "Float",
            "precision": "2",
            "default": 12.0,
            "reqd": 1,
        },
    ]

    nombre_dt = "Lebaref Configuracion Motor Precios"
    if frappe.db.exists("DocType", nombre_dt):
        dt = frappe.get_doc("DocType", nombre_dt)
        dt.set("permissions", permisos)
        existentes = {f.fieldname for f in dt.fields}
        for cf in campos_dt:
            if cf["fieldname"] not in existentes:
                dt.append("fields", cf)
        dt.save(ignore_permissions=True)
        return

    frappe.get_doc({
        "doctype": "DocType",
        "name": nombre_dt,
        "module": "Lebaref Compras",
        "custom": 1,
        "issingle": 1,
        "track_changes": 1,
        "fields": campos_dt,
        "permissions": permisos,
    }).insert(ignore_permissions=True)


def _asegurar_doctype_evaluacion_sombra():
    permisos = [
        {
            "role": "System Manager",
            "read": 1,
            "write": 1,
            "create": 1,
            "delete": 1,
            "report": 1,
            "export": 1,
        }
    ]
    campos_dt = [
        {"fieldname": "fecha_evento", "label": "Fecha de evaluación", "fieldtype": "Date", "reqd": 1, "in_list_view": 1},
        {"fieldname": "item_code", "label": "Artículo (SKU)", "fieldtype": "Link", "options": "Item", "reqd": 1, "in_list_view": 1},
        {"fieldname": "item_name", "label": "Nombre del artículo", "fieldtype": "Data", "read_only": 1},
        {"fieldname": "cotizacion", "label": "Cotización real", "fieldtype": "Link", "options": "Supplier Quotation", "reqd": 1, "in_list_view": 1},
        {"fieldname": "cotizacion_renglon", "label": "Renglón de cotización", "fieldtype": "Data", "read_only": 1, "hidden": 1},
        {"fieldname": "solicitud", "label": "Solicitud vinculada", "fieldtype": "Link", "options": "Material Request"},
        {"fieldname": "proveedor", "label": "Proveedor", "fieldtype": "Link", "options": "Supplier"},
        {"fieldname": "cb_sh_1", "fieldtype": "Column Break"},
        {"fieldname": "precio_estimado_previo_mxn", "label": "Estimado previo s/IVA (MXN)", "fieldtype": "Float", "precision": "4", "read_only": 1, "in_list_view": 1},
        {"fieldname": "confianza_previa_pct", "label": "Confianza previa (%)", "fieldtype": "Int", "read_only": 1, "in_list_view": 1},
        {"fieldname": "semaforo_previo", "label": "Nivel de confianza previo", "fieldtype": "Data", "read_only": 1},
        {"fieldname": "muestra_previa", "label": "Muestra previa (eventos)", "fieldtype": "Int", "read_only": 1},
        {"fieldname": "precio_cotizado_real_mxn", "label": "Cotización real s/IVA (MXN)", "fieldtype": "Float", "precision": "4", "read_only": 1, "in_list_view": 1},
        {"fieldname": "diferencia_mxn", "label": "Diferencia real vs. estimado (MXN)", "fieldtype": "Float", "precision": "4", "read_only": 1},
        {"fieldname": "error_absoluto_pct", "label": "Error porcentual absoluto (%)", "fieldtype": "Float", "precision": "2", "read_only": 1},
        {"fieldname": "precision_pct", "label": "Puntería del estimado (%)", "fieldtype": "Float", "precision": "2", "read_only": 1},
        {"fieldname": "acierto_dentro_10pct", "label": "Dentro de ±10%", "fieldtype": "Check", "read_only": 1},
    ]

    nombre_dt = "Lebaref Evaluacion Sombra"
    if frappe.db.exists("DocType", nombre_dt):
        dt = frappe.get_doc("DocType", nombre_dt)
        dt.set("permissions", permisos)
        existentes = {f.fieldname for f in dt.fields}
        for cf in campos_dt:
            if cf["fieldname"] not in existentes:
                dt.append("fields", cf)
        dt.save(ignore_permissions=True)
        return

    frappe.get_doc({
        "doctype": "DocType",
        "name": nombre_dt,
        "module": "Lebaref Compras",
        "custom": 1,
        "autoname": "format:SOMBRA-{#####}",
        "track_changes": 0,
        "fields": campos_dt,
        "permissions": permisos,
    }).insert(ignore_permissions=True)


def configurar_campos_fase5():
    """Instala de forma idempotente los campos de triaje gerencial y los DocTypes privados de Fase 5."""
    campos = {
        "Material Request Item": [
            {
                "fieldname": "custom_lebaref_no_recotizar",
                "label": "Validado por Gerencia — No recotizar",
                "fieldtype": "Check",
                "default": "0",
                "insert_after": "custom_lebaref_concepto",
                "allow_on_submit": 1,
                "read_only": 1,
                "no_copy": 1,
                "print_hide": 1,
            },
            {
                "fieldname": "custom_lebaref_validado_por",
                "label": "Validado por",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_no_recotizar",
                "allow_on_submit": 1,
                "read_only": 1,
                "no_copy": 1,
                "hidden": 1,
                "print_hide": 1,
                "report_hide": 1,
            },
            {
                "fieldname": "custom_lebaref_validado_fecha",
                "label": "Fecha validación gerencia",
                "fieldtype": "Datetime",
                "insert_after": "custom_lebaref_validado_por",
                "allow_on_submit": 1,
                "read_only": 1,
                "no_copy": 1,
                "hidden": 1,
                "print_hide": 1,
                "report_hide": 1,
            },
            {
                "fieldname": "custom_lebaref_precio_estimado_fijado",
                "label": "Estimado interno al validar",
                "fieldtype": "Float",
                "precision": "4",
                "insert_after": "custom_lebaref_validado_fecha",
                "allow_on_submit": 1,
                "read_only": 1,
                "no_copy": 1,
                "hidden": 1,
                "print_hide": 1,
                "report_hide": 1,
            },
            {
                "fieldname": "custom_lebaref_confianza_fijada",
                "label": "Confianza interna al validar",
                "fieldtype": "Int",
                "insert_after": "custom_lebaref_precio_estimado_fijado",
                "allow_on_submit": 1,
                "read_only": 1,
                "no_copy": 1,
                "hidden": 1,
                "print_hide": 1,
                "report_hide": 1,
            },
        ]
    }
    create_custom_fields(campos, update=True)
    _asegurar_doctype_config_motor()
    _asegurar_doctype_evaluacion_sombra()


def obtener_configuracion_motor() -> dict:
    """Obtiene los umbrales y pesos activos del motor con respaldos seguros."""
    cfg = dict(DEFAULTS_CONFIG_MOTOR)
    if frappe.db.exists("DocType", "Lebaref Configuracion Motor Precios"):
        for k, def_val in DEFAULTS_CONFIG_MOTOR.items():
            raw = frappe.db.get_single_value("Lebaref Configuracion Motor Precios", k)
            if raw is not None and raw != "":
                cfg[k] = cint(raw) if isinstance(def_val, int) else flt(raw)
    cfg["umbral_alta"] = max(55, min(99, cint(cfg["umbral_alta"] or 80)))
    cfg["umbral_media"] = max(20, min(cfg["umbral_alta"] - 5, cint(cfg["umbral_media"] or 50)))
    cfg["dias_vigencia_optima"] = max(7, cint(cfg["dias_vigencia_optima"] or 45))
    cfg["dias_caducidad_maxima"] = max(cfg["dias_vigencia_optima"] + 30, cint(cfg["dias_caducidad_maxima"] or 365))
    cfg["peso_compra_real"] = max(0.5, flt(cfg["peso_compra_real"] or 1.5))
    cfg["peso_cotizacion"] = max(0.2, flt(cfg["peso_cotizacion"] or 1.0))
    cfg["cv_maximo_estable"] = max(2.0, min(40.0, flt(cfg["cv_maximo_estable"] or 12.0)))
    return cfg


def _mapa_factores_uom(codigos: tuple[str, ...]) -> dict[tuple[str, str], float]:
    if not codigos:
        return {}
    factores = {}
    for r in frappe.db.sql(
        """
        SELECT parent, UPPER(TRIM(uom)) AS uom_key, conversion_factor
        FROM `tabUOM Conversion Detail`
        WHERE parent IN %s AND COALESCE(conversion_factor, 0) > 0
        """,
        (codigos,),
        as_dict=True,
    ):
        factores[(r.parent, r.uom_key)] = flt(r.conversion_factor) or 1.0
    return factores


def _recolectar_eventos_precios(
    codigos: tuple[str, ...],
    excluir_cotizacion: str = "",
    fecha_corte: date | None = None,
) -> dict[str, list[dict]]:
    """Reúne y normaliza a MXN por unidad base (stock_uom) las 4 fuentes reales de precios sin IVA."""
    por_item: dict[str, list[dict]] = {c: [] for c in codigos}
    if not codigos:
        return por_item

    factores_uom = _mapa_factores_uom(codigos)

    # 1. Compras Reales: Órdenes de Compra confirmadas (Purchase Order docstatus=1)
    tiene_po_tc = frappe.db.has_column("Purchase Order", "custom_lebaref_tc_dof")
    col_po_tc = "COALESCE(NULLIF(po.custom_lebaref_tc_dof, 0), NULLIF(po.conversion_rate, 0), 1.0)" if tiene_po_tc else "COALESCE(NULLIF(po.conversion_rate, 0), 1.0)"
    for r in frappe.db.sql(
        f"""
        SELECT
            poi.item_code,
            po.name AS folio,
            po.transaction_date AS fecha,
            COALESCE(po.supplier_name, po.supplier, '') AS proveedor,
            COALESCE(po.currency, 'MXN') AS moneda,
            {col_po_tc} AS tc_dof,
            poi.rate AS precio_origen,
            poi.base_net_rate,
            COALESCE(NULLIF(poi.conversion_factor, 0), 1.0) AS factor,
            COALESCE(poi.uom, poi.stock_uom, 'Pieza') AS uom,
            COALESCE(poi.project, '') AS obra
        FROM `tabPurchase Order Item` poi
        INNER JOIN `tabPurchase Order` po ON po.name = poi.parent
        WHERE po.docstatus = 1
          AND poi.item_code IN %s
          AND COALESCE(poi.rate, 0) > 0
        ORDER BY po.transaction_date ASC, po.creation ASC
        """,
        (codigos,),
        as_dict=True,
    ):
        f_ev = getdate(r.fecha) if r.fecha else getdate(today())
        if fecha_corte and f_ev > fecha_corte:
            continue
        tc = flt(r.tc_dof or 1.0, 4) if r.moneda == "USD" else 1.0
        bruto_mxn = (flt(r.precio_origen) * tc) if r.moneda == "USD" else flt(r.base_net_rate or r.precio_origen)
        precio_mxn = bruto_mxn / (flt(r.factor) or 1.0)
        if precio_mxn > 0:
            por_item[r.item_code].append({
                "fecha": str(f_ev),
                "fecha_obj": f_ev,
                "serie": "compra_real",
                "origen": "Orden de Compra",
                "doctype": "Purchase Order",
                "folio": r.folio,
                "proveedor": r.proveedor,
                "moneda": r.moneda,
                "tc_dof": tc,
                "precio_origen": round(flt(r.precio_origen) / (flt(r.factor) or 1.0), 4),
                "precio_mxn": round(precio_mxn, 4),
                "uom_captura": r.uom,
                "obra": r.obra,
            })

    # 2. Compras Reales Directas / Campo: Facturas confirmadas sin OC vinculada (evita duplicar con PO)
    tiene_pi_tc = frappe.db.has_column("Purchase Invoice", "custom_lebaref_tc_dof")
    tiene_pi_campo = frappe.db.has_column("Purchase Invoice", "custom_lebaref_es_compra_campo")
    col_pi_tc = "COALESCE(NULLIF(pi.custom_lebaref_tc_dof, 0), NULLIF(pi.conversion_rate, 0), 1.0)" if tiene_pi_tc else "COALESCE(NULLIF(pi.conversion_rate, 0), 1.0)"
    col_pi_campo = "COALESCE(pi.custom_lebaref_es_compra_campo, 0)" if tiene_pi_campo else "0"
    for r in frappe.db.sql(
        f"""
        SELECT
            pii.item_code,
            pi.name AS folio,
            pi.posting_date AS fecha,
            COALESCE(pi.supplier_name, pi.supplier, '') AS proveedor,
            COALESCE(pi.currency, 'MXN') AS moneda,
            {col_pi_tc} AS tc_dof,
            {col_pi_campo} AS es_campo,
            pii.rate AS precio_origen,
            pii.base_net_rate,
            COALESCE(NULLIF(pii.conversion_factor, 0), 1.0) AS factor,
            COALESCE(pii.uom, pii.stock_uom, 'Pieza') AS uom,
            COALESCE(pii.project, '') AS obra
        FROM `tabPurchase Invoice Item` pii
        INNER JOIN `tabPurchase Invoice` pi ON pi.name = pii.parent
        WHERE pi.docstatus = 1
          AND COALESCE(pi.is_return, 0) = 0
          AND COALESCE(pii.purchase_order, '') = ''
          AND pii.item_code IN %s
          AND COALESCE(pii.rate, 0) > 0
        ORDER BY pi.posting_date ASC, pi.creation ASC
        """,
        (codigos,),
        as_dict=True,
    ):
        f_ev = getdate(r.fecha) if r.fecha else getdate(today())
        if fecha_corte and f_ev > fecha_corte:
            continue
        tc = flt(r.tc_dof or 1.0, 4) if r.moneda == "USD" else 1.0
        bruto_mxn = (flt(r.precio_origen) * tc) if r.moneda == "USD" else flt(r.base_net_rate or r.precio_origen)
        precio_mxn = bruto_mxn / (flt(r.factor) or 1.0)
        if precio_mxn > 0:
            por_item[r.item_code].append({
                "fecha": str(f_ev),
                "fecha_obj": f_ev,
                "serie": "compra_real",
                "origen": "Compra en Campo (XML)" if cint(r.es_campo) else "Factura Directa",
                "doctype": "Purchase Invoice",
                "folio": r.folio,
                "proveedor": r.proveedor,
                "moneda": r.moneda,
                "tc_dof": tc,
                "precio_origen": round(flt(r.precio_origen) / (flt(r.factor) or 1.0), 4),
                "precio_mxn": round(precio_mxn, 4),
                "uom_captura": r.uom,
                "obra": r.obra,
            })

    # 3. Cotizaciones Operativas en ERP (Supplier Quotation docstatus=1)
    tiene_sq_tc = frappe.db.has_column("Supplier Quotation", "custom_lebaref_tc_dof")
    tiene_sqi_limpio = frappe.db.has_column("Supplier Quotation Item", "custom_lebaref_precio_limpio_mxn")
    col_sq_tc = "COALESCE(NULLIF(sq.custom_lebaref_tc_dof, 0), NULLIF(sq.conversion_rate, 0), 1.0)" if tiene_sq_tc else "COALESCE(NULLIF(sq.conversion_rate, 0), 1.0)"
    col_sqi_limpio = "COALESCE(sqi.custom_lebaref_precio_limpio_mxn, 0)" if tiene_sqi_limpio else "0"
    for r in frappe.db.sql(
        f"""
        SELECT
            sqi.item_code,
            sq.name AS folio,
            sq.transaction_date AS fecha,
            COALESCE(sq.supplier_name, sq.supplier, '') AS proveedor,
            COALESCE(sq.currency, 'MXN') AS moneda,
            {col_sq_tc} AS tc_dof,
            {col_sqi_limpio} AS limpio_mxn,
            sqi.rate AS precio_origen,
            sqi.base_net_rate,
            COALESCE(NULLIF(sqi.conversion_factor, 0), 1.0) AS factor,
            COALESCE(sqi.uom, sqi.stock_uom, 'Pieza') AS uom,
            COALESCE(sqi.project, '') AS obra
        FROM `tabSupplier Quotation Item` sqi
        INNER JOIN `tabSupplier Quotation` sq ON sq.name = sqi.parent
        WHERE sq.docstatus = 1
          AND sqi.item_code IN %s
          AND COALESCE(sqi.rate, 0) > 0
        ORDER BY sq.transaction_date ASC, sq.creation ASC
        """,
        (codigos,),
        as_dict=True,
    ):
        if excluir_cotizacion and r.folio == excluir_cotizacion:
            continue
        f_ev = getdate(r.fecha) if r.fecha else getdate(today())
        if fecha_corte and f_ev > fecha_corte:
            continue
        tc = flt(r.tc_dof or 1.0, 4) if r.moneda == "USD" else 1.0
        factor = flt(r.factor) or 1.0
        if flt(r.limpio_mxn) > 0:
            precio_mxn = flt(r.limpio_mxn) / factor
        elif r.moneda == "USD":
            precio_mxn = (flt(r.precio_origen) * tc) / factor
        else:
            precio_mxn = flt(r.base_net_rate or r.precio_origen) / factor
        if precio_mxn > 0:
            por_item[r.item_code].append({
                "fecha": str(f_ev),
                "fecha_obj": f_ev,
                "serie": "cotizacion",
                "origen": "Cotización ERP",
                "doctype": "Supplier Quotation",
                "folio": r.folio,
                "proveedor": r.proveedor,
                "moneda": r.moneda,
                "tc_dof": tc,
                "precio_origen": round(flt(r.precio_origen) / factor, 4),
                "precio_mxn": round(precio_mxn, 4),
                "uom_captura": r.uom,
                "obra": r.obra,
            })

    # 4. Archivo Histórico Importado de 1 Año (Lebaref Cotizacion Historica)
    if frappe.db.exists("DocType", "Lebaref Cotizacion Historica"):
        for r in frappe.db.sql(
            """
            SELECT
                ch.item_code,
                ch.name AS folio,
                ch.fecha_cotizacion AS fecha,
                COALESCE(NULLIF(ch.proveedor_texto, ''), ch.proveedor, '') AS proveedor,
                COALESCE(ch.moneda, 'MXN') AS moneda,
                COALESCE(NULLIF(ch.tc_dof, 0), 1.0) AS tc_dof,
                COALESCE(ch.precio_unitario_sin_iva, 0) AS precio_origen,
                COALESCE(ch.precio_unitario_mxn, 0) AS precio_mxn,
                COALESCE(ch.unidad, 'Pieza') AS uom,
                COALESCE(ch.obra_referencia, '') AS obra
            FROM `tabLebaref Cotizacion Historica` ch
            WHERE ch.item_code IN %s
              AND COALESCE(ch.precio_unitario_mxn, 0) > 0
            ORDER BY ch.fecha_cotizacion ASC, ch.creation ASC
            """,
            (codigos,),
            as_dict=True,
        ):
            f_ev = getdate(r.fecha) if r.fecha else getdate(today())
            if fecha_corte and f_ev > fecha_corte:
                continue
            factor = factores_uom.get((r.item_code, (r.uom or "").strip().upper()), 1.0)
            tc = flt(r.tc_dof or 1.0, 4) if r.moneda == "USD" else 1.0
            precio_mxn = flt(r.precio_mxn) / (factor or 1.0)
            if precio_mxn > 0:
                por_item[r.item_code].append({
                    "fecha": str(f_ev),
                    "fecha_obj": f_ev,
                    "serie": "cotizacion",
                    "origen": "Cotización Histórica",
                    "doctype": "Lebaref Cotizacion Historica",
                    "folio": r.folio,
                    "proveedor": r.proveedor,
                    "moneda": r.moneda,
                    "tc_dof": tc,
                    "precio_origen": round(flt(r.precio_origen) / (factor or 1.0), 4),
                    "precio_mxn": round(precio_mxn, 4),
                    "uom_captura": r.uom,
                    "obra": r.obra,
                })

    for ic in por_item:
        por_item[ic].sort(key=lambda ev: (ev["fecha_obj"], 0 if ev["serie"] == "cotizacion" else 1, ev["folio"]))
    return por_item


def _atenuar_atipicos(valores: list[float]) -> list[float]:
    """Suaviza errores de dedo extremos mediante límites robustos basados en mediana + MAD cuando n >= 4."""
    if len(valores) < 4:
        return list(valores)
    med = statistics.median(valores)
    desv_abs = [abs(v - med) for v in valores]
    mad = statistics.median(desv_abs)
    if mad <= 1e-6:
        return list(valores)
    escala = 1.4826 * mad
    piso = max(0.0001, med - 2.8 * escala)
    techo = med + 2.8 * escala
    return [min(techo, max(piso, v)) for v in valores]


def evaluar_modelo_articulo(
    meta_item: dict,
    eventos: list[dict],
    cfg: dict | None = None,
    fecha_ref: date | None = None,
) -> dict:
    """Calcula Precio Estimado MXN, Volatilidad, Tendencia e Índice de Confianza (0–100%) con desglose transparente."""
    cfg = cfg or obtener_configuracion_motor()
    hoy = fecha_ref or getdate(today())
    recotizar_siempre = cint(meta_item.get("custom_lebaref_recotizar_siempre") or 0)
    uom_base = meta_item.get("stock_uom") or "Pieza"

    n_total = len(eventos)
    n_compras = sum(1 for e in eventos if e["serie"] == "compra_real")
    n_cotizaciones = n_total - n_compras
    proveedores_unicos = len({(e.get("proveedor") or "").strip().upper() for e in eventos if (e.get("proveedor") or "").strip()})

    if n_total == 0:
        semaforo = "Recotizar siempre" if recotizar_siempre else "Sin historial"
        color = "red" if recotizar_siempre else "gray"
        return {
            "item_code": meta_item["name"],
            "item_name": meta_item.get("item_name") or meta_item["name"],
            "codigo_barras": meta_item.get("custom_lebaref_codigo_barras") or meta_item["name"],
            "familia": meta_item.get("custom_lebaref_familia") or meta_item.get("item_group") or "General",
            "gerencias": meta_item.get("custom_lebaref_gerencias") or "",
            "uom_base": uom_base,
            "recotizar_siempre": recotizar_siempre,
            "tiene_historial": 0,
            "precio_estimado_mxn": 0.0,
            "precio_min_mxn": 0.0,
            "precio_max_mxn": 0.0,
            "precio_promedio_mxn": 0.0,
            "ultimo_precio_mxn": 0.0,
            "ultima_fecha": "",
            "dias_antiguedad": None,
            "muestra_total": 0,
            "muestra_compras": 0,
            "muestra_cotizaciones": 0,
            "proveedores_distintos": 0,
            "volatilidad_cv_pct": 0.0,
            "tendencia_pct": 0.0,
            "tendencia_etiqueta": "Sin datos",
            "indice_confianza": 0,
            "semaforo": semaforo,
            "semaforo_color": color,
            "desglose_confianza": {
                "frescura": 0,
                "muestra": 0,
                "estabilidad": 0,
                "tendencia": 0,
                "nota": "Exige recotizar siempre" if recotizar_siempre else "Sin cotizaciones ni compras registradas",
            },
        }

    precios_raw = [flt(e["precio_mxn"]) for e in eventos]
    precios_limpios = _atenuar_atipicos(precios_raw)

    pesos = []
    for e in eventos:
        dias_ev = max(0, date_diff(hoy, e["fecha_obj"]))
        peso_tiempo = math.pow(0.5, dias_ev / 90.0)
        peso_fuente = cfg["peso_compra_real"] if e["serie"] == "compra_real" else cfg["peso_cotizacion"]
        pesos.append(max(0.02, peso_tiempo * peso_fuente))

    suma_pesos = sum(pesos) or 1.0
    estimado_mxn = sum(p * w for p, w in zip(precios_limpios, pesos)) / suma_pesos

    media_simple = sum(precios_raw) / n_total
    if n_total >= 2 and media_simple > 0:
        desv = statistics.pstdev(precios_limpios)
        cv_pct = (desv / media_simple) * 100.0
    else:
        cv_pct = 0.0

    # Tendencia temporal (comparación entre tramo inicial y tramo reciente o pendiente lineal)
    tendencia_pct = 0.0
    if n_total >= 2:
        t0 = eventos[0]["fecha_obj"]
        xs = [float(max(0, date_diff(e["fecha_obj"], t0))) for e in eventos]
        span_dias = xs[-1] - xs[0]
        if span_dias >= 7:
            x_med = sum(xs) / n_total
            y_med = sum(precios_limpios) / n_total
            denom = sum((x - x_med) ** 2 for x in xs)
            if denom > 0 and y_med > 0:
                pendiente_diaria = sum((x - x_med) * (y - y_med) for x, y in zip(xs, precios_limpios)) / denom
                cambio_tramo = pendiente_diaria * min(span_dias, 180.0)
                tendencia_pct = (cambio_tramo / y_med) * 100.0
        elif precios_limpios[0] > 0:
            tendencia_pct = ((precios_limpios[-1] - precios_limpios[0]) / precios_limpios[0]) * 100.0

    if abs(tendencia_pct) < 4.0:
        tendencia_etq = "Estable"
    elif tendencia_pct > 0:
        tendencia_etq = f"Al alza (+{tendencia_pct:.1f}%)"
    else:
        tendencia_etq = f"A la baja ({tendencia_pct:.1f}%)"

    ultimo_ev = eventos[-1]
    dias_ultimo = max(0, date_diff(hoy, ultimo_ev["fecha_obj"]))

    # Cálculo transparente de los 4 pilares del Índice de Confianza (0–100%)
    if recotizar_siempre:
        pts_frescura = 0
        pts_muestra = 0
        pts_estabilidad = 0
        pts_tendencia = 0
        confianza = 0
        semaforo = "Recotizar siempre"
        color = "red"
        nota_conf = "Artículo marcado con 'Exige recotizar siempre': confianza fijada en 0%."
    else:
        # 1. Frescura (0 a 35 pts)
        d_opt = cfg["dias_vigencia_optima"]
        d_max = cfg["dias_caducidad_maxima"]
        if dias_ultimo <= d_opt:
            pts_frescura = 35.0
        elif dias_ultimo <= d_max:
            avance = (dias_ultimo - d_opt) / float(max(1, d_max - d_opt))
            pts_frescura = 35.0 - 30.0 * avance
        else:
            pts_frescura = max(0.0, 5.0 - ((dias_ultimo - d_max) / 180.0) * 5.0)

        # 2. Tamaño y respaldo real de muestra (0 a 25 pts)
        tabla_base = {1: 8.0, 2: 14.0, 3: 18.0, 4: 20.0}
        pts_muestra = tabla_base.get(n_total, min(22.0, 20.0 + (n_total - 4) * 0.5))
        if n_compras >= 1:
            pts_muestra += 2.0
        if proveedores_unicos >= 2:
            pts_muestra += 1.0
        pts_muestra = min(25.0, pts_muestra)

        # 3. Estabilidad / Baja Dispersión (0 a 25 pts)
        if n_total == 1:
            pts_estabilidad = 14.0
        else:
            cv_ok = cfg["cv_maximo_estable"]
            if cv_pct <= cv_ok:
                pts_estabilidad = 25.0
            elif cv_pct >= 50.0:
                pts_estabilidad = 0.0
            else:
                pts_estabilidad = 25.0 * (1.0 - ((cv_pct - cv_ok) / max(1.0, 50.0 - cv_ok)))

        # 4. Tendencia controlada (0 a 15 pts)
        abs_tend = abs(tendencia_pct)
        if abs_tend <= 6.0:
            pts_tendencia = 15.0
        elif abs_tend >= 40.0:
            pts_tendencia = 2.0
        else:
            pts_tendencia = 15.0 - 13.0 * ((abs_tend - 6.0) / 34.0)

        confianza = int(round(max(1.0, min(100.0, pts_frescura + pts_muestra + pts_estabilidad + pts_tendencia))))
        if confianza >= cfg["umbral_alta"]:
            semaforo, color = "Alta", "green"
        elif confianza >= cfg["umbral_media"]:
            semaforo, color = "Media", "orange"
        else:
            semaforo, color = "Baja", "red"
        nota_conf = f"Último dato hace {dias_ultimo} d · CV {cv_pct:.1f}% · {n_compras} compra(s) y {n_cotizaciones} cotización(es)."

    return {
        "item_code": meta_item["name"],
        "item_name": meta_item.get("item_name") or meta_item["name"],
        "codigo_barras": meta_item.get("custom_lebaref_codigo_barras") or meta_item["name"],
        "familia": meta_item.get("custom_lebaref_familia") or meta_item.get("item_group") or "General",
        "gerencias": meta_item.get("custom_lebaref_gerencias") or "",
        "uom_base": uom_base,
        "recotizar_siempre": recotizar_siempre,
        "tiene_historial": 1,
        "precio_estimado_mxn": round(estimado_mxn, 2),
        "precio_min_mxn": round(min(precios_raw), 2),
        "precio_max_mxn": round(max(precios_raw), 2),
        "precio_promedio_mxn": round(media_simple, 2),
        "ultimo_precio_mxn": round(flt(ultimo_ev["precio_mxn"]), 2),
        "ultima_fecha": ultimo_ev["fecha"],
        "dias_antiguedad": dias_ultimo,
        "muestra_total": n_total,
        "muestra_compras": n_compras,
        "muestra_cotizaciones": n_cotizaciones,
        "proveedores_distintos": proveedores_unicos,
        "volatilidad_cv_pct": round(cv_pct, 2),
        "tendencia_pct": round(tendencia_pct, 2),
        "tendencia_etiqueta": tendencia_etq,
        "indice_confianza": confianza,
        "semaforo": semaforo,
        "semaforo_color": color,
        "desglose_confianza": {
            "frescura": round(pts_frescura, 1),
            "muestra": round(pts_muestra, 1),
            "estabilidad": round(pts_estabilidad, 1),
            "tendencia": round(pts_tendencia, 1),
            "nota": nota_conf,
        },
    }


def obtener_estimacion_privada_articulo(item_code: str) -> dict | None:
    """Devuelve el resumen estimado SOLO si la sesión pertenece a Oscar; en otro caso devuelve None."""
    if not es_usuario_motor_precios():
        return None
    if not item_code or item_code == ARTICULO_LIBRE or not frappe.db.exists("Item", item_code):
        return None
    campos = ["name", "item_name", "item_group", "stock_uom"]
    for extra in ("custom_lebaref_codigo_barras", "custom_lebaref_familia", "custom_lebaref_gerencias", "custom_lebaref_recotizar_siempre"):
        if frappe.db.has_column("Item", extra):
            campos.append(extra)
    meta = frappe.db.get_value("Item", item_code, campos, as_dict=True)
    if not meta:
        return None
    eventos = _recolectar_eventos_precios((item_code,)).get(item_code, [])
    return evaluar_modelo_articulo(meta, eventos)


def enriquecer_partidas_solicitud_segun_usuario(doc_mr, partidas_lista: list[dict]):
    """Inyecta estado de triaje para todos, y precio estimado + % confianza EXCLUSIVAMENTE para Oscar."""
    es_oscar = es_usuario_motor_precios()
    mapa_rows = {r.name: r for r in (doc_mr.items or [])}

    estimaciones_por_sku = {}
    if es_oscar:
        codigos = tuple({
            r.item_code
            for r in (doc_mr.items or [])
            if r.item_code and r.item_code != ARTICULO_LIBRE and not r.get("custom_lebaref_concepto")
        })
        if codigos:
            cfg = obtener_configuracion_motor()
            campos_it = ["name", "item_name", "item_group", "stock_uom"]
            for ex in ("custom_lebaref_codigo_barras", "custom_lebaref_familia", "custom_lebaref_gerencias", "custom_lebaref_recotizar_siempre"):
                if frappe.db.has_column("Item", ex):
                    campos_it.append(ex)
            metas = {
                it.name: it
                for it in frappe.get_all("Item", filters={"name": ["in", codigos]}, fields=campos_it, limit_page_length=0)
            }
            eventos_map = _recolectar_eventos_precios(codigos)
            for c in codigos:
                if c in metas:
                    estimaciones_por_sku[c] = evaluar_modelo_articulo(metas[c], eventos_map.get(c, []), cfg)

    for p in partidas_lista:
        row_id = p.get("name") or p.get("id")
        r_doc = mapa_rows.get(row_id)
        no_recotizar = cint(r_doc.get("custom_lebaref_no_recotizar") or 0) if r_doc else 0
        p["no_recotizar"] = no_recotizar
        p["no_recotizar_etiqueta"] = "Validado por Gerencia — No recotizar" if no_recotizar else ""

        es_libre_p = bool(p.get("es_libre") or (r_doc and (r_doc.item_code == ARTICULO_LIBRE or r_doc.get("custom_lebaref_concepto"))))
        if es_oscar and r_doc and not es_libre_p and r_doc.item_code in estimaciones_por_sku:
            est = estimaciones_por_sku[r_doc.item_code]
            factor = flt(r_doc.conversion_factor) or 1.0
            precio_uom = round(flt(est["precio_estimado_mxn"]) * factor, 2)
            p["estimacion_oscar"] = {
                "item_code": r_doc.item_code,
                "tiene_historial": est["tiene_historial"],
                "precio_estimado_uom_mxn": precio_uom,
                "subtotal_estimado_mxn": round(precio_uom * flt(r_doc.qty or 0), 2),
                "indice_confianza": est["indice_confianza"],
                "semaforo": est["semaforo"],
                "semaforo_color": est["semaforo_color"],
                "recotizar_siempre": est["recotizar_siempre"],
                "muestra_total": est["muestra_total"],
                "tendencia_etiqueta": est["tendencia_etiqueta"],
            }


@frappe.whitelist(methods=["GET"])
def estado_acceso_motor():
    """Informa al cliente web si debe mostrar controles del Motor Experimental (solo Oscar)."""
    activo = es_usuario_motor_precios()
    return {"autorizado": 1 if activo else 0}


@frappe.whitelist(methods=["POST"])
def marcar_validado_no_recotizar(solicitud: str, mr_item: str, validar: int = 1):
    """Permite a Oscar marcar o desmarcar en 1 clic una partida como 'Validado por Gerencia — No recotizar'."""
    exigir_acceso_motor_precios()
    from lebaref_compras import flujo_operativo

    doc = frappe.get_doc("Material Request", solicitud)
    if doc.docstatus == 2:
        frappe.throw("No se puede modificar una solicitud cancelada.")

    objetivo = next((r for r in doc.items if r.name == mr_item), None)
    if not objetivo:
        frappe.throw("No se encontró la partida indicada en la solicitud.")

    activar = 1 if cint(validar) else 0
    precio_fijado = 0.0
    conf_fijada = 0
    if activar and objetivo.item_code and objetivo.item_code != ARTICULO_LIBRE:
        est = obtener_estimacion_privada_articulo(objetivo.item_code)
        if est and est.get("tiene_historial"):
            precio_fijado = round(flt(est["precio_estimado_mxn"]) * (flt(objetivo.conversion_factor) or 1.0), 4)
            conf_fijada = cint(est["indice_confianza"])

    nombre_usr = frappe.utils.get_fullname(frappe.session.user) or frappe.session.user
    frappe.db.set_value(
        "Material Request Item",
        objetivo.name,
        {
            "custom_lebaref_no_recotizar": activar,
            "custom_lebaref_validado_por": nombre_usr if activar else "",
            "custom_lebaref_validado_fecha": now_datetime() if activar else None,
            "custom_lebaref_precio_estimado_fijado": precio_fijado if activar else 0.0,
            "custom_lebaref_confianza_fijada": conf_fijada if activar else 0,
        },
        update_modified=False,
    )

    desc_partida = objetivo.get("custom_lebaref_concepto") or objetivo.item_name or objetivo.item_code
    accion_txt = (
        f"Partida #{objetivo.idx} ({desc_partida[:60]}): Validado por Gerencia — No recotizar."
        if activar
        else f"Partida #{objetivo.idx} ({desc_partida[:60]}): Se reactivó cotización normal."
    )
    flujo_operativo._agregar_evento_bitacora("Material Request", doc.name, "Triaje Gerencial", accion_txt)

    return {
        "ok": True,
        "solicitud": doc.name,
        "mr_item": objetivo.name,
        "no_recotizar": activar,
        "etiqueta": "Validado por Gerencia — No recotizar" if activar else "",
    }


@frappe.whitelist(methods=["POST"])
def validar_lote_alta_confianza_solicitud(solicitud: str):
    """Marca en 1 clic todas las partidas con Confianza Alta (>= umbral_alta) de una Solicitud como No recotizar."""
    exigir_acceso_motor_precios()
    doc = frappe.get_doc("Material Request", solicitud)
    if doc.docstatus == 2:
        frappe.throw("La solicitud está cancelada.")

    cfg = obtener_configuracion_motor()
    marcadas = 0
    for r in doc.items:
        if not r.item_code or r.item_code == ARTICULO_LIBRE or r.get("custom_lebaref_concepto"):
            continue
        if cint(r.get("custom_lebaref_no_recotizar") or 0) == 1:
            continue
        est = obtener_estimacion_privada_articulo(r.item_code)
        if est and est["tiene_historial"] and not est["recotizar_siempre"] and est["indice_confianza"] >= cfg["umbral_alta"]:
            marcar_validado_no_recotizar(doc.name, r.name, 1)
            marcadas += 1
    return {"ok": True, "marcadas": marcadas, "umbral_alta": cfg["umbral_alta"]}


def registrar_modo_sombra_cotizacion(doc, method=None):
    """Hook silencioso en Supplier Quotation on_submit: compara Estimado previo vs. Nueva cotización real."""
    if not frappe.db.exists("DocType", "Lebaref Evaluacion Sombra"):
        return

    codigos = tuple({
        r.item_code
        for r in (getattr(doc, "items", None) or [])
        if r.item_code and r.item_code != ARTICULO_LIBRE and not r.get("custom_lebaref_concepto") and flt(r.rate) > 0
    })
    if not codigos:
        return

    cfg = obtener_configuracion_motor()
    campos_it = ["name", "item_name", "item_group", "stock_uom"]
    for ex in ("custom_lebaref_codigo_barras", "custom_lebaref_familia", "custom_lebaref_gerencias", "custom_lebaref_recotizar_siempre"):
        if frappe.db.has_column("Item", ex):
            campos_it.append(ex)
    metas = {
        it.name: it
        for it in frappe.get_all("Item", filters={"name": ["in", codigos]}, fields=campos_it, limit_page_length=0)
    }

    fecha_sq = getdate(doc.transaction_date) if doc.transaction_date else getdate(today())
    # Evaluar estrictamente con el historial previo, excluyendo esta nueva cotización
    eventos_previos = _recolectar_eventos_precios(codigos, excluir_cotizacion=doc.name, fecha_corte=fecha_sq)

    tc_dof = flt(doc.get("custom_lebaref_tc_dof") or doc.conversion_rate or 1.0, 4) if doc.currency == "USD" else 1.0

    for r in doc.items:
        ic = r.item_code
        if not ic or ic not in metas or r.get("custom_lebaref_concepto") or flt(r.rate) <= 0:
            continue
        if frappe.db.exists("Lebaref Evaluacion Sombra", {"cotizacion": doc.name, "cotizacion_renglon": r.name}):
            continue

        ev_prev = eventos_previos.get(ic, [])
        if not ev_prev:
            continue

        est_prev = evaluar_modelo_articulo(metas[ic], ev_prev, cfg, fecha_ref=fecha_sq)
        precio_est = flt(est_prev.get("precio_estimado_mxn") or 0.0)
        if precio_est <= 0:
            continue

        factor = flt(r.conversion_factor) or 1.0
        limpio_row = flt(r.get("custom_lebaref_precio_limpio_mxn") or 0.0)
        if limpio_row > 0:
            real_mxn = limpio_row / factor
        elif doc.currency == "USD":
            real_mxn = (flt(r.rate) * tc_dof) / factor
        else:
            real_mxn = flt(r.base_net_rate or r.rate) / factor

        if real_mxn <= 0:
            continue

        dif_mxn = real_mxn - precio_est
        err_pct = (abs(dif_mxn) / real_mxn) * 100.0
        prec_pct = max(0.0, 100.0 - err_pct)

        frappe.get_doc({
            "doctype": "Lebaref Evaluacion Sombra",
            "fecha_evento": fecha_sq,
            "item_code": ic,
            "item_name": (metas[ic].get("item_name") or ic)[:140],
            "cotizacion": doc.name,
            "cotizacion_renglon": r.name,
            "solicitud": r.get("material_request") or None,
            "proveedor": doc.supplier,
            "precio_estimado_previo_mxn": round(precio_est, 4),
            "confianza_previa_pct": cint(est_prev["indice_confianza"]),
            "semaforo_previo": est_prev["semaforo"],
            "muestra_previa": cint(est_prev["muestra_total"]),
            "precio_cotizado_real_mxn": round(real_mxn, 4),
            "diferencia_mxn": round(dif_mxn, 4),
            "error_absoluto_pct": round(err_pct, 2),
            "precision_pct": round(prec_pct, 2),
            "acierto_dentro_10pct": 1 if err_pct <= 10.0 else 0,
        }).insert(ignore_permissions=True)


def limpiar_modo_sombra_cotizacion(doc, method=None):
    """Elimina evaluaciones sombra si una Supplier Quotation es cancelada."""
    if not frappe.db.exists("DocType", "Lebaref Evaluacion Sombra"):
        return
    for name in frappe.get_all("Lebaref Evaluacion Sombra", filters={"cotizacion": doc.name}, pluck="name"):
        frappe.delete_doc("Lebaref Evaluacion Sombra", name, ignore_permissions=True, force=True)


def _resumen_modo_sombra() -> dict:
    if not frappe.db.exists("DocType", "Lebaref Evaluacion Sombra"):
        return {
            "total_evaluaciones": 0,
            "precision_promedio_pct": 0.0,
            "mape_pct": 0.0,
            "aciertos_10pct_ratio": 0.0,
            "por_nivel": {},
            "registros": [],
        }

    rows = frappe.get_all(
        "Lebaref Evaluacion Sombra",
        fields=[
            "name",
            "fecha_evento",
            "item_code",
            "item_name",
            "cotizacion",
            "solicitud",
            "proveedor",
            "precio_estimado_previo_mxn",
            "confianza_previa_pct",
            "semaforo_previo",
            "muestra_previa",
            "precio_cotizado_real_mxn",
            "diferencia_mxn",
            "error_absoluto_pct",
            "precision_pct",
            "acierto_dentro_10pct",
        ],
        order_by="fecha_evento desc, creation desc",
        limit_page_length=250,
    )
    if not rows:
        return {
            "total_evaluaciones": 0,
            "precision_promedio_pct": 0.0,
            "mape_pct": 0.0,
            "aciertos_10pct_ratio": 0.0,
            "por_nivel": {},
            "registros": [],
        }

    n = len(rows)
    prec_med = sum(flt(r.precision_pct) for r in rows) / n
    mape = sum(flt(r.error_absoluto_pct) for r in rows) / n
    aciertos = (sum(1 for r in rows if cint(r.acierto_dentro_10pct)) / n) * 100.0

    por_nivel = {}
    for niv in ("Alta", "Media", "Baja", "Recotizar siempre"):
        sub = [r for r in rows if (r.semaforo_previo or "") == niv]
        if sub:
            por_nivel[niv] = {
                "eventos": len(sub),
                "precision_pct": round(sum(flt(x.precision_pct) for x in sub) / len(sub), 1),
                "mape_pct": round(sum(flt(x.error_absoluto_pct) for x in sub) / len(sub), 1),
            }

    return {
        "total_evaluaciones": n,
        "precision_promedio_pct": round(prec_med, 1),
        "mape_pct": round(mape, 1),
        "aciertos_10pct_ratio": round(aciertos, 1),
        "por_nivel": por_nivel,
        "registros": [
            {
                "id": r.name,
                "fecha": str(r.fecha_evento or ""),
                "item_code": r.item_code,
                "item_name": r.item_name or r.item_code,
                "cotizacion": r.cotizacion,
                "solicitud": r.solicitud or "",
                "proveedor": r.proveedor or "",
                "estimado_previo_mxn": round(flt(r.precio_estimado_previo_mxn), 2),
                "confianza_previa_pct": cint(r.confianza_previa_pct),
                "semaforo_previo": r.semaforo_previo or "",
                "muestra_previa": cint(r.muestra_previa),
                "real_mxn": round(flt(r.precio_cotizado_real_mxn), 2),
                "diferencia_mxn": round(flt(r.diferencia_mxn), 2),
                "error_pct": round(flt(r.error_absoluto_pct), 1),
                "precision_pct": round(flt(r.precision_pct), 1),
                "acierto_10pct": cint(r.acierto_dentro_10pct),
            }
            for r in rows
        ],
    }


@frappe.whitelist(methods=["GET"])
def tablero_confianza_precios(busqueda: str = "", nivel: str = "", gerencia: str = "", solo_con_datos: int = 0):
    """Tablero analítico exclusivo de Oscar: todos los artículos con Precio Estimado, % Confianza y Modo Sombra."""
    exigir_acceso_motor_precios()
    cfg = obtener_configuracion_motor()

    campos = ["name", "item_name", "item_group", "stock_uom"]
    for extra in ("custom_lebaref_codigo_barras", "custom_lebaref_familia", "custom_lebaref_gerencias", "custom_lebaref_recotizar_siempre"):
        if frappe.db.has_column("Item", extra):
            campos.append(extra)

    items = frappe.get_all(
        "Item",
        filters={"disabled": 0, "name": ["!=", ARTICULO_LIBRE]},
        fields=campos,
        order_by="name asc",
        limit_page_length=0,
    )
    codigos = tuple(it.name for it in items)
    eventos_por_sku = _recolectar_eventos_precios(codigos)

    q = (busqueda or "").strip().upper()
    niv = (nivel or "").strip()
    ger = (gerencia or "").strip().upper()
    exigir_datos = cint(solo_con_datos) == 1

    conteos = {
        "total_catalogo": len(items),
        "con_historial": 0,
        "alta": 0,
        "media": 0,
        "baja": 0,
        "recotizar_siempre": 0,
    }

    evaluados = []
    for it in items:
        ev = evaluar_modelo_articulo(it, eventos_por_sku.get(it.name, []), cfg)
        if ev["recotizar_siempre"]:
            conteos["recotizar_siempre"] += 1
        if ev["tiene_historial"]:
            conteos["con_historial"] += 1
            if ev["semaforo"] == "Alta":
                conteos["alta"] += 1
            elif ev["semaforo"] == "Media":
                conteos["media"] += 1
            elif ev["semaforo"] == "Baja":
                conteos["baja"] += 1

        if exigir_datos and not ev["tiene_historial"] and not ev["recotizar_siempre"]:
            continue
        if niv and niv != "Todos":
            if niv == "Con historial" and not ev["tiene_historial"]:
                continue
            elif niv != "Con historial" and ev["semaforo"] != niv:
                continue
        if ger and ger not in (ev["gerencias"] or "").upper():
            continue
        if q:
            blob = f"{ev['item_code']} {ev['codigo_barras']} {ev['item_name']} {ev['familia']} {ev['gerencias']}".upper()
            if q not in blob:
                continue
        evaluados.append(ev)

    # Ordenar primero los que tienen historial por mayor confianza y muestra
    evaluados.sort(
        key=lambda x: (
            1 if x["tiene_historial"] else 0,
            x["indice_confianza"],
            x["muestra_total"],
        ),
        reverse=True,
    )

    return {
        "config": cfg,
        "conteos": conteos,
        "sombra": _resumen_modo_sombra(),
        "articulos": evaluados[:300],
        "total_filtrados": len(evaluados),
    }


@frappe.whitelist(methods=["GET"])
def detalle_serie_articulo(item_code: str):
    """Devuelve para la vista de Oscar la serie Tiempo (X) vs. Precio MXN (Y) + tabla documental histórica."""
    exigir_acceso_motor_precios()
    if not item_code or item_code == ARTICULO_LIBRE or not frappe.db.exists("Item", item_code):
        frappe.throw("Selecciona un artículo válido del catálogo.")

    cfg = obtener_configuracion_motor()
    campos = ["name", "item_name", "item_group", "stock_uom"]
    for extra in ("custom_lebaref_codigo_barras", "custom_lebaref_familia", "custom_lebaref_gerencias", "custom_lebaref_recotizar_siempre"):
        if frappe.db.has_column("Item", extra):
            campos.append(extra)
    meta = frappe.db.get_value("Item", item_code, campos, as_dict=True)

    eventos_raw = _recolectar_eventos_precios((item_code,)).get(item_code, [])
    resumen = evaluar_modelo_articulo(meta, eventos_raw, cfg)

    puntos = []
    for idx, ev in enumerate(eventos_raw, start=1):
        puntos.append({
            "idx": idx,
            "fecha": ev["fecha"],
            "timestamp_ms": int(getdate(ev["fecha_obj"]).strftime("%s")) * 1000 if hasattr(getdate(ev["fecha_obj"]), "strftime") else 0,
            "serie": ev["serie"],
            "serie_etiqueta": "Compra Real" if ev["serie"] == "compra_real" else "Cotización",
            "origen": ev["origen"],
            "doctype": ev["doctype"],
            "folio": ev["folio"],
            "proveedor": ev["proveedor"] or "—",
            "moneda": ev["moneda"],
            "tc_dof": ev["tc_dof"],
            "precio_origen": ev["precio_origen"],
            "precio_mxn": ev["precio_mxn"],
            "uom_captura": ev["uom_captura"],
            "obra": ev["obra"],
        })

    # Calcular timestamp_ms portátil (compatible Windows/Linux sin depender de %s)
    epoca = date(1970, 1, 1)
    for pt, ev in zip(puntos, eventos_raw):
        pt["timestamp_ms"] = (ev["fecha_obj"] - epoca).days * 86400000

    sombra_item = []
    if frappe.db.exists("DocType", "Lebaref Evaluacion Sombra"):
        for s in frappe.get_all(
            "Lebaref Evaluacion Sombra",
            filters={"item_code": item_code},
            fields=[
                "fecha_evento",
                "cotizacion",
                "proveedor",
                "precio_estimado_previo_mxn",
                "confianza_previa_pct",
                "precio_cotizado_real_mxn",
                "error_absoluto_pct",
                "precision_pct",
            ],
            order_by="fecha_evento desc, creation desc",
            limit_page_length=25,
        ):
            sombra_item.append({
                "fecha": str(s.fecha_evento or ""),
                "cotizacion": s.cotizacion,
                "proveedor": s.proveedor or "",
                "estimado_previo_mxn": round(flt(s.precio_estimado_previo_mxn), 2),
                "confianza_previa_pct": cint(s.confianza_previa_pct),
                "real_mxn": round(flt(s.precio_cotizado_real_mxn), 2),
                "error_pct": round(flt(s.error_absoluto_pct), 1),
                "precision_pct": round(flt(s.precision_pct), 1),
            })

    return {
        "resumen": resumen,
        "config": cfg,
        "puntos": puntos,
        "sombra_item": sombra_item,
    }


@frappe.whitelist(methods=["POST"])
def guardar_configuracion_motor(datos: str):
    """Permite a Oscar calibrar los umbrales del semáforo y ponderaciones del motor."""
    exigir_acceso_motor_precios()
    _asegurar_doctype_config_motor()
    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(d, dict):
        frappe.throw("Parámetros de configuración inválidos.")

    u_alta = max(55, min(99, cint(d.get("umbral_alta") or 80)))
    u_media = max(20, min(u_alta - 5, cint(d.get("umbral_media") or 50)))
    d_opt = max(7, min(180, cint(d.get("dias_vigencia_optima") or 45)))
    d_max = max(d_opt + 30, min(1095, cint(d.get("dias_caducidad_maxima") or 365)))
    p_real = max(0.5, min(5.0, flt(d.get("peso_compra_real") or 1.5)))
    p_cot = max(0.2, min(3.0, flt(d.get("peso_cotizacion") or 1.0)))
    cv_est = max(2.0, min(40.0, flt(d.get("cv_maximo_estable") or 12.0)))

    doc = frappe.get_single("Lebaref Configuracion Motor Precios")
    doc.umbral_alta = u_alta
    doc.umbral_media = u_media
    doc.dias_vigencia_optima = d_opt
    doc.dias_caducidad_maxima = d_max
    doc.peso_compra_real = p_real
    doc.peso_cotizacion = p_cot
    doc.cv_maximo_estable = cv_est
    doc.save(ignore_permissions=True)

    return {"ok": True, "config": obtener_configuracion_motor()}
