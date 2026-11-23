# Seguimiento de solicitudes internas de LEBAREF

El programador de Frappe revisa las solicitudes de compra confirmadas y canceladas cada cinco minutos. El worker reconcilia la versión actual de cada solicitud y guarda su snapshot en **Lebaref Sync Solicitud** antes de hacer la llamada HTTPS. Si falla la conexión o la confirmación, reintenta el mismo cuerpo con una nueva firma. La espera aumenta de 5 minutos hasta 4 horas. Cuando cambia un registro pendiente, el worker guarda una versión nueva con el estado más reciente. El guardado de documentos de compra no realiza llamadas externas.

El ERP es el origen. Apps Script actualiza [General](https://docs.google.com/spreadsheets/d/12tPuqb-CCSp7Ruhvj46kDtQcRO_Lay_3vKmF1kaxT6c/edit), crea o recupera el expediente bajo [Solicitudes](https://drive.google.com/drive/folders/1Ayp_pXEsWs5ko7njSA6raHT3Ap9eU57P) y alimenta la ficha.

## Selección de documentos

- Se conserva el folio y la fecha legados registrados en la nota de migración. Para la serie nativa `MAT-MR-AAAA-NNNNN` se usa `SOL-AAAA-8NNNNNNN`, con el número completado a siete dígitos. Si dos solicitudes comparten un folio, se omiten ambas. Tras identificar el registro correcto en ERPNext, `excluir_solicitudes` admite el nombre ERPNext del duplicado que se debe omitir.
- Solo se procesan solicitudes de compra de la empresa configurada con un departamento mapeado. Se vinculan las ofertas por `Supplier Quotation Item.material_request_item`.
- El total de una oferta se replica si está en MXN y todas sus partidas pertenecen a ese folio. En los demás casos se conserva la oferta con importe vacío. El enlace de documento se toma de un adjunto de Drive vinculado a la oferta. La carga de binarios y el traslado de expedientes entre gerencias requieren un flujo distinto.
- Si una solicitud publicada se elimina de ERPNext, el registro queda conservado en Google. La cancelación se refleja mientras la solicitud cancelada permanezca disponible.

## Puesta en marcha

1. Desplegar la misma versión de la app en backend, worker y programador. Ejecutar `bench --site <sitio> migrate` y habilitar el programador con `bench --site <sitio> enable-scheduler`. El entorno Docker versionado incluye el servicio `scheduler`.
2. En el proyecto existente de Apps Script, utilizar la implementación web receptora, ejecutada como propietario y con acceso para el servidor que envía las solicitudes. La ficha usa su propio despliegue con control por correo. Una prueba HTTP sin firma debe devolver el JSON de rechazo del receptor. Una respuesta HTML 401 de Google indica que la solicitud se detuvo antes de llegar a `doPost`. Confirmar en las propiedades del proyecto los IDs del tablero y las carpetas antes de habilitar el envío.
3. Configurar URL y secreto compartido en el worker mediante `LEBAREF_SYNC_URL` y `LEBAREF_SYNC_SECRET`. También se admiten `lebaref_sync_url` y `lebaref_sync_secret` en la configuración privada del sitio. El secreto es el mismo `SYNC_SECRET` de Apps Script y debe tener al menos 32 caracteres.
4. Configurar `lebaref_seguimiento` en el sitio. Ejemplo para el ERP de pruebas, inicialmente deshabilitado:

       {
         "lebaref_seguimiento": {
           "habilitado": false,
           "empresa": "Lebaref Pruebas",
           "gerencias": {
             "Electricidad - LBP": "Eléctrico",
             "HVAC - LBP": "HVAC",
             "Obra Civil - LBP": "Obra Civil"
           }
         }
       }

5. Revisar los folios elegibles con `bench --site <sitio> execute lebaref_compras.seguimiento_google.vista_previa --args '["<empresa>", {"<departamento ERP>":"<gerencia Google>"}]'`. Para la primera entrega, `solo_solicitudes` puede contener el nombre ERPNext de un único folio real ya validado. Cambiar `habilitado` a `true`, verificar que aparece una sola vez en General y que el expediente y la ficha abren, y retirar la lista para la carga restante.

El identificador, la versión, el estado, los intentos y el próximo reintento se consultan en **Lebaref Sync Solicitud** con rol de administrador del sistema. No eliminar la cola al reinstalar la app, ya que contiene las versiones aceptadas por Google. Una versión remota diferente se conserva como error pendiente para revisar el estado de `_Datos`.

Al marcar una solicitud **En espera**, General muestra **En espera** y el siguiente paso contiene **En espera de: <detalle>**. La cola conserva el folio y la versión cuando la solicitud de ERPNext pasa del nombre anterior al folio oficial, siempre que el documento con el nombre anterior ya no exista. Los cambios en el estado o el detalle producen una versión nueva.

La prueba automatizada `lebaref_compras.verificar_seguimiento_google.verificar` usa respuestas locales y verifica firma, reintento y confirmación por versión.

## Adjuntos de expedientes en Windows

Desde el repositorio en el equipo que ejecuta ERPNext y tiene Google Drive para escritorio, abre `Activar_Sync_Expedientes_Drive.cmd` con la cuenta de Windows que tiene la unidad de Drive montada. El instalador registra el lanzador silencioso con rutas relativas al repositorio. Una instalación nueva ejecuta la tarea cada cinco minutos durante la sesión de Windows. Si la tarea ya existe, el instalador actualiza su acción y conserva la cuenta y los horarios registrados.

La acción usa `wscript.exe` y espera la salida de PowerShell sin abrir una consola. El resultado queda en el Programador de tareas y el detalle en `expedientes_drive.log`, en la carpeta superior del repositorio. Ejecuta el instalador nuevamente si cambias de ubicación el repositorio.
