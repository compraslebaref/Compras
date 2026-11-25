"""Fase 2 — Flujo Operativo Real de Compras y Logística LEBAREF:
- FLU-01: Cotizaciones MXN/USD con Tipo de Cambio DOF manual, origen (Compras vs. Ingeniero) y flete separado.
- FLU-02: Estados reales de pago y logística (Cliente, Pago Dirección, Ir a recolectar, En flete) + CSF y Carátula Bancaria.
- FLU-03: Confirmación de entrega en obra con foto (entregas totales o divididas), XML directo de campo y Consumibles de Oficina.
- FLU-04: Los 3 Tableros Operativos vivos en Inicio Lebaref.
- FLU-05: Hilo Conductor Único conciliado (Solicitud <-> Cotización <-> OC/Pago <-> Entrega/Foto <-> XML) y OCs consolidadas.
"""
import base64
import json
import frappe
from frappe.custom.doctype.custom_field.custom_field import create_custom_fields
from frappe.utils import add_days, cint, date_diff, flt, getdate, now_datetime, today

ESTADOS_OC_LOGISTICA = (
    "",
    "Pendiente de gestionar pago",
    "Esperando pago Dirección",
    "Pagado — Enviar comprobante",
    "Liberado — Ir a recolectar",
    "En flete",
    "Entregado en almacén",
    "Entregado en obra (con foto)",
    "Consumo directo (Oficina)",
)

MODALIDADES_ENTREGA = (
    "",
    "Ir a recolectar",
    "En flete (proveedor entrega)",
    "Entrega directa en obra",
    "Consumo directo (Oficina)",
)


def configurar_campos_fase2():
    """Instala de forma idempotente los campos de Fase 2 y habilita USD + regla de Consumibles de Oficina."""
    for curr in ("MXN", "USD"):
        if frappe.db.exists("Currency", curr):
            frappe.db.set_value("Currency", curr, "enabled", 1, update_modified=False)

    # Asegurar tipo de cambio base de respaldo para que mappers internos de ERPNext no fallen antes de aplicar TC DOF
    if frappe.db.exists("DocType", "Currency Exchange"):
        if not frappe.db.exists("Currency Exchange", {"from_currency": "USD", "to_currency": "MXN"}):
            try:
                frappe.get_doc({
                    "doctype": "Currency Exchange",
                    "date": "2026-01-01",
                    "from_currency": "USD",
                    "to_currency": "MXN",
                    "exchange_rate": 20.0,
                    "for_buying": 1,
                    "for_selling": 1,
                }).insert(ignore_permissions=True)
            except Exception:
                pass

    campos = {
        "Supplier": [
            {
                "fieldname": "custom_sec_expediente_pago",
                "label": "Expediente Fiscal y Bancario (RFC, CSF y Carátula)",
                "fieldtype": "Section Break",
                "insert_after": "payment_terms",
            },
            {
                "fieldname": "custom_lebaref_csf_archivo",
                "label": "Constancia de Situación Fiscal (CSF)",
                "fieldtype": "Attach",
                "insert_after": "custom_sec_expediente_pago",
            },
            {
                "fieldname": "custom_lebaref_csf_fecha",
                "label": "Fecha de CSF",
                "fieldtype": "Date",
                "insert_after": "custom_lebaref_csf_archivo",
            },
            {
                "fieldname": "custom_cb_expediente_pago",
                "fieldtype": "Column Break",
                "insert_after": "custom_lebaref_csf_fecha",
            },
            {
                "fieldname": "custom_lebaref_caratula_archivo",
                "label": "Carátula Bancaria",
                "fieldtype": "Attach",
                "insert_after": "custom_cb_expediente_pago",
            },
            {
                "fieldname": "custom_lebaref_clabe",
                "label": "CLABE / Cuenta Bancaria",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_caratula_archivo",
            },
        ],
        "Item": [
            {
                "fieldname": "custom_lebaref_consumo_directo",
                "label": "Consumo directo (Oficina / No pasa por almacén)",
                "fieldtype": "Check",
                "insert_after": "custom_lebaref_ultimo_costo",
                "description": "Conserva código para historial de precios, pero no exige entrada ni salida de almacén.",
            },
        ],
        "Material Request": [
            {
                "fieldname": "custom_lebaref_proposito",
                "label": "Propósito de la solicitud",
                "fieldtype": "Select",
                "options": "Compra operativa\nSolo presupuesto para ingeniero",
                "default": "Compra operativa",
                "insert_after": "custom_lebaref_nombre_solicitud",
                "allow_on_submit": 1,
                "in_standard_filter": 1,
            },
            {
                "fieldname": "custom_lebaref_bitacora_cambios",
                "label": "Bitácora de conciliación",
                "fieldtype": "Long Text",
                "insert_after": "custom_lebaref_notas",
                "read_only": 1,
                "allow_on_submit": 1,
                "hidden": 1,
                "print_hide": 1,
            },
        ],
        "Supplier Quotation": [
            {
                "fieldname": "custom_sec_multimoneda_logistica",
                "label": "Tipo de Cambio DOF y Condiciones de Entrega",
                "fieldtype": "Section Break",
                "insert_after": "custom_lebaref_fecha_programada_pago",
            },
            {
                "fieldname": "custom_lebaref_tc_dof",
                "label": "Tipo de cambio oficial DOF",
                "fieldtype": "Float",
                "precision": "4",
                "insert_after": "custom_sec_multimoneda_logistica",
                "description": "Obligatorio en cotizaciones en USD (publicado en el DOF de ese día).",
            },
            {
                "fieldname": "custom_lebaref_fecha_dof",
                "label": "Fecha publicación DOF",
                "fieldtype": "Date",
                "insert_after": "custom_lebaref_tc_dof",
            },
            {
                "fieldname": "custom_cb_sq_log",
                "fieldtype": "Column Break",
                "insert_after": "custom_lebaref_fecha_dof",
            },
            {
                "fieldname": "custom_lebaref_modalidad_entrega",
                "label": "Modalidad de entrega",
                "fieldtype": "Select",
                "options": "\n".join(MODALIDADES_ENTREGA),
                "insert_after": "custom_cb_sq_log",
            },
            {
                "fieldname": "custom_lebaref_dias_entrega_promesa",
                "label": "Días promesa de entrega",
                "fieldtype": "Int",
                "insert_after": "custom_lebaref_modalidad_entrega",
            },
            {
                "fieldname": "custom_lebaref_flete_sin_iva",
                "label": "Flete separado (sin IVA)",
                "fieldtype": "Currency",
                "insert_after": "custom_lebaref_dias_entrega_promesa",
            },
        ],
        "Supplier Quotation Item": [
            {
                "fieldname": "custom_lebaref_precio_limpio_mxn",
                "label": "Precio unitario limpio (MXN)",
                "fieldtype": "Currency",
                "options": "Company:company:default_currency",
                "insert_after": "rate",
                "read_only": 1,
            },
            {
                "fieldname": "custom_lebaref_precio_sitio_mxn",
                "label": "Costo unitario c/flete (MXN)",
                "fieldtype": "Currency",
                "options": "Company:company:default_currency",
                "insert_after": "custom_lebaref_precio_limpio_mxn",
                "read_only": 1,
            },
        ],
        "Purchase Order": [
            {
                "fieldname": "custom_sec_logistica_pago",
                "label": "Seguimiento de Pago, Recolección / Flete y Entrega",
                "fieldtype": "Section Break",
                "insert_after": "custom_lebaref_fecha_programada_pago",
            },
            {
                "fieldname": "custom_lebaref_estado_operativo",
                "label": "Estado operativo y logístico",
                "fieldtype": "Select",
                "options": "\n".join(ESTADOS_OC_LOGISTICA),
                "insert_after": "custom_sec_logistica_pago",
                "allow_on_submit": 1,
                "in_list_view": 1,
                "in_standard_filter": 1,
            },
            {
                "fieldname": "custom_lebaref_modalidad_entrega",
                "label": "Modalidad de entrega",
                "fieldtype": "Select",
                "options": "\n".join(MODALIDADES_ENTREGA),
                "insert_after": "custom_lebaref_estado_operativo",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_dias_flete",
                "label": "Días promesa de entrega",
                "fieldtype": "Int",
                "insert_after": "custom_lebaref_modalidad_entrega",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_fecha_promesa_entrega",
                "label": "Fecha promesa de llegada",
                "fieldtype": "Date",
                "insert_after": "custom_lebaref_dias_flete",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_tc_dof",
                "label": "Tipo de cambio oficial DOF",
                "fieldtype": "Float",
                "precision": "4",
                "insert_after": "custom_lebaref_fecha_promesa_entrega",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_flete_sin_iva",
                "label": "Flete separado (sin IVA)",
                "fieldtype": "Currency",
                "insert_after": "custom_lebaref_tc_dof",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_cb_logistica_pago",
                "fieldtype": "Column Break",
                "insert_after": "custom_lebaref_flete_sin_iva",
            },
            {
                "fieldname": "custom_lebaref_comprobante_pago",
                "label": "Comprobante de pago (Dirección)",
                "fieldtype": "Attach",
                "insert_after": "custom_cb_logistica_pago",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_foto_entrega_obra",
                "label": "Foto de entrega en obra / servicio",
                "fieldtype": "Attach Image",
                "insert_after": "custom_lebaref_comprobante_pago",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_reportado_por",
                "label": "Confirmado en obra por",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_foto_entrega_obra",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_notas_logisticas",
                "label": "Instrucciones de recolección / seguimiento",
                "fieldtype": "Small Text",
                "insert_after": "custom_lebaref_reportado_por",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_bitacora_cambios",
                "label": "Bitácora de conciliación",
                "fieldtype": "Long Text",
                "insert_after": "custom_lebaref_notas_logisticas",
                "read_only": 1,
                "allow_on_submit": 1,
                "hidden": 1,
                "print_hide": 1,
            },
        ],
        "Purchase Invoice": [
            {
                "fieldname": "custom_sec_campo_directo",
                "label": "Compra Directa en Campo (Sin Solicitud ni OC)",
                "fieldtype": "Section Break",
                "insert_after": "custom_lebaref_clave_comprobante",
            },
            {
                "fieldname": "custom_lebaref_es_compra_campo",
                "label": "Compra directa en campo (solo factura XML)",
                "fieldtype": "Check",
                "insert_after": "custom_sec_campo_directo",
                "allow_on_submit": 1,
                "in_standard_filter": 1,
            },
            {
                "fieldname": "custom_lebaref_obra_campo",
                "label": "Obra / Proyecto asignado",
                "fieldtype": "Link",
                "options": "Project",
                "insert_after": "custom_lebaref_es_compra_campo",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_cb_campo_directo",
                "fieldtype": "Column Break",
                "insert_after": "custom_lebaref_obra_campo",
            },
            {
                "fieldname": "custom_lebaref_departamento",
                "label": "Gerencia / Departamento",
                "fieldtype": "Link",
                "options": "Department",
                "insert_after": "custom_cb_campo_directo",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_comprador_campo",
                "label": "Realizada en campo por",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_departamento",
                "allow_on_submit": 1,
            },
            {
                "fieldname": "custom_lebaref_tc_dof",
                "label": "Tipo de cambio oficial DOF",
                "fieldtype": "Float",
                "precision": "4",
                "insert_after": "custom_lebaref_comprador_campo",
                "allow_on_submit": 1,
            },
        ],
    }
    create_custom_fields(campos)

    # Eliminar el campo descartado custom_lebaref_origen_cotizacion si se había creado previamente
    if frappe.db.exists("Custom Field", "Supplier Quotation-custom_lebaref_origen_cotizacion"):
        frappe.delete_doc("Custom Field", "Supplier Quotation-custom_lebaref_origen_cotizacion", ignore_permissions=True, force=True)

    # Marcar automáticamente consumibles de oficina / papelería / comedor con regla de Consumo Directo
    if frappe.db.has_column("Item", "custom_lebaref_consumo_directo"):
        frappe.db.sql(
            """
            UPDATE `tabItem`
            SET custom_lebaref_consumo_directo = 1
            WHERE IFNULL(custom_lebaref_consumo_directo, 0) = 0
              AND (
                  UPPER(IFNULL(item_group, '')) LIKE '%PAPELER%'
                  OR UPPER(IFNULL(item_group, '')) LIKE '%OFICINA%'
                  OR UPPER(IFNULL(item_group, '')) LIKE '%COMEDOR%'
                  OR UPPER(IFNULL(item_name, '')) LIKE '%PAPEL BOND%'
                  OR UPPER(IFNULL(item_name, '')) LIKE '%TONER%'
                  OR UPPER(IFNULL(item_name, '')) LIKE '%CARPETA LEFORT%'
              )
            """
        )


def _agregar_evento_bitacora(doctype: str, name: str, etapa: str, detalle: str):
    if not name or not frappe.db.exists(doctype, name):
        return
    if not frappe.db.has_column(doctype, "custom_lebaref_bitacora_cambios"):
        return
    previo = frappe.db.get_value(doctype, name, "custom_lebaref_bitacora_cambios") or "[]"
    try:
        lista = json.loads(previo) if previo else []
        if not isinstance(lista, list):
            lista = []
    except Exception:
        lista = []
    lista.append({
        "fecha": str(now_datetime())[:19],
        "usuario": frappe.utils.get_fullname(frappe.session.user) or frappe.session.user,
        "etapa": etapa,
        "detalle": detalle,
    })
    frappe.db.set_value(
        doctype,
        name,
        "custom_lebaref_bitacora_cambios",
        json.dumps(lista, ensure_ascii=False),
        update_modified=False,
    )


def aplicar_multimoneda_y_flete_en_cotizacion(doc, data: dict):
    """Aplica moneda MXN/USD, TC DOF obligatorio para USD, origen de cotización y prorrateo limpio de flete."""
    moneda = (data.get("currency") or doc.currency or "MXN").strip().upper()
    if moneda not in ("MXN", "USD"):
        frappe.throw("Selecciona una moneda válida (MXN o USD).")

    tc_dof = flt(data.get("tc_dof") or data.get("custom_lebaref_tc_dof") or 0, 4)
    fecha_dof = (data.get("fecha_dof") or data.get("custom_lebaref_fecha_dof") or str(doc.transaction_date or today())).strip()

    if moneda == "USD":
        if tc_dof <= 0:
            frappe.throw("Para cotizaciones en dólares (USD), captura el Tipo de Cambio oficial del DOF de ese día.")
        doc.currency = "USD"
        doc.conversion_rate = tc_dof
        doc.ignore_pricing_rule = 1
        doc.custom_lebaref_tc_dof = tc_dof
        doc.custom_lebaref_fecha_dof = getdate(fecha_dof)
    else:
        doc.currency = "MXN"
        doc.conversion_rate = 1.0
        doc.custom_lebaref_tc_dof = 1.0
        doc.custom_lebaref_fecha_dof = None

    factor_mxn = tc_dof if moneda == "USD" else 1.0

    modalidad = (data.get("modalidad_entrega") or "").strip()
    if modalidad in MODALIDADES_ENTREGA:
        doc.custom_lebaref_modalidad_entrega = modalidad

    dias_promesa = cint(data.get("dias_entrega_promesa") or 0)
    if dias_promesa > 0:
        doc.custom_lebaref_dias_entrega_promesa = dias_promesa

    flete_sin_iva = max(0.0, flt(data.get("flete_sin_iva") or 0, 2))
    doc.custom_lebaref_flete_sin_iva = flete_sin_iva

    subtotal_lineas = sum(flt(r.qty) * flt(r.rate) for r in (doc.items or []))
    for r in doc.items or []:
        limpio_mxn = flt(r.rate) * factor_mxn
        flete_unit_moneda = 0.0
        if flete_sin_iva > 0 and subtotal_lineas > 0 and flt(r.qty) > 0:
            proporcion = (flt(r.qty) * flt(r.rate)) / subtotal_lineas
            flete_unit_moneda = (flete_sin_iva * proporcion) / flt(r.qty)
        r.custom_lebaref_precio_limpio_mxn = round(limpio_mxn, 4)
        r.custom_lebaref_precio_sitio_mxn = round((flt(r.rate) + flete_unit_moneda) * factor_mxn, 4)

    # Si se capturó flete separado y no hay regla de envío, agregar cargo explícito de flete sin tocar el precio limpio del artículo
    if flete_sin_iva > 0 and not doc.shipping_rule and cint(data.get("incluir_flete_en_total", 1)) == 1:
        cuenta_gasto = frappe.db.get_value("Company", doc.company, "default_expense_account")
        if not cuenta_gasto:
            cuenta_gasto = frappe.db.get_value(
                "Account",
                {"company": doc.company, "is_group": 0, "disabled": 0, "root_type": "Expense"},
                "name",
            )
        if cuenta_gasto:
            ya_tiene_flete = any("FLETE" in (t.description or "").upper() for t in (doc.taxes or []))
            if not ya_tiene_flete:
                doc.append("taxes", {
                    "charge_type": "Actual",
                    "account_head": cuenta_gasto,
                    "description": "Flete / Envío separado (sin IVA)",
                    "tax_amount": flete_sin_iva,
                    "category": "Total",
                    "add_deduct_tax": "Add",
                })


def trasladar_logistica_a_orden(order, quote):
    """Traslada moneda/TC DOF, flete, modalidad, promesa y estado operativo inicial desde la Cotización a la OC."""
    if not quote:
        return
    if quote.currency == "USD" and flt(quote.get("custom_lebaref_tc_dof") or quote.conversion_rate) > 0:
        tc = flt(quote.get("custom_lebaref_tc_dof") or quote.conversion_rate, 4)
        order.currency = "USD"
        order.conversion_rate = tc
        order.custom_lebaref_tc_dof = tc
    if flt(quote.get("custom_lebaref_flete_sin_iva") or 0) > 0:
        order.custom_lebaref_flete_sin_iva = flt(quote.get("custom_lebaref_flete_sin_iva"))
    mod = (quote.get("custom_lebaref_modalidad_entrega") or "").strip()
    if mod:
        order.custom_lebaref_modalidad_entrega = mod
    dias = cint(quote.get("custom_lebaref_dias_entrega_promesa") or 0)
    if not dias and order.items:
        dias = max([cint(getattr(it, "lead_time_days", 0) or 0) for it in order.items] + [0])
    if dias > 0:
        order.custom_lebaref_dias_flete = dias
        order.custom_lebaref_fecha_promesa_entrega = add_days(today(), dias)

    # Detectar si todos los artículos de la OC son de Consumo Directo (Oficina)
    codigos = [it.item_code for it in (order.items or []) if it.item_code]
    todos_directos = False
    if codigos and frappe.db.has_column("Item", "custom_lebaref_consumo_directo"):
        flags = frappe.get_all(
            "Item",
            filters={"name": ["in", codigos]},
            fields=["name", "custom_lebaref_consumo_directo"],
        )
        if flags and all(cint(f.custom_lebaref_consumo_directo) == 1 for f in flags):
            todos_directos = True

    if todos_directos:
        order.custom_lebaref_modalidad_entrega = "Consumo directo (Oficina)"
        order.custom_lebaref_estado_operativo = "Consumo directo (Oficina)"
    elif not (order.get("custom_lebaref_estado_operativo") or "").strip():
        cond_pago = (order.payment_terms_template or quote.get("custom_lebaref_condicion_pago") or "").upper()
        if "CRÉDITO" in cond_pago or "CREDITO" in cond_pago:
            if mod == "Ir a recolectar":
                order.custom_lebaref_estado_operativo = "Liberado — Ir a recolectar"
            elif mod == "En flete (proveedor entrega)":
                order.custom_lebaref_estado_operativo = "En flete"
            else:
                order.custom_lebaref_estado_operativo = "Liberado — Ir a recolectar"
        else:
            order.custom_lebaref_estado_operativo = "Pendiente de gestionar pago"


@frappe.whitelist(methods=["GET"])
def estado_fiscal_proveedor(proveedor: str):
    """Devuelve si el proveedor cuenta con Constancia de Situación Fiscal (CSF) y Carátula Bancaria."""
    sup = frappe.get_doc("Supplier", proveedor)
    sup.check_permission("read")
    csf = (sup.get("custom_lebaref_csf_archivo") or "").strip()
    caratula = (sup.get("custom_lebaref_caratula_archivo") or "").strip()
    clabe = (sup.get("custom_lebaref_clabe") or "").strip()
    completo = bool(csf and caratula)
    faltantes = []
    if not csf:
        faltantes.append("Constancia de Situación Fiscal (CSF)")
    if not caratula:
        faltantes.append("Carátula Bancaria")
    return {
        "proveedor": sup.name,
        "nombre": sup.supplier_name or sup.name,
        "rfc": sup.tax_id or "",
        "csf_archivo": csf,
        "csf_fecha": str(sup.get("custom_lebaref_csf_fecha") or ""),
        "caratula_archivo": caratula,
        "clabe": clabe,
        "expediente_completo": completo,
        "faltantes": faltantes,
    }


@frappe.whitelist(methods=["POST"])
def subir_documento_fiscal_proveedor(proveedor: str, tipo: str, filename: str = "", content_base64: str = "", clabe: str = ""):
    """Adjunta CSF o Carátula Bancaria al expediente del proveedor."""
    if not (frappe.has_permission("Supplier", "write") or frappe.has_permission("Purchase Order", "create")):
        frappe.throw("Sin permiso para actualizar expediente de proveedor.", frappe.PermissionError)
    sup = frappe.get_doc("Supplier", proveedor)
    if clabe and clabe.strip():
        frappe.db.set_value("Supplier", sup.name, "custom_lebaref_clabe", clabe.strip(), update_modified=False)

    if filename and content_base64:
        b64 = content_base64.split(",", 1)[1] if "," in content_base64 else content_base64
        raw = base64.b64decode(b64)
        from frappe.utils.file_manager import save_file
        prefijo = "CSF" if tipo == "csf" else "Caratula_Bancaria"
        fdoc = save_file(f"{prefijo}_{filename}", raw, "Supplier", sup.name, is_private=1)
        if tipo == "csf":
            frappe.db.set_value("Supplier", sup.name, {
                "custom_lebaref_csf_archivo": fdoc.file_url,
                "custom_lebaref_csf_fecha": today(),
            }, update_modified=False)
        elif tipo == "caratula":
            frappe.db.set_value("Supplier", sup.name, "custom_lebaref_caratula_archivo", fdoc.file_url, update_modified=False)

    return estado_fiscal_proveedor(sup.name)


@frappe.whitelist(methods=["POST"])
def actualizar_estado_pago_logistica(
    doctype: str,
    folio: str,
    nuevo_estado: str,
    modalidad_entrega: str = "",
    dias_flete: int = 0,
    notas: str = "",
    comprobante_nombre: str = "",
    comprobante_base64: str = "",
):
    """Actualiza el estado real de pago/logística en Solicitud (Presupuesto Cliente) u Orden de Compra."""
    if doctype == "Material Request":
        mr = frappe.get_doc("Material Request", folio)
        mr.check_permission("write")
        if nuevo_estado == "En espera de aprobación de cliente":
            frappe.db.set_value("Material Request", mr.name, {
                "custom_lebaref_proposito": "Solo presupuesto para ingeniero",
                "custom_lebaref_en_espera_de": "Aprobación de cliente (Presupuesto)",
                "custom_lebaref_en_espera_desde": now_datetime(),
            }, update_modified=False)
            _agregar_evento_bitacora("Material Request", mr.name, "Presupuesto enviado", notas or "Cotización entregada al ingeniero; en espera de aprobación del cliente.")
        elif nuevo_estado == "Compra operativa":
            frappe.db.set_value("Material Request", mr.name, {
                "custom_lebaref_proposito": "Compra operativa",
                "custom_lebaref_en_espera_de": "",
                "custom_lebaref_en_espera_desde": None,
            }, update_modified=False)
            _agregar_evento_bitacora("Material Request", mr.name, "Aprobado para compra", notas or "Cliente aprobó; pasa a compra operativa.")
        from lebaref_compras.ux_lebaref import recalcular_estados_vista_solicitudes
        recalcular_estados_vista_solicitudes([mr.name])
        return {"ok": True, "folio": mr.name, "estado": nuevo_estado}

    po = frappe.get_doc("Purchase Order", folio)
    if not (po.has_permission("write") or frappe.has_permission("Purchase Order", "create")):
        frappe.throw("Sin permiso para actualizar seguimiento de la Orden de Compra.", frappe.PermissionError)

    if nuevo_estado not in ESTADOS_OC_LOGISTICA:
        frappe.throw("Selecciona un estado operativo válido.")

    aviso_fiscal = ""
    if nuevo_estado == "Esperando pago Dirección" and po.supplier:
        ef = estado_fiscal_proveedor(po.supplier)
        if not ef["expediente_completo"]:
            aviso_fiscal = "Aviso: Al proveedor le falta " + " y ".join(ef["faltantes"]) + " en su expediente."

    updates = {"custom_lebaref_estado_operativo": nuevo_estado}
    if modalidad_entrega in MODALIDADES_ENTREGA and modalidad_entrega:
        updates["custom_lebaref_modalidad_entrega"] = modalidad_entrega
    if cint(dias_flete) > 0:
        updates["custom_lebaref_dias_flete"] = cint(dias_flete)
        updates["custom_lebaref_fecha_promesa_entrega"] = add_days(today(), cint(dias_flete))
    if notas is not None and notas.strip():
        updates["custom_lebaref_notas_logisticas"] = notas.strip()

    if comprobante_nombre and comprobante_base64:
        b64 = comprobante_base64.split(",", 1)[1] if "," in comprobante_base64 else comprobante_base64
        raw = base64.b64decode(b64)
        from frappe.utils.file_manager import save_file
        fdoc = save_file(f"Pago_{po.name}_{comprobante_nombre}", raw, "Purchase Order", po.name, is_private=1)
        updates["custom_lebaref_comprobante_pago"] = fdoc.file_url

    frappe.db.set_value("Purchase Order", po.name, updates, update_modified=False)
    detalle_log = f"Estado -> {nuevo_estado}"
    if modalidad_entrega:
        detalle_log += f" · Modalidad: {modalidad_entrega}"
    if cint(dias_flete) > 0:
        detalle_log += f" · Promesa: {cint(dias_flete)} día(s)"
    if notas and notas.strip():
        detalle_log += f" · Nota: {notas.strip()}"
    _agregar_evento_bitacora("Purchase Order", po.name, "Pago / Logística", detalle_log)

    return {"ok": True, "folio": po.name, "estado": nuevo_estado, "aviso_fiscal": aviso_fiscal}


@frappe.whitelist(methods=["POST"])
def confirmar_entrega_obra_foto(orden: str, datos: str):
    """Confirma entrega directa en obra o servicio con evidencia fotográfica (admite entregas parciales / divididas)."""
    po = frappe.get_doc("Purchase Order", orden)
    if not (frappe.has_permission("Purchase Receipt", "create") or frappe.has_permission("Purchase Order", "create")):
        frappe.throw("Sin permiso para registrar confirmaciones de entrega.", frappe.PermissionError)

    payload = frappe.parse_json(datos) if isinstance(datos, str) else datos
    reportado_por = (payload.get("reportado_por") or "").strip()
    if not reportado_por:
        frappe.throw("Indica qué ingeniero o auxiliar confirmó la entrega en sitio.")

    foto_nombre = (payload.get("foto_nombre") or "").strip()
    foto_b64 = (payload.get("foto_base64") or "").strip()
    if not foto_b64:
        frappe.throw("Adjunta la fotografía de evidencia de entrega en obra o servicio.")

    b64_clean = foto_b64.split(",", 1)[1] if "," in foto_b64 else foto_b64
    raw_img = base64.b64decode(b64_clean)
    from frappe.utils.file_manager import save_file
    nombre_seguro = foto_nombre or f"evidencia_{po.name}.jpg"
    f_po = save_file(f"EntregaObra_{po.name}_{nombre_seguro}", raw_img, "Purchase Order", po.name, is_private=0)

    notas = (payload.get("notas") or "").strip()
    almacen_obra = (payload.get("almacen_obra") or "").strip()

    # Si la OC ya está autorizada (docstatus == 1), podemos también generar el Purchase Receipt parcial/total si tiene saldo
    recibo_folio = ""
    cantidades_elegidas = {str(r.get("po_item")): flt(r.get("qty")) for r in (payload.get("rows") or []) if flt(r.get("qty")) > 0}

    if po.docstatus == 1 and cint(payload.get("generar_recepcion", 1)) == 1:
        from erpnext.buying.doctype.purchase_order.purchase_order import make_purchase_receipt
        pr = make_purchase_receipt(po.name)
        pr.custom_lebaref_recibido_por = reportado_por
        kept = []
        for r in pr.items:
            pend = flt(r.qty)
            q_usar = cantidades_elegidas.get(r.purchase_order_item, pend if not cantidades_elegidas else 0.0)
            if q_usar <= 0:
                continue
            if q_usar > pend + 1e-6:
                frappe.throw(f"La cantidad ({q_usar}) excede el saldo pendiente ({pend}) en {r.item_code}.")
            r.qty = q_usar
            r.received_qty = q_usar
            if almacen_obra and cint(frappe.db.get_value("Item", r.item_code, "is_stock_item") or 0) == 1:
                r.warehouse = almacen_obra
            kept.append(r)
        if kept:
            pr.set("items", kept)
            pr.remarks = f"Confirmado en obra con foto por: {reportado_por}. {notas}".strip()
            pr.insert(ignore_permissions=True)
            if pr.has_permission("submit") or frappe.has_permission("Purchase Receipt", "create"):
                pr.submit()
            recibo_folio = pr.name
            save_file(f"EntregaObra_{pr.name}_{nombre_seguro}", raw_img, "Purchase Receipt", pr.name, is_private=0)

    po.reload()
    completo = flt(po.per_received or 0) >= 99.9 or po.docstatus == 0
    estado_nuevo = "Entregado en obra (con foto)" if completo else (po.get("custom_lebaref_estado_operativo") or "En flete")
    frappe.db.set_value("Purchase Order", po.name, {
        "custom_lebaref_foto_entrega_obra": f_po.file_url,
        "custom_lebaref_reportado_por": reportado_por,
        "custom_lebaref_estado_operativo": estado_nuevo,
        "custom_lebaref_notas_logisticas": (notas or po.get("custom_lebaref_notas_logisticas") or ""),
    }, update_modified=False)

    resumen_ev = f"Foto en obra reportada por {reportado_por}"
    if recibo_folio:
        resumen_ev += f" · Recepción {recibo_folio} ({round(flt(po.per_received or 0), 1)}% recibido)"
    if notas:
        resumen_ev += f" · {notas}"
    _agregar_evento_bitacora("Purchase Order", po.name, "Entrega en Obra (Foto)", resumen_ev)

    for mri in {it.material_request for it in po.items if it.material_request}:
        _agregar_evento_bitacora("Material Request", mri, "Entrega en Obra (Foto)", f"OC {po.name}: {resumen_ev}")

    return {
        "ok": True,
        "orden": po.name,
        "recibo": recibo_folio,
        "foto_url": f_po.file_url,
        "pct_recibido": round(flt(po.per_received or 0), 1),
        "estado": estado_nuevo,
    }


@frappe.whitelist(methods=["POST"])
def registrar_xml_campo_directo(xml: str, datos: str):
    """Registra una factura XML de compra realizada directo en campo sin Solicitud ni OC y sin afectar almacén."""
    from lebaref_compras import operaciones, solicitudes

    if not frappe.has_permission("Purchase Invoice", "create"):
        frappe.throw("Sin permiso para registrar facturas XML de campo.", frappe.PermissionError)

    payload = frappe.parse_json(datos) if isinstance(datos, str) else datos
    obra = (payload.get("obra") or "").strip()
    departamento = (payload.get("departamento") or "").strip()
    comprador = (payload.get("comprador_campo") or "").strip()
    tc_dof = flt(payload.get("tc_dof") or 0, 4)
    mapeo = payload.get("mapeo") or {}

    parsed = operaciones._parsear_cfdi_compra(xml, purchase_order="", crear_proveedor_si_falta=True)
    xml_limpio = parsed.pop("xml_limpio")

    if parsed["factura_existente"]:
        frappe.throw(f"Este CFDI ({parsed['uuid']}) ya existe en la factura {parsed['factura_existente']}.")
    if parsed["moneda"] == "USD" and tc_dof <= 0:
        frappe.throw("El XML está en USD; captura el Tipo de Cambio oficial del DOF de la fecha de factura.")

    pi_items = []
    for it in parsed["items"]:
        key_idx = str(it["idx_xml"])
        elegido_sku = (mapeo.get(key_idx) or it["item_code"] or solicitudes.ARTICULO_LIBRE).strip()
        if not frappe.db.exists("Item", elegido_sku):
            elegido_sku = solicitudes.ARTICULO_LIBRE
        meta = frappe.db.get_value("Item", elegido_sku, ["item_name", "stock_uom", "purchase_uom"], as_dict=True)
        if elegido_sku != solicitudes.ARTICULO_LIBRE:
            factor_costo = tc_dof if (parsed["moneda"] == "USD" and tc_dof > 0) else 1.0
            operaciones._aprender_sinonimo_proveedor(
                parsed["supplier"],
                elegido_sku,
                it.get("supplier_part_no") or it.get("description") or "",
                flt(it.get("rate") or 0) * factor_costo,
            )
        uom_usar = (meta.purchase_uom or meta.stock_uom or "Pieza") if meta else "Pieza"
        row_d = {
            "item_code": elegido_sku,
            "item_name": ((meta.item_name if (meta and elegido_sku != solicitudes.ARTICULO_LIBRE) else it["description"]) or it["description"])[:140],
            "description": it["description"],
            "custom_lebaref_concepto": it["description"] if elegido_sku == solicitudes.ARTICULO_LIBRE else "",
            "qty": it["qty"],
            "uom": uom_usar,
            "stock_uom": (meta.stock_uom or uom_usar) if meta else "Pieza",
            "conversion_factor": 1.0,
            "rate": it["rate"],
            "expense_account": it["expense_account"],
        }
        if obra:
            row_d["project"] = obra
        if payload.get("partida"):
            row_d["custom_lebaref_partida"] = (payload.get("partida") or "").strip()
        pi_items.append(row_d)

    moneda_doc = parsed["moneda"] if frappe.db.exists("Currency", parsed["moneda"]) else "MXN"
    pi_doc = frappe.get_doc({
        "doctype": "Purchase Invoice",
        "company": parsed["company"],
        "supplier": parsed["supplier"],
        "posting_date": parsed["fecha"] or today(),
        "bill_no": parsed["uuid"],
        "bill_date": parsed["fecha"] or today(),
        "currency": moneda_doc,
        "conversion_rate": tc_dof if (moneda_doc == "USD" and tc_dof > 0) else 1.0,
        "update_stock": 0,
        "taxes_and_charges": parsed["taxes_and_charges"] or None,
        "custom_lebaref_es_compra_campo": 1,
        "custom_lebaref_obra_campo": obra or None,
        "custom_lebaref_departamento": departamento or None,
        "custom_lebaref_comprador_campo": comprador,
        "custom_lebaref_tc_dof": tc_dof if moneda_doc == "USD" else 1.0,
        "remarks": f"[COMPRA DIRECTA EN CAMPO] Comprador: {comprador or 'Campo'} | Obra: {obra or 'General'} | UUID: {parsed['uuid']}",
        "items": pi_items,
    })
    if pi_doc.taxes_and_charges:
        tpl_doc = frappe.get_doc("Purchase Taxes and Charges Template", pi_doc.taxes_and_charges)
        for tx in tpl_doc.taxes:
            pi_doc.append("taxes", {
                "charge_type": tx.charge_type,
                "account_head": tx.account_head,
                "description": tx.description,
                "rate": tx.rate,
                "category": tx.category,
                "add_deduct_tax": tx.add_deduct_tax,
            })

    pi_doc.insert()
    from frappe.utils.file_manager import save_file
    save_file(f"Campo_{parsed['uuid']}.xml", xml_limpio.encode("utf-8"), "Purchase Invoice", pi_doc.name, is_private=1)

    return {
        "ok": True,
        "folio": pi_doc.name,
        "uuid": parsed["uuid"],
        "supplier": parsed["supplier"],
        "total": parsed["total"],
        "moneda": moneda_doc,
    }


@frappe.whitelist(methods=["POST"])
def registrar_nota_conciliacion(doctype: str, folio: str, etapa: str, detalle: str):
    """Permite anotar ajustes de cantidad, precio, artículo o condición entre etapas del hilo conductor."""
    if doctype not in ("Material Request", "Purchase Order"):
        frappe.throw("Documento no soportado para bitácora de conciliación.")
    doc = frappe.get_doc(doctype, folio)
    if not (doc.has_permission("write") or frappe.has_permission("Purchase Order", "create")):
        frappe.throw("Sin permiso para registrar nota de conciliación.", frappe.PermissionError)
    if not (detalle or "").strip():
        frappe.throw("Escribe el detalle del cambio o ajuste.")
    _agregar_evento_bitacora(doctype, folio, (etapa or "Ajuste / Conciliación").strip(), detalle.strip())
    return {"ok": True}


@frappe.whitelist(methods=["POST"])
def consolidar_cotizaciones_en_oc(cotizaciones_json: str):
    """Agrupa varias Cotizaciones vigentes de un mismo proveedor (de 1 o varias Solicitudes) en una sola OC consolidada."""
    from erpnext.buying.doctype.supplier_quotation.supplier_quotation import make_purchase_order
    from lebaref_compras import comercial

    frappe.has_permission("Purchase Order", "create", throw=True)
    nombres = frappe.parse_json(cotizaciones_json) if isinstance(cotizaciones_json, str) else cotizaciones_json
    if not isinstance(nombres, list) or len(nombres) < 1:
        frappe.throw("Selecciona al menos una cotización vigente.")

    docs_sq = [frappe.get_doc("Supplier Quotation", n) for n in nombres]
    proveedores = {sq.supplier for sq in docs_sq}
    monedas = {sq.currency for sq in docs_sq}
    if len(proveedores) > 1:
        frappe.throw("Para consolidar en una sola Orden de Compra, todas las cotizaciones deben ser del mismo proveedor.")
    if len(monedas) > 1:
        frappe.throw("Todas las cotizaciones a consolidar deben estar en la misma moneda.")

    ya_ordenados = set(
        frappe.get_all(
            "Purchase Order Item",
            filters={"supplier_quotation": ["in", [sq.name for sq in docs_sq]], "docstatus": ["<", 2]},
            pluck="supplier_quotation_item",
        )
    )

    base_sq = docs_sq[0]
    po = make_purchase_order(base_sq.name)
    po.items = [it for it in po.items if it.supplier_quotation_item not in ya_ordenados]

    for extra_sq in docs_sq[1:]:
        po_extra = make_purchase_order(extra_sq.name)
        for it in po_extra.items:
            if it.supplier_quotation_item not in ya_ordenados:
                po.append("items", it)

    if not po.items:
        frappe.throw("Todos los renglones de las cotizaciones seleccionadas ya están incluidos en una Orden de Compra activa.")

    for it in po.items:
        if not it.schedule_date or getdate(it.schedule_date) < getdate(today()):
            it.schedule_date = today()

    comercial.condiciones_en_orden(po, base_sq, desde_comparativa=True)
    trasladar_logistica_a_orden(po, base_sq)
    po.insert()

    mrs_vinculadas = sorted({it.material_request for it in po.items if it.material_request})
    resumen = f"OC consolidada con {len(docs_sq)} cotización(es) y {len(mrs_vinculadas)} solicitud(es): {', '.join(mrs_vinculadas)}"
    _agregar_evento_bitacora("Purchase Order", po.name, "Consolidación OC", resumen)
    for mr_n in mrs_vinculadas:
        _agregar_evento_bitacora("Material Request", mr_n, "Consolidación OC", f"Incluida en OC consolidada {po.name}")

    return {"ok": True, "folio": po.name, "proveedor": po.supplier_name or po.supplier, "solicitudes": mrs_vinculadas}


@frappe.whitelist(methods=["GET"])
def hilo_conductor(doctype: str, folio: str):
    """Construye la vista continua conciliada: Solicitud <-> Cotización <-> OC/Pago <-> Entrega/Foto <-> XML."""
    doc = frappe.get_doc(doctype, folio)
    doc.check_permission("read")

    mr_names = set()
    po_names = set()
    if doctype == "Material Request":
        mr_names.add(doc.name)
        for r in frappe.db.sql(
            """
            SELECT DISTINCT poi.parent AS po
            FROM `tabPurchase Order Item` poi
            LEFT JOIN `tabSupplier Quotation Item` sqi ON sqi.name = poi.supplier_quotation_item
            WHERE poi.docstatus < 2 AND (poi.material_request = %s OR sqi.material_request = %s)
            """,
            (doc.name, doc.name),
            as_dict=True,
        ):
            po_names.add(r.po)
    elif doctype == "Purchase Order":
        po_names.add(doc.name)
        for it in doc.items:
            if it.material_request:
                mr_names.add(it.material_request)

    solicitudes_info = []
    if mr_names:
        for mr in frappe.get_all(
            "Material Request",
            filters={"name": ["in", list(mr_names)]},
            fields=["name", "title", "custom_lebaref_nombre_solicitud", "transaction_date", "status", "custom_lebaref_proposito", "custom_lebaref_en_espera_de"],
        ):
            solicitudes_info.append({
                "folio": mr.name,
                "nombre": mr.custom_lebaref_nombre_solicitud or mr.title or mr.name,
                "fecha": str(mr.transaction_date or ""),
                "proposito": mr.custom_lebaref_proposito or "Compra operativa",
                "en_espera_de": mr.custom_lebaref_en_espera_de or "",
            })

    cotizaciones_info = []
    cond_sq = []
    params_sq = []
    if mr_names:
        cond_sq.append("sqi.material_request IN %s")
        params_sq.append(tuple(mr_names))
    if po_names:
        cond_sq.append("sq.name IN (SELECT DISTINCT supplier_quotation FROM `tabPurchase Order Item` WHERE parent IN %s AND IFNULL(supplier_quotation,'')!='')")
        params_sq.append(tuple(po_names))
    if cond_sq:
        for sq in frappe.db.sql(
            f"""
            SELECT DISTINCT sq.name, sq.supplier_name, sq.supplier, sq.transaction_date, sq.currency,
                            sq.grand_total, sq.custom_lebaref_tc_dof,
                            sq.custom_lebaref_flete_sin_iva, sq.custom_lebaref_modalidad_entrega
            FROM `tabSupplier Quotation` sq
            INNER JOIN `tabSupplier Quotation Item` sqi ON sq.name = sqi.parent
            WHERE sq.docstatus < 2 AND ({" OR ".join(cond_sq)})
            ORDER BY sq.creation DESC
            """,
            tuple(params_sq),
            as_dict=True,
        ):
            cotizaciones_info.append({
                "folio": sq.name,
                "proveedor": sq.supplier_name or sq.supplier,
                "fecha": str(sq.transaction_date or ""),
                "moneda": sq.currency or "MXN",
                "tc_dof": flt(sq.custom_lebaref_tc_dof or 1.0, 4),
                "total": flt(sq.grand_total),
                "flete": flt(sq.custom_lebaref_flete_sin_iva),
                "modalidad": sq.custom_lebaref_modalidad_entrega or "",
            })

    ordenes_info = []
    facturas_info = []
    recepciones_info = []
    if po_names:
        for po in frappe.get_all(
            "Purchase Order",
            filters={"name": ["in", list(po_names)]},
            fields=[
                "name", "supplier", "supplier_name", "transaction_date", "currency", "grand_total",
                "docstatus", "status", "per_received", "per_billed", "custom_lebaref_estado_operativo",
                "custom_lebaref_modalidad_entrega", "custom_lebaref_fecha_promesa_entrega",
                "custom_lebaref_comprobante_pago", "custom_lebaref_foto_entrega_obra",
                "custom_lebaref_reportado_por", "custom_lebaref_tc_dof",
            ],
        ):
            ordenes_info.append({
                "folio": po.name,
                "proveedor": po.supplier_name or po.supplier,
                "fecha": str(po.transaction_date or ""),
                "moneda": po.currency or "MXN",
                "tc_dof": flt(po.custom_lebaref_tc_dof or 1.0, 4),
                "total": flt(po.grand_total),
                "docstatus": cint(po.docstatus),
                "estado_logistico": po.custom_lebaref_estado_operativo or po.status,
                "modalidad": po.custom_lebaref_modalidad_entrega or "",
                "promesa": str(po.custom_lebaref_fecha_promesa_entrega or ""),
                "pct_recibido": round(flt(po.per_received or 0), 1),
                "pct_facturado": round(flt(po.per_billed or 0), 1),
                "comprobante_pago": po.custom_lebaref_comprobante_pago or "",
                "foto_obra": po.custom_lebaref_foto_entrega_obra or "",
                "reportado_por": po.custom_lebaref_reportado_por or "",
            })

        for pr in frappe.db.sql(
            """
            SELECT DISTINCT pr.name, pr.posting_date, pr.supplier_name, pr.docstatus, pr.custom_lebaref_recibido_por
            FROM `tabPurchase Receipt` pr
            INNER JOIN `tabPurchase Receipt Item` pri ON pri.parent = pr.name
            WHERE pr.docstatus < 2 AND pri.purchase_order IN %s
            ORDER BY pr.posting_date DESC
            """,
            (tuple(po_names),),
            as_dict=True,
        ):
            recepciones_info.append({
                "folio": pr.name,
                "fecha": str(pr.posting_date or ""),
                "recibido_por": pr.custom_lebaref_recibido_por or "Almacén",
                "estado": "Confirmada" if cint(pr.docstatus) == 1 else "Borrador",
            })

        for pi in frappe.db.sql(
            """
            SELECT DISTINCT pi.name, pi.posting_date, pi.bill_no, pi.currency, pi.grand_total, pi.docstatus, pi.outstanding_amount
            FROM `tabPurchase Invoice` pi
            INNER JOIN `tabPurchase Invoice Item` pii ON pii.parent = pi.name
            WHERE pi.docstatus < 2
              AND (pii.purchase_order IN %s OR pii.purchase_receipt IN (
                  SELECT DISTINCT parent FROM `tabPurchase Receipt Item` WHERE purchase_order IN %s
              ))
            ORDER BY pi.posting_date DESC
            """,
            (tuple(po_names), tuple(po_names)),
            as_dict=True,
        ):
            facturas_info.append({
                "folio": pi.name,
                "fecha": str(pi.posting_date or ""),
                "uuid": pi.bill_no or "",
                "moneda": pi.currency or "MXN",
                "total": flt(pi.grand_total),
                "estado": "Autorizada" if cint(pi.docstatus) == 1 else "Borrador XML",
                "saldo": flt(pi.outstanding_amount),
            })

    bitacora = []
    for dt_b, nombres_b in (("Material Request", mr_names), ("Purchase Order", po_names)):
        if nombres_b and frappe.db.has_column(dt_b, "custom_lebaref_bitacora_cambios"):
            for row_b in frappe.get_all(dt_b, filters={"name": ["in", list(nombres_b)]}, fields=["name", "custom_lebaref_bitacora_cambios"]):
                try:
                    evs = json.loads(row_b.custom_lebaref_bitacora_cambios or "[]")
                    for ev in evs:
                        ev["documento"] = row_b.name
                        bitacora.append(ev)
                except Exception:
                    pass
    bitacora.sort(key=lambda x: x.get("fecha") or "", reverse=True)

    return {
        "solicitudes": solicitudes_info,
        "cotizaciones": cotizaciones_info,
        "ordenes": ordenes_info,
        "recepciones": recepciones_info,
        "facturas": facturas_info,
        "bitacora": bitacora,
    }


@frappe.whitelist(methods=["GET"])
def tableros_inicio_lebaref():
    """Devuelve los 3 tableros diarios vivos de Inicio Lebaref (FLU-04)."""
    hoy = getdate(today())

    # 1. Solicitudes sin atender (confirmadas o borradores, sin estar pausadas ni concluidas, pendientes de ordenar)
    solicitudes_sin_atender = []
    if frappe.has_permission("Material Request", "read"):
        rows_mr = frappe.db.sql(
            """
            SELECT mr.name, mr.title, mr.custom_lebaref_nombre_solicitud, mr.transaction_date,
                   mr.docstatus, mr.custom_lebaref_solicitante, mr.custom_lebaref_departamento,
                   mr.per_ordered, mr.custom_lebaref_proposito,
                   (SELECT COUNT(*) FROM `tabSupplier Quotation Item` sqi
                    INNER JOIN `tabSupplier Quotation` sq ON sq.name = sqi.parent
                    WHERE sqi.material_request = mr.name AND sq.docstatus = 1) AS n_cot
            FROM `tabMaterial Request` mr
            WHERE mr.docstatus < 2
              AND mr.status NOT IN ('Stopped', 'Cancelled')
              AND mr.material_request_type = 'Purchase'
              AND IFNULL(mr.custom_lebaref_concluida, 0) = 0
              AND IFNULL(mr.custom_lebaref_en_espera_de, '') = ''
              AND IFNULL(mr.per_ordered, 0) < 99.9
            ORDER BY mr.docstatus ASC, mr.transaction_date DESC, mr.creation DESC
            LIMIT 40
            """,
            as_dict=True,
        )
        for r in rows_mr:
            etapa = "Borrador por confirmar" if cint(r.docstatus) == 0 else ("Con cotizaciones — Adjudicar" if cint(r.n_cot) > 0 else "Sin cotizar")
            solicitudes_sin_atender.append({
                "folio": r.name,
                "nombre": r.custom_lebaref_nombre_solicitud or r.title or r.name,
                "fecha": str(r.transaction_date or ""),
                "solicitante": r.custom_lebaref_solicitante or "—",
                "departamento": r.custom_lebaref_departamento or "—",
                "proposito": r.custom_lebaref_proposito or "Compra operativa",
                "etapa": etapa,
            })

    # 2. Compras pendientes de pago por Dirección
    pendientes_pago = []
    # 3. Pagadas / Liberadas: Ir a recolectar vs. En flete (+ XML pendientes)
    ir_a_recolectar = []
    en_flete = []
    xml_pendientes = []

    if frappe.has_permission("Purchase Order", "read"):
        rows_po = frappe.db.sql(
            """
            SELECT po.name, po.supplier, po.supplier_name, po.transaction_date, po.currency, po.grand_total,
                   po.docstatus, po.status, po.per_received, po.per_billed,
                   po.custom_lebaref_estado_operativo, po.custom_lebaref_modalidad_entrega,
                   po.custom_lebaref_dias_flete, po.custom_lebaref_fecha_promesa_entrega,
                   po.custom_lebaref_foto_entrega_obra,
                   sup.custom_lebaref_csf_archivo, sup.custom_lebaref_caratula_archivo
            FROM `tabPurchase Order` po
            LEFT JOIN `tabSupplier` sup ON sup.name = po.supplier
            WHERE po.docstatus < 2
              AND po.status NOT IN ('Closed', 'Cancelled', 'Completed')
            ORDER BY po.transaction_date DESC, po.creation DESC
            LIMIT 80
            """,
            as_dict=True,
        )
        for po in rows_po:
            est = (po.custom_lebaref_estado_operativo or "").strip()
            mod = (po.custom_lebaref_modalidad_entrega or "").strip()
            tiene_csf = bool((po.custom_lebaref_csf_archivo or "").strip())
            tiene_car = bool((po.custom_lebaref_caratula_archivo or "").strip())
            item_base = {
                "folio": po.name,
                "proveedor": po.supplier_name or po.supplier,
                "supplier_id": po.supplier,
                "fecha": str(po.transaction_date or ""),
                "moneda": po.currency or "MXN",
                "total": flt(po.grand_total),
                "docstatus": cint(po.docstatus),
                "estado": est or ("Por autorizar" if cint(po.docstatus) == 0 else po.status),
                "tiene_csf": tiene_csf,
                "tiene_caratula": tiene_car,
                "expediente_ok": tiene_csf and tiene_car,
            }

            if est in ("Esperando pago Dirección", "Pendiente de gestionar pago") or (cint(po.docstatus) == 0 and est != "Consumo directo (Oficina)"):
                pendientes_pago.append(item_base)
            elif est == "Liberado — Ir a recolectar" or (cint(po.docstatus) == 1 and flt(po.per_received) < 99.9 and mod == "Ir a recolectar" and est not in ("Entregado en almacén", "Entregado en obra (con foto)")):
                ir_a_recolectar.append(item_base)
            elif est in ("En flete", "Pagado — Enviar comprobante") or (cint(po.docstatus) == 1 and flt(po.per_received) < 99.9 and est not in ("Entregado en almacén", "Entregado en obra (con foto)", "Consumo directo (Oficina)")):
                promesa_str = str(po.custom_lebaref_fecha_promesa_entrega or "")
                dias_rest = None
                vencido = False
                if promesa_str:
                    dias_rest = date_diff(getdate(promesa_str), hoy)
                    vencido = dias_rest < 0
                en_flete.append(dict(item_base, promesa=promesa_str, dias_restantes=dias_rest, vencido=vencido))

            if cint(po.docstatus) == 1 and flt(po.per_billed or 0) < 99.9 and (flt(po.per_received or 0) > 0 or est in ("Entregado en almacén", "Entregado en obra (con foto)", "Consumo directo (Oficina)")):
                xml_pendientes.append(item_base)

    return {
        "resumen": {
            "sin_atender": len(solicitudes_sin_atender),
            "pendientes_pago_direccion": len(pendientes_pago),
            "ir_a_recolectar": len(ir_a_recolectar),
            "en_flete": len(en_flete),
            "xml_pendientes": len(xml_pendientes),
        },
        "solicitudes_sin_atender": solicitudes_sin_atender[:15],
        "pendientes_pago": pendientes_pago[:15],
        "ir_a_recolectar": ir_a_recolectar[:15],
        "en_flete": en_flete[:15],
        "xml_pendientes": xml_pendientes[:15],
    }
