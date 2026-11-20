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
| 3 | `ALM-02`..`ALM-08` | **Variantes de almacén en ERPNext:** entrada por regularizar, recepción directa a obra/servicio, sustitución autorizada al recibir, distinción clara *Resguardo en Obra* (`Material Transfer`) vs. *Consumo Real* (`Material Issue`) y ajuste/merma (`Stock Reconciliation`). | `ALM-02 y ALM-03 COMPLETADOS E INTEGRADOS EN DESK` · ALM-04 BACKEND VERIFICADO (eef09e4); ALM-05 BACKEND VERIFICADO (68ccbdb); ALM-06..08 pendientes |
| 4 | `XML-02`..`XML-04` | **CFDI 4.0 y compras con nota/ticket:** lectura de conceptos/importes del XML contra `Purchase Receipt` / `Purchase Order` (o vínculo con `Purchase Invoice`) y registro auditable de nota sin CFDI. | `PENDIENTE CODEX` |

### Carril Experiencia ERPNext Desk, Impresión Carta e Integraciones — **Antigravity** (`public/js/**`, `public/css/**`, `desk_ui.py`, `impresion_carta.py`, `sheets_proveedores.py`)

| Prioridad | ID Matriz | Tarea en la Interfaz Nativa de ERPNext (`Frappe Desk`) | Estado |
|---|---|---|---|
| 1 | `UX-DESK-01` | **Workspace "Lebaref", Sidebar/Dock unificados y botones guiados en Desk (`public/js/lebaref_desk.js` + `desk_ui.py`):** 19 accesos por sección, riel unificado sin saltos, identidad vectorial SVG y botones guiados para `SOL-03`, `SOL-05`, `CAT-04`, `COM-04/07`, Almacén, `HER`, `KIT`, `PRE` y `VEH`. | `COMPLETADO Y MIGRADO` |
| 2 | `COM-02` / `COM-04` / `COM-07` | **Cuadro Comparativo Lebaref con desglose comercial completo (`public/js/material_request.js` + `desk_ui.py`):** Plantillas automáticas por empresa (`IVA Compras 16%`, `Compra Exenta 0%`, `Flete Local/Foráneo`, `Contado`, `Crédito 15/30 días`, `Anticipo 50%`) y comparativa mostrando Subtotal, IVA/flete, Total cotizado y condición de pago. | `COMPLETADO Y MIGRADO` |
| 3 | `DOC-01` | **7 Formatos de Impresión Tamaño Carta con Logo Vectorial SVG y Código de Barras Code 128 (`impresion_carta.py`):** `Solicitud`, `Orden de Compra` (con desglose de impuestos y tabla de vencimientos), `Recepción Almacén`, `Salida o Sobrante`, `Responsiva EPP y Equipo`, `Vale Préstamo Herramienta` y `Check-list Vehicular`. | `COMPLETADO (7 DE 7 FORMATOS)` |
| 4 | `CAT-03` / `ALM-MAPA` | **Directorio `¿A quién cotizar?` conectado a Google Sheets (`sheets_proveedores.py`) y Mapa Visual interactivo 3×2 de Zonas A–F (`desk_ui.py` + `lebaref_desk.js`).** | `COMPLETADO Y MIGRADO` |
| 5 | `UX-DESK-03` | **Conectar en Desk las variantes de almacén (`ALM-02..08`) y conciliación de CFDI/notas (`XML-02..04`)** conforme Codex publique sus esquemas en `operaciones.py`. | `ALM-02 y ALM-03 CONECTADOS Y DESPLEGADOS EN DESK` · En espera de ALM-04..08 y XML-02..04 |

---

## Bitácora de Entregas Entre Agentes (Más reciente arriba)

- **2026-11-20 — Codex, ALM-05 (`68ccbdb`).** Publicados `consumir` y `consumos.consultar` según §12 del contrato. Consumo por Material Issue ligado a la entrega original, con obra, partida y motivo, valoración y contabilidad nativas. Consumo y devolución comparten saldo normalizado por conversión; el inventario de otra procedencia conserva su asignación. La anulación restituye saldo y costo; no se permite anular la entrega con movimientos vigentes. Nuevos hooks validate/before_cancel de Stock Entry protegen también Desk/REST nativos. Migración y tres suites superadas con `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:alm05`, incluido factor 10, gasto por obra, stock adicional, reintento y documentos alterados rechazados. Los costos de consulta requieren lectura de GL Entry. **Handoff para Antigravity** — integrar y desplegar backend/workers de la misma versión, conectar acción y consulta en Desk y adaptar `impresion_carta.py` para distinguir consumo Material Issue de retorno Material Transfer cuando existe custom_lebaref_origen. Validar el recorrido del auxiliar. Mermas y ajustes físicos siguen en ALM-08.


- **2026-11-20 — Antigravity (`ALM-02`, `ALM-03`, Migración Segura Completa + Archivo Histórico):** Fusionados `ef430ae` (`ALM-02`) y `68d31c5` (`ALM-03`) en `integracion/erpnext` y desplegados en el contenedor compartido; conectados en Desk `entrada_sin_oc`, el diálogo inteligente `abrirDialogoRegularizarEntrada` (autovínculo de renglones de OC), la recepción directa a almacén de obra / bloqueo de almacén en renglones de Servicio (`ALM-03`), el alta de `Servicio` en `Item` y el perfil `Lebaref Coordinador Operativo` (`oscar.gomez@lebaref.com`). Migrados de forma segura con respaldos previos los 665 artículos (603 fotos reales, 664 códigos de barras, 6 conversiones, 556 precios, 477 mapeos XML), el inventario físico inicial (`340` SKUs = \$132,136.32 MXN), kits, garantías, préstamos históricos, 20 solicitudes activas con 49 cotizaciones, 4 OCs abiertas y el visor `Archivo Histórico SQLite` (1,019 precios, 220 CFDIs y 229 OCs cerradas).

- **2026-11-20 — Codex, ALM-04 (`eef09e4`).** Publicada `autorizar_sustitucion` y columna opcional `sustitucion` en `recibir`. El nuevo DocType `Lebaref Sustitucion` conserva aprobación por responsable, renglón, original/sustituto, motivo, cantidad y precio. Misma unidad base, conversión y precio de la orden. Extensiones declarativas en `hooks.py` para recepción/factura y validadores de aprobación/cancelación; requieren despliegue coordinado de backend y workers y renovación de caché. Migración y tres suites superadas con `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:alm04`, incluida factura y devolución nativas y rechazo de recepción alterada. **Handoff para Antigravity** — integrar y desplegar el commit, conectar la autorización y selección al recibir según §11 del contrato, presentar original/sustituto y motivo en Desk e impresión y validar el recorrido. Cambios comerciales fuera de la equivalencia de precio y unidad requieren revisión de documentos de compra. ALM-04 sigue parcial en la matriz hasta su validación operativa.


- **2026-11-20 — Codex, ALM-03 (`68d31c5`).** `recibir` admite almacén de obra por renglón con motivo de cambio, persona que recibe y remisión. `articulo` admite `tipo_articulo=Servicio` para compras no inventariables. El POST anterior mantiene el destino de la orden. Migración y tres suites superadas con `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:alm03`. Verificados inventario directo en obra, servicio sin Bin ni Stock Ledger Entry, reintento, destinos inválidos y saldo en almacén original. **Handoff para Antigravity** — integrar y desplegar el commit, conectar §10 de CONTRATO-API.md en la captura de artículos y recepción de Desk y validar el recorrido del auxiliar. Campo nuevo `Purchase Receipt.custom_lebaref_recibido_por`. ERPNext puede completar un almacén de referencia para el servicio sin generar existencias. ALM-03 permanece parcial en la matriz hasta validar la interfaz.


- **2026-11-20 — Codex, ALM-02 (`ef430ae`).** Publicadas `entrada_sin_oc` y `regularizar_entrada` en `operaciones.py`, con campos y contratos en §9. La regularización conserva el folio, inventario y valoración de la recepción y recalcula saldos nativos; exige coincidencia de artículo, unidad, factor, obra y precio y se ejecuta antes de facturas o devoluciones vinculadas. Migración y tres suites superadas con `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:alm02`, incluido material ya consumido, reintentos y recepción del saldo. **Handoff para Antigravity** — integrar el commit, desplegar el backend y conectar los dos esquemas en Desk. La operación exige escritura de recepción y orden; el perfil Responsable de Compras carece de escritura de recepción, por lo que la prueba positiva usa Administrator. La revisión automática rechazó sustituir esos permisos por lectura/autorización; se conservaron las comprobaciones de escritura. Habilitación del perfil operativo pendiente. ALM-02 continúa parcial en la matriz hasta validar su captura desde Desk.


- **2026-11-20 — Antigravity (`COM-04/07`, `SOL-03/05/CAT-04`, `DOC-01`, `ALM-MAPA`, `CAT-03`):** Fusionado `codex/erpnext` (`21d7e85`, `0797f43`, `0764f07`, `b4ccda1`) en `integracion/erpnext`; aprovisionadas plantillas fiscales, de flete y de pago por empresa en `desk_ui.py`; integrados en Desk los botones de anexo, conversión de concepto libre a SKU y surtido desde stock, el desglose comercial en comparativa e impresión de OC, el 7.º formato Carta (`Responsiva EPP y Equipo`), el Mapa Visual interactivo Zonas A–F y el Directorio `¿A quién cotizar?` (`sheets_proveedores.py`).
- **2026-11-19 — Antigravity (`8b60676`):** Unificados `Sidebar` y `Dock` de Frappe 16 sin saltos e integrado logotipo oficial vectorizado SVG/ICO de Lebaref en Desk y formatos Carta.
- **2026-11-19 — Codex, SOL-03 / SOL-05 / CAT-04 (`0797f43`).** Verificados el anexo en borrador, la conversión a SKU antes de dependencias activas, la salida parcial y el traslado vinculado a solicitud.
- **2026-11-19 — Codex, COM-04 / COM-07 (`21d7e85`).** Verificados IVA 16 %, exento, flete y programación, incluida la creación nativa de orden.
- **2026-11-19 — Codex, validación (`0764f07`).** Migración y las tres suites superadas en `lebaref-pruebas.local`.
- **2026-11-19 — Alineación Definitiva a ERPNext Directo (Usuario + Antigravity + Codex):** Confirmado por Oscar usar **ERPNext 16 directo (procesos + interfaz nativa Frappe Desk)** amoldado con `lebaref_compras`.

- **2026-11-20 — Codex, sincronización y validación (`b56185e`).** Integrada `integracion/erpnext` en `codex/erpnext`. Resuelto el conflicto de `hooks.py` conservando los scripts de Desk de Antigravity y la herencia de condiciones de pago de Codex. Imagen `lebaref/erpnext-codex:almacen` reconstruida, migración y las tres suites de `Validar.ps1` superadas. Se reiniciaron los contenedores existentes del sitio de pruebas. ALM-02 continúa pendiente de implementación y validación.

- **2026-11-19 — Codex, SOL-03 / SOL-05 / CAT-04 (`0797f43`).** Verificados el anexo en borrador, la conversión a SKU antes de dependencias activas, la salida parcial y el traslado vinculado a solicitud. Conectar las acciones de [§8 del contrato](integracion_erpnext/CONTRATO-API.md#8-anexos-enmiendas-y-surtido-de-solicitudes-sol-03--sol-05--cat-04) en los botones de Desk. La reserva previa de existencias queda pendiente.
- **2026-11-19 — Codex, COM-04 / COM-07 (`21d7e85`).** Verificados IVA 16 %, exento, flete y programación, incluida la creación nativa de orden. `hooks.py` añade un evento `before_validate` para heredar los datos de pago. Configurar las plantillas por empresa y consumir [§7 del contrato](integracion_erpnext/CONTRATO-API.md#7-impuestos-flete-y-programación-de-pago-com-04--com-07) en la oferta y comparativa guiadas.
- **2026-11-19 — Codex, validación (`0764f07`).** Migración y las tres suites superadas en `lebaref-pruebas.local` con `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:bloque1`. El procedimiento usa contenedores temporales y conserva el contenedor compartido de Desk. [Reproducir](integracion_erpnext/auditoria/REPRODUCIR.md)

- **2026-11-19 — Alineación Definitiva a ERPNext Directo (Usuario + Antigravity + Codex):** Confirmado por Oscar usar **ERPNext 16 directo (procesos + interfaz nativa Frappe Desk)** amoldado con `lebaref_compras` para salir rápido y sólido a producción en la empresa. Integrados `1371366` (`CAT-02`), `00f2a90` (`CAT-03`) y `daf8a9f` (`SOL-02`) en `integracion/erpnext`.
- **2026-11-19 — Codex, SOL-02 (`daf8a9f`), CAT-03 (`00f2a90`), CAT-02 (`1371366`), COM-02 (`96f8082`):** Entregados y verificados en backend y documentados en [`CONTRATO-API.md`](integracion_erpnext/CONTRATO-API.md).
- **2026-11-19 — Antigravity ([OPS-01 / CAT-03] `45eee19`, `7d80cf2`):** Migrados 889 maestros reales a `lebaref-pruebas.local` y creada hoja oficial Google Sheets *¿A quién cotizar?*.

