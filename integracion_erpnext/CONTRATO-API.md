# Contrato de API — ERPNext 16, Frappe Desk y `lebaref_compras`

> **Regla de colaboración:** Cada vez que **Codex** agregue o modifique un endpoint `@frappe.whitelist` en `integracion_erpnext/app/lebaref_compras/`, debe actualizar este documento con los parámetros de entrada, estructura JSON de respuesta y errores de validación. **Antigravity** usará este contrato al conectar los diálogos y botones guiados de Frappe Desk.

---

## 1. Transporte, Sesión e Idempotencia

- **Base URL local de pruebas:** `http://127.0.0.1:8186/` (en producción de oficina: `https://<servidor-privado>/` configurado en `lebaref-servidor.txt`).
- **Autenticación:**
  - `POST /api/method/login` (`application/x-www-form-urlencoded`: `usr`, `pwd`) → establece cookie `sid`.
  - `GET /api/method/frappe.auth.get_logged_user` → `{"message": "<usuario>"}` (rechazar si vacío o `"Guest"`).
  - `POST /api/method/logout` → cierra sesión.
- **Control de idempotencia en escrituras:**
  - Todo llamado a `ejecutar` recibe `operacion` (UUID v4 generado por la ventana de captura).
  - El servidor calcula `custom_lebaref_huella = sha256([accion, folio, datos])` y serializa por usuario (`SELECT ... FOR UPDATE`).
  - Si el mismo UUID llega de nuevo con los mismos datos, devuelve `{"folio": "...", "tipo": "...", "repetida": true}` sin duplicar documentos. Si llega con datos distintos, lanza HTTP `417` indicando abrir nueva operación.
- **Errores HTTP manejados por el cliente:**
  - `401`: credenciales inválidas o sesión expirada.
  - `403`: `frappe.PermissionError` (cuenta sin permiso para el DocType/acción).
  - `404`: documento no encontrado.
  - `417`: validación de negocio (`_server_messages` contiene el mensaje legible en español).

---

## 2. Endpoints Actuales en Producción de Pruebas (Baseline `afc2b49`)

### 2.1Consultas Estándar Frappe
- `GET /api/resource/<DocType>?fields=[...]&filters=[...]&order_by=modified desc&limit_page_length=100`
  - *Nota (`UX-01` pendiente):* Ampliar con `limit_start`, filtros por folio/fecha/proveedor/obra para superar el límite fijo de 100 registros.
- `GET /api/resource/<DocType>/<folio>` → devuelve el documento completo en `data`.

### 2.2 Operaciones de Compras, Almacén y Catálogos (`lebaref_compras.operaciones`)
- **`GET /api/method/lebaref_compras.operaciones.esquema?accion=<accion>&folio=<folio>`**
  - Acciones compras/almacén/maestros: `ajuste`, `autorizar_ajuste`, `conciliar_cfdi`, `facturar_cfdi`, `nota_compra`, `autorizar_factura`, `pagar_factura`, `autorizar_pago`, `articulo`, `proveedor`, `obra`, `almacen`, `solicitud`, `anexo`, `convertir_concepto`, `canalizar_stock`, `surtir`, `peticion`, `oferta`, `autorizar`, `autorizar_sustitucion`, `recibir`, `entrada_sin_oc`, `regularizar_entrada`, `entregar`, `consumir`, `sobrante`, `autorizar_sobrante`, `devolver`, `cfdi`.
  - Delega automáticamente a `lebaref_compras.equipos.esquema` cuando `accion` pertenece a herramientas/flotilla/kits (`herramienta`, `prestamo`, `retorno_herramienta`, `estado_herramienta`, `vehiculo`, `gasto_vehiculo`, `revision_vehiculo`, `estado_vehiculo`, `kit`, `composicion_kit`, `prestamo_kit`, `garantia_herramienta`, `mantenimiento_herramienta`, `anular_mantenimiento`).
  - Respuesta `message`:
    ```json
    {
      "fields": [{"name": "...", "label": "...", "value": "...", "choices": ["..."], "kind": "text|date|multiline|file"}],
      "columns": [{"name": "...", "label": "...", "editable": true, "choices": ["..."]}],
      "rows": [{ "...": "..." }],
      "add_rows": true,
      "note": "Texto guía en español"
    }
    ```
- **`POST /api/method/lebaref_compras.operaciones.ejecutar`**
  - Form params: `accion`, `folio` (vacío si no aplica), `operacion` (UUID), `datos` (JSON con `{"<campo>": "...", "rows": [...]}`).
  - Respuesta `message` en la primera creación: `{"folio": "<nombre-doc>", "tipo": "<DocType>"}`. En un reintento idéntico, incluye `"repetida": true`.

### 2.3 Comparativa de Cotizaciones (`lebaref_compras.comparativa`)
- **`GET /api/method/lebaref_compras.comparativa.consultar?solicitud=<MAT-MR-xxxx>`**
  - Respuesta `message`:
    ```json
    {
      "puede_generar": true,
      "renglones": [
        {
          "id": "<Material Request Item name>",
          "articulo": "...",
          "codigo": "...",
          "partida": "...",
          "obra": "...",
          "cantidad": 4.0,
          "unidad": "Nos",
          "ordenado": false,
          "opciones": [
            {
              "id": "<Supplier Quotation Item name>",
              "cotizacion": "<Supplier Quotation name>",
              "proveedor": "...",
              "precio": 100.0,
              "moneda": "MXN",
              "entrega": 3
            }
          ]
        }
      ]
    }
    ```
- **`POST /api/method/lebaref_compras.comparativa.generar_ordenes`**
  - Form params: `solicitud`, `seleccion` (JSON `{"<mr_item_id>": "<sq_item_id>"}`).
  - Respuesta `message`: `[{"folio": "PUR-ORD-...", "proveedor": "..."}]` (órdenes creadas en borrador `docstatus=0`).

### 2.4 Detalle Especializado de Equipos, Flotilla y Kits (`lebaref_compras.equipos`)
- **`GET /api/method/lebaref_compras.equipos.detalle?tipo=<DocType>&folio=<folio>`**
  - Devuelve `header` (texto resumen) y `rows` (historial/composición/gastos/mantenimientos asociados).

---

## 3. Ofertas parciales (`COM-02`)

- `esquema?accion=oferta&folio=PUR-RFQ-2026-00001` sigue devolviendo todos los renglones de la petición con `rate: 0`. El campo `id` identifica un **Request for Quotation Item**.
- En `ejecutar`, envía `accion=oferta`, `folio=PUR-RFQ-2026-00001`, `operacion=7b14cd06-e977-4c3e-8d81-f474fa143712` y el parámetro `datos` con este JSON. Para un renglón sin cotizar, puedes omitirlo de `rows` o enviar el `id` con un precio cero, vacío, nulo o sin `rate`. Se guardan únicamente los renglones con precio positivo, con su cantidad y referencias de ERPNext. `lead_time_days` se valida para los renglones cotizados y vale cero si se omite.

  ```json
  {
    "supplier": "Proveedor A",
    "valid_till": "2026-12-15",
    "rows": [
      {"id": "rfq-item-a", "rate": "100.00", "lead_time_days": "2"},
      {"id": "rfq-item-b", "rate": 0}
    ]
  }
  ```

- Respuesta HTTP de creación. Un reintento idéntico devuelve el mismo folio y agrega `"repetida": true` dentro de `message`.

  ```json
  {"message": {"folio": "PUR-SQTN-2026-00001", "tipo": "Supplier Quotation"}}
  ```

- HTTP `417` para un proveedor no invitado, referencias ajenas o duplicadas (incluidos renglones con precio cero), precios negativos, no finitos o no numéricos, o una oferta sin al menos un precio positivo.
- `comparativa.consultar` devuelve todos los renglones de la solicitud. Si un proveedor no cotizó uno, queda ausente de su lista de `opciones`. Cuando ningún proveedor tiene una oferta vigente admisible para ese renglón, el resultado es `"opciones": []`, como en el segundo renglón de este ejemplo.

  ```json
  {
    "message": {
      "puede_generar": true,
      "renglones": [
        {
          "id": "mr-item-a", "articulo": "Tornillo", "codigo": "TOR-01",
          "partida": "Materiales", "obra": "PROJ-0001", "cantidad": 4.0,
          "unidad": "Nos", "ordenado": false,
          "opciones": [
            {"id": "sq-item-a", "cotizacion": "PUR-SQTN-2026-00001", "proveedor": "Proveedor A", "precio": 100.0, "moneda": "MXN", "entrega": 2}
          ]
        },
        {
          "id": "mr-item-b", "articulo": "Tuerca", "codigo": "TUE-01",
          "partida": "Materiales", "obra": "PROJ-0001", "cantidad": 4.0,
          "unidad": "Nos", "ordenado": false, "opciones": []
        }
      ]
    }
  }
  ```

- `comparativa.generar_ordenes` conserva el formato de la sección 2.3. Por ejemplo, envía `solicitud=MAT-MR-2026-00001` y `seleccion={"mr-item-a":"sq-item-a","mr-item-b":"sq-item-b"}` cuando el proveedor B ya cotizó el segundo renglón. Cada valor identifica un **Supplier Quotation Item** asociado a la clave de solicitud. La respuesta son dos órdenes en borrador, una por proveedor.

  ```json
  {"message": [{"folio": "PUR-ORD-2026-00001", "proveedor": "Proveedor A"}, {"folio": "PUR-ORD-2026-00002", "proveedor": "Proveedor B"}]}
  ```

## 4. Unidades de compra e inventario (`CAT-02`)

- En `ejecutar?accion=articulo`, `uom` es la unidad de compra de ERPNext (`Item.purchase_uom`). Si se omite, se usa `stock_uom` con factor 1. Si las unidades difieren, `conversion_factor` es obligatorio y positivo. Se guarda en `Item.uoms` y expresa cuántas unidades de inventario hay en una unidad de compra. Las unidades deben existir y estar activas.

  ```json
  {"item_code":"TOR-01","item_name":"Tornillo","item_group":"Products","stock_uom":"Nos","uom":"Box","conversion_factor":10}
  ```
  ```json
  {"message":{"folio":"TOR-01","tipo":"Item"}}
  ```

- `esquema?accion=articulo&folio=TOR-01` consulta el artículo existente con permiso de lectura. En `message.fields` se incluyen estas entradas, entre las de identificación del artículo. Para otras unidades configuradas consulta `GET /api/resource/Item/TOR-01` y lee `data.uoms`.

  ```json
  [
    {"name":"stock_uom","label":"Unidad de inventario","value":"Nos","choices":["Box","Nos"],"kind":"text"},
    {"name":"uom","label":"Unidad de compra","value":"Box","choices":["Box","Nos"],"kind":"text"},
    {"name":"conversion_factor","label":"Factor a inventario","value":10.0,"choices":null,"kind":"text"}
  ]
  ```

- `esquema?accion=solicitud` agrega las columnas `uom` (editable), `stock_uom` y `conversion_factor` (informativas). Al crear una solicitud, `qty` está en `uom`. Se puede omitir toda la información de unidades para aplicar el valor de compra del artículo. Si se envía `uom`, se usa una unidad con conversión válida en ERPNext. Los valores informativos, si se envían, deben coincidir con el catálogo.

  ```json
  {"company":"Lebaref","schedule_date":"2026-12-01","title":"Materiales","rows":[{"item_code":"TOR-01","qty":2,"warehouse":"Central - L","uom":"Box","stock_uom":"Nos","conversion_factor":10}]}
  ```
  ```json
  {"message":{"folio":"MAT-MR-2026-00001","tipo":"Material Request"}}
  ```

  En `GET /api/resource/Material%20Request/MAT-MR-2026-00001`, el primer elemento de `data.items` incluye `{"item_code":"TOR-01","qty":2.0,"uom":"Box","stock_uom":"Nos","conversion_factor":10.0,"stock_qty":20.0}`.

- En `esquema?accion=oferta` y `esquema?accion=recibir` las unidades y el factor son de solo lectura. Ejemplos de elementos en `message.rows`, primero para oferta y después para recepción.

  ```json
  {"id":"rfq-item-a","item_code":"TOR-01","qty":2.0,"rate":0,"lead_time_days":0,"uom":"Box","stock_uom":"Nos","conversion_factor":10.0,"concepto_libre":""}
  ```
  ```json
  {"id":"po-item-a","item_code":"TOR-01","pending":2.0,"qty":0,"uom":"Box","stock_uom":"Nos","conversion_factor":10.0,"concepto_libre":""}
  ```

  En los POST de oferta y recepción puedes omitir estos tres campos o enviarlos tal como los devolvió el esquema. Se valida su coincidencia en renglones cotizados o recibidos. La oferta expresa `rate` por unidad de compra. Por ejemplo, para recibir una caja envía `{"rows":[{"id":"po-item-a","qty":1}]}` y obtendrás `{"message":{"folio":"MAT-PRE-2026-00001","tipo":"Purchase Receipt"}}`. Se reciben 10 unidades de inventario. Un factor inválido, una unidad sin conversión aplicable o el intento de alterar las unidades del documento de origen se rechazan con HTTP `417`.

## 5. Contacto del proveedor (`CAT-03`)

- En `POST /api/method/lebaref_compras.operaciones.ejecutar` con `accion=proveedor`, `telefono`, `contacto` y `correo` son textos opcionales en `datos`. Si llega al menos uno, se crea el contacto principal vinculado al proveedor en la misma transacción. Se requieren permisos de creación de `Supplier` y, para esos datos, de `Contact`. Un correo inválido o varios destinatarios se rechazan con HTTP `417`. El alta conserva la respuesta y la idempotencia de §2.2.

  ```json
  {"supplier_name":"Proveedor A","supplier_group":"Local","tax_id":"AAA010101AAA","telefono":"+52 55 1234 5678","contacto":"Ana de Prueba","correo":"cotizaciones@lebaref.test"}
  ```
  ```json
  {"message":{"folio":"Proveedor A","tipo":"Supplier"}}
  ```

- En `GET /api/method/lebaref_compras.operaciones.esquema?accion=proveedor&folio=Proveedor%20A`, se exige permiso de lectura sobre el proveedor. Estas entradas de `message.fields` muestran los datos existentes. `contacto` se lee desde `Contact.full_name` con permiso de lectura de ese contacto. Cada dato ausente se devuelve como `""`. Con `folio` vacío, el esquema de alta ofrece los mismos campos vacíos.

  ```json
  [
    {"name":"telefono","label":"Teléfono","value":"+52 55 1234 5678","choices":null,"kind":"text"},
    {"name":"contacto","label":"Contacto","value":"Ana de Prueba","choices":null,"kind":"text"},
    {"name":"correo","label":"Correo","value":"cotizaciones@lebaref.test","choices":null,"kind":"text"}
  ]
  ```

- En `GET /api/resource/Supplier/Proveedor%20A` se pueden leer los campos nativos `data.mobile_no`, `data.email_id` y `data.supplier_primary_contact`. Este último identifica el documento `Contact`. Con los permisos correspondientes, se puede consultar `GET /api/resource/Contact/<folio>` y leer `data.full_name`.

## 6. Solicitud con conceptos libres y datos de captura (`SOL-02`)

- `GET /api/method/lebaref_compras.operaciones.esquema?accion=solicitud` agrega `departamento`, `solicitante` y `notas` a `message.fields`, y `concepto_libre` editable a `message.columns`. En `departamento.choices` aparecen los nombres de departamentos activos visibles para la sesión. El POST exige que el departamento seleccionado pertenezca a la empresa de la solicitud. `solicitante` guarda el nombre escrito, incluso si la persona carece de cuenta en ERPNext. `notas` acepta texto multilínea. Los tres campos son opcionales.

- Para crear, usa `POST /api/method/lebaref_compras.operaciones.ejecutar`, `accion=solicitud`, un UUID nuevo y un `folio` vacío. En cada renglón envía un `item_code` de catálogo o un `concepto_libre` con texto. Para el concepto libre la API usa la unidad base del artículo interno `LEBAREF-CONCEPTO-LIBRE` con factor 1. Si se envían las unidades, deben coincidir. El artículo interno es no inventariable. Su recepción conserva el concepto y la trazabilidad sin incrementar existencias. Para productos con control de stock se usa un SKU del catálogo.

  ```json
  {
    "company":"Lebaref","schedule_date":"2026-12-01","title":"Materiales para taller",
    "departamento":"Mantenimiento - L","solicitante":"Ana de Prueba",
    "notas":"Entregar en taller.\nAvisar al recibir.",
    "rows":[
      {"item_code":"TOR-01","qty":2,"warehouse":"Central - L"},
      {"concepto_libre":"Corte de placa a medida","qty":1,"project":"PROJ-0001","custom_lebaref_partida":"Taller"}
    ]
  }
  ```
  ```json
  {"message":{"folio":"MAT-MR-2026-00002","tipo":"Material Request"}}
  ```

- `GET /api/method/lebaref_compras.operaciones.esquema?accion=solicitud&folio=MAT-MR-2026-00002` devuelve una solicitud existente con permiso de lectura. `message.add_rows` es `false` y las columnas son de solo lectura. Ejemplo de los datos de captura en `message.fields` y un renglón libre de `message.rows`.

  ```json
  [
    {"name":"departamento","label":"Departamento","value":"Mantenimiento - L","choices":null,"kind":"text"},
    {"name":"solicitante","label":"Solicitante","value":"Ana de Prueba","choices":null,"kind":"text"},
    {"name":"notas","label":"Notas","value":"Entregar en taller.\nAvisar al recibir.","choices":null,"kind":"multiline"}
  ]
  ```
  ```json
  {"id":"mr-item-b","item_code":"","concepto_libre":"Corte de placa a medida","qty":1.0,"warehouse":null,"project":"PROJ-0001","custom_lebaref_partida":"Taller","uom":"Nos","stock_uom":"Nos","conversion_factor":1.0}
  ```

- En REST nativo, `data.custom_lebaref_departamento`, `data.custom_lebaref_solicitante` y `data.custom_lebaref_notas` son los campos de cabecera de `Material Request`. Los renglones libres incluyen `custom_lebaref_concepto` e `item_code: "LEBAREF-CONCEPTO-LIBRE"`. Los campos nativos de notas y concepto usan entidades HTML para conservar caracteres como `<` y `&`. Los esquemas y la comparativa devuelven texto plano. El concepto se conserva en petición, cotización, orden y recepción. Los esquemas de oferta y recepción lo exponen como `concepto_libre` de solo lectura (cadena vacía para renglones de catálogo); la comparativa devuelve `articulo: "Corte de placa a medida"` y `codigo: ""` para un renglón libre. Identifica el renglón por `id` al adjudicar.
- Se rechazan con HTTP `417` los renglones sin artículo ni texto, con ambos, con el código interno ingresado directamente, con unidades incompatibles o con un departamento deshabilitado o de otra empresa. HTTP `403` si la sesión carece del permiso aplicable de escritura o selección. El alta confirma la solicitud. Se requiere ejecutar `bench --site <sitio> migrate` al desplegar para instalar los campos y el artículo interno.

## 7. Impuestos, flete y programación de pago (`COM-04 / COM-07`)

En Desk, la cotización y la orden utilizan **Purchase Taxes and Charges Template**, **Shipping Rule** de tipo `Buying` y sus cuentas existentes. Configura por empresa las plantillas de IVA 16 % y de exención con Contabilidad. Para plantillas simples, usa un renglón `On Net Total` con tasa `16` o `0`, respectivamente. ERPNext calcula las líneas y totales. La regla de envío se calcula por orden con las partidas adjudicadas. Una regla fija agrega su importe a cada orden. La posición y el tipo de cargo en la tabla nativa determinan la base del impuesto. En el ejemplo de envío nativo, el IVA sobre los artículos es 64 y el flete fijo de 40 se agrega por separado.

`esquema?accion=oferta&folio=PUR-RFQ-2026-00001` agrega opciones para `taxes_and_charges`, `shipping_rule`, `payment_terms_template` y el campo de fecha `fecha_programada_pago`. Los cuatro son opcionales en el POST de `ejecutar` con `accion=oferta`. Se deben enviar los identificadores de los maestros accesibles para la sesión. Si se omite un campo, se aplica el comportamiento habitual de ERPNext. Para una oferta exenta selecciona la plantilla exenta de la empresa.

```json
{
  "supplier":"Proveedor A",
  "valid_till":"2026-12-15",
  "taxes_and_charges":"IVA compras 16 % - L",
  "shipping_rule":"Flete fijo compras",
  "payment_terms_template":"Crédito 30 días",
  "fecha_programada_pago":"2026-12-12",
  "rows":[{"id":"rfq-item-a","rate":100}]
}
```

```json
{"message":{"folio":"PUR-SQTN-2026-00002","tipo":"Supplier Quotation"}}
```

Cada elemento de `message.renglones[].opciones` de `comparativa.consultar` incorpora el objeto `comercial`. Los importes representan la cotización completa en `moneda`. Para los renglones adjudicados, la orden calcula sus propios importes.

```json
{
  "neto":400.0,"impuestos_y_cargos":104.0,"total":504.0,
  "taxes_and_charges":"IVA compras 16 % - L","shipping_rule":"Flete fijo compras",
  "payment_terms_template":"Crédito 30 días","fecha_programada_pago":"2026-12-12",
  "cargos":[
    {"descripcion":"IVA 16%","tipo_calculo":"On Net Total","importe":64.0},
    {"descripcion":"Flete fijo compras","tipo_calculo":"Actual","importe":40.0}
  ]
}
```

En REST nativo, **Supplier Quotation** guarda la condición en `custom_lebaref_condicion_pago` y la fecha en `custom_lebaref_fecha_programada_pago`. La orden guarda `payment_terms_template`, genera `payment_schedule` con vencimientos e importes a partir de su total, y conserva `custom_lebaref_fecha_programada_pago` para el seguimiento operativo. Una orden nativa con una sola cotización como origen hereda la condición cuando el campo está vacío. Una orden creada desde la comparativa usa la condición de la cotización adjudicada. Consultar la orden por `GET /api/resource/Purchase%20Order/<folio>`. Campos representativos en `data` para una orden del 19 de noviembre con crédito a 30 días.

```json
{
  "net_total":400.0,"total_taxes_and_charges":104.0,"grand_total":504.0,
  "payment_terms_template":"Crédito 30 días","custom_lebaref_fecha_programada_pago":"2026-12-12",
  "payment_schedule":[{"payment_term":"Crédito 30 días","due_date":"2026-12-19","invoice_portion":100.0,"payment_amount":504.0}]
}
```

HTTP `417` para una fecha con formato inválido, una plantilla fiscal deshabilitada o de otra empresa, o una regla de flete deshabilitada, de venta o de otra empresa. HTTP `403` para maestros sin acceso. El registro de un pago efectivo corresponde al flujo financiero nativo.

## 8. Anexos, enmiendas y surtido de solicitudes (`SOL-03 / SOL-05 / CAT-04`)

Todas las escrituras usan `POST /api/method/lebaref_compras.operaciones.ejecutar` con `accion`, `folio` de origen, `operacion` UUID y `datos`. Un reintento idéntico devuelve el mismo folio con `repetida: true`. La consulta `esquema?accion=<accion>&folio=<folio>` devuelve los campos de captura. Las acciones aplican los permisos nativos sobre el documento de origen y sobre los documentos que generan.

### Anexo de solicitud

`accion=anexo` crea un **Material Request** en borrador. Hereda empresa y tipo de la solicitud confirmada. Usa renglones nuevos y permite actualizar `departamento`, `solicitante` y `notas`. Los valores omitidos de esos campos se heredan. Una transferencia requiere `from_warehouse` y `warehouse` en cada renglón. El borrador se puede editar y confirmar en Desk con el permiso nativo de envío. `data.custom_lebaref_solicitud_origen` muestra el folio de origen en la lectura nativa.

```json
{"title":"Anexo de consumibles","schedule_date":"2026-12-04","rows":[{"item_code":"TOR-01","qty":2,"warehouse":"Central - L"}]}
```

```json
{"message":{"folio":"MAT-MR-2026-00003","tipo":"Material Request"}}
```

### Conversión de concepto libre a SKU

`accion=convertir_concepto` aplica a una solicitud de compra vigente y confirmada, antes de que tenga peticiones, cotizaciones, órdenes o recepciones activas. Selecciona `renglon` por el ID del **Material Request Item** y proporciona `item_code` de un SKU existente o `articulo` con los datos de alta de §4. Crear un SKU requiere permiso de alta de `Item`. El SKU debe estar activo, ser inventariable y estar habilitado para compras. La cantidad conserva su valor numérico y se interpreta en la unidad de compra del SKU, o en la `uom` que selecciones. Puedes indicar `warehouse` para el renglón.

```json
{"renglon":"mr-item-b","item_code":"TOR-01","uom":"Box","warehouse":"Central - L"}
```

```json
{"renglon":"mr-item-b","articulo":{"item_code":"CORTE-001","item_name":"Placa a medida","item_group":"Products","stock_uom":"Nos"},"warehouse":"Central - L"}
```

```json
{"message":{"folio":"MAT-MR-2026-00004-1","tipo":"Material Request"}}
```

La operación cancela el original y confirma su enmienda en una transacción. La enmienda conserva `data.amended_from` y cada renglón conserva `custom_lebaref_renglon_origen`. El concepto original se consulta en esa versión cancelada. Las dependencias activas se rechazan con HTTP `417`. La sesión necesita permisos de anulación y enmienda del original, y de creación y envío del nuevo documento.

### Surtido desde stock

Para un **Material Request** nativo de tipo `Material Issue` (consumo) o `Material Transfer` (traslado a otro almacén), usa `accion=surtir`. Para una solicitud de compra confirmada sin dependencias activas, usa primero `accion=canalizar_stock`. Esta acción confirma una enmienda con el tipo nativo de inventario. Exige que todos los renglones tengan un SKU inventariable activo. La confirmación de la solicitud prepara el surtido. El inventario se mueve al confirmar el **Stock Entry**.

```json
{"modo":"transferencia","almacen_origen":"Central - L","almacen_destino":"Obra Norte - L"}
```

Para consumo usa `{"modo":"salida","almacen_origen":"Central - L"}`. La respuesta devuelve el folio de la enmienda.

```json
{"message":{"folio":"MAT-MR-2026-00005-1","tipo":"Material Request"}}
```

`esquema?accion=surtir&folio=MAT-MR-2026-00005-1` devuelve los renglones con saldo. Ejemplo de `message.rows[0]` para una transferencia:

```json
{"id":"mr-stock-item-a","item_code":"TOR-01","pending":4.0,"qty":0,"s_warehouse":"Central - L","t_warehouse":"Obra Norte - L","uom":"Nos","stock_uom":"Nos","conversion_factor":1.0}
```

Envía las cantidades parciales o totales en la unidad del renglón. ERPNext valida existencias y mantiene el saldo mediante `Stock Entry Detail.material_request` y `material_request_item`. Se requieren permisos de creación y envío de **Stock Entry**. Un renglón sin saldo o una cantidad superior al saldo devuelve HTTP `417`.

```json
{"rows":[{"id":"mr-stock-item-a","qty":2}]}
```

```json
{"message":{"folio":"MAT-STE-2026-00001","tipo":"Stock Entry"}}
```

La instalación de los campos de procedencia requiere `bench --site <sitio> migrate`. El surtido corresponde al movimiento físico. La reserva previa de existencias y la selección del solicitante desde un directorio quedan pendientes.

## 9. Entrada anticipada y regularización (`ALM-02`)

Ambas acciones usan `operaciones.esquema` y `operaciones.ejecutar` de §2.2. Requieren migración de campos y reconstrucción del backend. La entrada anticipada confirma un **Purchase Receipt** nativo. La configuración `Buying Settings.po_required` debe permitir recepción sin orden. Se conserva su validación nativa.

Para `accion=entrada_sin_oc`, usa `folio` vacío, UUID nuevo y estos `datos`. El precio por unidad de compra y el motivo son obligatorios. Los artículos deben ser activos, inventariables y de compra. El almacén debe ser activo, de detalle y de la empresa. Se aplican las unidades de compra del catálogo o la `uom` enviada con conversión válida. Las series, lotes e inspecciones conservan los requisitos nativos de ERPNext.

```json
{"company":"Lebaref","supplier":"Proveedor A","remarks":"Entrega anticipada pendiente de autorización","rows":[{"item_code":"TOR-01","qty":2,"rate":100,"warehouse":"Central - L","project":"PROJ-0001"}]}
```

```json
{"message":{"folio":"MAT-PRE-2026-00010","tipo":"Purchase Receipt"}}
```

El documento conserva `custom_lebaref_por_regularizar=1`. El esquema permite agregar renglones y seleccionar unidades, almacén, obra y partida. La sesión necesita permisos nativos de creación y envío de recepción y lectura de los maestros aplicables.

Para `accion=regularizar_entrada`, usa como `folio` esa recepción confirmada y envía una orden autorizada con el mismo proveedor, empresa y moneda. Cada `id` es un **Purchase Receipt Item** y cada `purchase_order_item` es un renglón de la orden seleccionada. Consulta sus IDs con `GET /api/resource/Purchase%20Order/<folio>` en `data.items[].name`. Relaciona todos los renglones de la recepción. Varios pueden referirse al mismo renglón de orden si su suma cabe en el saldo. Artículo, unidad, factor, obra y precio deben coincidir. La recepción debe regularizarse antes de crear facturas o devoluciones vinculadas.

```json
{"purchase_order":"PUR-ORD-2026-00010","motivo":"Orden autorizada después de la entrega","rows":[{"id":"pr-item-a","purchase_order_item":"po-item-a"}]}
```

```json
{"message":{"folio":"MAT-PRE-2026-00010","tipo":"Purchase Receipt"}}
```

La operación conserva folio, fecha, importes y valoración originales. Actualiza únicamente los vínculos a orden y solicitud y recalcula saldos con los agregadores nativos de ERPNext. Registra comentarios con usuario, fecha y motivo, y cambia `custom_lebaref_por_regularizar` a cero. Los libros de inventario y contabilidad conservan sus entradas originales. La sesión necesita escritura de recepción y de orden, y lectura de ambos documentos. La orden se bloquea durante la transacción para serializar recepciones concurrentes.

Un reintento idéntico devuelve el mismo folio con `repetida: true`. La regularización guarda su UUID y huella en campos propios, conservando la idempotencia del alta inicial. Una nueva operación sobre una entrada ya regularizada se rechaza. HTTP `417` para recepción ajena al flujo, orden cerrada o suspendida, referencias ajenas o repetidas, diferencias en datos o exceso de saldo. HTTP `403` para permisos insuficientes. Los fallos de regularización revierten todos sus cambios.

Antigravity debe conectar las acciones nuevas en Desk y desplegar la imagen integrada antes de su uso operativo.

## 10. Recepción directa a obra y servicios (`ALM-03`)

`accion=articulo` admite `tipo_articulo` con los valores `Material` y `Servicio`. Omitirlo conserva el alta inventariable habitual. Servicio crea un Item con `is_stock_item=0` y las unidades indicadas. El esquema de alta y consulta devuelve `tipo_articulo`. Los permisos de alta de catálogo se conservan. Un valor distinto devuelve HTTP `417`.

```json
{"item_code":"SRV-INST-01","item_name":"Servicio de instalación","item_group":"Services","stock_uom":"Nos","tipo_articulo":"Servicio"}
```

```json
{"message":{"folio":"SRV-INST-01","tipo":"Item"}}
```

El grupo y la unidad deben existir. Un servicio puede incluirse en solicitud, petición, oferta y orden mediante su SKU. Al recibirlo, ERPNext actualiza la cantidad recibida y mantiene el vínculo a la orden sin crear existencias.

`esquema?accion=recibir&folio=<orden>` incorpora campos opcionales `recibido_por`, `supplier_delivery_note` y `motivo_destino`. Las filas incorporan `warehouse` editable, `project` e `is_stock_item` informativos. Los servicios muestran almacén vacío. Para material, omitir `warehouse` conserva el destino de la orden. Un almacén distinto requiere una obra en el renglón de la orden y un motivo de cambio. La obra debe pertenecer a la empresa. El destino debe ser activo, de detalle, de la misma empresa y legible para la sesión. La selección del almacén de la obra es explícita en cada renglón.

```json
{"recibido_por":"Ana Pérez","supplier_delivery_note":"REM-123","motivo_destino":"Entrega del proveedor directamente en obra","rows":[{"id":"po-material-a","qty":2,"warehouse":"Obra Norte - L"},{"id":"po-servicio-b","qty":1}]}
```

```json
{"message":{"folio":"MAT-PRE-2026-00011","tipo":"Purchase Receipt"}}
```

La recepción conserva `custom_lebaref_recibido_por`, la remisión nativa `supplier_delivery_note`, y el motivo y destinos en `remarks`. Conserva obra, orden, unidades y partida del documento de origen. El material recibido en obra incrementa ese almacén. ERPNext puede completar el almacén de referencia del servicio con sus valores predeterminados. El servicio conserva `is_stock_item=0` y carece de movimiento de inventario. HTTP `417` para servicio con almacén enviado, destino inválido, cambio de almacén sin obra o motivo, o cantidades inválidas. Los permisos nativos, la recepción parcial y los reintentos de §2.2 se mantienen.

El nuevo campo de persona requiere migración. Para uso operativo hay que desplegar el backend integrado y conectar la captura en Desk. La selección de almacén representa el lugar de resguardo y conserva inventario hasta un movimiento nativo posterior de consumo.

## 11. Sustitución autorizada al recibir (`ALM-04`)

`accion=autorizar_sustitucion` usa el folio de una **Purchase Order** confirmada y abierta. La sesión requiere crear y confirmar **Lebaref Sustitucion**, además de escritura y autorización (`submit`) de esa orden. Se aprovisionan permisos de creación y confirmación para `Purchase Manager`, y de lectura para `Purchase User` y `Stock User`. El evento de validación también exige esos permisos de orden cuando se captura por REST nativo o Desk.

```json
{"renglon":"po-item-a","sustituto":"TOR-EQUIV-01","cantidad":2,"motivo":"Marca equivalente aprobada para entrega inmediata"}
```

```json
{"message":{"folio":"autorizacion-id","tipo":"Lebaref Sustitucion"}}
```

La autorización confirmada conserva `orden`, `renglon`, `original`, `sustituto`, `cantidad`, `unidad`, `factor`, `precio`, `motivo`, propietario e historial. Los datos del original, unidad, factor y precio se obtienen del servidor. El sustituto debe ser otro artículo activo, inventariable y de compra, con la misma unidad base y una conversión de compra idéntica. La cantidad autorizada debe caber en el saldo pendiente después de descontar las otras autorizaciones vigentes aún pendientes de recibir. Se conservan UUID y huella para reintentos.

`esquema?accion=recibir&folio=<orden>` añade la columna opcional `sustitucion` con las autorizaciones confirmadas de esa orden. En el POST normal de `recibir`, cada renglón puede indicar una autorización. Omitirla conserva la recepción del artículo original. Se puede recibir parcialmente dentro de la cantidad autorizada.

```json
{"rows":[{"id":"po-item-a","qty":1,"sustitucion":"autorizacion-id"}]}
```

```json
{"message":{"folio":"MAT-PRE-2026-00012","tipo":"Purchase Receipt"}}
```

En **Purchase Receipt Item**, `item_code` identifica el sustituto recibido, `custom_lebaref_articulo_original` conserva el artículo solicitado y `custom_lebaref_sustitucion` identifica la autorización. La recepción mantiene las referencias nativas `purchase_order` y `purchase_order_item`. ERPNext calcula el saldo sobre el renglón original y el inventario sobre el sustituto físico. El precio se conserva en la recepción y en la autorización. Esta versión exige el mismo precio y conversión de la orden; los cambios comerciales requieren ajustar la documentación de compra por el proceso correspondiente.

La extensión declarativa `extend_doctype_class` de **Purchase Receipt** y **Purchase Invoice** admite la diferencia de código exclusivamente al comprobar una autorización confirmada cuyo par original/sustituto, orden, renglón, cantidad, unidad, factor y precio coinciden. Las demás comparaciones y validaciones nativas se mantienen. La factura debe generarse desde la recepción confirmada y usar `update_stock=0`. Los campos de procedencia se copian a **Purchase Invoice Item**. Las devoluciones refieren el renglón de recepción original y restituyen el cupo recibido conforme al movimiento nativo. Una autorización con recepciones vigentes conserva su vínculo y rechaza la anulación.

HTTP `403` para un auxiliar que intenta autorizar. HTTP `417` para saldo o cantidad autorizada excedidos, unidades incompatibles, código, precio o referencias que difieran de la autorización, o una autorización cancelada. Hay que reconstruir el backend y migrar para instalar el DocType y los campos. Antigravity debe conectar el diálogo de autorización y el selector de recepción en Desk y desplegar la imagen integrada.

## 12. Consumo vinculado y saldo bajo resguardo (`ALM-05`)

`entregar` mantiene la transferencia nativa de Central a Obra. El nuevo `accion=consumir` usa como `folio` el **Stock Entry** de entrega original confirmada (`Material Transfer` con `custom_lebaref_origen` vacío). Requiere permisos nativos de creación y envío de Stock Entry y lectura del original. Cada renglón seleccionado debe identificar su obra en la entrega. `remarks` es obligatorio y describe el motivo o actividad.

`esquema?accion=consumir&folio=<entrega>` devuelve `remarks` y renglones con `id`, `item_code`, `pending`, `qty` y `uom`. La cantidad se captura en la unidad del renglón original. Se permiten cantidades parciales y se conserva el factor de conversión.

```json
{"remarks":"Instalación de soportes en obra","rows":[{"id":"entrega-item-a","qty":0.5}]}
```

```json
{"message":{"folio":"MAT-STE-2026-00020","tipo":"Stock Entry"}}
```

La escritura confirma un **Stock Entry** nativo `Material Issue`, desde el almacén donde quedó el material, conservando empresa, obra, partida y unidad. `custom_lebaref_origen` identifica la entrega y cada `Stock Entry Detail.custom_lebaref_renglon` identifica el renglón original. ERPNext valida existencias, calcula la valoración y contabiliza el gasto con la dimensión de obra y sus cuentas configuradas. La transferencia a obra conserva el inventario hasta ese consumo. El importe consumido se obtiene del movimiento nativo en la moneda de la empresa.

Consumos y devoluciones comparten el saldo de cada renglón. Se agregan cantidades de inventario y se convierten a la unidad del original. Por ejemplo, dos cajas de diez piezas permiten consumir media caja y devolver una, dejando media caja bajo resguardo. Otro inventario físico en el mismo almacén conserva su procedencia y no amplía el saldo de esta entrega. Una anulación nativa de consumo o devolución restituye su saldo. La entrega original rechaza su anulación mientras tenga movimientos vinculados confirmados.

`devolver` conserva el contrato anterior y ahora limita su cantidad al saldo después de consumos y devoluciones. Los eventos `Stock Entry.validate` y `before_cancel` comprueban también la ruta nativa de Desk/REST para referencias, artículo, almacén, obra, partida y saldo. La entrega de origen se bloquea y se usan lecturas actuales bloqueantes del saldo para serializar movimientos vinculados. Los reintentos de `ejecutar` conservan el UUID de §2.2.

`GET /api/method/lebaref_compras.consumos.consultar?entrega=<folio>` devuelve cantidades por renglón y exige lectura de la entrega. Ejemplo de respuesta para una sesión con permiso nativo de lectura de GL Entry.

```json
{"message":{"entrega":"MAT-STE-2026-00019","puede_ver_costos":true,"moneda":"MXN","rows":[{"id":"entrega-item-a","item_code":"TOR-01","uom":"Box","entregado":2.0,"consumido":0.5,"devuelto":1.0,"saldo":0.5,"warehouse":"Obra Norte - L","project":"PROJ-0001","costo_consumido":50.0}]}}
```

Para una sesión sin ese permiso, `puede_ver_costos` es `false` y las filas omiten `costo_consumido`. La consulta resume los movimientos vinculados a esa entrega. Los movimientos generales de inventario conservan los flujos nativos existentes. El consumo directo desde una solicitud de Material Issue sigue disponible mediante `surtir` (§8).

HTTP `403` por permisos insuficientes y HTTP `417` por referencia inválida, origen no confirmado o ajeno al flujo, obra o motivo ausentes, destino incoherente, o exceso del saldo compartido. El consumo conserva los controles nativos de existencias y contabilidad. Mermas y ajustes físicos se completarán en ALM-08. Antigravity debe desplegar backend y workers de la misma versión, conectar consumir y consultar en Desk y validar la impresión del consumo como Material Issue.

## 13. Sobrante histórico sin entrega original (`ALM-07`)

`accion=sobrante` crea un **Stock Entry en borrador**, con permiso nativo de creación. El responsable revisa procedencia, acta y existencias antes de confirmar. Captura cantidades en la unidad de inventario de cada SKU, con factor 1. La obra de procedencia, persona que entrega, motivo y referencia del acta son obligatorios.

Para material ya contabilizado, incluso por la migración, usa `tipo=Traslado`, un almacén de origen donde esté registrado y un destino distinto. La confirmación mueve las existencias existentes por Material Transfer y conserva su valoración nativa.

```json
{"company":"Lebaref","project":"PROJ-0001","referencia":"ACTA-SOB-001","persona":"Ana Pérez","motivo":"Sobrante recuperado al concluir la obra","tipo":"Traslado","from_warehouse":"Obra Norte - L","to_warehouse":"Central - L","rows":[{"item_code":"TOR-01","qty":3}]}
```

```json
{"message":{"folio":"MAT-STE-2026-00030","tipo":"Stock Entry"}}
```

Para una existencia física que quedó fuera del inventario contabilizado, usa `tipo=Entrada no contabilizada`, deja vacío el almacén de origen, proporciona `valor_unitario` positivo por unidad base en moneda de empresa y confirma exactamente `NO CONTABILIZADO`. La clasificación depende de la revisión física y conciliación con la migración por el responsable. El acta identifica el evento físico y su lote de renglones.

```json
{"company":"Lebaref","project":"PROJ-0001","referencia":"ACTA-SOB-002","persona":"Ana Pérez","motivo":"Material encontrado fuera del conteo inicial, revisado contra migración","tipo":"Entrada no contabilizada","to_warehouse":"Central - L","confirmacion":"NO CONTABILIZADO","rows":[{"item_code":"TOR-01","qty":2,"valor_unitario":20}]}
```

Guardar mantiene `docstatus=0`. `esquema?accion=autorizar_sobrante&folio=<borrador>` muestra los renglones y la confirmación. Para confirmar usa `accion=autorizar_sobrante`, el folio de borrador y estos datos.

```json
{"confirmar":"AUTORIZAR"}
```

```json
{"message":{"folio":"MAT-STE-2026-00030","tipo":"Stock Entry"}}
```

La autorización exige permisos nativos de envío de Stock Entry y Stock Reconciliation. Los eventos de Stock Entry exigen esa autoridad también al confirmar y anular el sobrante por Desk o REST nativos. Los traslados usan Material Transfer. Las entradas no contabilizadas usan Material Receipt y sus cuentas y valoración nativas. Los almacenes deben ser activos, de detalle y de la misma empresa. Los artículos deben ser activos e inventariables.

El documento conserva `custom_lebaref_tipo_sobrante`, `custom_lebaref_referencia_sobrante`, `custom_lebaref_persona_sobrante`, `custom_lebaref_confirmacion_sobrante` y `remarks`. `custom_lebaref_clave_sobrante` es una huella única por empresa y acta normalizada con espacios compactados y mayúsculas. Otro UUID con la misma referencia de un borrador o movimiento vigente se rechaza por esa restricción única. El acta debe agrupar todos sus renglones en un documento. Al anular el movimiento, ERPNext revierte inventario y contabilidad y se libera su clave para corregir el acta. El original conserva referencia e historial. La confirmación repetida conserva el folio y evita otro movimiento.

Un sobrante histórico usa este flujo cuando carece de entrega original vinculada. La devolución de una entrega conocida conserva `devolver`. Si el material ya está incluido en el almacén de destino, se conserva su evidencia en el expediente existente. Los sobrantes históricos no se seleccionan como entrega original en consumir o devolver.

HTTP `403` por falta de autoridad para confirmar o anular. HTTP `417` por datos de procedencia incompletos, unidad incompatible, almacenes inválidos o falta de verificación de stock nuevo. Un acta duplicada conserva el error nativo de clave única. Se conservan los errores nativos de existencias, series, lotes y contabilidad. Requiere reconstrucción y migración para instalar los campos y su índice único. Antigravity debe conectar la captura, revisión y autorización e identificar el tipo de sobrante en la impresión.

## 14. Conteo, ajuste y merma (`ALM-08`)

`accion=ajuste` crea un **Stock Reconciliation** en borrador y requiere permiso nativo de creación. `tipo` admite `Conteo` y `Merma`. `qty` es la cantidad física final en la unidad de inventario, incluido cero. Se capturan acta y motivo. La valoración se toma del Bin actual, o de `valuation_rate` si se proporciona. Un conteo positivo con existencia previa cero requiere valoración positiva explícita.

```json
{"company":"Lebaref","tipo":"Conteo","referencia":"ACTA-CON-001","motivo":"Conteo físico de anaquel A","rows":[{"item_code":"TOR-01","warehouse":"Central - L","qty":12}]}
```

```json
{"message":{"folio":"MAT-RECO-2026-00001","tipo":"Stock Reconciliation"}}
```

Para merma se captura la cantidad final menor que la observada y se conserva la valoración observada. Las revisiones de valoración se registran mediante Conteo. Por ejemplo, con diez piezas observadas y tres dañadas, se envía `tipo=Merma` y `qty=7`. El documento conserva tipo, motivo, acta, UUID y huella en `custom_lebaref_*`. Cada renglón guarda `custom_lebaref_observado` y `custom_lebaref_valor_observado`.

`accion=autorizar_ajuste` usa el folio de borrador y `datos={"confirmar":"AUTORIZAR"}`. Requiere permiso nativo de envío de Stock Reconciliation. Antes de confirmar se bloquean los Bin y se compara su cantidad y valoración actuales con la observación inicial. Un cambio exige nueva captura. La confirmación guiada fija fecha/hora actuales y deja que ERPNext contabilice la diferencia con sus cuentas configuradas. La referencia tiene clave única por empresa/acta. La cancelación conserva las reglas nativas y libera la clave para corregir el documento.

Los eventos validate/before_submit/before_cancel/on_cancel aplican también a la ruta nativa. HTTP `403` por permisos insuficientes. HTTP `417` por tipo o motivo inválido, merma que no reduce stock, datos de almacén/artículo inválidos, o conteo desactualizado. Referencias duplicadas conservan el error nativo de clave única. Los requisitos de series, lotes y valoración siguen el flujo de Stock Reconciliation.

## 15. Conciliación y factura de comprobantes (`XML-02 / XML-03 / XML-04`)

El alcance contable adoptado es **Purchase Receipt → Purchase Invoice → Payment Entry** nativos, con permisos financieros existentes. Cada conciliación de esta API cubre un CFDI y una recepción completa. La selección identifica conceptos XML por su posición estable y renglones por su ID de ERPNext. Se revisan moneda, RFC, huella del archivo privado, cantidades, importes netos por renglón y total. Las diferencias monetarias usan la precisión de la recepción. La validación estructural y conciliación quedan registradas en **Lebaref Conciliacion CFDI**, con propietario, fecha, selección y resultado inmutables. La comprobación de vigencia SAT y firma fiscal requiere su proceso fiscal correspondiente.

`GET /api/method/lebaref_compras.comprobantes.consultar?cfdi=<UUID>` devuelve `conceptos`, `renglones`, `cargos` fijos nativos, `moneda`, `fecha_xml`, `total_xml`, `total_recepcion` y `diferencia_total`. Para previsualizar una selección se añade `seleccion` como JSON con este formato. `factor_xml` convierte cantidad XML a unidad de inventario de la recepción. Cada concepto selecciona un `renglon` o un `cargo`, exclusivamente. Cargo identifica una fila de impuestos/cargos nativos con `charge_type=Actual`, útil cuando el flete aparece como concepto XML.

```json
{"rows":[{"concepto":"1","renglon":"pr-item-a","factor_xml":10},{"concepto":"2","cargo":"pr-tax-flete"}]}
```

La respuesta añade `seleccion`, `diferencias` por cantidad/importe y `coincide`. Con `coincide=false` se revisa el expediente antes de continuar. Todos los conceptos deben estar relacionados y todos los renglones de recepción deben cuadrar. Varios conceptos pueden acumularse en un renglón. Los factores equivalentes, como 1 y 1.00, se normalizan.

`accion=conciliar_cfdi` usa como `folio` el UUID del CFDI archivado y como `datos` esa selección. Requiere crear Lebaref Conciliacion CFDI. Se rechazan diferencias y la conciliación repetida equivalente devuelve el folio existente.

```json
{"message":{"folio":"conciliacion-id","tipo":"Lebaref Conciliacion CFDI"}}
```

`accion=facturar_cfdi` usa como `folio` el UUID conciliado. `datos` admite `fecha` opcional para la fecha del comprobante y se propone la fecha del XML cuando está disponible. La API usa el mapeador nativo de recepción a factura y exige que la recepción carezca de otra factura activa. Devuelve una **Purchase Invoice en borrador**. La factura conserva `custom_lebaref_tipo_comprobante=CFDI`, `custom_lebaref_cfdi`, `custom_lebaref_recepcion_comprobante`, `bill_no` y su clave de comprobante. La creación requiere permiso nativo de Purchase Invoice.

```json
{"fecha":"2026-11-20"}
```

```json
{"message":{"folio":"ACC-PINV-2026-00001","tipo":"Purchase Invoice"}}
```

Para una compra con nota o ticket, `accion=nota_compra` usa el folio de la recepción confirmada y estos datos. Crea igualmente una Purchase Invoice en borrador, con tipo `Nota/ticket`, referencia y motivo, y conserva la recepción. La referencia se normaliza y su clave es única por empresa/proveedor/tipo/referencia. Los archivos de soporte pueden adjuntarse al expediente mediante File nativo.

```json
{"referencia":"TICKET-001","motivo":"Compra documentada con nota de mostrador","fecha":"2026-11-20"}
```

`accion=autorizar_factura` usa el folio de factura y `datos={"confirmar":"AUTORIZAR"}`. Exige permiso nativo de envío de Purchase Invoice. Los validadores financieros comparan cantidades e importes con la recepción y comprueban la conciliación vigente del CFDI. La factura mantiene `update_stock=0` y evita otra recepción de inventario. Los activos fijos configurados en Item conservan el proceso nativo de ERPNext y las referencias de recepción. La creación automática de fichas personalizadas HER no forma parte de este flujo contable.

La anulación nativa de factura libera su clave de comprobante para corrección y conserva el UUID/referencia e historial. Las notas de crédito nativas conservan `return_against` y su propio tratamiento fiscal; los marcadores de comprobante de ingreso se limpian al generarlas. El test comprueba una nota de crédito nativa desde una factura documentada con nota.

## 16. Pago y saldo de factura (`XML-04`)

`accion=pagar_factura` usa una Purchase Invoice confirmada con saldo. Crea un **Payment Entry en borrador** mediante `get_payment_entry` nativo. `importe` está en la moneda de la cuenta por pagar del proveedor, coherente con `outstanding_amount`; `cuenta` es una cuenta activa Bank o Cash de la misma empresa, visible para la sesión. ERPNext aplica sus conversiones y configuración contable. `referencia` y `fecha` identifican el comprobante del pago.

```json
{"importe":120,"cuenta":"Bancos - L","referencia":"TRANSFERENCIA-001","fecha":"2026-11-20"}
```

```json
{"message":{"folio":"ACC-PAY-2026-00001","tipo":"Payment Entry"}}
```

La captura exige crear Payment Entry. `accion=autorizar_pago`, con el folio del pago y `datos={"confirmar":"PAGAR"}`, requiere permiso nativo de envío. Antes de confirmar se bloquea la factura y se verifica su saldo actual, proveedor, empresa y asignación única. Un pago preparado antes de otro que reduzca el saldo se rechaza si excede el remanente. `custom_lebaref_factura` conserva el vínculo. El saldo y la cancelación se resuelven por las entradas contables y de proveedor nativas. `GET /api/resource/Purchase%20Invoice/<folio>` expone `outstanding_amount` y `status` actualizados.

Reintentos de creación con el mismo UUID conservan el borrador y las autorizaciones repetidas conservan el documento confirmado. HTTP `403` por permisos contables insuficientes. HTTP `417` por diferencia de conciliación, comprobante/recepción incoherentes, factura ya vinculada, cuenta inválida, referencia ausente o exceso del saldo actual. Los requisitos contables adicionales conservan sus errores nativos.

Estos módulos requieren reconstrucción, migración y despliegue coherente de backend/workers. Se agregan permisos de lectura del archivo CFDI para Accounts User y Accounts Manager. La conexión de diálogos, selección de conceptos/cargos, revisión contable e impresión corresponde a Antigravity. Los perfiles contables y cuentas reales deben validarse en el recorrido operativo.
