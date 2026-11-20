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
      <td><b>Título / Ref:</b> {{ doc.title or "—" }}</td>
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
        <td><b>{{ "LIBRE" if row.custom_lebaref_concepto else row.item_code }}</b></td>
        <td>{{ row.custom_lebaref_concepto or row.item_name or row.description }}</td>
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
        <td><b>{{ "LIBRE" if row.custom_lebaref_concepto else row.item_code }}</b></td>
        <td>{{ row.custom_lebaref_concepto or row.item_name or row.description }}</td>
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
        <td><b>{{ "LIBRE" if row.custom_lebaref_concepto else row.item_code }}</b></td>
        <td>{{ row.custom_lebaref_concepto or row.item_name }}</td>
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
<div class="leb-carta">
  <div class="leb-hdr">
    <div class="leb-hdr-brand">
      {{ svg_logo_lebaref(195) }}
      <div class="leb-hdr-info">
        <div class="leb-sub-area">Control de Bodega y Obra</div>
        <div class="leb-doc-title">{{ "Vale de Retorno de Sobrante a Almacén" if doc.custom_lebaref_origen else "Vale de Entrega / Salida de Material a Obra" }}</div>
        <div style="font-size:11px;color:#475569;">Fecha: {{ frappe.utils.formatdate(doc.posting_date) }} · Tipo: {{ _(doc.stock_entry_type) }}</div>
      </div>
    </div>
    <div>{{ svg_code128(doc.name) }}</div>
  </div>
  <table class="leb-meta">
    <tr>
      <td><b>Folio Movimiento:</b> {{ doc.name }}</td>
      <td><b>Obra / Proyecto:</b> {{ doc.project or "—" }}</td>
      <td><b>Entrega Origen:</b> {{ doc.custom_lebaref_origen or "N/A (Salida Original)" }}</td>
    </tr>
    <tr>
      <td colspan="3"><b>Recibe / Observaciones:</b> {{ doc.remarks or "—" }}</td>
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
        <td>{{ row.item_name }}{% if row.custom_lebaref_partida %}<br><small>Partida: {{ row.custom_lebaref_partida }}</small>{% endif %}</td>
        <td>{{ row.s_warehouse or "—" }}</td>
        <td>{{ row.t_warehouse or "—" }}</td>
        <td class="leb-num"><b>{{ "{:,.2f}".format(row.qty) }} {{ row.uom }}</b></td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
  <table class="leb-firmas">
    <tr>
      <td><div class="leb-linea-firma">Despachó Almacén</div></td>
      <td><div class="leb-linea-firma">Recibió Conformidad en Obra</div></td>
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

PRINT_FORMATS = (
    ("Lebaref Carta - Solicitud", "Material Request", FORMATO_SOLICITUD),
    ("Lebaref Carta - Orden de Compra", "Purchase Order", FORMATO_ORDEN_COMPRA),
    ("Lebaref Carta - Recepción Almacén", "Purchase Receipt", FORMATO_RECEPCION),
    ("Lebaref Carta - Salida o Sobrante", "Stock Entry", FORMATO_SALIDA_SOBRANTE),
    ("Lebaref Carta - Responsiva EPP y Equipo", "Stock Entry", FORMATO_RESPONSIVA_EPP),
    ("Lebaref Carta - Vale Préstamo Herramienta", "Lebaref Prestamo", FORMATO_PRESTAMO_HER),
    ("Lebaref Carta - Check-list Vehicular", "Lebaref Revision Vehiculo", FORMATO_CHECKLIST_VEH),
)


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
