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
| 1 | `COM-04` / `COM-07` | **Impuestos (IVA 16% / exento), flete y condiciones/programación de pago** en `Supplier Quotation` y `Purchase Order` usando plantillas estándar de ERPNext (caso: neto `400` + IVA `64` = `464`). | `BACKEND VERIFICADO (21d7e85)` · Plantillas por empresa y guías de Desk pendientes |
| 2 | `SOL-03` / `SOL-05` / `CAT-04` | **Anexos vinculados a solicitud origen y ruta de surtido desde stock** (`Stock Entry` tipo transferencia/salida ligada a `Material Request`) + conversión posterior de concepto libre a `Item` inventariable. | `BACKEND VERIFICADO (0797f43)` · Guías de Desk pendientes |
| 3 | `ALM-02`..`ALM-08` | **Variantes de almacén en ERPNext:** entrada por regularizar, recepción directa a obra/servicio, sustitución autorizada al recibir, distinción clara *Resguardo en Obra* (`Material Transfer`) vs. *Consumo Real* (`Material Issue`) y ajuste/merma (`Stock Reconciliation`). | `PENDIENTE CODEX` |
| 4 | `XML-02`..`XML-04` | **CFDI 4.0 y compras con nota/ticket:** lectura de conceptos/importes del XML contra `Purchase Receipt` / `Purchase Order` (o vínculo con `Purchase Invoice`) y registro auditable de nota sin CFDI. | `PENDIENTE CODEX` |

### Carril Experiencia ERPNext Desk, Impresión Carta e Integraciones — **Antigravity** (`public/js/**`, `public/css/**`, `desk_ui.py`, `impresion_carta.py`, `sheets_proveedores.py`)

| Prioridad | ID Matriz | Tarea en la Interfaz Nativa de ERPNext (`Frappe Desk`) | Estado |
|---|---|---|---|
| 1 | `UX-DESK-01` | **Workspace "Lebaref" completo y botones guiados en Desk (`public/js/` + `desk_ui.py`):** Accesos claros para Compras y Almacén (ocultando importes al perfil Almacén donde aplique), botones guiados en `Material Request`, `Request for Quotation`, `Purchase Order`, `Purchase Receipt`, y diálogos en Desk para operar `Lebaref Herramienta`, `Lebaref Kit`, `Lebaref Prestamo` y `Lebaref Vehiculo` sin chocar con el candado `equipos.proteger`. | `SIGUIENTE PARA ANTIGRAVITY` |
| 2 | `COM-02` / `UX-DESK-02` | **Cuadro Comparativo Lebaref mejorado dentro de ERPNext (`public/js/material_request.js`):** Vista lado a lado multiproveedor mostrando renglones *"Sin cotizar"*, mejor precio resaltado, captura ágil de ofertas desde la solicitud/petición y enlace directo a la hoja *¿A quién cotizar?*. | `SIGUIENTE PARA ANTIGRAVITY` |
| 3 | `DOC-01` | **Formatos de Impresión Tamaño Carta con Código de Barras Code 128 (`impresion_carta.py`):** Print Formats limpios en español dentro de ERPNext para Solicitud, Comparativa, Orden de Compra, Recepción, Entrega/Salida, Vale de Préstamo, Check-list Vehicular y Responsiva EPP. | `PENDIENTE ANTIGRAVITY` |
| 4 | `CAT-03` / `ALM-MAPA` | **Sincronización ERPNext ↔ Google Sheets (`1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I`) y Mapa de Ubicaciones A–F** accesible desde el Workspace Lebaref en ERPNext. | `PENDIENTE ANTIGRAVITY` |

---

## Bitácora de Entregas Entre Agentes (Más reciente arriba)

- **2026-11-19 — Codex, SOL-03 / SOL-05 / CAT-04 (`0797f43`).** Verificados el anexo en borrador, la conversión a SKU antes de dependencias activas, la salida parcial y el traslado vinculado a solicitud. Conectar las acciones de [§8 del contrato](integracion_erpnext/CONTRATO-API.md#8-anexos-enmiendas-y-surtido-de-solicitudes-sol-03--sol-05--cat-04) en los botones de Desk. La reserva previa de existencias queda pendiente.
- **2026-11-19 — Codex, COM-04 / COM-07 (`21d7e85`).** Verificados IVA 16 %, exento, flete y programación, incluida la creación nativa de orden. `hooks.py` añade un evento `before_validate` para heredar los datos de pago. Configurar las plantillas por empresa y consumir [§7 del contrato](integracion_erpnext/CONTRATO-API.md#7-impuestos-flete-y-programación-de-pago-com-04--com-07) en la oferta y comparativa guiadas.
- **2026-11-19 — Codex, validación (`0764f07`).** Migración y las tres suites superadas en `lebaref-pruebas.local` con `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:bloque1`. El procedimiento usa contenedores temporales y conserva el contenedor compartido de Desk. [Reproducir](integracion_erpnext/auditoria/REPRODUCIR.md)

- **2026-11-19 — Alineación Definitiva a ERPNext Directo (Usuario + Antigravity + Codex):** Confirmado por Oscar usar **ERPNext 16 directo (procesos + interfaz nativa Frappe Desk)** amoldado con `lebaref_compras` para salir rápido y sólido a producción en la empresa. Integrados `1371366` (`CAT-02`), `00f2a90` (`CAT-03`) y `daf8a9f` (`SOL-02`) en `integracion/erpnext`.
- **2026-11-19 — Codex, SOL-02 (`daf8a9f`), CAT-03 (`00f2a90`), CAT-02 (`1371366`), COM-02 (`96f8082`):** Entregados y verificados en backend y documentados en [`CONTRATO-API.md`](integracion_erpnext/CONTRATO-API.md).
- **2026-11-19 — Antigravity ([OPS-01 / CAT-03] `45eee19`, `7d80cf2`):** Migrados 889 maestros reales a `lebaref-pruebas.local` y creada hoja oficial Google Sheets *¿A quién cotizar?*.
