# Contrato de API — Servidor ERPNext (`lebaref_compras`) ↔ Cliente Windows

> **Regla de colaboración:** Cada vez que **Codex** agregue o modifique un endpoint `@frappe.whitelist` en `integracion_erpnext/app/lebaref_compras/`, debe actualizar este documento con los parámetros de entrada, estructura JSON de respuesta y errores de validación. **Antigravity** usará este contrato como única fuente de verdad al programar las pantallas en C#.

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
  - Acciones compras/almacén/maestros: `articulo`, `proveedor`, `obra`, `almacen`, `solicitud`, `peticion`, `oferta`, `autorizar`, `recibir`, `entregar`, `devolver`, `cfdi`.
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

## 7. Cambios de Contrato Planificados (Bloque 1)

- **`[SOL-03 / SOL-05]` Flujo de solicitudes:**
  - Estado borrador, anexos, origen de captura y aprobación de surtido desde stock.
- **`[SOL-02 / CAT-04]` Equivalencia pendiente:**
  - Vínculo posterior de un concepto libre a un SKU definitivo y selección del solicitante desde un directorio.
- **`[COM-04 / COM-07]` Impuestos, flete y condiciones de pago:**
  - Exponer tasa de IVA, cargo por flete/envío, condición de pago y fecha programada en `oferta`, `comparativa.consultar` y `Purchase Order`.
