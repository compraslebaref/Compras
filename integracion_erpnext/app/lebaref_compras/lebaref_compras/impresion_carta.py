"""Formatos de Impresión Tamaño Carta en español con Logo Vectorial Lebaref y Código de Barras Code 128B SVG nativo (DOC-01)."""
import html
import os
import re
import frappe

# Patrones estándar ISO/IEC 15417 Code 128 (valores 0..106)
_CODE128_PATTERNS = (
    "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
    "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
    "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
    "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
    "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
    "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
    "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
    "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
    "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
    "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
    "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
)


def svg_code128(texto, alto=42, ancho_modulo=1.55):
    """Genera un código de barras Code 128B limpio en SVG inline sin dependencias externas."""
    limpio = "".join(ch for ch in str(texto or "").strip() if 32 <= ord(ch) <= 126)[:48]
    if not limpio:
        return ""
    codigos = [104]  # Start Code B
    checksum = 104
    for pos, ch in enumerate(limpio, start=1):
        val = ord(ch) - 32
        codigos.append(val)
        checksum += val * pos
    codigos.append(checksum % 103)
    codigos.append(106)  # Stop

    x = 6.0
    rects = []
    for code in codigos:
        patron = _CODE128_PATTERNS[code]
        es_barra = True
        for dig in patron:
            w = int(dig) * float(ancho_modulo)
            if es_barra:
                rects.append(f'<rect x="{x:.2f}" y="2" width="{w:.2f}" height="{int(alto)}" fill="#0f172a"/>')
            x += w
            es_barra = not es_barra
    ancho_total = x + 6.0
    etiqueta = html.escape(limpio)
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{ancho_total:.0f}" height="{int(alto) + 18}" '
        f'viewBox="0 0 {ancho_total:.0f} {int(alto) + 18}" style="display:block;margin-left:auto;">'
        + "".join(rects)
        + f'<text x="{ancho_total / 2:.1f}" y="{int(alto) + 15}" text-anchor="middle" '
        f'font-family="Consolas, monospace" font-size="11" font-weight="bold" fill="#0f172a">{etiqueta}</text>'
        f'</svg>'
    )


def svg_logo_lebaref(ancho=195):
    """Devuelve el logotipo oficial de Lebaref en SVG vectorial puro para impresión nítida en documentos."""
    ruta = frappe.get_app_path("lebaref_compras", "public", "img", "lebaref_logo_horizontal.svg")
    if os.path.exists(ruta):
        with open(ruta, encoding="utf-8") as f:
            raw = f.read().strip()
        alto = max(24, round(int(ancho) * 385 / 1210))
        return re.sub(
            r'<svg\b[^>]*>',
            f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1210 385" width="{int(ancho)}" height="{alto}" style="display:block;flex-shrink:0;">',
            raw,
            count=1,
        )
    return '<div class="leb-brand">LEBAREF</div>'


_CSS_CARTA = """
<style>
  @page { size: letter portrait; margin: 12mm 14mm; }
  .leb-carta { font-family: 'Segoe UI', Arial, sans-serif; color: #0f172a; font-size: 11.5px; line-height: 1.38; }
  .leb-hdr { display: flex; justify-content: space-between; align-items: center; border-bottom: 2.5px solid #031e34; padding-bottom: 8px; margin-bottom: 12px; gap: 12px; }
  .leb-hdr-brand { display: flex; align-items: center; gap: 12px; }
  .leb-hdr-info { border-left: 1.5px solid #cbd5e1; padding-left: 12px; }
  .leb-brand { font-size: 18px; font-weight: 800; letter-spacing: 0.6px; color: #031e34; }
  .leb-sub-area { font-size: 10px; font-weight: 700; letter-spacing: 0.8px; color: #475569; text-transform: uppercase; }
  .leb-doc-title { font-size: 14px; font-weight: 800; color: #0d5b94; margin-top: 1px; text-transform: uppercase; }
  .leb-meta { width: 100%; border-collapse: collapse; margin-bottom: 12px; background: #f8fafc; border: 1px solid #cbd5e1; }
  .leb-meta td { padding: 5px 8px; border: 1px solid #e2e8f0; font-size: 11px; }
  .leb-meta b { color: #334155; }
  .leb-tbl { width: 100%; border-collapse: collapse; margin-bottom: 14px; }
  .leb-tbl th { background: #0f172a; color: #ffffff; font-weight: 700; font-size: 10.5px; padding: 6px 7px; text-align: left; border: 1px solid #0f172a; }
  .leb-tbl td { padding: 6px 7px; border: 1px solid #cbd5e1; vertical-align: top; font-size: 11px; }
  .leb-tbl tr:nth-child(even) td { background: #f8fafc; }
  .leb-num { text-align: right; white-space: nowrap; }
  .leb-totales { width: 290px; margin-left: auto; border-collapse: collapse; margin-bottom: 18px; }
  .leb-totales td { padding: 5px 8px; border: 1px solid #cbd5e1; font-size: 11.5px; }
  .leb-totales .gran-total td { background: #eff6ff; font-weight: 800; font-size: 12.5px; color: #1e3a8a; }
  .leb-firmas { width: 100%; margin-top: 34px; border-collapse: collapse; page-break-inside: avoid; }
  .leb-firmas td { width: 33.3%; text-align: center; padding: 0 12px; vertical-align: bottom; }
  .leb-linea-firma { border-top: 1px solid #334155; padding-top: 5px; font-size: 10.5px; font-weight: 600; color: #334155; }
  .leb-notas { border: 1px solid #cbd5e1; background: #f8fafc; padding: 7px 10px; margin-bottom: 12px; font-size: 11px; }
</style>
"""

FORMATO_SOLICITUD = _CSS_CARTA + """
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Abastecimiento y Compras</div>
        <div class="leb-doc-title">Solicitud de Material / Requerimiento</div>
        <div style="font-size:11px;color:#475569;">Empresa: {{ doc.company }} · Estado: <b>{{ _(doc.status) }}</b></div>
      </div>
    </div>
    <div>{{ svg_code128(doc.name) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Folio:</b> {{ doc.name }}</td>
      <td><b>Fecha emisión:</b> {{ frappe.utils.formatdate(doc.transaction_date) }}</td>
      <td><b>Fecha requerida:</b> {{ frappe.utils.formatdate(doc.schedule_date) if doc.schedule_date else "—" }}</td>
    </tr>
    <tr>
      <td><b>Departamento:</b> {{ doc.custom_lebaref_departamento or "—" }}</td>
      <td><b>Solicitante:</b> {{ doc.custom_lebaref_solicitante or doc.owner }}</td>
      <td><b>Nombre Solicitud:</b> {{ doc.custom_lebaref_nombre_solicitud or doc.title or "—" }}</td>
    </tr>
    {% if doc.custom_lebaref_solicitud_origen or doc.amended_from %}
    <tr>
      <td colspan="3">
        {% if doc.custom_lebaref_solicitud_origen %}<b>Anexo vinculado a solicitud origen:</b> {{ doc.custom_lebaref_solicitud_origen }} &nbsp;{% endif %}
        {% if doc.amended_from %}<b>Enmienda de:</b> {{ doc.amended_from }}{% endif %}
      </td>
    </tr>
    {% endif %}
  </table>
  {% if doc.custom_lebaref_notas %}
  <div class="leb-notas"><b>Notas / Justificación:</b> {{ doc.custom_lebaref_notas }}</div>
  {% endif %}
  <table class="leb-tbl">
    <thead>
      <tr>
        <th style="width:34px;">#</th>
        <th style="width:105px;">Código</th>
        <th>Artículo / Concepto Solicitado</th>
        <th style="width:115px;">Obra / Partida</th>
        <th class="leb-num" style="width:78px;">Cantidad</th>
        <th style="width:72px;">Unidad</th>
      </tr>
    </thead>
    <tbody>
      {% for row in doc.items %}
      <tr>
        <td>{{ row.idx }}</td>
        <td><b>{{ row.custom_lebaref_clave_vista or ("SIN SKU" if row.custom_lebaref_concepto else row.item_code) }}</b></td>
        <td>{{ row.custom_lebaref_descripcion_vista or row.custom_lebaref_concepto or row.item_name or row.description }}</td>
        <td>{{ row.project or "—" }}{% if row.custom_lebaref_partida %}<br><small>{{ row.custom_lebaref_partida }}</small>{% endif %}</td>
        <td class="leb-num"><b>{{ "{:,.2f}".format(row.qty) }}</b></td>
        <td>{{ row.uom }}</td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
  <table class="leb-firmas">
    <tr>
      <td><div class="leb-linea-firma">Solicitó ({{ doc.custom_lebaref_solicitante or "Área Solicitante" }})</div></td>
      <td><div class="leb-linea-firma">Recibe en Compras</div></td>
      <td><div class="leb-linea-firma">Vo.Bo. Gerencia / Proyecto</div></td>
    </tr>
  </table>
</div>
"""

FORMATO_ORDEN_COMPRA = _CSS_CARTA + """
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Departamento de Compras</div>
        <div class="leb-doc-title">Orden de Compra</div>
        <div style="font-size:11px;color:#475569;">Empresa: {{ doc.company }} · Estado: <b>{{ "BORRADOR (SIN AUTORIZAR)" if doc.docstatus == 0 else _(doc.status) }}</b></div>
      </div>
    </div>
    <div>{{ svg_code128(doc.name) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Folio OC:</b> {{ doc.name }}</td>
      <td><b>Fecha:</b> {{ frappe.utils.formatdate(doc.transaction_date) }}</td>
      <td><b>Entrega requerida:</b> {{ frappe.utils.formatdate(doc.schedule_date) if doc.schedule_date else "—" }}</td>
    </tr>
    <tr>
      <td colspan="2"><b>Proveedor:</b> {{ doc.supplier_name or doc.supplier }} {% if doc.tax_id %}(RFC: {{ doc.tax_id }}){% endif %}</td>
      <td><b>Moneda:</b> {{ doc.currency }}</td>
    </tr>
    <tr>
      <td><b>Condición de pago:</b> {{ doc.payment_terms_template or "Contado / Según acuerdo" }}</td>
      <td><b>Fecha prog. pago:</b> {{ frappe.utils.formatdate(doc.custom_lebaref_fecha_programada_pago) if doc.custom_lebaref_fecha_programada_pago else "—" }}</td>
      <td><b>Regla de flete:</b> {{ doc.shipping_rule or "Incluido / No aplica" }}</td>
    </tr>
  </table>
  <table class="leb-tbl">
    <thead>
      <tr>
        <th style="width:30px;">#</th>
        <th style="width:95px;">Código</th>
        <th>Descripción</th>
        <th style="width:110px;">Obra / Partida</th>
        <th class="leb-num" style="width:65px;">Cant.</th>
        <th style="width:60px;">Unidad</th>
        <th class="leb-num" style="width:85px;">P. Unit.</th>
        <th class="leb-num" style="width:90px;">Importe</th>
      </tr>
    </thead>
    <tbody>
      {% for row in doc.items %}
      <tr>
        <td>{{ row.idx }}</td>
        <td><b>{{ row.custom_lebaref_clave_vista or ("SIN SKU" if row.custom_lebaref_concepto else row.item_code) }}</b></td>
        <td>{{ row.custom_lebaref_descripcion_vista or row.custom_lebaref_concepto or row.item_name or row.description }}</td>
        <td>{{ row.project or "—" }}{% if row.custom_lebaref_partida %}<br><small>{{ row.custom_lebaref_partida }}</small>{% endif %}</td>
        <td class="leb-num">{{ "{:,.2f}".format(row.qty) }}</td>
        <td>{{ row.uom }}</td>
        <td class="leb-num">{{ frappe.utils.fmt_money(row.rate, currency=doc.currency) }}</td>
        <td class="leb-num"><b>{{ frappe.utils.fmt_money(row.amount, currency=doc.currency) }}</b></td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
  <table class="leb-totales">
    <tr><td><b>Subtotal Neto</b></td><td class="leb-num">{{ frappe.utils.fmt_money(doc.net_total, currency=doc.currency) }}</td></tr>
    {% if doc.taxes %}
      {% for tx in doc.taxes %}
      <tr><td><b>{{ tx.description or "Impuesto / Cargo" }}</b></td><td class="leb-num">{{ frappe.utils.fmt_money(tx.tax_amount or 0, currency=doc.currency) }}</td></tr>
      {% endfor %}
    {% else %}
      <tr><td><b>Impuestos / Cargos</b></td><td class="leb-num">{{ frappe.utils.fmt_money(doc.total_taxes_and_charges or 0, currency=doc.currency) }}</td></tr>
    {% endif %}
    <tr class="gran-total"><td>Total Orden ({{ doc.currency }})</td><td class="leb-num">{{ frappe.utils.fmt_money(doc.grand_total, currency=doc.currency) }}</td></tr>
  </table>
  {% if doc.payment_schedule %}
  <table class="leb-tbl" style="margin-bottom:14px;">
    <thead>
      <tr>
        <th>Tramo / Condición de Pago</th>
        <th style="width:120px;">Fecha Vencimiento</th>
        <th class="leb-num" style="width:90px;">Porción (%)</th>
        <th class="leb-num" style="width:120px;">Importe Programado</th>
      </tr>
    </thead>
    <tbody>
      {% for ps in doc.payment_schedule %}
      <tr>
        <td>{{ ps.payment_term or ps.description or "Pago programado" }}</td>
        <td>{{ frappe.utils.formatdate(ps.due_date) if ps.due_date else "—" }}</td>
        <td class="leb-num">{{ "{:,.1f}%".format(ps.invoice_portion or 0) }}</td>
        <td class="leb-num"><b>{{ frappe.utils.fmt_money(ps.payment_amount or 0, currency=doc.currency) }}</b></td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
  {% endif %}
  <table class="leb-firmas">
    <tr>
      <td><div class="leb-linea-firma">Elaboró Compras</div></td>
      <td><div class="leb-linea-firma">Autorizó Responsable / Dirección</div></td>
      <td><div class="leb-linea-firma">Conformidad Proveedor</div></td>
    </tr>
  </table>
</div>
"""

FORMATO_RECEPCION = _CSS_CARTA + """
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Almacén y Control de Entradas</div>
        <div class="leb-doc-title">Vale de Entrada / Recepción de Mercancía</div>
        <div style="font-size:11px;color:#475569;">Empresa: {{ doc.company }} · Fecha: {{ frappe.utils.formatdate(doc.posting_date) }}</div>
      </div>
    </div>
    <div>{{ svg_code128(doc.name) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Folio Recepción:</b> {{ doc.name }}</td>
      <td><b>Proveedor:</b> {{ doc.supplier_name or doc.supplier }}</td>
      <td><b>Remisión Proveedor:</b> {{ doc.supplier_delivery_note or "—" }}</td>
    </tr>
    <tr>
      <td colspan="2"><b>Recibió físicamente:</b> {{ doc.custom_lebaref_recibido_por or "Almacén" }}</td>
      <td><b>Modalidad:</b> {{ "⚠️ Entrada anticipada sin OC (Por regularizar)" if doc.custom_lebaref_por_regularizar else "Con Orden de Compra" }}</td>
    </tr>
  </table>
  {% if doc.custom_lebaref_por_regularizar %}
  <div class="leb-notas" style="background:#fffbeb;border-color:#f59e0b;color:#92400e;">
    <b>⚠️ ENTRADA ANTICIPADA PENDIENTE DE REGULARIZAR:</b> Esta mercancía ingresó físicamente antes de emitirse o autorizarse su Orden de Compra. Debe vincularse posteriormente mediante <i>Regularizar entrada (ALM-02)</i>.
  </div>
  {% endif %}
  {% if doc.remarks %}
  <div class="leb-notas"><b>Observaciones / Destino de recepción:</b> {{ doc.remarks }}</div>
  {% endif %}
  <table class="leb-tbl">
    <thead>
      <tr>
        <th style="width:32px;">#</th>
        <th style="width:100px;">Código</th>
        <th>Material Recibido</th>
        <th style="width:120px;">Orden Origen / Obra</th>
        <th style="width:120px;">Almacén Destino</th>
        <th class="leb-num" style="width:82px;">Recibido</th>
        <th style="width:95px;">Equiv. Stock</th>
      </tr>
    </thead>
    <tbody>
      {% for row in doc.items %}
      <tr>
        <td>{{ row.idx }}</td>
        <td>
          <b>{{ "LIBRE" if row.custom_lebaref_concepto else row.item_code }}</b>
          {% if row.custom_lebaref_articulo_original %}
          <br><small style="color:#b45309;font-weight:700;">Sustituye a: {{ row.custom_lebaref_articulo_original }}</small>
          {% endif %}
        </td>
        <td>
          {{ row.custom_lebaref_concepto or row.item_name }}
          {% if row.custom_lebaref_sustitucion %}
          {% set sust_motivo = frappe.db.get_value("Lebaref Sustitucion", row.custom_lebaref_sustitucion, "motivo") %}
          <br><small style="color:#475569;">Autorización sustitución: <b>{{ row.custom_lebaref_sustitucion }}</b>{% if sust_motivo %} · Motivo: <i>{{ sust_motivo }}</i>{% endif %}</small>
          {% endif %}
        </td>
        <td>{{ row.purchase_order or "—" }}{% if row.project %}<br><small>{{ row.project }}</small>{% endif %}</td>
        <td>{{ row.warehouse or "—" }}</td>
        <td class="leb-num"><b>{{ "{:,.2f}".format(row.qty) }} {{ row.uom }}</b></td>
        <td>{{ "{:,.2f}".format(row.stock_qty) }} {{ row.stock_uom }}</td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
  <table class="leb-firmas">
    <tr>
      <td><div class="leb-linea-firma">Entregó (Proveedor / Transporte)</div></td>
      <td><div class="leb-linea-firma">Recibió y Verificó Almacén</div></td>
      <td><div class="leb-linea-firma">Control Documental / Compras</div></td>
    </tr>
  </table>
</div>
"""

FORMATO_SALIDA_SOBRANTE = _CSS_CARTA + """
{% set es_sobrante_hist = doc.custom_lebaref_tipo_sobrante %}
{% set es_consumo_obra = (doc.custom_lebaref_origen and (doc.purpose == "Material Issue" or doc.stock_entry_type == "Material Issue")) %}
{% set es_retorno_vinc = (doc.custom_lebaref_origen and not es_consumo_obra) %}
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Control de Bodega, Resguardo y Consumo en Obra</div>
        <div class="leb-doc-title">
          {% if es_sobrante_hist %}
            Acta de Sobrante Histórico ({{ doc.custom_lebaref_tipo_sobrante }}){% if doc.docstatus == 0 %} · BORRADOR{% endif %}
          {% elif es_consumo_obra %}
            Vale de Consumo Real de Material en Obra
          {% elif es_retorno_vinc %}
            Vale de Retorno de Sobrante a Almacén
          {% else %}
            Vale de Entrega / Resguardo de Material en Obra
          {% endif %}
        </div>
        <div style="font-size:11px;color:#475569;">Fecha: {{ frappe.utils.formatdate(doc.posting_date) }} · Movimiento ERPNext: {{ _(doc.stock_entry_type) }}</div>
      </div>
    </div>
    <div>{{ svg_code128(doc.name) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Folio Movimiento:</b> {{ doc.name }}</td>
      <td><b>Obra / Proyecto:</b> {{ doc.project or "—" }}</td>
      <td>
        {% if es_sobrante_hist %}
          <b>Acta Física Origen:</b> {{ doc.custom_lebaref_referencia_sobrante or "—" }}
        {% else %}
          <b>Entrega Origen:</b> {{ doc.custom_lebaref_origen or "N/A (Entrega Original a Obra)" }}
        {% endif %}
      </td>
    </tr>
    {% if es_sobrante_hist %}
    <tr>
      <td><b>Tratamiento (ALM-07):</b> {{ doc.custom_lebaref_tipo_sobrante }}</td>
      <td><b>Persona que entrega:</b> {{ doc.custom_lebaref_persona_sobrante or "—" }}</td>
      <td><b>Verificación Stock:</b> {{ doc.custom_lebaref_confirmacion_sobrante or "Existencia ya contabilizada" }}</td>
    </tr>
    {% endif %}
    <tr>
      <td colspan="3"><b>{{ "Motivo / Actividad en Obra:" if es_consumo_obra else ("Motivo de Recuperación:" if es_sobrante_hist else "Recibe / Observaciones:") }}</b> {{ doc.remarks or "—" }}</td>
    </tr>
  </table>
  <table class="leb-tbl">
    <thead>
      <tr>
        <th style="width:32px;">#</th>
        <th style="width:105px;">Código</th>
        <th>Descripción del Material</th>
        <th style="width:125px;">Almacén Origen</th>
        <th style="width:125px;">Almacén Destino</th>
        <th class="leb-num" style="width:90px;">Cantidad</th>
      </tr>
    </thead>
    <tbody>
      {% for row in doc.items %}
      <tr>
        <td>{{ row.idx }}</td>
        <td><b>{{ row.item_code }}</b></td>
        <td>
          {{ row.item_name }}
          {% if row.custom_lebaref_partida %}<br><small>Partida: {{ row.custom_lebaref_partida }}</small>{% endif %}
          {% if es_sobrante_hist == "Entrada no contabilizada" and row.basic_rate %}
          <br><small style="color:#475569;">Valor unitario asignado: {{ frappe.utils.fmt_money(row.basic_rate, currency="MXN") }}</small>
          {% endif %}
        </td>
        <td>{{ row.s_warehouse or "—" }}</td>
        <td>{{ row.t_warehouse or ("Consumo definitivo en obra" if es_consumo_obra else "—") }}</td>
        <td class="leb-num"><b>{{ "{:,.2f}".format(row.qty) }} {{ row.uom }}</b></td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
  <table class="leb-firmas">
    <tr>
      <td><div class="leb-linea-firma">{{ "Entregó Sobrante" if es_sobrante_hist else ("Registró Consumo" if es_consumo_obra else "Despachó Almacén") }}</div></td>
      <td><div class="leb-linea-firma">{{ "Verificó Almacén" if es_sobrante_hist else "Conformidad Residente / Obra" }}</div></td>
      <td><div class="leb-linea-firma">Autorizó Responsable</div></td>
    </tr>
  </table>
</div>
"""

FORMATO_PRESTAMO_HER = _CSS_CARTA + """
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Custodia de Herramientas</div>
        <div class="leb-doc-title">Vale de Resguardo y Préstamo de Herramientas</div>
        <div style="font-size:11px;color:#475569;">Estado actual: <b>{{ doc.estado }}</b></div>
      </div>
    </div>
    <div>{{ svg_code128(doc.name) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Folio Préstamo:</b> {{ doc.name }}</td>
      <td><b>Fecha salida:</b> {{ frappe.utils.formatdate(doc.fecha) }}</td>
      <td><b>Fecha compromiso:</b> {{ frappe.utils.formatdate(doc.compromiso) }}</td>
    </tr>
    <tr>
      <td><b>Responsable:</b> {{ doc.responsable }}</td>
      <td><b>Persona que recibe:</b> {{ doc.recibe }}</td>
      <td><b>Obra:</b> {{ doc.obra }} {% if doc.kit %}(Kit: {{ doc.kit }}){% endif %}</td>
    </tr>
  </table>
  {% if doc.notas %}<div class="leb-notas"><b>Notas:</b> {{ doc.notas }}</div>{% endif %}
  <table class="leb-tbl">
    <thead>
      <tr>
        <th style="width:36px;">#</th>
        <th style="width:140px;">Código Individual (HER)</th>
        <th style="width:120px;">Estado Retorno</th>
        <th style="width:110px;">Fecha Retorno</th>
        <th>Observaciones / Incidencias</th>
      </tr>
    </thead>
    <tbody>
      {% for row in doc.items %}
      <tr>
        <td>{{ row.idx }}</td>
        <td><b>{{ row.herramienta }}</b></td>
        <td>{{ row.estado }}</td>
        <td>{{ frappe.utils.formatdate(row.fecha_retorno) if row.fecha_retorno else "En obra" }}</td>
        <td>{{ row.observaciones or "—" }}</td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
  <div style="font-size:10px;color:#475569;margin-bottom:8px;">
    Me comprometo a cuidar las herramientas descritas y devolverlas en la fecha compromiso; cualquier daño por mal uso o extravío deberá reportarse de inmediato a Almacén.
  </div>
  <table class="leb-firmas">
    <tr>
      <td><div class="leb-linea-firma">Entregó Almacén</div></td>
      <td><div class="leb-linea-firma">Recibe en Resguardo ({{ doc.recibe }})</div></td>
      <td><div class="leb-linea-firma">Responsable ({{ doc.responsable }})</div></td>
    </tr>
  </table>
</div>
"""

FORMATO_CHECKLIST_VEH = _CSS_CARTA + """
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Flotilla Vehicular</div>
        <div class="leb-doc-title">Check-list de Inspección Vehicular</div>
        <div style="font-size:11px;color:#475569;">Unidad: <b>{{ doc.vehiculo }}</b> · Fecha: {{ frappe.utils.formatdate(doc.fecha) }}</div>
      </div>
    </div>
    <div>{{ svg_code128(doc.vehiculo) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Folio Revisión:</b> {{ doc.name }}</td>
      <td><b>Kilometraje:</b> {{ "{:,}".format(doc.km or 0) }} km</td>
      <td><b>Combustible:</b> {{ doc.nivel }}</td>
    </tr>
    <tr>
      <td colspan="3"><b>Inspector responsable:</b> {{ doc.inspector }}</td>
    </tr>
  </table>
  <table class="leb-tbl">
    <thead>
      <tr><th>Punto de Inspección</th><th style="width:160px;">Dictamen</th></tr>
    </thead>
    <tbody>
      <tr><td>Estado y presión de llantas</td><td><b>{{ doc.llantas }}</b></td></tr>
      <tr><td>Niveles de aceite, anticongelante y fluidos</td><td><b>{{ doc.fluidos }}</b></td></tr>
      <tr><td>Sistema de frenos, luces e intermitentes</td><td><b>{{ doc.frenos_luces }}</b></td></tr>
      <tr><td>Carrocería, espejos y limpieza general</td><td><b>{{ doc.carroceria }}</b></td></tr>
    </tbody>
  </table>
  <div class="leb-notas"><b>Observaciones / Hallazgos:</b> {{ doc.observaciones or "Sin observaciones." }}</div>
  <table class="leb-firmas">
    <tr>
      <td><div class="leb-linea-firma">Inspector ({{ doc.inspector }})</div></td>
      <td><div class="leb-linea-firma">Operador de la Unidad</div></td>
      <td><div class="leb-linea-firma">Control de Flotilla</div></td>
    </tr>
  </table>
</div>
"""

FORMATO_RESPONSIVA_EPP = _CSS_CARTA + """
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Seguridad Industrial y Almacén (NOM-017-STPS)</div>
        <div class="leb-doc-title">Carta Responsiva de Entrega de EPP y Equipo</div>
        <div style="font-size:11px;color:#475569;">Fecha de entrega: {{ frappe.utils.formatdate(doc.posting_date) }} · Folio: <b>{{ doc.name }}</b></div>
      </div>
    </div>
    <div>{{ svg_code128(doc.name) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Folio Salida:</b> {{ doc.name }}</td>
      <td><b>Obra / Centro de Trabajo:</b> {{ doc.project or "Operación General" }}</td>
      <td><b>Tipo Movimiento:</b> {{ _(doc.stock_entry_type) }}</td>
    </tr>
    <tr>
      <td colspan="3"><b>Colaborador Receptor / Observaciones:</b> {{ doc.remarks or "—" }}</td>
    </tr>
  </table>
  <table class="leb-tbl">
    <thead>
      <tr>
        <th style="width:32px;">#</th>
        <th style="width:110px;">Código</th>
        <th>Equipo de Protección Personal (EPP) / Dotación Entregada</th>
        <th style="width:130px;">Almacén Origen</th>
        <th class="leb-num" style="width:85px;">Cantidad</th>
        <th style="width:95px;">Estado Entrega</th>
      </tr>
    </thead>
    <tbody>
      {% for row in doc.items %}
      <tr>
        <td>{{ row.idx }}</td>
        <td><b>{{ row.item_code }}</b></td>
        <td>{{ row.item_name }}{% if row.custom_lebaref_partida %}<br><small>Área / Partida: {{ row.custom_lebaref_partida }}</small>{% endif %}</td>
        <td>{{ row.s_warehouse or "—" }}</td>
        <td class="leb-num"><b>{{ "{:,.2f}".format(row.qty) }} {{ row.uom }}</b></td>
        <td>Nuevo / Óptimo</td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
  <div class="leb-notas" style="font-size:10.5px;line-height:1.45;">
    <b>Declaratoria y Compromiso del Colaborador:</b> Hago constar que recibí en óptimas condiciones el Equipo de Protección Personal (EPP) y/o dotación arriba detallada, así como las indicaciones para su uso, revisión, limpieza y resguardo conforme a la normatividad de seguridad industrial vigente (NOM-017-STPS) y al reglamento interno de <b>LEBAREF</b>. Me comprometo a portarlo obligatoriamente durante mis actividades operativas y a reportar de inmediato cualquier deterioro, desgaste o extravío para su reposición oportuna.
  </div>
  <table class="leb-firmas">
    <tr>
      <td><div class="leb-linea-firma">Entregó Almacén / Seguridad Industrial</div></td>
      <td><div class="leb-linea-firma">Nombre y Firma del Colaborador que Recibe</div></td>
      <td><div class="leb-linea-firma">Supervisor / Residente de Obra</div></td>
    </tr>
  </table>
</div>
"""

FORMATO_FICHA_HER = _CSS_CARTA + """
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Control de Activos y Herramientas</div>
        <div class="leb-doc-title">Ficha de Identificación y Etiqueta de Herramienta</div>
        <div style="font-size:11px;color:#475569;">Código Individual: <b>{{ doc.codigo }}</b> · Estado: <b>{{ doc.estado }}</b></div>
      </div>
    </div>
    <div>{{ svg_code128(doc.codigo) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Código HER:</b> {{ doc.codigo }}</td>
      <td><b>SKU Catálogo:</b> {{ doc.articulo or "—" }}</td>
      <td><b>Núm. Serie:</b> {{ doc.serie or "S/N" }}</td>
    </tr>
    <tr>
      <td><b>Marca / Modelo:</b> {{ doc.marca_modelo or "—" }}</td>
      <td><b>Ubicación Base:</b> {{ doc.ubicacion or "—" }}</td>
      <td><b>Resguardo Actual:</b> {{ doc.responsable or "En almacén" }} {% if doc.obra %}({{ doc.obra }}){% endif %}</td>
    </tr>
    <tr>
      <td><b>Fecha Compra:</b> {{ frappe.utils.formatdate(doc.fecha_compra) if doc.fecha_compra else "—" }}</td>
      <td><b>Garantía Hasta:</b> {{ frappe.utils.formatdate(doc.fin_garantia) if doc.fin_garantia else "Sin registro" }}</td>
      <td><b>Costo Adquisición:</b> {{ frappe.utils.fmt_money(doc.costo_adquisicion or 0, currency=doc.moneda or "MXN") }}</td>
    </tr>
  </table>
  <div style="margin-top:18px;border:2px dashed #475569;border-radius:8px;padding:12px 16px;width:300px;background:#ffffff;">
    <div style="font-size:9.5px;font-weight:800;color:#031e34;text-transform:uppercase;letter-spacing:0.6px;margin-bottom:3px;">✂ Etiqueta Recortable para Gabinete / Herramienta</div>
    <div style="font-size:12px;font-weight:700;color:#0f172a;margin-bottom:4px;">{{ doc.marca_modelo or doc.articulo }} · <span style="color:#0d5b94;">{{ doc.ubicacion or "" }}</span></div>
    <div style="text-align:center;">{{ svg_code128(doc.codigo, 38, 1.5) }}</div>
  </div>
</div>
"""

FORMATO_CARATULA_KIT = _CSS_CARTA + """
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Carátula Escaneable para Tapa de Maletín (KIT)</div>
        <div class="leb-doc-title">{{ doc.codigo }} · {{ doc.nombre }}</div>
        <div style="font-size:11px;color:#475569;">Especialidad: <b>{{ doc.especialidad or "General" }}</b> · Ubicación: <b>{{ doc.ubicacion or "—" }}</b></div>
      </div>
    </div>
    <div>{{ svg_code128(doc.codigo) }}</div>
  </div>
  <div class="leb-notas">
    <b>Instrucción operativa (Datamax S30W):</b> Coloca esta hoja plastificada en la tapa del maletín. Escanea arriba el código del kit para préstamo completo/selectivo, o escanea cualquier herramienta individual al verificar su retorno.
  </div>
  <table class="leb-tbl">
    <thead>
      <tr>
        <th style="width:32px;">#</th>
        <th style="width:120px;">Código HER</th>
        <th>Herramienta Integrante</th>
        <th style="width:200px;text-align:center;">Código de Barras Individual</th>
        <th style="width:95px;text-align:center;">Check Salida / Retorno</th>
      </tr>
    </thead>
    <tbody>
      {% for row in doc.items %}
      {% set hdoc = frappe.db.get_value("Lebaref Herramienta", row.herramienta, ["marca_modelo", "articulo", "serie"], as_dict=True) %}
      <tr>
        <td>{{ row.idx }}</td>
        <td><b>{{ row.herramienta }}</b><br><small>{{ hdoc.serie if hdoc and hdoc.serie else "" }}</small></td>
        <td>{{ (hdoc.marca_modelo or hdoc.articulo) if hdoc else row.herramienta }}</td>
        <td style="text-align:center;background:#ffffff;">{{ svg_code128(row.herramienta, 28, 1.25) }}</td>
        <td style="text-align:center;font-size:11px;">☐ Salida<br>☐ Retorno</td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
</div>
"""

FORMATO_TARJETON_VEH = _CSS_CARTA + """
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Control Operativo de Flotilla</div>
        <div class="leb-doc-title">Tarjetón Escaneable de Unidad Vehicular ({{ doc.codigo }})</div>
        <div style="font-size:11px;color:#475569;">Placas: <b>{{ doc.placas }}</b> · Unidad: <b>{{ doc.marca_modelo }}</b></div>
      </div>
    </div>
    <div>{{ svg_code128(doc.codigo) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Código Unidad:</b> {{ doc.codigo }}</td>
      <td><b>Placas:</b> {{ doc.placas }}</td>
      <td><b>Estado:</b> {{ doc.estado }}</td>
    </tr>
    <tr>
      <td><b>Responsable Habitual:</b> {{ doc.responsable }}</td>
      <td><b>Combustible:</b> {{ doc.combustible }} (Tanque: {{ doc.capacidad_tanque }} L)</td>
      <td><b>Seguro Vigente Hasta:</b> <b>{{ frappe.utils.formatdate(doc.vigencia_seguro) if doc.vigencia_seguro else "—" }}</b></td>
    </tr>
    <tr>
      <td><b>Kilometraje Actual:</b> {{ "{:,}".format(doc.km_actual or 0) }} km</td>
      <td><b>Intervalo Servicio:</b> Cada {{ "{:,}".format(doc.intervalo_servicio or 10000) }} km</td>
      <td><b>Próximo Servicio:</b> <b>{{ "{:,}".format(doc.proximo_servicio or 0) }} km</b></td>
    </tr>
  </table>
  <div style="margin-top:16px;border:2px dashed #031e34;border-radius:8px;padding:12px 16px;width:320px;background:#ffffff;">
    <div style="font-size:9.5px;font-weight:800;color:#031e34;text-transform:uppercase;">✂ Tarjeta para Guantera / Llavero de Unidad</div>
    <div style="font-size:12.5px;font-weight:700;margin:3px 0 5px 0;">{{ doc.codigo }} · {{ doc.marca_modelo }} ({{ doc.placas }})</div>
    <div>{{ svg_code128(doc.codigo, 38, 1.5) }}</div>
  </div>
</div>
"""

PRINT_FORMATS = (
    ("Lebaref Carta - Solicitud", "Material Request", FORMATO_SOLICITUD),
    ("Lebaref Carta - Orden de Compra", "Purchase Order", FORMATO_ORDEN_COMPRA),
    ("Lebaref Carta - Recepción Almacén", "Purchase Receipt", FORMATO_RECEPCION),
    ("Lebaref Carta - Salida o Sobrante", "Stock Entry", FORMATO_SALIDA_SOBRANTE),
    ("Lebaref Carta - Responsiva EPP y Equipo", "Stock Entry", FORMATO_RESPONSIVA_EPP),
    ("Lebaref Carta - Vale Préstamo Herramienta", "Lebaref Prestamo", FORMATO_PRESTAMO_HER),
    ("Lebaref Carta - Check-list Vehicular", "Lebaref Revision Vehiculo", FORMATO_CHECKLIST_VEH),
    ("Lebaref Carta - Ficha y Etiqueta Herramienta", "Lebaref Herramienta", FORMATO_FICHA_HER),
    ("Lebaref Carta - Carátula de Maletín Kit", "Lebaref Kit", FORMATO_CARATULA_KIT),
    ("Lebaref Carta - Tarjetón Vehicular", "Lebaref Vehiculo", FORMATO_TARJETON_VEH),
)


def _extraer_anaquel(descripcion: str) -> str:
    txt = descripcion or ""
    if "[Anaquel:" in txt:
        return txt.split("[Anaquel:", 1)[1].split("]", 1)[0].strip()
    return ""


def _svg_code128_centrado(texto, alto=32, ancho_modulo=1.32):
    raw = svg_code128(texto, alto=alto, ancho_modulo=ancho_modulo)
    return raw.replace("margin-left:auto;", "margin:0 auto;max-width:100%;height:auto;")


@frappe.whitelist(methods=["GET"])
def opciones_hojas_codigos():
    """Devuelve las categorías y anaqueles reales disponibles para filtrar hojas Carta de códigos de barras."""
    grupos = []
    if frappe.has_permission("Item Group", "read"):
        grupos = frappe.get_list("Item Group", filters={"is_group": 0}, pluck="name", order_by="name asc", limit_page_length=200)

    anaqueles = set()
    if frappe.has_permission("Item", "read"):
        for d in frappe.get_list("Item", filters={"disabled": 0}, pluck="description", limit_page_length=2000):
            anq = _extraer_anaquel(d)
            if anq:
                anaqueles.add(anq)
    if frappe.has_permission("Lebaref Herramienta", "read"):
        for ub in frappe.get_list("Lebaref Herramienta", pluck="ubicacion", limit_page_length=1000):
            if ub and ub.strip():
                anaqueles.add(ub.strip())

    return {
        "grupos": grupos,
        "anaqueles": sorted(anaqueles),
    }


@frappe.whitelist(methods=["GET"])
def generar_html_codigos_carta(
    origen="articulos",
    formato="catalogo_12",
    grupo="",
    anaquel="",
    solo_con_stock=0,
    busqueda="",
    copias=1,
):
    """Genera un documento HTML Tamaño Carta listo para imprimir con códigos de barras Code 128B vectoriales."""
    origen = (origen or "articulos").strip().lower()
    formato = (formato or "catalogo_12").strip().lower()
    grupo = (grupo or "").strip()
    anaquel = (anaquel or "").strip()
    solo_stock = int(solo_con_stock or 0) == 1
    q = (busqueda or "").strip().lower()
    repeticiones = max(1, min(30, int(copias or 1)))

    tarjetas = []
    titulo_origen = "Catálogo de Artículos (SKUs)"

    if origen == "articulos":
        if not frappe.has_permission("Item", "read"):
            frappe.throw("No tienes permiso para leer artículos.", frappe.PermissionError)
        filtros_it = {"disabled": 0, "name": ["!=", "LEBAREF-CONCEPTO-LIBRE"]}
        if grupo:
            filtros_it["item_group"] = grupo
        items = frappe.get_list(
            "Item",
            filters=filtros_it,
            fields=["name", "item_code", "item_name", "item_group", "stock_uom", "image", "description"],
            order_by="item_code asc",
            limit_page_length=2000,
        )
        barcodes_map = {}
        for bc in frappe.get_all("Item Barcode", fields=["parent", "barcode"], order_by="idx asc"):
            if bc.parent not in barcodes_map and bc.barcode:
                barcodes_map[bc.parent] = bc.barcode

        stock_map = {}
        if frappe.has_permission("Bin", "read"):
            for b in frappe.get_list("Bin", filters={"actual_qty": [">", 0]}, fields=["item_code", "actual_qty"], limit_page_length=5000):
                stock_map[b.item_code] = stock_map.get(b.item_code, 0.0) + float(b.actual_qty or 0.0)

        for it in items:
            code = it.item_code or it.name
            anq = _extraer_anaquel(it.description)
            qty = stock_map.get(code, 0.0)
            if solo_stock and qty <= 0:
                continue
            if anaquel and anq.lower() != anaquel.lower():
                continue
            bc_val = barcodes_map.get(it.name) or code
            if q and not any(q in str(v or "").lower() for v in (code, bc_val, it.item_name, it.item_group, anq)):
                continue
            tarjetas.append({
                "codigo": code,
                "barcode": bc_val,
                "nombre": it.item_name or code,
                "subtitulo": it.item_group or "",
                "ubicacion": anq or "Almacén General",
                "extra": f"Stock: {qty:g} {it.stock_uom or ''}".strip() if qty > 0 else (it.stock_uom or ""),
                "imagen": it.image or "",
            })

    elif origen == "herramientas":
        titulo_origen = "Custodia de Herramientas Individuales (HER)"
        if not frappe.has_permission("Lebaref Herramienta", "read"):
            frappe.throw("No tienes permiso para leer herramientas.", frappe.PermissionError)
        hers = frappe.get_list(
            "Lebaref Herramienta",
            fields=["name", "codigo", "articulo", "marca_modelo", "serie", "ubicacion", "estado"],
            order_by="codigo asc",
            limit_page_length=1000,
        )
        for h in hers:
            ub = (h.ubicacion or "").strip()
            if anaquel and ub.lower() != anaquel.lower():
                continue
            if q and not any(q in str(v or "").lower() for v in (h.codigo, h.articulo, h.marca_modelo, h.serie, ub)):
                continue
            tarjetas.append({
                "codigo": h.codigo,
                "barcode": h.codigo,
                "nombre": h.marca_modelo or h.articulo or h.codigo,
                "subtitulo": f"SKU: {h.articulo or '—'} · Serie: {h.serie or 'S/N'}",
                "ubicacion": ub or "Gabinete",
                "extra": h.estado or "Disponible",
                "imagen": "",
            })

    elif origen == "kits":
        titulo_origen = "Maletines y Kits de Cuadrilla (KIT)"
        if not frappe.has_permission("Lebaref Kit", "read"):
            frappe.throw("No tienes permiso para leer kits.", frappe.PermissionError)
        for k in frappe.get_list("Lebaref Kit", fields=["codigo", "nombre", "especialidad", "ubicacion"], order_by="codigo asc", limit_page_length=500):
            ub = (k.ubicacion or "").strip()
            if anaquel and ub.lower() != anaquel.lower():
                continue
            if q and not any(q in str(v or "").lower() for v in (k.codigo, k.nombre, k.especialidad, ub)):
                continue
            tarjetas.append({
                "codigo": k.codigo,
                "barcode": k.codigo,
                "nombre": k.nombre or k.codigo,
                "subtitulo": k.especialidad or "Kit Operativo",
                "ubicacion": ub or "Almacén",
                "extra": "KIT",
                "imagen": "",
            })

    elif origen == "vehiculos":
        titulo_origen = "Unidades de Flotilla Vehicular (VEH)"
        if not frappe.has_permission("Lebaref Vehiculo", "read"):
            frappe.throw("No tienes permiso para leer vehículos.", frappe.PermissionError)
        for v in frappe.get_list("Lebaref Vehiculo", fields=["codigo", "placas", "marca_modelo", "combustible", "responsable"], order_by="codigo asc", limit_page_length=200):
            if q and not any(q in str(val or "").lower() for val in (v.codigo, v.placas, v.marca_modelo, v.responsable)):
                continue
            tarjetas.append({
                "codigo": v.codigo,
                "barcode": v.codigo,
                "nombre": v.marca_modelo or v.codigo,
                "subtitulo": f"Placas: {v.placas or '—'} · {v.combustible or ''}",
                "ubicacion": v.responsable or "Flotilla",
                "extra": "VEHÍCULO",
                "imagen": "",
            })

    if repeticiones > 1:
        expandidas = []
        for t in tarjetas:
            for _ in range(repeticiones):
                expandidas.append(t)
        tarjetas = expandidas

    if not tarjetas:
        frappe.throw("No se encontraron registros con los filtros seleccionados para generar la hoja Carta.")

    # Limitar a máximo 600 etiquetas por tiraje para mantener respuesta instantánea en navegador
    tarjetas = tarjetas[:600]
    logo_hdr = svg_logo_lebaref(175)

    if formato == "etiquetas_30":
        # Planilla 3x10 Tamaño Carta (medidas compatibles con hoja adherible Avery 5160: 66.7mm x 25.4mm)
        paginas = [tarjetas[i:i + 30] for i in range(0, len(tarjetas), 30)]
        bloques_pag = []
        for p_idx, lote in enumerate(paginas):
            celdas = []
            for t in lote:
                bc_svg = _svg_code128_centrado(t["barcode"], alto=23, ancho_modulo=1.16)
                celdas.append(
                    f'<div class="lbl-cell">'
                    f'<div class="lbl-top"><span>LEBAREF · {html.escape(t["ubicacion"][:18])}</span><b>{html.escape(t["codigo"])}</b></div>'
                    f'<div class="lbl-name">{html.escape(t["nombre"][:42])}</div>'
                    f'<div class="lbl-bc">{bc_svg}</div>'
                    f'</div>'
                )
            salto = ' style="page-break-after:always;"' if p_idx < len(paginas) - 1 else ""
            bloques_pag.append(f'<div class="sheet-30"{salto}>{"".join(celdas)}</div>')

        return f"""<!doctype html><html><head><meta charset="utf-8">
<title>Planilla 30 Etiquetas Carta · Lebaref ({len(tarjetas)})</title>
<style>
  @page {{ size: letter portrait; margin: 12.7mm 4.8mm; }}
  * {{ box-sizing: border-box; }}
  body {{ margin: 0; padding: 0; font-family: 'Segoe UI', Arial, sans-serif; color: #0f172a; background: #fff; }}
  .no-print {{ background: #031e34; color: #fff; padding: 8px 16px; display: flex; justify-content: space-between; align-items: center; font-size: 12px; }}
  .no-print button {{ background: #0d5b94; color: #fff; border: none; border-radius: 5px; padding: 6px 14px; font-weight: 700; cursor: pointer; }}
  @media print {{ .no-print {{ display: none !important; }} }}
  .sheet-30 {{ display: grid; grid-template-columns: repeat(3, 66.6mm); grid-auto-rows: 25.4mm; column-gap: 3.1mm; row-gap: 0mm; justify-content: center; }}
  .lbl-cell {{ width: 66.6mm; height: 25.4mm; border: 0.4px dashed #cbd5e1; padding: 1.5mm 2.2mm; overflow: hidden; display: flex; flex-direction: column; justify-content: space-between; background: #fff; }}
  .lbl-top {{ display: flex; justify-content: space-between; font-size: 7.5px; color: #475569; text-transform: uppercase; line-height: 1.1; }}
  .lbl-top b {{ color: #031e34; font-size: 8px; }}
  .lbl-name {{ font-size: 8.5px; font-weight: 700; color: #0f172a; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; line-height: 1.15; }}
  .lbl-bc svg {{ max-height: 14.5mm; width: auto; }}
</style></head><body>
<div class="no-print">
  <div><b>Planilla Carta 3×10 (30 etiquetas por hoja · Avery 5160 / Recortable)</b> — Total: {len(tarjetas)} etiquetas ({len(paginas)} hoja/s)</div>
  <button onclick="window.print()">🖨️ Imprimir ahora (Tamaño Carta · Escala 100%)</button>
</div>
{"".join(bloques_pag)}
</body></html>"""

    # Formato Catálogo Visual Escaneable (catalogo_12: 3x4 = 12 tarjetas/hoja, o tarjetas_6: 2x3 = 6 tarjetas grandes/hoja)
    es_grande = formato == "tarjetas_6"
    por_pagina = 6 if es_grande else 12
    cols_css = "repeat(2, minmax(0, 1fr))" if es_grande else "repeat(3, minmax(0, 1fr))"
    alto_bc = 40 if es_grande else 30
    mod_bc = 1.45 if es_grande else 1.25

    filtros_txt = " · ".join(
        x for x in (
            titulo_origen,
            f"Categoría: {grupo}" if grupo else "",
            f"Anaquel: {anaquel}" if anaquel else "",
            "Solo con stock disponible" if solo_stock else "",
            f"Filtro: '{busqueda}'" if busqueda else "",
        ) if x
    )

    paginas = [tarjetas[i:i + por_pagina] for i in range(0, len(tarjetas), por_pagina)]
    html_paginas = []
    for p_idx, lote in enumerate(paginas):
        cards_html = []
        for t in lote:
            img_tag = (
                f'<img src="{html.escape(t["imagen"])}" class="cat-img" alt="">'
                if t["imagen"]
                else f'<div class="cat-noimg">{html.escape(t["codigo"][:8])}</div>'
            )
            bc_svg = _svg_code128_centrado(t["barcode"], alto=alto_bc, ancho_modulo=mod_bc)
            cards_html.append(
                f'<div class="cat-card">'
                f'  <div class="cat-top">'
                f'    {img_tag}'
                f'    <div class="cat-meta">'
                f'      <div class="cat-badges"><span class="b-code">{html.escape(t["codigo"])}</span><span class="b-loc">{html.escape(t["ubicacion"][:20])}</span></div>'
                f'      <div class="cat-title">{html.escape(t["nombre"][:78])}</div>'
                f'      <div class="cat-sub">{html.escape(t["subtitulo"][:38])}{(" · <b>" + html.escape(t["extra"]) + "</b>") if t["extra"] else ""}</div>'
                f'    </div>'
                f'  </div>'
                f'  <div class="cat-bc">{bc_svg}</div>'
                f'</div>'
            )
        salto = ' style="page-break-after:always;"' if p_idx < len(paginas) - 1 else ""
        html_paginas.append(
            f'<div class="cat-page"{salto}>'
            f'  <div class="cat-hdr">'
            f'    <div style="display:flex;align-items:center;gap:12px;">{logo_hdr}'
            f'      <div style="border-left:1.5px solid #cbd5e1;padding-left:10px;">'
            f'        <div style="font-size:13px;font-weight:800;color:#0d5b94;text-transform:uppercase;">Hoja Escaneable de Códigos de Barras (Datamax S30W)</div>'
            f'        <div style="font-size:10.5px;color:#475569;">{html.escape(filtros_txt)}</div>'
            f'      </div>'
            f'    </div>'
            f'    <div style="text-align:right;font-size:10.5px;color:#475569;"><b>Página {p_idx + 1} de {len(paginas)}</b><br>{len(tarjetas)} códigos</div>'
            f'  </div>'
            f'  <div class="cat-grid">{"".join(cards_html)}</div>'
            f'</div>'
        )

    img_sz = "56px" if es_grande else "42px"
    return f"""<!doctype html><html><head><meta charset="utf-8">
<title>Catálogo Escaneable Carta · Lebaref ({len(tarjetas)})</title>
<style>
  @page {{ size: letter portrait; margin: 10mm 11mm; }}
  * {{ box-sizing: border-box; }}
  body {{ margin: 0; padding: 0; font-family: 'Segoe UI', Arial, sans-serif; color: #0f172a; background: #fff; }}
  .no-print {{ background: #031e34; color: #fff; padding: 8px 16px; display: flex; justify-content: space-between; align-items: center; font-size: 12px; }}
  .no-print button {{ background: #0d5b94; color: #fff; border: none; border-radius: 5px; padding: 6px 14px; font-weight: 700; cursor: pointer; }}
  @media print {{ .no-print {{ display: none !important; }} }}
  .cat-page {{ padding-top: 2px; }}
  .cat-hdr {{ display: flex; justify-content: space-between; align-items: center; border-bottom: 2px solid #031e34; padding-bottom: 6px; margin-bottom: 10px; }}
  .cat-grid {{ display: grid; grid-template-columns: {cols_css}; gap: 8px; }}
  .cat-card {{ border: 1px solid #94a3b8; border-radius: 7px; padding: 7px 8px; display: flex; flex-direction: column; justify-content: space-between; background: #fff; page-break-inside: avoid; }}
  .cat-top {{ display: flex; gap: 8px; align-items: flex-start; margin-bottom: 6px; }}
  .cat-img {{ width: {img_sz}; height: {img_sz}; object-fit: contain; border: 1px solid #e2e8f0; border-radius: 5px; background: #f8fafc; flex-shrink: 0; }}
  .cat-noimg {{ width: {img_sz}; height: {img_sz}; border: 1px solid #cbd5e1; border-radius: 5px; background: #f1f5f9; color: #475569; font-size: 9px; font-weight: 800; display: flex; align-items: center; justify-content: center; text-align: center; flex-shrink: 0; }}
  .cat-meta {{ min-width: 0; flex: 1; }}
  .cat-badges {{ display: flex; justify-content: space-between; gap: 4px; margin-bottom: 2px; }}
  .b-code {{ background: #031e34; color: #fff; font-size: 9px; font-weight: 800; padding: 1px 5px; border-radius: 3px; }}
  .b-loc {{ background: #eff6ff; color: #0d5b94; border: 1px solid #bfdbfe; font-size: 8.5px; font-weight: 700; padding: 1px 5px; border-radius: 3px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }}
  .cat-title {{ font-size: {"11.5px" if es_grande else "10px"}; font-weight: 700; color: #0f172a; line-height: 1.2; max-height: 2.4em; overflow: hidden; }}
  .cat-sub {{ font-size: 8.5px; color: #64748b; margin-top: 2px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }}
  .cat-bc {{ border-top: 1px dashed #e2e8f0; padding-top: 4px; text-align: center; background: #fff; }}
</style></head><body>
<div class="no-print">
  <div><b>Hoja Escaneable Tamaño Carta</b> — {len(tarjetas)} códigos en {len(paginas)} página(s)</div>
  <button onclick="window.print()">🖨️ Imprimir Hoja Carta</button>
</div>
{"".join(html_paginas)}
</body></html>"""


@frappe.whitelist(methods=["GET"])
def resolver_escaneo(codigo: str):
    """Resuelve cualquier código escaneado con la pistola Datamax S30W hacia su documento o artículo en ERPNext."""
    limpio = (codigo or "").strip()
    if not limpio:
        frappe.throw("Escanea o escribe un código válido.")

    candidatos_directos = (
        ("Purchase Order", limpio),
        ("Material Request", limpio),
        ("Purchase Receipt", limpio),
        ("Stock Entry", limpio),
        ("Request for Quotation", limpio),
        ("Supplier Quotation", limpio),
        ("Lebaref Prestamo", limpio),
        ("Lebaref Herramienta", limpio.upper()),
        ("Lebaref Kit", limpio.upper()),
        ("Lebaref Vehiculo", limpio.upper()),
        ("Item", limpio),
        ("Item", limpio.upper()),
    )
    for dt, nombre in candidatos_directos:
        if frappe.db.exists("DocType", dt) and frappe.db.exists(dt, nombre):
            if frappe.has_permission(dt, "read", nombre):
                extra = {}
                if dt == "Purchase Order":
                    po = frappe.db.get_value("Purchase Order", nombre, ["docstatus", "status", "supplier"], as_dict=True)
                    extra = po or {}
                elif dt == "Lebaref Prestamo":
                    extra = {"estado": frappe.db.get_value("Lebaref Prestamo", nombre, "estado")}
                return {"encontrado": True, "doctype": dt, "name": nombre, "extra": extra}

    # Buscar por código de barras de fábrica / EAN / UPC en tabItem Barcode
    item_por_cb = frappe.db.get_value("Item Barcode", {"barcode": limpio}, "parent")
    if not item_por_cb:
        item_por_cb = frappe.db.get_value("Item Barcode", {"barcode": limpio.upper()}, "parent")
    if item_por_cb and frappe.has_permission("Item", "read", item_por_cb):
        return {"encontrado": True, "doctype": "Item", "name": item_por_cb, "extra": {"barcode": limpio}}

    return {"encontrado": False, "codigo": limpio}


def configurar_formatos_impresion():
    for nombre, doctype, plantilla in PRINT_FORMATS:
        if not frappe.db.exists("DocType", doctype):
            continue
        valores = {
            "doc_type": doctype,
            "module": "Lebaref Compras",
            "standard": "No",
            "custom_format": 1,
            "print_format_type": "Jinja",
            "default_print_language": "es",
            "disabled": 0,
            "html": plantilla,
        }
        if frappe.db.exists("Print Format", nombre):
            pf = frappe.get_doc("Print Format", nombre)
            pf.update(valores)
            pf.save(ignore_permissions=True)
        else:
            frappe.get_doc({"doctype": "Print Format", "name": nombre, **valores}).insert(ignore_permissions=True)
