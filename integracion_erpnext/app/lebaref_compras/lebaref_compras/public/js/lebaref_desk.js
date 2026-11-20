(() => {
    if (window.__lebarefDeskLoaded) return;
    window.__lebarefDeskLoaded = true;
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

    // Campos opcionales que admiten dejarse en blanco cuando el usuario no elige ninguno
    const SELECT_OPCIONALES = new Set([
        "departamento",
        "taxes_and_charges",
        "shipping_rule",
        "payment_terms_template",
        "almacen_destino",
    ]);

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
                const permiteVacio = SELECT_OPCIONALES.has(f.name) || f.choices.includes("");
                const listaOpciones = permiteVacio && !f.choices.includes("") ? ["", ...f.choices] : f.choices;
                dialogFields.push({
                    fieldname: f.name,
                    label: f.label,
                    fieldtype: "Select",
                    options: listaOpciones.join("\n"),
                    default: f.value !== undefined && f.value !== null && f.value !== "" ? f.value : (permiteVacio ? "" : (f.choices[0] || "")),
                });
            } else if (f.kind === "date") {
                const esFechaOpcional = f.name === "fecha_programada_pago";
                dialogFields.push({
                    fieldname: f.name,
                    label: f.label,
                    fieldtype: "Date",
                    default: f.value || (esFechaOpcional ? "" : frappe.datetime.get_today()),
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
                    default: f.value !== undefined && f.value !== null ? String(f.value) : "",
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

        const rowStates = (schema.rows || []).map((r) => Object.assign({}, r));

        function crearFilaVacia() {
            const obj = {};
            for (const col of schema.columns || []) {
                obj[col.name] = "";
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
                    const val = dialog.get_value(f.name);
                    if (val !== undefined && val !== null && val !== "") {
                        payload[f.name] = val;
                    } else if (!SELECT_OPCIONALES.has(f.name) && f.name !== "fecha_programada_pago") {
                        payload[f.name] = "";
                    }
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

        const zonas = data.zonas || [];
        const herramientas = data.herramientas || [];
        const existencias = data.existencias || [];
        const kits = data.kits || [];

        const d = new frappe.ui.Dialog({
            title: "🗺️ Mapa Visual de Almacén, Anaqueles A–F y Custodia Lebaref",
            size: "extra-large",
            fields: [{ fieldname: "mapa", fieldtype: "HTML" }],
        });

        const host = d.fields_dict.mapa.$wrapper;
        let zonaActiva = "TODAS";
        let textoFiltro = "";

        const topBar = $(`
            <div class="d-flex justify-content-between align-items-center mb-2 flex-wrap" style="gap:8px;">
                <div class="small text-muted">Haz clic en cualquier <b>Zona (A–F)</b> del plano para filtrar sus herramientas y existencias.</div>
                <div class="d-flex align-items-center" style="gap:6px;">
                    <input type="text" class="form-control form-control-sm leb-map-search" placeholder="Buscar código HER, artículo o responsable…" style="width:250px;">
                    <button type="button" class="btn btn-xs btn-default leb-map-all">Mostrar Todas (${herramientas.length} HER · ${existencias.length} Stock)</button>
                </div>
            </div>
        `).appendTo(host);

        const gridWrap = $('<div style="display:grid;grid-template-columns:repeat(3, minmax(0, 1fr));gap:10px;margin-bottom:14px;">').appendTo(host);
        const detailWrap = $("<div>").appendTo(host);

        function renderPlanoYTablas() {
            gridWrap.empty();
            for (const z of zonas) {
                const selected = zonaActiva === z.clave;
                const card = $(`
                    <div style="cursor:pointer;border:2px solid ${selected ? z.color : "#cbd5e1"};background:${selected ? "#eff6ff" : "#ffffff"};border-radius:8px;padding:10px 12px;transition:all .12s;">
                        <div class="d-flex justify-content-between align-items-center">
                            <span style="display:inline-block;background:${z.color};color:#fff;font-weight:800;font-size:12px;padding:2px 8px;border-radius:5px;">ZONA ${frappe.utils.escape_html(z.clave)}</span>
                            <span style="font-size:11px;font-weight:700;color:#334155;">${z.herramientas} HER · ${z.articulos_stock} Stock${z.kits ? ` · ${z.kits} KIT` : ""}</span>
                        </div>
                        <div style="font-weight:700;font-size:12.5px;color:#0f172a;margin-top:6px;">${frappe.utils.escape_html(z.titulo)}</div>
                        <div style="font-size:11px;color:#64748b;margin-top:2px;">${frappe.utils.escape_html(z.subtitulo)}</div>
                    </div>
                `).appendTo(gridWrap);
                card.on("click", () => {
                    zonaActiva = zonaActiva === z.clave ? "TODAS" : z.clave;
                    renderPlanoYTablas();
                });
            }

            const q = textoFiltro.trim().toLowerCase();
            const herFiltradas = herramientas.filter((h) => {
                if (zonaActiva !== "TODAS" && h.zona !== zonaActiva) return false;
                if (!q) return true;
                return [h.codigo, h.articulo, h.marca_modelo, h.ubicacion, h.responsable, h.obra, h.estado]
                    .some((v) => String(v || "").toLowerCase().includes(q));
            });

            const binFiltrados = existencias.filter((b) => {
                if (zonaActiva !== "TODAS" && b.zona !== zonaActiva) return false;
                if (!q) return true;
                return [b.warehouse, b.item_code, b.item_name, b.anaquel].some((v) => String(v || "").toLowerCase().includes(q));
            });

            const herRows = herFiltradas.map((h) => {
                const badgeColor = h.estado === "Disponible" ? "success" : (h.estado === "Prestada" ? "warning" : "secondary");
                return `<tr>
                    <td><b>Zona ${frappe.utils.escape_html(h.zona)}</b> · ${frappe.utils.escape_html(h.ubicacion || "Sin estante")}</td>
                    <td><a href="/app/lebaref-herramienta/${encodeURIComponent(h.name)}"><b>${frappe.utils.escape_html(h.codigo)}</b></a></td>
                    <td>${frappe.utils.escape_html(h.marca_modelo || h.articulo)}</td>
                    <td><span class="badge badge-${badgeColor}">${frappe.utils.escape_html(h.estado)}</span></td>
                    <td>${frappe.utils.escape_html(h.responsable || (h.obra ? "Obra " + h.obra : "En almacén"))}</td>
                </tr>`;
            }).join("");

            const binRows = binFiltrados.map((b) =>
                `<tr>
                    <td><b>Zona ${frappe.utils.escape_html(b.zona)}</b> · ${frappe.utils.escape_html(b.anaquel ? b.anaquel + " (" + b.warehouse + ")" : b.warehouse)}</td>
                    <td><a href="/app/item/${encodeURIComponent(b.item_code)}"><b>${frappe.utils.escape_html(b.item_code)}</b></a> — ${frappe.utils.escape_html(b.item_name || "")}</td>
                    <td class="text-right"><b>${b.actual_qty} ${frappe.utils.escape_html(b.stock_uom || "")}</b></td>
                </tr>`
            ).join("");

            detailWrap.html(`
                <div class="mb-1" style="font-weight:700;font-size:12.5px;color:#031e34;">
                    Filtro activo: <span class="badge badge-primary">${zonaActiva === "TODAS" ? "Todas las Zonas (A–F)" : "Zona " + zonaActiva}</span>
                    &nbsp;·&nbsp; Herramientas mostradas: <b>${herFiltradas.length}</b> &nbsp;·&nbsp; Renglones de stock: <b>${binFiltrados.length}</b>
                </div>
                <div class="table-responsive mb-3" style="max-height:220px;overflow:auto;">
                    <table class="table table-sm table-bordered" style="font-size:12px;">
                        <thead style="background:#f8fafc;"><tr><th>Zona / Anaquel</th><th>Código HER</th><th>Descripción / Modelo</th><th>Estado</th><th>Resguardo / Obra</th></tr></thead>
                        <tbody>${herRows || "<tr><td colspan='5' class='text-muted'>Sin herramientas en este filtro.</td></tr>"}</tbody>
                    </table>
                </div>
                <div class="table-responsive" style="max-height:180px;overflow:auto;">
                    <table class="table table-sm table-bordered" style="font-size:12px;">
                        <thead style="background:#f8fafc;"><tr><th>Zona / Almacén</th><th>Artículo (SKU)</th><th class="text-right">Existencia Actual</th></tr></thead>
                        <tbody>${binRows || "<tr><td colspan='3' class='text-muted'>Sin existencias con saldo distinto de cero en este filtro.</td></tr>"}</tbody>
                    </table>
                </div>
            `);
        }

        topBar.find(".leb-map-search").on("input", function () {
            textoFiltro = $(this).val() || "";
            renderPlanoYTablas();
        });
        topBar.find(".leb-map-all").on("click", () => {
            zonaActiva = "TODAS";
            textoFiltro = "";
            topBar.find(".leb-map-search").val("");
            renderPlanoYTablas();
        });

        renderPlanoYTablas();
        d.show();
    }

    async function abrirDirectorioProveedores() {
        const { message: dir } = await frappe.call({
            method: "lebaref_compras.sheets_proveedores.directorio",
            type: "GET",
            freeze: true,
            freeze_message: "Consultando directorio de proveedores…",
        });

        const d = new frappe.ui.Dialog({
            title: `📊 ¿A quién cotizar? · Directorio Lebaref (${dir.total} proveedores)`,
            size: "extra-large",
            fields: [{ fieldname: "dir_html", fieldtype: "HTML" }],
        });

        const host = d.fields_dict.dir_html.$wrapper;
        const bar = $(`
            <div class="d-flex justify-content-between align-items-center mb-2 flex-wrap" style="gap:8px;padding:8px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;">
                <input type="text" class="form-control form-control-sm leb-dir-q" placeholder="Filtrar por especialidad, proveedor, RFC o contacto…" style="width:290px;">
                <div class="d-flex" style="gap:6px;">
                    <button type="button" class="btn btn-xs btn-default leb-dir-copy">📋 Copiar Tabla para Sheets</button>
                    <button type="button" class="btn btn-xs btn-primary leb-dir-open">🌐 Abrir Google Sheets Oficial</button>
                </div>
            </div>
        `).appendTo(host);

        const tblHost = $('<div class="table-responsive" style="max-height:420px;overflow:auto;">').appendTo(host);

        function renderDir(filtro = "") {
            const q = filtro.trim().toLowerCase();
            const lista = (dir.proveedores || []).filter((p) => {
                if (!q) return true;
                return [p.especialidad, p.proveedor, p.rfc, p.contacto, p.telefono, p.correo]
                    .some((v) => String(v || "").toLowerCase().includes(q));
            });
            const rows = lista.map((p) => `
                <tr>
                    <td><span class="badge badge-light">${frappe.utils.escape_html(p.especialidad)}</span></td>
                    <td><a href="/app/supplier/${encodeURIComponent(p.id)}"><b>${frappe.utils.escape_html(p.proveedor)}</b></a></td>
                    <td>${frappe.utils.escape_html(p.rfc || "—")}</td>
                    <td>${frappe.utils.escape_html(p.contacto || "—")}</td>
                    <td>${frappe.utils.escape_html(p.telefono || "—")}</td>
                    <td>${frappe.utils.escape_html(p.correo || "—")}</td>
                    <td class="text-right">${p.cotizaciones} / <b>${p.ordenes}</b></td>
                </tr>
            `).join("");
            tblHost.html(`
                <table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;">
                        <tr><th>Especialidad / Grupo</th><th>Proveedor</th><th>RFC</th><th>Contacto</th><th>Teléfono</th><th>Correo</th><th class="text-right">Cot. / OCs</th></tr>
                    </thead>
                    <tbody>${rows || "<tr><td colspan='7' class='text-muted'>Sin coincidencias.</td></tr>"}</tbody>
                </table>
            `);
        }

        bar.find(".leb-dir-q").on("input", function () {
            renderDir($(this).val() || "");
        });
        bar.find(".leb-dir-copy").on("click", async () => {
            if (navigator.clipboard && dir.tsv) {
                await navigator.clipboard.writeText(dir.tsv);
                frappe.show_alert({ message: "Directorio copiado al portapapeles (listo para pegar en Google Sheets)", indicator: "green" });
            }
        });
        bar.find(".leb-dir-open").on("click", () => window.open(dir.sheets_url || SHEETS_URL, "_blank", "noopener"));

        renderDir();
        d.show();
    }

    window.lebaref = { abrirOperacion, verDetalleEquipo, abrirMapaUbicaciones, abrirDirectorioProveedores, SHEETS_URL };

    // --- Botones guiados en Formularios de ERPNext Desk ---

    frappe.ui.form.on("Request for Quotation", {
        refresh(frm) {
            if (frm.doc.docstatus === 1) {
                frm.add_custom_button("Registrar oferta de proveedor", () =>
                    abrirOperacion("oferta", frm.doc.name, `Registrar oferta comercial para ${frm.doc.name}`)
                ).addClass("btn-primary");
            }
            frm.add_custom_button("¿A quién cotizar? (Directorio / Sheets)", () => abrirDirectorioProveedores(), "Lebaref");
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
            frm.add_custom_button("🗺️ Mapa de ubicaciones (A–F)", () => abrirMapaUbicaciones(), "Acciones Lebaref");
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
                    listview.page.add_inner_button("📊 ¿A quién cotizar? (Directorio / Sheets)", () => abrirDirectorioProveedores());
                }
                if (dt === "Lebaref Herramienta") {
                    listview.page.add_inner_button("🗺️ Mapa de Ubicaciones (A–F)", () => abrirMapaUbicaciones());
                }
            },
        });
    }

    // También agregar el Mapa de Ubicaciones en la lista de Item (Artículos)
    const prevItem = frappe.listview_settings["Item"] || {};
    const prevItemOnload = prevItem.onload;
    frappe.listview_settings["Item"] = Object.assign({}, prevItem, {
        onload(listview) {
            if (typeof prevItemOnload === "function") prevItemOnload(listview);
            listview.page.add_inner_button("Alta Rápida SKU (con Conversión)", () => abrirOperacion("articulo", "", "Alta Rápida de Artículo", () => listview.refresh()));
            listview.page.add_inner_button("🗺️ Mapa de Ubicaciones (A–F)", () => abrirMapaUbicaciones());
        },
    });
})();
