"""Esquemas del registro operativo de herramientas y flotilla."""
import frappe


def field(name, label, kind="Data", **options):
    return dict(fieldname=name, label=label, fieldtype=kind, **options)


def link(name, label, target, **options):
    return field(name, label, "Link", options=target, **options)


def schemas():
    return {
        "Lebaref Herramienta": [
            field("codigo", "Código individual", reqd=1, unique=1),
            link("company", "Empresa", "Company", reqd=1), link("articulo", "Artículo", "Item", reqd=1),
            field("serie", "Número de serie"), field("marca_modelo", "Marca y modelo"),
            field("ubicacion", "Ubicación / gabinete", reqd=1),
            field("estado", "Estado", "Select", options="Disponible\nPrestada\nDañada\nIncompleta\nExtraviada\nBaja", reqd=1),
            field("responsable", "Responsable actual"), link("obra", "Obra actual", "Project"),
            link("prestamo", "Préstamo actual", "Lebaref Prestamo"), field("notas", "Notas", "Small Text"),
            field("fecha_compra", "Fecha de compra", "Date"), field("fin_garantia", "Garantía hasta", "Date"),
            link("moneda", "Moneda", "Currency"), field("costo_adquisicion", "Costo de adquisición", "Currency", options="moneda"),
        ],
        "Lebaref Prestamo Item": [
            link("herramienta", "Herramienta", "Lebaref Herramienta", reqd=1),
            field("estado", "Retorno", "Select", options="Pendiente\nBueno\nDañada\nIncompleta\nExtraviada", reqd=1),
            field("fecha_retorno", "Fecha de retorno", "Date"), field("observaciones", "Observaciones", "Small Text"),
        ],
        "Lebaref Prestamo": [
            link("company", "Empresa", "Company", reqd=1), field("fecha", "Fecha de salida", "Date", reqd=1),
            field("compromiso", "Fecha compromiso", "Date", reqd=1), field("responsable", "Responsable", reqd=1),
            field("recibe", "Persona que recibe", reqd=1), field("departamento", "Departamento"),
            link("obra", "Obra", "Project", reqd=1), field("estado", "Estado", reqd=1),
            field("items", "Herramientas", "Table", options="Lebaref Prestamo Item", reqd=1),
            field("notas", "Notas", "Small Text"),
            link("kit", "Kit de origen", "Lebaref Kit"),
        ],
        "Lebaref Retorno Item": [
            field("renglon", "Renglón de préstamo", reqd=1), link("herramienta", "Herramienta", "Lebaref Herramienta", reqd=1),
            field("estado", "Condición", reqd=1), field("observaciones", "Observaciones", "Small Text"),
        ],
        "Lebaref Retorno": [
            link("prestamo", "Préstamo", "Lebaref Prestamo", reqd=1), field("fecha", "Fecha", "Date", reqd=1),
            field("recibe", "Recibe en almacén", reqd=1), field("items", "Retornos", "Table", options="Lebaref Retorno Item", reqd=1),
        ],
        "Lebaref Estado Herramienta": [
            link("herramienta", "Herramienta", "Lebaref Herramienta", reqd=1), field("fecha", "Fecha", "Date", reqd=1),
            field("estado_anterior", "Estado anterior"), field("estado", "Nuevo estado", reqd=1),
            field("ubicacion", "Ubicación", reqd=1), field("motivo", "Motivo", "Small Text", reqd=1),
        ],
        "Lebaref Vehiculo": [
            field("codigo", "Código", reqd=1, unique=1), field("placas", "Placas", reqd=1, unique=1),
            link("company", "Empresa", "Company", reqd=1), field("marca_modelo", "Marca y modelo", reqd=1),
            field("responsable", "Responsable habitual", reqd=1),
            field("km_inicial", "Kilometraje inicial", "Int", reqd=1), field("km_actual", "Kilometraje actual", "Int", reqd=1),
            field("fecha_lectura", "Fecha última lectura", "Date", reqd=1),
            field("intervalo", "Intervalo de servicio (km)", "Int", reqd=1), field("proximo_servicio", "Próximo servicio (km)", "Int", reqd=1),
            field("ultimo_servicio", "Último servicio (km)", "Int"), field("capacidad", "Capacidad de tanque (L)", "Float", reqd=1),
            field("combustible", "Combustible", reqd=1), field("seguro", "Vigencia de seguro", "Date", reqd=1),
            field("estado", "Estado", "Select", options="Activo\nEn taller\nFuera de servicio", reqd=1),
        ],
        "Lebaref Gasto Vehiculo": [
            link("vehiculo", "Vehículo", "Lebaref Vehiculo", reqd=1), field("fecha", "Fecha", "Date", reqd=1),
            field("tipo", "Tipo de gasto", reqd=1), field("km", "Kilometraje", "Int", reqd=1),
            field("litros", "Litros", "Float"), field("tanque_lleno", "Tanque lleno", "Data"),
            link("moneda", "Moneda", "Currency", reqd=1), field("monto", "Importe total", "Currency", options="moneda", reqd=1),
            field("proveedor", "Proveedor / taller", reqd=1), field("referencia", "Folio de comprobante", reqd=1),
            field("clave_referencia", "Identificador de comprobante", unique=1, hidden=1),
            link("obra", "Obra", "Project"), field("descripcion", "Descripción", "Small Text", reqd=1),
        ],
        "Lebaref Revision Vehiculo": [
            link("vehiculo", "Vehículo", "Lebaref Vehiculo", reqd=1), field("fecha", "Fecha", "Date", reqd=1),
            field("km", "Kilometraje", "Int", reqd=1), field("inspector", "Inspector", reqd=1),
            field("nivel", "Nivel de combustible", reqd=1), field("llantas", "Llantas", reqd=1),
            field("fluidos", "Niveles / fluidos", reqd=1), field("frenos_luces", "Frenos / luces", reqd=1),
            field("carroceria", "Carrocería / limpieza", reqd=1), field("observaciones", "Observaciones", "Small Text"),
        ],
        "Lebaref Estado Vehiculo": [
            link("vehiculo", "Vehículo", "Lebaref Vehiculo", reqd=1), field("fecha", "Fecha", "Date", reqd=1),
            field("estado_anterior", "Estado anterior"), field("estado", "Estado", reqd=1),
            field("responsable", "Responsable", reqd=1), field("seguro", "Seguro vigente hasta", "Date", reqd=1),
            field("motivo", "Motivo", "Small Text", reqd=1),
        ],
        "Lebaref Kit Item": [link("herramienta", "Herramienta", "Lebaref Herramienta", reqd=1)],
        "Lebaref Kit": [
            field("codigo", "Código", reqd=1, unique=1), field("nombre", "Nombre", reqd=1),
            link("company", "Empresa", "Company", reqd=1), field("especialidad", "Especialidad"),
            field("ubicacion", "Ubicación", reqd=1), field("items", "Contenido", "Table", options="Lebaref Kit Item"),
            link("ultimo_prestamo", "Último préstamo", "Lebaref Prestamo"),
        ],
        "Lebaref Composicion Kit": [
            link("kit", "Kit", "Lebaref Kit", reqd=1), field("fecha", "Fecha", "Date", reqd=1),
            field("motivo", "Motivo", "Small Text", reqd=1), field("anterior", "Contenido anterior", "Small Text"),
            field("items", "Nuevo contenido", "Table", options="Lebaref Kit Item"),
        ],
        "Lebaref Garantia Herramienta": [
            link("herramienta", "Herramienta", "Lebaref Herramienta", reqd=1), field("fecha", "Registrado el", "Date", reqd=1),
            field("fecha_compra", "Fecha de compra", "Date", reqd=1), field("fin_garantia", "Garantía hasta", "Date"),
            link("moneda", "Moneda", "Currency", reqd=1), field("costo_adquisicion", "Costo de adquisición", "Currency", options="moneda"),
            field("referencia", "Referencia / comprobante", reqd=1), field("motivo", "Motivo", "Small Text", reqd=1),
            field("anterior", "Datos anteriores", "Small Text"),
        ],
        "Lebaref Mantenimiento Herramienta": [
            link("herramienta", "Herramienta", "Lebaref Herramienta", reqd=1), field("fecha", "Fecha", "Date", reqd=1),
            field("proveedor", "Proveedor / taller", reqd=1), field("referencia", "Folio de comprobante", reqd=1),
            field("clave_referencia", "Clave comprobante", hidden=1, unique=1),
            link("moneda", "Moneda", "Currency", reqd=1), field("monto", "Costo pagado", "Currency", options="moneda"),
            field("bajo_garantia", "Atendido bajo garantía", "Select", options="No\nSí", reqd=1),
            field("motivo", "Trabajo / motivo", "Small Text", reqd=1), field("anulado", "Anulado", "Check", default="0"),
        ],
        "Lebaref Anulacion Mantenimiento": [
            link("mantenimiento", "Mantenimiento", "Lebaref Mantenimiento Herramienta", reqd=1),
            link("herramienta", "Herramienta", "Lebaref Herramienta", reqd=1), field("fecha", "Fecha", "Date", reqd=1),
            field("motivo", "Motivo de anulación", "Small Text", reqd=1),
        ],
    }


def configurar():
    definitions = schemas()
    # First create all types, then resolve their mutual Link/Table references.
    for name in definitions:
        if not frappe.db.exists("DocType", name):
            frappe.get_doc(dict(doctype="DocType", name=name, module="Lebaref Compras", custom=1,
                                istable=int(name.endswith(" Item")), fields=[field("descripcion", "Descripción")])).insert()
    for name, fields in definitions.items():
        doc = frappe.get_doc("DocType", name)
        child = name.endswith(" Item")
        if not child:
            fields = fields + [field("operacion", "Operación", unique=1, read_only=1, no_copy=1), field("huella", "Huella", hidden=1, read_only=1)]
            doc.autoname = "field:codigo" if name in ("Lebaref Herramienta", "Lebaref Vehiculo", "Lebaref Kit") else "hash"
            doc.track_changes = 1
            masters = name in ("Lebaref Herramienta", "Lebaref Vehiculo", "Lebaref Kit")
            manager_only = masters or name.startswith("Lebaref Estado") or name in ("Lebaref Composicion Kit", "Lebaref Garantia Herramienta", "Lebaref Anulacion Mantenimiento")
            doc.set("permissions", [
                dict(role=role, read=1, select=1, create=int(role in ("Purchase Manager", "System Manager") or (role == "Stock User" and not manager_only)),
                     write=int((role in ("Purchase Manager", "System Manager", "Stock User") and name in ("Lebaref Herramienta", "Lebaref Vehiculo", "Lebaref Prestamo", "Lebaref Kit")) or (role in ("Purchase Manager", "System Manager") and name == "Lebaref Mantenimiento Herramienta")),
                     delete=0, submit=0, cancel=0, amend=0, export=1, **{"import": 0})
                for role in ("Purchase User", "Purchase Manager", "Stock User", "System Manager")
            ])
        doc.set("fields", fields)
        doc.save()
    frappe.clear_cache()
