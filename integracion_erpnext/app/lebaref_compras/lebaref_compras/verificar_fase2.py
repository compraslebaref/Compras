"""Suite transaccional de verificación de Fase 2 (FLU-01 a FLU-05). Revierte todos sus datos al finalizar."""
import base64
import json
import uuid
import frappe
from frappe.utils import add_days, flt, today
from lebaref_compras import comparativa, flujo_operativo, operaciones, ux_lebaref


def verificar():
    if frappe.local.site != "lebaref-pruebas.local":
        raise RuntimeError("Solo se permite en el entorno de pruebas.")
    usuario_orig = frappe.session.user
    try:
        frappe.set_user("oscar.gomez@lebaref.com" if frappe.db.exists("User", "oscar.gomez@lebaref.com") else "Administrator")
        seed = frappe.get_doc("Material Request", "MAT-MR-2026-00001")
        company = seed.company
        item_base = seed.items[0]

        sup_group = frappe.db.get_value("Supplier Group", {"is_group": 0})
        sup = frappe.get_doc({
            "doctype": "Supplier",
            "supplier_name": "Proveedor Fase2 " + uuid.uuid4().hex[:6],
            "supplier_group": sup_group,
            "supplier_type": "Company",
            "tax_id": "FAS010101AAA",
        }).insert(ignore_permissions=True)

        # Solicitud 1
        mr1_folio = operaciones.ejecutar("solicitud", {
            "company": company,
            "schedule_date": add_days(today(), 5),
            "title": "Solicitud Fase 2 USD+DOF",
            "rows": [{
                "item_code": item_base.item_code,
                "qty": "4",
                "warehouse": item_base.warehouse,
                "project": item_base.project,
                "custom_lebaref_partida": "HVAC",
            }],
        }, str(uuid.uuid4()))["folio"]
        mr1 = frappe.get_doc("Material Request", mr1_folio)

        # FLU-01: USD sin TC DOF debe rechazarse
        rechazado_usd = False
        try:
            ux_lebaref.registrar_cotizacion_directa(mr1.name, json.dumps({
                "supplier": sup.name,
                "currency": "USD",
                "tc_dof": 0,
                "rows": [{"mr_item": mr1.items[0].name, "rate": 50, "lead_time_days": 3}],
            }))
        except frappe.ValidationError:
            rechazado_usd = True
        assert rechazado_usd, "Cotización en USD sin TC DOF debe ser rechazada"

        # FLU-01: USD con TC DOF 20.5000 + flete separado 80 USD
        sq1_res = ux_lebaref.registrar_cotizacion_directa(mr1.name, json.dumps({
            "supplier": sup.name,
            "currency": "USD",
            "tc_dof": 20.5,
            "fecha_dof": today(),
            "modalidad_entrega": "En flete (proveedor entrega)",
            "dias_entrega_promesa": 4,
            "flete_sin_iva": 80.0,
            "rows": [{"mr_item": mr1.items[0].name, "rate": 100.0, "lead_time_days": 4}],
        }))
        sq1 = frappe.get_doc("Supplier Quotation", sq1_res["folio"])
        assert sq1.currency == "USD" and flt(sq1.custom_lebaref_tc_dof, 4) == 20.5
        # Precio limpio MXN = 100 * 20.5 = 2050; flete unit = (80 / 4) * 20.5 = 410 -> sitio = 2460
        assert abs(flt(sq1.items[0].custom_lebaref_precio_limpio_mxn) - 2050.0) < 0.01
        assert abs(flt(sq1.items[0].custom_lebaref_precio_sitio_mxn) - 2460.0) < 0.01

        comp = comparativa.consultar(mr1.name)
        opt = comp["renglones"][0]["opciones"][0]
        assert opt["moneda"] == "USD" and abs(opt["precio_mxn"] - 2050.0) < 0.01

        # FLU-05: Solicitud 2 y consolidación de 2 cotizaciones en una sola OC
        mr2_folio = operaciones.ejecutar("solicitud", {
            "company": company,
            "schedule_date": add_days(today(), 5),
            "title": "Solicitud 2 Consolidada",
            "rows": [{
                "item_code": item_base.item_code,
                "qty": "2",
                "warehouse": item_base.warehouse,
                "project": item_base.project,
                "custom_lebaref_partida": "Eléctrico",
            }],
        }, str(uuid.uuid4()))["folio"]
        mr2 = frappe.get_doc("Material Request", mr2_folio)
        sq2_res = ux_lebaref.registrar_cotizacion_directa(mr2.name, json.dumps({
            "supplier": sup.name,
            "currency": "USD",
            "tc_dof": 20.5,
            "fecha_dof": today(),
            "rows": [{"mr_item": mr2.items[0].name, "rate": 95.0, "lead_time_days": 2}],
        }))

        cons = flujo_operativo.consolidar_cotizaciones_en_oc(json.dumps([sq1.name, sq2_res["folio"]]))
        po = frappe.get_doc("Purchase Order", cons["folio"])
        assert len(po.items) == 2 and set(cons["solicitudes"]) == {mr1.name, mr2.name}
        assert po.currency == "USD" and flt(po.custom_lebaref_tc_dof, 4) == 20.5

        # FLU-02: Expediente fiscal incompleto genera aviso, luego se sube CSF y Carátula con PDF válido
        est1 = flujo_operativo.actualizar_estado_pago_logistica("Purchase Order", po.name, "Esperando pago Dirección")
        assert "Constancia" in est1["aviso_fiscal"]
        pdf_minimo = (
            b"%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n"
            b"2 0 obj<</Type/Pages/Count 1/Kids[3 0 R]>>endobj\n"
            b"3 0 obj<</Type/Page/MediaBox[0 0 200 200]/Parent 2 0 R>>endobj\n"
            b"xref\n0 4\n0000000000 65535 f \n0000000009 00000 n \n0000000052 00000 n \n0000000101 00000 n \n"
            b"trailer<</Size 4/Root 1 0 R>>\nstartxref\n164\n%%EOF\n"
        )
        dummy_b64 = base64.b64encode(pdf_minimo).decode("ascii")
        flujo_operativo.subir_documento_fiscal_proveedor(sup.name, "csf", "csf.pdf", dummy_b64, "012345678901234567")
        ef_ok = flujo_operativo.subir_documento_fiscal_proveedor(sup.name, "caratula", "banco.pdf", dummy_b64)
        assert ef_ok["expediente_completo"] is True

        # Autorizar OC y probar FLU-03a (Entrega dividida con foto en obra)
        po.reload()
        po.submit()
        png_1px = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
        po.reload()
        res_f1 = flujo_operativo.confirmar_entrega_obra_foto(po.name, json.dumps({
            "reportado_por": "Ing. Residente Obra",
            "foto_nombre": "entrega_parcial_1.png",
            "foto_base64": png_1px,
            "notas": "Primera entrega parcial en obra",
            "rows": [{"po_item": po.items[0].name, "qty": 2}],
        }))
        assert 0 < res_f1["pct_recibido"] < 100

        po.reload()
        res_f2 = flujo_operativo.confirmar_entrega_obra_foto(po.name, json.dumps({
            "reportado_por": "Ing. Residente Obra",
            "foto_nombre": "entrega_final_2.png",
            "foto_base64": png_1px,
            "notas": "Segunda entrega completada",
        }))
        assert res_f2["pct_recibido"] >= 99.9 and res_f2["estado"] == "Entregado en obra (con foto)"

        # FLU-03b: Compra directa en campo solo con XML (sin Solicitud ni OC y sin mover stock)
        frappe.db.set_value("Company", company, "tax_id", "BBB010101BBB")
        uuid_campo = str(uuid.uuid4()).upper()
        xml_campo = (
            f'<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" '
            f'xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{today()}T12:00:00" '
            f'SubTotal="250.00" Moneda="MXN" Total="290.00" TipoDeComprobante="I">'
            f'<cfdi:Emisor Rfc="FAS010101AAA" Nombre="FERRETERIA DE CAMPO SA"/>'
            f'<cfdi:Receptor Rfc="BBB010101BBB" Nombre="LEBAREF"/>'
            f'<cfdi:Conceptos><cfdi:Concepto ClaveProdServ="27111701" NoIdentificacion="CAM-01" Cantidad="2" '
            f'ClaveUnidad="H87" Descripcion="Disco de corte comprado urgente en campo" ValorUnitario="125.00" Importe="250.00"/></cfdi:Conceptos>'
            f'<cfdi:Impuestos TotalImpuestosTrasladados="40.00"/>'
            f'<cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid_campo}"/></cfdi:Complemento></cfdi:Comprobante>'
        )
        res_campo = flujo_operativo.registrar_xml_campo_directo(xml_campo, json.dumps({
            "obra": item_base.project,
            "comprador_campo": "Ing. Eléctrico Campo",
            "partida": "Urgencias Campo",
        }))
        pi_campo = frappe.get_doc("Purchase Invoice", res_campo["folio"])
        assert pi_campo.update_stock == 0 and int(pi_campo.custom_lebaref_es_compra_campo) == 1

        # FLU-04 y FLU-05: Tableros de Inicio Lebaref e Hilo Conductor Conciliado
        tb = flujo_operativo.tableros_inicio_lebaref()
        assert "resumen" in tb and "solicitudes_sin_atender" in tb and "pendientes_pago" in tb
        hilo = flujo_operativo.hilo_conductor("Purchase Order", po.name)
        assert len(hilo["solicitudes"]) == 2 and len(hilo["cotizaciones"]) == 2 and len(hilo["recepciones"]) == 2

        return {"correcto": True, "pruebas_fase2": ["FLU-01 USD+DOF+Flete", "FLU-02 Pago/Logistica+CSF/Banco", "FLU-03 FotoObra+XMLCampo+Oficina", "FLU-04 3Tableros", "FLU-05 Consolidacion+HiloConductor"]}
    finally:
        frappe.db.rollback()
        frappe.set_user(usuario_orig)
