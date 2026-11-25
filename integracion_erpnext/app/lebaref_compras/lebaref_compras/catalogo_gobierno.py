"""Fase 4 — Gobierno de Catálogo, Alias, Equivalencias e Importación Histórica (CAT-01, CAT-02, MIG-02):
- CAT-01: Limpieza y unificación de familias duplicadas en los artículos del catálogo, etiquetas multigerencia,
          plantillas técnicas sugeridas por categoría y bandera 'Exige recotizar siempre'.
- CAT-02: DocType 'Lebaref Equivalencia Articulo' abierto a los 4 usuarios (Oscar, Airan, Juan y Salvia),
          preservando texto original, con dictamen de gerencia y alcance ('Toda la empresa' vs 'Solo Cliente / Proyecto específico').
- MIG-02: DocType e importador asistido 'Lebaref Cotizacion Historica' para el archivo de 1 año de cotizaciones
          (Plantilla_Cotizaciones_Historicas_Lebaref.xlsx), idempotente por huella SHA-256, multimoneda MXN/USD con TC DOF,
          vinculación de alias y separación estricta respecto a compras reales.
"""
import csv
import hashlib
import io
import json
import re
import unicodedata

import frappe
from frappe.custom.doctype.custom_field.custom_field import create_custom_fields
from frappe.utils import cint, flt, getdate, today

GERENCIAS_OFICIALES = (
    "HVAC",
    "Eléctrico",
    "Obra Civil",
    "Mantenimiento Industrial",
    "Oficina",
)

DICTAMENES_EQUIVALENCIA = (
    "Pendiente",
    "Exacto (Alias directo)",
    "Equivalente aprobado",
    "Sustituto puntual",
    "Distinto (No agrupar)",
)

ALCANCES_EQUIVALENCIA = (
    "Toda la empresa",
    "Solo Cliente / Proyecto específico",
)

PLANTILLAS_TECNICAS_POR_CATEGORIA = {
    "Refrigeración y Aire Acondicionado (HVAC)": {
        "modelo": "Ej. AR5000 / ZP61K5E",
        "medidas": "Ej. 1/4\" x 3/8\" · Tramo 15 m",
        "capacidad": "Ej. 1.5 TR (18,000 BTU/h) · 220V",
        "espec1": "Refrigerante (R-410A / R-32 / R-22)",
        "espec2": "Tecnología (Inverter / Estándar / SEER)",
    },
    "Eléctrico, Iluminación y Control": {
        "modelo": "Ej. QO230 / LC1D18",
        "medidas": "Ej. Calibre 12 AWG · Caja 100 m",
        "capacidad": "Ej. 30 A · 220V · 2 Polos",
        "espec1": "Aislamiento / Tipo (THW-LS / Uso rudo)",
        "espec2": "Norma / Grado IP (NOM / IP65)",
    },
    "Plomería, Tubería y Conexiones": {
        "modelo": "Ej. Cédula 40 / Hidráulico RD-26",
        "medidas": "Ej. Diámetro 1 1/4\" · Tramo 6 m",
        "capacidad": "Ej. 10 kg/cm² · Agua fría/caliente",
        "espec1": "Material (PVC / CPVC / Cobre / Galv.)",
        "espec2": "Tipo de unión (Cementar / Roscable)",
    },
    "Herramientas, Medición y Maquinaria": {
        "modelo": "Ej. DCD778 / Fluke 323",
        "medidas": "Ej. Broquero 1/2\" · Disco 4 1/2\"",
        "capacidad": "Ej. 20V Brushless / 400 A AC",
        "espec1": "Alimentación (Inalámbrico / 120V / Manual)",
        "espec2": "Accesorios incluidos / Garantía",
    },
    "Tornillería, Fijación y Herrajes": {
        "modelo": "Ej. Hexagonal G5 / Unicanal 4x2",
        "medidas": "Ej. 3/8\" x 2 1/2\"",
        "capacidad": "Ej. Carga media / Pesada",
        "espec1": "Acabado (Galvanizado / Inoxidable / Negro)",
        "espec2": "Tipo de rosca / Taquete compatible",
    },
    "Soldadura, Químicos y Consumibles Técnicos": {
        "modelo": "Ej. Harris 15% Plata / Map-Pro",
        "medidas": "Ej. Envase 1 L / Rollo 450 g",
        "capacidad": "Ej. Alta temperatura / Desincrustante",
        "espec1": "Aplicación (Cobre-Cobre / Limpieza serpentín)",
        "espec2": "Hoja de seguridad / Dilución",
    },
    "Seguridad Industrial y EPP": {
        "modelo": "Ej. Clase E / Nitrilo Anticorte",
        "medidas": "Ej. Talla G (9) / Ajuste matraca",
        "capacidad": "Ej. Dieléctrico hasta 20,000 V",
        "espec1": "Certificación (NOM-115 / ANSI Z87.1)",
        "espec2": "Color / Área asignada",
    },
    "Pintura, Impermeabilizantes y Obra": {
        "modelo": "Ej. Vinimex / Acriton 5 años",
        "medidas": "Ej. Cubeta 19 L / Galón 4 L",
        "capacidad": "Ej. Rendimiento 8 m²/L",
        "espec1": "Acabado / Color (Blanco ostión / Mate)",
        "espec2": "Superficie (Interior / Exterior / Losas)",
    },
    "Limpieza, Higiene y Jardinería": {
        "modelo": "Ej. Uso institucional",
        "medidas": "Ej. Bidón 20 L / Paquete 12 rollos",
        "capacidad": "Ej. Biodegradable / Uso rudo",
        "espec1": "Presentación (Concentrado / Listo para usar)",
        "espec2": "Área destino",
    },
    "Papelería, Oficina y Comedor": {
        "modelo": "Ej. Carta 75 g / Tóner 105A",
        "medidas": "Ej. Caja 5,000 hojas",
        "capacidad": "Ej. Rendimiento 1,000 págs.",
        "espec1": "Compatibilidad / Presentación",
        "espec2": "Consumo directo en oficina",
    },
}

MAPA_FAMILIAS_CANONICAS = {
    "TUBERIA Y CONEXIONES PVC": "Tubería y Conexiones PVC",
    "SELLADORES, SILICONES Y PEGAMENTOS": "Selladores, Silicones y Pegamentos",
    "TORNILLOS Y TUERCAS": "Tornillos y Tuercas",
    "GAS REFRIGERANTE R-410A": "Gas Refrigerante R-410A",
    "GAS REFRIGERANTE R-32": "Gas Refrigerante R-32",
    "GAS REFRIGERANTE R-22": "Gas Refrigerante R-22",
    "GUANTES DE SEGURIDAD": "Guantes de Seguridad",
    "CASCOS Y PROTECCION CABEZA": "Cascos y Protección Cabeza",
    "SENSORES Y CONTROLES HVAC": "Sensores y Controles HVAC",
    "JABON, DETERGENTES Y LIMPIADORES": "Jabón, Detergentes y Limpiadores",
    "BROCHAS, RODILLOS Y FELPAS": "Brochas, Rodillos y Felpas",
    "IMPERMEABILIZANTES Y PINTURA": "Impermeabilizantes y Pintura",
    "BROCAS Y PUNTAS": "Brocas y Puntas",
    "CINTA DE AISLAR": "Cinta de Aislar",
    "TOALLAS, PAPEL Y MICROFIBRAS": "Toallas, Papel y Microfibras",
    "PILAS Y BATERIAS": "Pilas y Baterías",
    "PIJAS Y TAQUETES": "Pijas y Taquetes",
    "ESCOBAS, CEPILLOS Y TRAPEADORES": "Escobas, Cepillos y Trapeadores",
    "CINTA DE MONTAJE / DUCTO / TEFLON": "Cinta de Montaje, Ducto y Teflón",
    "CHALECOS Y SENALIZACION": "Chalecos y Señalización",
    "FOCOS Y LUMINARIAS LED": "Focos y Luminarias LED",
    "CONTACTORES Y RELEVADORES": "Contactores y Relevadores",
    "ACEITES Y QUIMICOS HVAC": "Aceites y Químicos HVAC",
    "MANGUERAS Y CONEXIONES HIDRAULICAS": "Mangueras y Conexiones Hidráulicas",
    "VALVULAS Y ACCESORIOS HVAC": "Válvulas y Accesorios HVAC",
    "INSECTICIDAS Y PLAGAS": "Insecticidas y Control de Plagas",
    "DISCOS DE CORTE Y LIJA": "Discos de Corte y Abrasivos",
    "BOLSAS PARA BASURA": "Bolsas y Recolección",
    "HERRAMIENTA MANUAL": "Herramienta Manual",
    "GAS PROPILENO / SOLDADURA": "Soldadura y Gases de Aporte",
    "SOLDADURA DE PLATA Y FUNDENTE / VARILLA / VARITA": "Soldadura y Gases de Aporte",
    "CAPACITORES HVAC": "Capacitores de Arranque y Trabajo",
}


def _sin_acentos(texto: str) -> str:
    limpio = unicodedata.normalize("NFKD", str(texto or ""))
    sin_comb = "".join(c for c in limpio if not unicodedata.combining(c))
    return re.sub(r"\s+", " ", sin_comb).strip().upper()


def normalizar_nombre_familia(raw_familia: str, item_name: str = "", item_group: str = "") -> str:
    clave = _sin_acentos(raw_familia)
    if clave in MAPA_FAMILIAS_CANONICAS:
        return MAPA_FAMILIAS_CANONICAS[clave]
    if raw_familia and raw_familia.strip():
        limpia = re.sub(r"\s+", " ", raw_familia.strip())
        if limpia.isupper() and len(limpia) > 4:
            palabras = []
            siglas = {"PVC", "CPVC", "HVAC", "LED", "EPP", "AWG", "AC", "DC", "TR", "R-410A", "R-32", "R-22"}
            min_conectores = {"de", "del", "la", "las", "los", "y", "o", "en", "para", "con", "por"}
            for idx, w in enumerate(limpia.split(" ")):
                if w in siglas:
                    palabras.append(w)
                elif idx > 0 and w.lower() in min_conectores:
                    palabras.append(w.lower())
                else:
                    palabras.append(w.capitalize())
            return " ".join(palabras)
        return limpia

    # Inferir familia técnica limpia cuando el artículo no tenía familia capturada
    txt = _sin_acentos(f"{item_name} {item_group}")
    reglas = (
        (("REFRIGERANTE", "FREON", "R-410", "R410", "R-32", "R-22"), "Gases Refrigerantes"),
        (("MINISPLIT", "CONDENSADOR", "EVAPORADOR", "FAN & COIL", "MANEJADORA"), "Equipos de Aire Acondicionado"),
        (("TUBO DE COBRE", "TUBERIA DE COBRE", "COBRE ACR", "ARMAFLEX", "AISLAMIENTO TERMICO"), "Tubería de Cobre y Aislamiento HVAC"),
        (("CAPACITOR",), "Capacitores de Arranque y Trabajo"),
        (("CONTACTOR", "RELEVADOR", "GUARDAMOTOR", "PASTILLA", "INTERRUPTOR TERMOMAGNETICO", "BREAKER"), "Protección y Control Eléctrico"),
        (("CABLE", "ALAMBRE", "CORDON USO RUDO", "THW"), "Conductores y Cables Eléctricos"),
        (("CONDUIT", "CANALETA", "CHALUPA", "CAJA REGISTRO", "CONECTOR GLANDULA"), "Canalización y Tubería Eléctrica"),
        (("LAMPARA", "FOCO", "REFLECTOR", "LUMINARIA"), "Focos y Luminarias LED"),
        (("APAGADOR", "CONTACTO DUPLEX", "CLAVIJA"), "Accesorios y Conectividad Eléctrica"),
        (("PVC", "CPVC", "COFLEX", "VALVULA ESFERA", "NIPLE", "CODO ", "TEE "), "Tubería y Conexiones Hidrosanitarias"),
        (("PIJA", "TAQUETE", "TORNILLO", "TUERCA", "RONDANA", "ESPIRRAGO", "VARILLA ROSCADA", "UNICANAL", "ABRAZADera".upper()), "Tornillería, Fijación y Soportería"),
        (("DISCO DE CORTE", "DISCO DESBASTE", "LIJA", "CARDAS", "BROCA"), "Brocas, Discos y Abrasivos"),
        (("GUANTE", "CASCO", "LENTE DE SEGURIDAD", "CHALECO", "ARNES", "BOTA", "TAPON AUDITIVO"), "Equipo de Protección Personal (EPP)"),
        (("PINTURA", "ESMALTE", "IMPERMEABILIZANTE", "THINNER", "BROCHA", "RODILLO", "CEMENTO", "TABLAROCA", "REDIMIX"), "Pintura, Recubrimientos y Obra Civil"),
        (("SOLDADURA", "FUNDENTE", "GAS MAP", "PROPILENO", "SILICON", "SELLADOR", "PEGAMENTO", "CINTA TEFLON"), "Soldadura, Selladores y Químicos"),
        (("JABON", "CLORO", "ESCOBA", "TRAPEADOR", "MICROFIBRA", "PAPEL HIGIENICO", "BOLSA NEGRA"), "Limpieza e Insumos Generales"),
        (("HOJA BLANCA", "PAPEL BOND", "TONER", "CARPETA", "PLUMA", "MARCADOR", "CAFE", "AGUA"), "Papelería y Consumibles de Oficina"),
        (("TALADRO", "ROTOMARTILLO", "PINZA", "DESARMADOR", "LLAVE ", "FLEXOMETRO", "MULTIMETRO", "MANOMETRO"), "Herramienta y Equipo de Medición"),
    )
    for palabras_clave, fam_sugerida in reglas:
        if any(k in txt for k in palabras_clave):
            return fam_sugerida
    return (item_group or "General").strip()


def inferir_gerencias_articulo(item_name: str = "", item_group: str = "", consumo_directo: int = 0) -> str:
    if consumo_directo or "OFICINA" in _sin_acentos(item_group) or "PAPELER" in _sin_acentos(item_group):
        return "Oficina"

    grp = _sin_acentos(item_group)
    nom = _sin_acentos(item_name)
    gerencias = []

    if "HVAC" in grp or "REFRIG" in grp or any(k in nom for k in ("HVAC", "REFRIGERANTE", "MINISPLIT", "COBRE", "ARMAFLEX", "CONDENSADOR", "SERPENTIN")):
        gerencias.append("HVAC")
    if "ELECTRIC" in grp or any(k in nom for k in ("CABLE", "PASTILLA", "CONTACTOR", "CONDUIT", "LED", "MULTIMETRO", "AISLAR", "VOLT")):
        gerencias.append("Eléctrico")
    if "PINTURA" in grp or "OBRA" in grp or "PLOMER" in grp or any(k in nom for k in ("PVC", "CEMENTO", "TABLAROCA", "IMPERMEABIL", "BROCHA", "RODILLO", "HIDRAULIC")):
        gerencias.append("Obra Civil")

    # Artículos transversales compartidos por más de la mitad de las gerencias operativas
    es_transversal = any(
        k in grp for k in ("TORNILLER", "HERRAMIENTA", "SEGURIDAD", "EPP", "SOLDADURA", "LIMPIEZA")
    ) or any(
        k in nom for k in ("PIJA", "TAQUETE", "TORNILLO", "DISCO", "CINTA", "GUANTE", "CASCO", "CHALECO", "SILICON", "BROCA")
    )
    if es_transversal:
        for g_tr in ("HVAC", "Eléctrico", "Obra Civil", "Mantenimiento Industrial"):
            if g_tr not in gerencias:
                gerencias.append(g_tr)

    if not gerencias:
        gerencias.append("Mantenimiento Industrial")

    orden_oficial = [g for g in GERENCIAS_OFICIALES if g in gerencias]
    return ", ".join(orden_oficial)


def obtener_plantilla_tecnica_sugerida(categoria: str) -> dict:
    cat = (categoria or "").strip()
    if cat in PLANTILLAS_TECNICAS_POR_CATEGORIA:
        return PLANTILLAS_TECNICAS_POR_CATEGORIA[cat]
    return {
        "modelo": "Modelo o serie del fabricante",
        "medidas": "Dimensiones, calibre o presentación",
        "capacidad": "Capacidad eléctrica, térmica o carga",
        "espec1": "Material o norma principal",
        "espec2": "Aplicación o detalle adicional",
    }


def parsear_lista_gerencias(valor_str: str) -> list[str]:
    partes = [p.strip() for p in str(valor_str or "").split(",") if p.strip()]
    vistas = []
    for p in partes:
        if p not in vistas:
            vistas.append(p)
    return vistas


def _asegurar_doctype_equivalencias():
    permisos = [
        {
            "role": rol,
            "read": 1,
            "create": 1,
            "write": 1,
            "delete": 1 if rol in ("Purchase Manager", "System Manager") else 0,
            "select": 1,
            "report": 1,
            "export": 1,
        }
        for rol in ("Purchase User", "Purchase Manager", "Stock User", "Stock Manager", "Item Manager", "System Manager")
        if frappe.db.exists("Role", rol)
    ]
    campos_dt = [
        {"fieldname": "texto_original", "label": "Texto o clave original recibida", "fieldtype": "Data", "reqd": 1, "in_list_view": 1, "in_standard_filter": 1},
        {"fieldname": "clave_externa", "label": "Clave / Modelo del proveedor", "fieldtype": "Data"},
        {"fieldname": "origen", "label": "Origen del texto", "fieldtype": "Select", "options": "Solicitud de Ingeniero\nCotización de Proveedor\nFactura XML\nImportación Histórica\nRevisión Manual", "default": "Solicitud de Ingeniero", "in_standard_filter": 1},
        {"fieldname": "proveedor", "label": "Proveedor origen", "fieldtype": "Link", "options": "Supplier", "in_standard_filter": 1},
        {"fieldname": "gerencia", "label": "Gerencia relacionada", "fieldtype": "Select", "options": "\nHVAC\nEléctrico\nObra Civil\nMantenimiento Industrial\nOficina\nGeneral", "in_standard_filter": 1},
        {"fieldname": "cb_eq_1", "fieldtype": "Column Break"},
        {"fieldname": "articulo_propuesto", "label": "Artículo del catálogo (SKU)", "fieldtype": "Link", "options": "Item", "in_list_view": 1, "in_standard_filter": 1},
        {"fieldname": "nombre_articulo", "label": "Nombre en catálogo", "fieldtype": "Data", "read_only": 1},
        {"fieldname": "dictamen", "label": "Dictamen de gerencia", "fieldtype": "Select", "options": "\n".join(DICTAMENES_EQUIVALENCIA), "default": "Pendiente", "reqd": 1, "in_list_view": 1, "in_standard_filter": 1},
        {"fieldname": "alcance", "label": "Alcance aprobado", "fieldtype": "Select", "options": "\n".join(ALCANCES_EQUIVALENCIA), "default": "Toda la empresa", "in_list_view": 1},
        {"fieldname": "proyecto", "label": "Proyecto específico", "fieldtype": "Link", "options": "Project"},
        {"fieldname": "cliente", "label": "Cliente específico", "fieldtype": "Link", "options": "Customer"},
        {"fieldname": "sec_eq_bitacora", "label": "Trazabilidad de revisión", "fieldtype": "Section Break"},
        {"fieldname": "propuesto_por", "label": "Propuesto por", "fieldtype": "Data", "read_only": 1},
        {"fieldname": "dictaminado_por", "label": "Dictaminado por", "fieldtype": "Data", "read_only": 1},
        {"fieldname": "fecha_dictamen", "label": "Fecha de dictamen", "fieldtype": "Date", "read_only": 1},
        {"fieldname": "notas_gerencia", "label": "Acuerdo / Notas de junta", "fieldtype": "Small Text"},
    ]

    if frappe.db.exists("DocType", "Lebaref Equivalencia Articulo"):
        dt = frappe.get_doc("DocType", "Lebaref Equivalencia Articulo")
        dt.set("permissions", permisos)
        existentes = {f.fieldname for f in dt.fields}
        for cf in campos_dt:
            if cf["fieldname"] not in existentes:
                dt.append("fields", cf)
        dt.save(ignore_permissions=True)
        return

    frappe.get_doc({
        "doctype": "DocType",
        "name": "Lebaref Equivalencia Articulo",
        "module": "Lebaref Compras",
        "custom": 1,
        "autoname": "format:EQV-{#####}",
        "track_changes": 1,
        "fields": campos_dt,
        "permissions": permisos,
    }).insert(ignore_permissions=True)


def _asegurar_doctype_cotizacion_historica():
    permisos = [
        {
            "role": rol,
            "read": 1,
            "create": 1,
            "write": 1,
            "delete": 1 if rol in ("Purchase Manager", "System Manager") else 0,
            "select": 1,
            "report": 1,
            "export": 1,
        }
        for rol in ("Purchase User", "Purchase Manager", "Stock User", "Stock Manager", "Item Manager", "Accounts User", "System Manager")
        if frappe.db.exists("Role", rol)
    ]
    campos_dt = [
        {"fieldname": "fecha_cotizacion", "label": "Fecha de cotización", "fieldtype": "Date", "reqd": 1, "in_list_view": 1, "in_standard_filter": 1},
        {"fieldname": "proveedor_texto", "label": "Proveedor", "fieldtype": "Data", "reqd": 1, "in_list_view": 1, "in_standard_filter": 1},
        {"fieldname": "proveedor", "label": "Proveedor vinculado", "fieldtype": "Link", "options": "Supplier", "in_standard_filter": 1},
        {"fieldname": "descripcion_original", "label": "Descripción original cotizada", "fieldtype": "Small Text", "reqd": 1, "in_list_view": 1},
        {"fieldname": "item_code", "label": "Artículo catálogo (SKU)", "fieldtype": "Link", "options": "Item", "in_list_view": 1, "in_standard_filter": 1},
        {"fieldname": "cb_ch_1", "fieldtype": "Column Break"},
        {"fieldname": "cantidad", "label": "Cantidad cotizada", "fieldtype": "Float", "default": 1},
        {"fieldname": "unidad", "label": "Unidad", "fieldtype": "Data", "default": "Pieza"},
        {"fieldname": "moneda", "label": "Moneda", "fieldtype": "Select", "options": "MXN\nUSD", "default": "MXN", "reqd": 1, "in_standard_filter": 1},
        {"fieldname": "precio_unitario_sin_iva", "label": "Precio unitario s/IVA (moneda origen)", "fieldtype": "Float", "precision": "4", "reqd": 1},
        {"fieldname": "tc_dof", "label": "Tipo de cambio DOF", "fieldtype": "Float", "precision": "4", "default": 1},
        {"fieldname": "precio_unitario_mxn", "label": "Precio limpio s/IVA (MXN)", "fieldtype": "Float", "precision": "4", "read_only": 1, "in_list_view": 1},
        {"fieldname": "flete_sin_iva", "label": "Flete separado s/IVA", "fieldtype": "Float", "precision": "2", "default": 0},
        {"fieldname": "precio_sitio_mxn", "label": "Costo puesto en sitio s/IVA (MXN)", "fieldtype": "Float", "precision": "4", "read_only": 1},
        {"fieldname": "sec_ch_extra", "label": "Referencias comerciales y origen histórico", "fieldtype": "Section Break"},
        {"fieldname": "clave_modelo_proveedor", "label": "Clave o modelo proveedor", "fieldtype": "Data"},
        {"fieldname": "marca", "label": "Marca", "fieldtype": "Data"},
        {"fieldname": "folio_cotizacion_proveedor", "label": "Folio cotización proveedor", "fieldtype": "Data"},
        {"fieldname": "modalidad_entrega", "label": "Modalidad de entrega", "fieldtype": "Data"},
        {"fieldname": "dias_entrega_promesa", "label": "Días promesa entrega", "fieldtype": "Int", "default": 0},
        {"fieldname": "vigencia_dias", "label": "Vigencia (días)", "fieldtype": "Int", "default": 0},
        {"fieldname": "cb_ch_2", "fieldtype": "Column Break"},
        {"fieldname": "condicion_pago", "label": "Condición de pago", "fieldtype": "Data"},
        {"fieldname": "gerencia", "label": "Gerencia o área", "fieldtype": "Data", "in_standard_filter": 1},
        {"fieldname": "obra_referencia", "label": "Cliente u obra referencia", "fieldtype": "Data"},
        {"fieldname": "se_compro_realmente", "label": "¿Se compró realmente? (informativo)", "fieldtype": "Data"},
        {"fieldname": "origen_vinculo", "label": "Método de vínculo al catálogo", "fieldtype": "Data", "read_only": 1},
        {"fieldname": "notas", "label": "Notas", "fieldtype": "Small Text"},
        {"fieldname": "huella_fila", "label": "Huella de fila", "fieldtype": "Data", "unique": 1, "read_only": 1, "hidden": 1, "print_hide": 1, "report_hide": 1},
    ]

    if frappe.db.exists("DocType", "Lebaref Cotizacion Historica"):
        dt = frappe.get_doc("DocType", "Lebaref Cotizacion Historica")
        dt.set("permissions", permisos)
        existentes = {f.fieldname for f in dt.fields}
        for cf in campos_dt:
            if cf["fieldname"] not in existentes:
                dt.append("fields", cf)
        dt.save(ignore_permissions=True)
        return

    frappe.get_doc({
        "doctype": "DocType",
        "name": "Lebaref Cotizacion Historica",
        "module": "Lebaref Compras",
        "custom": 1,
        "autoname": "format:CHIST-{#####}",
        "track_changes": 0,
        "fields": campos_dt,
        "permissions": permisos,
    }).insert(ignore_permissions=True)


def configurar_campos_fase4():
    """Instala los campos de gobierno de catálogo (CAT-01), DocTypes de equivalencias (CAT-02) y cotizaciones históricas (MIG-02)."""
    campos_item = {
        "Item": [
            {
                "fieldname": "custom_lebaref_familia",
                "label": "Familia técnica",
                "fieldtype": "Data",
                "insert_after": "item_group",
                "in_standard_filter": 1,
            },
            {
                "fieldname": "custom_lebaref_gerencias",
                "label": "Gerencias usuarias",
                "fieldtype": "Data",
                "insert_after": "custom_lebaref_familia",
                "in_standard_filter": 1,
                "description": "Áreas que utilizan este artículo (HVAC, Eléctrico, Obra Civil, Mantenimiento Industrial, Oficina).",
            },
            {
                "fieldname": "custom_lebaref_recotizar_siempre",
                "label": "Exige recotizar siempre (fabricación a medida / precio volátil)",
                "fieldtype": "Check",
                "insert_after": "custom_lebaref_consumo_directo",
                "default": "0",
                "description": "Marca artículos o servicios cuyo precio cambia en cada evento y nunca debe estimarse sin cotizar.",
            },
        ]
    }
    create_custom_fields(campos_item, update=True)
    _asegurar_doctype_equivalencias()
    _asegurar_doctype_cotizacion_historica()
    unificar_familias_y_gerencias_catalogo()


def unificar_familias_y_gerencias_catalogo() -> dict:
    """Normaliza familias técnicas duplicadas y asigna etiquetas multigerencia iniciales en el catálogo existente."""
    if not frappe.db.has_column("Item", "custom_lebaref_familia"):
        return {"actualizados": 0}

    tiene_consumo_dir = frappe.db.has_column("Item", "custom_lebaref_consumo_directo")
    campos = ["name", "item_name", "item_group", "description", "custom_lebaref_familia", "custom_lebaref_gerencias"]
    if tiene_consumo_dir:
        campos.append("custom_lebaref_consumo_directo")

    items = frappe.get_all("Item", filters={"name": ["!=", "LEBAREF-CONCEPTO-LIBRE"]}, fields=campos, limit_page_length=0)
    actualizados = 0
    for it in items:
        desc = it.get("description") or ""
        fam_raw = (it.get("custom_lebaref_familia") or "").strip()
        if not fam_raw and "[Familia:" in desc:
            fam_raw = desc.split("[Familia:", 1)[1].split("]", 1)[0].strip()

        fam_limpia = normalizar_nombre_familia(fam_raw, it.get("item_name") or "", it.get("item_group") or "")
        ger_actual = (it.get("custom_lebaref_gerencias") or "").strip()
        ger_limpia = ger_actual or inferir_gerencias_articulo(
            it.get("item_name") or "",
            it.get("item_group") or "",
            cint(it.get("custom_lebaref_consumo_directo") or 0),
        )

        cambios = {}
        if fam_limpia and fam_limpia != (it.get("custom_lebaref_familia") or ""):
            cambios["custom_lebaref_familia"] = fam_limpia[:140]
        if ger_limpia and not ger_actual:
            cambios["custom_lebaref_gerencias"] = ger_limpia[:140]

        if cambios:
            frappe.db.set_value("Item", it.name, cambios, update_modified=False)
            actualizados += 1

    return {"actualizados": actualizados, "total_catalogo": len(items)}


def buscar_alias_aprobado(token: str, proyecto: str = "", cliente: str = "", proveedor: str = ""):
    """Busca en Lebaref Equivalencia Articulo e Item Supplier si existe un alias o equivalencia aprobada aplicable."""
    limpio = (token or "").strip()
    if not limpio:
        return None, ""

    # 1. Item Supplier (clave o nombre comercial registrado por proveedor)
    if frappe.db.exists("DocType", "Item Supplier"):
        filtros_is = {"supplier_part_no": limpio}
        if proveedor and frappe.db.exists("Supplier", proveedor):
            parent_prov = frappe.db.get_value("Item Supplier", {"supplier_part_no": limpio, "supplier": proveedor}, "parent")
            if parent_prov and frappe.db.exists("Item", parent_prov):
                it = frappe.db.get_value("Item", parent_prov, ["name", "item_name", "purchase_uom", "stock_uom"], as_dict=True)
                return it, "Clave proveedor"
        parent_any = frappe.db.get_value("Item Supplier", filtros_is, "parent")
        if parent_any and frappe.db.exists("Item", parent_any):
            it = frappe.db.get_value("Item", parent_any, ["name", "item_name", "purchase_uom", "stock_uom"], as_dict=True)
            return it, "Clave proveedor"

    # 2. Lebaref Equivalencia Articulo (con respeto estricto de dictamen y alcance)
    if not frappe.db.exists("DocType", "Lebaref Equivalencia Articulo"):
        return None, ""

    clave_norm = _sin_acentos(limpio)
    if not cliente and proyecto and frappe.db.exists("Project", proyecto):
        cliente = frappe.db.get_value("Project", proyecto, "customer") or ""

    candidatos = frappe.get_all(
        "Lebaref Equivalencia Articulo",
        filters={
            "dictamen": ["in", ["Exacto (Alias directo)", "Equivalente aprobado"]],
            "articulo_propuesto": ["is", "set"],
        },
        fields=["name", "texto_original", "clave_externa", "articulo_propuesto", "dictamen", "alcance", "proyecto", "cliente", "proveedor"],
        order_by="modified desc",
        limit_page_length=2000,
    )

    coincidencias_especificas = []
    coincidencias_globales = []
    for c in candidatos:
        match_txt = _sin_acentos(c.texto_original) == clave_norm
        match_ext = bool(c.clave_externa) and _sin_acentos(c.clave_externa) == clave_norm
        if not (match_txt or match_ext):
            continue

        if c.alcance == "Solo Cliente / Proyecto específico":
            ok_proy = bool(proyecto and c.proyecto and c.proyecto == proyecto)
            ok_cli = bool(cliente and c.cliente and c.cliente == cliente and not c.proyecto)
            if ok_proy or ok_cli:
                coincidencias_especificas.append(c)
        else:
            coincidencias_globales.append(c)

    elegido = (coincidencias_especificas or coincidencias_globales or [None])[0]
    if elegido and frappe.db.exists("Item", elegido.articulo_propuesto):
        it = frappe.db.get_value(
            "Item",
            elegido.articulo_propuesto,
            ["name", "item_name", "purchase_uom", "stock_uom"],
            as_dict=True,
        )
        etiqueta = (
            "Alias directo"
            if elegido.dictamen == "Exacto (Alias directo)"
            else ("Equivalencia proyecto" if elegido.alcance != "Toda la empresa" else "Equivalencia aprobada")
        )
        return it, etiqueta

    return None, ""


def obtener_alias_de_articulo(item_code: str) -> list[dict]:
    """Devuelve los alias y equivalencias vinculados a un SKU para mostrarlos en la ficha del catálogo."""
    salida = []
    if not item_code or not frappe.db.exists("DocType", "Lebaref Equivalencia Articulo"):
        return salida
    rows = frappe.get_all(
        "Lebaref Equivalencia Articulo",
        filters={"articulo_propuesto": item_code, "dictamen": ["!=", "Distinto (No agrupar)"]},
        fields=["name", "texto_original", "clave_externa", "proveedor", "dictamen", "alcance", "proyecto"],
        order_by="modified desc",
        limit_page_length=25,
    )
    for r in rows:
        salida.append({
            "id": r.name,
            "texto": r.texto_original,
            "clave_externa": r.clave_externa or "",
            "proveedor": r.proveedor or "",
            "dictamen": r.dictamen,
            "alcance": r.proyecto if r.alcance != "Toda la empresa" and r.proyecto else "Empresa",
        })
    return salida


def _sincronizar_item_supplier_si_aplica(item_code: str, proveedor: str, clave_o_texto: str):
    if not item_code or not proveedor or not clave_o_texto:
        return
    if not (frappe.db.exists("Item", item_code) and frappe.db.exists("Supplier", proveedor)):
        return
    part_no = clave_o_texto.strip()[:140]
    ya = frappe.db.exists("Item Supplier", {"parent": item_code, "supplier": proveedor, "supplier_part_no": part_no})
    if ya:
        return
    doc_it = frappe.get_doc("Item", item_code)
    doc_it.append("supplier_items", {"supplier": proveedor, "supplier_part_no": part_no})
    doc_it.save(ignore_permissions=True)


@frappe.whitelist(methods=["POST"])
def proponer_alias_o_equivalencia(datos: str):
    """Permite a cualquiera de los 4 usuarios enviar un candidato a revisión de gerencias o registrar un alias."""
    frappe.has_permission("Lebaref Equivalencia Articulo", "create", throw=True)
    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(d, dict):
        frappe.throw("Datos inválidos para equivalencia.")

    texto = (d.get("texto_original") or "").strip()[:140]
    if not texto:
        frappe.throw("Escribe el nombre o descripción original recibida.")

    sku = (d.get("articulo_propuesto") or "").strip()
    if sku and not frappe.db.exists("Item", sku):
        frappe.throw(f"El artículo '{sku}' no existe en el catálogo.")

    dictamen = (d.get("dictamen") or "Pendiente").strip()
    if dictamen not in DICTAMENES_EQUIVALENCIA:
        dictamen = "Pendiente"

    alcance = (d.get("alcance") or "Toda la empresa").strip()
    if alcance not in ALCANCES_EQUIVALENCIA:
        alcance = "Toda la empresa"

    proyecto = (d.get("proyecto") or "").strip()
    cliente = (d.get("cliente") or "").strip()
    if alcance == "Solo Cliente / Proyecto específico" and not (proyecto or cliente):
        frappe.throw("Selecciona el Proyecto o Cliente específico cuando el alcance no es para toda la empresa.")

    proveedor = (d.get("proveedor") or "").strip()
    if proveedor and not frappe.db.exists("Supplier", proveedor):
        proveedor = ""

    clave_ext = (d.get("clave_externa") or "").strip()[:140]
    gerencia = (d.get("gerencia") or "").strip()
    origen = (d.get("origen") or "Revisión Manual").strip()
    notas = (d.get("notas_gerencia") or "").strip()
    usuario = frappe.utils.get_fullname(frappe.session.user) or frappe.session.user

    # Evitar duplicar exactamente la misma propuesta
    existentes = frappe.get_all(
        "Lebaref Equivalencia Articulo",
        filters={"articulo_propuesto": sku or "", "proyecto": proyecto or ""},
        fields=["name", "texto_original"],
        limit_page_length=500,
    )
    existente_id = next((e.name for e in existentes if _sin_acentos(e.texto_original) == _sin_acentos(texto)), None)

    nombre_it = frappe.db.get_value("Item", sku, "item_name") if sku else ""

    if existente_id:
        doc = frappe.get_doc("Lebaref Equivalencia Articulo", existente_id)
        doc.clave_externa = clave_ext or doc.clave_externa
        doc.proveedor = proveedor or doc.proveedor
        doc.gerencia = gerencia or doc.gerencia
        doc.alcance = alcance
        doc.cliente = cliente or doc.cliente
        if notas:
            doc.notas_gerencia = notas
        if dictamen != "Pendiente":
            doc.dictamen = dictamen
            doc.dictaminado_por = usuario
            doc.fecha_dictamen = today()
        doc.save(ignore_permissions=True)
    else:
        doc = frappe.get_doc({
            "doctype": "Lebaref Equivalencia Articulo",
            "texto_original": texto,
            "clave_externa": clave_ext,
            "origen": origen if origen in ("Solicitud de Ingeniero", "Cotización de Proveedor", "Factura XML", "Importación Histórica", "Revisión Manual") else "Revisión Manual",
            "proveedor": proveedor or None,
            "gerencia": gerencia if gerencia in ("HVAC", "Eléctrico", "Obra Civil", "Mantenimiento Industrial", "Oficina", "General") else "",
            "articulo_propuesto": sku or None,
            "nombre_articulo": nombre_it or "",
            "dictamen": dictamen,
            "alcance": alcance,
            "proyecto": proyecto or None,
            "cliente": cliente or None,
            "propuesto_por": usuario,
            "dictaminado_por": usuario if dictamen != "Pendiente" else "",
            "fecha_dictamen": today() if dictamen != "Pendiente" else None,
            "notas_gerencia": notas,
        }).insert(ignore_permissions=True)

    if sku and proveedor and dictamen in ("Exacto (Alias directo)", "Equivalente aprobado"):
        _sincronizar_item_supplier_si_aplica(sku, proveedor, clave_ext or texto)

    return {"ok": True, "folio": doc.name, "dictamen": doc.dictamen}


@frappe.whitelist(methods=["POST"])
def dictaminar_equivalencia(datos: str):
    """Registra el dictamen acordado con las gerencias sobre un candidato de la bandeja."""
    frappe.has_permission("Lebaref Equivalencia Articulo", "write", throw=True)
    d = frappe.parse_json(datos) if isinstance(datos, str) else datos
    folio = (d.get("name") or d.get("folio") or "").strip()
    if not folio or not frappe.db.exists("Lebaref Equivalencia Articulo", folio):
        frappe.throw("Selecciona un registro válido de la bandeja de equivalencias.")

    doc = frappe.get_doc("Lebaref Equivalencia Articulo", folio)
    nuevo_sku = (d.get("articulo_propuesto") or doc.articulo_propuesto or "").strip()
    dictamen = (d.get("dictamen") or doc.dictamen or "Pendiente").strip()
    if dictamen not in DICTAMENES_EQUIVALENCIA:
        frappe.throw("Selecciona un dictamen válido.")
    if dictamen in ("Exacto (Alias directo)", "Equivalente aprobado", "Sustituto puntual") and not nuevo_sku:
        frappe.throw("Indica el SKU del catálogo antes de aprobar el alias o equivalencia.")
    if nuevo_sku and not frappe.db.exists("Item", nuevo_sku):
        frappe.throw(f"El SKU '{nuevo_sku}' no existe en el catálogo.")

    alcance = (d.get("alcance") or doc.alcance or "Toda la empresa").strip()
    proyecto = (d.get("proyecto") if "proyecto" in d else doc.proyecto) or ""
    cliente = (d.get("cliente") if "cliente" in d else doc.cliente) or ""
    if alcance == "Solo Cliente / Proyecto específico" and not (proyecto or cliente):
        frappe.throw("Selecciona el Proyecto o Cliente específico para este alcance.")

    doc.articulo_propuesto = nuevo_sku or None
    doc.nombre_articulo = frappe.db.get_value("Item", nuevo_sku, "item_name") if nuevo_sku else ""
    doc.dictamen = dictamen
    doc.alcance = alcance
    doc.proyecto = proyecto or None
    doc.cliente = cliente or None
    if "notas_gerencia" in d:
        doc.notas_gerencia = (d.get("notas_gerencia") or "").strip()
    if dictamen != "Pendiente":
        doc.dictaminado_por = frappe.utils.get_fullname(frappe.session.user) or frappe.session.user
        doc.fecha_dictamen = today()

    doc.save(ignore_permissions=True)

    if nuevo_sku and doc.proveedor and dictamen in ("Exacto (Alias directo)", "Equivalente aprobado"):
        _sincronizar_item_supplier_si_aplica(nuevo_sku, doc.proveedor, doc.clave_externa or doc.texto_original)

    return {"ok": True, "folio": doc.name, "dictamen": doc.dictamen, "alcance": doc.alcance}


@frappe.whitelist(methods=["GET"])
def listar_bandeja_equivalencias(estado: str = "", gerencia: str = "", q: str = ""):
    """Consulta la bandeja de alias y equivalencias con conteos para junta con gerencias."""
    if not frappe.db.exists("DocType", "Lebaref Equivalencia Articulo"):
        return {"resumen": {"pendientes": 0, "aprobadas": 0, "puntuales": 0, "rechazadas": 0}, "items": [], "skus": [], "obras": []}

    frappe.has_permission("Lebaref Equivalencia Articulo", "read", throw=True)
    rows = frappe.get_all(
        "Lebaref Equivalencia Articulo",
        fields=[
            "name", "texto_original", "clave_externa", "origen", "proveedor", "gerencia",
            "articulo_propuesto", "nombre_articulo", "dictamen", "alcance", "proyecto", "cliente",
            "propuesto_por", "dictaminado_por", "fecha_dictamen", "notas_gerencia", "creation",
        ],
        order_by="modified desc",
        limit_page_length=500,
    )
    rows.sort(key=lambda r: 0 if r.dictamen == "Pendiente" else 1)

    resumen = {
        "pendientes": sum(1 for r in rows if r.dictamen == "Pendiente"),
        "aprobadas": sum(1 for r in rows if r.dictamen in ("Exacto (Alias directo)", "Equivalente aprobado")),
        "puntuales": sum(1 for r in rows if r.dictamen == "Sustituto puntual"),
        "rechazadas": sum(1 for r in rows if r.dictamen == "Distinto (No agrupar)"),
    }

    est_f = (estado or "").strip().lower()
    ger_f = (gerencia or "").strip()
    q_norm = _sin_acentos(q)

    filtradas = []
    for r in rows:
        if est_f == "pendientes" and r.dictamen != "Pendiente":
            continue
        if est_f == "aprobadas" and r.dictamen not in ("Exacto (Alias directo)", "Equivalente aprobado"):
            continue
        if ger_f and (r.gerencia or "") != ger_f:
            continue
        if q_norm:
            blob = _sin_acentos(f"{r.name} {r.texto_original} {r.clave_externa} {r.articulo_propuesto} {r.nombre_articulo} {r.proveedor} {r.proyecto}")
            if q_norm not in blob:
                continue
        filtradas.append(r)

    skus = frappe.get_all(
        "Item",
        filters={"disabled": 0, "name": ["!=", "LEBAREF-CONCEPTO-LIBRE"]},
        fields=["name", "item_name", "custom_lebaref_codigo_barras"],
        order_by="item_name asc",
        limit_page_length=1000,
    )
    obras = frappe.get_all("Project", pluck="name", order_by="name asc", limit_page_length=300)
    clientes = frappe.get_all("Customer", pluck="name", order_by="name asc", limit_page_length=300)
    proveedores = frappe.get_all("Supplier", filters={"disabled": 0}, pluck="name", order_by="name asc", limit_page_length=500)

    return {
        "resumen": resumen,
        "items": filtradas,
        "skus": skus,
        "obras": obras,
        "clientes": clientes,
        "proveedores": proveedores,
        "gerencias": list(GERENCIAS_OFICIALES),
    }


# =============================================================================
# MIG-02: Importador Asistido de Plantilla_Cotizaciones_Historicas_Lebaref.xlsx
# =============================================================================

def _calcular_huella_cotizacion_historica(fecha_str: str, proveedor: str, desc: str, moneda: str, precio: float, folio_prov: str) -> str:
    semilla = "|".join([
        str(fecha_str or "").strip(),
        _sin_acentos(proveedor),
        _sin_acentos(desc),
        str(moneda or "MXN").strip().upper(),
        f"{flt(precio, 4):.4f}",
        _sin_acentos(folio_prov),
    ])
    return hashlib.sha256(semilla.encode("utf-8")).hexdigest()


def _parsear_fecha_flexible(valor: str) -> str:
    s = str(valor or "").strip()
    if not s:
        return ""
    # Número serial de Excel (ej. 46070 -> 2026-02-17)
    if re.fullmatch(r"\d{5}(\.\d+)?", s):
        from datetime import date, timedelta
        serial = int(float(s))
        if 35000 <= serial <= 60000:
            return str(date(1899, 12, 30) + timedelta(days=serial))
    s_corta = s[:10]
    m_dmy = re.fullmatch(r"(\d{1,2})[/-](\d{1,2})[/-](\d{4})", s)
    if m_dmy:
        d_i, m_i, y_i = int(m_dmy.group(1)), int(m_dmy.group(2)), int(m_dmy.group(3))
        return f"{y_i:04d}-{m_i:02d}-{d_i:02d}"
    try:
        return str(getdate(s_corta))
    except Exception:
        return ""


def _parsear_numero(val, default=0.0) -> float:
    s = str(val or "").replace("$", "").replace(",", "").strip()
    if not s:
        return default
    try:
        return float(s)
    except ValueError:
        return default


def _resolver_proveedor_existente(nombre_prov: str) -> str:
    limpio = (nombre_prov or "").strip()
    if not limpio:
        return ""
    if frappe.db.exists("Supplier", limpio):
        return limpio
    por_nom = frappe.db.get_value("Supplier", {"supplier_name": limpio}, "name")
    if por_nom:
        return por_nom
    clave = _sin_acentos(limpio)
    for s in frappe.get_all("Supplier", fields=["name", "supplier_name"], limit_page_length=1000):
        if _sin_acentos(s.name) == clave or _sin_acentos(s.supplier_name) == clave:
            return s.name
    return ""


@frappe.whitelist(methods=["POST"])
def previsualizar_importacion_cotizaciones_historicas(texto_csv: str = "", filename: str = "", content_base64: str = ""):
    """Analiza Plantilla_Cotizaciones_Historicas_Lebaref.xlsx o CSV, valida USD+DOF, detecta duplicados y propone vínculo a SKU."""
    from lebaref_compras import analitica_operativa

    frappe.has_permission("Lebaref Cotizacion Historica", "create", throw=True)
    if content_base64 and not (texto_csv or "").strip():
        texto_csv = analitica_operativa.extraer_texto_desde_archivo_base64(filename, content_base64)
    raw = (texto_csv or "").strip()
    if not raw:
        frappe.throw("Selecciona el archivo Excel (.xlsx) de cotizaciones históricas o pega las filas.")

    primera_linea = raw.splitlines()[0]
    delim = "\t" if "\t" in primera_linea else (";" if ";" in primera_linea and "," not in primera_linea else ",")
    filas_csv = list(csv.reader(io.StringIO(raw), delimiter=delim))
    if not filas_csv:
        frappe.throw("El archivo no contiene filas legibles.")

    # Mapear columnas por encabezado si existe, o por orden posicional oficial de las 21 columnas de MIG-01
    orden_oficial = [
        "fecha_cotizacion", "proveedor", "descripcion", "cantidad", "unidad", "moneda", "precio", "tc_dof",
        "codigo_interno", "clave_proveedor", "marca", "folio_cotizacion", "flete", "modalidad_entrega",
        "dias_entrega", "vigencia_dias", "condicion_pago", "gerencia", "obra_referencia", "se_compro", "notas",
    ]
    mapa_idx = {campo: i for i, campo in enumerate(orden_oficial)}
    inicio = 0
    hdr_norm = [_sin_acentos(c) for c in filas_csv[0]]
    if any("FECHA" in h or "PROVEEDOR" in h or "PRECIO" in h or "DESCRIPCION" in h for h in hdr_norm):
        inicio = 1
        alias_hdr = {
            "FECHA_COTIZACION": "fecha_cotizacion",
            "FECHA": "fecha_cotizacion",
            "PROVEEDOR": "proveedor",
            "DESCRIPCION_PROVEEDOR_O_SOLICITADA": "descripcion",
            "DESCRIPCION": "descripcion",
            "CONCEPTO": "descripcion",
            "CANTIDAD_COTIZADA": "cantidad",
            "CANTIDAD": "cantidad",
            "UNIDAD": "unidad",
            "MONEDA": "moneda",
            "PRECIO_UNITARIO_SIN_IVA": "precio",
            "PRECIO_SIN_IVA": "precio",
            "PRECIO": "precio",
            "TIPO_CAMBIO_DOF": "tc_dof",
            "TC_DOF": "tc_dof",
            "CODIGO_INTERNO_LEBAREF": "codigo_interno",
            "SKU": "codigo_interno",
            "CLAVE_O_MODELO_PROVEEDOR": "clave_proveedor",
            "MARCA": "marca",
            "FOLIO_COTIZACION_PROVEEDOR": "folio_cotizacion",
            "FLETE_SIN_IVA": "flete",
            "MODALIDAD_ENTREGA": "modalidad_entrega",
            "DIAS_ENTREGA_PROMESA": "dias_entrega",
            "VIGENCIA_DIAS": "vigencia_dias",
            "CONDICION_PAGO": "condicion_pago",
            "GERENCIA_O_AREA": "gerencia",
            "CLIENTE_U_OBRA_REFERENCIA": "obra_referencia",
            "SE_COMPRO_REALMENTE": "se_compro",
            "NOTAS": "notas",
        }
        mapa_dinamico = {}
        for i, h in enumerate(hdr_norm):
            h_key = re.sub(r"[^A-Z0-9]+", "_", h).strip("_")
            if h_key in alias_hdr:
                mapa_dinamico[alias_hdr[h_key]] = i
        if "proveedor" in mapa_dinamico and "precio" in mapa_dinamico:
            mapa_idx = mapa_dinamico

    def _celda(cols, clave, default=""):
        pos = mapa_idx.get(clave, -1)
        if pos < 0 or pos >= len(cols):
            return default
        return (cols[pos] or "").strip()

    huellas_bd = set(frappe.get_all("Lebaref Cotizacion Historica", pluck="huella_fila", limit_page_length=0))
    huellas_lote = set()

    filas_validas = []
    errores = []
    ejemplos_ignorados = 0
    duplicadas = 0

    for num_fila, cols in enumerate(filas_csv[inicio:], start=inicio + 1):
        if not cols or not any((c or "").strip() for c in cols):
            continue

        desc = _celda(cols, "descripcion")
        if "[EJEMPLO" in desc.upper() or "EJEMPLO - BORRAR" in desc.upper():
            ejemplos_ignorados += 1
            continue

        fecha_raw = _celda(cols, "fecha_cotizacion")
        fecha_ok = _parsear_fecha_flexible(fecha_raw)
        prov_txt = _celda(cols, "proveedor")
        cant = _parsear_numero(_celda(cols, "cantidad", "1"), 1.0)
        unidad = _celda(cols, "unidad", "Pieza") or "Pieza"
        moneda = (_celda(cols, "moneda", "MXN") or "MXN").upper()
        precio = _parsear_numero(_celda(cols, "precio"), 0.0)
        tc_dof = _parsear_numero(_celda(cols, "tc_dof"), 0.0)
        flete = max(0.0, _parsear_numero(_celda(cols, "flete"), 0.0))

        if not fecha_ok:
            errores.append(f"Fila {num_fila}: Fecha '{fecha_raw}' inválida (usa AAAA-MM-DD).")
            continue
        if not prov_txt:
            errores.append(f"Fila {num_fila}: Falta el nombre del Proveedor.")
            continue
        if not desc:
            errores.append(f"Fila {num_fila}: Falta Descripcion_Proveedor_o_Solicitada.")
            continue
        if cant <= 0:
            errores.append(f"Fila {num_fila} ({desc[:40]}): Cantidad debe ser mayor que cero.")
            continue
        if moneda not in ("MXN", "USD"):
            errores.append(f"Fila {num_fila} ({desc[:40]}): Moneda '{moneda}' inválida (usa MXN o USD).")
            continue
        if precio <= 0:
            errores.append(f"Fila {num_fila} ({desc[:40]}): Precio_Unitario_Sin_IVA debe ser mayor que cero.")
            continue
        if moneda == "USD" and tc_dof <= 0:
            errores.append(f"Fila {num_fila} ({desc[:40]}): Cotización en USD exige capturar Tipo_Cambio_DOF del día (> 0).")
            continue

        folio_prov = _celda(cols, "folio_cotizacion")
        huella = _calcular_huella_cotizacion_historica(fecha_ok, prov_txt, desc, moneda, precio, folio_prov)
        if huella in huellas_bd or huella in huellas_lote:
            duplicadas += 1
            continue
        huellas_lote.add(huella)

        prov_id = _resolver_proveedor_existente(prov_txt)
        cod_in = _celda(cols, "codigo_interno")
        clave_prov = _celda(cols, "clave_proveedor")
        obra_ref = _celda(cols, "obra_referencia")

        # Intentar vincular al catálogo por Código_Interno, luego por Clave_Proveedor y luego por Descripción / Alias
        it_match, origen_vinc = (None, "")
        for candidato_tok in (cod_in, clave_prov, desc):
            if candidato_tok:
                it_match, origen_vinc = analitica_operativa._resolver_sku_o_concepto(
                    candidato_tok,
                    proyecto=obra_ref,
                    proveedor=prov_id,
                )
                if it_match:
                    break

        factor_tc = tc_dof if moneda == "USD" else 1.0
        precio_mxn = round(precio * factor_tc, 4)
        flete_unit_mxn = round((flete / cant) * factor_tc, 4) if flete > 0 and cant > 0 else 0.0

        filas_validas.append({
            "fila": num_fila,
            "huella_fila": huella,
            "fecha_cotizacion": fecha_ok,
            "proveedor_texto": prov_txt[:140],
            "proveedor": prov_id,
            "descripcion_original": desc,
            "item_code": it_match.name if it_match else "",
            "item_name": it_match.item_name if it_match else "",
            "origen_vinculo": origen_vinc if it_match else "Concepto histórico sin SKU",
            "cantidad": cant,
            "unidad": unidad[:40],
            "moneda": moneda,
            "precio_unitario_sin_iva": precio,
            "tc_dof": tc_dof if moneda == "USD" else 1.0,
            "precio_unitario_mxn": precio_mxn,
            "flete_sin_iva": flete,
            "precio_sitio_mxn": round(precio_mxn + flete_unit_mxn, 4),
            "clave_modelo_proveedor": clave_prov[:140],
            "marca": _celda(cols, "marca")[:140],
            "folio_cotizacion_proveedor": folio_prov[:140],
            "modalidad_entrega": _celda(cols, "modalidad_entrega")[:140],
            "dias_entrega_promesa": max(0, int(_parsear_numero(_celda(cols, "dias_entrega"), 0))),
            "vigencia_dias": max(0, int(_parsear_numero(_celda(cols, "vigencia_dias"), 0))),
            "condicion_pago": _celda(cols, "condicion_pago")[:140],
            "gerencia": _celda(cols, "gerencia")[:140],
            "obra_referencia": obra_ref[:140],
            "se_compro_realmente": _celda(cols, "se_compro")[:40],
            "notas": _celda(cols, "notas"),
        })

    skus_disp = frappe.get_all(
        "Item",
        filters={"disabled": 0, "name": ["!=", "LEBAREF-CONCEPTO-LIBRE"]},
        fields=["name", "item_name", "custom_lebaref_codigo_barras"],
        order_by="item_name asc",
        limit_page_length=1000,
    )

    return {
        "resumen": {
            "listas_para_importar": len(filas_validas),
            "vinculadas_a_sku": sum(1 for f in filas_validas if f["item_code"]),
            "sin_sku": sum(1 for f in filas_validas if not f["item_code"]),
            "duplicadas_omitidas": duplicadas,
            "ejemplos_ignorados": ejemplos_ignorados,
            "errores": len(errores),
        },
        "errores": errores[:50],
        "filas": filas_validas,
        "skus": skus_disp,
    }


@frappe.whitelist(methods=["POST"])
def ejecutar_importacion_cotizaciones_historicas(datos: str):
    """Guarda las cotizaciones históricas validadas (sin alterar stock ni crear compras) y registra alias/candidatos."""
    frappe.has_permission("Lebaref Cotizacion Historica", "create", throw=True)
    payload = frappe.parse_json(datos) if isinstance(datos, str) else datos
    if not isinstance(payload, dict):
        frappe.throw("Datos de importación inválidos.")

    filas = payload.get("filas") or []
    registrar_alias_vinculados = bool(cint(payload.get("registrar_alias_vinculados", 1)))
    enviar_sin_sku_a_bandeja = bool(cint(payload.get("enviar_sin_sku_a_bandeja", 0)))

    if not filas:
        frappe.throw("No hay filas nuevas para importar.")

    huellas_existentes = set(frappe.get_all("Lebaref Cotizacion Historica", pluck="huella_fila", limit_page_length=0))
    importadas = 0
    omitidas = 0
    alias_creados = 0
    propuestas_bandeja = 0

    for f in filas:
        huella = (f.get("huella_fila") or "").strip()
        if not huella:
            huella = _calcular_huella_cotizacion_historica(
                f.get("fecha_cotizacion"),
                f.get("proveedor_texto"),
                f.get("descripcion_original"),
                f.get("moneda"),
                f.get("precio_unitario_sin_iva"),
                f.get("folio_cotizacion_proveedor"),
            )
        if huella in huellas_existentes:
            omitidas += 1
            continue
        huellas_existentes.add(huella)

        sku = (f.get("item_code") or "").strip()
        if sku and not frappe.db.exists("Item", sku):
            sku = ""

        moneda = (f.get("moneda") or "MXN").upper()
        precio = flt(f.get("precio_unitario_sin_iva") or 0, 4)
        tc = flt(f.get("tc_dof") or 1, 4) if moneda == "USD" else 1.0
        if precio <= 0 or (moneda == "USD" and tc <= 0):
            omitidas += 1
            continue

        cant = max(0.0001, flt(f.get("cantidad") or 1))
        flete = max(0.0, flt(f.get("flete_sin_iva") or 0))
        precio_mxn = round(precio * tc, 4)
        precio_sitio = round(precio_mxn + ((flete / cant) * tc if flete > 0 else 0.0), 4)

        prov_id = (f.get("proveedor") or "").strip() or _resolver_proveedor_existente(f.get("proveedor_texto") or "")

        frappe.get_doc({
            "doctype": "Lebaref Cotizacion Historica",
            "fecha_cotizacion": f.get("fecha_cotizacion"),
            "proveedor_texto": (f.get("proveedor_texto") or "")[:140],
            "proveedor": prov_id or None,
            "descripcion_original": f.get("descripcion_original") or "",
            "item_code": sku or None,
            "cantidad": cant,
            "unidad": (f.get("unidad") or "Pieza")[:40],
            "moneda": moneda,
            "precio_unitario_sin_iva": precio,
            "tc_dof": tc,
            "precio_unitario_mxn": precio_mxn,
            "flete_sin_iva": flete,
            "precio_sitio_mxn": precio_sitio,
            "clave_modelo_proveedor": (f.get("clave_modelo_proveedor") or "")[:140],
            "marca": (f.get("marca") or "")[:140],
            "folio_cotizacion_proveedor": (f.get("folio_cotizacion_proveedor") or "")[:140],
            "modalidad_entrega": (f.get("modalidad_entrega") or "")[:140],
            "dias_entrega_promesa": cint(f.get("dias_entrega_promesa") or 0),
            "vigencia_dias": cint(f.get("vigencia_dias") or 0),
            "condicion_pago": (f.get("condicion_pago") or "")[:140],
            "gerencia": (f.get("gerencia") or "")[:140],
            "obra_referencia": (f.get("obra_referencia") or "")[:140],
            "se_compro_realmente": (f.get("se_compro_realmente") or "")[:40],
            "origen_vinculo": (f.get("origen_vinculo") or ("Asignado en importación" if sku else "Sin SKU"))[:140],
            "notas": f.get("notas") or "",
            "huella_fila": huella,
        }).insert(ignore_permissions=True)
        importadas += 1

        desc_orig = (f.get("descripcion_original") or "").strip()
        if sku and registrar_alias_vinculados and desc_orig:
            nombre_cat = frappe.db.get_value("Item", sku, "item_name") or ""
            if _sin_acentos(desc_orig) != _sin_acentos(nombre_cat):
                proponer_alias_o_equivalencia(json.dumps({
                    "texto_original": desc_orig,
                    "clave_externa": f.get("clave_modelo_proveedor") or "",
                    "origen": "Importación Histórica",
                    "proveedor": prov_id,
                    "gerencia": f.get("gerencia") or "",
                    "articulo_propuesto": sku,
                    "dictamen": "Exacto (Alias directo)",
                    "alcance": "Toda la empresa",
                }))
                alias_creados += 1
        elif not sku and enviar_sin_sku_a_bandeja and desc_orig:
            proponer_alias_o_equivalencia(json.dumps({
                "texto_original": desc_orig,
                "clave_externa": f.get("clave_modelo_proveedor") or "",
                "origen": "Importación Histórica",
                "proveedor": prov_id,
                "gerencia": f.get("gerencia") or "",
                "dictamen": "Pendiente",
                "alcance": "Toda la empresa",
            }))
            propuestas_bandeja += 1

    return {
        "ok": True,
        "importadas": importadas,
        "omitidas": omitidas,
        "alias_creados": alias_creados,
        "propuestas_bandeja": propuestas_bandeja,
    }
