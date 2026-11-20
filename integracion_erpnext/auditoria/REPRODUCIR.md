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

## ALM-05 — consumo y resguardo por entrega

Usar `lebaref/erpnext-codex:alm05` en los comandos de construcción y Validar.ps1. La suite integral llama a `verificar_consumos`: ingresa cincuenta piezas a costo diez, entrega dos cajas de diez a obra, consume media caja y comprueba gasto por obra de cincuenta en GL Entry. Agrega existencias de otra procedencia al almacén, devuelve una caja y rechaza sobreconsumo y devolución por encima del saldo de esa entrega. Verifica rechazo de documentos vinculados alterados por la ruta nativa y de cancelación de la entrega con movimientos activos. Anula el consumo, comprueba reversión de saldo/costo, consume la caja restante y concilia stock y saldo. Todos esos movimientos ficticios se revierten.

Se reutilizan los campos de procedencia de Stock Entry y Stock Entry Detail existentes. `hooks.py` declara validación y control previo a cancelación de movimientos vinculados. Debe desplegarse backend y workers de la misma versión y renovar caché. Los importes de consultar se omiten sin permiso nativo de lectura de GL Entry. La consulta agrega movimientos vinculados a una entrega, con valoración y cuentas nativas de ERPNext.

## ALM-07 — sobrante histórico autorizado

Usar la etiqueta `lebaref/erpnext-codex:alm07` en construcción y Validar.ps1. La suite integral incluye verificar_sobrantes. Crea diez unidades registradas en obra, captura un borrador de sobrante de tres, comprueba que el borrador conserva las existencias y que Almacén carece de autorización para confirmarlo, incluso por submit nativo. Administrator confirma el traslado, dejando siete en obra y tres en destino. Rechaza el acta duplicada con otra operación y conserva el total diez. Rechaza una entrada adicional sin confirmación de existencias previas. Confirma dos unidades verificadas como no contabilizadas, anula el movimiento y corrige el acta comprobando reversión y conservación del original. Comprueba el rechazo de cancelación por Almacén. Todos los registros ficticios se revierten.

La migración añade cinco campos de Stock Entry, incluido el índice único de empresa/acta. Los hooks de sobrantes se componen con los controles existentes de consumos. El borrador usa permiso nativo de creación de Stock Entry; confirmación y cancelación requieren también submit de Stock Reconciliation. Se libera la clave única al anular y se conserva el acta legible. La clasificación del stock histórico requiere revisión física y conciliación con lo migrado por el responsable. Desplegar backend y workers de la misma versión antes de probar la conexión e impresión en Desk.

## Cierre backend — ALM-08 y XML-02..04

Construir y validar la imagen final desde el worktree de Codex.

```powershell
$pruebas = (Resolve-Path .\integracion_erpnext\pruebas).Path
wsl.exe -d Ubuntu --cd $pruebas --exec docker build -f Dockerfile -t lebaref/erpnext-codex:cierre ..
powershell -ExecutionPolicy Bypass -File .\integracion_erpnext\pruebas\Validar.ps1 -ImagenBackend lebaref/erpnext-codex:cierre
```

`verificar_integracion` ejecuta también `verificar_ajustes` y `verificar_comprobantes`. El primero comprueba aumento por conteo, merma con valoración conservada, permisos, reversión y rechazo de captura desactualizada. El segundo concilia un CFDI con dos conceptos, incluido flete registrado como cargo fijo, detecta diferencias, conserva idempotencia con factores equivalentes, factura sin otro ingreso de stock y verifica factura con nota/ticket. Prueba autorización contable, pago parcial, otro pago que queda desactualizado, cancelación, pago total, exceso rechazado y nota de crédito nativa. Las transacciones de datos ficticios se revierten al finalizar.

Se migran los campos de Stock Reconciliation/Item, Purchase Invoice y Payment Entry y el DocType inmutable Lebaref Conciliacion CFDI. Las cuentas, asientos, activos fijos configurados y saldos del proveedor conservan los mecanismos nativos. Los permisos financieros se mantienen y se habilita lectura de CFDI para Accounts User/Manager. Desplegar backend y workers coherentes, migrar y renovar caché antes de conectar las operaciones en Desk. Los contratos exactos están en §14..16.

El cierre cubre la cola transaccional indicada, con conciliación de un CFDI y una recepción completa. La puesta en marcha requiere verificar perfiles y cuentas reales, capturas e impresión de Desk y aceptación por usuarios. El alcance fiscal de la conciliación es estructural y de correspondencia con ERPNext. La verificación de vigencia/firma SAT pertenece a su procedimiento fiscal. Las fichas personalizadas HER se mantienen en el módulo de herramientas.
