# AGENTS.md — Guía Oficial de Colaboración Multi-Agente (Codex + Antigravity)

Este repositorio unifica el sistema de **Compras, Almacén y Flotilla de Lebaref** (cliente nativo Windows + integración con servidor ERPNext 16.55 / Frappe 16.56).

En este proyecto colaboran **dos agentes en paralelo**:
- **Codex** (especialista en Backend ERPNext/Frappe, reglas transaccionales, seguridad/permisos, migración de datos y suites de verificación).
- **Antigravity** (especialista en Cliente Windows UX/UI, vistas operativas, formatos imprimibles Carta/PDF con códigos de barras, conciliación visual CFDI y automatización de correo).

Antes de editar cualquier archivo, **ambos agentes DEBEN leer [`COORDINACION.md`](COORDINACION.md) y [`integracion_erpnext/CONTRATO-API.md`](integracion_erpnext/CONTRATO-API.md)**.

---

## 1. Topología de Carpetas y Ramas (`git worktree`)

La carpeta maestra vive en `C:\Users\oscar\Proyectos\Lebaref\` dividida en dos *worktrees* conectados al **mismo repositorio Git**:

| Carpeta | Agente propietario | Rama habitual | Regla |
|---|---|---|---|
| `C:\Users\oscar\Proyectos\Lebaref\app` | **Antigravity** y **ÚNICA carpeta que abre/usa el usuario** | `integracion/erpnext` (o `agy/<id-tarea>`) | **Codex NO edita ni compila dentro de `app\`.** |
| `C:\Users\oscar\Proyectos\Lebaref\codex` | **Codex** (taller interno exclusivo de backend) | `codex/erpnext` (o `codex/<id-tarea>`) | **Antigravity NO edita ni compila dentro de `codex\`. Nunca pedir al usuario abrir ejecutables desde `codex\`.** |

Al compartir el mismo `.git`, todo `git commit` hecho en `codex\` es inmediatamente visible desde `app\` (`git log codex/erpnext`, `git diff`, `git merge --no-ff`) y viceversa, sin bloquear binarios `.exe`/`.dll` ni ensuciar el árbol de trabajo del otro agente.

### Regla de Oro sobre el Cliente Windows Oficial (Cero Trabajo Doble)
- **ÚNICA aplicación oficial para el usuario final y sus auxiliares:** **`SistemaComprasAlmacen.csproj`** (aplicación completa Windows 11 WPF `.NET 8` en la raíz de `C:\Users\oscar\Proyectos\Lebaref\app`, iniciada con `Iniciar_Lebaref.exe`).
- **Estado de `integracion_erpnext/cliente_windows/` (`Lebaref-Integrado.exe`):** Queda **congelado únicamente como arnés ligero de verificación técnica de API**. **NO** se desarrollan nuevas pantallas para usuario final en WinForms (`cliente_windows`) ni se le indica al usuario usar `Lebaref-Integrado.exe` como sistema operativo diario. Todo desarrollo de interfaz de usuario ocurre exclusivamente en el proyecto WPF `.NET 8` consumiendo `Services/ErpNextApiClient.cs`.
- **Sincronización al iniciar turno:** Antes de editar `COORDINACION.md` o iniciar una nueva tarea, **Codex debe ejecutar `git merge --no-ff integracion/erpnext`** en `codex\`, y **Antigravity debe ejecutar `git merge --no-ff codex/erpnext`** en `app\`, para que ambos partan siempre del mismo estado conciliado.

---

## 2. Fronteras de Propiedad de Archivos (Cero Conflictos)

Salvo acuerdo explícito anotado en `COORDINACION.md`, cada agente edita únicamente su zona:

### Zona de Codex (Backend, Infraestructura, Auditoría y Pruebas de Servidor)
- `integracion_erpnext/app/lebaref_compras/**` (`operaciones.py`, `comparativa.py`, `equipos.py`, `kits.py`, `setup.py`, `configuracion_inicial.py`, `trazabilidad.py`, `verificar_*.py`, hooks y parches).
- `integracion_erpnext/pruebas/**` (Docker/WSL, respaldos, scripts de migración SQLite → ERPNext y validadores de API).
- `integracion_erpnext/auditoria/**` (`MATRIZ-EQUIVALENCIA.md`, `MATRIZ.json`, scripts de verificación de equivalencia).

### Zona de Antigravity (Cliente Windows, UX, Impresión Carta/PDF, CFDI Visual y Correo)
- Proyecto WPF `.NET 8` en raíz (`MainWindow*.cs`, `MainWindow.xaml`, `NuevaSolicitudCaptura.*`, `Services/**`, `Models/**`, `Data/**`).
- Cliente Windows conectado a ERPNext (`integracion_erpnext/cliente_windows/**` y nuevos módulos C# modulares de cliente ERPNext).

### Archivos Compartidos de Coordinación (Lectura/Escritura por ambos en turnos limpios)
- `COORDINACION.md`: tablero activo de tareas, asignaciones, bloqueos y entregas listas para integrar.
- `integracion_erpnext/CONTRATO-API.md`: fuente de verdad de endpoints HTTP/JSON entre Python (`@frappe.whitelist`) y C# (`ErpClient`).

---

## 3. Flujo de Trabajo entre Codex y Antigravity

1. **Tomar tarea:** Revisar `COORDINACION.md` y elegir un ítem de la matriz ([`integracion_erpnext/auditoria/MATRIZ-EQUIVALENCIA.md`](integracion_erpnext/auditoria/MATRIZ-EQUIVALENCIA.md), p. ej. `COM-02`, `SOL-02`, `DOC-01`). Marcarlo `EN CURSO (Codex)` o `EN CURSO (Antigravity)`.
2. **Acordar el contrato antes o al entregar el backend:** Si una capacidad requiere cambios en servidor y cliente:
   - Codex implementa/ajusta el método en `lebaref_compras`, lo prueba en `lebaref-pruebas.local` y documenta el JSON exacto de entrada/salida en `integracion_erpnext/CONTRATO-API.md`.
   - Antigravity consume exactamente ese contrato en C# sin adivinar nombres de campos.
3. **Commits atómicos y trazables:**
   - Formato de mensaje: `[ID-MATRIZ] Descripción breve en español` (ejemplo: `[COM-02] Permitir ofertas parciales por renglon en Supplier Quotation`).
4. **Verificación obligatoria antes de declarar terminado:**
   - Ejecutar `powershell -ExecutionPolicy Bypass -File .\integracion_erpnext\pruebas\Validar.ps1` (o las suites correspondientes).
   - Prohibido declarar cerrada una brecha de `MATRIZ-EQUIVALENCIA.md` sin evidencia reproducible.
5. **Integración (`merge`):**
   - Cuando Codex deja commits verificados en `codex/erpnext`, lo anota en `COORDINACION.md` con el hash del commit. Antigravity (o Codex cuando integra cambios de UI en su rama) sincroniza con `git merge --no-ff` manteniendo el historial limpio.

---

## 4. Reglas Inquebrantables de Seguridad y Calidad

- **Aislamiento de datos:** Jamás modificar ni sobrescribir la base real del usuario (`%LocalAppData%\SistemaComprasAlmacen\compras_almacen.db`) durante pruebas o migraciones; trabajar siempre sobre copias aisladas y contra el sitio ficticio `lebaref-pruebas.local` (`http://127.0.0.1:8186`).
- **Cero envíos accidentales de correo en pruebas automatizadas:** En suites automáticas mantener `send_email=0` o destinatarios de prueba controlados; nunca disparar correos reales a proveedores o gerentes desde scripts de verificación.
- **Idempotencia y permisos reales:** Toda escritura hacia ERPNext debe conservar control de idempotencia (`operacion` UUID + huella SHA-256) y validar permisos reales en servidor (`frappe.has_permission` / `check_permission`), nunca depender solo de ocultar botones en Windows.
- **Archivos secretos fuera de Git:** Nunca versionar `.env`, `Accesos-Lebaref-Pruebas.txt`, contraseñas de aplicación Gmail ni respaldos `.sql.gz`/`.tgz`.
- **Modularidad en C# y Python:** Evitar seguir inflando archivos monolíticos; las piezas nuevas de integración Windows ↔ ERPNext deben crearse en clases pequeñas y enfocadas por dominio.
