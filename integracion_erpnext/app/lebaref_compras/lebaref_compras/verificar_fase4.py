"""Suite transaccional de verificación de Fase 4 (CAT-01, CAT-02, MIG-02). Revierte todos sus datos al finalizar."""
import json
import uuid
import frappe
from frappe.utils import add_days, flt, today
from lebaref_compras import analitica_operativa, catalogo_gobierno, operaciones, sheets_proveedores, ux_lebaref


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
        obra_prueba = seed.items[0].project
        sku_base = seed.items[0].item_code

        # 1. CAT-01: Unificación de familias, multigerencia, plantillas técnicas y bandera recotizar_siempre
        assert (
            catalogo_gobierno.normalizar_nombre_familia("TUBERÍA Y CONEXIONES PVC")
            == catalogo_gobierno.normalizar_nombre_familia("Tubería y Conexiones PVC")
            == "Tubería y Conexiones PVC"
        )
        assert (
            catalogo_gobierno.normalizar_nombre_familia("VÁLVULAS Y ACCESORIOS HVAC")
            == catalogo_gobierno.normalizar_nombre_familia("Válvulas y Accesorios HVAC")
            == "Válvulas y Accesorios HVAC"
        )

        # Crear un artículo temporal sin compras previas para probar CAT-01, CAT-02 y MIG-02 limpiamente
        grupo_it = frappe.db.get_value("Item", sku_base, "item_group") or "Products"
        sku_f4 = "SKU-FASE4-" + uuid.uuid4().hex[:6].upper()
        frappe.get_doc({
            "doctype": "Item",
            "item_code": sku_f4,
            "item_name": "Contactor Trifásico Prueba Fase 4",
            "item_group": grupo_it,
            "stock_uom": "Pieza",
            "is_stock_item": 1,
            "is_purchase_item": 1,
        }).insert(ignore_permissions=True)

        ficha_act = ux_lebaref.actualizar_ficha_producto(
            sku_f4,
            json.dumps({
                "familia": "CONTACTORES Y RELEVADORES",
                "gerencias": ["HVAC", "Eléctrico", "Mantenimiento Industrial"],
                "recotizar_siempre": 1,
            }),
        )
        assert ficha_act["familia"] == "Contactores y Relevadores"
        assert ficha_act["gerencias_lista"] == ["HVAC", "Eléctrico", "Mantenimiento Industrial"]
        assert ficha_act["recotizar_siempre"] == 1
        assert isinstance(ficha_act.get("plantilla_sugerida"), dict) and ficha_act["plantilla_sugerida"].get("modelo")

        # 2. CAT-02: Propuesta por los 4 perfiles + Dictamen y Alcance (Toda la empresa vs Proyecto específico)
        texto_alias_global = f"Arrancador magnético 3P 220V esp {uuid.uuid4().hex[:5]}"
        texto_equiv_proy = f"Relevador sustituto exclusivo obra {uuid.uuid4().hex[:5]}"
        texto_distinto = f"Equipo distinto no agrupar {uuid.uuid4().hex[:5]}"

        if frappe.db.exists("User", "juan.cetina@lebaref.com"):
            frappe.set_user("juan.cetina@lebaref.com")
        prop_juan = catalogo_gobierno.proponer_alias_o_equivalencia(json.dumps({
            "texto_original": texto_alias_global,
            "origen": "Solicitud de Ingeniero",
            "gerencia": "Eléctrico",
            "articulo_propuesto": sku_f4,
            "dictamen": "Pendiente",
        }))
        assert prop_juan["ok"] and prop_juan["dictamen"] == "Pendiente"

        if frappe.db.exists("User", "airan.echeverria@lebaref.com"):
            frappe.set_user("airan.echeverria@lebaref.com")
        prop_airan = catalogo_gobierno.proponer_alias_o_equivalencia(json.dumps({
            "texto_original": texto_equiv_proy,
            "origen": "Revisión Manual",
            "gerencia": "HVAC",
            "articulo_propuesto": sku_f4,
            "dictamen": "Pendiente",
        }))
        bandeja = catalogo_gobierno.listar_bandeja_equivalencias(estado="pendientes")
        assert bandeja["resumen"]["pendientes"] >= 2

        frappe.set_user(
            "oscar.gomez@lebaref.com"
            if frappe.db.exists("User", "oscar.gomez@lebaref.com")
            else "Administrator"
        )
        # Antes de aprobar, debe seguir tratándose como Concepto Libre
        it_pre, _ = analitica_operativa._resolver_sku_o_concepto(texto_alias_global)
        assert it_pre is None, "Un candidato Pendiente aún no debe agruparse automáticamente"

        # Aprobar globalmente el alias de Juan y aprobar SOLO para obra_prueba la equivalencia de Airan
        catalogo_gobierno.dictaminar_equivalencia(json.dumps({
            "name": prop_juan["folio"],
            "articulo_propuesto": sku_f4,
            "dictamen": "Exacto (Alias directo)",
            "alcance": "Toda la empresa",
        }))
        catalogo_gobierno.dictaminar_equivalencia(json.dumps({
            "name": prop_airan["folio"],
            "articulo_propuesto": sku_f4,
            "dictamen": "Equivalente aprobado",
            "alcance": "Solo Cliente / Proyecto específico",
            "proyecto": obra_prueba,
        }))
        catalogo_gobierno.proponer_alias_o_equivalencia(json.dumps({
            "texto_original": texto_distinto,
            "articulo_propuesto": sku_f4,
            "dictamen": "Distinto (No agrupar)",
            "alcance": "Toda la empresa",
        }))

        it_glob, orig_glob = analitica_operativa._resolver_sku_o_concepto(texto_alias_global)
        assert it_glob and it_glob.name == sku_f4 and orig_glob == "Alias directo"

        it_proy_ok, orig_proy = analitica_operativa._resolver_sku_o_concepto(texto_equiv_proy, proyecto=obra_prueba)
        assert it_proy_ok and it_proy_ok.name == sku_f4 and orig_proy == "Equivalencia proyecto"

        it_proy_fuera, _ = analitica_operativa._resolver_sku_o_concepto(texto_equiv_proy, proyecto="")
        assert it_proy_fuera is None, "Equivalencia exclusiva de un proyecto no debe aplicarse fuera de ese proyecto"

        it_rech, _ = analitica_operativa._resolver_sku_o_concepto(texto_distinto, proyecto=obra_prueba)
        assert it_rech is None, "Dictamen Distinto no debe agruparse nunca"

        # Comprobar preservación del texto original al previsualizar solicitud
        prev_sol = analitica_operativa.previsualizar_carga_masiva_solicitud(
            texto_csv=f"Codigo_o_Concepto,Cantidad,Unidad,Obra,Partida\n{texto_alias_global},3,Pieza,{obra_prueba},Control Eléctrico"
        )
        assert prev_sol["total_skus"] == 1
        fila_sol = prev_sol["filas_validas"][0]
        assert fila_sol["item_code"] == sku_f4 and "Orig:" in fila_sol["descripcion"]

        # 3. MIG-02: Importador Asistido del Excel de 1 Año de Cotizaciones (MXN + USD/DOF + Idempotencia + Sin compras)
        po_antes = frappe.db.count("Purchase Order")
        sle_antes = frappe.db.count("Stock Ledger Entry")

        encabezados = ",".join([
            "Fecha_Cotizacion", "Proveedor", "Descripcion_Proveedor_o_Solicitada", "Cantidad_Cotizada",
            "Unidad", "Moneda", "Precio_Unitario_Sin_IVA", "Tipo_Cambio_DOF", "Codigo_Interno_Lebaref",
            "Clave_o_Modelo_Proveedor", "Marca", "Folio_Cotizacion_Proveedor", "Flete_Sin_IVA",
            "Modalidad_Entrega", "Dias_Entrega_Promesa", "Vigencia_Dias", "Condicion_Pago",
            "Gerencia_o_Area", "Cliente_u_Obra_Referencia", "Se_Compro_Realmente", "Notas",
        ])

        # Validar rechazo de USD sin TC DOF
        csv_usd_invalido = f"{encabezados}\n2026-03-10,PROV ELECTRICO SUR,Contactor importado s/TC,2,Pieza,USD,95.00,,,,,,,,,,,,,,,"
        prev_err = catalogo_gobierno.previsualizar_importacion_cotizaciones_historicas(texto_csv=csv_usd_invalido)
        assert len(prev_err["errores"]) == 1 and "DOF" in prev_err["errores"][0]

        desc_prov_nuevo_alias = f"Contactor Schneider LC1D18 220V hist {uuid.uuid4().hex[:5]}"
        csv_valido = "\n".join([
            encabezados,
            "2026-02-18,ELÉCTRICA SURESTE,[EJEMPLO - BORRAR] Fila ejemplo plantilla,10,Caja,MXN,1385.00,,,,,,,,,,,,,,Borrar",
            f"2026-01-20,PROVEEDORA CONTROL SURESTE,{desc_prov_nuevo_alias},2,Pieza,MXN,840.00,,{sku_f4},LC1D18-M7,Schneider,COT-H1,100.00,Flete_Proveedor,3,15,Contado,Eléctrico,{obra_prueba},NO,Cotizacion historica MXN",
            f"2026-02-15,IMPORTADORA HVAC USA,Valvula expansion termostatica hist {uuid.uuid4().hex[:4]},2,Pieza,USD,120.00,20.5000,,TXV-05,Danfoss,COT-H2,20.00,Flete_Proveedor,7,10,Anticipo 50%,HVAC,,NO,Cotizacion historica USD",
        ])
        prev_ok = catalogo_gobierno.previsualizar_importacion_cotizaciones_historicas(texto_csv=csv_valido)
        assert prev_ok["resumen"]["ejemplos_ignorados"] == 1
        assert prev_ok["resumen"]["listas_para_importar"] == 2
        assert prev_ok["resumen"]["vinculadas_a_sku"] == 1
        assert abs(flt(prev_ok["filas"][0]["precio_unitario_mxn"]) - 840.0) < 0.01
        assert abs(flt(prev_ok["filas"][0]["precio_sitio_mxn"]) - 890.0) < 0.01
        assert abs(flt(prev_ok["filas"][1]["precio_unitario_mxn"]) - 2460.0) < 0.01
        assert abs(flt(prev_ok["filas"][1]["precio_sitio_mxn"]) - 2665.0) < 0.01

        imp_1 = catalogo_gobierno.ejecutar_importacion_cotizaciones_historicas(json.dumps({
            "filas": prev_ok["filas"],
            "registrar_alias_vinculados": 1,
            "enviar_sin_sku_a_bandeja": 1,
        }))
        assert imp_1["importadas"] == 2 and imp_1["alias_creados"] == 1 and imp_1["propuestas_bandeja"] == 1

        # Reimportar el mismo lote: 0 duplicados
        imp_2 = catalogo_gobierno.ejecutar_importacion_cotizaciones_historicas(json.dumps({
            "filas": prev_ok["filas"],
        }))
        assert imp_2["importadas"] == 0 and imp_2["omitidas"] == 2

        # Cero afectación a compras reales ni a inventario
        assert frappe.db.count("Purchase Order") == po_antes
        assert frappe.db.count("Stock Ledger Entry") == sle_antes

        # El alias aprendido en la importación histórica ahora resuelve automáticamente al SKU
        it_learned, _ = analitica_operativa._resolver_sku_o_concepto(desc_prov_nuevo_alias)
        assert it_learned and it_learned.name == sku_f4

        # Visibilidad en Archivo Histórico y en Referencias Históricas de Solicitud
        arch = sheets_proveedores.archivo_historico()
        assert any(p.get("concepto") == desc_prov_nuevo_alias and p.get("es_cotizacion_historica") == 1 for p in arch["precios"])

        mr_ref_folio = operaciones.ejecutar(
            "solicitud",
            {
                "company": company,
                "schedule_date": add_days(today(), 7),
                "title": "Solicitud Ref Historica Fase 4",
                "rows": [{
                    "item_code": sku_f4,
                    "qty": "2",
                    "warehouse": seed.items[0].warehouse,
                    "project": obra_prueba,
                }],
            },
            str(uuid.uuid4()),
        )["folio"]
        refs = analitica_operativa.referencias_historicas_solicitud(mr_ref_folio)
        mri_id = frappe.get_doc("Material Request", mr_ref_folio).items[0].name
        assert mri_id in refs and abs(flt(refs[mri_id]["ref_precio"]) - 840.0) < 0.01
        assert "histórica" in refs[mri_id]["ref_fuente"]

        return {
            "correcto": True,
            "pruebas_fase4": [
                "CAT-01 FamiliasNormalizadas+Multigerencia+RecotizarSiempre+Plantillas",
                "CAT-02 Bandeja4Usuarios+AlcanceEmpresaVsProyecto+TextoOriginal",
                "MIG-02 ImportadorExcel1Año+USD_DOF+Idempotente+SinCompras",
            ],
        }
    finally:
        frappe.db.rollback()
        frappe.set_user(usuario_orig)
