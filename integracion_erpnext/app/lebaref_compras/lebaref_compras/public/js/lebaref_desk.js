(() => {
    const SHEETS_URL = "https://docs.google.com/spreadsheets/d/1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I/edit?gid=0#gid=0";

    function nuevoUuid() {
        if (window.crypto && typeof window.crypto.randomUUID === "function") {
            return window.crypto.randomUUID();
        }
        const bytes = new Uint8Array(16);
        window.crypto.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;
        const hex = [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }

    async function abrirOperacion(accion, folio = "", tituloPersonalizado = "", alTerminar = null) {
        const { message: schema } = await frappe.call({
            method: "lebaref_compras.operaciones.esquema",
            type: "GET",
            args: { accion, folio: folio || "" },
            freeze: true,
            freeze_message: "Cargando operación…",
        });

        const tokenOperacion = nuevoUuid();
        const fileContents = {};
        const dialogFields = [];

        if (schema.note) {
            dialogFields.push({
                fieldname: "_leb_note",
                fieldtype: "HTML",
                options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">${frappe.utils.escape_html(schema.note)}</div>`,
            });
        }

        for (const f of schema.fields || []) {
            if (f.kind === "file") {
                dialogFields.push({
                    fieldname: "_file_html_" + f.name,
                    fieldtype: "HTML",
                    options: `
                        <div class="form-group">
                            <label class="control-label">${frappe.utils.escape_html(f.label)}</label>
                            <input type="file" accept=".xml,text/xml" class="form-control leb-xml-input" data-fname="${frappe.utils.escape_html(f.name)}" />
                        </div>`,
                });
            } else if (Array.isArray(f.choices) && f.choices.length) {
                dialogFields.push({
                    fieldname: f.name,
                    label: f.label,
                    fieldtype: "Select",
                    options: f.choices.join("\n"),
                    default: f.value || f.choices[0] || "",
                });
            } else if (f.kind === "date") {
                dialogFields.push({
                    fieldname: f.name,
                    label: f.label,
                    fieldtype: "Date",
                    default: f.value || frappe.datetime.get_today(),
                });
            } else if (f.kind === "multiline") {
                dialogFields.push({
                    fieldname: f.name,
                    label: f.label,
                    fieldtype: "Small Text",
                    default: f.value || "",
                });
            } else {
                dialogFields.push({
                    fieldname: f.name,
                    label: f.label,
                    fieldtype: "Data",
                    default: f.value || "",
                });
            }
        }

        const hasTable = Array.isArray(schema.columns) && schema.columns.length > 0;
        if (hasTable) {
            dialogFields.push({ fieldname: "_leb_rows_html", fieldtype: "HTML" });
        }

        const dialog = new frappe.ui.Dialog({
            title: tituloPersonalizado || `Operación Lebaref · ${accion.toUpperCase()}${folio ? " (" + folio + ")" : ""}`,
            size: hasTable ? "extra-large" : "large",
            fields: dialogFields,
        });

        // Enlazar lectura de archivo XML local cuando aplique (CFDI 4.0)
        dialog.$wrapper.find(".leb-xml-input").on("change", function () {
            const fname = $(this).data("fname");
            const file = this.files && this.files[0];
            if (!file) {
                fileContents[fname] = "";
                return;
            }
            const reader = new FileReader();
            reader.onload = (ev) => {
                fileContents[fname] = String(ev.target.result || "");
            };
            reader.readAsText(file, "UTF-8");
        });

        // Construir tabla interactiva conservando metadatos originales de cada renglón (uom, stock_uom, conversion_factor, id)
        const rowStates = (schema.rows || []).map((r) => Object.assign({}, r));

        function crearFilaVacia() {
            const obj = {};
            for (const col of schema.columns || []) {
                obj[col.name] = Array.isArray(col.choices) && col.choices.length ? (col.choices[0] || "") : "";
            }
            return obj;
        }

        if (hasTable && rowStates.length === 0 && schema.add_rows) {
            rowStates.push(crearFilaVacia());
        }

        function renderRowsTable() {
            if (!hasTable) return;
            const host = dialog.fields_dict._leb_rows_html.$wrapper;
            host.empty();

            const wrap = $('<div class="table-responsive mt-2">').appendTo(host);
            const tbl = $('<table class="table table-bordered table-sm" style="font-size:12px;">').appendTo(wrap);
            const thead = $('<thead style="background:#f1f5f9;">').appendTo(tbl);
            const hr = $("<tr>").appendTo(thead);
            for (const col of schema.columns) {
                $("<th>").text(col.label).appendTo(hr);
            }
            if (schema.add_rows) $("<th>").css("width", "42px").appendTo(hr);

            const tbody = $("<tbody>").appendTo(tbl);
            rowStates.forEach((rowObj, rIdx) => {
                const tr = $("<tr>").appendTo(tbody);
                for (const col of schema.columns) {
                    const td = $("<td>").appendTo(tr);
                    const curVal = rowObj[col.name] !== undefined && rowObj[col.name] !== null ? String(rowObj[col.name]) : "";
                    if (!col.editable) {
                        td.css({ background: "#f8fafc", verticalAlign: "middle" }).text(curVal);
                    } else if (Array.isArray(col.choices) && col.choices.length) {
                        const sel = $('<select class="form-control form-control-sm">').appendTo(td);
                        if (!col.choices.includes("")) {
                            $("<option>").val("").text("—").appendTo(sel);
                        }
                        for (const ch of col.choices) {
                            $("<option>").val(ch).text(ch).prop("selected", ch === curVal).appendTo(sel);
                        }
                        sel.on("change", function () {
                            rowObj[col.name] = $(this).val();
                        });
                    } else {
                        const inp = $('<input type="text" class="form-control form-control-sm">').val(curVal).appendTo(td);
                        inp.on("input", function () {
                            rowObj[col.name] = $(this).val();
                        });
                    }
                }
                if (schema.add_rows) {
                    const tdDel = $("<td class='text-center'>").appendTo(tr);
                    $('<button type="button" class="btn btn-xs btn-danger">✕</button>')
                        .on("click", () => {
                            rowStates.splice(rIdx, 1);
                            renderRowsTable();
                        })
                        .appendTo(tdDel);
                }
            });

            if (schema.add_rows) {
                const btnAdd = $('<button type="button" class="btn btn-xs btn-secondary mt-1">+ Agregar renglón</button>')
                    .on("click", () => {
                        rowStates.push(crearFilaVacia());
                        renderRowsTable();
                    });
                host.append(btnAdd);
            }
        }

        renderRowsTable();

        dialog.set_primary_action("Guardar y Confirmar", async () => {
            const payload = {};
            for (const f of schema.fields || []) {
                if (f.kind === "file") {
                    payload[f.name] = fileContents[f.name] || "";
                } else {
                    payload[f.name] = dialog.get_value(f.name);
                }
            }
            if (hasTable) {
                payload.rows = rowStates;
            }

            const btn = dialog.get_primary_btn().prop("disabled", true);
            try {
                const { message: result } = await frappe.call({
                    method: "lebaref_compras.operaciones.ejecutar",
                    type: "POST",
                    args: {
                        accion,
                        folio: folio || "",
                        operacion: tokenOperacion,
                        datos: JSON.stringify(payload),
                    },
                    freeze: true,
                    freeze_message: "Procesando en ERPNext…",
                });
                dialog.hide();
                frappe.show_alert({
                    message: `Operación completada: ${result.folio} (${result.tipo})`,
                    indicator: "green",
                });
                if (typeof alTerminar === "function") {
                    alTerminar(result);
                } else if (result && result.tipo && result.folio) {
                    frappe.set_route("Form", result.tipo, result.folio);
                }
            } finally {
                btn.prop("disabled", false);
            }
        });

        dialog.show();
    }

    async function verDetalleEquipo(tipo, folio) {
        const { message: info } = await frappe.call({
            method: "lebaref_compras.equipos.detalle",
            type: "GET",
            args: { tipo, folio },
        });
        const rows = info.rows || [];
        let htmlTbl = `<div class="mb-2 p-2" style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:6px;font-weight:600;">${frappe.utils.escape_html(info.header || folio)}</div>`;
        if (rows.length) {
            const cols = Object.keys(rows[0]);
            const ths = cols.map((c) => `<th>${frappe.utils.escape_html(c)}</th>`).join("");
            const trs = rows.map((r) => `<tr>${cols.map((c) => `<td>${frappe.utils.escape_html(String(r[c] ?? ""))}</td>`).join("")}</tr>`).join("");
            htmlTbl += `<div class="table-responsive"><table class="table table-bordered table-sm" style="font-size:12px;"><thead><tr>${ths}</tr></thead><tbody>${trs}</tbody></table></div>`;
        } else {
            htmlTbl += `<div class="text-muted small">Sin movimientos adicionales registrados.</div>`;
        }
        const d = new frappe.ui.Dialog({
            title: `Resumen e Historial · ${folio}`,
            size: "large",
            fields: [{ fieldname: "detalle", fieldtype: "HTML", options: htmlTbl }],
        });
        d.show();
    }

    async function abrirMapaUbicaciones() {
        const { message: data } = await frappe.call({
            method: "lebaref_compras.desk_ui.mapa_ubicaciones",
            type: "GET",
        });
        const herRows = (data.herramientas || []).map((h) =>
            `<tr><td><b>${frappe.utils.escape_html(h.ubicacion || "—")}</b></td><td>${frappe.utils.escape_html(h.codigo)}</td><td>${frappe.utils.escape_html(h.marca_modelo || h.articulo)}</td><td>${frappe.utils.escape_html(h.estado)}</td><td>${frappe.utils.escape_html(h.responsable || "En almacén")}</td></tr>`
        ).join("");
        const binRows = (data.existencias || []).map((b) =>
            `<tr><td><b>${frappe.utils.escape_html(b.warehouse)}</b></td><td>${frappe.utils.escape_html(b.item_code)}</td><td class="text-right"><b>${b.actual_qty} ${frappe.utils.escape_html(b.stock_uom || "")}</b></td></tr>`
        ).join("");

        const d = new frappe.ui.Dialog({
            title: "Mapa de Ubicaciones, Anaqueles y Custodia Lebaref",
            size: "extra-large",
            fields: [{
                fieldname: "mapa",
                fieldtype: "HTML",
                options: `
                    <h6><b>Herramientas por Ubicación / Gabinete (${(data.herramientas || []).length})</b></h6>
                    <div class="table-responsive mb-3" style="max-height:240px;overflow:auto;">
                        <table class="table table-sm table-bordered" style="font-size:12px;">
                            <thead><tr><th>Ubicación</th><th>Código HER</th><th>Descripción</th><th>Estado</th><th>Resguardo</th></tr></thead>
                            <tbody>${herRows || "<tr><td colspan='5'>Sin herramientas registradas.</td></tr>"}</tbody>
                        </table>
                    </div>
                    <h6><b>Existencias de Inventario por Almacén (${(data.existencias || []).length})</b></h6>
                    <div class="table-responsive" style="max-height:220px;overflow:auto;">
                        <table class="table table-sm table-bordered" style="font-size:12px;">
                            <thead><tr><th>Almacén</th><th>Artículo</th><th class="text-right">Existencia</th></tr></thead>
                            <tbody>${binRows || "<tr><td colspan='3'>Sin existencias con saldo distinto de cero.</td></tr>"}</tbody>
                        </table>
                    </div>`,
            }],
        });
        d.show();
    }

    window.lebaref = { abrirOperacion, verDetalleEquipo, abrirMapaUbicaciones, SHEETS_URL };

    // --- Botones guiados en Formularios de ERPNext Desk ---

    frappe.ui.form.on("Request for Quotation", {
        refresh(frm) {
            if (frm.doc.docstatus === 1) {
                frm.add_custom_button("Registrar oferta de proveedor", () =>
                    abrirOperacion("oferta", frm.doc.name, `Registrar oferta para ${frm.doc.name}`)
                ).addClass("btn-primary");
            }
            frm.add_custom_button("¿A quién cotizar? (Sheets)", () => window.open(SHEETS_URL, "_blank", "noopener"), "Lebaref");
        },
    });

    frappe.ui.form.on("Purchase Order", {
        refresh(frm) {
            if (frm.doc.docstatus === 0 && frappe.model.can_submit("Purchase Order")) {
                frm.add_custom_button("Autorizar Orden (Lebaref)", () =>
                    abrirOperacion("autorizar", frm.doc.name, `Autorizar Orden de Compra ${frm.doc.name}`, () => frm.reload_doc())
                ).addClass("btn-primary");
            }
            if (frm.doc.docstatus === 1 && frm.doc.status !== "Closed") {
                frm.add_custom_button("Recibir mercancía en almacén", () =>
                    abrirOperacion("recibir", frm.doc.name, `Recibir mercancía de ${frm.doc.name}`)
                ).addClass("btn-primary");
            }
        },
    });

    frappe.ui.form.on("Purchase Receipt", {
        refresh(frm) {
            if (frm.doc.docstatus === 1) {
                frm.add_custom_button("Archivar CFDI 4.0 (XML)", () =>
                    abrirOperacion("cfdi", frm.doc.name, `Archivar XML CFDI contra ${frm.doc.name}`)
                ).addClass("btn-primary");
            }
        },
    });

    frappe.ui.form.on("Stock Entry", {
        refresh(frm) {
            if (frm.doc.docstatus === 1 && frm.doc.stock_entry_type === "Material Transfer" && !frm.doc.custom_lebaref_origen) {
                frm.add_custom_button("Devolver sobrante a almacén", () =>
                    abrirOperacion("devolver", frm.doc.name, `Devolver sobrante de ${frm.doc.name}`)
                ).addClass("btn-primary");
            }
        },
    });

    frappe.ui.form.on("Lebaref Herramienta", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            frm.add_custom_button("Ver historial y TCO", () => verDetalleEquipo("Lebaref Herramienta", frm.doc.name));
            frm.add_custom_button("Compra y garantía", () =>
                abrirOperacion("garantia_herramienta", frm.doc.name, `Compra y garantía · ${frm.doc.name}`, () => frm.reload_doc()), "Acciones Lebaref");
            frm.add_custom_button("Registrar mantenimiento", () =>
                abrirOperacion("mantenimiento_herramienta", frm.doc.name, `Mantenimiento · ${frm.doc.name}`, () => frm.reload_doc()), "Acciones Lebaref");
            frm.add_custom_button("Cambiar estado / ubicación", () =>
                abrirOperacion("estado_herramienta", frm.doc.name, `Estado de herramienta · ${frm.doc.name}`, () => frm.reload_doc()), "Acciones Lebaref");
            frm.add_custom_button("Mapa de ubicaciones", () => abrirMapaUbicaciones(), "Acciones Lebaref");
        },
    });

    frappe.ui.form.on("Lebaref Mantenimiento Herramienta", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            if (!frm.doc.anulado) {
                frm.add_custom_button("Anular mantenimiento con motivo", () =>
                    abrirOperacion("anular_mantenimiento", frm.doc.name, `Anular mantenimiento · ${frm.doc.name}`, () => frm.reload_doc())
                );
            }
        },
    });

    frappe.ui.form.on("Lebaref Kit", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            frm.add_custom_button("Prestar kit (selectivo)", () =>
                abrirOperacion("prestamo_kit", frm.doc.name, `Prestar herramientas del kit ${frm.doc.name}`)
            ).addClass("btn-primary");
            frm.add_custom_button("Ver contenido y estado", () => verDetalleEquipo("Lebaref Kit", frm.doc.name));
            frm.add_custom_button("Cambiar composición del kit", () =>
                abrirOperacion("composicion_kit", frm.doc.name, `Composición de ${frm.doc.name}`, () => frm.reload_doc()), "Acciones Lebaref");
        },
    });

    frappe.ui.form.on("Lebaref Prestamo", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            if (frm.doc.estado !== "Devuelto") {
                frm.add_custom_button("Registrar retorno / check-list", () =>
                    abrirOperacion("retorno_herramienta", frm.doc.name, `Retorno de préstamo ${frm.doc.name}`, () => frm.reload_doc())
                ).addClass("btn-primary");
            }
            frm.add_custom_button("Ver detalle", () => verDetalleEquipo("Lebaref Prestamo", frm.doc.name));
        },
    });

    frappe.ui.form.on("Lebaref Vehiculo", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            frm.add_custom_button("Registrar gasto / combustible", () =>
                abrirOperacion("gasto_vehiculo", frm.doc.name, `Gasto o combustible · ${frm.doc.name}`, () => frm.reload_doc())
            ).addClass("btn-primary");
            frm.add_custom_button("Check-list semanal", () =>
                abrirOperacion("revision_vehiculo", frm.doc.name, `Check-list vehicular · ${frm.doc.name}`, () => frm.reload_doc()));
            frm.add_custom_button("Ver bitácora y rendimiento", () => verDetalleEquipo("Lebaref Vehiculo", frm.doc.name));
            frm.add_custom_button("Estado / vigencia de seguro", () =>
                abrirOperacion("estado_vehiculo", frm.doc.name, `Estado y seguro · ${frm.doc.name}`, () => frm.reload_doc()), "Acciones Lebaref");
        },
    });

    // --- Botones rápidos en Listas (ListView) para altas protegidas y capturas guiadas ---
    const listActions = [
        ["Material Request", "Captura Rápida Lebaref (con Conceptos Libres)", "solicitud"],
        ["Stock Entry", "Entregar Material a Obra", "entregar"],
        ["Supplier", "Alta Rápida con Contacto", "proveedor"],
        ["Lebaref Herramienta", "Nueva Herramienta (HER)", "herramienta"],
        ["Lebaref Kit", "Nuevo Maletín / Kit (KIT)", "kit"],
        ["Lebaref Prestamo", "Nuevo Vale de Préstamo", "prestamo"],
        ["Lebaref Vehiculo", "Nuevo Vehículo (VEH)", "vehiculo"],
    ];

    for (const [dt, label, accion] of listActions) {
        const prev = frappe.listview_settings[dt] || {};
        const prevOnload = prev.onload;
        frappe.listview_settings[dt] = Object.assign({}, prev, {
            onload(listview) {
                if (typeof prevOnload === "function") prevOnload(listview);
                listview.page.add_inner_button(label, () => abrirOperacion(accion, "", label, () => listview.refresh()));
                if (dt === "Supplier") {
                    listview.page.add_inner_button("🌐 ¿A quién cotizar? (Sheets)", () => window.open(SHEETS_URL, "_blank", "noopener"));
                }
                if (dt === "Lebaref Herramienta" || dt === "Item") {
                    listview.page.add_inner_button("🗺️ Mapa de Ubicaciones", () => abrirMapaUbicaciones());
                }
            },
        });
    }
})();
