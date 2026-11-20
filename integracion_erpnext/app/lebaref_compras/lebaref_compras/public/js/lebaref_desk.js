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

    window.lebaref = {
        abrirOperacion,
        verDetalleEquipo,
        abrirMapaUbicaciones,
        abrirDirectorioProveedores,
        abrirArchivoHistorico,
        abrirImpresorCodigosCarta,
        abrirEscaneoPistola,
        SHEETS_URL,
    };

    // --- Botones guiados en Formularios de ERPNext Desk ---

    frappe.ui.form.on("Item", {
        refresh(frm) {
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

    // --- Botones rápidos en Listas (ListView) para altas protegidas, escaneo S30W e impresión Carta ---
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

    for (const [dt, label, accion] of listActions) {
        const prev = frappe.listview_settings[dt] || {};
        const prevOnload = prev.onload;
        frappe.listview_settings[dt] = Object.assign({}, prev, {
            onload(listview) {
                if (typeof prevOnload === "function") prevOnload(listview);
                listview.page.add_inner_button(label, () => abrirOperacion(accion, "", label, () => listview.refresh()));
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
        const prevHOnload = prevH.onload;
        frappe.listview_settings[dtHist] = Object.assign({}, prevH, {
            onload(listview) {
                if (typeof prevHOnload === "function") prevHOnload(listview);
                if (dtHist === "Purchase Order") {
                    listview.page.add_inner_button("🔫 Escanear S30W (F2)", () => abrirEscaneoPistola());
                }
                listview.page.add_inner_button("📜 Archivo Histórico SQLite (CFDIs / Precios / OCs)", () => abrirArchivoHistorico(tabDef));
            },
        });
    }

    // Lista de Item (Artículos): Alta rápida, Hojas de Códigos Carta, Escaneo S30W, Mapa A–F e Histórico
    const prevItem = frappe.listview_settings["Item"] || {};
    const prevItemOnload = prevItem.onload;
    frappe.listview_settings["Item"] = Object.assign({}, prevItem, {
        onload(listview) {
            if (typeof prevItemOnload === "function") prevItemOnload(listview);
            listview.page.add_inner_button("Alta Rápida SKU (con Conversión)", () => abrirOperacion("articulo", "", "Alta Rápida de Artículo", () => listview.refresh()));
            listview.page.add_inner_button("🏷️ Hojas de Códigos / Etiquetas Carta", () => abrirImpresorCodigosCarta("articulos"));
            listview.page.add_inner_button("🔫 Escanear S30W (F2)", () => abrirEscaneoPistola());
            listview.page.add_inner_button("🗺️ Mapa de Ubicaciones (A–F)", () => abrirMapaUbicaciones());
            listview.page.add_inner_button("💰 Banco Histórico de Precios", () => abrirArchivoHistorico("precios"));
        },
    });
})();
