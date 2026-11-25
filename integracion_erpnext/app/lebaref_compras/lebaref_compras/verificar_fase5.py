"""Suite transaccional de verificación de Fase 5 (PRE-01, PRE-02, PRE-03). Revierte todos sus datos al finalizar."""
import json
import uuid

import frappe
from frappe.utils import add_days, flt, today

from lebaref_compras import comparativa, motor_precios, operaciones, ux_lebaref


def verificar():
    if frappe.local.site != "lebaref-pruebas.local":
        raise RuntimeError("Solo se permite en el entorno de pruebas.")
    usuario_orig = frappe.session.user
    try:
        user_oscar = "oscar.gomez@lebaref.com" if frappe.db.exists("User", "oscar.gomez@lebaref.com") else "Administrator"
        frappe.set_user(user_oscar)

        seed = frappe.get_doc("Material Request", "MAT-MR-2026-00001")
        company = seed.company
        item_seed = seed.items[0]
        grupo_it = frappe.db.get_value("Item", item_seed.item_code, "item_group") or "Products"
        sup_group = frappe.db.get_value("Supplier Group", {"is_group": 0})

        sup1 = frappe.get_doc({
            "doctype": "Supplier",
            "supplier_name": "Proveedor Motor A " + uuid.uuid4().hex[:5],
            "supplier_group": sup_group,
            "supplier_type": "Company",
            "tax_id": "MOT010101AAA",
        }).insert(ignore_permissions=True)
        sup2 = frappe.get_doc({
            "doctype": "Supplier",
            "supplier_name": "Proveedor Motor B " + uuid.uuid4().hex[:5],
            "supplier_group": sup_group,
            "supplier_type": "Company",
            "tax_id": "MOT020202BBB",
        }).insert(ignore_permissions=True)

        sku = "SKU-FASE5-" + uuid.uuid4().hex[:6].upper()
        frappe.get_doc({
            "doctype": "Item",
            "item_code": sku,
            "item_name": "Minisplit Inverter Prueba Motor Precios",
            "item_group": grupo_it,
            "stock_uom": "Pieza",
            "is_stock_item": 1,
            "is_purchase_item": 1,
            "custom_lebaref_familia": "Equipos HVAC",
            "custom_lebaref_gerencias": "HVAC",
            "custom_lebaref_recotizar_siempre": 0,
        }).insert(ignore_permissions=True)

        # 1. PRE-01: Cargar muestra mixta (Cotizaciones históricas en MXN y USD+DOF + Compra real en OC)
        for dias_atras, moneda, precio, tc, prov in (
            (-60, "MXN", 1000.0, 1.0, sup1.supplier_name),
            (-35, "USD", 51.0, 20.0, sup2.supplier_name),  # 1,020.00 MXN
            (-15, "MXN", 1030.0, 1.0, sup1.supplier_name),
        ):
            precio_mxn = precio * tc
            frappe.get_doc({
                "doctype": "Lebaref Cotizacion Historica",
                "fecha_cotizacion": add_days(today(), dias_atras),
                "proveedor_texto": prov,
                "proveedor": sup1.name if prov == sup1.supplier_name else sup2.name,
                "descripcion_original": "Minisplit Inverter Prueba Motor Precios",
                "item_code": sku,
                "cantidad": 2,
                "unidad": "Pieza",
                "moneda": moneda,
                "precio_unitario_sin_iva": precio,
                "tc_dof": tc,
                "precio_unitario_mxn": precio_mxn,
                "precio_sitio_mxn": precio_mxn,
                "gerencia": "HVAC",
                "huella_fila": uuid.uuid4().hex,
            }).insert(ignore_permissions=True)

        # Orden de compra real confirmada hace 5 días a $1,040.00 MXN
        po_hist = frappe.get_doc({
            "doctype": "Purchase Order",
            "company": company,
            "supplier": sup1.name,
            "transaction_date": add_days(today(), -5),
            "schedule_date": today(),
            "currency": "MXN",
            "items": [{
                "item_code": sku,
                "qty": 2,
                "uom": "Pieza",
                "stock_uom": "Pieza",
                "conversion_factor": 1.0,
                "rate": 1040.0,
                "warehouse": item_seed.warehouse,
                "project": item_seed.project,
                "schedule_date": today(),
            }],
        }).insert(ignore_permissions=True)
        po_hist.submit()

        est_oscar = motor_precios.obtener_estimacion_privada_articulo(sku)
        assert est_oscar is not None and est_oscar["tiene_historial"] == 1
        assert est_oscar["muestra_total"] == 4
        assert est_oscar["muestra_compras"] == 1 and est_oscar["muestra_cotizaciones"] == 3
        assert 1000.0 <= est_oscar["precio_estimado_mxn"] <= 1045.0
        assert est_oscar["indice_confianza"] >= 80 and est_oscar["semaforo"] == "Alta"

        # Regla dura: 'Exige recotizar siempre' fija confianza en 0%
        frappe.db.set_value("Item", sku, "custom_lebaref_recotizar_siempre", 1, update_modified=False)
        est_forzado = motor_precios.obtener_estimacion_privada_articulo(sku)
        assert est_forzado["indice_confianza"] == 0 and est_forzado["semaforo"] == "Recotizar siempre"
        frappe.db.set_value("Item", sku, "custom_lebaref_recotizar_siempre", 0, update_modified=False)

        # Calibración dinámica de umbrales por Oscar
        motor_precios.guardar_configuracion_motor(json.dumps({
            "umbral_alta": 98,
            "umbral_media": 50,
            "dias_vigencia_optima": 45,
            "dias_caducidad_maxima": 365,
            "peso_compra_real": 1.5,
            "peso_cotizacion": 1.0,
            "cv_maximo_estable": 12.0,
        }))
        est_recal = motor_precios.obtener_estimacion_privada_articulo(sku)
        assert est_recal["semaforo"] in ("Media", "Alta")
        motor_precios.guardar_configuracion_motor(json.dumps(motor_precios.DEFAULTS_CONFIG_MOTOR))

        # 2. PRE-02: Triaje Gerencial + Invisibilidad total para Juan, Salvia y Airan + Modo Sombra
        mr_folio = operaciones.ejecutar("solicitud", {
            "company": company,
            "schedule_date": add_days(today(), 5),
            "title": "Solicitud Piloto Fase 5",
            "rows": [{
                "item_code": sku,
                "qty": "3",
                "warehouse": item_seed.warehouse,
                "project": item_seed.project,
                "custom_lebaref_partida": "PARTIDA HVAC",
            }],
        }, str(uuid.uuid4()))["folio"]
        mr_doc = frappe.get_doc("Material Request", mr_folio)
        mr_item_id = mr_doc.items[0].name

        ficha_sol_oscar = ux_lebaref.ficha_solicitud(mr_folio)
        assert ficha_sol_oscar["es_motor_oscar"] == 1
        assert ficha_sol_oscar["partidas"][0]["estimacion_oscar"]["indice_confianza"] >= 80

        # Oscar marca en 1 clic "Validado por Gerencia — No recotizar"
        res_triaje = motor_precios.marcar_validado_no_recotizar(mr_folio, mr_item_id, 1)
        assert res_triaje["ok"] and res_triaje["no_recotizar"] == 1

        # Verificar aislamiento estricto en los 3 usuarios operativos (Juan, Salvia y Airan)
        for correo_aux in ("juan.cetina@lebaref.com", "salvia.sosa@lebaref.com", "airan.echeverria@lebaref.com"):
            if not frappe.db.exists("User", correo_aux):
                continue
            frappe.set_user(correo_aux)
            assert not motor_precios.es_usuario_motor_precios()
            assert motor_precios.estado_acceso_motor()["autorizado"] == 0

            fp_aux = ux_lebaref.ficha_producto(sku)
            assert fp_aux.get("estimacion_oscar") is None, f"{correo_aux} no debe recibir estimacion_oscar en ficha_producto"

            fs_aux = ux_lebaref.ficha_solicitud(mr_folio)
            assert fs_aux["es_motor_oscar"] == 0
            assert "estimacion_oscar" not in fs_aux["partidas"][0], f"{correo_aux} no debe ver estimado en Solicitud"
            # Pero SÍ debe ver operativamente que Gerencia validó la partida para no recotizar
            assert fs_aux["partidas"][0]["no_recotizar"] == 1
            assert fs_aux["partidas"][0]["no_recotizar_etiqueta"] == "Validado por Gerencia — No recotizar"

            comp_aux = comparativa.consultar(mr_folio)
            assert comp_aux["es_motor_oscar"] == 0
            assert "estimacion_oscar" not in comp_aux["renglones"][0]
            assert comp_aux["renglones"][0]["no_recotizar"] == 1

            esq_aux = ux_lebaref.esquema_cotizacion_directa(mr_folio)
            assert esq_aux["partidas"][0]["no_recotizar"] == 1

            for fn_priv, kwargs_priv in (
                (motor_precios.tablero_confianza_precios, {}),
                (motor_precios.detalle_serie_articulo, {"item_code": sku}),
                (motor_precios.marcar_validado_no_recotizar, {"solicitud": mr_folio, "mr_item": mr_item_id, "validar": 0}),
                (motor_precios.guardar_configuracion_motor, {"datos": "{}"}),
            ):
                bloqueado = False
                try:
                    fn_priv(**kwargs_priv)
                except frappe.PermissionError:
                    bloqueado = True
                assert bloqueado, f"{correo_aux} debe ser bloqueado en {fn_priv.__name__}"

        # Modo Sombra: Juan captura una nueva cotización real en USD ($52.50 USD * TC DOF 20.0 = $1,050.00 MXN)
        if frappe.db.exists("User", "juan.cetina@lebaref.com"):
            frappe.set_user("juan.cetina@lebaref.com")
        sq_res = ux_lebaref.registrar_cotizacion_directa(mr_folio, json.dumps({
            "supplier": sup2.name,
            "currency": "USD",
            "tc_dof": 20.0,
            "fecha_dof": today(),
            "rows": [{"mr_item": mr_item_id, "rate": 52.5, "lead_time_days": 3}],
        }))
        sq_folio = sq_res["folio"]
        if frappe.session.user == "juan.cetina@lebaref.com":
            assert not frappe.has_permission("Lebaref Evaluacion Sombra", "read")

        # 3. PRE-03: Oscar consulta el Tablero de Confianza, Modo Sombra y la Serie Tiempo-Precio
        frappe.set_user(user_oscar)
        tablero = motor_precios.tablero_confianza_precios(busqueda=sku, nivel="Todos")
        assert tablero["total_filtrados"] == 1
        assert tablero["sombra"]["total_evaluaciones"] >= 1

        reg_sombra = next((s for s in tablero["sombra"]["registros"] if s["cotizacion"] == sq_folio), None)
        assert reg_sombra is not None, "Modo Sombra debe registrar automáticamente la comparación al confirmar cotización"
        assert abs(flt(reg_sombra["real_mxn"]) - 1050.0) < 0.01
        assert abs(flt(reg_sombra["estimado_previo_mxn"]) - flt(est_oscar["precio_estimado_mxn"])) < 0.05
        assert flt(reg_sombra["precision_pct"]) >= 95.0 and reg_sombra["acierto_10pct"] == 1

        serie = motor_precios.detalle_serie_articulo(sku)
        assert len(serie["puntos"]) == 5  # 3 cotizaciones históricas + 1 OC real + 1 cotización ERP nueva
        tipos_serie = {p["serie"] for p in serie["puntos"]}
        assert tipos_serie == {"cotizacion", "compra_real"}
        assert all(p["timestamp_ms"] > 0 and p["precio_mxn"] > 0 for p in serie["puntos"])
        assert len(serie["sombra_item"]) == 1

        out = {
            "correcto": True,
            "pruebas_fase5": [
                "PRE-01 PrecioEstimadoMXN+USD_DOF+Volatilidad+IndiceConfianza+Recotizar0%+Umbrales",
                "PRE-02 ExclusivoOscar+InvisibleAuxiliares+Triaje1ClicNoRecotizar+ModoSombraAutomatico",
                "PRE-03 TableroConfianza+SerieTiempoPrecioComprasVsCotizaciones+TablaDocumental",
            ],
        }
        print(json.dumps(out))
        return out
    finally:
        frappe.set_user(usuario_orig)
        frappe.db.rollback()
