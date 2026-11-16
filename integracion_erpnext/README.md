# Compras y Almacén de Lebaref sobre ERPNext

La interfaz de los auxiliares será una aplicación nativa de Windows que se
comunica con ERPNext por HTTPS. Una instancia de ERPNext en la red privada de
la oficina mantendrá el catálogo, las existencias y las compras compartidas.
Cada auxiliar usará su propia cuenta. Para limitar el acceso a estaciones
autorizadas, el servidor deberá aceptar conexiones solo desde esas estaciones
o desde su segmento de red dedicado.

## Primera entrega

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

La versión operativa estará lista cuando una estación autorizada pueda completar
la solicitud, la comparativa y las órdenes por proveedor. Desde una segunda
estación autorizada se deberá poder registrar una recepción parcial, la entrega
a obra, el regreso de sobrante y el CFDI sin duplicarlo. Los movimientos se
deberán consultar desde el folio de origen y cuadrar con el inventario.

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
