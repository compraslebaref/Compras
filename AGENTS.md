# AGENTS.md — Guía Oficial de Colaboración Multi-Agente (Codex + Antigravity)

## Decisión Estratégica Rectora (Confirmada por el Usuario)

El sistema oficial para integrar en la empresa es **ERPNext 16 directo** (aprovechando tanto **sus procesos transaccionales establecidos** como **su propio diseño e interfaz web oficial Frappe Desk en español**), amoldándolo por dentro mediante la app [`lebaref_compras`](integracion_erpnext/app/lebaref_compras) con las herramientas operativas propias de Lebaref.

- **NO se desarrollarán más interfaces de escritorio paralelas en C#:**
  - El proyecto WPF (`SistemaComprasAlmacen.csproj`) se conserva únicamente como **referencia viva del molde funcional de Lebaref** (formatos Carta, códigos de barras, mapa 3×3, reglas de negocio y catálogo histórico).
  - El proyecto WinForms (`integracion_erpnext/cliente_windows/`) se conserva únicamente como **arnés ligero de humo de API** en `Validar.ps1`.
- **Cómo operan el usuario y sus auxiliares:** Entrando directamente a **ERPNext (`http://127.0.0.1:8186/app/lebaref` en pruebas, o `https://<servidor-oficina>/app/lebaref` en red local)**, abriéndolo como ventana dedicada de aplicación (`Abrir_ERPNext_Lebaref.cmd` / PWA) con su usuario y contraseña individuales.

Antes de editar cualquier archivo, **ambos agentes DEBEN leer [`COORDINACION.md`](COORDINACION.md) y [`integracion_erpnext/CONTRATO-API.md`](integracion_erpnext/CONTRATO-API.md)**.

---

## 1. Topología de Carpetas y Ramas (`git worktree`)

| Carpeta | Agente propietario | Rama habitual | Regla |
|---|---|---|---|
| `C:\Users\oscar\Proyectos\Lebaref\app` | **Antigravity** (y carpeta principal del usuario) | `integracion/erpnext` | **Codex NO edita dentro de `app\`.** |
| `C:\Users\oscar\Proyectos\Lebaref\codex` | **Codex** (taller de backend y pruebas) | `codex/erpnext` | **Antigravity NO edita dentro de `codex\`.** |

**Regla de sincronización al iniciar turno:** Antes de editar archivos o actualizar `COORDINACION.md`, ejecutar `git merge --no-ff` contra la rama del otro agente (`integracion/erpnext` ↔ `codex/erpnext`) para partir siempre del estado conciliado.

---

## 2. Fronteras de Propiedad de Archivos dentro de ERPNext (`lebaref_compras`)

Para trabajar en paralelo sobre ERPNext **sin conflictos de Git**, cada agente es propietario exclusivo de sus módulos:

### Zona de Codex (Motor Transaccional, Procesos, Permisos, Impuestos, Inventario y Verificación)
- Módulos transaccionales Python en `integracion_erpnext/app/lebaref_compras/lebaref_compras/`:
  - `operaciones.py`, `comparativa.py`, `solicitudes.py`, `unidades.py`, `proveedores.py`, `equipos.py`, `equipos_setup.py`, `kits.py`, `setup.py`, `configuracion_inicial.py`, `trazabilidad.py`, `demo.py`, `verificar_*.py` y `patches.txt`.
- Infraestructura y auditoría: `integracion_erpnext/pruebas/**` y `integracion_erpnext/auditoria/**`.

### Zona de Antigravity (Experiencia Visual en ERPNext Desk, Formatos Carta, Mapa A–F, Google Sheets y Correo)
- Scripts y estilos de interfaz nativa Frappe Desk:
  - `integracion_erpnext/app/lebaref_compras/lebaref_compras/public/js/**` (botones guiados *Siguiente paso*, diálogos rápidos en Desk para Solicitudes, Peticiones, Ofertas parciales, Comparativa visual enriquecida, Recepción, Salidas/Sobrantes, Herramientas `HER`, Kits `KIT` y Flotilla `VEH`).
  - `integracion_erpnext/app/lebaref_compras/lebaref_compras/public/css/**`.
- Módulos Python exclusivos de presentación, impresión e integraciones externas en `integracion_erpnext/app/lebaref_compras/lebaref_compras/`:
  - `desk_ui.py` (Workspace ejecutivo Lebaref por rol, indicadores KPI y vistas de mapa de anaqueles A–F).
  - `impresion_carta.py` (Print Formats Tamaño Carta / PDF limpios con código de barras Code 128 para Solicitud, Comparativa dinámica, OC, Recepción, Salida, Préstamo, Check-list vehicular y Responsiva EPP).
  - `sheets_proveedores.py` (sincronización del directorio `Supplier` de ERPNext con la hoja oficial de Google Sheets *¿A quién cotizar?* `1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I`).
- Lanzadores de acceso directo para el usuario (`Abrir_ERPNext_Lebaref.cmd`).

### Archivos Compartidos (Coordinación ordenada)
- `integracion_erpnext/app/lebaref_compras/lebaref_compras/hooks.py`: ediciones mínimas y declarativas (`doctype_js`, `app_include_js`, `after_migrate`) documentadas en `COORDINACION.md`.
- `COORDINACION.md` e `integracion_erpnext/CONTRATO-API.md`.

---

## 3. Reglas Inquebrantables de Calidad y Puesta en Marcha

1. **Adoptar procesos maduros de ERPNext primero:** Usar los flujos nativos probados de ERPNext (impuestos mediante plantillas fiscales, multi-almacén *Central ↔ Obra* diferenciado de *Consumo*, permisos `Role Profile` + `Custom DocPerm`, paginación/filtros nativos de Desk) y extender únicamente lo específico de Lebaref.
2. **Operación cómoda desde ERPNext Desk:** Toda operación de `lebaref_compras` (incluidos `Lebaref Herramienta`, `Lebaref Kit`, `Lebaref Vehiculo` y `Lebaref CFDI`, que tienen candado `_writing` en `equipos.py`) debe poder operarse limpiamente desde la interfaz web de ERPNext mediante botones/diálogos en Desk que invoquen los endpoints validados `@frappe.whitelist`.
3. **Aislamiento de secretos y datos:** Nunca versionar `.env`, `Accesos-Lebaref-Pruebas.txt`, contraseñas ni respaldos `.sql.gz`/`.tgz`; nunca alterar destructivamente `%LocalAppData%\SistemaComprasAlmacen\compras_almacen.db`.
4. **Verificación reproducible:** Toda entrega debe pasar `Validar.ps1` y reconstruir/migrar el contenedor local cuando cambie código de `lebaref_compras`.
