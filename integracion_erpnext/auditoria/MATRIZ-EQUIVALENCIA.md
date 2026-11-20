# Matriz de equivalencia funcional

Fecha: 19 de noviembre de 2026. Capacidades agrupadas del código inspeccionado; no es un porcentaje de avance del producto.

Total: 55 capacidades. Parcial: 21; Verificado acotado: 8; No integrado: 20; Cambio de comportamiento: 3; Sin verificar: 3.

**Verificado acotado:** el caso descrito pasó; no certifica variantes no ensayadas. **Parcial:** existe una parte pero faltan datos, acciones o pruebas. **No integrado:** no está disponible como flujo equivalente en el cliente entregado, aunque ERPNext tenga piezas estándar. **Cambio de comportamiento:** requiere homologar reglas/experiencia; no es necesariamente un error. **Sin verificar:** no hay evidencia suficiente.

**P1:** bloquea declarar equivalencia para ese proceso si se conserva en el alcance. **P2:** diferencia funcional o de experiencia que debe resolverse o retirarse explícitamente del alcance. No son severidades de una auditoría de ciberseguridad.

## CAT-01 · Catálogo técnico y familias

**Parcial · P2**

- Original: Marca, modelo, medidas, familias y especificaciones por categoría.
- Integración: Alta de código, nombre, grupo y unidad; sin edición técnica equivalente.
- Evidencia: Código y prueba de alta básica.
- Criterio para cerrar: Crear y editar una ficha completa sin perder sus campos.
- Fuentes: [original L1844](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:1844) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## CAT-02 · Unidades de compra y almacén

**Parcial · P1**

- Original: Unidad de compra, unidad de almacén y factor editable.
- Integración: La API expone unidad de compra, unidad de inventario y factor en artículo, solicitud, oferta y recepción. Pendiente conectar la captura en WPF.
- Evidencia: Validar.ps1: 2 cajas con factor 10 generan stock_qty=20 y recibir 1 caja ingresa 10 unidades de inventario.
- Criterio para cerrar: Verificar en WPF la captura y visualización del factor y las unidades. Probar salida y retorno con factores y decimales.
- Fuentes: [original L74](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Models/Entities.cs:74) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## CAT-03 · Proveedores y condiciones

**Parcial · P1**

- Original: Razón social, nombre comercial, RFC, contacto y condiciones de pago.
- Integración: La API de proveedor guarda teléfono, persona de contacto y correo mediante Supplier y Contact de ERPNext.
- Evidencia: Validar.ps1: creación del contacto principal, consulta por Compras, alta con solo nombre de contacto y reintento idempotente.
- Criterio para cerrar: Verificar en WPF los datos de contacto guardados en ERPNext y completar condiciones de compra en la orden.
- Fuentes: [original L1414](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:1414) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## CAT-04 · Personal, departamentos y responsables

**Parcial · P1**

- Original: Directorios relacionados por identificadores.
- Integración: La solicitud vincula un Department de ERPNext y guarda el nombre del solicitante como texto. En préstamos se mantiene la captura de responsable y departamento como texto.
- Evidencia: Validar.ps1: captura y consulta de departamento, solicitante y notas en Material Request, con permiso de selección de Department.
- Criterio para cerrar: Seleccionar personal y departamento; conservar trazabilidad hasta entrega y reportes.
- Fuentes: [original L1212](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:1212) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/equipos.py)

## CAT-05 · Obras y expedientes por proyecto

**Parcial · P2**

- Original: Código, área/cliente, estado y desglose de movimientos.
- Integración: Project con empresa y nombre; referencias por renglón; sin expediente equivalente.
- Evidencia: Prueba de alta y trazabilidad básica.
- Criterio para cerrar: Consultar compras, salidas, costos y responsables de una obra.
- Fuentes: [original L1297](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:1297) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## CAT-06 · Fotos, ubicación y mapa de anaqueles

**Parcial · P2**

- Original: Fotos y mapa visual; ubicación por producto/herramienta.
- Integración: Texto de ubicación en herramienta/kit; sin fotos ni mapa.
- Evidencia: Código y acciones del cliente.
- Criterio para cerrar: Adjuntar imagen y localizar/reubicar material con trazabilidad.
- Fuentes: [original L8019](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:8019) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/equipos.py)

## SOL-01 · Solicitud con artículos catalogados

**Verificado acotado · P2**

- Original: Captura de renglones y cantidades.
- Integración: Crea Material Request confirmada con artículos, obra y partida.
- Evidencia: Suite verificar_integracion aprobada.
- Criterio para cerrar: Mantener el caso aprobado; no implica equivalencia de metadatos o edición.
- Fuentes: [original L2743](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:2743) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## SOL-02 · Conceptos libres y conversión a SKU

**Parcial · P1**

- Original: Descripción y unidad libre, después vincular o crear producto.
- Integración: La API conserva la trazabilidad de conceptos libres y permite resolverlos a un SKU mediante una enmienda nativa antes de crear documentos de compra.
- Evidencia: La suite integral conservó dos conceptos libres hasta recepción y verificó la conversión a un SKU existente o nuevo con historial. Rechazó la conversión con dependencias activas.
- Criterio para cerrar: Verificar en Desk la captura y la conversión a SKU. Definir el tratamiento de conceptos con documentos de compra activos.
- Fuentes: [original L2788](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:2788) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## SOL-03 · Borradores, edición y anexos

**Parcial · P1**

- Original: Actualizar solicitud existente y crear anexos.
- Integración: El backend crea un anexo en borrador vinculado a la solicitud. Desk permite editar, confirmar y consultar su documento de origen.
- Evidencia: La suite integral verificó el anexo vinculado en borrador, la herencia de cabecera, el reintento y la confirmación nativa.
- Criterio para cerrar: Verificar en Desk la edición de borradores y el tratamiento de cambios cuando hay compras activas.
- Fuentes: [original L2813](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:2813) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## SOL-04 · Carga rápida CSV y correo entrante

**No integrado · P2**

- Original: Plantilla, interpretación de correo y conversión a solicitud.
- Integración: No hay importación ni bandeja en Windows.
- Evidencia: Código de plantilla y ConvertirCorreoBandejaEnSolicitud.
- Criterio para cerrar: Importar un archivo ejemplo con errores controlados; correo solo cuando se configure.
- Fuentes: [original L11416](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:11416) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## SOL-05 · Surtir desde stock y reserva por obra

**Parcial · P1**

- Original: Autorización CP-1A y carga de solicitud a salida; reserva en stock.
- Integración: La enmienda cambia una compra sin dependencias a salida o traslado desde stock. Las Stock Entry nativas conservan el vínculo con la solicitud y su renglón.
- Evidencia: La suite integral verificó el saldo tras una salida parcial, el traslado completo, las existencias y los rechazos de excedentes. Los reintentos devolvieron el folio original.
- Criterio para cerrar: Validar el flujo guiado de autorización y definir la reserva previa de existencias por obra.
- Fuentes: [original L666](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/MainWindow.Checkpoints.cs:666) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## COM-01 · Petición y oferta completa

**Verificado acotado · P2**

- Original: Cotizar renglones por proveedor.
- Integración: RFQ y Supplier Quotation mediante mapeos ERPNext.
- Evidencia: Suite integral: petición/oferta confirmadas.
- Criterio para cerrar: Mantener caso completo; ofertas parciales se evalúan por separado.
- Fuentes: [original L3864](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:3864) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## COM-02 · Proveedor cotiza solo algunos renglones

**Parcial · P1**

- Original: Captura y adjudicación por renglón/proveedor.
- Integración: La API guarda los renglones con precio positivo y permite adjudicarlos por proveedor. Pendiente conectar la captura y la presentación en Windows.
- Evidencia: Validar.ps1 en lebaref-pruebas.local: una oferta por renglón, comparativa sin opción para el renglón omitido y dos órdenes en borrador para proveedores distintos.
- Criterio para cerrar: Verificar en Windows la captura, la comparativa y la impresión de dos ofertas parciales adjudicadas a proveedores distintos.
- Fuentes: [original L3864](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:3864) · [suite integral](../app/lebaref_compras/lebaref_compras/verificar_integracion.py)

## COM-03 · Comparativa y órdenes por proveedor

**Parcial · P1**

- Original: Ganador por artículo, 4+ proveedores, condiciones y costos.
- Integración: Selección por renglón y agrupación; una cotización vigente por proveedor.
- Evidencia: Caso simple aprobado; no se acredita toda combinación comercial.
- Criterio para cerrar: Dos obras/partidas, tres proveedores, selección mixta y órdenes sin duplicar.
- Fuentes: [original L4281](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:4281) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/comparativa.py)

## COM-04 · Flete, impuestos, descuentos y moneda

**Parcial · P1**

- Original: Comparativa original suma flete y calcula 16% fijo en generación de OC.
- Integración: ERPNext aplica plantillas fiscales y reglas de flete en cotización y orden. La comparativa expone el desglose de la oferta.
- Evidencia: Validar.ps1: neto 400 más IVA 64 da 464, exento con flete 40 da 440, e IVA con flete da 504 desde el mapper nativo.
- Criterio para cerrar: Verificar descuentos, moneda y las plantillas fiscales configuradas para la empresa.
- Fuentes: [original L4403](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:4403) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## COM-05 · Banco de precios y sugerencias históricas

**No integrado · P2**

- Original: Banco de cotizaciones, vigencia, referencias similares y auto-adjudicación.
- Integración: Ofertas vinculadas a la solicitud actual; sin análisis histórico equivalente.
- Evidencia: Métodos originales y acciones nuevas.
- Criterio para cerrar: Consultar histórico normalizado por unidad/moneda y justificar propuesta de adjudicación.
- Fuentes: [original L3674](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:3674) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/comparativa.py)

## COM-06 · Autorización de orden

**Verificado acotado · P1**

- Original: Vo.Bo. Dirección y cambios de etapa.
- Integración: Compras crea borrador; Responsable confirma; Compras no puede confirmar.
- Evidencia: Suite integral y lectura actual de permisos.
- Criterio para cerrar: Conservar separación probada; no confundir confirmar con pagar.
- Fuentes: [original L4418](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:4418) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## COM-07 · Pago programado y seguimiento de pago

**Parcial · P1**

- Original: Condición de pago, fecha programada y estado pagada.
- Integración: Las cotizaciones registran condición y fecha programada. La orden hereda ambas y ERPNext calcula el calendario nativo. El pago efectivo queda en el flujo financiero.
- Evidencia: Validar.ps1: vencimiento a 30 días, fecha programada e importe de cuota tanto en órdenes de la comparativa como en el mapper nativo.
- Criterio para cerrar: Registrar programación y pago con responsable y comprobante; reflejar saldo correctamente.
- Fuentes: [original L4562](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:4562) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## COM-08 · Cierre parcial, rechazo y reapertura

**No integrado · P1**

- Original: Finiquitar parcial y acciones administrativas con motivo.
- Integración: Sin acciones nativas de cierre/reapertura; algunas acciones existen en ERPNext administrativo.
- Evidencia: BtnFiniquitarOcParcial y Actions.
- Criterio para cerrar: Cerrar el remanente sin inventar una recepción; reabrir/corregir con autorización.
- Fuentes: [original L792](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/MainWindow.Checkpoints.cs:792) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## ALM-01 · Recepción parcial contra orden

**Verificado acotado · P1**

- Original: Incrementar recibido y stock por renglón.
- Integración: Purchase Receipt, saldo por recibir y rechazo de exceso.
- Evidencia: Suite integral aprobada: 2 de4 y exceso99 rechazado.
- Criterio para cerrar: Mantener prueba; ampliar a unidades alternativas y varios renglones.
- Fuentes: [original L4660](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:4660) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## ALM-02 · Entrada sin OC y regularización

**Parcial · P1**

- Original: Entrada por regularizar vinculable después a OC/XML.
- Integración: API `entrada_sin_oc` confirma una recepción nativa. `regularizar_entrada` vincula la misma recepción a una orden autorizada conservando inventario, valoración y comentarios auditables. Se regulariza antes de facturas o devoluciones vinculadas.
- Evidencia: Validar.ps1 con imagen lebaref/erpnext-codex:alm02, migración y tres suites correctas. Caso con consumo previo, reintentos, referencias inválidas, permisos y recepción del saldo. Libros de inventario y contabilidad de la recepción conservan su número de registros.
- Criterio para cerrar: Conectar los diálogos de Desk, desplegar el backend y habilitar un perfil operativo con escritura de recepción y orden. El perfil actual Responsable de Compras carece de escritura de recepción. Validar la captura por auxiliares.
- Fuentes: `recepciones.py`, `verificar_recepciones.py` y §9 de CONTRATO-API.md.

## ALM-03 · Recepción directa a obra y servicios

**Parcial · P1**

- Original: Persona, remisión y opción de servicio no inventariable.
- Integración: `recibir` permite elegir almacén de la obra por renglón conservando la obra de la orden. Captura persona, remisión y motivo del cambio. `articulo` permite alta de Servicio (`is_stock_item=0`). Los datos omitidos mantienen el flujo habitual.
- Evidencia: `verificar_destinos` recibe dos unidades directamente en obra y un servicio, verifica el inventario por almacén, ausencia de Bin y Stock Ledger Entry para el servicio, reintento, destinos inválidos y recepción del saldo en el almacén original. `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:alm03`.
- Criterio para cerrar: Integrar y desplegar el backend, conectar la captura en Desk y validar el recorrido del auxiliar. El almacén de obra se selecciona explícitamente.
- Fuentes: `recepciones.configurar_destinos`, `solicitudes.nuevo_articulo`, `verificar_recepciones.verificar_destinos` y §10 de CONTRATO-API.md.

## ALM-04 · Sustitución de marca/artículo al recibir

**Parcial · P1**

- Original: Cambiar producto de partida y motivo.
- Integración: Autorización confirmada por renglón y cantidad, con permisos de escritura y autorización de la orden. Conserva original, sustituto, motivo, precio y referencias nativas. Recepción y factura aplican una excepción de identidad acotada a la autorización comprobada.
- Evidencia: `verificar_sustituciones` valida auxiliar rechazado, aprobación del responsable, recepción parcial e idempotente, límite autorizado, rechazo de recepción alterada, factura nativa, devolución nativa, conservación del original y saldo de orden. `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:alm04`.
- Criterio para cerrar: Desplegar el backend y los workers de la misma versión, conectar la autorización y recepción en Desk y validar el recorrido operativo. Esta versión conserva precio, unidad base y conversión de la orden; cambios comerciales requieren revisar la documentación de compra.
- Fuentes: `sustituciones.py`, `verificar_sustituciones.py` y §11 de CONTRATO-API.md.

## ALM-05 · Entrega versus consumo/costo de obra

**Parcial · P1**

- Original: Salida descuenta stock y calcula costo neto consumido con devoluciones.
- Integración: La entrega mantiene Material Transfer y el consumo vinculado confirma Material Issue con obra, partida, motivo y valoración nativa. Consumo y devolución comparten el saldo bajo resguardo en unidades del original. La consulta muestra cantidades y costo a sesiones con lectura de GL Entry.
- Evidencia: `verificar_consumos` prueba dos cajas con factor 10, consumo de media caja con gasto por obra de 50, devolución de una caja, reintento, límites compartidos aun con stock de otra procedencia, consumo nativo alterado rechazado y anulación que restituye saldo y revierte costo. `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:alm05`.
- Criterio para cerrar: Desplegar y conectar consumir y consultar en Desk, identificar el consumo en impresión y validar el recorrido operativo. Merma y ajustes físicos se completan en ALM-08.
- Fuentes: `consumos.py`, `verificar_consumos.py` y §12 de CONTRATO-API.md.

## ALM-06 · Devolución ligada a entrega

**Verificado acotado · P1**

- Original: Restituye stock y controla saldo de salida.
- Integración: Transferencia inversa con referencia al renglón y límite retornable.
- Evidencia: Suite integral: almacén de prueba vuelve a cero y segunda devolución se rechaza.
- Criterio para cerrar: Mantener invariantes de cantidad; costo/consumo se audita en ALM-05.
- Fuentes: [original L5123](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:5123) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## ALM-07 · Sobrante sin salida original

**Parcial · P1**

- Original: Reingreso identificado por obra/persona sin salida asociada.
- Integración: Sobrante en borrador con obra, persona, acta y motivo. El responsable confirma con permisos de Stock Entry y Stock Reconciliation. Traslada inventario ya contabilizado, o ingresa stock físicamente verificado como no contabilizado con valoración declarada y confirmación explícita.
- Evidencia: `verificar_sobrantes` comprueba borrador sin movimiento, rechazo de autorización/cancelación por Almacén, traslado de tres unidades conservando total diez, acta normalizada única con otro UUID, entrada verificada de dos, anulación y corrección conservando referencia e historial. `Validar.ps1 -ImagenBackend lebaref/erpnext-codex:alm07`.
- Criterio para cerrar: Conectar revisión/autorización e impresión en Desk y validar actas reales contra existencias migradas. La clasificación del material como contabilizado o pendiente de alta requiere verificación física del responsable.
- Fuentes: `sobrantes.py`, `verificar_sobrantes.py` y §13 de CONTRATO-API.md.

## ALM-08 · Conteo físico, ajustes y merma

**No integrado · P1**

- Original: Ajuste al stock físico con motivo y bitácora.
- Integración: No hay acción Windows; Stock Reconciliation existe pero no está habilitado a estos perfiles.
- Evidencia: Código y consulta de permisos.
- Criterio para cerrar: Ajuste positivo/negativo aprobado, razón y valoración auditables.
- Fuentes: [original L8869](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:8869) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## ALM-09 · Stock mínimo y reabastecimiento

**No integrado · P2**

- Original: Mínimos, alertas y solicitar reabastecimiento.
- Integración: Consulta Bin; sin configuración/flujo equivalente en Windows.
- Evidencia: ActualizarStockMinimoProducto y Actions.
- Criterio para cerrar: Bajo mínimo produce alerta y solicitud trazable sin duplicaciones.
- Fuentes: [original L7468](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:7468) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## ALM-10 · Series, lotes e inspección

**Sin verificar · P2**

- Original: No se acredita un flujo original completo comparable.
- Integración: DocTypes estándar existen; UI no recoge bundle/inspección especializada.
- Evidencia: Inventario instalado; no prueba punta a punta.
- Criterio para cerrar: Probar únicamente si los artículos reales requieren series/lotes/inspección.
- Fuentes: [original L53](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Models/Entities.cs:53) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## XML-01 · Archivo privado y UUID

**Verificado acotado · P1**

- Original: Registro de comprobante por UUID.
- Integración: Archivo privado, RFC/UUID y rechazo de duplicado.
- Evidencia: Suite integral y sondeo de flags: inserción sin XML rechazada.
- Criterio para cerrar: Mantener validaciones; esto no acredita conciliación ni estado SAT.
- Fuentes: [original L6111](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:6111) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## XML-02 · Conceptos XML y conciliación

**No integrado · P1**

- Original: Mapea conceptos a productos, crea proveedor/producto y sincroniza orden.
- Integración: Solo encabezado y archivo contra recepción; no valida conceptos/importes contra OC.
- Evidencia: GuardarFacturaXml frente a registrar_cfdi.
- Criterio para cerrar: Vincular conceptos/cantidades/importes y detectar diferencias sin contabilizar dos veces.
- Fuentes: [original L6111](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:6111) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## XML-03 · Compra con nota/ticket y sin factura

**No integrado · P1**

- Original: Compra directa, comprobante interno y entrada opcional.
- Integración: Solo CFDI tipo I contra recepción confirmada.
- Evidencia: RegistrarCompraDirectaSinFactura y TARGETS.
- Criterio para cerrar: Registrar nota interna diferenciada de CFDI, con gasto/compra/entrada según corresponda.
- Fuentes: [original L7097](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:7097) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## XML-04 · Factura/pago contable y conexión con activos

**No integrado · P1**

- Original: Registro operativo de factura/pago; no se acredita contabilidad ERP completa en el original.
- Integración: Purchase Invoice/Payment Entry/Asset existen, pero no están integrados en Windows.
- Evidencia: DocTypes instalados y permisos; no ensayo contable.
- Criterio para cerrar: Definir alcance contable y conciliar recepción-factura-pago si se adopta; no presumir que archivar XML lo cumple.
- Fuentes: [original L5979](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:5979) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## HER-01 · Fichas y vínculo con recepción

**Parcial · P1**

- Original: Directorio ligado a producto; hay helper de alta desde recepción.
- Integración: Alta individual manual vinculada a Item, sin enlace a recepción/costo de compra.
- Evidencia: Helper original declarado sin llamadas halladas; NO se acredita automatización original activa.
- Criterio para cerrar: Relacionar unidades adquiridas y fichas sin doble alta; no copiar automatismo no comprobado.
- Fuentes: [original L4611](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:4611) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/equipos.py)

## HER-02 · Préstamo y retorno con incidencias

**Verificado acotado · P1**

- Original: Custodia por herramienta, responsable y obra.
- Integración: Préstamo exclusivo, retorno parcial, daño/extravío e historial.
- Evidencia: Suite equipos aprobada; concurrencia HTTP previamente documentada.
- Criterio para cerrar: Mantener pruebas y validar entrega/retorno desde dos estaciones físicas.
- Fuentes: [original L5258](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:5258) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/equipos.py)

## HER-03 · Kits y checklist de retorno

**Parcial · P2**

- Original: Despacho de disponibles y checklist centrado en kit.
- Integración: Selección explícita, composición protegida; retorno se hace desde préstamo.
- Evidencia: Suite kits aprobada; experiencia de retorno distinta.
- Criterio para cerrar: Kit parcialmente prestado, parte vuelve dañada y parte queda en obra, sin perder custodia.
- Fuentes: [original L9176](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:9176) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/kits.py)

## HER-04 · Garantías y mantenimiento acumulado

**Verificado acotado · P2**

- Original: Acumulado y aviso de reparación >=50%.
- Integración: Eventos de garantía/mantenimiento, acumulado, aviso y anulación auditada.
- Evidencia: Suite kits: anular y recapturar sin inflar costo.
- Criterio para cerrar: Conservar mejora de historial y validar migración del acumulado histórico.
- Fuentes: [original L9790](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:9790) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/kits.py)

## HER-05 · Etiquetas de códigos de barras

**Parcial · P2**

- Original: Etiquetas/catálogo y códigos en documentos.
- Integración: Etiqueta individual herramienta/kit/vehículo; no catálogo general ni códigos en todos los formatos.
- Evidencia: Decodificación independiente previa; hardware sin probar.
- Criterio para cerrar: Lectura e impresión física y etiquetas de artículos si se conservan del original.
- Fuentes: [original L1229](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Services/BarcodePrintService.cs:1229) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/LabelForm.cs)

## FLO-01 · Vehículos, gastos y revisión

**Parcial · P2**

- Original: Ficha, combustible/taller y checklist semanal.
- Integración: Operaciones básicas e historial; checklist y campos resumidos.
- Evidencia: Suite equipos aprobada; no homologación campo por campo del checklist.
- Criterio para cerrar: Aprobar cada punto de inspección y corregir datos sin borrar historial.
- Fuentes: [original L9386](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:9386) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/equipos.py)

## FLO-02 · Rendimiento, CPK y proyección de servicio

**No integrado · P2**

- Original: Costo/km, km/litro, gasto por categoría y fecha estimada de servicio.
- Integración: Total de gastos y alertas de km/seguro; sin indicadores equivalentes.
- Evidencia: Models VehiculoFlotilla frente a detalle de equipos.
- Criterio para cerrar: Mismo conjunto de cargas/km produce cálculo documentado y separa datos insuficientes.
- Fuentes: [original L1504](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Models/Entities.cs:1504) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/equipos.py)

## FLO-03 · Corrección de gastos y revisiones

**No integrado · P1**

- Original: Permite eliminar gastos/revisiones.
- Integración: Eventos protegidos sin anulación para flotilla; anulación nueva solo cubre mantenimiento de herramienta.
- Evidencia: Métodos originales y TARGETS equipos/kits.
- Criterio para cerrar: Anular con motivo, recalcular acumulados y servicio; conservar registro original.
- Fuentes: [original L9640](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:9640) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/equipos.py)

## DOC-01 · Vales, OC, comparativa, PDF y responsiva EPP

**No integrado · P1**

- Original: Formatos imprimibles y descargables con metadatos/cierre.
- Integración: Windows exporta CSV y etiquetas; no reproduce esos documentos.
- Evidencia: Servicios de impresión original y menú nuevo.
- Criterio para cerrar: Emitir OC, recepción, salida, préstamo y responsiva con folio/personas/firmas de recepción.
- Fuentes: [original L739](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Services/BarcodePrintService.cs:739) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## GES-01 · Agenda y pendientes operativos

**No integrado · P2**

- Original: Eventos manuales/automáticos, posponer, completar y navegar al folio.
- Integración: Sin agenda equivalente en Windows.
- Evidencia: ObtenerEventosCalendarioUnificados y Actions.
- Criterio para cerrar: Pendientes por responsable, fecha, vencimiento y cierre documentado.
- Fuentes: [original L7584](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:7584) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## GES-02 · Checkpoints y notificaciones internas

**No integrado · P1**

- Original: Transiciones entre áreas y notificaciones leídas/no leídas.
- Integración: Permisos e historial de documentos, sin bandeja equivalente.
- Evidencia: RegistrarCheckpoint frente a cliente.
- Criterio para cerrar: Compras/Almacén/Responsable reciben y resuelven tareas de cada transición.
- Fuentes: [original L8087](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:8087) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## GES-03 · Correo saliente y adjuntos

**No integrado · P2**

- Original: Plantillas, comparativa y notificaciones SMTP.
- Integración: Peticiones con send_email=0; sin envíos.
- Evidencia: Código original y petición nueva.
- Criterio para cerrar: Configurar y aprobar destinatarios/plantillas; probar sin enviar a terceros en esta auditoría.
- Fuentes: [original L10974](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:10974) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/operaciones.py)

## ANA-01 · Históricos, ranking, Kraljic, PPV y cuadrillas

**No integrado · P2**

- Original: Análisis de compras, variación de precios y consumo/merma.
- Integración: No hay reportes equivalentes en Windows.
- Evidencia: Métodos originales y módulos nuevos.
- Criterio para cerrar: Comparar indicadores con un dataset conciliado y fórmulas explícitas.
- Fuentes: [original L9844](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:9844) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## SEG-01 · Identidad y roles de usuarios

**Cambio de comportamiento · P1**

- Original: Usuarios/PIN y rol operativo local.
- Integración: Sesión individual de servidor; permisos reales por DocType.
- Evidencia: Sondeo runtime: Compras no autoriza; Almacén puede cancelar PR/Stock Entry.
- Criterio para cerrar: Aprobar matriz de roles, incluidas API y pantalla administrativa; no basta ocultar botones.
- Fuentes: [original L8298](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:8298) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/configuracion_inicial.py)

## SEG-02 · Reintentos e interrupción

**Parcial · P1**

- Original: No se acredita control equivalente de idempotencia en el original.
- Integración: UUID por ventana y bloqueo de transacción; se pierde al cerrar la captura.
- Evidencia: Pruebas de reintento aprobadas; cierre abrupto pendiente.
- Criterio para cerrar: Recuperar resultado después de cierre/corte sin duplicar; conciliar órdenes de comparativa.
- Fuentes: [original L4281](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:4281) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/OperationForm.cs)

## SEG-03 · Historial de auditoría y correcciones

**Parcial · P1**

- Original: Bitácora usuario/admin consultable.
- Integración: owner/modified_by y eventos custom; sin visor unificado ni correcciones de todos los documentos.
- Evidencia: Código de modelos, hooks y cliente.
- Criterio para cerrar: Buscar quién cambió qué, motivo y antes/después por folio.
- Fuentes: [original L8417](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:8417) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/app/lebaref_compras/lebaref_compras/equipos.py)

## UX-01 · Búsquedas, filtros y exportación completa

**Parcial · P1**

- Original: Filtros y expedientes específicos en módulos originales.
- Integración: 100 recientes, selectores1000; código exacto solo herramientas/vehículos/kits.
- Evidencia: ErpClient.List y MainForm.SearchCode.
- Criterio para cerrar: Encontrar una OC antigua fuera de100; paginar y exportar todo con filtros.
- Fuentes: [original L1463](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:1463) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/ErpClient.cs)

## UX-02 · Navegación y correspondencia visual

**Cambio de comportamiento · P2**

- Original: Interfaz WPF con módulos, tarjetas, expedientes y menús contextuales.
- Integración: Nueva interfaz WinForms genérica; ERPNext está detrás, no su escritorio.
- Evidencia: Arquitectura y listas Actions/DocTypes.
- Criterio para cerrar: Recorrido autónomo por auxiliar con mismos conceptos, ayudas y acciones por contexto.
- Fuentes: [original L1629](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/MainWindow.xaml:1629) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/MainForm.cs)

## OPS-01 · Migración de datos e históricos

**No integrado · P1**

- Original: SQLite y archivos propios.
- Integración: Datos ficticios ERPNext; no importador/conciliación ejecutados.
- Evidencia: Documentación de entrega; baseline no leyó datos personales.
- Criterio para cerrar: Ensayo migración repetible; conteos, saldos y vínculos conciliados; plan de reversión.
- Fuentes: [original L26](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:26) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/ENTREGA.md)

## OPS-02 · Respaldo/restauración versión completa

**Parcial · P1**

- Original: ZIP integral y restauración local.
- Integración: Scripts de respaldo; restauración previa a módulos nuevos.
- Evidencia: Ensayo documentado16nov; no se repitió restauración completa en esta auditoría.
- Criterio para cerrar: Restaurar versión actual en sitio aislado y verificar kits/archivos/saldos/permisos.
- Fuentes: [original L6736](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:6736) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/pruebas/Respaldar.ps1)

## OPS-03 · Varias estaciones y acceso solo oficina

**Sin verificar · P1**

- Original: No se acredita despliegue original compartido en esta auditoría.
- Integración: Servidor loopback; HTTPS admitido por cliente; red privada aún no desplegada.
- Evidencia: Configuración compose y ErpClient.
- Criterio para cerrar: Dos estaciones físicas, usuarios nominales, acceso externo bloqueado y reconexión probada.
- Fuentes: [original L13](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/Data/DatabaseService.cs:13) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/cliente_windows/ErpClient.cs)

## OPS-04 · Aceptación por usuarios y fallos reales

**Sin verificar · P1**

- Original: No se ejecutó comparación paralela del original con casos reales.
- Integración: Pruebas sintéticas; no cortes eléctricos, carga alta ni revisión manual de todas las pantallas.
- Evidencia: Límites de evidencia declarados.
- Criterio para cerrar: Auxiliares completan escenarios acordados; incidencias resueltas y aceptación registrada.
- Fuentes: [original L1](C:/Users/oscar/.gemini/antigravity/scratch/SistemaComprasAlmacen/MainWindow.xaml:1) · [integración](C:/Users/oscar/Documents/Codex/2026-11-16/https-github-com-frappe-erpnext-git/work/lebaref-integracion/integracion_erpnext/RESULTADO-PRUEBAS.md)
