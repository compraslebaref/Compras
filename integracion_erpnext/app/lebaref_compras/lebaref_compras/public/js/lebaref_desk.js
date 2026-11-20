(() => {
    if (window.__lebarefDeskLoaded) {
        if (typeof window.__lebarefAplicarListviewSettings === "function") {
            window.__lebarefAplicarListviewSettings();
        }
        return;
    }
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
        "from_warehouse",
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
                const etiquetaCampo = accion === "solicitud" && f.name === "title"
                    ? "Nombre descriptivo de la solicitud (para ubicarla fácil en la tabla)"
                    : f.label;
                dialogFields.push({
                    fieldname: f.name,
                    label: etiquetaCampo,
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

        const mapaSustituciones = {};
        if (accion === "recibir" && folio) {
            try {
                const listaSust = await frappe.db.get_list("Lebaref Sustitucion", {
                    filters: { orden: folio, docstatus: 1 },
                    fields: ["name", "renglon", "original", "sustituto", "cantidad", "unidad", "motivo"],
                    limit: 100,
                });
                for (const s of listaSust || []) {
                    mapaSustituciones[s.name] = s;
                }
            } catch (e) {
                // Conservar choices del esquema si no hubiera lectura extendida
            }
        }

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
                    if (!col.editable || (accion === "recibir" && col.name === "warehouse" && rowObj.is_stock_item !== undefined && !Number(rowObj.is_stock_item))) {
                        if (accion === "recibir" && col.name === "warehouse" && !Number(rowObj.is_stock_item)) {
                            rowObj.warehouse = "";
                            td.css({ background: "#f8fafc", color: "#94a3b8", verticalAlign: "middle" }).text("No aplica (Servicio)");
                        } else {
                            td.css({ background: "#f8fafc", verticalAlign: "middle" }).text(curVal);
                        }
                    } else if (accion === "recibir" && col.name === "sustitucion") {
                        const todasSust = Array.isArray(col.choices) ? col.choices : [];
                        const opcionesSust = todasSust.filter((ch) => {
                            const info = mapaSustituciones[ch];
                            return !info || info.renglon === rowObj.id;
                        });
                        if (!opcionesSust.length) {
                            rowObj.sustitucion = "";
                            td.css({ background: "#f8fafc", color: "#94a3b8", verticalAlign: "middle" }).text("— Original —");
                        } else {
                            const sel = $('<select class="form-control form-control-sm">').appendTo(td);
                            $("<option>").val("").text(`— Recibir original (${rowObj.item_code}) —`).appendTo(sel);
                            for (const ch of opcionesSust) {
                                const s = mapaSustituciones[ch];
                                const lbl = s
                                    ? `🔄 ${s.sustituto} (máx ${s.cantidad} ${s.unidad || ""}) · ${ch}`
                                    : ch;
                                $("<option>").val(ch).text(lbl).prop("selected", ch === curVal).appendTo(sel);
                            }
                            sel.on("change", function () {
                                rowObj.sustitucion = $(this).val();
                            });
                        }
                    } else if (accion === "sobrante" && col.name === "valor_unitario" && dialog.get_value("tipo") !== "Entrada no contabilizada") {
                        rowObj.valor_unitario = "";
                        td.css({ background: "#f8fafc", color: "#94a3b8", verticalAlign: "middle" }).text("No aplica (Traslado)");
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
                    } else if (Array.isArray(col.choices) && col.choices.length === 0) {
                        rowObj[col.name] = "";
                        td.css({ background: "#f8fafc", color: "#94a3b8", verticalAlign: "middle" }).text("—");
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

        if (accion === "autorizar_sustitucion" && dialog.fields_dict.renglon && dialog.fields_dict.cantidad) {
            const sugerirPendiente = () => {
                const rid = dialog.get_value("renglon");
                const rFound = (schema.rows || []).find((x) => x.id === rid);
                if (rFound && rFound.pending !== undefined) {
                    dialog.set_value("cantidad", String(rFound.pending));
                }
            };
            dialog.fields_dict.renglon.$input.on("change", sugerirPendiente);
            sugerirPendiente();
        }

        if (accion === "sobrante" && dialog.fields_dict.tipo) {
            const syncTipoSobrante = () => {
                const esNuevo = dialog.get_value("tipo") === "Entrada no contabilizada";
                dialog.set_df_property("from_warehouse", "hidden", esNuevo ? 1 : 0);
                dialog.set_df_property("confirmacion", "hidden", esNuevo ? 0 : 1);
                if (esNuevo) {
                    dialog.set_value("from_warehouse", "");
                    if (dialog.fields_dict.confirmacion && dialog.fields_dict.confirmacion.$input) {
                        dialog.fields_dict.confirmacion.$input.attr("placeholder", "Escribe exactamente: NO CONTABILIZADO");
                    }
                } else {
                    dialog.set_value("confirmacion", "");
                }
                renderRowsTable();
            };
            dialog.fields_dict.tipo.$input.on("change", syncTipoSobrante);
            setTimeout(syncTipoSobrante, 30);
        }

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
                <div class="d-flex align-items-center flex-wrap" style="gap:6px;">
                    <input type="text" class="form-control form-control-sm leb-map-search" placeholder="Buscar código HER, artículo o responsable…" style="width:240px;">
                    <button type="button" class="btn btn-xs btn-default leb-map-all">Mostrar Todas (${herramientas.length} HER · ${existencias.length} Stock)</button>
                    <button type="button" class="btn btn-xs btn-primary leb-map-print">🏷️ Imprimir Códigos de Almacén</button>
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
        topBar.find(".leb-map-print").on("click", () => {
            d.hide();
            abrirImpresorCodigosCarta("articulos", textoFiltro);
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

    async function abrirArchivoHistorico(tabInicial = "precios", filtroInicial = "") {
        const { message: hist } = await frappe.call({
            method: "lebaref_compras.sheets_proveedores.archivo_historico",
            type: "GET",
            freeze: true,
            freeze_message: "Cargando archivo histórico de Lebaref…",
        });

        const precios = hist.precios || [];
        const cfdis = hist.cfdis || [];
        const ordenes = hist.ordenes || [];
        const moneyFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

        const d = new frappe.ui.Dialog({
            title: `📜 Archivo Histórico Lebaref (${precios.length} Precios · ${cfdis.length} CFDIs 4.0 · ${ordenes.length} OCs Cerradas)`,
            size: "extra-large",
            fields: [{ fieldname: "hist_html", fieldtype: "HTML" }],
        });

        const host = d.fields_dict.hist_html.$wrapper;
        let tabActiva = tabInicial;
        let query = filtroInicial || "";

        const nav = $(`
            <div class="d-flex justify-content-between align-items-center mb-2 flex-wrap" style="gap:8px;padding:8px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;">
                <div class="btn-group btn-group-sm">
                    <button type="button" class="btn btn-default leb-htab" data-tab="precios">💰 Banco de Precios (${precios.length})</button>
                    <button type="button" class="btn btn-default leb-htab" data-tab="cfdis">🧾 Facturas XML CFDI (${cfdis.length})</button>
                    <button type="button" class="btn btn-default leb-htab" data-tab="ordenes">📦 OCs Históricas Cerradas (${ordenes.length})</button>
                </div>
                <input type="text" class="form-control form-control-sm leb-hist-q" placeholder="Buscar producto, proveedor, RFC, UUID o folio…" style="width:290px;">
            </div>
        `).appendTo(host);
        nav.find(".leb-hist-q").val(query);

        const bodyWrap = $('<div class="table-responsive" style="max-height:430px;overflow:auto;">').appendTo(host);

        function renderHist() {
            nav.find(".leb-htab").each(function () {
                const active = $(this).data("tab") === tabActiva;
                $(this).toggleClass("btn-primary", active).toggleClass("btn-default", !active);
            });
            const q = query.trim().toLowerCase();

            if (tabActiva === "precios") {
                const rows = precios.filter((r) => !q || [r.codigo, r.concepto, r.familia, r.proveedor, r.origen].some((v) => String(v || "").toLowerCase().includes(q)))
                    .slice(0, 300)
                    .map((r) => `<tr>
                        <td>${frappe.utils.escape_html(r.fecha || "—")}</td>
                        <td><b>${frappe.utils.escape_html(r.codigo || "LIBRE")}</b><br><small>${frappe.utils.escape_html(r.concepto || "")}</small></td>
                        <td>${frappe.utils.escape_html(r.proveedor || "—")}</td>
                        <td class="text-right"><b>$${moneyFmt.format(r.precio || 0)}</b></td>
                        <td class="text-right">${r.dias || 0} d</td>
                        <td>${r.ganador ? "<span class='badge badge-success'>Ganador</span>" : "<span class='badge badge-light'>Cotizado</span>"} <small>${frappe.utils.escape_html(r.origen || "")}</small></td>
                    </tr>`).join("");
                bodyWrap.html(`<table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;"><tr><th>Fecha</th><th>SKU / Concepto</th><th>Proveedor</th><th class="text-right">Precio s/IVA</th><th class="text-right">Entrega</th><th>Resultado / Origen</th></tr></thead>
                    <tbody>${rows || "<tr><td colspan='6' class='text-muted'>Sin registros para este filtro.</td></tr>"}</tbody></table>`);
            } else if (tabActiva === "cfdis") {
                const rows = cfdis.filter((r) => !q || [r.uuid, r.serie_folio, r.rfc, r.emisor, r.oc_folio, r.resumen].some((v) => String(v || "").toLowerCase().includes(q)))
                    .slice(0, 300)
                    .map((r) => `<tr>
                        <td><code style="font-size:11px;">${frappe.utils.escape_html(r.uuid)}</code><br><small>Serie/Folio: ${frappe.utils.escape_html(r.serie_folio || "—")}</small></td>
                        <td>${frappe.utils.escape_html((r.fecha || "").slice(0, 10))}</td>
                        <td><b>${frappe.utils.escape_html(r.emisor || "—")}</b><br><small>RFC: ${frappe.utils.escape_html(r.rfc || "")}</small></td>
                        <td>${frappe.utils.escape_html(r.oc_folio || "—")}<br><small class="text-muted">${frappe.utils.escape_html(r.resumen || "")}</small></td>
                        <td class="text-right">$${moneyFmt.format(r.subtotal || 0)}</td>
                        <td class="text-right"><b>$${moneyFmt.format(r.total || 0)} ${frappe.utils.escape_html(r.moneda || "MXN")}</b></td>
                    </tr>`).join("");
                bodyWrap.html(`<table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;"><tr><th>UUID Fiscal</th><th>Fecha</th><th>Emisor / RFC</th><th>OC Origen / Conceptos</th><th class="text-right">Subtotal</th><th class="text-right">Total XML</th></tr></thead>
                    <tbody>${rows || "<tr><td colspan='6' class='text-muted'>Sin CFDIs para este filtro.</td></tr>"}</tbody></table>`);
            } else {
                const rows = ordenes.filter((r) => !q || [r.folio, r.proveedor, r.obra, r.depto, r.encargado].some((v) => String(v || "").toLowerCase().includes(q)))
                    .slice(0, 300)
                    .map((r) => `<tr>
                        <td><b>${frappe.utils.escape_html(r.folio)}</b></td>
                        <td>${frappe.utils.escape_html((r.fecha || "").slice(0, 10))}</td>
                        <td>${frappe.utils.escape_html(r.proveedor || "—")}</td>
                        <td>${frappe.utils.escape_html(r.obra || "Stock General")}<br><small class="text-muted">${frappe.utils.escape_html([r.depto, r.encargado].filter(Boolean).join(" · "))}</small></td>
                        <td class="text-right">$${moneyFmt.format(r.subtotal || 0)}</td>
                        <td class="text-right"><b>$${moneyFmt.format(r.total || 0)}</b></td>
                    </tr>`).join("");
                bodyWrap.html(`<table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;"><tr><th>Folio Histórico</th><th>Fecha</th><th>Proveedor</th><th>Obra / Solicitante</th><th class="text-right">Subtotal</th><th class="text-right">Total c/IVA</th></tr></thead>
                    <tbody>${rows || "<tr><td colspan='6' class='text-muted'>Sin órdenes históricas para este filtro.</td></tr>"}</tbody></table>`);
            }
        }

        nav.find(".leb-htab").on("click", function () {
            tabActiva = $(this).data("tab");
            renderHist();
        });
        nav.find(".leb-hist-q").on("input", function () {
            query = $(this).val() || "";
            renderHist();
        });

        renderHist();
        d.show();
    }

    async function abrirDialogoRegularizarEntrada(frm) {
        const { message: schema } = await frappe.call({
            method: "lebaref_compras.operaciones.esquema",
            type: "GET",
            args: { accion: "regularizar_entrada", folio: frm.doc.name },
            freeze: true,
            freeze_message: "Consultando órdenes autorizadas del proveedor…",
        });

        const poField = (schema.fields || []).find((f) => f.name === "purchase_order");
        const poChoices = (poField && poField.choices) || [];
        if (!poChoices.length) {
            frappe.msgprint(`No hay Órdenes de Compra autorizadas abiertas de <b>${frappe.utils.escape_html(frm.doc.supplier)}</b> en esta empresa. Autoriza primero la OC y vuelve a abrir Regularizar.`);
            return;
        }

        const tokenOp = nuevoUuid();
        const rowLinks = {};

        const dlg = new frappe.ui.Dialog({
            title: `Regularizar Entrada Anticipada · ${frm.doc.name}`,
            size: "extra-large",
            fields: [
                {
                    fieldname: "_nota",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-warning py-2 px-3 mb-2" style="font-size:12px;">
                        Vincula esta recepción física con su Orden de Compra autorizada sin duplicar existencias ni asientos contables (<b>ALM-02</b>). Artículo, unidad, obra y precio deben coincidir.
                    </div>`,
                },
                {
                    fieldname: "purchase_order",
                    label: "Orden de compra autorizada",
                    fieldtype: "Select",
                    options: poChoices.join("\n"),
                    default: poChoices[0],
                    reqd: 1,
                },
                {
                    fieldname: "motivo",
                    label: "Motivo de regularización",
                    fieldtype: "Small Text",
                    default: "Orden de compra autorizada posterior a la recepción física en almacén",
                    reqd: 1,
                },
                { fieldname: "_rows_html", fieldtype: "HTML" },
            ],
            primary_action_label: "Vincular y Regularizar Recepción",
            async primary_action(vals) {
                const rowsPayload = (frm.doc.items || []).map((r) => ({
                    id: r.name,
                    purchase_order_item: rowLinks[r.name] || "",
                }));
                if (rowsPayload.some((r) => !r.purchase_order_item)) {
                    frappe.msgprint("Selecciona el renglón correspondiente de la Orden de Compra en todas las partidas.");
                    return;
                }
                const btn = dlg.get_primary_btn().prop("disabled", true);
                try {
                    await frappe.call({
                        method: "lebaref_compras.operaciones.ejecutar",
                        type: "POST",
                        args: {
                            accion: "regularizar_entrada",
                            folio: frm.doc.name,
                            operacion: tokenOp,
                            datos: JSON.stringify({
                                purchase_order: vals.purchase_order,
                                motivo: vals.motivo,
                                rows: rowsPayload,
                            }),
                        },
                        freeze: true,
                        freeze_message: "Regularizando recepción contra Orden de Compra…",
                    });
                    dlg.hide();
                    frappe.show_alert({ message: `Recepción ${frm.doc.name} regularizada correctamente`, indicator: "green" });
                    frm.reload_doc();
                } finally {
                    btn.prop("disabled", false);
                }
            },
        });

        async function cargarRenglonesOrden(poName) {
            const host = dlg.fields_dict._rows_html.$wrapper;
            host.html(`<div class="text-muted small py-2">Cargando partidas de ${frappe.utils.escape_html(poName)}…</div>`);
            const { message: poDoc } = await frappe.call({
                method: "frappe.client.get",
                type: "GET",
                args: { doctype: "Purchase Order", name: poName },
            });
            const poItems = (poDoc && poDoc.items) || [];
            host.empty();
            const tbl = $('<table class="table table-bordered table-sm mt-2" style="font-size:12px;">').appendTo(host);
            $('<thead style="background:#f1f5f9;"><tr><th>Renglón Recepción</th><th>Cant. Recibida</th><th>Precio / Obra</th><th>Renglón de Orden de Compra a Vincular</th></tr></thead>').appendTo(tbl);
            const tbody = $("<tbody>").appendTo(tbl);

            for (const prRow of frm.doc.items || []) {
                const tr = $("<tr>").appendTo(tbody);
                $("<td>").html(`<b>${frappe.utils.escape_html(prRow.item_code)}</b><br><small class="text-muted">${frappe.utils.escape_html(prRow.item_name || "")}</small>`).appendTo(tr);
                $("<td>").text(`${prRow.qty} ${prRow.uom || ""}`).appendTo(tr);
                $("<td>").text(`$${prRow.rate} · ${prRow.project || "Sin obra"}`).appendTo(tr);

                const tdSel = $("<td>").appendTo(tr);
                const sel = $('<select class="form-control form-control-sm">').appendTo(tdSel);
                $("<option>").val("").text("— Selecciona renglón de OC —").appendTo(sel);

                let autoMatch = "";
                for (const poi of poItems) {
                    const pend = Math.max(0, (poi.qty || 0) - (poi.received_qty || 0));
                    const lbl = `#${poi.idx} · ${poi.item_code} · Pend: ${pend} ${poi.uom || ""} · $${poi.rate} · ${poi.project || "Sin obra"}`;
                    $("<option>").val(poi.name).text(lbl).appendTo(sel);
                    if (
                        !autoMatch &&
                        poi.item_code === prRow.item_code &&
                        (poi.uom || "") === (prRow.uom || "") &&
                        (poi.project || "") === (prRow.project || "") &&
                        Math.abs((poi.rate || 0) - (prRow.rate || 0)) < 0.01 &&
                        pend >= (prRow.received_qty || prRow.qty || 0)
                    ) {
                        autoMatch = poi.name;
                    }
                }
                rowLinks[prRow.name] = autoMatch;
                if (autoMatch) sel.val(autoMatch);
                sel.on("change", function () {
                    rowLinks[prRow.name] = $(this).val();
                });
            }
        }

        dlg.fields_dict.purchase_order.$input.on("change", function () {
            const v = dlg.get_value("purchase_order");
            if (v) cargarRenglonesOrden(v);
        });

        dlg.show();
        cargarRenglonesOrden(poChoices[0]);
    }

    async function abrirImpresorCodigosCarta(origenInicial = "articulos", busquedaInicial = "", anaquelInicial = "") {
        const { message: opts } = await frappe.call({
            method: "lebaref_compras.impresion_carta.opciones_hojas_codigos",
            type: "GET",
            freeze: true,
            freeze_message: "Preparando opciones de impresión Tamaño Carta…",
        });

        const grupos = (opts && opts.grupos) || [];
        const anaqueles = (opts && opts.anaqueles) || [];

        const ORIGEN_POR_ETIQUETA = {
            "Artículos del Catálogo (SKUs)": "articulos",
            "Herramientas Individuales (HER)": "herramientas",
            "Maletines / Kits de Cuadrilla (KIT)": "kits",
            "Flotilla Vehicular (VEH)": "vehiculos",
        };
        const ETIQUETA_POR_ORIGEN = {
            articulos: "Artículos del Catálogo (SKUs)",
            herramientas: "Herramientas Individuales (HER)",
            kits: "Maletines / Kits de Cuadrilla (KIT)",
            vehiculos: "Flotilla Vehicular (VEH)",
        };

        const FORMATO_POR_ETIQUETA = {
            "Catálogo Visual con Foto (12 por hoja Carta · 3×4)": "catalogo_12",
            "Planilla 30 Etiquetas (3×10 · Avery 5160 / Recortable)": "etiquetas_30",
            "Tarjetas Grandes de Anaquel / Gabinete (6 por hoja Carta · 2×3)": "tarjetas_6",
        };

        const d = new frappe.ui.Dialog({
            title: "🏷️ Imprimir Hojas de Códigos de Barras Tamaño Carta (Datamax S30W)",
            size: "large",
            fields: [
                {
                    fieldname: "_info",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        Diseñado para <b>impresora láser o inyección Tamaño Carta</b> (sin rollo térmico) y lectura inmediata con pistola <b>Datamax S30W</b>:<br>
                        • <b>Catálogo 12/hoja:</b> tarjetas con foto real y código Code 128B para carpeta de mostrador.<br>
                        • <b>Planilla 30 etiquetas (3×10):</b> compatible con papel adherible <i>Avery 5160 (66.6 × 25.4 mm)</i> o Carta recortable.<br>
                        • <b>Tarjetas grandes 6/hoja:</b> ideales para frentes de estantes A–F, tapas de maletines KIT y unidades VEH.
                    </div>`,
                },
                {
                    fieldname: "origen_lbl",
                    label: "Qué códigos deseas imprimir",
                    fieldtype: "Select",
                    options: Object.keys(ORIGEN_POR_ETIQUETA).join("\n"),
                    default: ETIQUETA_POR_ORIGEN[origenInicial] || "Artículos del Catálogo (SKUs)",
                    reqd: 1,
                },
                {
                    fieldname: "formato_lbl",
                    label: "Distribución en Hoja Tamaño Carta",
                    fieldtype: "Select",
                    options: Object.keys(FORMATO_POR_ETIQUETA).join("\n"),
                    default: busquedaInicial ? "Planilla 30 Etiquetas (3×10 · Avery 5160 / Recortable)" : "Catálogo Visual con Foto (12 por hoja Carta · 3×4)",
                    reqd: 1,
                },
                { fieldtype: "Column Break" },
                {
                    fieldname: "grupo",
                    label: "Categoría / Familia (opcional, solo SKUs)",
                    fieldtype: "Select",
                    options: ["", ...grupos].join("\n"),
                    default: "",
                },
                {
                    fieldname: "anaquel",
                    label: "Ubicación / Anaquel (opcional)",
                    fieldtype: "Select",
                    options: ["", ...anaqueles].join("\n"),
                    default: anaquelInicial || "",
                },
                {
                    fieldname: "busqueda",
                    label: "Filtro específico (código, serie, placas o descripción)",
                    fieldtype: "Data",
                    default: busquedaInicial || "",
                },
                {
                    fieldname: "solo_con_stock",
                    label: "Solo artículos con existencia física (> 0) en almacén",
                    fieldtype: "Check",
                    default: busquedaInicial || origenInicial !== "articulos" ? 0 : 1,
                },
                {
                    fieldname: "copias",
                    label: "Copias por cada código",
                    fieldtype: "Int",
                    default: 1,
                },
            ],
            primary_action_label: "🖨️ Generar Hoja Carta Lista para Imprimir",
            async primary_action(vals) {
                const origen = ORIGEN_POR_ETIQUETA[vals.origen_lbl] || "articulos";
                const formato = FORMATO_POR_ETIQUETA[vals.formato_lbl] || "catalogo_12";
                const win = window.open("", "_blank");
                if (win) {
                    win.document.write("<!doctype html><html><body style='font-family:Segoe UI,Arial,sans-serif;padding:24px;'>Generando códigos vectoriales Code 128B para impresión Tamaño Carta…</body></html>");
                }
                const btn = d.get_primary_btn().prop("disabled", true);
                try {
                    const { message: htmlDoc } = await frappe.call({
                        method: "lebaref_compras.impresion_carta.generar_html_codigos_carta",
                        type: "GET",
                        args: {
                            origen,
                            formato,
                            grupo: origen === "articulos" ? (vals.grupo || "") : "",
                            anaquel: vals.anaquel || "",
                            solo_con_stock: origen === "articulos" && vals.solo_con_stock ? 1 : 0,
                            busqueda: vals.busqueda || "",
                            copias: Math.max(1, Math.min(30, Number(vals.copias || 1))),
                        },
                        freeze: true,
                        freeze_message: "Armando hojas Tamaño Carta con códigos Code 128B…",
                    });
                    if (win) {
                        win.document.open();
                        win.document.write(htmlDoc);
                        win.document.close();
                        d.hide();
                    } else {
                        frappe.msgprint("Tu navegador bloqueó la pestaña emergente. Permite ventanas emergentes para 127.0.0.1 y vuelve a intentar.");
                    }
                } catch (err) {
                    if (win) win.close();
                } finally {
                    btn.prop("disabled", false);
                }
            },
        });

        const actualizarVisibilidadSku = () => {
            const esSku = (ORIGEN_POR_ETIQUETA[d.get_value("origen_lbl")] || "articulos") === "articulos";
            d.set_df_property("grupo", "hidden", esSku ? 0 : 1);
            d.set_df_property("solo_con_stock", "hidden", esSku ? 0 : 1);
        };
        d.fields_dict.origen_lbl.$input.on("change", actualizarVisibilidadSku);
        d.show();
        actualizarVisibilidadSku();
    }

    let dialogoEscaneoActivo = null;

    function abrirEscaneoPistola() {
        if (dialogoEscaneoActivo && dialogoEscaneoActivo.display) {
            const inp = dialogoEscaneoActivo.get_field("codigo").$input;
            if (inp) inp.val("").trigger("focus");
            return;
        }
        const d = new frappe.ui.Dialog({
            title: "🔫 Escanear Código con Pistola Datamax S30W (Atajo: F2)",
            fields: [
                {
                    fieldname: "_ayuda",
                    fieldtype: "HTML",
                    options: `<div class="small text-muted mb-2">
                        Dispara el lector <b>Datamax S30W</b> sobre cualquier código impreso en hoja Carta (Solicitud, Orden, Recepción, Salida, Herramienta <b>HER</b>, Maletín <b>KIT</b>, Vehículo <b>VEH</b>, SKU o código de barras de fábrica).
                    </div>`,
                },
                {
                    fieldname: "codigo",
                    label: "Código escaneado o escrito",
                    fieldtype: "Data",
                    reqd: 1,
                },
            ],
            primary_action_label: "Abrir en ERPNext",
            async primary_action(vals) {
                const codigo = String(vals.codigo || "").trim();
                if (!codigo) return;
                const btn = d.get_primary_btn().prop("disabled", true);
                try {
                    const { message: res } = await frappe.call({
                        method: "lebaref_compras.impresion_carta.resolver_escaneo",
                        type: "GET",
                        args: { codigo },
                    });
                    if (res && res.encontrado) {
                        d.hide();
                        frappe.show_alert({
                            message: `🔫 Código reconocido: ${res.name} (${res.doctype})`,
                            indicator: "green",
                        });
                        frappe.set_route("Form", res.doctype, res.name);
                    } else {
                        frappe.show_alert({
                            message: `No se encontró ningún documento o artículo con el código "${codigo}".`,
                            indicator: "orange",
                        });
                        const inp = d.get_field("codigo").$input;
                        if (inp) inp.val("").trigger("focus");
                    }
                } finally {
                    btn.prop("disabled", false);
                }
            },
        });
        dialogoEscaneoActivo = d;
        d.show();
        setTimeout(() => {
            const inp = d.get_field("codigo").$input;
            if (inp) {
                inp.attr("placeholder", "Apunta y dispara con la pistola S30W…");
                inp.trigger("focus");
                inp.off("keydown.lebs30w").on("keydown.lebs30w", (ev) => {
                    if (ev.key === "Enter") {
                        ev.preventDefault();
                        d.get_primary_btn().trigger("click");
                    }
                });
            }
        }, 120);
    }

    // Atajo global F2 para disparar pistola lectora S30W desde cualquier pantalla de ERPNext
    $(document).on("keydown.lebarefScanner", (ev) => {
        if (ev.key === "F2") {
            ev.preventDefault();
            abrirEscaneoPistola();
        }
    });

    // Enlaces rápidos del banner superior en el Workspace Lebaref
    $(document).on("click", 'a[href="#leb-escanear-s30w"], a[href="#leb-codigos-carta"], a[href="#leb-mapa-af"], a[href="#leb-historico"]', function (ev) {
        ev.preventDefault();
        const href = $(this).attr("href");
        if (href === "#leb-escanear-s30w") abrirEscaneoPistola();
        else if (href === "#leb-codigos-carta") abrirImpresorCodigosCarta("articulos");
        else if (href === "#leb-mapa-af") abrirMapaUbicaciones();
        else if (href === "#leb-historico") abrirArchivoHistorico("precios");
    });

    async function verSaldosEntrega(folio) {
        const { message: res } = await frappe.call({
            method: "lebaref_compras.consumos.consultar",
            type: "GET",
            args: { entrega: folio },
            freeze: true,
            freeze_message: "Consultando saldo bajo resguardo y consumos en obra…",
        });

        const filas = (res && res.rows) || [];
        const verCostos = Boolean(res && res.puede_ver_costos);
        const moneda = (res && res.moneda) || "MXN";
        const moneyFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

        let totalCosto = 0;
        const trs = filas
            .map((r) => {
                totalCosto += Number(r.costo_consumido || 0);
                return `<tr>
                    <td><b>${frappe.utils.escape_html(r.item_code)}</b></td>
                    <td>${frappe.utils.escape_html(r.warehouse || "—")}<br><small class="text-muted">${frappe.utils.escape_html(r.project || "")}</small></td>
                    <td class="text-right">${r.entregado} ${frappe.utils.escape_html(r.uom || "")}</td>
                    <td class="text-right" style="color:#b45309;font-weight:600;">${r.consumido} ${frappe.utils.escape_html(r.uom || "")}</td>
                    <td class="text-right">${r.devuelto} ${frappe.utils.escape_html(r.uom || "")}</td>
                    <td class="text-right"><b style="color:#031e34;">${r.saldo} ${frappe.utils.escape_html(r.uom || "")}</b></td>
                    ${verCostos ? `<td class="text-right"><b>$${moneyFmt.format(r.costo_consumido || 0)} ${frappe.utils.escape_html(moneda)}</b></td>` : ""}
                </tr>`;
            })
            .join("");

        const html = `
            <div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                <b>Control de Resguardo vs. Consumo Real en Obra (ALM-05)</b> · Entrega origen: <b>${frappe.utils.escape_html(folio)}</b>
                ${verCostos ? ` · Costo total ya consumido en obra: <b>$${moneyFmt.format(totalCosto)} ${frappe.utils.escape_html(moneda)}</b>` : ""}
            </div>
            <div class="table-responsive">
                <table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;">
                        <tr>
                            <th>Artículo</th>
                            <th>Almacén Obra / Proyecto</th>
                            <th class="text-right">Entregado</th>
                            <th class="text-right">Consumido</th>
                            <th class="text-right">Devuelto</th>
                            <th class="text-right">Bajo Resguardo</th>
                            ${verCostos ? `<th class="text-right">Costo Consumido</th>` : ""}
                        </tr>
                    </thead>
                    <tbody>${trs || "<tr><td colspan='7' class='text-muted'>Sin partidas.</td></tr>"}</tbody>
                </table>
            </div>
        `;

        const d = new frappe.ui.Dialog({
            title: `📦 Saldo Bajo Resguardo y Consumos · ${folio}`,
            size: "extra-large",
            fields: [{ fieldname: "_html", fieldtype: "HTML", options: html }],
            primary_action_label: "🔨 Registrar Consumo en Obra",
            primary_action() {
                d.hide();
                abrirOperacion("consumir", folio, `Registrar consumo real en obra · ${folio}`);
            },
        });
        d.add_custom_action("↩️ Devolver Sobrante a Almacén", () => {
            d.hide();
            abrirOperacion("devolver", folio, `Devolver sobrante de ${folio}`);
        });
        d.show();
    }

    async function renderizarFichaProductoEnItem(frm) {
        if (!frm.fields_dict.custom_ficha_visual_html) return;
        const host = frm.fields_dict.custom_ficha_visual_html.$wrapper;
        if (frm.is_new()) {
            host.empty();
            return;
        }
        try {
            const { message: f } = await frappe.call({
                method: "lebaref_compras.ux_lebaref.ficha_producto",
                type: "GET",
                args: { item_code: frm.doc.name },
            });
            if (!f) return;

            const esc = frappe.utils.escape_html;
            const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            const alertaBajoStock = f.es_inventariable && f.stock_minimo > 0 && f.stock_total <= f.stock_minimo;
            const colorStock = !f.es_inventariable ? "#475569" : (alertaBajoStock ? "#b45309" : (f.stock_total > 0 ? "#059669" : "#64748b"));

            const imgHtml = f.imagen
                ? `<img src="${esc(f.imagen)}" alt="${esc(f.nombre)}" class="leb-ficha-img" style="width:176px;height:176px;object-fit:contain;border:1px solid #cbd5e1;border-radius:10px;background:#ffffff;cursor:zoom-in;padding:6px;">`
                : `<div style="width:176px;height:176px;border:2px dashed #cbd5e1;border-radius:10px;background:#f8fafc;display:flex;flex-direction:column;align-items:center;justify-content:center;color:#64748b;font-size:12px;text-align:center;padding:10px;">Sin fotografía<br><small>Haz clic abajo para subir</small></div>`;

            const convTxt = f.factor_compra > 1
                ? `1 ${esc(f.uom_compra)} = <b>${f.factor_compra} ${esc(f.uom_almacen)}</b>`
                : `${esc(f.uom_almacen)} (1:1)`;

            const clavesXmlTxt = (f.claves_proveedores || []).length
                ? f.claves_proveedores.map((c) => `<span class="badge badge-light mr-1" style="border:1px solid #cbd5e1;">${esc(c.proveedor)}: <b>${esc(c.clave)}</b></span>`).join(" ")
                : `<span class="text-muted">Sin claves XML registradas aún</span>`;

            host.html(`
                <div style="border:1.5px solid #cbd5e1;border-radius:12px;background:linear-gradient(180deg,#f8fafc 0%,#ffffff 48%);padding:14px 16px;margin-bottom:12px;box-shadow:0 1px 3px rgba(15,23,42,0.05);">
                    <div style="display:flex;gap:18px;flex-wrap:wrap;align-items:flex-start;">
                        <div style="width:180px;flex-shrink:0;text-align:center;">
                            ${imgHtml}
                            <div class="mt-2">
                                <button type="button" class="btn btn-xs btn-default leb-btn-subir-foto">📷 Subir / Cambiar Foto</button>
                            </div>
                            <div class="mt-2 p-1" style="background:#fff;border:1px solid #e2e8f0;border-radius:6px;">
                                ${f.svg_barcode || ""}
                            </div>
                        </div>
                        <div style="flex:1;min-width:280px;">
                            <div class="d-flex justify-content-between align-items-start flex-wrap" style="gap:8px;margin-bottom:6px;">
                                <div>
                                    <span style="background:#031e34;color:#fff;font-size:11.5px;font-weight:800;padding:3px 9px;border-radius:5px;margin-right:6px;">CLAVE: ${esc(f.codigo_barras)}</span>
                                    <span style="background:#e2e8f0;color:#0f172a;font-size:11px;font-weight:700;padding:3px 8px;border-radius:5px;margin-right:6px;">ID: ${esc(f.codigo_interno)}</span>
                                    <span style="background:#eff6ff;color:#0d5b94;border:1px solid #bfdbfe;font-size:11px;font-weight:700;padding:3px 9px;border-radius:5px;">📍 ${esc(f.ubicacion)}</span>
                                </div>
                                <span class="badge ${f.es_inventariable ? "badge-info" : "badge-secondary"}">${f.es_inventariable ? "Inventariable" : "Servicio"}</span>
                            </div>
                            <div style="font-size:16px;font-weight:800;color:#0f172a;line-height:1.25;margin-bottom:10px;">${esc(f.nombre)}</div>

                            <div style="display:grid;grid-template-columns:repeat(4, minmax(120px, 1fr));gap:8px;margin-bottom:10px;">
                                <div style="background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:7px 10px;">
                                    <div style="font-size:10.5px;color:#64748b;text-transform:uppercase;font-weight:700;">Existencia Actual</div>
                                    <div style="font-size:15px;font-weight:800;color:${colorStock};">${f.es_inventariable ? `${f.stock_total} ${esc(f.uom_almacen)}` : "N/A"}</div>
                                </div>
                                <div style="background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:7px 10px;">
                                    <div style="font-size:10.5px;color:#64748b;text-transform:uppercase;font-weight:700;">Stock Mínimo</div>
                                    <div style="font-size:15px;font-weight:800;color:#334155;">${f.stock_minimo} ${esc(f.uom_almacen)}</div>
                                </div>
                                <div style="background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:7px 10px;">
                                    <div style="font-size:10.5px;color:#64748b;text-transform:uppercase;font-weight:700;">Último Costo s/IVA</div>
                                    <div style="font-size:15px;font-weight:800;color:#031e34;">$${money.format(f.ultimo_costo || 0)} MXN</div>
                                </div>
                                <div style="background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:7px 10px;">
                                    <div style="font-size:10.5px;color:#64748b;text-transform:uppercase;font-weight:700;">Unidad / Conversión</div>
                                    <div style="font-size:12.5px;font-weight:600;color:#0f172a;margin-top:2px;">${convTxt}</div>
                                </div>
                            </div>

                            <table class="table table-bordered table-sm mb-2" style="font-size:12px;background:#fff;">
                                <tbody>
                                    <tr>
                                        <td style="width:25%;background:#f8fafc;"><b>Categoría:</b> ${esc(f.categoria)}</td>
                                        <td style="width:25%;background:#f8fafc;"><b>Marca:</b> ${esc(f.marca)}</td>
                                        <td style="width:25%;background:#f8fafc;"><b>Modelo:</b> ${esc(f.modelo)}</td>
                                        <td style="width:25%;background:#f8fafc;"><b>Medidas:</b> ${esc(f.medidas)}</td>
                                    </tr>
                                    <tr>
                                        <td colspan="2"><b>Capacidad Eléctrica / Uso:</b> ${esc(f.capacidad)}</td>
                                        <td><b>Espec. 1:</b> ${esc(f.espec1)}</td>
                                        <td><b>Espec. 2:</b> ${esc(f.espec2)}</td>
                                    </tr>
                                    <tr>
                                        <td colspan="4"><b>Claves XML de Proveedores:</b> ${clavesXmlTxt}</td>
                                    </tr>
                                </tbody>
                            </table>
                        </div>
                    </div>
                </div>
            `);

            host.find(".leb-ficha-img").on("click", () => {
                const dlgImg = new frappe.ui.Dialog({
                    title: `${f.codigo_barras} · ${f.nombre}`,
                    size: "large",
                    fields: [{
                        fieldname: "img_full",
                        fieldtype: "HTML",
                        options: `<div class="text-center p-2"><img src="${esc(f.imagen)}" style="max-width:100%;max-height:70vh;object-fit:contain;border-radius:8px;"></div>`,
                    }],
                });
                dlgImg.show();
            });

            host.find(".leb-btn-subir-foto").on("click", () => {
                new frappe.ui.FileUploader({
                    doctype: frm.doctype,
                    docname: frm.docname,
                    frm,
                    folder: "Home/Attachments",
                    restrictions: { allowed_file_types: ["image/*"] },
                    on_success(fileDoc) {
                        frm.set_value("image", fileDoc.file_url).then(() => frm.save());
                    },
                });
            });
        } catch (e) {
            // Mantener formulario estándar si ocurre error
        }
    }

    async function renderizarDirectorioEnPestanaProveedor(frm) {
        if (!frm.fields_dict.custom_quien_cotizar_html) return;
        const host = frm.fields_dict.custom_quien_cotizar_html.$wrapper;
        if (host.data("loadedDir")) return;
        host.html(`<div class="text-muted small py-2">Cargando directorio ¿A quién cotizar?…</div>`);
        try {
            const { message: dir } = await frappe.call({
                method: "lebaref_compras.sheets_proveedores.directorio",
                type: "GET",
            });
            host.data("loadedDir", true);
            host.empty();
            const bar = $(`
                <div class="d-flex justify-content-between align-items-center mb-2 mt-2 flex-wrap" style="gap:8px;padding:8px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;">
                    <input type="text" class="form-control form-control-sm leb-tabdir-q" placeholder="Buscar por especialidad (Eléctrico, HVAC, Plomería…), proveedor, RFC o contacto…" style="width:360px;">
                    <div class="d-flex" style="gap:6px;">
                        <button type="button" class="btn btn-xs btn-default leb-tabdir-copy">📋 Copiar Tabla</button>
                        <button type="button" class="btn btn-xs btn-primary leb-tabdir-open">🌐 Abrir Google Sheet de Respaldo</button>
                    </div>
                </div>
            `).appendTo(host);
            const tblWrap = $('<div class="table-responsive" style="max-height:420px;overflow:auto;">').appendTo(host);
            const pintar = (q = "") => {
                const filtro = q.trim().toLowerCase();
                const lista = (dir.proveedores || []).filter((p) => !filtro || [p.especialidad, p.proveedor, p.rfc, p.contacto, p.telefono, p.correo].some((v) => String(v || "").toLowerCase().includes(filtro)));
                const rows = lista.map((p) => `
                    <tr ${p.id === frm.doc.name ? 'style="background:#eff6ff;"' : ""}>
                        <td><span class="badge badge-info">${frappe.utils.escape_html(p.especialidad)}</span></td>
                        <td><a href="/app/supplier/${encodeURIComponent(p.id)}"><b>${frappe.utils.escape_html(p.proveedor)}</b></a></td>
                        <td>${frappe.utils.escape_html(p.rfc || "—")}</td>
                        <td>${frappe.utils.escape_html(p.contacto || "—")}</td>
                        <td>${frappe.utils.escape_html(p.telefono || "—")}</td>
                        <td>${frappe.utils.escape_html(p.correo || "—")}</td>
                        <td class="text-right">${p.cotizaciones} / <b>${p.ordenes}</b></td>
                    </tr>
                `).join("");
                tblWrap.html(`
                    <table class="table table-bordered table-sm" style="font-size:12px;">
                        <thead style="background:#031e34;color:#fff;">
                            <tr><th>Especialidad</th><th>Proveedor</th><th>RFC</th><th>Contacto</th><th>Teléfono</th><th>Correo</th><th class="text-right">Cot. / OCs</th></tr>
                        </thead>
                        <tbody>${rows || "<tr><td colspan='7' class='text-muted'>Sin coincidencias.</td></tr>"}</tbody>
                    </table>
                `);
            };
            bar.find(".leb-tabdir-q").on("input", function () { pintar($(this).val() || ""); });
            bar.find(".leb-tabdir-copy").on("click", async () => {
                if (navigator.clipboard && dir.tsv) {
                    await navigator.clipboard.writeText(dir.tsv);
                    frappe.show_alert({ message: "Directorio copiado al portapapeles", indicator: "green" });
                }
            });
            bar.find(".leb-tabdir-open").on("click", () => window.open(dir.sheets_url || SHEETS_URL, "_blank", "noopener"));
            pintar();
        } catch (e) {
            host.html(`<div class="text-muted small">Guarda primero el proveedor para consultar el directorio completo.</div>`);
        }
    }

    async function abrirDialogoBajaProveedor(frm) {
        const { message: info } = await frappe.call({
            method: "lebaref_compras.ux_lebaref.info_baja_proveedor",
            type: "GET",
            args: { proveedor: frm.doc.name },
            freeze: true,
        });

        const c = info.conteos || {};
        const resumenHist = `Cotizaciones: <b>${c.cotizaciones}</b> · Órdenes: <b>${c.ordenes}</b> · Recepciones: <b>${c.recepciones}</b> · CFDIs: <b>${c.cfdis}</b>`;
        const opcionesAccion = [];
        if (!info.tiene_historial) {
            opcionesAccion.push("Eliminar definitivamente del sistema");
        }
        opcionesAccion.push("Fusionar duplicado (transferir su historial a otro proveedor y eliminar este)");
        opcionesAccion.push(info.disabled ? "Reactivar proveedor oculto" : "Ocultar / Deshabilitar proveedor (conservar historial)");

        const d = new frappe.ui.Dialog({
            title: `🗑️ Eliminar o Depurar Proveedor · ${frm.doc.name}`,
            fields: [
                {
                    fieldname: "_resumen",
                    fieldtype: "HTML",
                    options: `<div class="alert ${info.tiene_historial ? "alert-warning" : "alert-info"} py-2 px-3 mb-2" style="font-size:12px;">
                        ${info.tiene_historial
                            ? `Este proveedor ya cuenta con documentos históricos (${resumenHist}). Para no romper auditoría puedes <b>fusionarlo</b> con otro proveedor o <b>deshabilitarlo</b>.`
                            : `Este proveedor <b>no tiene compras ni cotizaciones registradas</b> (${resumenHist}). Puedes eliminarlo definitivamente en un clic.`}
                    </div>`,
                },
                {
                    fieldname: "accion_sel",
                    label: "¿Qué deseas hacer con este proveedor?",
                    fieldtype: "Select",
                    options: opcionesAccion.join("\n"),
                    default: opcionesAccion[0],
                    reqd: 1,
                },
                {
                    fieldname: "proveedor_destino",
                    label: "Proveedor destino (el que conservará todo el historial)",
                    fieldtype: "Select",
                    options: ["", ...(info.otros_proveedores || [])].join("\n"),
                    depends_on: "eval:doc.accion_sel && doc.accion_sel.startsWith('Fusionar')",
                },
            ],
            primary_action_label: "Aplicar Acción",
            async primary_action(vals) {
                let modo = "ocultar";
                if (vals.accion_sel.startsWith("Eliminar")) modo = "eliminar";
                else if (vals.accion_sel.startsWith("Fusionar")) modo = "fusionar";
                else if (vals.accion_sel.startsWith("Reactivar")) modo = "reactivar";

                if (modo === "fusionar" && !vals.proveedor_destino) {
                    frappe.msgprint("Selecciona el proveedor destino al cual transferir el historial.");
                    return;
                }

                await frappe.call({
                    method: "lebaref_compras.ux_lebaref.gestionar_baja_proveedor",
                    type: "POST",
                    args: {
                        proveedor: frm.doc.name,
                        modo,
                        proveedor_destino: vals.proveedor_destino || "",
                    },
                    freeze: true,
                    freeze_message: "Aplicando cambio en catálogo de proveedores…",
                });
                d.hide();
                if (modo === "eliminar") {
                    frappe.show_alert({ message: `Proveedor ${frm.doc.name} eliminado definitivamente`, indicator: "green" });
                    frappe.set_route("List", "Supplier");
                } else if (modo === "fusionar") {
                    frappe.show_alert({ message: `Proveedor fusionado dentro de ${vals.proveedor_destino}`, indicator: "green" });
                    frappe.set_route("Form", "Supplier", vals.proveedor_destino);
                } else {
                    frappe.show_alert({ message: "Estado del proveedor actualizado", indicator: "green" });
                    frm.reload_doc();
                }
            },
        });
        d.show();
    }

    function abrirDialogoNuevoCliente(alTerminar = null) {
        const d = new frappe.ui.Dialog({
            title: "➕ Registrar Nuevo Cliente Lebaref",
            fields: [
                { fieldname: "customer_name", label: "Nombre comercial o Razón Social", fieldtype: "Data", reqd: 1 },
                { fieldname: "tax_id", label: "RFC (opcional)", fieldtype: "Data" },
                {
                    fieldname: "customer_group",
                    label: "Tipo / Sector del Cliente",
                    fieldtype: "Select",
                    options: "Comercial / Privado\nIndustrial\nGobierno\nResidencial",
                    default: "Comercial / Privado",
                },
                { fieldtype: "Column Break" },
                { fieldname: "contacto", label: "Persona de Contacto", fieldtype: "Data" },
                { fieldname: "telefono", label: "Teléfono / Celular", fieldtype: "Data" },
                { fieldname: "correo", label: "Correo Electrónico", fieldtype: "Data" },
                { fieldtype: "Section Break" },
                { fieldname: "direccion", label: "Dirección / Ubicación Fiscal u Operativa", fieldtype: "Data" },
                { fieldname: "ciudad", label: "Ciudad / Municipio", fieldtype: "Data", default: "Mérida" },
            ],
            primary_action_label: "Guardar Cliente",
            async primary_action(vals) {
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.ux_lebaref.crear_cliente_rapido",
                    type: "POST",
                    args: { datos: JSON.stringify(vals) },
                    freeze: true,
                    freeze_message: "Registrando cliente…",
                });
                d.hide();
                frappe.show_alert({ message: `Cliente registrado: ${res.folio}`, indicator: "green" });
                if (typeof alTerminar === "function") alTerminar(res);
                else frappe.set_route("Form", "Customer", res.folio);
            },
        });
        d.show();
    }

    function abrirDialogoClienteObra(frm) {
        const d = new frappe.ui.Dialog({
            title: `🏢 Vincular Cliente a Obra · ${frm.doc.name}`,
            fields: [
                {
                    fieldname: "cliente",
                    label: "Cliente de esta Obra / Proyecto",
                    fieldtype: "Link",
                    options: "Customer",
                    default: frm.doc.customer || "",
                },
            ],
            primary_action_label: "Guardar Vínculo",
            async primary_action(vals) {
                await frappe.call({
                    method: "lebaref_compras.ux_lebaref.asignar_cliente_obra",
                    type: "POST",
                    args: { obra: frm.doc.name, cliente: vals.cliente || "" },
                    freeze: true,
                });
                d.hide();
                frappe.show_alert({ message: "Cliente vinculado a la obra", indicator: "green" });
                frm.reload_doc();
            },
        });
        d.add_custom_action("➕ Crear Nuevo Cliente", () => {
            d.hide();
            abrirDialogoNuevoCliente((res) => {
                frappe.call({
                    method: "lebaref_compras.ux_lebaref.asignar_cliente_obra",
                    type: "POST",
                    args: { obra: frm.doc.name, cliente: res.folio },
                }).then(() => frm.reload_doc());
            });
        });
        d.show();
    }

    window.lebaref = {
        abrirOperacion,
        verDetalleEquipo,
        verSaldosEntrega,
        abrirMapaUbicaciones,
        abrirDirectorioProveedores,
        abrirArchivoHistorico,
        abrirImpresorCodigosCarta,
        abrirEscaneoPistola,
        abrirDialogoNuevoCliente,
        SHEETS_URL,
    };

    // --- Botones guiados en Formularios de ERPNext Desk ---

    frappe.ui.form.on("Item", {
        refresh(frm) {
            renderizarFichaProductoEnItem(frm);
            if (frm.is_new()) return;
            frm.add_custom_button("🏷️ Imprimir Etiquetas de este SKU", () =>
                abrirImpresorCodigosCarta("articulos", frm.doc.item_code || frm.doc.name)
            );
            frm.add_custom_button("💰 Histórico de Precios", () =>
                abrirArchivoHistorico("precios", frm.doc.item_code || frm.doc.name), "Lebaref"
            );
            frm.add_custom_button("🗺️ Mapa Zonas A–F", () => abrirMapaUbicaciones(), "Lebaref");
        },
    });

    frappe.ui.form.on("Supplier", {
        refresh(frm) {
            renderizarDirectorioEnPestanaProveedor(frm);
            if (frm.is_new()) return;
            if (frm.doc.disabled) {
                frm.dashboard.set_headline_alert("🚫 Proveedor oculto / deshabilitado — No aparece en capturas nuevas.", "orange");
            }
            frm.add_custom_button("🗑️ Eliminar / Depurar Proveedor", () => abrirDialogoBajaProveedor(frm));
            frm.add_custom_button("📜 Histórico de Precios y CFDIs", () => abrirArchivoHistorico("precios", frm.doc.supplier_name || frm.doc.name), "Lebaref");
        },
    });

    frappe.ui.form.on("Department", {
        refresh(frm) {
            if (frm.is_new() || frm.doc.is_group) return;
            const estaDeshab = Number(frm.doc.disabled || 0) === 1;
            if (estaDeshab) {
                frm.dashboard.set_headline_alert("🚫 Departamento deshabilitado — No se mostrará al crear nuevas solicitudes.", "orange");
            }
            if (frappe.model.can_write("Department")) {
                frm.add_custom_button(
                    estaDeshab ? "✅ Habilitar Departamento" : "🚫 Deshabilitar Departamento",
                    async () => {
                        await frappe.call({
                            method: "lebaref_compras.ux_lebaref.alternar_estado_departamento",
                            type: "POST",
                            args: { departamento: frm.doc.name },
                            freeze: true,
                        });
                        frappe.show_alert({
                            message: estaDeshab ? "Departamento habilitado" : "Departamento deshabilitado",
                            indicator: "green",
                        });
                        frm.reload_doc();
                    }
                ).addClass("btn-primary");
            }
        },
    });

    frappe.ui.form.on("Customer", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.add_custom_button("➕ Nueva Obra para este Cliente", () =>
                abrirOperacion("obra", "", "Nueva Obra / Proyecto", async (res) => {
                    await frappe.call({
                        method: "lebaref_compras.ux_lebaref.asignar_cliente_obra",
                        type: "POST",
                        args: { obra: res.folio, cliente: frm.doc.name },
                    });
                    frappe.set_route("Form", "Project", res.folio);
                })
            ).addClass("btn-primary");
        },
    });

    frappe.ui.form.on("Project", {
        refresh(frm) {
            if (frm.is_new()) return;
            if (frm.doc.customer) {
                frm.dashboard.set_headline_alert(
                    `🏢 Obra vinculada al cliente: <b>${frappe.utils.escape_html(frm.doc.customer)}</b>`,
                    "blue"
                );
            }
            if (frappe.model.can_write("Project")) {
                frm.add_custom_button("🏢 Asignar / Cambiar Cliente", () => abrirDialogoClienteObra(frm));
            }
        },
    });

    frappe.ui.form.on("Lebaref CFDI", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            if (frm.doc.recepcion) {
                frm.dashboard.set_headline_alert(
                    `🧾 CFDI 4.0 vinculado a Recepción de Almacén <b>${frappe.utils.escape_html(frm.doc.recepcion)}</b>`,
                    "green"
                );
                frm.add_custom_button("Abrir Recepción", () =>
                    frappe.set_route("Form", "Purchase Receipt", frm.doc.recepcion)
                );
            } else if (frm.doc.custom_oc_origen) {
                frm.dashboard.set_headline_alert(
                    `📜 Factura CFDI 4.0 Histórica Migrada · Serie/Folio: <b>${frappe.utils.escape_html(frm.doc.custom_serie_folio || "—")}</b> · Orden Origen: <b>${frappe.utils.escape_html(frm.doc.custom_oc_origen)}</b>`,
                    "blue"
                );
            }
        },
    });

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
        async refresh(frm) {
            if (frm.doc.docstatus === 0 && frappe.model.can_submit("Purchase Order")) {
                frm.add_custom_button("Autorizar Orden (Lebaref)", () =>
                    abrirOperacion("autorizar", frm.doc.name, `Autorizar Orden de Compra ${frm.doc.name}`, () => frm.reload_doc())
                ).addClass("btn-primary");
            }
            if (frm.doc.docstatus === 1 && frm.doc.status !== "Closed" && frm.doc.status !== "On Hold") {
                frm.add_custom_button("Recibir mercancía en almacén", () =>
                    abrirOperacion("recibir", frm.doc.name, `Recibir mercancía de ${frm.doc.name}`)
                ).addClass("btn-primary");
                if (frappe.model.can_write("Purchase Order") && frappe.model.can_submit("Purchase Order")) {
                    frm.add_custom_button("Autorizar sustitución al recibir (ALM-04)", () =>
                        abrirOperacion(
                            "autorizar_sustitucion",
                            frm.doc.name,
                            `Autorizar sustitución de artículo · ${frm.doc.name}`,
                            () => frm.reload_doc()
                        ),
                        "Lebaref"
                    );
                }
            }
            if (frm.doc.docstatus === 1) {
                try {
                    const susts = await frappe.db.get_list("Lebaref Sustitucion", {
                        filters: { orden: frm.doc.name, docstatus: 1 },
                        fields: ["name", "original", "sustituto", "cantidad", "unidad", "motivo"],
                        limit: 20,
                    });
                    if (susts && susts.length) {
                        const detalle = susts
                            .map((s) => `${frappe.utils.escape_html(s.original)} → <b>${frappe.utils.escape_html(s.sustituto)}</b> (${s.cantidad} ${frappe.utils.escape_html(s.unidad || "")} · Motivo: ${frappe.utils.escape_html(s.motivo || "")})`)
                            .join(" | ");
                        frm.dashboard.set_headline_alert(
                            `🔄 Sustitución(es) autorizada(s) para recibir (ALM-04): ${detalle}`,
                            "blue"
                        );
                    }
                } catch (e) {
                    // Sin lectura de Lebaref Sustitucion
                }
            }
        },
    });

    frappe.ui.form.on("Purchase Receipt", {
        async refresh(frm) {
            if (frm.doc.docstatus === 1 && frm.doc.custom_lebaref_por_regularizar) {
                frm.dashboard.set_headline_alert(
                    "⚠️ Entrada anticipada sin Orden de Compra — Pendiente de regularizar (ALM-02).",
                    "orange"
                );
                frm.add_custom_button("Regularizar contra Orden de Compra (ALM-02)", () =>
                    abrirDialogoRegularizarEntrada(frm)
                ).addClass("btn-primary");
            }
            if (frm.doc.docstatus === 1 && !frm.doc.custom_lebaref_por_regularizar) {
                frm.add_custom_button("Archivar CFDI 4.0 (XML)", () =>
                    abrirOperacion("cfdi", frm.doc.name, `Archivar XML CFDI contra ${frm.doc.name}`)
                ).addClass("btn-primary");
            }
            const filasSust = (frm.doc.items || []).filter((r) => r.custom_lebaref_articulo_original || r.custom_lebaref_sustitucion);
            if (frm.doc.docstatus === 1 && filasSust.length && !frm.doc.custom_lebaref_por_regularizar) {
                const motivosPorId = {};
                try {
                    const ids = [...new Set(filasSust.map((r) => r.custom_lebaref_sustitucion).filter(Boolean))];
                    if (ids.length) {
                        const docsSust = await frappe.db.get_list("Lebaref Sustitucion", {
                            filters: { name: ["in", ids] },
                            fields: ["name", "motivo"],
                            limit: 50,
                        });
                        for (const ds of docsSust || []) motivosPorId[ds.name] = ds.motivo;
                    }
                } catch (e) {
                    // Ignorar si no aplica
                }
                const txtSust = filasSust
                    .map((r) => {
                        const mot = motivosPorId[r.custom_lebaref_sustitucion];
                        return `<b>${frappe.utils.escape_html(r.custom_lebaref_articulo_original || "")}</b> → <b>${frappe.utils.escape_html(r.item_code)}</b> (${r.qty} ${frappe.utils.escape_html(r.uom || "")}${mot ? " · Motivo: " + frappe.utils.escape_html(mot) : ""})`;
                    })
                    .join(" | ");
                frm.dashboard.set_headline_alert(`🔄 Recepción con sustitución autorizada (ALM-04): ${txtSust}`, "blue");
            }
        },
    });

    frappe.ui.form.on("Lebaref Sustitucion", {
        refresh(frm) {
            if (!frm.is_new() && frm.doc.docstatus === 1) {
                frm.disable_save();
                if (frm.doc.orden) {
                    frm.add_custom_button("Abrir Orden de Compra", () =>
                        frappe.set_route("Form", "Purchase Order", frm.doc.orden)
                    );
                }
            }
        },
    });

    frappe.ui.form.on("Stock Entry", {
        async refresh(frm) {
            if (frm.is_new()) return;
            const esTransferencia = frm.doc.purpose === "Material Transfer" || frm.doc.stock_entry_type === "Material Transfer";
            const esConsumo = frm.doc.purpose === "Material Issue" || frm.doc.stock_entry_type === "Material Issue";

            // ALM-07: Sobrante histórico con acta
            if (frm.doc.custom_lebaref_tipo_sobrante) {
                const detalleActa = `Acta: <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_referencia_sobrante || "—")}</b> · Entrega: ${frappe.utils.escape_html(frm.doc.custom_lebaref_persona_sobrante || "—")} · Tratamiento: <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_tipo_sobrante)}</b>`;
                if (frm.doc.docstatus === 0) {
                    frm.dashboard.set_headline_alert(
                        `📋 Sobrante histórico en borrador pendiente de autorización (ALM-07) — ${detalleActa}`,
                        "orange"
                    );
                    if (frappe.model.can_submit("Stock Entry")) {
                        frm.add_custom_button("Autorizar sobrante histórico (ALM-07)", () =>
                            abrirOperacion(
                                "autorizar_sobrante",
                                frm.doc.name,
                                `Autorizar sobrante histórico · ${frm.doc.name}`,
                                () => frm.reload_doc()
                            )
                        ).addClass("btn-primary");
                    }
                } else if (frm.doc.docstatus === 1) {
                    frm.dashboard.set_headline_alert(
                        `✅ Sobrante histórico autorizado (ALM-07) — ${detalleActa}`,
                        "green"
                    );
                }
                return;
            }

            // ALM-05: Entrega original de material bajo resguardo en obra
            if (frm.doc.docstatus === 1 && esTransferencia && !frm.doc.custom_lebaref_origen) {
                frm.add_custom_button("Registrar consumo en obra (ALM-05)", () =>
                    abrirOperacion("consumir", frm.doc.name, `Registrar consumo real en obra · ${frm.doc.name}`)
                ).addClass("btn-primary");
                frm.add_custom_button("Devolver sobrante a almacén", () =>
                    abrirOperacion("devolver", frm.doc.name, `Devolver sobrante de ${frm.doc.name}`)
                );
                frm.add_custom_button("📦 Ver saldo bajo resguardo y costos", () =>
                    verSaldosEntrega(frm.doc.name)
                );
                try {
                    const { message: resumen } = await frappe.call({
                        method: "lebaref_compras.consumos.consultar",
                        type: "GET",
                        args: { entrega: frm.doc.name },
                    });
                    if (resumen && Array.isArray(resumen.rows) && resumen.rows.length) {
                        const txt = resumen.rows
                            .map((r) => `${frappe.utils.escape_html(r.item_code)}: resguardo <b>${r.saldo}</b> / ${r.entregado} ${frappe.utils.escape_html(r.uom || "")} (cons: ${r.consumido}, dev: ${r.devuelto})`)
                            .join(" · ");
                        frm.dashboard.set_headline_alert(`📦 Control de Resguardo en Obra (ALM-05): ${txt}`, "blue");
                    }
                } catch (e) {
                    // Ignorar si no aplica a este traslado
                }
            }

            // Movimientos hijos vinculados a una entrega original (Consumo ALM-05 o Retorno)
            if (frm.doc.docstatus === 1 && frm.doc.custom_lebaref_origen) {
                frm.dashboard.set_headline_alert(
                    esConsumo
                        ? `🔨 Consumo real de material en obra (ALM-05) vinculado a la entrega <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_origen)}</b>.`
                        : `↩️ Retorno de sobrante a almacén vinculado a la entrega <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_origen)}</b>.`,
                    esConsumo ? "blue" : "green"
                );
                frm.add_custom_button("Abrir entrega original", () =>
                    frappe.set_route("Form", "Stock Entry", frm.doc.custom_lebaref_origen)
                );
            }
        },
    });

    frappe.ui.form.on("Lebaref Herramienta", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            frm.add_custom_button("Ver historial y TCO", () => verDetalleEquipo("Lebaref Herramienta", frm.doc.name));
            frm.add_custom_button("🏷️ Imprimir Etiqueta / Ficha Carta", () =>
                abrirImpresorCodigosCarta("herramientas", frm.doc.codigo || frm.doc.name)
            );
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
            frm.add_custom_button("🏷️ Imprimir Etiqueta de Kit", () =>
                abrirImpresorCodigosCarta("kits", frm.doc.codigo || frm.doc.name)
            );
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
            frm.add_custom_button("🏷️ Imprimir Etiqueta Vehicular", () =>
                abrirImpresorCodigosCarta("vehiculos", frm.doc.codigo || frm.doc.name)
            );
            frm.add_custom_button("Ver bitácora y rendimiento", () => verDetalleEquipo("Lebaref Vehiculo", frm.doc.name));
            frm.add_custom_button("Estado / vigencia de seguro", () =>
                abrirOperacion("estado_vehiculo", frm.doc.name, `Estado y seguro · ${frm.doc.name}`, () => frm.reload_doc()), "Acciones Lebaref");
        },
    });

    // --- Vista Dividida Maestro–Detalle en Artículos (Catálogo) estilo ERP original con diseño nativo ERPNext ---
    function inyectarEstilosSplitCatalogo() {
        if (document.getElementById("leb-item-split-css")) return;
        const st = document.createElement("style");
        st.id = "leb-item-split-css";
        st.textContent = `
            .leb-split-active-page .layout-side-section {
                display: none !important;
            }
            .leb-split-wrapper {
                display: flex !important;
                gap: 14px;
                align-items: flex-start;
                width: 100%;
            }
            .leb-split-wrapper > .layout-main-section {
                flex: 1 1 0% !important;
                min-width: 0 !important;
            }
            .leb-item-split-panel {
                width: 396px;
                flex: 0 0 396px;
                background: var(--card-bg, #ffffff);
                border: 1px solid var(--border-color, #e2e8f0);
                border-radius: var(--border-radius-md, 8px);
                color: var(--text-color, #0f172a);
                position: sticky;
                top: 56px;
                max-height: calc(100vh - 72px);
                display: flex;
                flex-direction: column;
                overflow: hidden;
            }
            .leb-item-split-panel.leb-hidden {
                display: none !important;
            }
            @media (max-width: 1024px) {
                .leb-split-wrapper {
                    flex-direction: column !important;
                }
                .leb-item-split-panel {
                    width: 100%;
                    flex: 1 1 auto;
                    position: static;
                    max-height: none;
                }
            }
            .leb-split-active .frappe-list .list-row-container {
                cursor: pointer;
            }
            .leb-split-active .frappe-list .list-row-container.leb-row-selected .list-row {
                background-color: var(--bg-blue, #eff6ff) !important;
                box-shadow: inset 3px 0 0 var(--blue-500, #2563eb);
            }
            .leb-list-thumb {
                width: 30px;
                height: 30px;
                border-radius: 5px;
                object-fit: contain;
                border: 1px solid var(--border-color, #e2e8f0);
                background: #ffffff;
                margin-right: 7px;
                vertical-align: middle;
                flex-shrink: 0;
            }
            .leb-list-thumb-empty {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                width: 30px;
                height: 30px;
                border-radius: 5px;
                border: 1px solid var(--border-color, #e2e8f0);
                background: var(--subtle-fg, #f8fafc);
                color: var(--text-muted, #94a3b8);
                font-size: 11px;
                margin-right: 7px;
                vertical-align: middle;
                flex-shrink: 0;
            }
            .leb-sp-hdr {
                display: flex;
                align-items: center;
                justify-content: space-between;
                padding: 10px 14px;
                border-bottom: 1px solid var(--border-color, #e2e8f0);
                background: var(--subtle-fg, #f8fafc);
                gap: 8px;
            }
            .leb-sp-tabs {
                display: inline-flex;
                background: var(--control-bg, #e2e8f0);
                padding: 2px;
                border-radius: 6px;
                gap: 2px;
            }
            .leb-sp-tab {
                border: none;
                background: transparent;
                color: var(--text-muted, #475569);
                font-size: 11.5px;
                font-weight: 600;
                padding: 3px 10px;
                border-radius: 5px;
                cursor: pointer;
            }
            .leb-sp-tab.active {
                background: var(--card-bg, #ffffff);
                color: var(--text-color, #0f172a);
                box-shadow: 0 1px 2px rgba(15, 23, 42, 0.08);
            }
            .leb-sp-body {
                padding: 12px 14px;
                overflow-y: auto;
                flex: 1 1 auto;
            }
            .leb-sp-kv {
                display: grid;
                grid-template-columns: 112px 1fr;
                gap: 4px 8px;
                font-size: 12px;
                padding: 5px 0;
                border-bottom: 1px solid var(--border-color, #f1f5f9);
            }
            .leb-sp-kv:last-child {
                border-bottom: none;
            }
            .leb-sp-kv-lbl {
                color: var(--text-muted, #64748b);
                font-weight: 500;
            }
            .leb-sp-kv-val {
                color: var(--text-color, #0f172a);
                font-weight: 600;
                word-break: break-word;
            }
        `;
        document.head.appendChild(st);
    }

    async function abrirDialogoAgregarItemASolicitud(ficha) {
        const { message: opts } = await frappe.call({
            method: "lebaref_compras.ux_lebaref.opciones_agregar_a_solicitud",
            type: "GET",
            freeze: true,
        });
        const borradores = (opts && opts.borradores) || [];
        const confirmadas = (opts && opts.confirmadas) || [];
        const obras = (opts && opts.obras) || [];
        const deptos = (opts && opts.departamentos) || [];

        const mapSol = {};
        const listaOpcionesModo = ["Crear nueva solicitud con este artículo"];
        if (borradores.length) listaOpcionesModo.unshift("Agregar a solicitud en borrador existente");
        if (confirmadas.length) listaOpcionesModo.push("Crear anexo vinculado a solicitud activa (SOL-03)");

        for (const b of borradores) mapSol[b.etiqueta] = b.id;
        for (const c of confirmadas) mapSol[c.etiqueta] = c.id;

        const d = new frappe.ui.Dialog({
            title: `+ Agregar a Solicitud · ${ficha.codigo_barras}`,
            fields: [
                {
                    fieldname: "_art",
                    fieldtype: "HTML",
                    options: `<div class="p-2 mb-2" style="background:var(--subtle-fg,#f8fafc);border:1px solid var(--border-color,#e2e8f0);border-radius:6px;font-size:12px;">
                        <b>${frappe.utils.escape_html(ficha.nombre)}</b><br>
                        <span class="text-muted">ID: ${frappe.utils.escape_html(ficha.codigo_interno)} · Clave: ${frappe.utils.escape_html(ficha.codigo_barras)} · Stock actual: ${ficha.stock_total} ${frappe.utils.escape_html(ficha.uom_almacen)}</span>
                    </div>`,
                },
                {
                    fieldname: "modo_lbl",
                    label: "Destino",
                    fieldtype: "Select",
                    options: listaOpcionesModo.join("\n"),
                    default: listaOpcionesModo[0],
                    reqd: 1,
                },
                {
                    fieldname: "cantidad",
                    label: `Cantidad a solicitar (${ficha.uom_compra})`,
                    fieldtype: "Float",
                    default: 1,
                    reqd: 1,
                },
                {
                    fieldname: "sol_borrador",
                    label: "Solicitud en borrador",
                    fieldtype: "Select",
                    options: borradores.map((x) => x.etiqueta).join("\n"),
                    default: borradores[0] ? borradores[0].etiqueta : "",
                    depends_on: "eval:doc.modo_lbl === 'Agregar a solicitud en borrador existente'",
                },
                {
                    fieldname: "sol_activa",
                    label: "Solicitud confirmada origen",
                    fieldtype: "Select",
                    options: confirmadas.map((x) => x.etiqueta).join("\n"),
                    default: confirmadas[0] ? confirmadas[0].etiqueta : "",
                    depends_on: "eval:doc.modo_lbl && doc.modo_lbl.startsWith('Crear anexo')",
                },
                {
                    fieldname: "nombre_solicitud",
                    label: "Nombre descriptivo de la nueva solicitud",
                    fieldtype: "Data",
                    default: `Pedido · ${ficha.nombre.slice(0, 48)}`,
                    depends_on: "eval:doc.modo_lbl === 'Crear nueva solicitud con este artículo'",
                },
                {
                    fieldname: "obra",
                    label: "Obra / Proyecto (opcional)",
                    fieldtype: "Select",
                    options: ["", ...obras].join("\n"),
                },
                {
                    fieldname: "departamento",
                    label: "Departamento (opcional)",
                    fieldtype: "Select",
                    options: ["", ...deptos].join("\n"),
                    depends_on: "eval:doc.modo_lbl === 'Crear nueva solicitud con este artículo'",
                },
                {
                    fieldname: "solicitante",
                    label: "Solicitante (opcional)",
                    fieldtype: "Data",
                    depends_on: "eval:doc.modo_lbl === 'Crear nueva solicitud con este artículo'",
                },
            ],
            primary_action_label: "Guardar en Solicitud",
            async primary_action(vals) {
                let modo = "nueva";
                let solId = "";
                if (vals.modo_lbl === "Agregar a solicitud en borrador existente") {
                    modo = "borrador";
                    solId = mapSol[vals.sol_borrador] || "";
                } else if (vals.modo_lbl.startsWith("Crear anexo")) {
                    modo = "anexo";
                    solId = mapSol[vals.sol_activa] || "";
                }
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.ux_lebaref.agregar_item_a_solicitud",
                    type: "POST",
                    args: {
                        modo,
                        item_code: ficha.codigo_interno,
                        cantidad: vals.cantidad,
                        solicitud: solId,
                        obra: vals.obra || "",
                        departamento: vals.departamento || "",
                        solicitante: vals.solicitante || "",
                        nombre_solicitud: vals.nombre_solicitud || "",
                    },
                    freeze: true,
                    freeze_message: "Agregando artículo a la solicitud…",
                });
                d.hide();
                frappe.show_alert({
                    message: `Artículo agregado en ${res.folio}`,
                    indicator: "green",
                });
            },
        });
        d.show();
    }

    function asegurarListenersVentanaSplitCatalogo() {
        if (window.__lebSplitWindowListenersBound) return;
        window.__lebSplitWindowListenersBound = true;

        // Interceptar clic simple en cualquier fila de la lista izquierda (fase captura en window) para NO salir de la lista
        window.addEventListener(
            "click",
            (ev) => {
                const target = ev.target;
                if (!target || typeof target.closest !== "function") return;
                const wrapper = target.closest(".leb-split-wrapper.leb-split-active");
                if (!wrapper) return;
                const inResult = target.closest(".frappe-list .result");
                if (!inResult) return;
                if (target.closest(".list-row-checkbox, input[type='checkbox'], .select-like, .list-row-like, .like-action, .list-row-head")) {
                    return;
                }
                const rowContainer = target.closest(".list-row-container");
                if (!rowContainer) return;
                const code = extraerCodigoDeFila(rowContainer);
                if (!code) return;
                ev.preventDefault();
                ev.stopPropagation();
                if (typeof ev.stopImmediatePropagation === "function") ev.stopImmediatePropagation();
                const lv = wrapper.__lebListview || (window.cur_list && window.cur_list.doctype === "Item" ? window.cur_list : null);
                if (lv) seleccionarItemEnSplit(lv, code);
            },
            true
        );

        // Doble clic en una fila abre el formulario completo de ERPNext si se desea ver el detalle extendido
        window.addEventListener(
            "dblclick",
            (ev) => {
                const target = ev.target;
                if (!target || typeof target.closest !== "function") return;
                const wrapper = target.closest(".leb-split-wrapper");
                if (!wrapper) return;
                const rowContainer = target.closest(".frappe-list .result .list-row-container");
                if (!rowContainer) return;
                const code = extraerCodigoDeFila(rowContainer);
                if (code) frappe.set_route("Form", "Item", code);
            },
            true
        );
    }

    function inicializarSplitCatalogoItem(listview) {
        inyectarEstilosSplitCatalogo();
        asegurarListenersVentanaSplitCatalogo();
        const $page = listview.$page;
        if (!$page || !$page.length) return;

        if (!listview.__lebSplit) {
            const estado = {
                itemActual: "",
                tabActiva: "ficha",
                oculto: false,
                fichaCache: null,
                cargando: false,
            };
            listview.__lebSplit = estado;

            $page.addClass("leb-split-active-page");
            const $wrapper = $page.find(".layout-main-section-wrapper").first();
            $wrapper.addClass("leb-split-wrapper leb-split-active");
            if ($wrapper.get(0)) $wrapper.get(0).__lebListview = listview;

            const $panel = $(`
                <aside class="leb-item-split-panel">
                    <div class="leb-sp-hdr">
                        <div style="font-weight:700;font-size:13.5px;">Producto</div>
                        <div class="d-flex align-items-center" style="gap:6px;">
                            <div class="leb-sp-tabs">
                                <button type="button" class="leb-sp-tab active" data-tab="ficha">Ficha</button>
                                <button type="button" class="leb-sp-tab" data-tab="editar">Editar</button>
                            </div>
                            <button type="button" class="btn btn-xs btn-primary leb-sp-new" title="Dar de alta un nuevo artículo">+ Nuevo</button>
                        </div>
                    </div>
                    <div class="leb-sp-body">
                        <div class="text-muted small py-4 text-center">Selecciona un artículo de la lista izquierda para ver su ficha técnica y fotografía.</div>
                    </div>
                </aside>
            `).appendTo($wrapper);
            estado.$panel = $panel;
            estado.$wrapper = $wrapper;
            estado.$page = $page;

            $panel.find(".leb-sp-new").on("click", () => {
                abrirOperacion("articulo", "", "Alta Rápida de Artículo", (res) => {
                    listview.refresh();
                    if (res && res.folio) {
                        estado.tabActiva = "editar";
                        seleccionarItemEnSplit(listview, res.folio);
                    }
                });
            });

            $panel.find(".leb-sp-tab").on("click", function () {
                estado.tabActiva = $(this).data("tab");
                $panel.find(".leb-sp-tab").each(function () {
                    $(this).toggleClass("active", $(this).data("tab") === estado.tabActiva);
                });
                pintarContenidoSplit(listview);
            });

            // Soporte de flechas Arriba/Abajo para recorrer el catálogo viendo fotos al instante y Ctrl+V en Editar
            $(document).off("keydown.lebItemSplit").on("keydown.lebItemSplit", (ev) => {
                const route = frappe.get_route();
                if (!route || route[0] !== "List" || route[1] !== "Item" || estado.oculto) return;
                if ($(ev.target).is("input, textarea, select, [contenteditable]")) return;
                if (ev.key !== "ArrowDown" && ev.key !== "ArrowUp") return;
                const lista = (listview.data || []).map((d) => d.name).filter(Boolean);
                if (!lista.length) return;
                ev.preventDefault();
                const idx = lista.indexOf(estado.itemActual);
                const nextIdx = ev.key === "ArrowDown"
                    ? Math.min(lista.length - 1, idx + 1)
                    : Math.max(0, (idx === -1 ? 0 : idx - 1));
                seleccionarItemEnSplit(listview, lista[nextIdx]);
            });

            $(document).off("paste.lebItemSplit").on("paste.lebItemSplit", (ev) => {
                const route = frappe.get_route();
                if (!route || route[0] !== "List" || route[1] !== "Item" || estado.oculto) return;
                if (estado.tabActiva !== "editar" || !estado.fichaCache) return;
                const clip = ev.originalEvent && ev.originalEvent.clipboardData;
                if (!clip || !clip.items) return;
                for (const item of clip.items) {
                    if (item.type && item.type.startsWith("image/")) {
                        ev.preventDefault();
                        const blob = item.getAsFile();
                        if (blob) subirArchivoImagenSplit(listview, estado.fichaCache.codigo_interno, blob);
                        break;
                    }
                }
            });
        }
    }

    function extraerCodigoDeFila(rowContainer) {
        const chk = rowContainer.querySelector(".list-row-checkbox[data-name]");
        if (chk && chk.getAttribute("data-name")) return chk.getAttribute("data-name");
        const lnk = rowContainer.querySelector("a[data-doctype='Item'][data-name], a[data-name]");
        if (lnk && lnk.getAttribute("data-name")) return lnk.getAttribute("data-name");
        const hrefEl = rowContainer.querySelector("a[href*='/app/item/']");
        if (hrefEl) {
            const p = hrefEl.getAttribute("href").split("/app/item/")[1];
            if (p) return decodeURIComponent(p.split("?")[0]);
        }
        return "";
    }

    function decorarMiniaturasYSeleccionLista(listview) {
        const estado = listview.__lebSplit;
        if (!estado || !listview.$result) return;
        const porNombre = {};
        for (const r of listview.data || []) {
            if (r && r.name) porNombre[r.name] = r;
        }
        listview.$result.find(".list-row-container").each(function () {
            const code = extraerCodigoDeFila(this);
            if (!code) return;
            $(this).toggleClass("leb-row-selected", code === estado.itemActual);
            const $subj = $(this).find(".list-subject").first();
            if ($subj.length && !$subj.find(".leb-list-thumb, .leb-list-thumb-empty").length) {
                const rowData = porNombre[code] || {};
                const imgUrl = rowData.image || "";
                const $anchor = $subj.find("a").first();
                const thumbEl = imgUrl
                    ? $(`<img class="leb-list-thumb" src="${frappe.utils.escape_html(imgUrl)}" alt="">`)
                    : $(`<span class="leb-list-thumb-empty">📦</span>`);
                if ($anchor.length) thumbEl.insertBefore($anchor);
                else $subj.prepend(thumbEl);
            }
        });
    }

    async function seleccionarItemEnSplit(listview, itemCode) {
        const estado = listview.__lebSplit;
        if (!estado || !itemCode) return;
        estado.itemActual = itemCode;
        decorarMiniaturasYSeleccionLista(listview);

        const $body = estado.$panel.find(".leb-sp-body");
        $body.html(`<div class="text-muted small py-4 text-center">Cargando ficha de <b>${frappe.utils.escape_html(itemCode)}</b>…</div>`);
        try {
            const { message: f } = await frappe.call({
                method: "lebaref_compras.ux_lebaref.ficha_producto",
                type: "GET",
                args: { item_code: itemCode },
            });
            if (estado.itemActual !== itemCode) return;
            estado.fichaCache = f;
            pintarContenidoSplit(listview);
        } catch (e) {
            $body.html(`<div class="text-muted small py-3 text-center">No se pudo cargar el detalle de ${frappe.utils.escape_html(itemCode)}.</div>`);
        }
    }

    function subirArchivoImagenSplit(listview, itemCode, fileObj) {
        const reader = new FileReader();
        reader.onload = async (ev) => {
            await frappe.call({
                method: "lebaref_compras.ux_lebaref.subir_imagen_catalogo",
                type: "POST",
                args: {
                    item_code: itemCode,
                    filename: fileObj.name || `${itemCode}.png`,
                    content_base64: String(ev.target.result || ""),
                },
                freeze: true,
                freeze_message: "Guardando fotografía del producto…",
            });
            frappe.show_alert({ message: "Fotografía actualizada", indicator: "green" });
            listview.refresh();
            seleccionarItemEnSplit(listview, itemCode);
        };
        reader.readAsDataURL(fileObj);
    }

    function pintarContenidoSplit(listview) {
        const estado = listview.__lebSplit;
        if (!estado || !estado.$panel) return;
        const $body = estado.$panel.find(".leb-sp-body");
        const f = estado.fichaCache;
        estado.$panel.find(".leb-sp-tab").each(function () {
            $(this).toggleClass("active", $(this).data("tab") === estado.tabActiva);
        });

        if (!f) {
            $body.html(`<div class="text-muted small py-4 text-center">Selecciona un producto del catálogo a la izquierda para ver su ficha y fotografía.</div>`);
            return;
        }

        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

        if (estado.tabActiva === "editar") {
            if (!f.puede_editar) {
                $body.html(`<div class="alert alert-warning small mb-0">Tu perfil tiene acceso de lectura al catálogo. Solicita al responsable para editar esta ficha.</div>`);
                return;
            }
            const valLimpio = (v) => (!v || v === "—" ? "" : v);
            const optsCat = (f.grupos_disponibles || [])
                .map((g) => `<option value="${esc(g)}" ${g === f.categoria ? "selected" : ""}>${esc(g)}</option>`)
                .join("");
            const prevImg = f.imagen
                ? `<img src="${esc(f.imagen)}" style="max-height:108px;max-width:100%;object-fit:contain;">`
                : `<span class="text-muted small">Sin imagen (pega con Ctrl+V o sube archivo)</span>`;

            $body.html(`
                <div>
                    <div style="height:118px;border:1px solid var(--border-color,#cbd5e1);border-radius:8px;background:#fff;display:flex;align-items:center;justify-content:center;padding:4px;margin-bottom:8px;">
                        ${prevImg}
                    </div>
                    <div class="d-flex mb-3" style="gap:6px;">
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-ed-upload">📁 Subir foto</button>
                        <input type="file" accept="image/*" class="d-none leb-ed-file">
                        <span class="text-muted small align-self-center" style="font-size:11px;">o presiona <b>Ctrl+V</b></span>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Código Interno</label>
                            <input type="text" class="form-control input-xs" value="${esc(f.codigo_interno)}" readonly>
                        </div>
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Clave / Cód. Barras</label>
                            <input type="text" class="form-control input-xs leb-ed-cb" value="${esc(valLimpio(f.codigo_barras))}">
                        </div>
                    </div>

                    <div class="mb-2">
                        <label class="control-label small mb-1">Nombre del Producto</label>
                        <input type="text" class="form-control input-xs leb-ed-nom" value="${esc(f.nombre)}">
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Categoría</label>
                            <select class="form-control input-xs leb-ed-cat">${optsCat}</select>
                        </div>
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Ubicación / Anaquel</label>
                            <input type="text" class="form-control input-xs leb-ed-ubi" value="${esc(valLimpio(f.ubicacion))}">
                        </div>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Marca</label>
                            <input type="text" class="form-control input-xs leb-ed-marca" value="${esc(valLimpio(f.marca))}">
                        </div>
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Modelo</label>
                            <input type="text" class="form-control input-xs leb-ed-modelo" value="${esc(valLimpio(f.modelo))}">
                        </div>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Medidas</label>
                            <input type="text" class="form-control input-xs leb-ed-medidas" value="${esc(valLimpio(f.medidas))}">
                        </div>
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Capacidad / Uso</label>
                            <input type="text" class="form-control input-xs leb-ed-cap" value="${esc(valLimpio(f.capacidad))}">
                        </div>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Especificación 1</label>
                            <input type="text" class="form-control input-xs leb-ed-esp1" value="${esc(valLimpio(f.espec1))}">
                        </div>
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Especificación 2</label>
                            <input type="text" class="form-control input-xs leb-ed-esp2" value="${esc(valLimpio(f.espec2))}">
                        </div>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-3">
                            <label class="control-label small mb-1">Stock Mínimo</label>
                            <input type="number" step="0.01" min="0" class="form-control input-xs leb-ed-min" value="${f.stock_minimo || 0}">
                        </div>
                        <div class="col-6 px-1 mb-3">
                            <label class="control-label small mb-1">Último Costo s/IVA</label>
                            <input type="number" step="0.01" min="0" class="form-control input-xs leb-ed-costo" value="${f.ultimo_costo || 0}">
                        </div>
                    </div>

                    <button type="button" class="btn btn-primary btn-sm btn-block leb-ed-save">💾 Guardar cambios en ficha</button>
                    <button type="button" class="btn btn-default btn-xs btn-block mt-2 leb-ed-full">Abrir formulario avanzado de ERPNext ↗</button>
                </div>
            `);

            $body.find(".leb-ed-upload").on("click", () => $body.find(".leb-ed-file").trigger("click"));
            $body.find(".leb-ed-file").on("change", function () {
                const file = this.files && this.files[0];
                if (file) subirArchivoImagenSplit(listview, f.codigo_interno, file);
            });
            $body.find(".leb-ed-full").on("click", () => frappe.set_route("Form", "Item", f.codigo_interno));
            $body.find(".leb-ed-save").on("click", async () => {
                const payload = {
                    codigo_barras: $body.find(".leb-ed-cb").val(),
                    nombre: $body.find(".leb-ed-nom").val(),
                    categoria: $body.find(".leb-ed-cat").val(),
                    ubicacion: $body.find(".leb-ed-ubi").val(),
                    marca: $body.find(".leb-ed-marca").val(),
                    modelo: $body.find(".leb-ed-modelo").val(),
                    medidas: $body.find(".leb-ed-medidas").val(),
                    capacidad: $body.find(".leb-ed-cap").val(),
                    espec1: $body.find(".leb-ed-esp1").val(),
                    espec2: $body.find(".leb-ed-esp2").val(),
                    stock_minimo: $body.find(".leb-ed-min").val(),
                    ultimo_costo: $body.find(".leb-ed-costo").val(),
                };
                const { message: nuevaFicha } = await frappe.call({
                    method: "lebaref_compras.ux_lebaref.actualizar_ficha_producto",
                    type: "POST",
                    args: { item_code: f.codigo_interno, datos: JSON.stringify(payload) },
                    freeze: true,
                    freeze_message: "Guardando ficha del producto…",
                });
                estado.fichaCache = nuevaFicha;
                estado.tabActiva = "ficha";
                frappe.show_alert({ message: "Ficha del producto actualizada", indicator: "green" });
                listview.refresh();
                pintarContenidoSplit(listview);
            });
            return;
        }

        // Pestaña 1: FICHA (lectura inmediata estilo ERP original integrada en diseño ERPNext)
        const bajoMinimo = f.es_inventariable && f.stock_minimo > 0 && f.stock_total <= f.stock_minimo;
        const pillClase = !f.es_inventariable
            ? "gray"
            : (bajoMinimo ? "orange" : (f.stock_total > 0 ? "green" : "gray"));
        const pillTexto = !f.es_inventariable
            ? "Servicio"
            : (bajoMinimo ? "Bajo mínimo" : (f.stock_total > 0 ? "En stock" : "Sin stock"));

        const imgBox = f.imagen
            ? `<img src="${esc(f.imagen)}" alt="${esc(f.nombre)}" class="leb-sp-zoom" style="max-height:162px;max-width:100%;object-fit:contain;cursor:zoom-in;">`
            : `<div class="text-muted text-center" style="font-size:12px;"><div style="font-size:28px;opacity:0.45;">📦</div>Sin fotografía registrada</div>`;

        const convDetalle = f.factor_compra > 1
            ? `1 ${esc(f.uom_compra)} = ${f.factor_compra} ${esc(f.uom_almacen)}`
            : `${esc(f.uom_almacen)} (1:1)`;

        const filasKv = [
            ["Categoría", f.categoria],
            ["Marca", f.marca],
            ["Modelo", f.modelo],
            ["Medidas", f.medidas],
            ["Capacidad / Uso", f.capacidad],
            ["Especificaciones", [f.espec1, f.espec2].filter((x) => x && x !== "—").join(" · ") || "—"],
            ["Unidad / Conv.", convDetalle],
        ]
            .map(([k, v]) => `<div class="leb-sp-kv"><div class="leb-sp-kv-lbl">${esc(k)}</div><div class="leb-sp-kv-val">${esc(v || "—")}</div></div>`)
            .join("");

        const xmlChips = (f.claves_proveedores || []).length
            ? `<div class="mt-2 pt-2" style="border-top:1px solid var(--border-color,#e2e8f0);font-size:11px;">
                <div class="text-muted mb-1">Claves XML de Proveedor:</div>
                ${f.claves_proveedores.map((c) => `<span class="badge badge-light mr-1 mb-1" style="border:1px solid var(--border-color,#cbd5e1);">${esc(c.proveedor)}: <b>${esc(c.clave)}</b></span>`).join("")}
               </div>`
            : "";

        $body.html(`
            <div>
                <div style="position:relative;height:172px;border:1px solid var(--border-color,#e2e8f0);border-radius:8px;background:#ffffff;display:flex;align-items:center;justify-content:center;padding:6px;margin-bottom:10px;">
                    <span style="position:absolute;top:6px;left:6px;background:var(--subtle-fg,#f1f5f9);color:var(--text-muted,#475569);font-size:10px;font-weight:700;padding:2px 7px;border-radius:4px;max-width:58%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${esc(f.categoria)}</span>
                    <span style="position:absolute;top:6px;right:6px;background:#0f172a;color:#fff;font-family:Consolas,monospace;font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:4px;">${esc(f.codigo_interno)}</span>
                    ${imgBox}
                </div>

                <div style="font-size:15px;font-weight:700;line-height:1.3;color:var(--text-color,#0f172a);">${esc(f.nombre)}</div>
                <div style="font-family:Consolas,monospace;font-size:11.5px;color:var(--text-muted,#64748b);margin:2px 0 10px 0;">
                    Clave / Cód. Barras: <b style="color:var(--text-color,#0f172a);">${esc(f.codigo_barras)}</b>
                </div>

                <div style="padding:10px 12px;border-radius:8px;background:var(--subtle-fg,#f8fafc);border:1px solid var(--border-color,#e2e8f0);margin-bottom:10px;">
                    <div class="d-flex justify-content-between align-items-start">
                        <div>
                            <div style="font-size:11px;color:var(--text-muted,#64748b);">Existencia</div>
                            <div style="font-size:20px;font-weight:700;line-height:1.2;">${f.es_inventariable ? `${f.stock_total} ${esc(f.uom_almacen)}` : "No inventariable"}</div>
                            <div style="font-size:11.5px;color:var(--text-muted,#64748b);margin-top:2px;">
                                Mínimo: <b>${f.stock_minimo}</b> &nbsp;·&nbsp; Costo: <b style="color:#059669;">$${money.format(f.ultimo_costo || 0)}</b>
                            </div>
                        </div>
                        <span class="indicator-pill ${pillClase}">${esc(pillTexto)}</span>
                    </div>
                </div>

                <div class="d-flex justify-content-between align-items-center mb-2 pb-2" style="border-bottom:1px solid var(--border-color,#e2e8f0);">
                    <div>
                        <div style="font-size:11px;color:var(--text-muted,#64748b);">Ubicación en almacén</div>
                        <div style="font-family:Consolas,monospace;font-size:13.5px;font-weight:700;">📍 ${esc(f.ubicacion)}</div>
                    </div>
                    <button type="button" class="btn btn-default btn-xs leb-sp-mapa">🗺️ Ver mapa</button>
                </div>

                <div class="mb-2">${filasKv}</div>
                ${xmlChips}

                <div class="mt-2 p-1 text-center" style="background:#fff;border:1px solid var(--border-color,#e2e8f0);border-radius:6px;">
                    ${f.svg_barcode || ""}
                </div>

                <div class="mt-3">
                    <button type="button" class="btn btn-primary btn-sm btn-block leb-sp-add-sol">+ Agregar a solicitud</button>
                    <div class="d-flex mt-2" style="gap:6px;">
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sp-edit">✏️ Editar</button>
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sp-lbl">🏷️ Etiqueta</button>
                    </div>
                    <div class="d-flex mt-1" style="gap:6px;">
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sp-hist">💰 Historial precios</button>
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sp-open">Abrir completo ↗</button>
                    </div>
                </div>
            </div>
        `);

        $body.find(".leb-sp-zoom").on("click", () => {
            const dlgImg = new frappe.ui.Dialog({
                title: `${f.codigo_barras} · ${f.nombre}`,
                size: "large",
                fields: [{
                    fieldname: "img_full",
                    fieldtype: "HTML",
                    options: `<div class="text-center p-2"><img src="${esc(f.imagen)}" style="max-width:100%;max-height:72vh;object-fit:contain;border-radius:8px;"></div>`,
                }],
            });
            dlgImg.show();
        });
        $body.find(".leb-sp-mapa").on("click", () => abrirMapaUbicaciones());
        $body.find(".leb-sp-add-sol").on("click", () => abrirDialogoAgregarItemASolicitud(f));
        $body.find(".leb-sp-edit").on("click", () => {
            estado.tabActiva = "editar";
            pintarContenidoSplit(listview);
        });
        $body.find(".leb-sp-lbl").on("click", () => abrirImpresorCodigosCarta("articulos", f.codigo_interno));
        $body.find(".leb-sp-hist").on("click", () => abrirArchivoHistorico("precios", f.codigo_interno));
        $body.find(".leb-sp-open").on("click", () => frappe.set_route("Form", "Item", f.codigo_interno));
    }

    // --- Registro idempotente de ListView Settings (resistente a sobrescritura posterior de *_list.js de ERPNext) ---
    const listActions = [
        ["Material Request", "Captura Rápida Lebaref (con Conceptos Libres)", "solicitud"],
        ["Purchase Receipt", "Entrada Anticipada sin OC (ALM-02)", "entrada_sin_oc"],
        ["Stock Entry", "Entregar Material a Obra", "entregar"],
        ["Supplier", "Alta Rápida con Contacto", "proveedor"],
        ["Lebaref Herramienta", "Nueva Herramienta (HER)", "herramienta"],
        ["Lebaref Kit", "Nuevo Maletín / Kit (KIT)", "kit"],
        ["Lebaref Prestamo", "Nuevo Vale de Préstamo", "prestamo"],
        ["Lebaref Vehiculo", "Nuevo Vehículo (VEH)", "vehiculo"],
    ];

    const ORIGEN_IMPRESION_LISTA = {
        "Lebaref Herramienta": "herramientas",
        "Lebaref Kit": "kits",
        "Lebaref Vehiculo": "vehiculos",
    };

    function aplicarListviewSettings() {
        if (!window.frappe || !frappe.listview_settings) return;

        for (const [dt, label, accion] of listActions) {
            const prev = frappe.listview_settings[dt] || {};
            if (prev.__lebWrapped) continue;
            const prevOnload = prev.onload;
            frappe.listview_settings[dt] = Object.assign({}, prev, {
                __lebWrapped: true,
                onload(listview) {
                    if (typeof prevOnload === "function") prevOnload(listview);
                    if (listview.__lebBtnsReady) return;
                    listview.__lebBtnsReady = true;
                    listview.page.add_inner_button(label, () => abrirOperacion(accion, "", label, () => listview.refresh()));
                    if (dt === "Stock Entry") {
                        listview.page.add_inner_button("📋 Sobrante Histórico c/Acta (ALM-07)", () =>
                            abrirOperacion("sobrante", "", "Registrar Sobrante Histórico con Acta (ALM-07)", () => listview.refresh())
                        );
                    }
                    if (dt === "Supplier") {
                        listview.page.add_inner_button("📊 ¿A quién cotizar? (Directorio / Sheets)", () => abrirDirectorioProveedores());
                        listview.page.add_inner_button("📜 Histórico de Precios y CFDIs", () => abrirArchivoHistorico("precios"));
                    } else {
                        listview.page.add_inner_button("🔫 Escanear S30W (F2)", () => abrirEscaneoPistola());
                    }
                    if (ORIGEN_IMPRESION_LISTA[dt]) {
                        listview.page.add_inner_button("🏷️ Hojas de Códigos / Etiquetas Carta", () =>
                            abrirImpresorCodigosCarta(ORIGEN_IMPRESION_LISTA[dt])
                        );
                    }
                    if (dt === "Lebaref Herramienta") {
                        listview.page.add_inner_button("🗺️ Mapa de Ubicaciones (A–F)", () => abrirMapaUbicaciones());
                    }
                },
            });
        }

        for (const [dtHist, tabDef] of [["Purchase Order", "ordenes"], ["Lebaref CFDI", "cfdis"]]) {
            const prevH = frappe.listview_settings[dtHist] || {};
            if (prevH.__lebWrapped) continue;
            const prevHOnload = prevH.onload;
            frappe.listview_settings[dtHist] = Object.assign({}, prevH, {
                __lebWrapped: true,
                onload(listview) {
                    if (typeof prevHOnload === "function") prevHOnload(listview);
                    if (listview.__lebBtnsReady) return;
                    listview.__lebBtnsReady = true;
                    if (dtHist === "Purchase Order") {
                        listview.page.add_inner_button("🔫 Escanear S30W (F2)", () => abrirEscaneoPistola());
                    }
                    listview.page.add_inner_button("📜 Archivo Histórico SQLite (CFDIs / Precios / OCs)", () => abrirArchivoHistorico(tabDef));
                },
            });
        }

        const prevCust = frappe.listview_settings["Customer"] || {};
        if (!prevCust.__lebWrapped) {
            const prevCustOnload = prevCust.onload;
            frappe.listview_settings["Customer"] = Object.assign({}, prevCust, {
                __lebWrapped: true,
                onload(listview) {
                    if (typeof prevCustOnload === "function") prevCustOnload(listview);
                    if (listview.__lebBtnsReady) return;
                    listview.__lebBtnsReady = true;
                    listview.page.add_inner_button("➕ Nuevo Cliente Rápido (RFC y Contacto)", () =>
                        abrirDialogoNuevoCliente(() => listview.refresh())
                    );
                },
            });
        }

        const prevProj = frappe.listview_settings["Project"] || {};
        if (!prevProj.__lebWrapped) {
            const prevProjOnload = prevProj.onload;
            frappe.listview_settings["Project"] = Object.assign({}, prevProj, {
                __lebWrapped: true,
                onload(listview) {
                    if (typeof prevProjOnload === "function") prevProjOnload(listview);
                    if (listview.__lebBtnsReady) return;
                    listview.__lebBtnsReady = true;
                    listview.page.add_inner_button("➕ Nueva Obra / Proyecto", () =>
                        abrirOperacion("obra", "", "Nueva Obra / Proyecto", () => listview.refresh())
                    );
                },
            });
        }

        // Lista de Item (Artículos): Vista dividida Maestro–Detalle (Lista a la izquierda + Ficha con foto a la derecha)
        const prevItem = frappe.listview_settings["Item"] || {};
        if (!prevItem.__lebWrapped) {
            const prevItemOnload = prevItem.onload;
            const prevItemRefresh = prevItem.refresh;
            const camposExtraItem = [...new Set([...(prevItem.add_fields || []), "image", "item_name", "item_group", "stock_uom", "custom_lebaref_codigo_barras", "custom_lebaref_ubicacion"])];

            frappe.listview_settings["Item"] = Object.assign({}, prevItem, {
                __lebWrapped: true,
                add_fields: camposExtraItem,
                onload(listview) {
                    if (typeof prevItemOnload === "function") prevItemOnload(listview);
                    inicializarSplitCatalogoItem(listview);
                    if (listview.__lebBtnsReady) return;
                    listview.__lebBtnsReady = true;
                    listview.page.add_inner_button("◫ Mostrar / Ocultar Ficha", () => {
                        const est = listview.__lebSplit;
                        if (!est || !est.$panel) return;
                        est.oculto = !est.oculto;
                        est.$panel.toggleClass("leb-hidden", est.oculto);
                        est.$wrapper.toggleClass("leb-split-active", !est.oculto);
                        if (est.$page) est.$page.toggleClass("leb-split-active-page", !est.oculto);
                    });
                    listview.page.add_inner_button("🏷️ Hojas de Códigos Carta", () => abrirImpresorCodigosCarta("articulos"));
                    listview.page.add_inner_button("🔫 Escanear S30W (F2)", () => abrirEscaneoPistola());
                    listview.page.add_inner_button("🗺️ Mapa Zonas A–F", () => abrirMapaUbicaciones());
                    listview.page.add_inner_button("💰 Histórico de Precios", () => abrirArchivoHistorico("precios"));
                },
                refresh(listview) {
                    if (typeof prevItemRefresh === "function") prevItemRefresh(listview);
                    inicializarSplitCatalogoItem(listview);
                    decorarMiniaturasYSeleccionLista(listview);
                    const est = listview.__lebSplit;
                    if (!est || est.oculto) return;
                    const visibles = (listview.data || []).map((r) => r.name).filter(Boolean);
                    if (!visibles.length) {
                        est.itemActual = "";
                        est.fichaCache = null;
                        pintarContenidoSplit(listview);
                        return;
                    }
                    if (!est.itemActual || !visibles.includes(est.itemActual)) {
                        seleccionarItemEnSplit(listview, visibles[0]);
                    }
                },
            });
        }
    }

    window.__lebarefAplicarListviewSettings = aplicarListviewSettings;
    aplicarListviewSettings();

    if (window.frappe && frappe.router && typeof frappe.router.on === "function") {
        frappe.router.on("change", () => {
            aplicarListviewSettings();
            setTimeout(() => {
                const route = frappe.get_route ? frappe.get_route() : [];
                if (route[0] === "List" && route[1] === "Item" && window.cur_list && window.cur_list.doctype === "Item") {
                    inicializarSplitCatalogoItem(window.cur_list);
                    decorarMiniaturasYSeleccionLista(window.cur_list);
                    const est = window.cur_list.__lebSplit;
                    const visibles = (window.cur_list.data || []).map((r) => r.name).filter(Boolean);
                    if (est && !est.oculto && visibles.length && (!est.itemActual || !visibles.includes(est.itemActual))) {
                        seleccionarItemEnSplit(window.cur_list, visibles[0]);
                    }
                }
            }, 150);
        });
    }
})();
