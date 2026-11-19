# Kits, garantías, mantenimiento y etiquetas

Entrega local de pruebas del 19 de noviembre de 2026. La interfaz es la aplicación
nativa de Windows; todos los movimientos se guardan en ERPNext.

## Permisos

| Acción | Responsable | Almacén | Compras |
|---|---|---|---|
| Consultar, buscar código y generar etiqueta | Sí | Sí | Sí |
| Crear kit y cambiar su composición | Sí | No | No |
| Prestar kit y registrar retornos | Sí | Sí | No |
| Registrar costo y garantía | Sí | No | No |
| Registrar mantenimiento de herramienta | Sí | Sí | No |
| Anular mantenimiento con motivo | Sí | No | No |

## Kits

1. El responsable registra las herramientas individuales y crea un kit desde
   el selector de operaciones, con empresa, nombre, ubicación y herramientas.
2. En «Kits de herramientas», seleccionar el kit y consultar su detalle.
   Se muestran estados actuales, disponibilidad e historial de préstamos.
3. Abrir la operación de préstamo de kit, capturar obra y personas responsables,
   y revisar «Incluir en préstamo» en cada renglón. Solo salen los marcados «Sí».
4. Registrar las devoluciones desde el préstamo generado, como en herramientas.
5. Para cambiar el contenido, elegir la operación de composición y un motivo.
   Primero deben regresar todas las herramientas prestadas del contenido actual.

Una herramienta no puede pertenecer a dos kits. El servidor vuelve a comprobar
su disponibilidad al guardar y rechaza capturas si cambió la composición.
Cambiar el contenido conserva el historial; no borra los préstamos anteriores.
Un kit puede quedar vacío para desarmarlo, pero no se presta vacío.

## Garantías y costos

En «Herramientas», seleccionar la ficha y abrir la operación de garantía/costo.
Registrar fecha de compra, costo, referencia y motivo. Si tiene garantía,
indicar «Sí» y su vencimiento. Cero significa costo de adquisición no registrado.
La moneda corresponde a la empresa. El plazo es un registro interno: no verifica
la cobertura con el proveedor ni genera asientos contables.

Para cada mantenimiento, registrar fecha, taller/proveedor, comprobante, importe
y trabajo realizado. Se admite costo cero. Si se marca «bajo garantía», la fecha
debe estar dentro de la garantía registrada. El mantenimiento no modifica la
custodia ni libera automáticamente una herramienta dañada.

El detalle suma los mantenimientos vigentes y muestra su porcentaje respecto al
costo de adquisición. A partir de 50% presenta un aviso para revisar la conveniencia
de reparar. No es depreciación contable. No mezcla monedas distintas.

Para corregir un gasto, el responsable selecciona el registro en «Mantenimientos
de herramientas» y lo anula indicando el motivo. Se conserva el registro y se
excluye del acumulado; después puede recapturarse el mismo comprobante corregido.
Esta anulación corresponde a herramientas, no a gastos de flotilla.

## Buscar e imprimir etiquetas

En herramientas, vehículos o kits, pegar o escribir el código exacto en «Código
exacto» y pulsar Enter o «Buscar código». Consulta directamente el registro,
aunque no figure en los 100 documentos recientes. «Actualizar» vuelve al listado.
Un lector configurado como teclado puede introducir el código y Enter; falta
comprobar el lector físico de la oficina.

Seleccionar el registro y pulsar «Etiqueta». Se abre la vista previa, con botones
para guardar PNG o elegir impresora. Code 128 conserva el identificador exacto;
admite de 1 a 30 caracteres ASCII imprimibles. Un código incompatible se rechaza,
no se trunca ni sustituye. Los textos descriptivos sí pueden abreviarse visualmente.

La imagen tiene 1200 × 450 píxeles a 300 ppp: 10.16 × 3.81 cm. La impresión necesita
al menos esa área útil. Comprobar papel, márgenes y lectura en la impresora real.
Se verificaron imágenes con un decodificador independiente; no se envió una
impresión física ni se ensayó un escáner físico.

## Ejemplo disponible

En el sitio local, buscar `KIT-DEMO-1119104521`. Contiene dos herramientas,
una disponible y una prestada. El préstamo activo es `32l3f47307`.
La herramienta `DEMO-KH-1119104521-1` tiene adquisición de 1,000 MXN y un
mantenimiento corregido de 200 MXN (20%); el registro previo anulado se conserva.
Son datos ficticios. `Ejemplo-Etiqueta-Kit.png` corresponde al mismo kit.

## Alcance

Esta ampliación está desplegada en el servidor de pruebas y en el cliente de
Windows entregado. No migra el histórico SQLite, no incorpora fotos o firmas,
ni configura la red de oficina. Antes de compartirlo entre estaciones hacen
falta el servidor privado común, HTTPS, cuentas individuales y la restricción
de acceso de red. No se abrió acceso externo desde esta PC.
