# Tablero de Coordinación — Codex & Antigravity

> **Propósito:** Pizarrón compartido en el repositorio para que **Codex** y **Antigravity** sincronicen avances, contratos de API y entregas sin que el usuario tenga que copiar detalles técnicos entre chats.
> **Referencia maestra de brechas:** [`integracion_erpnext/AUDITORIA-EQUIVALENCIA.md`](integracion_erpnext/AUDITORIA-EQUIVALENCIA.md) y [`integracion_erpnext/auditoria/MATRIZ-EQUIVALENCIA.md`](integracion_erpnext/auditoria/MATRIZ-EQUIVALENCIA.md).

---

## Estado Actual del Repositorio Unificado

- **Ubicación maestra:** `C:\Users\oscar\Proyectos\Lebaref`
  - Worktree **Antigravity / Principal**: `C:\Users\oscar\Proyectos\Lebaref\app` (rama `integracion/erpnext`)
  - Worktree **Codex**: `C:\Users\oscar\Proyectos\Lebaref\codex` (rama `codex/erpnext`)
- **Estado base integrado (19-nov-2026):**
  - Incluye el código original WPF actualizado hasta las mejoras operativas recientes (vistas divididas Lista+Ficha en Solicitudes y OC, guía *Siguiente paso*, pausa *Esperando respuesta*, cierre manual, pestaña *Entrada de material* en Almacén, Dashboard unificado sin pestañas, correo IMAP/SMTP y envío de comparativa en HTML+PDF).
  - Incluye íntegra la integración ERPNext (`integracion_erpnext/`) y la auditoría de 55 capacidades (`afc2b49`).

---

## Decisión de Interfaz Pendiente de Confirmar o Afinar

- **Objetivo funcional:** Que el usuario final conserve la riqueza operativa y visual del sistema Lebaref (vistas Lista+Ficha, guía *Siguiente paso*, impresiones Tamaño Carta/PDF con código de barras, lector/conciliador CFDI, correo, etc.) respaldada por la base multiusuario, permisos e inventario transaccional de **ERPNext**.
- Mientras se cierran las brechas de servidor (`H01`–`H08`, `H11`), los contratos en [`integracion_erpnext/CONTRATO-API.md`](integracion_erpnext/CONTRATO-API.md) son agnósticos de si los consume `cliente_windows` o la interfaz WPF `.NET 8` mediante un cliente HTTP compartido.

---

## Cola Prioritaria de Trabajo (Orden de Cierre de Auditoría)

### Bloque 1 — Solicitudes, Unidades, Ofertas Parciales y Condiciones Comerciales (`H01`–`H04`)

| ID Matriz | Capacidad / Brecha | Responsable Backend (Codex) | Responsable Windows (Antigravity) | Estado | Notas / Handoff |
|---|---|---|---|---|---|
| `COM-02` | **Ofertas parciales de proveedores** (cotizar 1 de N renglones sin exigir todos) | Ajustar `operaciones.py` (`oferta`) y `comparativa.py` + prueba en `verificar_integracion.py` | Permitir captura de precio en `0` / omitido por renglón y mostrar "Sin cotizar" en comparativa e impresión | `PENDIENTE` | Hoy `operaciones.py` L196 lanza `"Cotiza todos los renglones de la petición una sola vez"`. |
| `CAT-02` | **Unidades de compra vs. almacén y factor de conversión** visibles de punta a punta | Exponer `uom`, `stock_uom` y `conversion_factor` en esquemas/consultas de `Item`, `solicitud`, `oferta` y `recibir` | Mostrar y permitir elegir unidad de compra + factor en alta/edición y renglones de solicitud/recepción | `PENDIENTE` | Servidor ya calcula `2 × 10 = 20`, falta exponerlo en API y UI. |
| `SOL-02` / `CAT-04` | **Solicitudes completas:** conceptos libres sin SKU previo, Departamento, Solicitante, Notas y Origen | Campos custom o manejo de ítem genérico/temporal convertible a SKU + vínculos de Departamento/Solicitante en `Material Request` | Captura completa con selector de Departamento/Solicitante, conceptos abiertos y vínculo posterior a SKU | `PENDIENTE` | Crítico para no obligar a catalogar antes de pedir cotización. |
| `SOL-03` / `SOL-05` | **Borrador/edición, Anexos (`SOL-A1`) y Aprobar Surtir desde Stock** | Soportar estado borrador vs. enviado, anexo vinculado a solicitud origen y ruta `Material Issue`/`Transfer` desde stock ligada a `Material Request` | Botones de guardar borrador, crear anexo y aprobar surtido desde stock con guía *Siguiente paso* | `PENDIENTE` | — |
| `COM-04` / `COM-07` | **IVA, flete, descuentos y condiciones/programación de pago en OC** | Plantilla de impuestos/cargos (IVA 16%, flete) y campos/flujo de condición y fecha programada de pago (`Payment Terms` / `Payment Entry`) | Captura clara de P.U. s/IVA y c/IVA, flete, condición de pago y desglose exacto de totales | `PENDIENTE` | Caso de aceptación: neto `400` + IVA `64` = `464`. |

### Bloque 2 — Recepciones Excepcionales, Consumo vs. Transferencia y CFDI (`H05`–`H07`)
- `ALM-02`, `ALM-03`, `ALM-04`: Entrada sin OC por regularizar, recepción directa a obra / servicios no inventariables, sustitución aprobada al recibir.
- `ALM-05`, `ALM-07`, `ALM-08`: Separar entrega a obra (resguardo) vs. consumo real (`Material Issue`), reingreso de sobrante histórico y ajuste físico/merma (`Stock Reconciliation` / movimiento auditado).
- `XML-02`, `XML-03`, `XML-04`: Conciliación de conceptos e importes CFDI 4.0 contra OC/recepción y registro de compras con Nota/Ticket sin factura.

### Bloque 3 — Documentos Imprimibles, Permisos, Búsqueda y Seguimiento (`H08`–`H10`)
- `DOC-01`: Formatos Tamaño Carta / PDF con código de barras Code 128 (Solicitud, Comparativa dinámica, OC, Entrada, Salida, Préstamo, Check-list vehicular, Responsiva EPP) — *Antigravity puede reutilizar `BarcodePrintService.cs`*.
- `SEG-01`, `FLO-03`: Matriz estricta de permisos de cancelación/anulación con motivo en servidor y cliente (incluyendo gastos/revisiones de flotilla).
- `UX-01`, `GES-01`, `GES-02`, `GES-03`, `ANA-01`, `FLO-02`: Paginación/búsqueda más allá de 100 registros, seguimiento (*Esperando respuesta* / agenda), correo entrante/saliente y reportes (Kraljic, PPV, CPK).

---

## Bitácora de Entregas Entre Agentes (Más reciente arriba)

- **2026-11-19 — Antigravity ([CAT-03] Google Sheets ¿A quién cotizar?):** Creada y poblada la hoja maestra en Google Drive (1SDPwkexUf2IvIAGniwjtfqH2m8dv2KshrSMsJ9_EiM4 con 3 pestañas: A_Quien_Cotizar, Directorio_Proveedores y Resumen_Por_Categoria) e integrado el servicio modular Services/GoogleSheetsProveedoresService.cs + MainWindow.ProveedoresSheets.cs. *Nota para Codex en CAT-03:* asegurar que Supplier en lebaref_compras conserve phone, contact y email para mantener paridad con esta sincronización.
- **2026-11-19 — Antigravity:** Unificación de repositorios en `C:\Users\oscar\Proyectos\Lebaref` (`app\` + worktree `codex\`), creación de `AGENTS.md`, `COORDINACION.md`, `integracion_erpnext/CONTRATO-API.md` y `integracion_erpnext/pruebas/Validar.ps1` sin alterar la lógica de negocio ni datos del entorno Docker `lebaref-pruebas`. Listo para iniciar Bloque 1 en conjunto con Codex.
