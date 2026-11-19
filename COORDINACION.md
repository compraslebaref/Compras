# Tablero de Coordinación — Codex & Antigravity

> **Propósito:** Pizarrón compartido en el repositorio para que **Codex** y **Antigravity** sincronicen avances, contratos de API y entregas sin duplicar esfuerzos.
> **Referencia maestra de brechas:** [`integracion_erpnext/AUDITORIA-EQUIVALENCIA.md`](integracion_erpnext/AUDITORIA-EQUIVALENCIA.md) y [`integracion_erpnext/auditoria/MATRIZ-EQUIVALENCIA.md`](integracion_erpnext/auditoria/MATRIZ-EQUIVALENCIA.md).

---

## Acuerdo Arquitectónico Oficial (Confirmado por el Usuario — Cero Trabajo Doble)

1. **ÚNICA aplicación Windows para el usuario y sus auxiliares:**
   - El sistema oficial es **`SistemaComprasAlmacen.csproj` (WPF `.NET 8`)** en **`C:\Users\oscar\Proyectos\Lebaref\app`** (ejecutado mediante `C:\Users\oscar\Proyectos\Lebaref\Iniciar_Lebaref.exe`).
   - El proyecto WinForms `integracion_erpnext/cliente_windows/` (`Lebaref-Integrado.exe`) **queda congelado únicamente como arnés técnico de humo/compilación**; **ningún agente debe agregarle pantallas nuevas ni pedirle al usuario abrir binarios desde `codex\integracion_erpnext\cliente_windows\bin`**.
2. **División estricta de responsabilidades:**
   - **Codex (`C:\Users\oscar\Proyectos\Lebaref\codex`, rama `codex/erpnext`):** Construye y verifica exclusivamente el **Backend ERPNext (`lebaref_compras/*.py`)**, reglas de permisos en Frappe, suites Python `verificar_*.py` y documenta el JSON exacto en [`integracion_erpnext/CONTRATO-API.md`](integracion_erpnext/CONTRATO-API.md). *(Al iniciar turno, ejecutar `git merge --no-ff integracion/erpnext` para traer el tablero actualizado).*
   - **Antigravity (`C:\Users\oscar\Proyectos\Lebaref\app`, rama `integracion/erpnext`):** Conecta las pantallas operativas de **WPF `.NET 8`** (`Solicitudes`, `Comparativa`, `Órdenes de Compra`, `Almacén`, `Facturas XML`, `Catálogos`, `Herramientas` y `Flotilla`) directamente contra `Services/ErpNextApiClient.cs` siguiendo `CONTRATO-API.md`.
3. **Estado del Servidor ERPNext local (`http://127.0.0.1:8186`):**
   - Ya cuenta con los **889 registros maestros reales** migrados sin advertencias: **49 proveedores**, **23 obras/proyectos**, **665 productos**, **149 herramientas (`HER-xxxx`)** y **3 vehículos (`VEH-xxxx`)**.

---

## Cola Prioritaria de Trabajo (Orden de Cierre de Auditoría)

### Bloque 1 — Solicitudes, Unidades, Ofertas Parciales y Condiciones Comerciales (`H01`–`H04`)

| ID Matriz | Capacidad / Brecha | Responsable Backend (Codex) | Responsable Windows WPF (Antigravity) | Estado | Notas / Handoff |
|---|---|---|---|---|---|
| `SEG-01` / `OPS-01` | **Login por Usuario y Contraseña, Matriz de Roles y Migración de Maestros a ERPNext** | Perfiles `Role Profile` y `Custom DocPerm` listos en `configuracion_inicial.py` | `LoginWindow`, `SesionUsuarioService`, `ErpNextApiClient`, `MainWindow.PermisosRol` y `ErpNextSincronizacionService` | `COMPLETADO EN BASE` | Commits `b9b22ee` y `45eee19` en `integracion/erpnext`. 889 maestros reales ya cargados en `lebaref-pruebas.local`. |
| `CAT-03` | **Catálogo "¿A quién cotizar?" sincronizado con Google Sheets** | Asegurar que `Supplier` en `operaciones.py` acepte/devuelva teléfono, contacto y correo | `GoogleSheetsProveedoresService.cs` + hoja oficial `1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I` | `BACKEND VERIFICADO (00f2a90) · CONEXIÓN WPF PENDIENTE` | `accion="proveedor"` da de alta y consulta `telefono`, `contacto` y `correo` mediante `Supplier` y `Contact`. Consumir §5 desde WPF. |
| `COM-02` | **Ofertas parciales de proveedores** (cotizar 1 de N renglones sin exigir todos) | Ajustado `operaciones.py` (`oferta`) y `comparativa.py` + prueba en `verificar_integracion.py` | Conectar pestaña `Comparativa` de WPF a `lebaref_compras.comparativa` permitiendo renglones en `0` / "Sin cotizar" | `BACKEND VERIFICADO (96f8082) · EN CURSO WINDOWS WPF` | Integrado en `integracion/erpnext`. Documentado en `CONTRATO-API.md` §3. |
| `CAT-02` | **Unidades de compra vs. almacén y factor de conversión** visibles de punta a punta | Exponer `uom`, `stock_uom` y `conversion_factor` en esquemas/consultas de `Item`, `solicitud`, `oferta` y `recibir` | Conectar alta/edición de `Catálogo`, `Solicitudes` y `Recepción` en WPF contra esos campos | `BACKEND VERIFICADO (1371366) · WPF PENDIENTE` | API expone `uom`, `stock_uom` y `conversion_factor` en artículo, solicitud, oferta y recepción. Caso de 2 cajas y recepción de 1 caja documentado en §4. |
| `SOL-02` / `CAT-04` | **Solicitudes completas:** conceptos libres sin SKU previo, Departamento, Solicitante, Notas y Origen | Campos custom o ítem genérico convertible a SKU + vínculos de Departamento/Solicitante/Notas en `Material Request` | Conectar pestaña `Solicitudes` de WPF directo a `Material Request` en ERPNext | `BACKEND VERIFICADO (daf8a9f) · WPF PENDIENTE` | §6 define departamento vinculado, solicitante como texto, notas y conceptos libres no inventariables. Pendientes la vinculación posterior a SKU, origen y selección desde directorio. |
| `SOL-03` / `SOL-05` | **Borrador/edición, Anexos (`SOL-A1`) y Aprobar Surtir desde Stock** | Soportar estado borrador vs. enviado, anexo vinculado y ruta `Stock Entry` ligada a `Material Request` | Conectar botones de borrador, anexo y surtido desde stock en WPF | `PENDIENTE` | — |
| `COM-04` / `COM-07` | **IVA, flete, descuentos y condiciones/programación de pago en OC** | Plantilla de impuestos/cargos (IVA 16%, flete) y condición/fecha programada de pago en `Purchase Order` | Conectar pestaña `Órdenes de Compra` de WPF directo a `Purchase Order` en ERPNext | `PENDIENTE` | Caso de aceptación: neto `400` + IVA `64` = `464`. |

### Bloque 2 — Recepciones Excepcionales, Consumo vs. Transferencia y CFDI (`H05`–`H07`)
- `ALM-02`, `ALM-03`, `ALM-04`: Entrada sin OC por regularizar, recepción directa a obra / servicios no inventariables, sustitución aprobada al recibir.
- `ALM-05`, `ALM-07`, `ALM-08`: Separar entrega a obra (resguardo) vs. consumo real (`Material Issue`), reingreso de sobrante histórico y ajuste físico/merma (`Stock Reconciliation` / movimiento auditado).
- `XML-02`, `XML-03`, `XML-04`: Conciliación de conceptos e importes CFDI 4.0 contra OC/recepción y registro de compras con Nota/Ticket sin factura.

### Bloque 3 — Documentos Imprimibles, Permisos, Búsqueda y Seguimiento (`H08`–`H10`)
- `DOC-01`: Formatos Tamaño Carta / PDF con código de barras Code 128 en WPF (`BarcodePrintService.cs`) alimentados desde documentos ERPNext.
- `SEG-01`, `FLO-03`: Matriz estricta de permisos de cancelación/anulación con motivo en servidor y cliente.
- `UX-01`, `GES-01`, `GES-02`, `GES-03`, `ANA-01`, `FLO-02`: Paginación/búsqueda más allá de 100 registros, seguimiento (*Esperando respuesta* / agenda), correo entrante/saliente y reportes (Kraljic, PPV, CPK).

---

## Bitácora de Entregas Entre Agentes (Más reciente arriba)

- **2026-11-19 — Codex, SOL-02 (`daf8a9f`):** Solicitud mixta con conceptos libres, departamento, solicitante y notas. El sitio de pruebas quedó migrado. `Validar.ps1` confirmó trazabilidad y recepción sin movimiento de inventario. Conectar captura y detalle WPF según [§6](integracion_erpnext/CONTRATO-API.md#6-solicitud-con-conceptos-libres-y-datos-de-captura-sol-02).
- **2026-11-19 — Codex, CAT-03 (`00f2a90`):** Alta y consulta de teléfono, contacto y correo con `Contact` principal, permisos y reintento idempotente verificados. Consumir [§5](integracion_erpnext/CONTRATO-API.md#5-contacto-del-proveedor-cat-03).
- **2026-11-19 — Codex, CAT-02 (`1371366`):** Unidades y factor expuestos y verificados en el sitio de pruebas. Dos cajas corresponden a 20 unidades de inventario y recibir una caja ingresó 10. Consumir [§4](integracion_erpnext/CONTRATO-API.md#4-unidades-de-compra-e-inventario-cat-02).

- **2026-11-19 — Conciliación de Ramas y Regla de Interfaz Única (Antigravity + Codex):** Integrado `codex/erpnext` (`a979667` / `96f8082`) dentro de `integracion/erpnext`. Queda establecido oficialmente que **`SistemaComprasAlmacen.csproj` (WPF `.NET 8` en `app\`)** es la única aplicación de usuario final y `cliente_windows` queda congelado solo como arnés técnico.
- **2026-11-19 — Codex, COM-02 (`96f8082`):** Backend listo e integrado. La oferta guarda los renglones con precio positivo. La comparativa conserva los renglones de solicitud y omite al proveedor de `opciones` si no cotizó ese renglón. `Validar.ps1` pasó tras reconstruir el backend en `lebaref-pruebas.local`. Documentado en [`CONTRATO-API.md`](integracion_erpnext/CONTRATO-API.md#3-ofertas-parciales-com-02).
- **2026-11-19 — Antigravity ([SEG-01 / OPS-01] `b9b22ee`, `45eee19`):** Implementado Login Windows 11 (`LoginWindow`), matriz de 6 tipos de usuario (`SesionUsuarioService`, `MainWindow.PermisosRol`), cliente HTTP `.NET 8` (`ErpNextApiClient`) y sincronización real de **889 registros maestros** (49 proveedores, 23 obras, 665 productos, 149 herramientas y 3 vehículos) en `lebaref-pruebas.local` con 0 advertencias.
- **2026-11-19 — Antigravity ([CAT-03] `7d80cf2`):** Catálogo *¿A quién cotizar?* en Google Sheets (`1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I`) e integración en `GoogleSheetsProveedoresService.cs`.
- **2026-11-19 — Antigravity (`3bc6ded`):** Unificación inicial de repositorios en `C:\Users\oscar\Proyectos\Lebaref` (`app\` + worktree `codex\`).
