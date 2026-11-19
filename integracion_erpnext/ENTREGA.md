# Lebaref + ERPNext: entrega integrada para pruebas

Actualización: 19 de noviembre de 2026. Rama: `integracion/erpnext`.

## Resultado

Cliente nativo de Windows en español, conectado a una única base ERPNext.
El flujo central de compras y almacén está integrado y probado con datos
ficticios. Los auxiliares no necesitan abrir un navegador para ese flujo.
No se importó ni modificó la base SQLite original.

| Función | Resultado |
|---|---|
| Catálogos | Alta de artículos de inventario, proveedores con RFC, obras y almacenes por el responsable; consulta desde Windows |
| Solicitud | Captura y confirmación con artículo, cantidad, almacén, obra y partida |
| Petición de cotización | Creación desde una solicitud; varios proveedores; no envía correos |
| Ofertas | Registro desde petición por proveedor, precio unitario, vigencia y días de entrega |
| Comparativa | Selección por renglón y órdenes en borrador por proveedor; conserva obra y partida |
| Autorización | Solo responsable confirma la orden; auxiliar de compras no puede |
| Recepción | Cantidades parciales, saldo de orden y actualización de existencias |
| Entrega a obra | Transferencia entre almacenes con obra, partida y referencia de quien recibe |
| Sobrantes | Retorno vinculado a la entrega original; límites por saldo retornable |
| XML | Archivo privado de CFDI 4.0 de ingreso contra recepción; RFC empresa/proveedor y UUID, sin duplicados |
| Consulta | Solicitudes, órdenes, recepciones, peticiones, ofertas, movimientos, existencias, catálogos y CFDI |
| Exportación | CSV de los registros mostrados; neutraliza fórmulas en celdas de texto |
| Conexión interrumpida | Identificador por operación y reintento de la misma captura sin duplicar movimientos |
| Herramientas | Fichas individuales, préstamos por responsable/obra, retorno parcial, incidencias y recuperación por responsable |
| Flotilla | Vehículos, gastos de combustible/mantenimiento, revisiones, estado, seguro e historial |
| Kits | Composición con historial, préstamo selectivo y disponibilidad por herramienta |
| Garantías y mantenimiento | Costo de adquisición, vigencia, trabajos gratuitos/pagados, acumulados y anulación con motivo |
| Etiquetas | Code 128, vista previa, PNG, impresión y búsqueda por código exacto |

El uso de estos nuevos módulos y los ejemplos disponibles están en
[HERRAMIENTAS-FLOTILLA.md](HERRAMIENTAS-FLOTILLA.md).

## Cómo abrirlo en esta PC

Abre `Iniciar-Lebaref.exe` junto a `Lebaref-Integrado.exe`. El iniciador comprueba
ERPNext y, si hace falta, ejecuta la tarea local `Lebaref-Pruebas-WSL`. Espera a
que aparezca la ventana de acceso. No cierres Docker/WSL mientras se trabaja.
La aplicación usa las mismas cuentas del archivo local
`Accesos-Lebaref-Pruebas.txt`; ese archivo no va incluido en el paquete para compartir.

Si el servicio ya está iniciado, puedes abrir directamente `Lebaref-Integrado.exe`.
La tarea de arranque está instalada en esta PC; copiar el iniciador a otra PC
no instala un servidor ERPNext.

## Recorrido de uso

1. **Responsable:** dar de alta los catálogos que hagan falta desde el selector
   de operaciones. Los nombres de almacén deben corresponder a su empresa.
2. **Compras:** seleccionar «Nueva solicitud de material», llenar renglones y
   guardar. Esto confirma la solicitud, no la deja en borrador.
3. En «Solicitudes», seleccionar el folio y «Pedir cotización de la solicitud».
   Escribir un proveedor exacto por línea; sus códigos se consultan en Proveedores.
4. En «Peticiones», seleccionar el folio y «Registrar oferta de la petición».
   Registrar cada oferta. La versión nativa usa la moneda de la empresa y
   cotiza todos los renglones de la petición. No envía mensajes al proveedor.
5. Volver a la solicitud, abrir «Comparativa», elegir ofertas y generar órdenes.
6. **Responsable:** en «Órdenes», abrir «Autorizar orden seleccionada», revisar
   proveedor, renglones y total; seleccionar AUTORIZAR y confirmar.
7. **Almacén:** en «Órdenes», abrir «Recibir orden seleccionada». Escribir solo
   las cantidades físicamente recibidas; cero omite un renglón. El almacén se
   conserva de la orden. No se admiten cantidades mayores al saldo.
8. «Entregar materiales a obra» transfiere existencia del almacén central al de
   obra. Registrar la referencia de quien recibe. No es una salida por consumo.
9. Para sobrantes, seleccionar la entrega en «Movimientos de almacén» y
   «Devolver sobrante de entrega». Se invierte el recorrido del almacén original.
10. **Compras:** seleccionar la recepción y «Archivar CFDI de recepción».
    Elegir XML de hasta 2 MB. Los RFC de empresa y proveedor deben estar
    configurados y coincidir. El UUID no puede asociarse de nuevo a otra recepción.

Si hay un timeout, usar **Reintentar mismo envío** en esa misma ventana; conserva
la captura y su identificador. No abrir otra captura hasta resolver el intento.
Si se cerró la aplicación durante una operación, consultar primero los documentos
recientes: el identificador de la captura no persiste después de cerrar la ventana.

## Ejemplo que quedó registrado

La prueba con el cliente .NET y cuentas separadas creó estos documentos ficticios:

| Documento | Folio |
|---|---|
| Solicitud | MAT-MR-2026-00006 |
| Orden autorizada | PUR-ORD-2026-00007 |
| Recepción de 2 de 4 unidades | MAT-PRE-2026-00001 |
| Entrega de una unidad | MAT-STE-2026-00001 |
| Retorno de esa unidad | MAT-STE-2026-00002 |

El saldo de la orden es de 2 unidades. El almacén creado para esta entrega queda
en cero después del retorno. También existe la comparativa de práctica
MAT-MR-2026-00005, que puede haber sido modificada por el usuario.

## Alcance y límites concretos

- Es una entrega integrada **para pruebas en esta PC**, no una instalación de
  producción certificada ni una migración total de la aplicación anterior.
- La interfaz nativa consulta hasta 100 registros recientes por módulo; los
  selectores cargan hasta 1,000 referencias. Para históricos mayores, filtros
  avanzados, correcciones/anulaciones, impresión formal y condiciones comerciales
  complejas se conserva ERPNext como herramienta administrativa.
- El XML se archiva; no valida sello/estado SAT, no concilia conceptos o importes,
  no genera factura contable ni contabiliza impuestos. No se inventa IVA.
  Actualmente cada UUID se vincula a una recepción, no a varias.
- Herramientas y flotilla ya tienen control operativo en esta interfaz. Faltan
  integración contable de activos,
  correo entrante, automatizaciones de correo, cuadros de análisis avanzados,
  fotos y firmas. El histórico original sigue separado.
- No se migraron proveedores, saldos, compras, CFDI ni documentos reales.
- No se ha probado concurrencia desde dos estaciones físicas ni cortes reales
  de energía. Sí se probaron dos sesiones HTTP simultáneas para préstamos,
  incluyendo seis rondas de competencia por la misma herramienta.
- ERPNext mantiene sus validaciones de inventario. Los artículos con series,
  lotes o inspección obligatoria necesitan su flujo especializado en ERPNext.

## Información compartida en la oficina

Las estaciones deben conectarse al **mismo servidor**; no se copia una base por
auxiliar. Cada persona necesita una cuenta individual y su perfil. El servidor
puede ser una PC dedicada en la oficina, siempre encendida, con respaldo externo.

El cliente admite `lebaref-servidor.txt` junto al ejecutable con una URL raíz
HTTPS, por ejemplo `https://erp.lebaref.interno/`. No contiene contraseñas.
HTTP solo se admite para loopback en pruebas. No se desactiva la validación TLS.

Para limitar acceso a la oficina hacen falta dirección privada, certificado TLS
confiable y reglas de red que permitan solo las estaciones autorizadas. El uso de
una aplicación nativa por sí mismo no impone esa restricción. Esta entrega no
publica puertos externos ni cambia el firewall. El servidor actual escucha
únicamente en `127.0.0.1:8186`.

Antes de uso real faltan la elección del servidor/estaciones, cuentas nominales,
empresa/RFC/configuración contable y datos maestros/inventario conciliados. Esos
datos no se pueden deducir ni sustituir con los ejemplos ficticios.

## Respaldo y recuperación

Desde la carpeta de integración:

```powershell
.\pruebas\Respaldar.ps1 -DistribucionWSL Ubuntu -Destino C:\Respaldos\Lebaref\fecha-nueva
```

Incluye base, archivos públicos/privados, configuración del sitio, variables del
entorno y un manifiesto SHA256. Contiene información privada y secretos: guardarlo
con acceso restringido y fuera del equipo del servidor. No incluirlo en el ZIP
que se entrega a los auxiliares. No se configuró una tarea automática de respaldo.

La restauración se debe hacer primero en otro sitio/instancia con la misma versión
ERPNext/Frappe y la app Lebaref. Usar `bench --site sitio-destino restore` con el SQL
y `--with-public-files`/`--with-private-files`, conservar la clave de cifrado del
respaldo en el sitio destino y ejecutar `migrate`. No sobrescribir el sitio activo
para comprobar un respaldo. El detalle del ensayo local está en RESULTADO-PRUEBAS.md.

## Verificación reproducible

La prueba del servidor solo admite `lebaref-pruebas.local` y siempre revierte su
transacción. Requiere el ejemplo MAT-MR-2026-00001 y las tres cuentas de prueba:

```powershell
wsl -d Ubuntu --cd .\pruebas --exec docker compose exec -T backend bench --site lebaref-pruebas.local execute lebaref_compras.verificar_integracion.verificar
```

La prueba de cliente .NET está en `pruebas/VerificarIntegracionWindows.cs`.
Recibe un JSON local con cuentas ficticias. Sí crea registros de prueba y debe
ejecutarse exclusivamente contra el entorno local.

## Ampliación del 19 de noviembre de 2026

Ya están integrados kits con préstamo selectivo, historial de composición,
costo de adquisición y garantías, mantenimientos de herramientas y anulación
con motivo. Los acumulados excluyen gastos anulados y permiten recapturar el
comprobante corregido sin duplicar el costo.

Herramientas, vehículos y kits incluyen búsqueda por código exacto y etiquetas
Code 128: vista previa, guardar PNG e impresión desde Windows. Consulta
[KITS-GARANTIAS-ETIQUETAS.md](KITS-GARANTIAS-ETIQUETAS.md) para utilizarlos.
