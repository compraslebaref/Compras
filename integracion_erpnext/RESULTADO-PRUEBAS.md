# Resultado de verificación de la integración

16 de noviembre de 2026. Entorno aislado `lebaref-pruebas.local`, ERPNext/Frappe
16.55.0, Windows/.NET Framework y Docker en Ubuntu WSL.

## Pruebas aprobadas

1. Compilación del cliente nativo y del iniciador de Windows.
2. Construcción de la imagen Docker con la app Lebaref.
3. Recorrido por HTTP desde el mismo `ErpClient.cs` de la aplicación, con sesiones
   independientes de Compras, Responsable y Almacén: proveedor, solicitud,
   petición, oferta, comparativa, orden, autorización, recepción parcial,
   alta de almacén, entrega y retorno. Se conservan los folios de ENTREGA.md.
4. Reintento de solicitud y recepción con el mismo identificador: devuelve el
   folio existente sin crear otro documento.
5. Autorización rechazada para el auxiliar de compras y permitida al responsable.
6. Altas de artículo y obra por responsable, con unidades disponibles. Alta de
   proveedores rechazada al auxiliar; Almacén sin permiso para archivar CFDI.
7. Recepción de 2 de 4 unidades, orden parcialmente recibida, obra y partida
   conservadas. Cantidad superior al saldo rechazada.
8. Entrega/retorno de una unidad; almacén de obra vuelve a cero. Segunda
   devolución superior al saldo rechazada.
9. XML ficticio con RFC coincidentes y UUID: archivo privado, repetición sin
   duplicado. Alta directa del registro sin pasar por validación XML rechazada.
   Los registros de esta prueba se revierten; no es validación fiscal SAT.
10. Revisión visual de la pantalla nativa de solicitud: campos, renglones y
    acciones visibles en español. No se ensayó manualmente cada pantalla.
11. Inicio en frío: servidor detenido, ejecución de `Iniciar-Lebaref.exe`,
    respuesta `pong` recuperada y proceso `Lebaref-Integrado` abierto.
12. Respaldo de SQL, archivos públicos/privados y configuración; manifiesto SHA256.
13. Restauración en `lebaref-recuperacion.local`, separado del sitio en uso,
    incluyendo archivos y clave de cifrado; migración ejecutada. Conteos
    coincidentes y recepción confirmada recuperada.

| Documento | Sitio origen | Sitio recuperado |
|---|---:|---:|
| Solicitudes | 6 | 6 |
| Órdenes | 7 | 7 |
| Recepciones | 1 | 1 |
| Movimientos de almacén | 2 | 2 |
| Proveedores | 10 | 10 |
| Usuarios | 5 | 5 |
| CFDI archivados | 0 | 0 |

La prueba transaccional del servidor (`verificar_integracion.verificar`) terminó
con `correcto: true`. La prueba HTTP del cliente terminó con código 0.
El ensayo de recuperación terminó con `recuperacion: correcta`.

## Límites de lo comprobado

### Actualización del 17 de noviembre de 2026

- `verificar_equipos.verificar`: correcto, incluyendo permisos, bloqueos de
  edición directa, préstamos, retornos parciales/con incidencia y flotilla.
- `VerificarEquiposWindows.cs`: código de salida 0 con tres sesiones reales.
  Dos envíos simultáneos del mismo intento devuelven el mismo folio. En seis
  rondas, dos usuarios compitiendo por una herramienta producen un único préstamo.
- Un conflicto intermitente de concurrencia observado durante las pruebas se
  resolvió coordinando las transacciones breves de custodia. La prueba de seis
  rondas se ejecutó después del ajuste, con el cliente definitivo.
- El recorrido de compras anterior volvió a pasar sin conservar nuevos movimientos.
- Compilación del cliente/iniciador y construcción Docker correctas; revisión
  visual del formulario de vehículo, con todos los campos accesibles.
- Respaldo previo a la actualización generado el 17 de noviembre. El ensayo de
  recuperación documentado arriba corresponde al respaldo del 16 de noviembre,
  anterior a la incorporación de los nuevos módulos.

No se verificó despliegue en otras estaciones físicas, carga alta, concurrencia
desde equipos distintos, cortes eléctricos ni operación fiscal/contable real. El respaldo
restaurado es el generado antes del último permiso de lectura de unidades; la
configuración de permisos de la versión entregada se aplica con `Configurar`.
No se realizó una migración del histórico de SQLite. La guía ENTREGA.md enumera
las funciones del sistema anterior que aún no están migradas.


### Actualización del 19 de noviembre de 2026

- Las tres suites `verificar_integracion.verificar`, `verificar_equipos.verificar`
  y `verificar_kits.verificar` finalizaron con `correcto: true`; sus movimientos
  transaccionales se revierten al terminar.
- Kits: exclusividad de pertenencia, préstamo selectivo, bloqueo de composición
  durante préstamo, retorno, historial y rechazo de capturas obsoletas.
- Garantías/mantenimiento: fechas, permisos, costo pagado/gratuito, rechazo de
  comprobantes duplicados e importes no finitos, anulación y recaptura sin inflar
  el acumulado. Los reintentos del mismo identificador no duplican la anulación.
- `VerificarKitsWindows.cs`: recorrido HTTP con tres cuentas y código de salida 0.
  Conserva el ejemplo KIT-DEMO-1119104521, préstamo activo 32l3f47307 y costo
  corregido de 200 MXN frente a adquisición de 1,000 MXN.
- Cliente e iniciador compilados. Imagen Docker reconstruida y desplegada;
  migración finalizada, servidor responde `pong` y tarea de permanencia WSL activa.
- Revisión visual del cliente nativo: búsqueda por código con Enter recupera el
  kit, muestra disponibilidad 1/2 y abre su vista previa de etiqueta en español.
- Cuatro imágenes generadas por `VerificarEtiquetas.cs` se decodificaron con
  zxing-cpp 3.1.1, independiente del generador: código corto, kit, máximo de
  30 caracteres y signos ASCII. Los cuatro contenidos coinciden exactamente.
  También se decodificó el PNG entregado, KIT-DEMO-1119104521. Códigos vacíos,
  con acentos, salto de línea o más de 30 caracteres fueron rechazados.
- La comprobación visual no envió impresiones; impresora y lector físicos
  siguen sin probarse. El decodificador es una dependencia de pruebas, no del cliente.
- Respaldo previo a la ampliación creado el 19 de noviembre. La restauración
  ensayada el 16 de noviembre no cubre las tablas incorporadas posteriormente.
