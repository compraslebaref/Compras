# Tablero de Coordinación — Codex & Antigravity (Enfoque Oficial: ERPNext Directo + Adecuaciones Lebaref)

> **Decisión confirmada por el usuario (19-nov-2026):**
> Usaremos **ERPNext 16 directo** (tanto **sus procesos establecidos** como **su diseño e interfaz nativa Frappe Desk en español**) como el programa oficial para integrar en la empresa, amoldándolo por dentro en [`lebaref_compras`](integracion_erpnext/app/lebaref_compras) con las herramientas propias de Lebaref.
> **Queda descartado seguir reescribiendo pantallas paralelas en C# (WPF / WinForms).** Todo el trabajo de ambos agentes se concentra en dejar **ERPNext (`http://127.0.0.1:8186/app/lebaref`)** listo para operar con los auxiliares.

---

## Estado Base Ya Logrado en el Servidor ERPNext (`lebaref-pruebas.local`)

- **Catálogos reales de Lebaref cargados (889 registros, 0 advertencias):**
  - `49` Proveedores (`Supplier`), `23` Obras (`Project`), `665` Artículos (`Item`), `149` Herramientas (`Lebaref Herramienta`) y `3` Vehículos (`Lebaref Vehiculo`).
- **Backend Bloque 1 entregado y verificado por Codex:**
  - `COM-02` (`96f8082`): Ofertas parciales por renglón en `Supplier Quotation` y `comparativa.py`.
  - `CAT-02` (`1371366`): Unidades de compra vs. almacén y `conversion_factor` en `Item`, `solicitud`, `oferta` y `recibir`.
  - `CAT-03` (`00f2a90`): Teléfono, contacto y correo en `Supplier` + `Contact`.
  - `SOL-02` (`daf8a9f`): Solicitudes mixtas con conceptos libres sin SKU previo, departamento, solicitante y notas.

---

## Reparto de Trabajo Actual (Cero Colisiones dentro de `lebaref_compras`)

### Carril Backend Transaccional — **Codex** (`lebaref_compras/*.py` core + pruebas)

| Prioridad | ID Matriz | Tarea en ERPNext (`lebaref_compras`) | Estado |
|---|---|---|---|
| 1 | `COM-04` / `COM-07` | **Impuestos (IVA 16% / exento), flete y condiciones/programación de pago** en `Supplier Quotation` y `Purchase Order` usando plantillas estándar de ERPNext (caso: neto `400` + IVA `64` = `464`). | `SIGUIENTE PARA CODEX` |
| 2 | `SOL-03` / `SOL-05` | **Anexos vinculados a solicitud origen y ruta de surtido desde stock** (`Stock Entry` tipo transferencia/salida ligada a `Material Request`) + conversión posterior de concepto libre a `Item` inventariable (`CAT-04`). | `PENDIENTE CODEX` |
| 3 | `ALM-02`..`ALM-08` | **Variantes de almacén en ERPNext:** entrada por regularizar, recepción directa a obra/servicio, sustitución autorizada al recibir, distinción clara *Resguardo en Obra* (`Material Transfer`) vs. *Consumo Real* (`Material Issue`) y ajuste/merma (`Stock Reconciliation`). | `PENDIENTE CODEX` |
| 4 | `XML-02`..`XML-04` | **CFDI 4.0 y compras con nota/ticket:** lectura de conceptos/importes del XML contra `Purchase Receipt` / `Purchase Order` (o vínculo con `Purchase Invoice`) y registro auditable de nota sin CFDI. | `PENDIENTE CODEX` |

### Carril Experiencia ERPNext Desk, Impresión Carta e Integraciones — **Antigravity** (`public/js/**`, `public/css/**`, `desk_ui.py`, `impresion_carta.py`, `sheets_proveedores.py`)

| Prioridad | ID Matriz | Tarea en la Interfaz Nativa de ERPNext (`Frappe Desk`) | Estado |
|---|---|---|---|
| 1 | `UX-DESK-01` | **Workspace "Lebaref" completo y botones guiados en Desk (`public/js/lebaref_desk.js` + `desk_ui.py`):** 19 accesos por sección en el Workspace Lebaref, ejecutor universal de operaciones guiadas en Desk para Compras, Almacén, Herramientas `HER`, Kits `KIT`, Préstamos `PRE` y Flotilla `VEH`, y diálogo de Mapa de Ubicaciones. | `COMPLETADO Y MIGRADO` |
| 2 | `COM-02` / `UX-DESK-02` | **Cuadro Comparativo Lebaref mejorado dentro de ERPNext (`public/js/material_request.js`):** Matriz multiproveedor lado a lado mostrando *"Sin cotizar"*, insignia `★ Mejor`, selección automática del mejor precio, totales adjudicados por proveedor, impresión Carta horizontal y enlace a Google Sheets *¿A quién cotizar?*. | `COMPLETADO Y MIGRADO` |
| 3 | `DOC-01` | **Formatos de Impresión Tamaño Carta con Código de Barras Code 128 (`impresion_carta.py`):** 6 Print Formats Jinja en español con Code 128B SVG nativo (`Solicitud`, `Orden de Compra`, `Recepción Almacén`, `Salida o Sobrante`, `Vale Préstamo Herramienta`, `Check-list Vehicular`). | `COMPLETADO BASE (6 FORMATOS) · FALTA RESPONSIVA EPP` |
| 4 | `CAT-03` / `ALM-MAPA` | **Sincronización ERPNext ↔ Google Sheets (`1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I`) desde ERPNext (`sheets_proveedores.py`)** y cuadrícula visual 3×3 interactiva A–F. | `SIGUIENTE PARA ANTIGRAVITY` |

---

## Bitácora de Entregas Entre Agentes (Más reciente arriba)

- **2026-11-19 — Alineación Definitiva a ERPNext Directo (Usuario + Antigravity + Codex):** Confirmado por Oscar usar **ERPNext 16 directo (procesos + interfaz nativa Frappe Desk)** amoldado con `lebaref_compras` para salir rápido y sólido a producción en la empresa. Integrados `1371366` (`CAT-02`), `00f2a90` (`CAT-03`) y `daf8a9f` (`SOL-02`) en `integracion/erpnext`.
- **2026-11-19 — Codex, SOL-02 (`daf8a9f`), CAT-03 (`00f2a90`), CAT-02 (`1371366`), COM-02 (`96f8082`):** Entregados y verificados en backend y documentados en [`CONTRATO-API.md`](integracion_erpnext/CONTRATO-API.md).
- **2026-11-19 — Antigravity ([OPS-01 / CAT-03] `45eee19`, `7d80cf2`):** Migrados 889 maestros reales a `lebaref-pruebas.local` y creada hoja oficial Google Sheets *¿A quién cotizar?*.
