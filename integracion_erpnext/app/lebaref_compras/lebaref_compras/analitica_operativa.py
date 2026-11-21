"""Módulo operativo y analítico de Lebaref para ERPNext 16:
- GES-01 / GES-02: Tablero de Pendientes, Agenda y Checkpoints por área (Compras, Dirección, Almacén y Flotilla).
- ALM-09: Alertas de Stock Mínimo con detección de pedidos en tránsito y generación de Solicitud de Reabastecimiento sin duplicados.
- CAT-05: Expediente Financiero y Operativo por Obra / Proyecto (OCs, Resguardo, Consumo real, Herramientas y Gastos).
- COM-05: Referencias de Banco de Precios Histórico normalizadas para Cotización Directa y Cuadro Comparativo.
- COM-08: Cierre parcial (Finiquitar remanente) y Reapertura de Orden de Compra con motivo auditable.
- SOL-04: Previsualización e importación controlada de Solicitud desde CSV o pegado de Excel.
- FLO-02: Indicadores reales de Flotilla (km/L con separación explícita de datos insuficientes, $/km, gasto por categoría y proyección de servicio).
- ANA-01: Analítica Ejecutiva (Variación de Precios PPV, Ranking de Proveedores, Matriz ABC/Kraljic por familia y Costo por Obra).
"""
from collections import defaultdict
import csv
import io
import json
import uuid

import frappe
from frappe.utils import add_days, cint, date_diff, flt, getdate, today


@frappe.whitelist(methods=["GET"])
def tablero_pendientes():
    """Devuelve la agenda viva de pendientes y checkpoints agrupada por área operativa (GES-01 / GES-02)."""
    hoy = getdate(today())
    limite_pronto = add_days(hoy, 7)

    # 1. COMPRAS: Solicitudes sin cotización, comparativas listas y entradas por regularizar
    solicitudes_sin_cotizar = []
    comparativas_listas = []
    if frappe.has_permission("Material Request", "read"):
        mrs = frappe.get_list(
            "Material Request",
            filters={
                "docstatus": 1,
                "material_request_type": "Purchase",
                "status": ["in", ["Pending", "Partially Ordered"]],
            },
            fields=["name", "title", "custom_lebaref_nombre_solicitud", "transaction_date", "custom_lebaref_solicitante",
                    "custom_lebaref_concluida", "custom_lebaref_en_espera_de"],
            order_by="transaction_date desc",
            limit_page_length=80,
        )
        # Las marcadas Concluida o En espera (depende de otra persona) no cuentan como pendiente propio
        mrs = [m for m in mrs if not cint(m.get("custom_lebaref_concluida")) and not (m.get("custom_lebaref_en_espera_de") or "").strip()]
        if mrs:
            mr_names = [m.name for m in mrs]
            mri_rows = frappe.db.sql(
                """
                SELECT mri.parent AS mr, COUNT(DISTINCT sqi.parent) AS num_sq, COUNT(DISTINCT poi.parent) AS num_po
                FROM `tabMaterial Request Item` mri
                LEFT JOIN `tabSupplier Quotation Item` sqi
                    ON sqi.material_request_item = mri.name AND sqi.docstatus = 1
                LEFT JOIN `tabPurchase Order Item` poi
                    ON poi.material_request_item = mri.name AND poi.docstatus < 2
                WHERE mri.parent IN %s
                GROUP BY mri.parent
                """,
                (tuple(mr_names),),
                as_dict=True,
            )
            stats_mr = {r.mr: r for r in mri_rows}
            for m in mrs:
                st = stats_mr.get(m.name) or frappe._dict(num_sq=0, num_po=0)
                item_info = {
                    "folio": m.name,
                    "titulo": m.custom_lebaref_nombre_solicitud or m.title or m.name,
                    "fecha": str(m.transaction_date or ""),
                    "responsable": m.custom_lebaref_solicitante or "—",
                    "cotizaciones": cint(st.num_sq),
                    "doctype": "Material Request",
                }
                if cint(st.num_sq) == 0:
                    solicitudes_sin_cotizar.append(item_info)
                else:
                    comparativas_listas.append(item_info)

    entradas_por_regularizar = []
    if frappe.has_permission("Purchase Receipt", "read") and frappe.get_meta("Purchase Receipt").has_field("custom_lebaref_por_regularizar"):
        for pr in frappe.get_list(
            "Purchase Receipt",
            filters={"docstatus": 1, "custom_lebaref_por_regularizar": 1},
            fields=["name", "supplier_name", "posting_date", "grand_total"],
            order_by="posting_date desc",
            limit_page_length=40,
        ):
            entradas_por_regularizar.append({
                "folio": pr.name,
                "titulo": f"{pr.supplier_name or 'Proveedor'} · ${flt(pr.grand_total):,.2f}",
                "fecha": str(pr.posting_date or ""),
                "doctype": "Purchase Receipt",
            })

    # 2. DIRECCIÓN / RESPONSABLE: OCs por autorizar, sobrantes históricos en borrador y pagos próximos
    oc_por_autorizar = []
    oc_por_recibir = []
    pagos_proximos = []
    if frappe.has_permission("Purchase Order", "read"):
        for po in frappe.get_list(
            "Purchase Order",
            filters={"docstatus": 0},
            fields=["name", "supplier_name", "transaction_date", "grand_total"],
            order_by="creation desc",
            limit_page_length=40,
        ):
            oc_por_autorizar.append({
                "folio": po.name,
                "titulo": f"{po.supplier_name} · ${flt(po.grand_total):,.2f}",
                "fecha": str(po.transaction_date or ""),
                "doctype": "Purchase Order",
            })

        for po_open in frappe.get_list(
            "Purchase Order",
            filters={"docstatus": 1, "status": ["in", ["To Receive and Bill", "To Receive"]]},
            fields=["name", "supplier_name", "transaction_date", "per_received", "grand_total"],
            order_by="transaction_date desc",
            limit_page_length=50,
        ):
            oc_por_recibir.append({
                "folio": po_open.name,
                "titulo": f"{po_open.supplier_name} ({flt(po_open.per_received):.0f}% recibido)",
                "fecha": str(po_open.transaction_date or ""),
                "doctype": "Purchase Order",
            })

        filas_pago = frappe.db.sql(
            """
            SELECT po.name AS folio, po.supplier_name, ps.due_date, ps.payment_amount, po.status
            FROM `tabPayment Schedule` ps
            INNER JOIN `tabPurchase Order` po ON po.name = ps.parent AND ps.parenttype = 'Purchase Order'
            WHERE po.docstatus = 1
              AND po.status NOT IN ('Closed', 'Cancelled', 'Completed')
              AND ps.due_date IS NOT NULL
              AND ps.due_date <= %s
            ORDER BY ps.due_date ASC
            LIMIT 30
            """,
            (limite_pronto,),
            as_dict=True,
        )
        for fp in filas_pago:
            vencido = getdate(fp.due_date) < hoy
            pagos_proximos.append({
                "folio": fp.folio,
                "titulo": f"{fp.supplier_name} · ${flt(fp.payment_amount):,.2f} ({'Vencido' if vencido else 'Próximo'})",
                "fecha": str(fp.due_date),
                "vencido": vencido,
                "doctype": "Purchase Order",
            })

    sobrantes_por_autorizar = []
    if frappe.has_permission("Stock Entry", "read") and frappe.get_meta("Stock Entry").has_field("custom_lebaref_tipo_sobrante"):
        for se in frappe.get_list(
            "Stock Entry",
            filters={"docstatus": 0, "custom_lebaref_tipo_sobrante": ["is", "set"]},
            fields=["name", "project", "posting_date", "custom_lebaref_referencia_sobrante", "custom_lebaref_persona_sobrante"],
            order_by="creation desc",
            limit_page_length=30,
        ):
            sobrantes_por_autorizar.append({
                "folio": se.name,
                "titulo": f"Acta {se.custom_lebaref_referencia_sobrante or '—'} · {se.project or 'Sin obra'}",
                "fecha": str(se.posting_date or ""),
                "doctype": "Stock Entry",
            })

    # 3. ALMACÉN, HERRAMIENTAS Y FLOTILLA: Stock mínimo, préstamos vencidos y alertas vehiculares
    resumen_min = alertas_stock_minimo()
    bajo_min_count = len(resumen_min.get("requieren_pedido", []))

    prestamos_vencidos = []
    if frappe.db.exists("DocType", "Lebaref Prestamo") and frappe.has_permission("Lebaref Prestamo", "read"):
        for pr_h in frappe.get_list(
            "Lebaref Prestamo",
            filters={"estado": ["!=", "Devuelto"], "compromiso": ["<=", str(hoy)]},
            fields=["name", "responsable", "obra", "compromiso", "estado"],
            order_by="compromiso asc",
            limit_page_length=30,
        ):
            prestamos_vencidos.append({
                "folio": pr_h.name,
                "titulo": f"{pr_h.responsable} · Obra {pr_h.obra}",
                "fecha": str(pr_h.compromiso or ""),
                "doctype": "Lebaref Prestamo",
            })

    alertas_flotilla = []
    if frappe.db.exists("DocType", "Lebaref Vehiculo") and frappe.has_permission("Lebaref Vehiculo", "read"):
        for v in frappe.get_list(
            "Lebaref Vehiculo",
            fields=["name", "codigo", "placas", "marca_modelo", "km_actual", "proximo_servicio", "seguro", "estado"],
            limit_page_length=50,
        ):
            km_rest = cint(v.proximo_servicio) - cint(v.km_actual)
            dias_seg = date_diff(v.seguro, hoy) if v.seguro else 999
            motivos = []
            if km_rest <= 1000:
                motivos.append(f"Servicio en {km_rest} km" if km_rest >= 0 else f"Servicio vencido ({abs(km_rest)} km)")
            if dias_seg <= 30:
                motivos.append(f"Seguro vence en {dias_seg} d" if dias_seg >= 0 else "Seguro vencido")
            if motivos:
                alertas_flotilla.append({
                    "folio": v.name,
                    "titulo": f"{v.codigo} ({v.placas}) · {' | '.join(motivos)}",
                    "fecha": str(v.seguro or ""),
                    "doctype": "Lebaref Vehiculo",
                })

    return {
        "conteos": {
            "solicitudes_sin_cotizar": len(solicitudes_sin_cotizar),
            "comparativas_listas": len(comparativas_listas),
            "entradas_por_regularizar": len(entradas_por_regularizar),
            "oc_por_autorizar": len(oc_por_autorizar),
            "sobrantes_por_autorizar": len(sobrantes_por_autorizar),
            "pagos_proximos": len(pagos_proximos),
            "oc_por_recibir": len(oc_por_recibir),
            "bajo_stock_minimo": bajo_min_count,
            "prestamos_vencidos": len(prestamos_vencidos),
            "alertas_flotilla": len(alertas_flotilla),
        },
        "compras": {
            "solicitudes_sin_cotizar": solicitudes_sin_cotizar[:20],
            "comparativas_listas": comparativas_listas[:20],
            "entradas_por_regularizar": entradas_por_regularizar[:20],
        },
        "direccion": {
            "oc_por_autorizar": oc_por_autorizar[:20],
            "sobrantes_por_autorizar": sobrantes_por_autorizar[:20],
            "pagos_proximos": pagos_proximos[:20],
        },
        "almacen_activos": {
            "oc_por_recibir": oc_por_recibir[:20],
            "bajo_stock_minimo": resumen_min.get("requieren_pedido", [])[:20],
            "prestamos_vencidos": prestamos_vencidos[:20],
            "alertas_flotilla": alertas_flotilla[:20],
        },
    }


@frappe.whitelist(methods=["GET"])
def alertas_stock_minimo():
    """Detecta artículos bajo stock mínimo y separa los que ya tienen solicitud/OC abierta para evitar duplicados (ALM-09)."""
    if not frappe.has_permission("Item", "read"):
        return {"requieren_pedido": [], "en_transito": []}

    rows = frappe.db.sql(
        """
        SELECT
            i.name AS item_code,
            i.item_name,
            i.item_group,
            i.stock_uom,
            COALESCE(NULLIF(i.purchase_uom, ''), i.stock_uom) AS purchase_uom,
            COALESCE(i.custom_lebaref_codigo_barras, i.name) AS clave,
            COALESCE(i.custom_lebaref_ubicacion, 'Almacén General') AS ubicacion,
            COALESCE(i.custom_lebaref_stock_minimo, 0) AS stock_minimo,
            COALESCE(i.custom_lebaref_ultimo_costo, 0) AS ultimo_costo,
            COALESCE(b_sum.qty_total, 0) AS stock_actual
        FROM `tabItem` i
        LEFT JOIN (
            SELECT item_code, SUM(actual_qty) AS qty_total
            FROM `tabBin`
            GROUP BY item_code
        ) b_sum ON b_sum.item_code = i.name
        WHERE i.disabled = 0
          AND i.is_stock_item = 1
          AND COALESCE(i.custom_lebaref_stock_minimo, 0) > 0
          AND COALESCE(b_sum.qty_total, 0) <= COALESCE(i.custom_lebaref_stock_minimo, 0)
        ORDER BY (COALESCE(i.custom_lebaref_stock_minimo, 0) - COALESCE(b_sum.qty_total, 0)) DESC, i.name ASC
        """,
        as_dict=True,
    )
    if not rows:
        return {"requieren_pedido": [], "en_transito": []}

    codigos = tuple(r.item_code for r in rows)

    # Factores de conversión para compra
    factores = {}
    for u in frappe.db.sql(
        "SELECT parent, uom, conversion_factor FROM `tabUOM Conversion Detail` WHERE parent IN %s",
        (codigos,),
        as_dict=True,
    ):
        factores[(u.parent, u.uom)] = flt(u.conversion_factor) or 1.0

    # Cantidades ya pedidas en Solicitudes activas (pendientes de OC)
    mr_abiertas = defaultdict(lambda: {"qty": 0.0, "folios": set()})
    for m in frappe.db.sql(
        """
        SELECT mri.item_code, mri.parent, SUM(GREATEST(mri.stock_qty - COALESCE(mri.ordered_qty, 0), 0)) AS pend
        FROM `tabMaterial Request Item` mri
        INNER JOIN `tabMaterial Request` mr ON mr.name = mri.parent
        WHERE mr.docstatus = 1
          AND mr.material_request_type = 'Purchase'
          AND mr.status NOT IN ('Stopped', 'Cancelled', 'Ordered')
          AND mri.item_code IN %s
        GROUP BY mri.item_code, mri.parent
        HAVING SUM(GREATEST(mri.stock_qty - COALESCE(mri.ordered_qty, 0), 0)) > 0
        """,
        (codigos,),
        as_dict=True,
    ):
        mr_abiertas[m.item_code]["qty"] += flt(m.pend)
        mr_abiertas[m.item_code]["folios"].add(m.parent)

    # Cantidades en OCs abiertas (borrador o autorizadas pendientes de recibir)
    po_abiertas = defaultdict(lambda: {"qty": 0.0, "folios": set()})
    for p in frappe.db.sql(
        """
        SELECT poi.item_code, poi.parent, SUM(GREATEST(poi.stock_qty - COALESCE(poi.received_qty, 0) * COALESCE(poi.conversion_factor, 1), 0)) AS pend
        FROM `tabPurchase Order Item` poi
        INNER JOIN `tabPurchase Order` po ON po.name = poi.parent
        WHERE po.docstatus IN (0, 1)
          AND po.status NOT IN ('Closed', 'On Hold', 'Cancelled', 'Completed', 'Delivered')
          AND poi.item_code IN %s
        GROUP BY poi.item_code, poi.parent
        HAVING SUM(GREATEST(poi.stock_qty - COALESCE(poi.received_qty, 0) * COALESCE(poi.conversion_factor, 1), 0)) > 0
        """,
        (codigos,),
        as_dict=True,
    ):
        po_abiertas[p.item_code]["qty"] += flt(p.pend)
        po_abiertas[p.item_code]["folios"].add(p.parent)

    requieren_pedido = []
    en_transito = []

    for r in rows:
        en_mr = flt(mr_abiertas[r.item_code]["qty"])
        en_po = flt(po_abiertas[r.item_code]["qty"])
        total_transito = en_mr + en_po
        folios_activos = sorted(mr_abiertas[r.item_code]["folios"] | po_abiertas[r.item_code]["folios"])

        factor = factores.get((r.item_code, r.purchase_uom), 1.0)
        faltante_base = max(flt(r.stock_minimo) * 2.0 - (flt(r.stock_actual) + total_transito), flt(r.stock_minimo) - flt(r.stock_actual), 1.0)
        sugerido_compra = max(1.0, round(faltante_base / factor, 2) if factor > 1 else round(faltante_base, 2))

        registro = {
            "item_code": r.item_code,
            "clave": r.clave,
            "nombre": r.item_name,
            "categoria": r.item_group,
            "ubicacion": r.ubicacion,
            "stock_actual": flt(r.stock_actual),
            "stock_minimo": flt(r.stock_minimo),
            "en_transito": total_transito,
            "folios_activos": folios_activos,
            "uom_almacen": r.stock_uom,
            "uom_compra": r.purchase_uom,
            "factor_compra": factor,
            "sugerido_compra": sugerido_compra,
            "ultimo_costo": flt(r.ultimo_costo),
        }
        if flt(r.stock_actual) + total_transito <= flt(r.stock_minimo) and not folios_activos:
            requieren_pedido.append(registro)
        else:
            en_transito.append(registro)

    return {
        "requieren_pedido": requieren_pedido,
        "en_transito": en_transito,
    }


@frappe.whitelist(methods=["POST"])
def generar_solicitud_reabastecimiento(datos: str):
    """Genera una Solicitud de Material consolidada y trazable para artículos bajo stock mínimo sin duplicar pedidos (ALM-09)."""
    from lebaref_compras import operaciones

    payload = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(payload, dict):
        frappe.throw("Datos de reabastecimiento inválidos.")

    filas_in = payload.get("items") or []
    if not filas_in:
        frappe.throw("Selecciona al menos un artículo para reabastecer.")

    actual_alertas = alertas_stock_minimo()
    ya_en_transito = {x["item_code"]: x["folios_activos"] for x in actual_alertas.get("en_transito", []) if x.get("folios_activos")}

    company = frappe.get_all("Company", pluck="name")[0]
    almacen = (payload.get("warehouse") or "").strip() or (
        frappe.db.get_value("Warehouse", {"company": company, "is_group": 0, "disabled": 0}, "name") or ""
    )

    rows_mr = []
    omitidos = []
    for f in filas_in:
        code = (f.get("item_code") or "").strip()
        qty = flt(f.get("qty") or 0)
        if not code or qty <= 0:
            continue
        if code in ya_en_transito and not payload.get("forzar"):
            omitidos.append(f"{code} ({', '.join(ya_en_transito[code])})")
            continue
        uom_c = (f.get("uom") or "").strip()
        row_dict = {"item_code": code, "qty": qty, "warehouse": almacen}
        if uom_c:
            row_dict["uom"] = uom_c
        rows_mr.append(row_dict)

    if not rows_mr:
        frappe.throw("Todos los artículos seleccionados ya cuentan con una solicitud u orden activa en curso: " + "; ".join(omitidos))

    titulo = (payload.get("titulo") or "").strip() or f"Reabastecimiento Stock Mínimo · {today()}"
    req_data = {
        "company": company,
        "schedule_date": add_days(today(), 3),
        "title": titulo[:140],
        "departamento": (payload.get("departamento") or "").strip(),
        "solicitante": (payload.get("solicitante") or frappe.session.user).strip(),
        "notas": "Generada automáticamente desde Centro de Alertas de Stock Mínimo (ALM-09).",
        "rows": rows_mr,
    }
    res = operaciones.ejecutar("solicitud", json.dumps(req_data, ensure_ascii=False), str(uuid.uuid4()))
    if frappe.get_meta("Material Request").has_field("custom_lebaref_nombre_solicitud"):
        frappe.db.set_value("Material Request", res["folio"], "custom_lebaref_nombre_solicitud", titulo[:140], update_modified=False)
    res["omitidos_en_transito"] = omitidos
    return res


@frappe.whitelist(methods=["GET"])
def expediente_obra(obra: str):
    """Consolida compras, entregas bajo resguardo, consumos reales, herramientas y gastos vehiculares de una Obra (CAT-05)."""
    prj = frappe.get_doc("Project", obra)
    prj.check_permission("read")

    solicitudes = []
    if frappe.has_permission("Material Request", "read"):
        solicitudes = frappe.db.sql(
            """
            SELECT DISTINCT mr.name, COALESCE(mr.custom_lebaref_nombre_solicitud, mr.title, mr.name) AS titulo,
                   mr.transaction_date, mr.status, mr.custom_lebaref_solicitante AS solicitante
            FROM `tabMaterial Request Item` mri
            INNER JOIN `tabMaterial Request` mr ON mr.name = mri.parent
            WHERE mri.project = %s AND mr.docstatus < 2
            ORDER BY mr.transaction_date DESC, mr.name DESC
            LIMIT 60
            """,
            (prj.name,),
            as_dict=True,
        )

    ordenes = []
    total_ordenes = 0.0
    if frappe.has_permission("Purchase Order", "read"):
        ordenes = frappe.db.sql(
            """
            SELECT po.name, po.supplier_name, po.transaction_date, po.status,
                   SUM(poi.base_net_amount) AS importe_obra
            FROM `tabPurchase Order Item` poi
            INNER JOIN `tabPurchase Order` po ON po.name = poi.parent
            WHERE poi.project = %s AND po.docstatus = 1
            GROUP BY po.name
            ORDER BY po.transaction_date DESC, po.name DESC
            LIMIT 80
            """,
            (prj.name,),
            as_dict=True,
        )
        total_ordenes = sum(flt(o.importe_obra) for o in ordenes)

    movimientos_almacen = []
    total_consumido = 0.0
    total_entregado_resguardo = 0.0
    if frappe.has_permission("Stock Entry", "read"):
        movimientos_almacen = frappe.db.sql(
            """
            SELECT se.name, se.posting_date, se.purpose, se.stock_entry_type,
                   se.custom_lebaref_origen AS entrega_origen,
                   se.custom_lebaref_persona_sobrante AS persona,
                   SUM(sed.amount) AS costo_movimiento,
                   COUNT(sed.name) AS partidas
            FROM `tabStock Entry Detail` sed
            INNER JOIN `tabStock Entry` se ON se.name = sed.parent
            WHERE (sed.project = %s OR se.project = %s)
              AND se.docstatus = 1
            GROUP BY se.name
            ORDER BY se.posting_date DESC, se.name DESC
            LIMIT 80
            """,
            (prj.name, prj.name),
            as_dict=True,
        )
        for m in movimientos_almacen:
            if m.purpose == "Material Issue":
                total_consumido += flt(m.costo_movimiento)
            elif m.purpose == "Material Transfer" and not m.entrega_origen:
                total_entregado_resguardo += flt(m.costo_movimiento)

    herramientas_en_obra = []
    if frappe.db.exists("DocType", "Lebaref Herramienta") and frappe.has_permission("Lebaref Herramienta", "read"):
        herramientas_en_obra = frappe.get_list(
            "Lebaref Herramienta",
            filters={"obra": prj.name},
            fields=["name", "codigo", "articulo", "marca_modelo", "estado", "responsable", "prestamo"],
            order_by="codigo asc",
            limit_page_length=100,
        )

    prestamos_obra = []
    if frappe.db.exists("DocType", "Lebaref Prestamo") and frappe.has_permission("Lebaref Prestamo", "read"):
        prestamos_obra = frappe.get_list(
            "Lebaref Prestamo",
            filters={"obra": prj.name},
            fields=["name", "fecha", "compromiso", "responsable", "recibe", "estado"],
            order_by="fecha desc",
            limit_page_length=50,
        )

    gastos_flotilla = 0.0
    if frappe.db.exists("DocType", "Lebaref Gasto Vehiculo") and frappe.has_permission("Lebaref Gasto Vehiculo", "read"):
        gastos_flotilla = flt(
            frappe.db.sql(
                "SELECT COALESCE(SUM(monto), 0) FROM `tabLebaref Gasto Vehiculo` WHERE obra = %s",
                (prj.name,),
            )[0][0]
        )

    return {
        "obra": prj.name,
        "nombre_obra": prj.project_name or prj.name,
        "cliente": prj.customer or "Sin cliente asignado",
        "estado": prj.status or "Open",
        "totales": {
            "ordenes_neto": total_ordenes,
            "resguardo_entregado": total_entregado_resguardo,
            "consumido_real": total_consumido,
            "gastos_flotilla": gastos_flotilla,
            "costo_total_acumulado": total_consumido + gastos_flotilla,
            "herramientas_activas": len(herramientas_en_obra),
            "solicitudes_count": len(solicitudes),
            "ordenes_count": len(ordenes),
        },
        "solicitudes": [
            {
                "folio": s.name,
                "titulo": s.titulo,
                "fecha": str(s.transaction_date or ""),
                "estado": s.status,
                "solicitante": s.solicitante or "—",
            }
            for s in solicitudes
        ],
        "ordenes": [
            {
                "folio": o.name,
                "proveedor": o.supplier_name,
                "fecha": str(o.transaction_date or ""),
                "estado": o.status,
                "importe": flt(o.importe_obra),
            }
            for o in ordenes
        ],
        "movimientos_almacen": [
            {
                "folio": m.name,
                "fecha": str(m.posting_date or ""),
                "tipo": "Consumo Real (ALM-05)" if m.purpose == "Material Issue" else ("Retorno a Almacén" if m.entrega_origen else "Entrega / Resguardo"),
                "partidas": cint(m.partidas),
                "costo": flt(m.costo_movimiento),
            }
            for m in movimientos_almacen
        ],
        "herramientas": herramientas_en_obra,
        "prestamos": [
            {
                "folio": p.name,
                "fecha": str(p.fecha or ""),
                "compromiso": str(p.compromiso or ""),
                "responsable": p.responsable,
                "recibe": p.recibe,
                "estado": p.estado,
            }
            for p in prestamos_obra
        ],
    }


@frappe.whitelist(methods=["GET"])
def referencias_historicas_solicitud(solicitud: str):
    """Obtiene el último costo y mejor precio histórico normalizado por unidad para cada partida de una Solicitud (COM-05)."""
    mr = frappe.get_doc("Material Request", solicitud)
    mr.check_permission("read")

    codigos = tuple({r.item_code for r in mr.items if r.item_code and r.item_code != "LEBAREF-CONCEPTO-LIBRE"})
    if not codigos:
        return {}

    info_item = {
        i.name: i
        for i in frappe.get_all(
            "Item",
            filters={"name": ["in", codigos]},
            fields=["name", "custom_lebaref_ultimo_costo", "stock_uom"],
        )
    }

    precios_lista = {}
    for ip in frappe.get_all(
        "Item Price",
        filters={"item_code": ["in", codigos], "buying": 1},
        fields=["item_code", "price_list_rate", "supplier", "uom"],
        order_by="modified desc",
    ):
        if ip.item_code not in precios_lista and flt(ip.price_list_rate) > 0:
            precios_lista[ip.item_code] = ip

    mejores_po = {}
    for po_row in frappe.db.sql(
        """
        SELECT poi.item_code,
               (poi.base_net_rate / NULLIF(COALESCE(poi.conversion_factor, 1), 0)) AS rate_por_stock_uom,
               po.supplier_name,
               po.transaction_date
        FROM `tabPurchase Order Item` poi
        INNER JOIN `tabPurchase Order` po ON po.name = poi.parent
        WHERE po.docstatus = 1
          AND poi.item_code IN %s
          AND poi.base_net_rate > 0
        ORDER BY po.transaction_date DESC, po.creation DESC
        """,
        (codigos,),
        as_dict=True,
    ):
        ic = po_row.item_code
        r_base = flt(po_row.rate_por_stock_uom)
        if r_base > 0 and (ic not in mejores_po or r_base < mejores_po[ic]["rate_base"]):
            mejores_po[ic] = {
                "rate_base": r_base,
                "proveedor": po_row.supplier_name or "",
                "fecha": str(po_row.transaction_date or ""),
            }

    resultado = {}
    for r in mr.items:
        ic = r.item_code
        if not ic or ic == "LEBAREF-CONCEPTO-LIBRE" or r.custom_lebaref_concepto:
            continue
        factor_mr = flt(r.conversion_factor) or 1.0
        it_doc = info_item.get(ic)
        costo_base = flt(it_doc.custom_lebaref_ultimo_costo) if it_doc else 0.0
        fuente = "Último costo catálogo"
        prov_ref = ""
        fecha_ref = ""

        if ic in mejores_po:
            costo_base = mejores_po[ic]["rate_base"]
            fuente = "Mejor OC histórica"
            prov_ref = mejores_po[ic]["proveedor"]
            fecha_ref = mejores_po[ic]["fecha"]
        elif costo_base <= 0 and ic in precios_lista:
            costo_base = flt(precios_lista[ic].price_list_rate)
            fuente = "Banco de precios"
            prov_ref = precios_lista[ic].supplier or ""

        if costo_base > 0:
            resultado[r.name] = {
                "ref_precio": round(costo_base * factor_mr, 2),
                "ref_fuente": fuente,
                "ref_proveedor": prov_ref,
                "ref_fecha": fecha_ref,
                "uom": r.uom or r.stock_uom or "Nos",
            }
    return resultado


@frappe.whitelist(methods=["POST"])
def gestionar_cierre_orden(orden: str, accion: str, motivo: str):
    """Finiquita el remanente de una OC parcial o la reabre usando el estado nativo de ERPNext sin inventar recepciones (COM-08)."""
    from erpnext.buying.doctype.purchase_order.purchase_order import update_status

    po = frappe.get_doc("Purchase Order", orden)
    po.check_permission("write")
    roles_usr = set(frappe.get_roles())
    if not (roles_usr & {"Purchase Manager", "System Manager"}):
        frappe.throw("Solo el Responsable de Compras o Coordinador puede finiquitar o reabrir una Orden de Compra.")

    if po.docstatus != 1:
        frappe.throw("La Orden de Compra debe estar autorizada (confirmada) para gestionar su cierre o reapertura.")

    motivo_limpio = (motivo or "").strip()
    if len(motivo_limpio) < 5:
        frappe.throw("Escribe un motivo claro (mínimo 5 caracteres) para dejar constancia en la bitácora.")

    modo = (accion or "").strip().lower()
    if modo == "cerrar":
        if po.status == "Closed":
            return {"ok": True, "status": "Closed"}
        update_status("Closed", po.name)
        po.add_comment("Info", f"🔒 Remanente de OC finiquitado / cerrado (COM-08): {motivo_limpio}")
        nuevo_status = frappe.db.get_value("Purchase Order", po.name, "status")
        return {"ok": True, "status": nuevo_status}

    if modo == "reabrir":
        if po.status != "Closed":
            frappe.throw("La Orden de Compra no se encuentra cerrada.")
        update_status("Submitted", po.name)
        po.add_comment("Info", f"🔓 Orden de Compra reabierta (COM-08): {motivo_limpio}")
        nuevo_status = frappe.db.get_value("Purchase Order", po.name, "status")
        return {"ok": True, "status": nuevo_status}

    frappe.throw("Acción inválida. Usa 'cerrar' o 'reabrir'.")


def _resolver_sku_o_concepto(token: str):
    limpio = (token or "").strip()
    if not limpio:
        return None, ""
    if frappe.db.exists("Item", limpio):
        it = frappe.db.get_value("Item", limpio, ["name", "item_name", "purchase_uom", "stock_uom"], as_dict=True)
        return it, "ID exacto"
    por_cb = frappe.db.get_value(
        "Item",
        {"custom_lebaref_codigo_barras": limpio, "disabled": 0},
        ["name", "item_name", "purchase_uom", "stock_uom"],
        as_dict=True,
    )
    if por_cb:
        return por_cb, "Clave / Cód. Barras"
    parent_bc = frappe.db.get_value("Item Barcode", {"barcode": limpio}, "parent")
    if parent_bc and frappe.db.exists("Item", parent_bc):
        it = frappe.db.get_value("Item", parent_bc, ["name", "item_name", "purchase_uom", "stock_uom"], as_dict=True)
        return it, "Código de barras"
    return None, "Concepto Libre (sin SKU)"


@frappe.whitelist(methods=["POST"])
def previsualizar_carga_masiva_solicitud(texto_csv: str):
    """Analiza texto CSV o pegado desde Excel y devuelve filas validadas y advertencias controladas (SOL-04)."""
    raw = (texto_csv or "").strip()
    if not raw:
        frappe.throw("Pega filas desde Excel o selecciona un archivo CSV con al menos una partida.")

    delimitador = "\t" if "\t" in raw.splitlines()[0] else (";" if ";" in raw.splitlines()[0] and "," not in raw.splitlines()[0] else ",")
    lector = list(csv.reader(io.StringIO(raw), delimiter=delimitador))
    if not lector:
        frappe.throw("No se encontraron filas legibles.")

    obras_validas = set(frappe.get_all("Project", pluck="name"))
    encabezado_palabras = {"codigo", "código", "clave", "sku", "concepto", "descripcion", "descripción", "articulo", "artículo"}
    inicio = 0
    primera_celda = (lector[0][0] or "").strip().lower()
    if any(p in primera_celda for p in encabezado_palabras):
        inicio = 1

    filas_ok = []
    errores = []

    for idx_f, cols in enumerate(lector[inicio:], start=inicio + 1):
        if not cols or not any((c or "").strip() for c in cols):
            continue
        col_cod = (cols[0] if len(cols) > 0 else "").strip()
        col_qty = (cols[1] if len(cols) > 1 else "1").strip()
        col_uom = (cols[2] if len(cols) > 2 else "").strip()
        col_obra = (cols[3] if len(cols) > 3 else "").strip()
        col_part = (cols[4] if len(cols) > 4 else "").strip()

        if not col_cod:
            errores.append(f"Fila {idx_f}: Falta código de artículo o descripción del concepto.")
            continue

        try:
            qty = float(col_qty.replace(",", ""))
        except ValueError:
            qty = 0.0
        if qty <= 0:
            errores.append(f"Fila {idx_f} ({col_cod}): La cantidad '{col_qty}' debe ser un número mayor que cero.")
            continue

        if col_obra and col_obra not in obras_validas:
            errores.append(f"Fila {idx_f} ({col_cod}): La obra '{col_obra}' no existe en Obras / Proyectos.")
            continue

        item_match, origen_match = _resolver_sku_o_concepto(col_cod)
        if item_match:
            uom_usar = col_uom if (col_uom and frappe.db.exists("UOM", col_uom)) else (item_match.purchase_uom or item_match.stock_uom or "Nos")
            filas_ok.append({
                "fila": idx_f,
                "es_sku": True,
                "item_code": item_match.name,
                "descripcion": item_match.item_name,
                "coincidencia": origen_match,
                "qty": qty,
                "uom": uom_usar,
                "obra": col_obra,
                "partida": col_part,
            })
        else:
            uom_libre = col_uom if (col_uom and frappe.db.exists("UOM", col_uom)) else "Nos"
            filas_ok.append({
                "fila": idx_f,
                "es_sku": False,
                "item_code": "",
                "descripcion": col_cod[:140],
                "coincidencia": origen_match,
                "qty": qty,
                "uom": uom_libre,
                "obra": col_obra,
                "partida": col_part,
            })

    return {
        "filas_validas": filas_ok,
        "errores": errores,
        "total_skus": sum(1 for x in filas_ok if x["es_sku"]),
        "total_libres": sum(1 for x in filas_ok if not x["es_sku"]),
    }


@frappe.whitelist(methods=["POST"])
def ejecutar_carga_masiva_solicitud(datos: str):
    """Crea la Solicitud de Material confirmada a partir de las filas previsualizadas de CSV/Excel (SOL-04)."""
    from lebaref_compras import operaciones

    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(d, dict):
        frappe.throw("Datos de importación inválidos.")

    prev = previsualizar_carga_masiva_solicitud(d.get("texto_csv") or "")
    if prev["errores"] and not d.get("ignorar_errores"):
        frappe.throw("Corrige primero las filas con error antes de importar:\n• " + "\n• ".join(prev["errores"][:8]))
    if not prev["filas_validas"]:
        frappe.throw("No hay partidas válidas para crear la solicitud.")

    company = frappe.get_all("Company", pluck="name")[0]
    almacen = frappe.db.get_value("Warehouse", {"company": company, "is_group": 0, "disabled": 0}, "name") or ""
    obra_def = (d.get("obra_default") or "").strip()

    rows_op = []
    for f in prev["filas_validas"]:
        obra_fila = f["obra"] or obra_def
        if f["es_sku"]:
            rows_op.append({
                "item_code": f["item_code"],
                "qty": f["qty"],
                "uom": f["uom"],
                "warehouse": almacen,
                "project": obra_fila,
                "partida": f["partida"],
            })
        else:
            rows_op.append({
                "concepto": f["descripcion"],
                "qty": f["qty"],
                "uom": f["uom"],
                "project": obra_fila,
                "partida": f["partida"],
            })

    titulo = (d.get("titulo") or "").strip() or f"Carga Rápida CSV · {today()}"
    payload = {
        "company": company,
        "schedule_date": d.get("schedule_date") or add_days(today(), 3),
        "title": titulo[:140],
        "departamento": (d.get("departamento") or "").strip(),
        "solicitante": (d.get("solicitante") or "").strip(),
        "notas": "Importada mediante Carga Rápida CSV / Excel (SOL-04).",
        "rows": rows_op,
    }
    res = operaciones.ejecutar("solicitud", json.dumps(payload, ensure_ascii=False), str(uuid.uuid4()))
    if frappe.get_meta("Material Request").has_field("custom_lebaref_nombre_solicitud"):
        frappe.db.set_value("Material Request", res["folio"], "custom_lebaref_nombre_solicitud", titulo[:140], update_modified=False)
    return res


@frappe.whitelist(methods=["GET"])
def indicadores_vehiculo(vehiculo: str):
    """Calcula km/L documentado (separando datos insuficientes), $/km, gasto por categoría y proyección de servicio (FLO-02)."""
    v = frappe.get_doc("Lebaref Vehiculo", vehiculo)
    v.check_permission("read")

    gastos = frappe.get_all(
        "Lebaref Gasto Vehiculo",
        filters={"vehiculo": v.name},
        fields=["name", "fecha", "tipo", "km", "litros", "tanque_lleno", "monto", "moneda", "proveedor", "obra"],
        order_by="km asc, fecha asc, creation asc",
    )

    por_categoria = defaultdict(float)
    total_gasto = 0.0
    cargas_combustible = []

    for g in gastos:
        monto = flt(g.monto)
        total_gasto += monto
        tipo_norm = (g.tipo or "Otros").strip()
        por_categoria[tipo_norm] += monto
        es_comb = "combust" in tipo_norm.lower() or "gasolin" in tipo_norm.lower() or "diésel" in tipo_norm.lower() or "diesel" in tipo_norm.lower() or flt(g.litros) > 0
        if es_comb and flt(g.litros) > 0 and cint(g.km) > 0:
            cargas_combustible.append(g)

    # Cálculo documentado de rendimiento km/L: requiere al menos 2 cargas con avance positivo de odómetro
    datos_suficientes_kml = False
    km_por_litro = None
    nota_kml = "Datos insuficientes: registra al menos 2 cargas de combustible con kilometraje progresivo."
    if len(cargas_combustible) >= 2:
        km_ini_c = cint(cargas_combustible[0].km)
        km_fin_c = cint(cargas_combustible[-1].km)
        litros_consumidos = sum(flt(c.litros) for c in cargas_combustible[1:])
        delta_km_comb = km_fin_c - km_ini_c
        if delta_km_comb > 0 and litros_consumidos > 0:
            datos_suficientes_kml = True
            km_por_litro = round(delta_km_comb / litros_consumidos, 2)
            nota_kml = f"{delta_km_comb:,} km entre {len(cargas_combustible)} cargas / {litros_consumidos:.1f} L"

    # Cálculo de Costo por Kilómetro ($/km)
    kms_registrados = [cint(v.km_inicial), cint(v.km_actual)] + [cint(g.km) for g in gastos if cint(g.km) > 0]
    km_min = min(k for k in kms_registrados if k >= 0) if kms_registrados else 0
    km_max = max(kms_registrados) if kms_registrados else 0
    recorrido_total = max(0, km_max - cint(v.km_inicial))
    costo_por_km = round(total_gasto / recorrido_total, 2) if recorrido_total > 0 and total_gasto > 0 else None

    # Proyección de próximo servicio y vigencia de seguro
    km_restantes_servicio = cint(v.proximo_servicio) - cint(v.km_actual)
    fecha_estimada_servicio = ""
    if gastos and len(gastos) >= 2 and km_restantes_servicio > 0:
        fechas_validas = [getdate(g.fecha) for g in gastos if g.fecha]
        if len(fechas_validas) >= 2:
            dias_span = max(1, date_diff(max(fechas_validas), min(fechas_validas)))
            km_dia = recorrido_total / dias_span if recorrido_total > 0 else 0
            if km_dia >= 1:
                fecha_estimada_servicio = str(add_days(today(), int(round(km_restantes_servicio / km_dia))))

    dias_seguro = date_diff(v.seguro, today()) if v.seguro else None

    return {
        "vehiculo": v.name,
        "placas": v.placas,
        "marca_modelo": v.marca_modelo,
        "km_inicial": cint(v.km_inicial),
        "km_actual": cint(v.km_actual),
        "recorrido_total": recorrido_total,
        "total_gasto": round(total_gasto, 2),
        "datos_suficientes_rendimiento": datos_suficientes_kml,
        "km_por_litro": km_por_litro,
        "nota_rendimiento": nota_kml,
        "costo_por_km": costo_por_km,
        "km_restantes_servicio": km_restantes_servicio,
        "proximo_servicio": cint(v.proximo_servicio),
        "fecha_estimada_servicio": fecha_estimada_servicio,
        "seguro_vence": str(v.seguro or ""),
        "dias_seguro": dias_seguro,
        "gasto_por_categoria": [
            {"categoria": k, "monto": round(val, 2)}
            for k, val in sorted(por_categoria.items(), key=lambda x: x[1], reverse=True)
        ],
    }


@frappe.whitelist(methods=["GET"])
def analitica_ejecutiva():
    """Calcula Variación de Precio de Compra (PPV), Ranking de Proveedores, Matriz Kraljic/ABC por familia y Costo por Obra (ANA-01)."""
    # 1. Variación de Precio de Compra (PPV): compara órdenes/cotizaciones contra último costo base de catálogo
    ppv_rows = frappe.db.sql(
        """
        SELECT
            poi.item_code,
            i.item_name,
            po.name AS orden,
            po.supplier_name AS proveedor,
            po.transaction_date AS fecha,
            poi.stock_qty,
            (poi.base_net_rate / NULLIF(COALESCE(poi.conversion_factor, 1), 0)) AS precio_real,
            COALESCE(i.custom_lebaref_ultimo_costo, 0) AS costo_referencia
        FROM `tabPurchase Order Item` poi
        INNER JOIN `tabPurchase Order` po ON po.name = poi.parent
        INNER JOIN `tabItem` i ON i.name = poi.item_code
        WHERE po.docstatus = 1
          AND poi.item_code != 'LEBAREF-CONCEPTO-LIBRE'
          AND COALESCE(i.custom_lebaref_ultimo_costo, 0) > 0
          AND poi.base_net_rate > 0
        ORDER BY po.transaction_date DESC
        LIMIT 80
        """,
        as_dict=True,
    )
    variaciones_ppv = []
    ahorro_neto_ppv = 0.0
    for r in ppv_rows:
        real_u = flt(r.precio_real)
        ref_u = flt(r.costo_referencia)
        diff_u = real_u - ref_u
        impacto = diff_u * flt(r.stock_qty)
        ahorro_neto_ppv -= impacto
        pct = ((real_u - ref_u) / ref_u * 100.0) if ref_u > 0 else 0.0
        variaciones_ppv.append({
            "item_code": r.item_code,
            "nombre": r.item_name,
            "orden": r.orden,
            "proveedor": r.proveedor,
            "fecha": str(r.fecha or ""),
            "cantidad": flt(r.stock_qty),
            "costo_ref": round(ref_u, 2),
            "precio_compra": round(real_u, 2),
            "pct_variacion": round(pct, 1),
            "impacto_total": round(impacto, 2),
        })

    # 2. Ranking de Proveedores (suma OCs activas + volumen histórico facturado en Lebaref CFDI)
    ranking_map = defaultdict(lambda: {"proveedor": "", "especialidad": "General", "monto_ocs": 0.0, "num_ocs": 0, "monto_cfdi": 0.0, "num_cfdis": 0})
    for s in frappe.get_all("Supplier", fields=["name", "supplier_name", "custom_especialidad_cotizar"]):
        ranking_map[s.name]["proveedor"] = s.supplier_name or s.name
        ranking_map[s.name]["especialidad"] = s.custom_especialidad_cotizar or "General"

    for po_agg in frappe.db.sql(
        """
        SELECT supplier, supplier_name, COUNT(name) AS n_oc, SUM(base_grand_total) AS total_oc
        FROM `tabPurchase Order`
        WHERE docstatus = 1
        GROUP BY supplier
        """,
        as_dict=True,
    ):
        ent = ranking_map[po_agg.supplier]
        ent["proveedor"] = ent["proveedor"] or po_agg.supplier_name or po_agg.supplier
        ent["num_ocs"] = cint(po_agg.n_oc)
        ent["monto_ocs"] = flt(po_agg.total_oc)

    if frappe.db.exists("DocType", "Lebaref CFDI"):
        for cf_agg in frappe.db.sql(
            """
            SELECT proveedor, COUNT(name) AS n_cfdi, SUM(total) AS total_cfdi
            FROM `tabLebaref CFDI`
            WHERE COALESCE(proveedor, '') <> ''
            GROUP BY proveedor
            """,
            as_dict=True,
        ):
            ent = ranking_map[cf_agg.proveedor]
            ent["proveedor"] = ent["proveedor"] or cf_agg.proveedor
            ent["num_cfdis"] = cint(cf_agg.n_cfdi)
            ent["monto_cfdi"] = flt(cf_agg.total_cfdi)

    ranking_proveedores = sorted(
        [
            {
                **v,
                "volumen_total": round(v["monto_ocs"] + v["monto_cfdi"], 2),
            }
            for v in ranking_map.values()
            if (v["num_ocs"] > 0 or v["num_cfdis"] > 0)
        ],
        key=lambda x: x["volumen_total"],
        reverse=True,
    )[:25]

    # 3. Clasificación ABC / Kraljic por Categoría / Familia de Catálogo (valor inventario + rotación/proveedores)
    familias_rows = frappe.db.sql(
        """
        SELECT
            i.item_group AS familia,
            COUNT(DISTINCT i.name) AS skus,
            COUNT(DISTINCT isup.supplier) AS num_proveedores,
            SUM(COALESCE(b_agg.qty_total, 0) * COALESCE(i.custom_lebaref_ultimo_costo, 0) / GREATEST(COALESCE(s_agg.n_sup, 1), 1)) AS valor_stock
        FROM `tabItem` i
        LEFT JOIN (
            SELECT item_code, SUM(actual_qty) AS qty_total
            FROM `tabBin`
            GROUP BY item_code
        ) b_agg ON b_agg.item_code = i.name
        LEFT JOIN (
            SELECT parent, COUNT(*) AS n_sup
            FROM `tabItem Supplier`
            GROUP BY parent
        ) s_agg ON s_agg.parent = i.name
        LEFT JOIN `tabItem Supplier` isup ON isup.parent = i.name
        WHERE i.disabled = 0 AND i.is_stock_item = 1
        GROUP BY i.item_group
        ORDER BY valor_stock DESC, skus DESC
        """,
        as_dict=True,
    )
    mediana_valor = sorted([flt(x.valor_stock) for x in familias_rows])[len(familias_rows) // 2] if familias_rows else 0.0
    matriz_kraljic = []
    for f in familias_rows:
        val = flt(f.valor_stock)
        n_prov = cint(f.num_proveedores)
        alto_impacto = val > max(mediana_valor, 1000.0)
        riesgo_suministro = n_prov <= 2
        if alto_impacto and riesgo_suministro:
            cuadrante = "Estratégico / Crítico"
        elif alto_impacto and not riesgo_suministro:
            cuadrante = "Apalancamiento (Competir precio)"
        elif not alto_impacto and riesgo_suministro:
            cuadrante = "Cuello de Botella (Asegurar stock)"
        else:
            cuadrante = "Rutinario / No crítico"
        matriz_kraljic.append({
            "familia": f.familia or "Sin familia",
            "skus": cint(f.skus),
            "proveedores": n_prov,
            "valor_stock": round(val, 2),
            "cuadrante": cuadrante,
        })

    # 4. Costo Acumulado por Obra (OCs + Consumo Real en Obra)
    obras_costo = frappe.db.sql(
        """
        SELECT t.obra, t.project_name, t.cliente, t.compras_oc, t.consumo_almacen
        FROM (
            SELECT
                p.name AS obra,
                p.project_name,
                COALESCE(p.customer, '—') AS cliente,
                COALESCE((
                    SELECT SUM(poi.base_net_amount)
                    FROM `tabPurchase Order Item` poi
                    INNER JOIN `tabPurchase Order` po ON po.name = poi.parent
                    WHERE poi.project = p.name AND po.docstatus = 1
                ), 0) AS compras_oc,
                COALESCE((
                    SELECT SUM(sed.amount)
                    FROM `tabStock Entry Detail` sed
                    INNER JOIN `tabStock Entry` se ON se.name = sed.parent
                    WHERE (sed.project = p.name OR se.project = p.name)
                      AND se.docstatus = 1
                      AND se.purpose = 'Material Issue'
                ), 0) AS consumo_almacen
            FROM `tabProject` p
        ) t
        ORDER BY (t.compras_oc + t.consumo_almacen) DESC, t.obra ASC
        LIMIT 25
        """,
        as_dict=True,
    )

    return {
        "resumen": {
            "ahorro_neto_ppv": round(ahorro_neto_ppv, 2),
            "partidas_evaluadas_ppv": len(variaciones_ppv),
            "proveedores_con_movimiento": len(ranking_proveedores),
            "familias_analizadas": len(matriz_kraljic),
        },
        "ppv": variaciones_ppv[:40],
        "ranking_proveedores": ranking_proveedores,
        "kraljic": matriz_kraljic,
        "obras": [
            {
                "obra": o.obra,
                "nombre": o.project_name or o.obra,
                "cliente": o.cliente,
                "compras_oc": round(flt(o.compras_oc), 2),
                "consumo_almacen": round(flt(o.consumo_almacen), 2),
            }
            for o in obras_costo
        ],
    }
