# Reproducir los sondeos de auditoría

## Suites con una imagen de backend concreta

Con el entorno de pruebas activo, ejecuta desde la raíz del worktree de Codex:

```powershell
$pruebas = (Resolve-Path .\integracion_erpnext\pruebas).Path
wsl.exe -d Ubuntu --cd $pruebas --exec docker build -f Dockerfile -t lebaref/erpnext-codex:bloque1 ..
powershell -ExecutionPolicy Bypass -File .\integracion_erpnext\pruebas\Validar.ps1 -ImagenBackend lebaref/erpnext-codex:bloque1
```

`-ImagenBackend` usa esa imagen local en contenedores temporales para migrar y verificar
`lebaref-pruebas.local`. Usa la red y los volúmenes del sitio de pruebas y permite
verificar un worktree aunque el otro agente reconstruya el backend de Desk.

## Sondeos de la auditoría original

Ejecutar desde `integracion_erpnext` en esta PC, con el sitio ficticio existente
encendido y sus datos de ejemplo. No usar en producción. Los scripts comprueban
el nombre del sitio y revierten sus transacciones al terminar.

```powershell
$directorioPruebas = (Resolve-Path .\pruebas).Path
Get-Content -Raw .\auditoria\verificar_auditoria.py | wsl -d Ubuntu --cd "$directorioPruebas" --exec docker compose exec -T backend env/bin/python -
Get-Content -Raw .\auditoria\verificar_unidades.py | wsl -d Ubuntu --cd "$directorioPruebas" --exec docker compose exec -T backend env/bin/python -
```

Se necesitan los ejemplos MAT-MR-2026-00001, MAT-PRE-2026-00001,
PUR-ORD-2026-00007 y los tres usuarios ficticios existentes. No se necesitan
contraseñas en estos archivos: los sondeos se ejecutan dentro del contenedor
administrado. Las pruebas de autorización usan las identidades indicadas,
pero esto no sustituye pruebas de autenticación HTTP desde otras estaciones.

`resultados.json` y `unidades.json` son las salidas conservadas del 19/11/2026.
`BASELINE.json` identifica el commit auditado y las huellas del código original.
`HASHES-SERVIDOR.json` contiene SHA256 de ocho módulos instalados, leídos como
UTF-8 y con saltos de línea normalizados a LF; coincidieron con el código local.

La matriz en Markdown y JSON contiene las mismas 55 capacidades. P1/P2 indican
prioridad para cerrar equivalencia, no vulnerabilidades de seguridad. Las funciones
no integradas no se consideran aprobadas por el hecho de pasar las tres suites.

## ALM-02 — recepción anticipada

Construir y validar desde el worktree de Codex con las mismas instrucciones anteriores, usando la etiqueta `lebaref/erpnext-codex:alm02` en ambos comandos. `verificar_integracion.verificar` incluye `verificar_recepciones`: recibe dos unidades, consume una, vincula la recepción a una orden de tres y recibe la unidad pendiente. Compara existencias y conteos de entradas contables y de inventario antes y después de vincular. Verifica reintentos y referencias inválidas. Toda la prueba se revierte.

La regularización requiere escritura sobre recepción y orden. La prueba positiva usa Administrator y comprueba el rechazo al usuario de Almacén. El perfil actual Responsable de Compras carece de escritura sobre recepción. La habilitación del perfil operativo y la conexión en Desk siguen pendientes. Los campos se migraron usando contenedores temporales. La imagen de backend de Desk requiere integración y despliegue para servir las acciones nuevas.

## ALM-03 — recepción directa y servicios

Usar la etiqueta `lebaref/erpnext-codex:alm03` en los comandos de construcción y `Validar.ps1` anteriores. La suite de integración llama a `verificar_destinos`. Crea material y servicio por la API de catálogo, confirma una orden mixta, recibe material directamente en el almacén de obra y registra el servicio. Verifica persona, remisión, motivo, saldo y reintento. Comprueba ausencia de existencias y movimientos de inventario del servicio. Recibe el saldo material usando el formato anterior del POST para conservar compatibilidad. Todos los datos de prueba se revierten.

El campo `custom_lebaref_recibido_por` requiere migración. ERPNext puede completar un almacén de referencia del servicio durante sus validaciones nativas. El servicio mantiene `is_stock_item=0`. La conexión visual y el despliegue del backend compartido quedan pendientes para Antigravity.

## ALM-04 — sustitución autorizada

Construir y ejecutar `Validar.ps1` con la etiqueta `lebaref/erpnext-codex:alm04`. La suite integral incluye `verificar_sustituciones`. Autoriza dos unidades sustitutas de una orden de tres, recibe una, comprueba el exceso rechazado, factura con el mapeador nativo, cancela la factura ficticia, devuelve mediante el flujo nativo, recibe las dos unidades sustitutas autorizadas y la unidad original restante. Comprueba cantidades por SKU y saldo de orden. Rechaza autorización por auxiliares, recepción sin prueba de aprobación y precio alterado por la ruta nativa. Todos esos documentos se revierten.

La suite integral carga los hooks desde la imagen bajo prueba mediante `frappe.conf.developer_mode=1` en su proceso y restaura el valor al terminar. Esto evita usar la caché de hooks de un backend compartido con otra versión. No escribe esa configuración en el sitio. Para operación se debe desplegar la misma versión en backend y workers, migrar y renovar su caché antes de validar Desk.

`hooks.py` añade extensiones declarativas de Purchase Receipt y Purchase Invoice y validadores de Lebaref Sustitucion en validate y before_cancel. El único desacuerdo de código admitido en las comparaciones contra Purchase Order Item corresponde al par original/sustituto de una autorización confirmada y verificada. La prueba conserva las demás validaciones nativas. El flujo admite el mismo precio y conversión de la orden. La conexión e impresión de los datos de autorización en Desk quedan pendientes para Antigravity.
