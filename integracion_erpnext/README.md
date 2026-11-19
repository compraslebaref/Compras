# Compras y Almacén de Lebaref sobre ERPNext

La interfaz de los auxiliares será una aplicación nativa de Windows que se
comunica con ERPNext por HTTPS. Una instancia de ERPNext en la red privada de
la oficina mantendrá el catálogo, las existencias y las compras compartidas.
Cada auxiliar usará su propia cuenta. Para limitar el acceso a estaciones
autorizadas, el servidor deberá aceptar conexiones solo desde esas estaciones
o desde su segmento de red dedicado.

## Integración operativa de pruebas

Consulta [ENTREGA.md](ENTREGA.md) para el alcance verificado, pasos de uso,
permisos, respaldo y condiciones para instalar en la oficina. El flujo central
se realiza desde Windows: catálogos, solicitud, petición, oferta, comparativa,
orden, autorización, recepción parcial, entrega a obra, retorno y archivo XML.
También incluye herramientas, flotilla, kits, garantías, mantenimientos y
etiquetas: consulta [KITS-GARANTIAS-ETIQUETAS.md](KITS-GARANTIAS-ETIQUETAS.md).
Esta entrega no equivale a migrar todas las funciones del programa SQLite.

El primer ajuste agrega la partida de obra a cada renglón de una solicitud de
material. La obra se representa con el proyecto estándar de ERPNext por renglón.
El campo es opcional para admitir solicitudes sin partidas, como en Lebaref.
Al guardar una petición de cotización, cotización de proveedor, orden de compra o recepción creada desde un documento relacionado, el sistema recupera la obra y la partida desde el renglón de origen. Usa las acciones de creación de ERPNext para conservar los vínculos. En una recepción parcial, el saldo por recibir se sigue controlando en la orden.

Para probar este ajuste en un bench de desarrollo con ERPNext 16 instalado,
agrega esta carpeta como aplicación local:

```sh
bench get-app --soft-link /ruta/a/integracion_erpnext/app/lebaref_compras
bench --site sitio-de-pruebas install-app lebaref_compras
```

Crea una solicitud de tipo Purchase y captura dos artículos del mismo proyecto,
uno en Eléctrico y otro en HVAC. Guarda, vuelve a abrir la solicitud y comprueba
que cada renglón mantiene la partida. Repite con una solicitud sin partida.

El recorrido se verificó en esta PC con las tres cuentas de prueba. Falta
validar el despliegue real en las estaciones de la oficina y cargar los datos
maestros reales. Los movimientos de entrega se representan como transferencias
entre almacenes; no se confunden con el consumo de material en obra.

## Levantar el entorno local de pruebas

Necesitas Docker Compose con un motor de contenedores Linux en ejecución y
PowerShell. Desde esta carpeta ejecuta:

```powershell
.\pruebas\Entorno-Pruebas.ps1
```

Entra en http://127.0.0.1:8186 con el usuario `Administrator` y la contraseña
`LEBAREF_TEST_ADMIN_PASSWORD` que el iniciador genera en `pruebas/.env`.
En el asistente inicial crea una empresa ficticia, por ejemplo **Lebaref
Pruebas**, con México y MXN. El acceso por navegador es para verificar el
servidor de pruebas en esta PC. La interfaz para los auxiliares será el
cliente de Windows.

```powershell
.\pruebas\Entorno-Pruebas.ps1 Ejemplo
```

Abre en ERPNext el folio que devuelve el comando. La solicitud debe estar en
borrador y mostrar un cable en Eléctrico y un filtro en HVAC. Si ya existe el
borrador del ejemplo, el comando comprueba que conserva las partidas y
devuelve su folio. No se importan compras, inventario ni CFDI reales.

Usa las acciones `Estado` y `Detener` para consultar o detener el entorno.
Al detenerlo se conservan la base de datos y las credenciales para reanudar
la prueba.

Si Docker Engine está instalado dentro de una distribución WSL, añade
`-DistribucionWSL Ubuntu` al comando, con el nombre de la distribución que
puede ejecutar `docker compose`.

## Configuración inicial de Lebaref

Con la empresa ficticia creada y el entorno en ejecución, aplica la
configuración inicial desde la carpeta de integración:

```powershell
.\pruebas\Entorno-Pruebas.ps1 Configurar
```

El comando configura español en el sitio de pruebas y en Administrator,
crea el espacio de trabajo Lebaref y prepara los perfiles de auxiliar de
Compras y de Almacén con los roles estándar de ERPNext. En WSL añade
`-DistribucionWSL Ubuntu`. Los perfiles se pueden asignar a usuarios de
pruebas cuando se definan sus responsabilidades.

## Comparativa Lebaref

Abre una solicitud de compra enviada y selecciona **Comparativa Lebaref**.
La pantalla muestra las cotizaciones de proveedor enviadas, vigentes y
vinculadas al renglón por la cantidad solicitada. El precio unitario se muestra
en la unidad de la solicitud y en la moneda de la cotización.

Selecciona un proveedor para cada material que quieras comprar. El botón
**Generar órdenes en borrador** agrupa los materiales por proveedor y conserva
la obra y la partida. Para cada proveedor se utiliza una sola cotización,
de modo que la orden conserve sus condiciones comerciales. Los renglones con
una orden activa quedan fuera de nuevas adjudicaciones.

## Permisos de compras y recepción

La acción `Configurar` aplica los perfiles de prueba. El auxiliar de Compras
envía solicitudes y peticiones de cotización, registra ofertas y genera
órdenes en borrador. El responsable de Compras puede enviar la orden para
autorizarla. El auxiliar de Almacén puede consultar la orden y enviar una
recepción. El rol estándar de Compras conserva la consulta de recepciones.
Los permisos se aplican también al guardar documentos por API. La creación
de cuentas individuales se realiza en el sitio donde se van a usar.

## Cliente Windows de pruebas

El cliente es una aplicación nativa en español. Usa las mismas
cuentas de ERPNext y muestra las últimas 100 solicitudes de compra, órdenes
o recepciones que el usuario pueda consultar. Abre un documento para ver
sus renglones, cantidades, obra y partida.

Esta versión se conecta al servidor de pruebas de la misma PC en
`127.0.0.1:8186`. Para compilarla en Windows con .NET Framework 4.8:

```powershell
.\cliente_windows\Compilar.ps1
```

Desde una solicitud enviada, abre **Comparativa**, selecciona un renglón y
elige una oferta. Cuando termines, genera las órdenes en borrador. ERPNext
agrupa por proveedor y aplica los permisos de cada usuario. La captura
de solicitudes, la autorización y el registro de recepciones se
realizan desde el menú de operaciones del cliente Windows. El responsable
mantiene los catálogos. Almacén registra entregas y devoluciones. Compras
archiva CFDI 4.0 con UUID contra una recepción.
