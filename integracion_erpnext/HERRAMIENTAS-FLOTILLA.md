# Herramientas y flotilla en Lebaref Windows

Actualización del 17 de noviembre de 2026. Usa `Iniciar-Lebaref.exe` y las mismas
cuentas del entorno local. No se importaron datos del programa anterior.

## Responsabilidades

| Puesto | Operaciones |
|---|---|
| Responsable de Compras | Alta de herramientas y vehículos; cambio de estado, ubicación, responsable y vigencia de seguro; también puede registrar movimientos |
| Auxiliar de Almacén | Préstamos, retornos, gastos de vehículos y revisiones |
| Auxiliar de Compras | Consulta de los registros e historiales |

Compras y Almacén pueden consultar las obras para asignar los movimientos.
Las validaciones y permisos se aplican en el servidor, no solo en los botones.
No se permite editar ni borrar directamente estos documentos para saltarse
los movimientos. El historial identifica al usuario que registró cada evento.

## Herramientas

1. El responsable selecciona **Nueva herramienta individual**. Captura código
   único, empresa, artículo del catálogo, serie opcional, marca/modelo y gabinete.
   Cada ficha representa una herramienta física; no crea existencias ni una compra.
2. Almacén selecciona **Nuevo préstamo de herramientas** y captura obra,
   responsable, persona que recibe, departamento opcional y fecha compromiso.
   Agrega las herramientas disponibles en los renglones. La salida queda fechada hoy.
3. En **Préstamos de herramientas**, selecciona un folio y abre
   **Retornar herramientas del préstamo**. Deja Pendiente lo que aún no regresa.
   Las opciones son Bueno, Dañada, Incompleta y Extraviada. Las incidencias
   requieren una observación.
4. La devolución parcial deja abiertas las herramientas restantes. Un retorno
   Bueno libera la herramienta; una incidencia la mantiene fuera de disponibles.
5. El responsable usa **Cambiar estado de herramienta**, con motivo y ubicación,
   para registrar reparación, recuperación o baja. Si está prestada, primero
   debe registrarse el retorno o extravío desde el préstamo.
6. **Ver detalle** en una herramienta muestra su custodia y los préstamos
   recientes. En un préstamo muestra sus renglones y si la fecha compromiso venció.
   **Historial de herramientas** conserva los cambios autorizados de estado.

Una herramienta no puede aparecer dos veces en el mismo préstamo ni asignarse
a otro mientras esté prestada. Las operaciones se coordinan en una transacción;
dos sesiones que intenten tomar la misma herramienta no generan dos asignaciones.
El registro de custodia no descuenta inventario ni calcula depreciación de activos.
El nombre de quien recibe no equivale a una firma electrónica.

## Flotilla

1. El responsable selecciona **Nuevo vehículo**: código, placas, empresa,
   marca/modelo, responsable habitual, odómetro inicial, fecha de lectura,
   intervalo y próxima meta de servicio, capacidad del tanque, combustible y seguro.
   Las placas se normalizan sin espacios/guiones y no pueden repetirse.
2. En **Flotilla**, selecciona el vehículo y **Registrar gasto del vehículo**.
   Captura fecha, kilometraje, importe, proveedor/taller, folio de comprobante,
   descripción y, si procede, obra. Para combustible registra litros y tanque lleno.
   Los importes se expresan en la moneda de la empresa.
3. Los registros deben capturarse en orden: fecha no futura ni anterior a la
   última lectura, y kilometraje no decreciente. Un comprobante no puede
   duplicarse para el mismo vehículo y proveedor. Los litros no pueden superar
   la capacidad registrada. No se asignan litros a otros tipos de gasto.
4. Un gasto de **Mantenimiento preventivo** registra el último servicio y
   calcula la nueva meta: kilometraje del servicio + intervalo configurado.
5. **Revisar vehículo** registra inspector, odómetro, combustible, llantas,
   fluidos, frenos/luces y carrocería/limpieza. Atención o Falla requieren notas.
   La revisión no autoriza automáticamente que el vehículo circule.
6. El responsable usa **Estado / seguro del vehículo** para dejarlo Activo,
   En taller o Fuera de servicio, actualizar al responsable o el seguro y guardar
   el motivo. El historial queda en **Historial de vehículos**.
7. **Ver detalle** muestra los gastos y revisiones recientes, el gasto total,
   servicio vencido/próximo (hasta 800 km) y seguro vencido/próximo (hasta 30 días).
   Los avisos aparecen al consultar; no se envían correos ni notificaciones externas.

Los gastos son una bitácora operativa. No crean facturas, pagos ni pólizas contables,
no validan XML y no concilian automáticamente contra compras. No hay todavía una
operación nativa de rectificación/anulación de gastos: revisar antes de confirmar.

## Ejemplos ficticios disponibles

Se conservaron ejemplos identificados como DEMO de las verificaciones por HTTP.
Puedes revisar, entre otros:

- Herramientas `DEMO-HER-1117175813-1`, `DEMO-HER-1117175813-2` y `DEMO-HER-1117175813-3`.
- Préstamo `96or3q2jau`: retornado con incidencia y reparación posterior documentada.
- Préstamo `96trv7p0g3`: una herramienta pendiente de retornar.
- Vehículo `DEMO-VEH-1117175813`: gastos por 1,240 MXN, revisión y estado En taller.
- Meta del próximo servicio del vehículo: 11,200 km; odómetro: 1,200 km.

No corresponden a herramientas, personal, gastos ni vehículos reales de Lebaref.
Pueden existir otros registros DEMO de las pruebas anteriores.

## Validación y límites

Pasaron las pruebas transaccionales del servidor y las pruebas HTTP con las tres
cuentas. Se comprobó préstamo exclusivo, reintento sin duplicado, retorno parcial,
incidencias, permisos, placas únicas, comprobantes repetidos, kilometraje y litros,
servicio preventivo e historial. Se hicieron seis rondas de dos usuarios compitiendo
por una herramienta y una prueba simultánea del mismo identificador de operación.
También volvió a pasar la prueba de compras/recepción/transferencias/CFDI.

Se revisó visualmente el formulario nativo de alta de vehículo. No se ha validado
uso desde dos computadoras físicas, carga elevada ni migración del histórico.
Los listados muestran hasta 100 registros recientes y los selectores hasta 1,000
referencias. El detalle muestra hasta 100 eventos recientes por tipo; el total
de gastos del vehículo incluye todos sus gastos visibles para el usuario.
Kits, costos/garantías, mantenimiento de herramientas y etiquetas ya están
integrados; consulta KITS-GARANTIAS-ETIQUETAS.md. Siguen pendientes fotos,
depreciación y firmas. La instalación sigue siendo local de pruebas.
La búsqueda por código exacto permite localizar herramientas, vehículos y kits
aunque no aparezcan entre los 100 registros recientes.
