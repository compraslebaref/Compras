# Tablero de Coordinación — Codex & Antigravity (Enfoque Oficial: ERPNext Directo + Adecuaciones Lebaref)

> **Decisión confirmada por el usuario (19-nov-2026):**
> Usaremos **ERPNext 16 directo** (tanto **sus procesos establecidos** como **su diseño e interfaz nativa Frappe Desk en español**) como el programa oficial para integrar en la empresa, amoldándolo por dentro en [`lebaref_compras`](integracion_erpnext/app/lebaref_compras) con las herramientas propias de Lebaref.
> **Queda descartado seguir reescribiendo pantallas paralelas en C# (WPF / WinForms).** Todo el trabajo de ambos agentes se concentra en dejar **ERPNext (`http://127.0.0.1:8186/app/lebaref`)** listo para operar con los auxiliares.

---

## Estado Base Ya Logrado en el Servidor ERPNext (`lebaref-pruebas.local`)

- **Catálogos reales de Lebaref cargados (889 registros, 0 advertencias):**
  - `49` Proveedores (`Supplier`), `23` Obras (`Project`), `665` Artículos (`Item`), `149` Herramientas (`Lebaref Herramienta`) y `3` Vehículos (`Lebaref Vehiculo`).
- **Identidad corporativa vectorizada y navegación unificada en Frappe Desk 16 (`8b60676`):**
  - Logo oficial vectorizado SVG (`lebaref_logo_completo.svg`, `lebaref_logo_horizontal.svg`, `lebaref_isotipo.svg` y `lebaref.ico`) aplicado en login, barra lateral, membrete predeterminado y formatos Carta.
  - `Sidebar` en español y `Dock` unificado (`Lebaref` fijo en `#1`, módulos ajenos ocultos y sin saltos al abrir `Item` o `Supplier`).
- **Backend Bloques 1 y 2 entregados y verificados por Codex e integrados en Desk:**
  - `COM-02` (`96f8082`): Ofertas parciales por renglón en `Supplier Quotation` y `comparativa.py`.
  - `CAT-02` (`1371366`): Unidades de compra vs. almacén y `conversion_factor` en `Item`, `solicitud`, `oferta` y `recibir`.
  - `CAT-03` (`00f2a90`): Teléfono, contacto y correo en `Supplier` + `Contact`.
  - `SOL-02` (`daf8a9f`): Solicitudes mixtas con conceptos libres sin SKU previo, departamento, solicitante y notas.
  - `COM-04` / `COM-07` (`21d7e85`): IVA 16%, exento, reglas de flete y programación de pagos heredados hasta `Purchase Order`.
  - `SOL-03` / `SOL-05` / `CAT-04` (`0797f43`): Anexos vinculados a solicitud origen, conversión de concepto libre a SKU y surtido desde stock.

---

## Reparto de Trabajo Actual (Cero Colisiones dentro de `lebaref_compras`)

### Carril Backend Transaccional — **Codex** (`lebaref_compras/*.py` core + pruebas)

| Prioridad | ID Matriz | Tarea en ERPNext (`lebaref_compras`) | Estado |
|---|---|---|---|
| 1 | `COM-04` / `COM-07` | **Impuestos (IVA 16% / exento), flete y condiciones/programación de pago** en `Supplier Quotation` y `Purchase Order` usando plantillas estándar de ERPNext (caso: neto `400` + IVA `64` = `464`). | `COMPLETADO E INTEGRADO EN DESK` |
| 2 | `SOL-03` / `SOL-05` / `CAT-04` | **Anexos vinculados a solicitud origen y ruta de surtido desde stock** (`Stock Entry` tipo transferencia/salida ligada a `Material Request`) + conversión posterior de concepto libre a `Item` inventariable. | `COMPLETADO E INTEGRADO EN DESK` |
| 3 | `ALM-02`..`ALM-08` | **Variantes de almacén en ERPNext:** entrada por regularizar, recepción directa a obra/servicio, sustitución autorizada al recibir, distinción clara *Resguardo en Obra* (`Material Transfer`) vs. *Consumo Real* (`Material Issue`) y ajuste/merma (`Stock Reconciliation`). | `SIGUIENTE PARA CODEX` |
| 4 | `XML-02`..`XML-04` | **CFDI 4.0 y compras con nota/ticket:** lectura de conceptos/importes del XML contra `Purchase Receipt` / `Purchase Order` (o vínculo con `Purchase Invoice`) y registro auditable de nota sin CFDI. | `PENDIENTE CODEX` |

### Carril Experiencia ERPNext Desk, Impresión Carta e Integraciones — **Antigravity** (`public/js/**`, `public/css/**`, `desk_ui.py`, `impresion_carta.py`, `sheets_proveedores.py`)

| Prioridad | ID Matriz | Tarea en la Interfaz Nativa de ERPNext (`Frappe Desk`) | Estado |
|---|---|---|---|
| 1 | `UX-DESK-01` | **Workspace "Lebaref", Sidebar/Dock unificados y botones guiados en Desk (`public/js/lebaref_desk.js` + `desk_ui.py`):** 19 accesos por sección, riel unificado sin saltos, identidad vectorial SVG y botones guiados para `SOL-03`, `SOL-05`, `CAT-04`, `COM-04/07`, Almacén, `HER`, `KIT`, `PRE` y `VEH`. | `COMPLETADO Y MIGRADO` |
| 2 | `COM-02` / `COM-04` / `COM-07` | **Cuadro Comparativo Lebaref con desglose comercial completo (`public/js/material_request.js` + `desk_ui.py`):** Plantillas automáticas por empresa (`IVA Compras 16%`, `Compra Exenta 0%`, `Flete Local/Foráneo`, `Contado`, `Crédito 15/30 días`, `Anticipo 50%`) y comparativa mostrando Subtotal, IVA/flete, Total cotizado y condición de pago. | `COMPLETADO Y MIGRADO` |
| 3 | `DOC-01` | **7 Formatos de Impresión Tamaño Carta con Logo Vectorial SVG y Código de Barras Code 128 (`impresion_carta.py`):** `Solicitud`, `Orden de Compra` (con desglose de impuestos y tabla de vencimientos), `Recepción Almacén`, `Salida o Sobrante`, `Responsiva EPP y Equipo`, `Vale Préstamo Herramienta` y `Check-list Vehicular`. | `COMPLETADO (7 DE 7 FORMATOS)` |
| 4 | `CAT-03` / `ALM-MAPA` | **Directorio `¿A quién cotizar?` conectado a Google Sheets (`sheets_proveedores.py`) y Mapa Visual interactivo 3×2 de Zonas A–F (`desk_ui.py` + `lebaref_desk.js`).** | `COMPLETADO Y MIGRADO` |
| 5 | `UX-DESK-03` | **Conectar en Desk las variantes de almacén (`ALM-02..08`) y conciliación de CFDI/notas (`XML-02..04`)** conforme Codex publique sus esquemas en `operaciones.py`. | `EN ESPERA DE BLOQUES 3 Y 4 DE CODEX` |

---

## Bitácora de Entregas Entre Agentes (Más reciente arriba)

- **2026-11-20 — Antigravity (`COM-04/07`, `SOL-03/05/CAT-04`, `DOC-01`, `ALM-MAPA`, `CAT-03`):** Fusionado `codex/erpnext` (`21d7e85`, `0797f43`, `0764f07`, `b4ccda1`) en `integracion/erpnext`; aprovisionadas plantillas fiscales, de flete y de pago por empresa en `desk_ui.py`; integrados en Desk los botones de anexo, conversión de concepto libre a SKU y surtido desde stock, el desglose comercial en comparativa e impresión de OC, el 7.º formato Carta (`Responsiva EPP y Equipo`), el Mapa Visual interactivo Zonas A–F y el Directorio `¿A quién cotizar?` (`sheets_proveedores.py`).
- **2026-11-19 — Antigravity (`8b60676`):** Unificados `Sidebar` y `Dock` de Frappe 16 sin saltos e integrado logotipo oficial vectorizado SVG/ICO de Lebaref en Desk y formatos Carta.
- **2026-11-19 — Codex, SOL-03 / SOL-05 / CAT-04 (`0797f43`).** Verificados el anexo en borrador, la conversión a SKU antes de dependencias activas, la salida parcial y el traslado vinculado a solicitud.
- **2026-11-19 — Codex, COM-04 / COM-07 (`21d7e85`).** Verificados IVA 16 %, exento, flete y programación, incluida la creación nativa de orden.
- **2026-11-19 — Codex, validación (`0764f07`).** Migración y las tres suites superadas en `lebaref-pruebas.local`.
- **2026-11-19 — Alineación Definitiva a ERPNext Directo (Usuario + Antigravity + Codex):** Confirmado por Oscar usar **ERPNext 16 directo (procesos + interfaz nativa Frappe Desk)** amoldado con `lebaref_compras`.
