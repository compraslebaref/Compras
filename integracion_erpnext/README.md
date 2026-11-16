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
Los documentos relacionados deberán conservar la referencia al renglón de origen.

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
