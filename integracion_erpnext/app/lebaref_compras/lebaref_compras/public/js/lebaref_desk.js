(() => {
    if (window.__lebarefDeskLoaded) {
        if (typeof window.__lebarefAplicarListviewSettings === "function") {
            window.__lebarefAplicarListviewSettings();
        }
        return;
    }
    window.__lebarefDeskLoaded = true;

    // Al arrancar el ERP, reiniciar el estado guardado de secciones desplegables para que siempre inicien cerradas
    try {
        const rawSbState = localStorage.getItem("section-breaks-state");
        if (rawSbState) {
            const parsedSb = JSON.parse(rawSbState);
            if (parsedSb && typeof parsedSb === "object") {
                delete parsedSb["Lebaref Compras"];
                delete parsedSb["Lebaref"];
                localStorage.setItem("section-breaks-state", JSON.stringify(parsedSb));
            }
        }
    } catch (e) {
        // Ignorar si localStorage no está disponible
    }
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

    function lebDebounce(fn, wait = 220) {
        let timer = null;
        return function (...args) {
            const ctx = this;
            if (timer) clearTimeout(timer);
            timer = setTimeout(() => {
                timer = null;
                fn.apply(ctx, args);
            }, wait);
        };
    }

    function inyectarEstilosSkeletonLebaref() {
        if (document.getElementById("leb-skeleton-styles")) return;
        const st = document.createElement("style");
        st.id = "leb-skeleton-styles";
        st.textContent = `
            @keyframes lebShimmer {
                0% { background-position: -360px 0; }
                100% { background-position: 360px 0; }
            }
            .leb-skel-line, .leb-skel-box {
                background: #e2e8f0;
                background-image: linear-gradient(90deg, #e2e8f0 0px, #f1f5f9 120px, #e2e8f0 240px);
                background-size: 720px 100%;
                animation: lebShimmer 1.25s infinite linear;
                border-radius: 6px;
            }
            .leb-skel-line { height: 12px; margin-bottom: 8px; }
        `;
        document.head.appendChild(st);
    }

    function lebSkeletonHtml(tipo = "panel") {
        inyectarEstilosSkeletonLebaref();
        if (tipo === "tablero") {
            const card = `
                <div style="background:#fff;border:1px solid #e2e8f0;border-radius:10px;padding:12px;">
                    <div class="leb-skel-line" style="width:58%;height:14px;margin-bottom:12px;"></div>
                    <div class="leb-skel-line" style="width:92%;"></div>
                    <div class="leb-skel-line" style="width:78%;"></div>
                    <div class="leb-skel-line" style="width:85%;margin-bottom:0;"></div>
                </div>
            `;
            return `<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:10px;margin-bottom:10px;">${card}${card}${card}</div>`;
        }
        if (tipo === "tabla") {
            return `
                <div class="py-2">
                    <div class="leb-skel-line" style="width:42%;height:14px;margin-bottom:10px;"></div>
                    <div class="leb-skel-line" style="width:100%;height:26px;"></div>
                    <div class="leb-skel-line" style="width:100%;height:24px;"></div>
                    <div class="leb-skel-line" style="width:100%;height:24px;"></div>
                    <div class="leb-skel-line" style="width:94%;height:24px;margin-bottom:0;"></div>
                </div>
            `;
        }
        return `
            <div class="py-2">
                <div class="d-flex justify-content-between align-items-center mb-2">
                    <div class="leb-skel-line" style="width:46%;height:18px;margin-bottom:0;"></div>
                    <div class="leb-skel-line" style="width:24%;height:18px;margin-bottom:0;"></div>
                </div>
                <div class="leb-skel-box mb-3" style="height:86px;width:100%;"></div>
                <div class="leb-skel-line" style="width:88%;"></div>
                <div class="leb-skel-line" style="width:72%;"></div>
                <div class="leb-skel-line" style="width:94%;"></div>
                <div class="leb-skel-box mt-3" style="height:108px;width:100%;"></div>
            </div>
        `;
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
                    } else if (accion === "ajuste" && col.name === "valuation_rate" && dialog.get_value("tipo") === "Merma") {
                        rowObj.valuation_rate = "";
                        td.css({ background: "#f8fafc", color: "#94a3b8", verticalAlign: "middle" }).text("Conserva valor observado");
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

        if (accion === "ajuste" && dialog.fields_dict.tipo) {
            dialog.fields_dict.tipo.$input.on("change", () => renderRowsTable());
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

        topBar.find(".leb-map-search").on("input", lebDebounce(function () {
            textoFiltro = $(this).val() || "";
            renderPlanoYTablas();
        }, 220));
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
                    ${frappe.model.can_create("Supplier") ? `<button type="button" class="btn btn-xs btn-primary leb-dir-new">+ Nuevo proveedor</button>` : ""}
                    <button type="button" class="btn btn-xs btn-default leb-dir-copy">Copiar tabla para Sheets</button>
                    <button type="button" class="btn btn-xs btn-default leb-dir-open">Abrir Google Sheets</button>
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
                    <td class="text-right" style="white-space:nowrap;">
                        ${p.cotizaciones} / <b>${p.ordenes}</b>
                        ${frappe.model.can_write("Supplier") ? `<button type="button" class="btn btn-xs btn-default ml-1 leb-dir-edit" data-sup="${frappe.utils.escape_html(p.id)}">Editar</button>` : ""}
                    </td>
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

        bar.find(".leb-dir-q").on("input", lebDebounce(function () {
            renderDir($(this).val() || "");
        }, 220));
        bar.find(".leb-dir-copy").on("click", async () => {
            if (navigator.clipboard && dir.tsv) {
                await navigator.clipboard.writeText(dir.tsv);
                frappe.show_alert({ message: "Directorio copiado al portapapeles (listo para pegar en Google Sheets)", indicator: "green" });
            }
        });
        bar.find(".leb-dir-open").on("click", () => window.open(dir.sheets_url || SHEETS_URL, "_blank", "noopener"));
        bar.find(".leb-dir-new").on("click", () => {
            d.hide();
            abrirModalProveedorRapido("", () => abrirDirectorioProveedores());
        });
        tblHost.on("click", ".leb-dir-edit", function () {
            const supId = $(this).data("sup");
            if (supId) {
                d.hide();
                abrirModalProveedorRapido(supId, () => abrirDirectorioProveedores());
            }
        });

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
                    <button type="button" class="btn btn-default leb-htab" data-tab="precios">Banco de Precios (${precios.length})</button>
                    <button type="button" class="btn btn-default leb-htab" data-tab="cfdis">Facturas XML CFDI (${cfdis.length})</button>
                    <button type="button" class="btn btn-default leb-htab" data-tab="ordenes">OCs Cerradas (${ordenes.length})</button>
                </div>
                <div class="d-flex align-items-center flex-wrap" style="gap:6px;">
                    <input type="text" class="form-control form-control-sm leb-hist-q" placeholder="Buscar producto, proveedor, RFC, UUID o folio…" style="width:255px;">
                    <button type="button" class="btn btn-xs btn-primary leb-hist-imp">Importar Excel histórico</button>
                </div>
            </div>
        `).appendTo(host);
        nav.find(".leb-hist-q").val(query);
        nav.find(".leb-hist-imp").on("click", () => {
            d.hide();
            if (window.lebaref && window.lebaref.abrirImportadorCotizacionesHistoricas) {
                window.lebaref.abrirImportadorCotizacionesHistoricas(() => abrirArchivoHistorico("precios", query));
            }
        });

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
        nav.find(".leb-hist-q").on("input", lebDebounce(function () {
            query = $(this).val() || "";
            renderHist();
        }, 220));

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
            host.html(lebSkeletonHtml("tabla"));
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

    async function abrirGestorUsuariosOficina() {
        const { message: data } = await frappe.call({
            method: "lebaref_compras.ux_lebaref.listar_usuarios_oficina",
            type: "GET",
            freeze: true,
            freeze_message: "Consultando cuentas de la oficina…",
        });
        const esc = frappe.utils.escape_html;
        const perfiles = (data && data.perfiles) || [];
        const usuarios = (data && data.usuarios) || [];

        const abrirEditorUsuario = (usr = null) => {
            const dEd = new frappe.ui.Dialog({
                title: usr ? `✏️ Personalizar Usuario · ${usr.email}` : "➕ Nueva Cuenta de Auxiliar / Área",
                fields: [
                    {
                        fieldname: "email",
                        label: "Correo de inicio de sesión",
                        fieldtype: "Data",
                        default: usr ? usr.email : "",
                        read_only: usr ? 1 : 0,
                        reqd: 1,
                    },
                    {
                        fieldname: "first_name",
                        label: "Nombre de la persona (o puesto)",
                        fieldtype: "Data",
                        default: usr ? usr.first_name : "",
                        reqd: 1,
                    },
                    {
                        fieldname: "last_name",
                        label: "Apellidos",
                        fieldtype: "Data",
                        default: usr ? usr.last_name || "" : "",
                    },
                    {
                        fieldname: "role_profile_name",
                        label: "Perfil de Permisos Lebaref",
                        fieldtype: "Select",
                        options: perfiles.join("\n"),
                        default: usr && usr.role_profile_name ? usr.role_profile_name : (perfiles[0] || ""),
                        reqd: 1,
                    },
                    {
                        fieldname: "new_password",
                        label: usr ? "Nueva contraseña (dejar vacío para conservar actual)" : "Contraseña inicial (mín. 8 caracteres)",
                        fieldtype: "Password",
                        reqd: usr ? 0 : 1,
                    },
                    {
                        fieldname: "enabled",
                        label: "Cuenta activa (puede iniciar sesión)",
                        fieldtype: "Check",
                        default: usr ? Number(usr.enabled) : 1,
                    },
                ],
                primary_action_label: "Guardar Usuario",
                async primary_action(vals) {
                    await frappe.call({
                        method: "lebaref_compras.ux_lebaref.guardar_usuario_oficina",
                        type: "POST",
                        args: { datos: JSON.stringify(vals) },
                        freeze: true,
                        freeze_message: "Aplicando configuración de usuario…",
                    });
                    dEd.hide();
                    d.hide();
                    frappe.show_alert({ message: "Usuario de oficina actualizado", indicator: "green" });
                    abrirGestorUsuariosOficina();
                },
            });
            dEd.show();
        };

        const trs = usuarios
            .map(
                (u, i) => `<tr>
                    <td><b>${esc(u.full_name || u.first_name || u.name)}</b><br><small class="text-muted">${esc(u.email || u.name)}</small></td>
                    <td><span class="badge badge-info">${esc(u.role_profile_name || "Personalizado")}</span></td>
                    <td>${Number(u.enabled) ? '<span class="indicator-pill green">Activo</span>' : '<span class="indicator-pill gray">Inactivo</span>'}</td>
                    <td class="text-right"><button type="button" class="btn btn-xs btn-default leb-usr-edit" data-idx="${i}">✏️ Renombrar / Clave / Rol</button></td>
                </tr>`
            )
            .join("");

        const d = new frappe.ui.Dialog({
            title: "👥 Cuentas de Oficina Lebaref (Auxiliares, Finanzas y Dirección)",
            size: "large",
            fields: [
                {
                    fieldname: "_html",
                    fieldtype: "HTML",
                    options: `
                        <div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                            Aquí puedes poner el <b>nombre real de cada auxiliar</b>, cambiar su contraseña o ajustar su perfil:<br>
                            • <b>Lebaref Auxiliar Operativo Mixto:</b> Compras + Almacén + Herramientas/Flotilla, <b>sin autorizar OCs, ajustes ni pagos</b>.<br>
                            • <b>Lebaref Finanzas y Contabilidad:</b> Conciliación CFDI, Facturas/Notas y preparación de Pagos, <b>sin autorizar</b>.<br>
                            • <b>Lebaref Coordinador Operativo:</b> Autorización completa (Oscar / Dirección).
                        </div>
                        <div class="table-responsive">
                            <table class="table table-bordered table-sm mb-0" style="font-size:12px;">
                                <thead style="background:#031e34;color:#fff;">
                                    <tr><th>Nombre / Correo</th><th>Perfil Asignado</th><th>Estado</th><th class="text-right">Acción</th></tr>
                                </thead>
                                <tbody>${trs || "<tr><td colspan='4' class='text-muted'>Sin cuentas registradas.</td></tr>"}</tbody>
                            </table>
                        </div>
                    `,
                },
            ],
            primary_action_label: "➕ Crear Nueva Cuenta",
            primary_action() {
                abrirEditorUsuario(null);
            },
        });

        d.fields_dict._html.$wrapper.find(".leb-usr-edit").on("click", function () {
            const idx = Number($(this).data("idx"));
            if (usuarios[idx]) abrirEditorUsuario(usuarios[idx]);
        });
        d.show();
    }

    // Enlaces rápidos del banner superior en el Workspace Lebaref
    $(document).on(
        "click",
        'a[href="#leb-pendientes"], a[href="#leb-minimos"], a[href="#leb-analitica"], a[href="#leb-escanear-s30w"], a[href="#leb-codigos-carta"], a[href="#leb-mapa-af"], a[href="#leb-historico"], a[href="#leb-usuarios"]',
        function (ev) {
            ev.preventDefault();
            const href = $(this).attr("href");
            if (href === "#leb-pendientes") abrirTableroPendientes();
            else if (href === "#leb-minimos") abrirAlertasStockMinimo();
            else if (href === "#leb-analitica") abrirAnaliticaEjecutiva();
            else if (href === "#leb-escanear-s30w") abrirEscaneoPistola();
            else if (href === "#leb-codigos-carta") abrirImpresorCodigosCarta("articulos");
            else if (href === "#leb-mapa-af") abrirMapaUbicaciones();
            else if (href === "#leb-historico") abrirArchivoHistorico("precios");
            else if (href === "#leb-usuarios") abrirGestorUsuariosOficina();
        }
    );

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
        if (!host.children().length) {
            host.html(lebSkeletonHtml("panel"));
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
                                        <td style="width:25%;background:#f8fafc;"><b>Familia:</b> ${esc(f.familia || "—")}</td>
                                        <td style="width:25%;background:#f8fafc;"><b>Marca:</b> ${esc(f.marca)}</td>
                                        <td style="width:25%;background:#f8fafc;"><b>Modelo:</b> ${esc(f.modelo)}</td>
                                    </tr>
                                    <tr>
                                        <td><b>Medidas:</b> ${esc(f.medidas)}</td>
                                        <td><b>Capacidad / Uso:</b> ${esc(f.capacidad)}</td>
                                        <td><b>Espec. 1:</b> ${esc(f.espec1)}</td>
                                        <td><b>Espec. 2:</b> ${esc(f.espec2)}</td>
                                    </tr>
                                    <tr>
                                        <td colspan="2"><b>Gerencias usuarias:</b> ${(f.gerencias_lista || []).map((g) => `<span class="badge badge-info mr-1">${esc(g)}</span>`).join("") || "—"}</td>
                                        <td colspan="2">
                                            ${f.recotizar_siempre ? `<span class="badge badge-warning mr-1">Exige recotizar siempre</span>` : ""}
                                            ${f.consumo_directo ? `<span class="badge badge-secondary mr-1">Consumo directo (Oficina)</span>` : `<span class="text-muted">Reposición normal</span>`}
                                        </td>
                                    </tr>
                                    <tr>
                                        <td colspan="4"><b>Claves XML y Alias:</b> ${clavesXmlTxt}${(f.alias_equivalencias || []).length ? " · " + (f.alias_equivalencias || []).map((a) => `<span class="badge badge-light mr-1" style="border:1px solid #93c5fd;">${esc(a.texto)} <small class="text-muted">(${esc(a.alcance)})</small></span>`).join(" ") : ""}</td>
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
        host.html(lebSkeletonHtml("tabla"));
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
            bar.find(".leb-tabdir-q").on("input", lebDebounce(function () { pintar($(this).val() || ""); }, 220));
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

    // --- Módulos Operativos y Analíticos de Cierre de Matriz (GES-01/02, ALM-09, CAT-05, COM-08, SOL-04, FLO-02, ANA-01) ---

    async function pintarFranjaPendientesWorkspace(forzarRecarga = false) {
        const host = document.getElementById("leb-ws-live-strip");
        if (!host) return;
        if (host.dataset.loaded === "1" && !forzarRecarga) return;
        host.dataset.loaded = "1";
        if (!host.children.length || forzarRecarga) {
            host.innerHTML = lebSkeletonHtml("tablero");
        }
        try {
            const { message: tb } = await frappe.call({
                method: "lebaref_compras.flujo_operativo.tableros_inicio_lebaref",
                type: "GET",
            });
            if (!tb) return;
            const esc = frappe.utils.escape_html;
            const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            const r = tb.resumen || {};

            const filasSol = (tb.solicitudes_sin_atender || []).slice(0, 6).map((s) => `
                <div class="d-flex justify-content-between align-items-center py-1" style="border-bottom:1px solid #f1f5f9;font-size:11.5px;gap:6px;">
                    <div style="min-width:0;">
                        <a href="/app/material-request/${encodeURIComponent(s.folio)}" style="font-weight:700;color:#0d5b94;">${esc(s.folio)}</a>
                        <span class="text-truncate d-inline-block align-bottom" style="max-width:165px;"> · ${esc(s.nombre)}</span>
                        <div class="text-muted" style="font-size:10px;">${esc(s.departamento)} · ${esc(s.fecha)}</div>
                    </div>
                    <span class="badge badge-light" style="border:1px solid #cbd5e1;white-space:nowrap;">${esc(s.etapa)}</span>
                </div>
            `).join("") || `<div class="text-muted small py-2">Sin solicitudes pendientes de atender.</div>`;

            const filasPago = (tb.pendientes_pago || []).slice(0, 6).map((p) => {
                const badgeFiscal = p.expediente_ok
                    ? `<span class="badge badge-success" title="CSF y Carátula Bancaria completos">CSF/Banco OK</span>`
                    : `<span class="badge badge-warning" title="Falta CSF o Carátula Bancaria">Falta CSF/Banco</span>`;
                return `
                    <div class="d-flex justify-content-between align-items-center py-1" style="border-bottom:1px solid #f1f5f9;font-size:11.5px;gap:6px;">
                        <div style="min-width:0;">
                            <a href="/app/purchase-order/${encodeURIComponent(p.folio)}" style="font-weight:700;color:#0d5b94;">${esc(p.folio)}</a>
                            <span class="text-truncate d-inline-block align-bottom" style="max-width:140px;"> · ${esc(p.proveedor)}</span>
                            <div class="text-muted" style="font-size:10px;">$${money.format(p.total || 0)} ${esc(p.moneda)} · ${esc(p.estado)}</div>
                        </div>
                        <div class="d-flex align-items-center" style="gap:4px;flex-shrink:0;">
                            ${badgeFiscal}
                            <button type="button" class="btn btn-xs btn-default leb-tb-pago" data-po="${esc(p.folio)}" data-sup="${esc(p.supplier_id || "")}" title="Actualizar estado de pago o expediente">Gestionar</button>
                        </div>
                    </div>
                `;
            }).join("") || `<div class="text-muted small py-2">Sin compras esperando pago de Dirección.</div>`;

            const renderLogistica = (modo) => {
                if (modo === "recoleccion") {
                    return (tb.ir_a_recolectar || []).slice(0, 6).map((x) => `
                        <div class="d-flex justify-content-between align-items-center py-1" style="border-bottom:1px solid #f1f5f9;font-size:11.5px;gap:6px;">
                            <div style="min-width:0;">
                                <a href="/app/purchase-order/${encodeURIComponent(x.folio)}" style="font-weight:700;color:#059669;">${esc(x.folio)}</a>
                                <span class="text-truncate d-inline-block align-bottom" style="max-width:155px;"> · ${esc(x.proveedor)}</span>
                                <div class="text-muted" style="font-size:10px;">$${money.format(x.total || 0)} ${esc(x.moneda)} · Ir a recolectar</div>
                            </div>
                            <button type="button" class="btn btn-xs btn-default leb-tb-foto" data-po="${esc(x.folio)}">Entregado</button>
                        </div>
                    `).join("") || `<div class="text-muted small py-2">Ninguna compra pendiente de recolección.</div>`;
                }
                if (modo === "xml") {
                    return (tb.xml_pendientes || []).slice(0, 6).map((x) => `
                        <div class="d-flex justify-content-between align-items-center py-1" style="border-bottom:1px solid #f1f5f9;font-size:11.5px;gap:6px;">
                            <div style="min-width:0;">
                                <a href="/app/purchase-order/${encodeURIComponent(x.folio)}" style="font-weight:700;color:#0d5b94;">${esc(x.folio)}</a>
                                <span class="text-truncate d-inline-block align-bottom" style="max-width:155px;"> · ${esc(x.proveedor)}</span>
                                <div class="text-muted" style="font-size:10px;">$${money.format(x.total || 0)} ${esc(x.moneda)} · Falta XML</div>
                            </div>
                            <button type="button" class="btn btn-xs btn-default leb-tb-xml" data-po="${esc(x.folio)}">Subir XML</button>
                        </div>
                    `).join("") || `<div class="text-muted small py-2">Sin facturas XML pendientes de recibir.</div>`;
                }
                return (tb.en_flete || []).slice(0, 6).map((x) => {
                    const txtDias = x.dias_restantes === null || x.dias_restantes === undefined
                        ? "En flete"
                        : (x.vencido ? `Vencido (${Math.abs(x.dias_restantes)}d)` : `Llega en ${x.dias_restantes}d`);
                    const clsDias = x.vencido ? "badge-danger" : "badge-info";
                    return `
                        <div class="d-flex justify-content-between align-items-center py-1" style="border-bottom:1px solid #f1f5f9;font-size:11.5px;gap:6px;">
                            <div style="min-width:0;">
                                <a href="/app/purchase-order/${encodeURIComponent(x.folio)}" style="font-weight:700;color:#0d5b94;">${esc(x.folio)}</a>
                                <span class="text-truncate d-inline-block align-bottom" style="max-width:140px;"> · ${esc(x.proveedor)}</span>
                                <div class="text-muted" style="font-size:10px;">Promesa: ${esc(x.promesa || "Sin fecha")}</div>
                            </div>
                            <div class="d-flex align-items-center" style="gap:4px;">
                                <span class="badge ${clsDias}">${esc(txtDias)}</span>
                                <button type="button" class="btn btn-xs btn-default leb-tb-foto" data-po="${esc(x.folio)}">Foto obra</button>
                            </div>
                        </div>
                    `;
                }).join("") || `<div class="text-muted small py-2">Ningún pedido en flete activo.</div>`;
            };

            host.innerHTML = `
                <div style="padding-top:10px;border-top:1px solid #e2e8f0;">
                    <div class="d-flex justify-content-between align-items-center mb-2 flex-wrap" style="gap:8px;">
                        <div style="font-size:11.5px;font-weight:800;color:#031e34;letter-spacing:0.02em;">TABLEROS OPERATIVOS DIARIOS</div>
                        <div class="d-flex" style="gap:6px;">
                            ${(window.lebaref && window.lebaref.esUsuarioMotorOscar && window.lebaref.esUsuarioMotorOscar()) ? `<button type="button" class="btn btn-xs btn-primary leb-ws-motor-precios">Motor de precios y confianza</button>` : ""}
                            <button type="button" class="btn btn-xs btn-default leb-ws-xml-campo">${LEB_SVGS.upload} XML directo de campo</button>
                            <button type="button" class="btn btn-xs btn-default leb-ws-refresh">Actualizar tableros</button>
                        </div>
                    </div>
                    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:10px;">
                        <div style="background:#ffffff;border:1px solid #cbd5e1;border-radius:8px;padding:10px 12px;">
                            <div class="d-flex justify-content-between align-items-center mb-2">
                                <b style="font-size:12.5px;color:#031e34;">1. Solicitudes sin atender</b>
                                <span class="badge badge-primary">${r.sin_atender || 0}</span>
                            </div>
                            <div>${filasSol}</div>
                        </div>
                        <div style="background:#ffffff;border:1px solid #cbd5e1;border-radius:8px;padding:10px 12px;">
                            <div class="d-flex justify-content-between align-items-center mb-2">
                                <b style="font-size:12.5px;color:#031e34;">2. Pendientes de pago Dirección</b>
                                <span class="badge badge-warning">${r.pendientes_pago_direccion || 0}</span>
                            </div>
                            <div>${filasPago}</div>
                        </div>
                        <div style="background:#ffffff;border:1px solid #cbd5e1;border-radius:8px;padding:10px 12px;">
                            <div class="d-flex justify-content-between align-items-center mb-2 flex-wrap" style="gap:4px;">
                                <b style="font-size:12.5px;color:#031e34;">3. Liberadas y seguimiento</b>
                                <div class="btn-group btn-group-xs">
                                    <button type="button" class="btn btn-xs btn-primary leb-log-tab" data-modo="recoleccion">Recolectar (${r.ir_a_recolectar || 0})</button>
                                    <button type="button" class="btn btn-xs btn-default leb-log-tab" data-modo="flete">Flete (${r.en_flete || 0})</button>
                                    <button type="button" class="btn btn-xs btn-default leb-log-tab" data-modo="xml">XML (${r.xml_pendientes || 0})</button>
                                </div>
                            </div>
                            <div class="leb-log-box">${renderLogistica("recoleccion")}</div>
                        </div>
                    </div>
                </div>
            `;

            const $h = $(host);
            const wireDynamicBtns = () => {
                $h.find(".leb-tb-pago").off("click").on("click", function () {
                    const po = $(this).data("po");
                    const sup = $(this).data("sup");
                    if (window.lebaref && window.lebaref.abrirModalPagoLogisticaOc) {
                        window.lebaref.abrirModalPagoLogisticaOc(po, sup, () => pintarFranjaPendientesWorkspace(true));
                    }
                });
                $h.find(".leb-tb-foto").off("click").on("click", function () {
                    const po = $(this).data("po");
                    if (window.lebaref && window.lebaref.abrirModalEntregaObraFoto) {
                        window.lebaref.abrirModalEntregaObraFoto(po, () => pintarFranjaPendientesWorkspace(true));
                    }
                });
                $h.find(".leb-tb-xml").off("click").on("click", function () {
                    const po = $(this).data("po");
                    if (window.lebaref && window.lebaref.abrirAsistenteFacturaXml) {
                        window.lebaref.abrirAsistenteFacturaXml({ purchaseOrder: po });
                    }
                });
                $h.find(".leb-ws-motor-precios").off("click").on("click", () => {
                    if (window.lebaref && window.lebaref.abrirMotorPreciosConfianza) {
                        window.lebaref.abrirMotorPreciosConfianza("catalogo");
                    }
                });
            };
            wireDynamicBtns();

            $h.find(".leb-log-tab").on("click", function () {
                const modo = $(this).data("modo");
                $h.find(".leb-log-tab").removeClass("btn-primary").addClass("btn-default");
                $(this).removeClass("btn-default").addClass("btn-primary");
                $h.find(".leb-log-box").html(renderLogistica(modo));
                wireDynamicBtns();
            });
            $h.find(".leb-ws-refresh").on("click", () => pintarFranjaPendientesWorkspace(true));
            $h.find(".leb-ws-xml-campo").on("click", () => {
                if (window.lebaref && window.lebaref.abrirModalXmlCampoDirecto) {
                    window.lebaref.abrirModalXmlCampoDirecto(() => pintarFranjaPendientesWorkspace(true));
                }
            });
        } catch (e) {
            // Silencioso si el perfil no tiene acceso a algún contador
        }
    }

    async function abrirTableroPendientes(tabInicial = "compras") {
        const { message: t } = await frappe.call({
            method: "lebaref_compras.analitica_operativa.tablero_pendientes",
            type: "GET",
            freeze: true,
            freeze_message: "Consultando pendientes y checkpoints por área…",
        });
        const esc = frappe.utils.escape_html;
        const d = new frappe.ui.Dialog({
            title: "📋 Agenda Operativa y Checkpoints por Área (GES-01 / GES-02)",
            size: "extra-large",
            fields: [{ fieldname: "_html", fieldtype: "HTML" }],
        });

        const wrap = d.fields_dict._html.$wrapper;
        const c = t.conteos || {};
        const tabsHtml = `
            <div class="d-flex mb-3 flex-wrap" style="gap:6px;">
                <button type="button" class="btn btn-sm btn-primary leb-pend-tab" data-tab="compras">🛒 Compras (${(c.solicitudes_sin_cotizar || 0) + (c.comparativas_listas || 0) + (c.entradas_por_regularizar || 0)})</button>
                <button type="button" class="btn btn-sm btn-default leb-pend-tab" data-tab="direccion">✍️ Dirección y Pagos (${(c.oc_por_autorizar || 0) + (c.sobrantes_por_autorizar || 0) + (c.pagos_proximos || 0)})</button>
                <button type="button" class="btn btn-sm btn-default leb-pend-tab" data-tab="almacen">📦 Almacén, Herramientas y Flotilla (${(c.oc_por_recibir || 0) + (c.bajo_stock_minimo || 0) + (c.prestamos_vencidos || 0) + (c.alertas_flotilla || 0)})</button>
            </div>
            <div class="leb-pend-body"></div>
        `;
        wrap.html(tabsHtml);

        const renderLista = (tituloSec, lista, vacioTxt) => {
            const filas = (lista || [])
                .map(
                    (it) => `<tr>
                        <td style="width:170px;"><a href="#" class="leb-goto-doc" data-dt="${esc(it.doctype)}" data-dn="${esc(it.folio)}"><b>${esc(it.folio)}</b></a></td>
                        <td>${esc(it.titulo || "")}</td>
                        <td style="width:120px;" class="text-muted">${esc(it.fecha || "—")}</td>
                    </tr>`
                )
                .join("");
            return `
                <div class="mb-3">
                    <div style="font-weight:700;font-size:12.5px;color:#031e34;margin-bottom:4px;">${esc(tituloSec)} (${(lista || []).length})</div>
                    <table class="table table-bordered table-sm mb-0" style="font-size:12px;">
                        <tbody>${filas || `<tr><td class="text-muted">${esc(vacioTxt)}</td></tr>`}</tbody>
                    </table>
                </div>
            `;
        };

        const pintarTab = (tab) => {
            wrap.find(".leb-pend-tab").each(function () {
                const act = $(this).data("tab") === tab;
                $(this).toggleClass("btn-primary", act).toggleClass("btn-default", !act);
            });
            const body = wrap.find(".leb-pend-body");
            if (tab === "compras") {
                body.html(
                    renderLista("1. Solicitudes confirmadas pendientes de cotizar", t.compras.solicitudes_sin_cotizar, "Todas las solicitudes activas ya tienen cotización.") +
                    renderLista("2. Solicitudes con cotizaciones listas para Cuadro Comparativo / Adjudicar", t.compras.comparativas_listas, "Sin comparativas pendientes.") +
                    renderLista("3. Entradas anticipadas sin OC pendientes de regularizar (ALM-02)", t.compras.entradas_por_regularizar, "Sin entradas pendientes de regularizar.")
                );
            } else if (tab === "direccion") {
                body.html(
                    renderLista("1. Órdenes de Compra en borrador pendientes de Autorización (Vo.Bo.)", t.direccion.oc_por_autorizar, "No hay órdenes en borrador pendientes de firma.") +
                    renderLista("2. Sobrantes históricos con acta pendientes de autorizar (ALM-07)", t.direccion.sobrantes_por_autorizar, "Sin actas de sobrante pendientes.") +
                    renderLista("3. Pagos programados vencidos o próximos en 7 días (COM-07)", t.direccion.pagos_proximos, "Sin vencimientos próximos.")
                );
            } else {
                const btnMin = c.bajo_stock_minimo > 0
                    ? `<div class="alert alert-warning py-2 px-3 mb-3 d-flex justify-content-between align-items-center" style="font-size:12px;">
                        <span>⚠️ Hay <b>${c.bajo_stock_minimo} artículo(s)</b> por debajo del stock mínimo sin pedido abierto.</span>
                        <button type="button" class="btn btn-xs btn-primary leb-open-min">Abrir Reabastecimiento en 1 Clic</button>
                       </div>`
                    : "";
                body.html(
                    btnMin +
                    renderLista("1. Órdenes de Compra autorizadas pendientes de recibir en almacén", t.almacen_activos.oc_por_recibir, "Sin recepciones pendientes.") +
                    renderLista("2. Préstamos de herramientas con fecha compromiso vencida (HER-02)", t.almacen_activos.prestamos_vencidos, "Sin préstamos vencidos.") +
                    renderLista("3. Vehículos con servicio próximo o seguro por vencer (FLO-02)", t.almacen_activos.alertas_flotilla, "Flotilla al día.")
                );
                body.find(".leb-open-min").on("click", () => {
                    d.hide();
                    abrirAlertasStockMinimo();
                });
            }

            body.find(".leb-goto-doc").on("click", function (ev) {
                ev.preventDefault();
                d.hide();
                frappe.set_route("Form", $(this).data("dt"), $(this).data("dn"));
            });
        };

        wrap.find(".leb-pend-tab").on("click", function () {
            pintarTab($(this).data("tab"));
        });
        pintarTab(tabInicial);
        d.show();
    }

    async function abrirAlertasStockMinimo() {
        const { message: data } = await frappe.call({
            method: "lebaref_compras.analitica_operativa.alertas_stock_minimo",
            type: "GET",
            freeze: true,
            freeze_message: "Verificando existencias contra stock mínimo y pedidos en tránsito…",
        });
        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const pedir = (data && data.requieren_pedido) || [];
        const transito = (data && data.en_transito) || [];

        const seleccion = pedir.map((r) => ({
            incluir: true,
            item_code: r.item_code,
            clave: r.clave,
            nombre: r.nombre,
            ubicacion: r.ubicacion,
            stock_actual: r.stock_actual,
            stock_minimo: r.stock_minimo,
            uom_almacen: r.uom_almacen,
            uom_compra: r.uom_compra,
            qty: r.sugerido_compra,
            costo: r.ultimo_costo,
        }));

        const d = new frappe.ui.Dialog({
            title: `⚠️ Alertas de Stock Mínimo y Reabastecimiento sin Duplicados (${pedir.length} por pedir)`,
            size: "extra-large",
            fields: [
                {
                    fieldname: "titulo",
                    label: "Nombre de la Solicitud de Reabastecimiento",
                    fieldtype: "Data",
                    default: `Reabastecimiento Stock Mínimo · ${frappe.datetime.get_today()}`,
                },
                { fieldname: "_tabla", fieldtype: "HTML" },
            ],
            primary_action_label: "🛒 Generar Solicitud Consolidada",
            async primary_action(vals) {
                const elegidos = seleccion
                    .filter((x) => x.incluir && Number(x.qty || 0) > 0)
                    .map((x) => ({ item_code: x.item_code, qty: Number(x.qty), uom: x.uom_compra }));
                if (!elegidos.length) {
                    frappe.msgprint("Marca al menos un artículo con cantidad mayor que cero.");
                    return;
                }
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.analitica_operativa.generar_solicitud_reabastecimiento",
                    type: "POST",
                    args: {
                        datos: JSON.stringify({
                            titulo: vals.titulo,
                            items: elegidos,
                        }),
                    },
                    freeze: true,
                    freeze_message: "Generando Solicitud de Reabastecimiento trazable…",
                });
                d.hide();
                frappe.show_alert({ message: `Solicitud creada: ${res.folio}`, indicator: "green" });
                const rt = frappe.get_route ? frappe.get_route() : [];
                if (rt[0] === "List" && rt[1] === "Material Request" && window.cur_list && window.cur_list.doctype === "Material Request") {
                    window.cur_list.refresh();
                    seleccionarSolicitudEnSplit(window.cur_list, res.folio);
                } else {
                    frappe.set_route("List", "Material Request");
                }
            },
        });

        const host = d.fields_dict._tabla.$wrapper;
        if (!pedir.length && !transito.length) {
            host.html(`<div class="alert alert-success mb-0">✅ Todos los artículos con stock mínimo configurado tienen existencias suficientes.</div>`);
            d.get_primary_btn().hide();
        } else {
            let html = `<div class="small text-muted mb-2">Los artículos que ya tienen una Solicitud u Orden de Compra activa se separan abajo automáticamente para <b>evitar pedidos duplicados</b> (ALM-09).</div>`;
            if (seleccion.length) {
                html += `<div class="table-responsive" style="max-height:360px;overflow:auto;">
                    <table class="table table-bordered table-sm" style="font-size:12px;">
                        <thead style="background:#031e34;color:#fff;">
                            <tr>
                                <th style="width:34px;"><input type="checkbox" class="leb-min-all" checked></th>
                                <th>ID / Clave</th>
                                <th>Artículo</th>
                                <th>Anaquel</th>
                                <th class="text-right">Actual / Mín.</th>
                                <th style="width:130px;">Pedir (${esc("U. Compra")})</th>
                                <th class="text-right">Últ. Costo</th>
                            </tr>
                        </thead>
                        <tbody class="leb-min-tbody"></tbody>
                    </table>
                </div>`;
            } else {
                d.get_primary_btn().hide();
            }

            if (transito.length) {
                const trTrans = transito
                    .map(
                        (t) => `<tr>
                            <td><b>${esc(t.item_code)}</b> · ${esc(t.clave)}</td>
                            <td>${esc(t.nombre)}</td>
                            <td class="text-right">${t.stock_actual} / ${t.stock_minimo} ${esc(t.uom_almacen)}</td>
                            <td class="text-right"><b>+${t.en_transito}</b></td>
                            <td>${(t.folios_activos || []).map((f) => `<span class="badge badge-info mr-1">${esc(f)}</span>`).join("")}</td>
                        </tr>`
                    )
                    .join("");
                html += `<div class="mt-3">
                    <div style="font-weight:700;font-size:12px;color:#0d5b94;" class="mb-1">🚚 Ya solicitados / En tránsito (${transito.length} omitidos para no duplicar):</div>
                    <div class="table-responsive" style="max-height:170px;overflow:auto;">
                        <table class="table table-bordered table-sm mb-0" style="font-size:11.5px;">
                            <thead class="thead-light"><tr><th>SKU</th><th>Artículo</th><th class="text-right">Actual / Mín.</th><th class="text-right">En Tránsito</th><th>Folios Activos</th></tr></thead>
                            <tbody>${trTrans}</tbody>
                        </table>
                    </div>
                </div>`;
            }

            host.html(html);
            const tbody = host.find(".leb-min-tbody");
            for (const row of seleccion) {
                const tr = $("<tr>").appendTo(tbody);
                const tdChk = $("<td class='text-center'>").appendTo(tr);
                const chk = $('<input type="checkbox" checked>').on("change", function () {
                    row.incluir = $(this).prop("checked");
                }).appendTo(tdChk);
                row._chk = chk;
                $("<td>").html(`<b>${esc(row.item_code)}</b><br><small class="text-muted">${esc(row.clave)}</small>`).appendTo(tr);
                $("<td>").text(row.nombre).appendTo(tr);
                $("<td>").text(row.ubicacion).appendTo(tr);
                $("<td class='text-right'>").html(`<b style="color:#dc2626;">${row.stock_actual}</b> / ${row.stock_minimo} ${esc(row.uom_almacen)}`).appendTo(tr);
                const tdQty = $("<td>").appendTo(tr);
                const grp = $('<div class="d-flex align-items-center" style="gap:4px;">').appendTo(tdQty);
                $('<input type="number" step="0.01" min="0" class="form-control form-control-sm">')
                    .val(row.qty)
                    .on("input", function () {
                        row.qty = $(this).val();
                    })
                    .appendTo(grp);
                $("<small class='text-muted'>").text(row.uom_compra).appendTo(grp);
                $("<td class='text-right'>").text(`$${money.format(row.costo || 0)}`).appendTo(tr);
            }
            host.find(".leb-min-all").on("change", function () {
                const v = $(this).prop("checked");
                for (const row of seleccion) {
                    row.incluir = v;
                    if (row._chk) row._chk.prop("checked", v);
                }
            });
        }
        d.show();
    }

    async function abrirAnaliticaEjecutiva(tabInicial = "ppv") {
        const { message: an } = await frappe.call({
            method: "lebaref_compras.analitica_operativa.analitica_ejecutiva",
            type: "GET",
            freeze: true,
            freeze_message: "Calculando PPV, Ranking de Proveedores, Matriz Kraljic y Costos por Obra…",
        });
        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const r = an.resumen || {};

        const d = new frappe.ui.Dialog({
            title: "📈 Analítica Ejecutiva Lebaref · PPV, Proveedores, Familias y Obras (ANA-01)",
            size: "extra-large",
            fields: [{ fieldname: "_html", fieldtype: "HTML" }],
        });

        const wrap = d.fields_dict._html.$wrapper;
        wrap.html(`
            <div style="display:grid;grid-template-columns:repeat(4,minmax(140px,1fr));gap:8px;margin-bottom:12px;">
                <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:8px 12px;">
                    <div class="small text-muted">Resultado Neto PPV</div>
                    <div style="font-size:16px;font-weight:800;color:${(r.ahorro_neto_ppv || 0) >= 0 ? "#059669" : "#b45309"};">
                        ${(r.ahorro_neto_ppv || 0) >= 0 ? "+" : "-"}$${money.format(Math.abs(r.ahorro_neto_ppv || 0))} MXN
                    </div>
                </div>
                <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:8px 12px;">
                    <div class="small text-muted">Partidas Evaluadas PPV</div>
                    <div style="font-size:16px;font-weight:800;color:#031e34;">${r.partidas_evaluadas_ppv || 0}</div>
                </div>
                <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:8px 12px;">
                    <div class="small text-muted">Proveedores Activos / CFDI</div>
                    <div style="font-size:16px;font-weight:800;color:#0d5b94;">${r.proveedores_con_movimiento || 0}</div>
                </div>
                <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:8px 12px;">
                    <div class="small text-muted">Familias Clasificadas</div>
                    <div style="font-size:16px;font-weight:800;color:#334155;">${r.familias_analizadas || 0}</div>
                </div>
            </div>
            <div class="d-flex mb-2 flex-wrap" style="gap:6px;">
                <button type="button" class="btn btn-xs btn-primary leb-an-tab" data-tab="ppv">📊 Variación de Precios (PPV)</button>
                <button type="button" class="btn btn-xs btn-default leb-an-tab" data-tab="proveedores">🏆 Ranking de Proveedores</button>
                <button type="button" class="btn btn-xs btn-default leb-an-tab" data-tab="kraljic">🧭 Matriz Kraljic / Familias</button>
                <button type="button" class="btn btn-xs btn-default leb-an-tab" data-tab="obras">🏗️ Costo por Obra</button>
            </div>
            <div class="leb-an-body" style="max-height:430px;overflow:auto;"></div>
        `);

        const pintar = (tab) => {
            wrap.find(".leb-an-tab").each(function () {
                const act = $(this).data("tab") === tab;
                $(this).toggleClass("btn-primary", act).toggleClass("btn-default", !act);
            });
            const body = wrap.find(".leb-an-body");
            if (tab === "ppv") {
                const trs = (an.ppv || []).map((x) => `
                    <tr>
                        <td><b>${esc(x.item_code)}</b> · ${esc(x.nombre)}</td>
                        <td>${esc(x.proveedor)}<br><small class="text-muted">${esc(x.orden)} · ${esc(x.fecha)}</small></td>
                        <td class="text-right">$${money.format(x.costo_ref)}</td>
                        <td class="text-right"><b>$${money.format(x.precio_compra)}</b></td>
                        <td class="text-right" style="font-weight:700;color:${x.pct_variacion <= 0 ? "#059669" : "#b45309"};">${x.pct_variacion > 0 ? "+" : ""}${x.pct_variacion}%</td>
                        <td class="text-right" style="font-weight:700;color:${x.impacto_total <= 0 ? "#059669" : "#b45309"};">${x.impacto_total > 0 ? "+$" : "-$"}${money.format(Math.abs(x.impacto_total))}</td>
                    </tr>
                `).join("");
                body.html(`<table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;"><tr><th>Artículo</th><th>Proveedor / OC</th><th class="text-right">Costo Ref.</th><th class="text-right">Compra Real</th><th class="text-right">% Var.</th><th class="text-right">Impacto Total</th></tr></thead>
                    <tbody>${trs || "<tr><td colspan='6' class='text-muted'>Sin órdenes confirmadas con variación registrada.</td></tr>"}</tbody>
                </table>`);
            } else if (tab === "proveedores") {
                const trs = (an.ranking_proveedores || []).map((p, idx) => `
                    <tr>
                        <td><b>#${idx + 1}</b></td>
                        <td><b>${esc(p.proveedor)}</b></td>
                        <td><span class="badge badge-light">${esc(p.especialidad)}</span></td>
                        <td class="text-right">${p.num_ocs} OC(s) · $${money.format(p.monto_ocs)}</td>
                        <td class="text-right">${p.num_cfdis} XML · $${money.format(p.monto_cfdi)}</td>
                        <td class="text-right"><b>$${money.format(p.volumen_total)} MXN</b></td>
                    </tr>
                `).join("");
                body.html(`<table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;"><tr><th>#</th><th>Proveedor</th><th>Especialidad</th><th class="text-right">Órdenes Activas</th><th class="text-right">CFDIs Históricos</th><th class="text-right">Volumen Acumulado</th></tr></thead>
                    <tbody>${trs || "<tr><td colspan='6' class='text-muted'>Sin datos.</td></tr>"}</tbody>
                </table>`);
            } else if (tab === "kraljic") {
                const trs = (an.kraljic || []).map((k) => `
                    <tr>
                        <td><b>${esc(k.familia)}</b></td>
                        <td class="text-right">${k.skus}</td>
                        <td class="text-right">${k.proveedores}</td>
                        <td class="text-right"><b>$${money.format(k.valor_stock)} MXN</b></td>
                        <td><span class="badge ${k.cuadrante.startsWith("Estratégico") ? "badge-warning" : (k.cuadrante.startsWith("Apalancamiento") ? "badge-success" : "badge-info")}">${esc(k.cuadrante)}</span></td>
                    </tr>
                `).join("");
                body.html(`<table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;"><tr><th>Familia / Categoría</th><th class="text-right">SKUs</th><th class="text-right">Proveedores Mapeados</th><th class="text-right">Valor en Inventario</th><th>Cuadrante Kraljic Sugerido</th></tr></thead>
                    <tbody>${trs}</tbody>
                </table>`);
            } else {
                const trs = (an.obras || []).map((o) => `
                    <tr>
                        <td><b>${esc(o.obra)}</b> · ${esc(o.nombre)}</td>
                        <td>${esc(o.cliente)}</td>
                        <td class="text-right">$${money.format(o.compras_oc)}</td>
                        <td class="text-right">$${money.format(o.consumo_almacen)}</td>
                        <td class="text-right"><b>$${money.format((o.compras_oc || 0) + (o.consumo_almacen || 0))} MXN</b></td>
                    </tr>
                `).join("");
                body.html(`<table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;"><tr><th>Obra / Proyecto</th><th>Cliente</th><th class="text-right">Compras Adjudicadas (OC)</th><th class="text-right">Consumo Real Almacén</th><th class="text-right">Total Obra</th></tr></thead>
                    <tbody>${trs}</tbody>
                </table>`);
            }
        };

        wrap.find(".leb-an-tab").on("click", function () {
            pintar($(this).data("tab"));
        });
        pintar(tabInicial);
        d.show();
    }

    function abrirCargaRapidaSolicitudCsv(alTerminar = null) {
        let ultimoTexto = "";
        const d = new frappe.ui.Dialog({
            title: "📥 Carga Rápida de Solicitud desde CSV o Excel (SOL-04)",
            size: "extra-large",
            fields: [
                {
                    fieldname: "_ayuda",
                    fieldtype: "HTML",
                    options: `<div class="d-flex justify-content-between align-items-center p-2 mb-2" style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;font-size:12px;">
                        <span>Pega filas directamente desde <b>Excel</b> o sube un archivo <b>.CSV</b> con columnas: <code>Código_o_Concepto, Cantidad, Unidad, Obra, Partida</code>. Si el código existe se vincula al SKU; si no, entra como <b>Concepto Libre</b>.</span>
                        <div class="d-flex" style="gap:6px;">
                            <button type="button" class="btn btn-xs btn-default leb-csv-tpl">📄 Descargar Plantilla CSV</button>
                            <button type="button" class="btn btn-xs btn-default leb-csv-file-btn">📁 Cargar archivo .CSV</button>
                            <input type="file" accept=".csv,text/csv,text/plain" class="d-none leb-csv-file">
                        </div>
                    </div>`,
                },
                {
                    fieldname: "titulo",
                    label: "Nombre descriptivo de la solicitud",
                    fieldtype: "Data",
                    default: `Pedido Masivo · ${frappe.datetime.get_today()}`,
                    reqd: 1,
                },
                { fieldtype: "Column Break" },
                {
                    fieldname: "solicitante",
                    label: "Solicitante (opcional)",
                    fieldtype: "Data",
                },
                { fieldtype: "Section Break" },
                {
                    fieldname: "texto_csv",
                    label: "Pega aquí tus filas desde Excel o CSV",
                    fieldtype: "Small Text",
                    reqd: 1,
                },
                {
                    fieldname: "_preview",
                    fieldtype: "HTML",
                },
            ],
            primary_action_label: "Crear Solicitud Confirmada",
            async primary_action(vals) {
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.analitica_operativa.ejecutar_carga_masiva_solicitud",
                    type: "POST",
                    args: {
                        datos: JSON.stringify({
                            titulo: vals.titulo,
                            solicitante: vals.solicitante || "",
                            texto_csv: vals.texto_csv,
                        }),
                    },
                    freeze: true,
                    freeze_message: "Importando partidas y creando solicitud…",
                });
                d.hide();
                frappe.show_alert({ message: `Solicitud creada por carga rápida: ${res.folio}`, indicator: "green" });
                if (typeof alTerminar === "function") {
                    alTerminar(res);
                } else {
                    frappe.set_route("Form", "Material Request", res.folio);
                }
            },
        });

        d.add_custom_action("🔍 Validar / Previsualizar Partidas", async () => {
            const txt = d.get_value("texto_csv") || "";
            if (!txt.trim()) {
                frappe.msgprint("Pega al menos una fila antes de validar.");
                return;
            }
            const { message: prev } = await frappe.call({
                method: "lebaref_compras.analitica_operativa.previsualizar_carga_masiva_solicitud",
                type: "POST",
                args: { texto_csv: txt },
                freeze: true,
            });
            ultimoTexto = txt;
            void ultimoTexto;
            const esc = frappe.utils.escape_html;
            const errHtml = (prev.errores || []).length
                ? `<div class="alert alert-danger py-2 px-3 mb-2" style="font-size:12px;"><b>Corrige estos errores antes de guardar:</b><br>• ${prev.errores.map(esc).join("<br>• ")}</div>`
                : `<div class="alert alert-success py-2 px-3 mb-2" style="font-size:12px;">✅ Validación correcta: <b>${prev.total_skus} SKU(s) del catálogo</b> y <b>${prev.total_libres} concepto(s) libre(s)</b> listos para importar.</div>`;
            const bloquesPrevCsv = agruparFilasPorPartida(prev.filas_validas || [], (f) => f.partida);
            const trs = (prev.filas_validas || []).length
                ? bloquesPrevCsv.map((g) => {
                    const sep = g.partida
                        ? `<tr style="background:#e2e8f0;color:#031e34;font-weight:800;"><td colspan="5" style="padding:5px 8px;">${esc(g.partida)} (${g.items.length})</td></tr>`
                        : "";
                    const rows = g.items.map((f) => `<tr>
                        <td>#${f.fila}</td>
                        <td><span class="badge ${f.es_sku ? "badge-success" : "badge-warning"}">${esc(f.coincidencia)}</span></td>
                        <td><b>${esc(f.item_code || "CONCEPTO LIBRE")}</b> · ${esc(f.descripcion)}</td>
                        <td class="text-right"><b>${f.qty} ${esc(f.uom)}</b></td>
                        <td>${esc(f.obra || "—")}</td>
                    </tr>`).join("");
                    return sep + rows;
                }).join("")
                : "";
            d.fields_dict._preview.$wrapper.html(`
                ${errHtml}
                <div class="table-responsive" style="max-height:220px;overflow:auto;">
                    <table class="table table-bordered table-sm mb-0" style="font-size:12px;">
                        <thead class="thead-light"><tr><th>Fila</th><th>Tipo</th><th>Artículo / Concepto</th><th class="text-right">Cantidad</th><th>Obra</th></tr></thead>
                        <tbody>${trs || "<tr><td colspan='5' class='text-muted'>Sin filas válidas.</td></tr>"}</tbody>
                    </table>
                </div>
            `);
        });

        const ayudaWrap = d.fields_dict._ayuda.$wrapper;
        ayudaWrap.find(".leb-csv-tpl").on("click", () => {
            const csvEjemplo = "Codigo_o_Concepto,Cantidad,Unidad,Obra,Partida\nPRD-0001,5,Nos,,Ferreteria\nTubo cobre rigido especial 3/4,2,Nos,,HVAC\n";
            const blob = new Blob([csvEjemplo], { type: "text/csv;charset=utf-8;" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = "Plantilla_Solicitud_Lebaref.csv";
            a.click();
            URL.revokeObjectURL(url);
        });
        ayudaWrap.find(".leb-csv-file-btn").on("click", () => ayudaWrap.find(".leb-csv-file").trigger("click"));
        ayudaWrap.find(".leb-csv-file").on("change", function () {
            const f = this.files && this.files[0];
            if (!f) return;
            const reader = new FileReader();
            reader.onload = (ev) => {
                d.set_value("texto_csv", String(ev.target.result || ""));
            };
            reader.readAsText(f);
        });

        d.show();
    }

    function imprimirExpedienteObraCarta(exp) {
        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const t = exp.totales || {};
        const trsOc = (exp.ordenes || []).map((o) => `<tr><td><b>${esc(o.folio)}</b></td><td>${esc(o.proveedor)}</td><td>${esc(o.fecha)}</td><td>${esc(o.estado)}</td><td style="text-align:right;">$${money.format(o.importe)}</td></tr>`).join("");
        const trsAlm = (exp.movimientos_almacen || []).map((m) => `<tr><td><b>${esc(m.folio)}</b></td><td>${esc(m.fecha)}</td><td>${esc(m.tipo)}</td><td style="text-align:right;">${m.partidas}</td><td style="text-align:right;">$${money.format(m.costo)}</td></tr>`).join("");
        const trsHer = (exp.herramientas || []).map((h) => `<tr><td><b>${esc(h.codigo)}</b></td><td>${esc(h.articulo)}</td><td>${esc(h.estado)}</td><td>${esc(h.responsable || "—")}</td></tr>`).join("");

        const w = window.open("", "_blank");
        if (!w) return;
        w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Expediente Obra ${esc(exp.obra)}</title>
            <style>
                @page { size: letter portrait; margin: 12mm; }
                body { font-family: 'Segoe UI', Arial, sans-serif; color:#0f172a; font-size:11px; }
                .hdr { display:flex; justify-content:space-between; align-items:center; border-bottom:2.5px solid #031e34; padding-bottom:8px; margin-bottom:10px; }
                .kpis { display:grid; grid-template-columns:repeat(4,1fr); gap:8px; margin-bottom:12px; }
                .kpi { border:1px solid #cbd5e1; border-radius:6px; padding:7px 10px; background:#f8fafc; }
                h3 { margin:12px 0 4px 0; font-size:12px; color:#031e34; text-transform:uppercase; }
                table { width:100%; border-collapse:collapse; margin-bottom:8px; }
                th { background:#031e34; color:#fff; padding:5px 7px; text-align:left; border:1px solid #031e34; }
                td { padding:5px 7px; border:1px solid #cbd5e1; }
            </style></head><body>
            <div class="hdr">
                <img src="/files/lebaref_logo_horizontal.svg" style="height:50px;">
                <div style="text-align:right;">
                    <b style="font-size:14px;color:#031e34;">EXPEDIENTE OPERATIVO DE OBRA · ${esc(exp.obra)}</b><br>
                    <span>${esc(exp.nombre_obra)} · Cliente: <b>${esc(exp.cliente)}</b></span>
                </div>
            </div>
            <div class="kpis">
                <div class="kpi"><small>Órdenes de Compra (Neto)</small><br><b style="font-size:14px;">$${money.format(t.ordenes_neto || 0)}</b></div>
                <div class="kpi"><small>Entregado a Resguardo</small><br><b style="font-size:14px;">$${money.format(t.resguardo_entregado || 0)}</b></div>
                <div class="kpi"><small>Consumo Real + Flotilla</small><br><b style="font-size:14px;color:#059669;">$${money.format(t.costo_total_acumulado || 0)}</b></div>
                <div class="kpi"><small>Herramientas en Obra</small><br><b style="font-size:14px;">${t.herramientas_activas || 0} HER</b></div>
            </div>
            <h3>Órdenes de Compra Adjudicadas</h3>
            <table><thead><tr><th>Folio OC</th><th>Proveedor</th><th>Fecha</th><th>Estado</th><th style="text-align:right;">Importe Neto</th></tr></thead><tbody>${trsOc || "<tr><td colspan='5'>Sin órdenes registradas.</td></tr>"}</tbody></table>
            <h3>Entregas, Consumos y Retornos de Almacén</h3>
            <table><thead><tr><th>Folio</th><th>Fecha</th><th>Movimiento</th><th style="text-align:right;">Partidas</th><th style="text-align:right;">Valor</th></tr></thead><tbody>${trsAlm || "<tr><td colspan='5'>Sin movimientos de almacén.</td></tr>"}</tbody></table>
            <h3>Herramientas Bajo Custodia en la Obra</h3>
            <table><thead><tr><th>Código HER</th><th>Artículo</th><th>Estado</th><th>Responsable</th></tr></thead><tbody>${trsHer || "<tr><td colspan='4'>Sin herramientas asignadas actualmente.</td></tr>"}</tbody></table>
            <script>window.onload = () => window.print();<\/script>
            </body></html>`);
        w.document.close();
    }

    async function renderizarExpedienteObraEnProject(frm) {
        if (frm.is_new()) return;
        try {
            const { message: exp } = await frappe.call({
                method: "lebaref_compras.analitica_operativa.expediente_obra",
                type: "GET",
                args: { obra: frm.doc.name },
            });
            if (!exp) return;
            frm.__lebExpObra = exp;
            const esc = frappe.utils.escape_html;
            const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            const t = exp.totales || {};

            const html = `
                <div class="leb-exp-obra-card" style="border:1px solid #cbd5e1;border-radius:10px;padding:12px 14px;background:#f8fafc;margin-bottom:10px;">
                    <div class="d-flex justify-content-between align-items-center mb-2 flex-wrap" style="gap:8px;">
                        <b style="color:#031e34;font-size:13px;">📊 Expediente Financiero y Operativo de la Obra (CAT-05)</b>
                        <button type="button" class="btn btn-xs btn-default leb-print-exp-obra">🖨️ Imprimir Expediente Carta</button>
                    </div>
                    <div style="display:grid;grid-template-columns:repeat(4,minmax(130px,1fr));gap:8px;">
                        <div style="background:#fff;border:1px solid #e2e8f0;border-radius:7px;padding:7px 10px;">
                            <div class="small text-muted">Compras OC (Neto)</div>
                            <div style="font-size:15px;font-weight:800;color:#031e34;">$${money.format(t.ordenes_neto || 0)}</div>
                            <small class="text-muted">${t.ordenes_count || 0} OC(s) · ${t.solicitudes_count || 0} Solicitud(es)</small>
                        </div>
                        <div style="background:#fff;border:1px solid #e2e8f0;border-radius:7px;padding:7px 10px;">
                            <div class="small text-muted">Entregado a Resguardo</div>
                            <div style="font-size:15px;font-weight:800;color:#0d5b94;">$${money.format(t.resguardo_entregado || 0)}</div>
                        </div>
                        <div style="background:#fff;border:1px solid #e2e8f0;border-radius:7px;padding:7px 10px;">
                            <div class="small text-muted">Consumo Real + Flotilla</div>
                            <div style="font-size:15px;font-weight:800;color:#059669;">$${money.format(t.costo_total_acumulado || 0)}</div>
                        </div>
                        <div style="background:#fff;border:1px solid #e2e8f0;border-radius:7px;padding:7px 10px;">
                            <div class="small text-muted">Herramientas en Obra</div>
                            <div style="font-size:15px;font-weight:800;color:#b45309;">${t.herramientas_activas || 0} equipo(s)</div>
                        </div>
                    </div>
                </div>
            `;
            frm.dashboard.wrapper.find(".leb-exp-obra-card").remove();
            const $el = $(html).prependTo(frm.dashboard.wrapper);
            $el.find(".leb-print-exp-obra").on("click", () => imprimirExpedienteObraCarta(exp));
            void esc;
        } catch (e) {
            // Mantener vista estándar si falla lectura parcial
        }
    }

    function abrirDialogoCierreReaperturaOc(frm, accion) {
        const esCerrar = accion === "cerrar";
        const d = new frappe.ui.Dialog({
            title: esCerrar
                ? `🔒 Finiquitar / Cerrar Remanente de OC · ${frm.doc.name}`
                : `🔓 Reabrir Orden de Compra Cerrada · ${frm.doc.name}`,
            fields: [
                {
                    fieldname: "_nota",
                    fieldtype: "HTML",
                    options: `<div class="alert ${esCerrar ? "alert-warning" : "alert-info"} py-2 px-3 mb-2" style="font-size:12px;">
                        ${esCerrar
                            ? "Esta acción da por concluido el remanente pendiente de recibir <b>sin inventar entradas falsas de almacén</b> y deja constancia auditada en la bitácora de la orden (COM-08)."
                            : "La orden volverá a su estado activo conforme a sus recepciones y facturas reales (COM-08)."}
                    </div>`,
                },
                {
                    fieldname: "motivo",
                    label: esCerrar ? "Motivo del cierre parcial / finiquito" : "Motivo de la reapertura",
                    fieldtype: "Small Text",
                    reqd: 1,
                },
            ],
            primary_action_label: esCerrar ? "Confirmar Finiquito de Remanente" : "Confirmar Reapertura",
            async primary_action(vals) {
                await frappe.call({
                    method: "lebaref_compras.analitica_operativa.gestionar_cierre_orden",
                    type: "POST",
                    args: {
                        orden: frm.doc.name,
                        accion,
                        motivo: vals.motivo,
                    },
                    freeze: true,
                });
                d.hide();
                frappe.show_alert({
                    message: esCerrar ? "Remanente de OC cerrado con motivo auditable" : "Orden de Compra reabierta",
                    indicator: "green",
                });
                frm.reload_doc();
            },
        });
        d.show();
    }

    async function renderizarIndicadoresVehiculoEnFlotilla(frm) {
        if (frm.is_new()) return;
        try {
            const { message: k } = await frappe.call({
                method: "lebaref_compras.analitica_operativa.indicadores_vehiculo",
                type: "GET",
                args: { vehiculo: frm.doc.name },
            });
            if (!k) return;
            const esc = frappe.utils.escape_html;
            const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            const kmlTxt = k.datos_suficientes_rendimiento ? `${k.km_por_litro} km/L` : "Datos insuficientes";
            const cpkTxt = k.costo_por_km !== null ? `$${money.format(k.costo_por_km)} / km` : "Sin recorrido";
            const catsTxt = (k.gasto_por_categoria || [])
                .map((c) => `<span class="badge badge-light mr-1" style="border:1px solid #cbd5e1;">${esc(c.categoria)}: <b>$${money.format(c.monto)}</b></span>`)
                .join("") || `<span class="text-muted small">Sin gastos registrados aún</span>`;

            const html = `
                <div class="leb-flotilla-kpi-card" style="border:1px solid #cbd5e1;border-radius:10px;padding:10px 14px;background:#f8fafc;margin-bottom:10px;">
                    <div style="display:grid;grid-template-columns:repeat(4,minmax(130px,1fr));gap:8px;margin-bottom:8px;">
                        <div style="background:#fff;border:1px solid #e2e8f0;border-radius:7px;padding:7px 10px;">
                            <div class="small text-muted">Rendimiento Real (FLO-02)</div>
                            <div style="font-size:15px;font-weight:800;color:${k.datos_suficientes_rendimiento ? "#059669" : "#64748b"};">${esc(kmlTxt)}</div>
                            <small class="text-muted" style="font-size:10.5px;">${esc(k.nota_rendimiento || "")}</small>
                        </div>
                        <div style="background:#fff;border:1px solid #e2e8f0;border-radius:7px;padding:7px 10px;">
                            <div class="small text-muted">Costo por Kilómetro</div>
                            <div style="font-size:15px;font-weight:800;color:#031e34;">${esc(cpkTxt)}</div>
                            <small class="text-muted" style="font-size:10.5px;">Total: $${money.format(k.total_gasto || 0)} (${k.recorrido_total} km)</small>
                        </div>
                        <div style="background:#fff;border:1px solid #e2e8f0;border-radius:7px;padding:7px 10px;">
                            <div class="small text-muted">Próximo Servicio</div>
                            <div style="font-size:15px;font-weight:800;color:${k.km_restantes_servicio <= 1000 ? "#b45309" : "#0d5b94"};">${k.km_restantes_servicio} km rest.</div>
                            <small class="text-muted" style="font-size:10.5px;">Meta: ${k.proximo_servicio} km ${k.fecha_estimada_servicio ? "· Est: " + esc(k.fecha_estimada_servicio) : ""}</small>
                        </div>
                        <div style="background:#fff;border:1px solid #e2e8f0;border-radius:7px;padding:7px 10px;">
                            <div class="small text-muted">Vigencia de Seguro</div>
                            <div style="font-size:15px;font-weight:800;color:${k.dias_seguro !== null && k.dias_seguro <= 30 ? "#dc2626" : "#059669"};">${esc(k.seguro_vence || "—")}</div>
                            <small class="text-muted" style="font-size:10.5px;">${k.dias_seguro !== null ? `${k.dias_seguro} días restantes` : ""}</small>
                        </div>
                    </div>
                    <div style="font-size:11.5px;"><b>Desglose por categoría:</b> ${catsTxt}</div>
                </div>
            `;
            frm.dashboard.wrapper.find(".leb-flotilla-kpi-card").remove();
            $(html).prependTo(frm.dashboard.wrapper);
        } catch (e) {
            // Ignorar si no aplica
        }
    }

    // --- Fase 5: Motor Experimental de Precio Estimado, Confianza, Modo Sombra y Gráfica Tiempo-Precio (Exclusivo Oscar) ---
    function esUsuarioMotorOscar() {
        const u = (window.frappe && frappe.session && frappe.session.user) || "";
        return u === "oscar.gomez@lebaref.com" || u === "Administrator";
    }

    function construirSvgGraficaTiempoPrecio(puntos, resumen) {
        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        if (!puntos || !puntos.length) {
            return `<div class="text-muted text-center py-5" style="background:#f8fafc;border:1px dashed #cbd5e1;border-radius:8px;font-size:12.5px;">
                Este artículo aún no tiene cotizaciones ni órdenes de compra registradas para trazar en el tiempo.
            </div>`;
        }

        const W = 820;
        const H = 290;
        const padL = 78;
        const padR = 26;
        const padT = 24;
        const padB = 46;
        const plotW = W - padL - padR;
        const plotH = H - padT - padB;

        const xs = puntos.map((p) => Number(p.timestamp_ms || 0));
        const ys = puntos.map((p) => Number(p.precio_mxn || 0));
        const estVal = Number(resumen && resumen.precio_estimado_mxn ? resumen.precio_estimado_mxn : 0);
        if (estVal > 0) ys.push(estVal);

        let minX = Math.min(...xs);
        let maxX = Math.max(...xs);
        if (maxX <= minX) {
            minX -= 86400000 * 7;
            maxX += 86400000 * 7;
        }

        let minY = Math.min(...ys);
        let maxY = Math.max(...ys);
        if (maxY <= minY) {
            minY = Math.max(0, minY * 0.85);
            maxY = maxY * 1.15 || 100;
        } else {
            const margenY = (maxY - minY) * 0.14;
            minY = Math.max(0, minY - margenY);
            maxY = maxY + margenY;
        }

        const mapX = (x) => padL + ((x - minX) / (maxX - minX)) * plotW;
        const mapY = (y) => padT + plotH - ((y - minY) / (maxY - minY)) * plotH;

        const gridY = [];
        for (let i = 0; i <= 4; i++) {
            const valY = minY + ((maxY - minY) * i) / 4;
            const py = mapY(valY);
            gridY.push(`
                <line x1="${padL}" y1="${py.toFixed(1)}" x2="${W - padR}" y2="${py.toFixed(1)}" stroke="#e2e8f0" stroke-width="1"/>
                <text x="${padL - 8}" y="${(py + 4).toFixed(1)}" text-anchor="end" font-size="10.5" fill="#64748b">$${money.format(valY)}</text>
            `);
        }

        const fmtFechaCorta = (ms) => {
            const dt = new Date(ms);
            if (Number.isNaN(dt.getTime())) return "";
            const dd = String(dt.getUTCDate()).padStart(2, "0");
            const mm = String(dt.getUTCMonth() + 1).padStart(2, "0");
            const yy = String(dt.getUTCFullYear()).slice(-2);
            return `${dd}/${mm}/${yy}`;
        };

        const ticksXCount = Math.min(6, Math.max(2, puntos.length));
        const gridX = [];
        for (let i = 0; i < ticksXCount; i++) {
            const frac = ticksXCount === 1 ? 0.5 : i / (ticksXCount - 1);
            const valX = minX + (maxX - minX) * frac;
            const px = mapX(valX);
            gridX.push(`
                <line x1="${px.toFixed(1)}" y1="${padT}" x2="${px.toFixed(1)}" y2="${H - padB}" stroke="#f1f5f9" stroke-width="1"/>
                <text x="${px.toFixed(1)}" y="${H - padB + 18}" text-anchor="middle" font-size="10.5" fill="#64748b">${esc(fmtFechaCorta(valX))}</text>
            `);
        }

        // Banda y línea horizontal del Precio Estimado
        let bandaEstimado = "";
        if (estVal > 0 && !(resumen && resumen.recotizar_siempre)) {
            const yEst = mapY(estVal);
            const cvFrac = Math.min(0.35, Math.max(0.03, Number((resumen && resumen.volatilidad_cv_pct) || 5) / 100));
            const ySup = mapY(Math.min(maxY, estVal * (1 + cvFrac)));
            const yInf = mapY(Math.max(minY, estVal * (1 - cvFrac)));
            bandaEstimado = `
                <rect x="${padL}" y="${ySup.toFixed(1)}" width="${plotW}" height="${Math.max(2, yInf - ySup).toFixed(1)}" fill="#10b981" fill-opacity="0.08"/>
                <line x1="${padL}" y1="${yEst.toFixed(1)}" x2="${W - padR}" y2="${yEst.toFixed(1)}" stroke="#059669" stroke-width="1.6" stroke-dasharray="5,4"/>
                <rect x="${W - padR - 148}" y="${Math.max(padT + 2, yEst - 17).toFixed(1)}" width="146" height="15" rx="3" fill="#ecfdf5" stroke="#6ee7b7" stroke-width="0.8"/>
                <text x="${W - padR - 75}" y="${Math.max(padT + 13, yEst - 6).toFixed(1)}" text-anchor="middle" font-size="9.8" font-weight="700" fill="#047857">Est. $${money.format(estVal)} MXN</text>
            `;
        }

        const ptsCot = puntos.filter((p) => p.serie === "cotizacion");
        const ptsComp = puntos.filter((p) => p.serie === "compra_real");

        const polyCot = ptsCot.length >= 2
            ? `<polyline fill="none" stroke="#d97706" stroke-width="2" stroke-dasharray="5,4" points="${ptsCot.map((p) => `${mapX(p.timestamp_ms).toFixed(1)},${mapY(p.precio_mxn).toFixed(1)}`).join(" ")}"/>`
            : "";
        const polyComp = ptsComp.length >= 2
            ? `<polyline fill="none" stroke="#0d5b94" stroke-width="2.4" points="${ptsComp.map((p) => `${mapX(p.timestamp_ms).toFixed(1)},${mapY(p.precio_mxn).toFixed(1)}`).join(" ")}"/>`
            : "";

        const nodosSvg = puntos.map((p, idx) => {
            const cx = mapX(p.timestamp_ms).toFixed(1);
            const cy = mapY(p.precio_mxn).toFixed(1);
            const esCompra = p.serie === "compra_real";
            const color = esCompra ? "#0d5b94" : "#d97706";
            const fill = esCompra ? "#0d5b94" : "#ffffff";
            return `<circle class="leb-chart-pt" data-idx="${idx}" cx="${cx}" cy="${cy}" r="5.5" fill="${fill}" stroke="${color}" stroke-width="2.2" style="cursor:pointer;"/>`;
        }).join("");

        return `
            <div class="leb-svg-chart-host" style="position:relative;background:#ffffff;border:1px solid #cbd5e1;border-radius:8px;padding:8px 10px;">
                <div class="d-flex justify-content-between align-items-center mb-1 flex-wrap" style="font-size:11.5px;gap:8px;padding:0 4px;">
                    <b style="color:#031e34;">Dispersión y Evolución en el Tiempo (Precio unitario limpio s/IVA en MXN)</b>
                    <div class="d-flex align-items-center flex-wrap" style="gap:12px;">
                        <span class="d-inline-flex align-items-center" style="gap:5px;"><span style="width:10px;height:10px;border-radius:50%;background:#0d5b94;display:inline-block;"></span> <b>Compra Real</b> (${ptsComp.length})</span>
                        <span class="d-inline-flex align-items-center" style="gap:5px;"><span style="width:10px;height:10px;border-radius:50%;background:#fff;border:2px solid #d97706;display:inline-block;"></span> <b>Cotización</b> (${ptsCot.length})</span>
                        ${estVal > 0 && !(resumen && resumen.recotizar_siempre) ? `<span class="d-inline-flex align-items-center" style="gap:5px;color:#047857;"><b>--- Estimado actual</b></span>` : ""}
                    </div>
                </div>
                <svg viewBox="0 0 ${W} ${H}" style="width:100%;height:auto;display:block;overflow:visible;">
                    ${gridY.join("")}
                    ${gridX.join("")}
                    <line x1="${padL}" y1="${padT}" x2="${padL}" y2="${H - padB}" stroke="#94a3b8" stroke-width="1.2"/>
                    <line x1="${padL}" y1="${H - padB}" x2="${W - padR}" y2="${H - padB}" stroke="#94a3b8" stroke-width="1.2"/>
                    ${bandaEstimado}
                    ${polyCot}
                    ${polyComp}
                    ${nodosSvg}
                    <text x="${padL + plotW / 2}" y="${H - 8}" text-anchor="middle" font-size="11" font-weight="600" fill="#475569">Eje X: Tiempo (Fechas de cotización y compra)</text>
                </svg>
                <div class="leb-chart-tooltip d-none" style="position:absolute;z-index:20;pointer-events:none;background:#0f172a;color:#fff;padding:7px 10px;border-radius:6px;font-size:11px;line-height:1.35;box-shadow:0 4px 12px rgba(15,23,42,0.25);max-width:260px;"></div>
            </div>
        `;
    }

    async function abrirGraficaTiempoPrecioArticulo(itemCode, onAfterChange = null) {
        if (!esUsuarioMotorOscar()) return;
        const { message: data } = await frappe.call({
            method: "lebaref_compras.motor_precios.detalle_serie_articulo",
            type: "GET",
            args: { item_code: itemCode },
            freeze: true,
            freeze_message: "Calculando serie tiempo-precio e índice de confianza…",
        });
        if (!data || !data.resumen) return;
        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const r = data.resumen;
        const puntos = data.puntos || [];
        const sombra = data.sombra_item || [];
        const desg = r.desglose_confianza || {};

        const dlg = new frappe.ui.Dialog({
            title: `Serie Tiempo–Precio y Confianza · ${r.codigo_barras} (${r.item_name})`,
            size: "extra-large",
            fields: [{ fieldname: "_body", fieldtype: "HTML" }],
        });

        const filasTabla = puntos.slice().reverse().map((p) => {
            const badgeSerie = p.serie === "compra_real"
                ? `<span class="badge badge-primary" style="background:#0d5b94;">Compra Real</span>`
                : `<span class="badge badge-warning" style="background:#fef3c7;color:#92400e;border:1px solid #fcd34d;">Cotización</span>`;
            const detMoneda = p.moneda === "USD"
                ? `$${money.format(p.precio_origen)} USD · TC DOF ${Number(p.tc_dof || 1).toFixed(4)}`
                : `$${money.format(p.precio_origen)} MXN`;
            return `
                <tr>
                    <td style="white-space:nowrap;">${esc(p.fecha)}</td>
                    <td>${badgeSerie} <span class="text-muted small">${esc(p.origen)}</span></td>
                    <td><a href="#" class="leb-pt-doc" data-dt="${esc(p.doctype)}" data-dn="${esc(p.folio)}"><b>${esc(p.folio)}</b></a></td>
                    <td>${esc(p.proveedor)}</td>
                    <td class="text-right small">${detMoneda}</td>
                    <td class="text-right"><b>$${money.format(p.precio_mxn)}</b> <small class="text-muted">/ ${esc(r.uom_base)}</small></td>
                </tr>
            `;
        }).join("");

        const htmlSombra = sombra.length ? `
            <div class="mt-3 p-2" style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:8px;font-size:11.5px;">
                <b style="color:#031e34;">Evaluaciones en Modo Sombra de este artículo (${sombra.length}):</b>
                <div class="d-flex flex-wrap mt-1" style="gap:8px;">
                    ${sombra.slice(0, 5).map((s) => `
                        <span class="badge badge-light p-1" style="border:1px solid #cbd5e1;font-weight:500;">
                            ${esc(s.fecha)} · Est: <b>$${money.format(s.estimado_previo_mxn)}</b> vs Real: <b>$${money.format(s.real_mxn)}</b>
                            (<b style="color:${s.error_pct <= 10 ? "#047857" : "#b45309"};">Puntería ${s.precision_pct}%</b>)
                        </span>
                    `).join("")}
                </div>
            </div>
        ` : "";

        const host = dlg.fields_dict._body.$wrapper;
        host.html(`
            <div>
                <div class="d-flex justify-content-between align-items-center mb-2 flex-wrap" style="gap:8px;padding:8px 12px;background:#f0f9ff;border:1px solid #bae6fd;border-radius:8px;">
                    <div style="font-size:12px;color:#0c4a6e;">
                        <b>Familia:</b> ${esc(r.familia)} &nbsp;·&nbsp; <b>Gerencias:</b> ${esc(r.gerencias || "General")} &nbsp;·&nbsp; <b>Unidad base:</b> ${esc(r.uom_base)}
                    </div>
                    <label class="d-inline-flex align-items-center mb-0" style="gap:6px;font-size:12px;font-weight:700;color:#991b1b;cursor:pointer;">
                        <input type="checkbox" class="leb-det-recotizar" ${r.recotizar_siempre ? "checked" : ""}>
                        <span>Exige recotizar siempre (fija confianza en 0%)</span>
                    </label>
                </div>

                <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(185px,1fr));gap:10px;margin-bottom:12px;">
                    <div style="background:#fff;border:1px solid #cbd5e1;border-radius:8px;padding:9px 12px;">
                        <div class="small text-muted">Precio Estimado Limpio (s/IVA)</div>
                        <div style="font-size:20px;font-weight:800;color:#059669;">
                            ${r.tiene_historial ? `$${money.format(r.precio_estimado_mxn)} MXN` : "Sin muestra"}
                        </div>
                        <div class="small text-muted">Rango: $${money.format(r.precio_min_mxn)} – $${money.format(r.precio_max_mxn)}</div>
                    </div>
                    <div style="background:#fff;border:1px solid #cbd5e1;border-radius:8px;padding:9px 12px;">
                        <div class="small text-muted">Índice de Confianza</div>
                        <div class="d-flex align-items-center" style="gap:8px;margin-top:2px;">
                            <span style="font-size:20px;font-weight:800;color:#0f172a;">${r.indice_confianza}%</span>
                            <span class="indicator-pill ${esc(r.semaforo_color)}">${esc(r.semaforo)}</span>
                        </div>
                        <div class="small text-muted" style="font-size:10.5px;">
                            Frescura ${desg.frescura || 0}/35 · Muestra ${desg.muestra || 0}/25 · Estab. ${desg.estabilidad || 0}/25 · Tend. ${desg.tendencia || 0}/15
                        </div>
                    </div>
                    <div style="background:#fff;border:1px solid #cbd5e1;border-radius:8px;padding:9px 12px;">
                        <div class="small text-muted">Volatilidad y Tendencia</div>
                        <div style="font-size:16px;font-weight:800;color:#031e34;">${esc(r.tendencia_etiqueta)}</div>
                        <div class="small text-muted">Variación (CV): <b>${r.volatilidad_cv_pct}%</b> · Último: ${esc(r.ultima_fecha || "—")}</div>
                    </div>
                    <div style="background:#fff;border:1px solid #cbd5e1;border-radius:8px;padding:9px 12px;">
                        <div class="small text-muted">Respaldo Documental</div>
                        <div style="font-size:16px;font-weight:800;color:#0d5b94;">${r.muestra_total} evento(s)</div>
                        <div class="small text-muted">${r.muestra_compras} compra(s) · ${r.muestra_cotizaciones} cotiz. · ${r.proveedores_distintos} prov.</div>
                    </div>
                </div>

                ${construirSvgGraficaTiempoPrecio(puntos, r)}
                ${htmlSombra}

                <div class="mt-3">
                    <div style="font-weight:700;font-size:12.5px;color:#031e34;" class="mb-1">Historial Documental Normalizado en MXN (${puntos.length})</div>
                    <div class="table-responsive" style="max-height:240px;overflow:auto;">
                        <table class="table table-bordered table-sm mb-0" style="font-size:11.8px;">
                            <thead style="background:#031e34;color:#fff;position:sticky;top:0;">
                                <tr>
                                    <th>Fecha</th>
                                    <th>Tipo / Fuente</th>
                                    <th>Documento</th>
                                    <th>Proveedor</th>
                                    <th class="text-right">Captura Origen</th>
                                    <th class="text-right">Precio Limpio MXN</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${filasTabla || `<tr><td colspan="6" class="text-center text-muted py-3">Sin documentos registrados.</td></tr>`}
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>
        `);

        // Interacción de puntos en la gráfica SVG
        const $chartHost = host.find(".leb-svg-chart-host");
        const $tip = $chartHost.find(".leb-chart-tooltip");
        $chartHost.find(".leb-chart-pt").on("mouseenter", function (ev) {
            const p = puntos[Number($(this).data("idx"))];
            if (!p) return;
            const detUsd = p.moneda === "USD" ? `<br>Origen: $${money.format(p.precio_origen)} USD (TC DOF ${Number(p.tc_dof || 1).toFixed(4)})` : "";
            $tip.html(`
                <b>${esc(p.serie_etiqueta)} · ${esc(p.fecha)}</b><br>
                Precio limpio: <b>$${money.format(p.precio_mxn)} MXN / ${esc(r.uom_base)}</b>${detUsd}<br>
                Proveedor: ${esc(p.proveedor)}<br>
                Folio: <b>${esc(p.folio)}</b> (clic para abrir)
            `).removeClass("d-none");
            const rect = $chartHost[0].getBoundingClientRect();
            const left = Math.min(rect.width - 250, Math.max(8, ev.clientX - rect.left + 12));
            const top = Math.max(8, ev.clientY - rect.top - 48);
            $tip.css({ left: `${left}px`, top: `${top}px` });
        }).on("mouseleave", () => {
            $tip.addClass("d-none");
        }).on("click", function () {
            const p = puntos[Number($(this).data("idx"))];
            if (p && p.doctype && p.folio) {
                dlg.hide();
                frappe.set_route("Form", p.doctype, p.folio);
            }
        });

        host.find(".leb-pt-doc").on("click", function (ev) {
            ev.preventDefault();
            dlg.hide();
            frappe.set_route("Form", $(this).data("dt"), $(this).data("dn"));
        });

        host.find(".leb-det-recotizar").on("change", async function () {
            const flag = $(this).prop("checked") ? 1 : 0;
            await frappe.call({
                method: "lebaref_compras.ux_lebaref.actualizar_ficha_producto",
                type: "POST",
                args: {
                    item_code: r.item_code,
                    datos: JSON.stringify({ recotizar_siempre: flag }),
                },
                freeze: true,
            });
            frappe.show_alert({
                message: flag ? "Marcado como Exige recotizar siempre (Confianza 0%)" : "Bandera desactivada; confianza recalculada",
                indicator: "green",
            });
            dlg.hide();
            if (typeof onAfterChange === "function") onAfterChange();
            abrirGraficaTiempoPrecioArticulo(r.item_code, onAfterChange);
        });

        dlg.show();
    }

    async function abrirMotorPreciosConfianza(tabInicial = "catalogo") {
        if (!esUsuarioMotorOscar()) {
            frappe.msgprint("Acceso exclusivo para Gerencia de Compras.");
            return;
        }
        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

        const dlg = new frappe.ui.Dialog({
            title: "Motor Experimental de Precio Estimado, Confianza y Modo Sombra (Vista Gerencia)",
            size: "extra-large",
            fields: [{ fieldname: "_wrap", fieldtype: "HTML" }],
        });

        const estado = {
            tab: tabInicial || "catalogo",
            q: "",
            nivel: "Con historial",
            gerencia: "",
            cache: null,
        };

        const host = dlg.fields_dict._wrap.$wrapper;
        host.html(lebSkeletonHtml("table"));
        dlg.show();

        const cargarDatos = async () => {
            const { message } = await frappe.call({
                method: "lebaref_compras.motor_precios.tablero_confianza_precios",
                type: "GET",
                args: {
                    busqueda: estado.q,
                    nivel: estado.nivel,
                    gerencia: estado.gerencia,
                },
            });
            estado.cache = message;
            pintar();
        };

        const pintar = () => {
            const d = estado.cache;
            if (!d) return;
            const c = d.conteos || {};
            const sm = d.sombra || {};
            const cfg = d.config || {};

            let cuerpoTab = "";
            if (estado.tab === "catalogo") {
                const filas = (d.articulos || []).map((a) => `
                    <tr>
                        <td>
                            <b>${esc(a.codigo_barras)}</b>
                            <div class="text-muted" style="font-size:10.5px;">${esc(a.item_code)}</div>
                        </td>
                        <td>
                            <div style="font-weight:600;">${esc(a.item_name)}</div>
                            <div class="text-muted" style="font-size:10.5px;">${esc(a.familia)} ${a.gerencias ? "· " + esc(a.gerencias) : ""}</div>
                        </td>
                        <td class="text-right" style="white-space:nowrap;">
                            ${a.tiene_historial ? `<b style="color:#059669;font-size:13px;">$${money.format(a.precio_estimado_mxn)}</b> <small class="text-muted">/ ${esc(a.uom_base)}</small>` : `<span class="text-muted">—</span>`}
                        </td>
                        <td class="text-center" style="white-space:nowrap;">
                            <span class="indicator-pill ${esc(a.semaforo_color)}">${a.indice_confianza}% · ${esc(a.semaforo)}</span>
                        </td>
                        <td class="text-center small">
                            ${a.tiene_historial ? `<b>${a.muestra_total}</b> <span class="text-muted">(${a.muestra_compras}C / ${a.muestra_cotizaciones}Q)</span>` : "0"}
                        </td>
                        <td class="small">
                            ${a.tiene_historial ? `${esc(a.tendencia_etiqueta)} <span class="text-muted">(CV ${a.volatilidad_cv_pct}%)</span><br><span class="text-muted">${esc(a.ultima_fecha)}</span>` : `<span class="text-muted">Sin eventos</span>`}
                        </td>
                        <td class="text-right" style="white-space:nowrap;">
                            <button type="button" class="btn btn-xs btn-default leb-mp-ver-serie" data-sku="${esc(a.item_code)}">Ver gráfica</button>
                        </td>
                    </tr>
                `).join("");

                cuerpoTab = `
                    <div class="d-flex flex-wrap align-items-center mb-2" style="gap:8px;">
                        <input type="text" class="form-control input-xs leb-mp-q" style="max-width:260px;" placeholder="Buscar código, artículo o familia…" value="${esc(estado.q)}">
                        <select class="form-control input-xs leb-mp-niv" style="max-width:190px;">
                            ${["Con historial", "Todos", "Alta", "Media", "Baja", "Recotizar siempre"].map((op) => `<option value="${esc(op)}" ${estado.nivel === op ? "selected" : ""}>Nivel: ${esc(op)}</option>`).join("")}
                        </select>
                        <select class="form-control input-xs leb-mp-ger" style="max-width:190px;">
                            <option value="">Todas las gerencias</option>
                            ${["HVAC", "Eléctrico", "Obra Civil", "Mantenimiento Industrial", "Oficina"].map((g) => `<option value="${esc(g)}" ${estado.gerencia === g ? "selected" : ""}>${esc(g)}</option>`).join("")}
                        </select>
                        <span class="small text-muted ml-auto">Mostrando <b>${(d.articulos || []).length}</b> de ${d.total_filtrados || 0}</span>
                    </div>
                    <div class="table-responsive" style="max-height:420px;overflow:auto;">
                        <table class="table table-bordered table-sm mb-0" style="font-size:12px;">
                            <thead style="background:#031e34;color:#fff;position:sticky;top:0;z-index:1;">
                                <tr>
                                    <th>Clave / SKU</th>
                                    <th>Artículo y Familia</th>
                                    <th class="text-right">Precio Estimado MXN</th>
                                    <th class="text-center">Índice de Confianza</th>
                                    <th class="text-center">Muestra</th>
                                    <th>Tendencia / Último</th>
                                    <th class="text-right">Serie</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${filas || `<tr><td colspan="7" class="text-center text-muted py-4">No hay artículos que coincidan con el filtro actual.</td></tr>`}
                            </tbody>
                        </table>
                    </div>
                `;
            } else if (estado.tab === "sombra") {
                const nivelesHtml = Object.entries(sm.por_nivel || {}).map(([niv, st]) => `
                    <span class="badge badge-light p-2 mr-2 mb-1" style="border:1px solid #cbd5e1;font-size:11.5px;">
                        Confianza <b>${esc(niv)}</b> (${st.eventos} ev.): Puntería <b>${st.precision_pct}%</b> · Error medio ${st.mape_pct}%
                    </span>
                `).join("") || `<span class="text-muted small">Aún no se han acumulado comparaciones por nivel.</span>`;

                const filasSombra = (sm.registros || []).map((r) => {
                    const signo = r.diferencia_mxn >= 0 ? "+" : "";
                    return `
                        <tr>
                            <td style="white-space:nowrap;">${esc(r.fecha)}</td>
                            <td><a href="/app/supplier-quotation/${encodeURIComponent(r.cotizacion)}"><b>${esc(r.cotizacion)}</b></a><div class="text-muted small">${esc(r.proveedor)}</div></td>
                            <td>
                                <a href="#" class="leb-mp-ver-serie" data-sku="${esc(r.item_code)}"><b>${esc(r.item_name)}</b></a>
                                <div class="text-muted small">${esc(r.item_code)}</div>
                            </td>
                            <td class="text-right"><b>$${money.format(r.estimado_previo_mxn)}</b><div class="text-muted small">Conf. previa: ${r.confianza_previa_pct}% (${esc(r.semaforo_previo)})</div></td>
                            <td class="text-right"><b>$${money.format(r.real_mxn)}</b></td>
                            <td class="text-right" style="color:${r.error_pct <= 10 ? "#047857" : "#b45309"};">
                                <b>${signo}$${money.format(r.diferencia_mxn)}</b>
                                <div class="small">Desv: ${r.error_pct}%</div>
                            </td>
                            <td class="text-center">
                                <span class="indicator-pill ${r.error_pct <= 10 ? "green" : (r.error_pct <= 20 ? "orange" : "red")}">${r.precision_pct}%</span>
                            </td>
                        </tr>
                    `;
                }).join("");

                cuerpoTab = `
                    <div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        <b>Modo Sombra activo:</b> cada vez que Juan, Salvia o tú confirman una nueva cotización real, el motor guarda en privado cuánto estimaba justo antes contra el precio real conseguido, sin exponer ningún dato a los auxiliares.
                    </div>
                    <div class="mb-2">${nivelesHtml}</div>
                    <div class="table-responsive" style="max-height:390px;overflow:auto;">
                        <table class="table table-bordered table-sm mb-0" style="font-size:12px;">
                            <thead style="background:#031e34;color:#fff;position:sticky;top:0;">
                                <tr>
                                    <th>Fecha</th>
                                    <th>Cotización / Proveedor</th>
                                    <th>Artículo</th>
                                    <th class="text-right">Estimado Previo MXN</th>
                                    <th class="text-right">Cotización Real MXN</th>
                                    <th class="text-right">Diferencia</th>
                                    <th class="text-center">Puntería</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${filasSombra || `<tr><td colspan="7" class="text-center text-muted py-4">Las evaluaciones aparecerán automáticamente conforme se capturen nuevas cotizaciones sobre artículos con historial previo.</td></tr>`}
                            </tbody>
                        </table>
                    </div>
                `;
            } else {
                cuerpoTab = `
                    <div class="p-3" style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:8px;">
                        <div class="mb-2" style="font-size:12.5px;font-weight:700;color:#031e34;">Calibración de Semáforo y Ponderaciones del Motor</div>
                        <div class="row">
                            <div class="col-md-4 mb-2">
                                <label class="small font-weight-bold">Umbral Confianza Alta (%) — Verde</label>
                                <input type="number" class="form-control input-sm leb-cfg-alta" min="55" max="99" value="${cfg.umbral_alta}">
                            </div>
                            <div class="col-md-4 mb-2">
                                <label class="small font-weight-bold">Umbral Confianza Media (%) — Ámbar</label>
                                <input type="number" class="form-control input-sm leb-cfg-media" min="20" max="90" value="${cfg.umbral_media}">
                            </div>
                            <div class="col-md-4 mb-2">
                                <label class="small font-weight-bold">Variación estable máxima CV (%)</label>
                                <input type="number" step="0.5" class="form-control input-sm leb-cfg-cv" min="2" max="40" value="${cfg.cv_maximo_estable}">
                            </div>
                            <div class="col-md-3 mb-2">
                                <label class="small font-weight-bold">Días de frescura óptima</label>
                                <input type="number" class="form-control input-sm leb-cfg-dopt" min="7" max="180" value="${cfg.dias_vigencia_optima}">
                            </div>
                            <div class="col-md-3 mb-2">
                                <label class="small font-weight-bold">Días caducidad máxima</label>
                                <input type="number" class="form-control input-sm leb-cfg-dmax" min="60" max="1095" value="${cfg.dias_caducidad_maxima}">
                            </div>
                            <div class="col-md-3 mb-2">
                                <label class="small font-weight-bold">Peso Compra Real (OC / XML)</label>
                                <input type="number" step="0.1" class="form-control input-sm leb-cfg-preal" min="0.5" max="5" value="${cfg.peso_compra_real}">
                            </div>
                            <div class="col-md-3 mb-2">
                                <label class="small font-weight-bold">Peso Cotización</label>
                                <input type="number" step="0.1" class="form-control input-sm leb-cfg-pcot" min="0.2" max="3" value="${cfg.peso_cotizacion}">
                            </div>
                        </div>
                        <div class="mt-2 text-right">
                            <button type="button" class="btn btn-primary btn-sm leb-cfg-guardar">Guardar umbrales y recalcular motor</button>
                        </div>
                    </div>
                `;
            }

            host.html(`
                <div>
                    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:8px;margin-bottom:10px;">
                        <div style="background:#fff;border:1px solid #cbd5e1;border-radius:7px;padding:8px 10px;">
                            <div class="small text-muted">Con Historial</div>
                            <div style="font-size:18px;font-weight:800;color:#031e34;">${c.con_historial || 0} <small class="text-muted">/ ${c.total_catalogo || 0}</small></div>
                        </div>
                        <div style="background:#ecfdf5;border:1px solid #a7f3d0;border-radius:7px;padding:8px 10px;">
                            <div class="small" style="color:#065f46;">Confianza Alta (≥${cfg.umbral_alta}%)</div>
                            <div style="font-size:18px;font-weight:800;color:#047857;">${c.alta || 0}</div>
                        </div>
                        <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:7px;padding:8px 10px;">
                            <div class="small" style="color:#92400e;">Confianza Media (${cfg.umbral_media}–${cfg.umbral_alta - 1}%)</div>
                            <div style="font-size:18px;font-weight:800;color:#b45309;">${c.media || 0}</div>
                        </div>
                        <div style="background:#fef2f2;border:1px solid #fecaca;border-radius:7px;padding:8px 10px;">
                            <div class="small" style="color:#991b1b;">Baja / Recotizar siempre</div>
                            <div style="font-size:18px;font-weight:800;color:#dc2626;">${(c.baja || 0) + (c.recotizar_siempre || 0)}</div>
                        </div>
                        <div style="background:#f0f9ff;border:1px solid #bae6fd;border-radius:7px;padding:8px 10px;">
                            <div class="small" style="color:#075985;">Puntería Modo Sombra</div>
                            <div style="font-size:18px;font-weight:800;color:#0369a1;">${sm.total_evaluaciones ? `${sm.precision_promedio_pct}%` : "En escucha"}</div>
                            <div class="small text-muted" style="font-size:10px;">${sm.total_evaluaciones || 0} comparación(es) reales</div>
                        </div>
                    </div>

                    <div class="d-flex mb-3" style="gap:6px;border-bottom:1px solid #cbd5e1;padding-bottom:8px;">
                        <button type="button" class="btn btn-xs ${estado.tab === "catalogo" ? "btn-primary" : "btn-default"} leb-mp-tab" data-tab="catalogo">1. Catálogo y Confianza</button>
                        <button type="button" class="btn btn-xs ${estado.tab === "sombra" ? "btn-primary" : "btn-default"} leb-mp-tab" data-tab="sombra">2. Modo Sombra (${sm.total_evaluaciones || 0})</button>
                        <button type="button" class="btn btn-xs ${estado.tab === "config" ? "btn-primary" : "btn-default"} leb-mp-tab" data-tab="config">3. Umbrales y Reglas</button>
                    </div>

                    ${cuerpoTab}
                </div>
            `);

            host.find(".leb-mp-tab").on("click", function () {
                estado.tab = $(this).data("tab");
                pintar();
            });

            const dispararBusqueda = window.lebaref && window.lebaref.lebDebounce
                ? window.lebaref.lebDebounce(() => cargarDatos(), 220)
                : () => cargarDatos();

            host.find(".leb-mp-q").on("input", function () {
                estado.q = $(this).val();
                dispararBusqueda();
            });
            host.find(".leb-mp-niv").on("change", function () {
                estado.nivel = $(this).val();
                cargarDatos();
            });
            host.find(".leb-mp-ger").on("change", function () {
                estado.gerencia = $(this).val();
                cargarDatos();
            });
            host.find(".leb-mp-ver-serie").on("click", function (ev) {
                ev.preventDefault();
                const sku = $(this).data("sku");
                if (sku) abrirGraficaTiempoPrecioArticulo(sku, () => cargarDatos());
            });
            host.find(".leb-cfg-guardar").on("click", async () => {
                const payload = {
                    umbral_alta: Number(host.find(".leb-cfg-alta").val()),
                    umbral_media: Number(host.find(".leb-cfg-media").val()),
                    cv_maximo_estable: Number(host.find(".leb-cfg-cv").val()),
                    dias_vigencia_optima: Number(host.find(".leb-cfg-dopt").val()),
                    dias_caducidad_maxima: Number(host.find(".leb-cfg-dmax").val()),
                    peso_compra_real: Number(host.find(".leb-cfg-preal").val()),
                    peso_cotizacion: Number(host.find(".leb-cfg-pcot").val()),
                };
                await frappe.call({
                    method: "lebaref_compras.motor_precios.guardar_configuracion_motor",
                    type: "POST",
                    args: { datos: JSON.stringify(payload) },
                    freeze: true,
                    freeze_message: "Recalculando índice de confianza con los nuevos umbrales…",
                });
                frappe.show_alert({ message: "Umbrales actualizados y recalculados", indicator: "green" });
                estado.tab = "catalogo";
                cargarDatos();
            });
        };

        cargarDatos();
    }

    async function abrirModalProveedorRapido(proveedorId = "", alGuardar = null) {
        const { message: info } = await frappe.call({
            method: "lebaref_compras.ux_lebaref.obtener_ficha_edicion_proveedor",
            type: "GET",
            args: { proveedor: proveedorId || "" },
            freeze: true,
        });
        const esc = frappe.utils.escape_html;
        const esNuevo = Boolean(info && info.nuevo);
        const dat = (info && info.datos) || {};
        const grupos = (info && info.grupos) || [];
        let csfNombre = "", csfBase64 = "", carNombre = "", carBase64 = "";

        const linkCsfActual = dat.csf_archivo
            ? `<div class="small mt-1"><a href="${esc(dat.csf_archivo)}" target="_blank"><b>Ver CSF actual cargada</b></a></div>`
            : `<div class="small text-muted mt-1">Sin CSF cargada aún</div>`;
        const linkCarActual = dat.caratula_archivo
            ? `<div class="small mt-1"><a href="${esc(dat.caratula_archivo)}" target="_blank"><b>Ver Carátula Bancaria actual</b></a></div>`
            : `<div class="small text-muted mt-1">Sin Carátula Bancaria cargada aún</div>`;

        const dlg = new frappe.ui.Dialog({
            title: esNuevo ? "Registrar Nuevo Proveedor" : `Modificar Proveedor · ${dat.supplier_name || proveedorId}`,
            size: "large",
            fields: [
                {
                    fieldname: "supplier_name",
                    label: "Nombre o razón social del proveedor",
                    fieldtype: "Data",
                    default: dat.supplier_name || "",
                    reqd: 1,
                },
                {
                    fieldname: "tax_id",
                    label: "RFC",
                    fieldtype: "Data",
                    default: dat.tax_id || "",
                },
                {
                    fieldname: "especialidad",
                    label: "Especialidad / Qué cotiza (ej. HVAC, Eléctrico, Tornillería)",
                    fieldtype: "Data",
                    default: dat.especialidad || "",
                },
                {
                    fieldname: "supplier_group",
                    label: "Grupo de proveedor",
                    fieldtype: "Select",
                    options: grupos.join("\n"),
                    default: dat.supplier_group || grupos[0] || "",
                },
                { fieldtype: "Column Break" },
                {
                    fieldname: "contacto",
                    label: "Nombre de contacto / asesor",
                    fieldtype: "Data",
                    default: dat.contacto || "",
                },
                {
                    fieldname: "telefono",
                    label: "Teléfono / WhatsApp",
                    fieldtype: "Data",
                    default: dat.telefono || "",
                },
                {
                    fieldname: "correo",
                    label: "Correo electrónico",
                    fieldtype: "Data",
                    default: dat.correo || "",
                },
                {
                    fieldname: "clabe",
                    label: "CLABE interbancaria / Cuenta bancaria",
                    fieldtype: "Data",
                    default: dat.clabe || "",
                },
                {
                    fieldtype: "Section Break",
                    label: "Expediente Fiscal y Bancario (CSF y Carátula Bancaria)",
                },
                {
                    fieldname: "_csf_box",
                    fieldtype: "HTML",
                    options: `<div class="form-group mb-2">
                        <label class="control-label">Constancia de Situación Fiscal — CSF (PDF o Imagen)</label>
                        <input type="file" accept=".pdf,image/*" class="form-control leb-prov-in-csf">
                        ${linkCsfActual}
                    </div>`,
                },
                {
                    fieldname: "csf_fecha",
                    label: "Fecha de emisión de la CSF",
                    fieldtype: "Date",
                    default: dat.csf_fecha || "",
                },
                { fieldtype: "Column Break" },
                {
                    fieldname: "_caratula_box",
                    fieldtype: "HTML",
                    options: `<div class="form-group mb-2">
                        <label class="control-label">Carátula Bancaria (PDF o Imagen)</label>
                        <input type="file" accept=".pdf,image/*" class="form-control leb-prov-in-car">
                        ${linkCarActual}
                    </div>`,
                },
            ],
            primary_action_label: esNuevo ? "Registrar proveedor" : "Guardar cambios",
            async primary_action(vals) {
                const payload = Object.assign(
                    {
                        id: esNuevo ? "" : (info.id || proveedorId),
                        csf_nombre: csfNombre,
                        csf_base64: csfBase64,
                        caratula_nombre: carNombre,
                        caratula_base64: carBase64,
                    },
                    vals
                );
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.ux_lebaref.guardar_proveedor_rapido",
                    type: "POST",
                    args: {
                        datos: JSON.stringify(payload),
                    },
                    freeze: true,
                    freeze_message: esNuevo ? "Registrando proveedor y expediente…" : "Actualizando datos y expediente del proveedor…",
                });
                dlg.hide();
                frappe.show_alert({
                    message: esNuevo ? `Proveedor registrado: ${res.supplier_name}` : `Proveedor actualizado: ${res.supplier_name}`,
                    indicator: "green",
                });
                if (typeof alGuardar === "function") alGuardar(res);
            },
        });

        dlg.fields_dict._csf_box.$wrapper.find(".leb-prov-in-csf").on("change", function () {
            const f = this.files && this.files[0];
            if (!f) return;
            csfNombre = f.name;
            const rd = new FileReader();
            rd.onload = (ev) => { csfBase64 = String(ev.target.result || ""); };
            rd.readAsDataURL(f);
            if (!dlg.get_value("csf_fecha")) {
                dlg.set_value("csf_fecha", frappe.datetime.get_today());
            }
        });
        dlg.fields_dict._caratula_box.$wrapper.find(".leb-prov-in-car").on("change", function () {
            const f = this.files && this.files[0];
            if (!f) return;
            carNombre = f.name;
            const rd = new FileReader();
            rd.onload = (ev) => { carBase64 = String(ev.target.result || ""); };
            rd.readAsDataURL(f);
        });

        dlg.show();
    }

    window.lebaref = {
        abrirOperacion,
        verDetalleEquipo,
        verSaldosEntrega,
        abrirMapaUbicaciones,
        abrirDirectorioProveedores,
        abrirModalProveedorRapido,
        abrirArchivoHistorico,
        abrirImpresorCodigosCarta,
        abrirEscaneoPistola,
        abrirDialogoNuevoCliente,
        abrirTableroPendientes,
        abrirAlertasStockMinimo,
        abrirAnaliticaEjecutiva,
        abrirCargaRapidaSolicitudCsv,
        esUsuarioMotorOscar,
        abrirGraficaTiempoPrecioArticulo,
        abrirMotorPreciosConfianza,
        SHEETS_URL,
    };

    // --- Botones guiados en Formularios de ERPNext Desk (máx. 4 botones visibles, iconos SVG limpios, sin emojis ni claves técnicas) ---
    const LEB_SVGS = {
        plus: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>',
        upload: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>',
        download: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>',
        printer: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>',
        check: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>',
        file: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>',
        sidebar: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><line x1="9" y1="3" x2="9" y2="21"/></svg>',
        alert: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
        search: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>',
        layers: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/></svg>',
        card: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="4" width="22" height="16" rx="2"/><line x1="1" y1="10" x2="23" y2="10"/></svg>',
        box: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/></svg>',
        edit: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>',
    };

    function lebBtn($btn, iconKey, label) {
        if (!$btn || !$btn.length) return $btn;
        const svg = LEB_SVGS[iconKey] || "";
        $btn.html(`<span class="d-inline-flex align-items-center" style="gap:5px;">${svg}<span>${frappe.utils.escape_html(label)}</span></span>`);
        return $btn;
    }

        frappe.ui.form.on("Item", {
            refresh(frm) {
                renderizarFichaProductoEnItem(frm);
                if (frm.is_new()) return;
                lebBtn(
                    frm.add_custom_button("Imprimir etiquetas", () =>
                        abrirImpresorCodigosCarta("articulos", frm.doc.item_code || frm.doc.name)
                    ),
                    "printer",
                    "Imprimir etiquetas"
                );
                if (window.lebaref && window.lebaref.esUsuarioMotorOscar && window.lebaref.esUsuarioMotorOscar()) {
                    frm.add_custom_button("Gráfica tiempo-precio", () =>
                        window.lebaref.abrirGraficaTiempoPrecioArticulo(frm.doc.item_code || frm.doc.name), "Más acciones"
                    );
                }
                frm.add_custom_button("Histórico de precios", () =>
                    abrirArchivoHistorico("precios", frm.doc.item_code || frm.doc.name), "Más acciones"
                );
                frm.add_custom_button("Mapa de zonas A–F", () => abrirMapaUbicaciones(), "Más acciones");
            },
        });

    frappe.ui.form.on("Supplier", {
        refresh(frm) {
            renderizarDirectorioEnPestanaProveedor(frm);
            if (frm.is_new()) return;
            if (frm.doc.disabled) {
                frm.dashboard.set_headline_alert("Proveedor deshabilitado — No aparece en capturas nuevas.", "orange");
            }
            if (frappe.model.can_write("Supplier")) {
                lebBtn(
                    frm.add_custom_button("Editar datos y expediente", () =>
                        abrirModalProveedorRapido(frm.doc.name, () => frm.reload_doc())
                    ),
                    "edit",
                    "Editar datos y expediente"
                );
                lebBtn(
                    frm.add_custom_button("Subir CSF / Carátula", () => {
                        if (window.lebaref && window.lebaref.abrirModalExpedienteFiscalProveedor) {
                            window.lebaref.abrirModalExpedienteFiscalProveedor(frm.doc.name, () => frm.reload_doc());
                        }
                    }),
                    "upload",
                    "Subir CSF / Carátula"
                );
            }
            frm.add_custom_button("Histórico de precios y facturas", () => abrirArchivoHistorico("precios", frm.doc.supplier_name || frm.doc.name), "Más acciones");
            if (frappe.model.can_write("Supplier") || frappe.model.can_delete("Supplier")) {
                frm.add_custom_button("Depurar o dar de baja", () => abrirDialogoBajaProveedor(frm), "Más acciones");
            }
        },
    });

    frappe.ui.form.on("Department", {
        refresh(frm) {
            if (frm.is_new() || frm.doc.is_group) return;
            const estaDeshab = Number(frm.doc.disabled || 0) === 1;
            if (estaDeshab) {
                frm.dashboard.set_headline_alert("Departamento deshabilitado — No se mostrará al crear nuevas solicitudes.", "orange");
            }
            if (frappe.model.can_write("Department")) {
                const txtDep = estaDeshab ? "Habilitar departamento" : "Deshabilitar departamento";
                lebBtn(
                    frm.add_custom_button(txtDep, async () => {
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
                    }).addClass("btn-primary"),
                    "check",
                    txtDep
                );
            }
        },
    });

    frappe.ui.form.on("Customer", {
        refresh(frm) {
            if (frm.is_new()) return;
            lebBtn(
                frm.add_custom_button("Nueva obra para este cliente", () =>
                    abrirOperacion("obra", "", "Nueva Obra / Proyecto", async (res) => {
                        await frappe.call({
                            method: "lebaref_compras.ux_lebaref.asignar_cliente_obra",
                            type: "POST",
                            args: { obra: res.folio, cliente: frm.doc.name },
                        });
                        frappe.set_route("Form", "Project", res.folio);
                    })
                ).addClass("btn-primary"),
                "plus",
                "Nueva obra para este cliente"
            );
        },
    });

    frappe.ui.form.on("Project", {
        refresh(frm) {
            if (frm.is_new()) return;
            renderizarExpedienteObraEnProject(frm);
            if (frm.doc.customer) {
                frm.dashboard.set_headline_alert(
                    `Obra vinculada al cliente: <b>${frappe.utils.escape_html(frm.doc.customer)}</b>`,
                    "blue"
                );
            }
            if (frappe.model.can_write("Project")) {
                lebBtn(frm.add_custom_button("Asignar cliente", () => abrirDialogoClienteObra(frm)), "edit", "Asignar cliente");
            }
            lebBtn(
                frm.add_custom_button("Imprimir expediente", () => {
                    if (frm.__lebExpObra) imprimirExpedienteObraCarta(frm.__lebExpObra);
                }),
                "printer",
                "Imprimir expediente"
            );
        },
    });

    async function abrirDialogoConciliarCfdi(frm) {
        const { message: evalIni } = await frappe.call({
            method: "lebaref_compras.comprobantes.consultar",
            type: "GET",
            args: { cfdi: frm.doc.name },
            freeze: true,
            freeze_message: "Leyendo conceptos del XML y renglones de la recepción…",
        });

        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const conceptos = (evalIni && evalIni.conceptos) || [];
        const renglones = (evalIni && evalIni.renglones) || [];
        const cargos = (evalIni && evalIni.cargos) || [];
        const tokenOp = nuevoUuid();

        const opcionesDestino = [{ val: "", lbl: "— Seleccionar renglón o cargo fijo —" }];
        for (const r of renglones) {
            opcionesDestino.push({
                val: `R:${r.id}`,
                lbl: `📦 ${r.item_code} · ${r.qty} ${r.uom || ""} (Neto $${money.format(r.net_amount || 0)})`,
            });
        }
        for (const cg of cargos) {
            opcionesDestino.push({
                val: `C:${cg.id}`,
                lbl: `🚚 Cargo fijo: ${cg.descripcion} ($${money.format(cg.importe || 0)})`,
            });
        }

        // Autopropuesta inteligente por coincidencia de código o importe neto
        const estadoMap = conceptos.map((c, idx) => {
            let propVal = "";
            const porCodigo = renglones.find((r) => c.codigo && (r.item_code === c.codigo));
            const porImporte = renglones.find((r) => Math.abs(Number(r.net_amount || 0) - Number(c.importe_neto || 0)) < 0.02);
            const porCargo = cargos.find((cg) => Math.abs(Number(cg.importe || 0) - Number(c.importe_neto || 0)) < 0.02);
            if (porCodigo) propVal = `R:${porCodigo.id}`;
            else if (porCargo && String(c.descripcion || "").toLowerCase().includes("flete")) propVal = `C:${porCargo.id}`;
            else if (porImporte) propVal = `R:${porImporte.id}`;
            else if (renglones[idx]) propVal = `R:${renglones[idx].id}`;
            return {
                concepto: c.id,
                descripcion: c.descripcion,
                codigo: c.codigo,
                cantidad: c.cantidad,
                unidad: c.unidad,
                importe_neto: c.importe_neto,
                destino: propVal,
                factor_xml: "1",
            };
        });

        const armarSeleccionPayload = () => ({
            rows: estadoMap
                .filter((m) => m.destino)
                .map((m) => {
                    if (m.destino.startsWith("C:")) {
                        return { concepto: String(m.concepto), cargo: m.destino.slice(2) };
                    }
                    return {
                        concepto: String(m.concepto),
                        renglon: m.destino.slice(2),
                        factor_xml: Number(m.factor_xml || 1),
                    };
                }),
        });

        const d = new frappe.ui.Dialog({
            title: `🔍 Conciliación Estructural CFDI 4.0 contra Recepción · ${frm.doc.name}`,
            size: "extra-large",
            fields: [{ fieldname: "_html", fieldtype: "HTML" }],
            primary_action_label: "✅ Guardar Conciliación Inmutable (XML-02)",
            async primary_action() {
                if (estadoMap.some((m) => !m.destino)) {
                    frappe.msgprint("Relaciona todos los conceptos del XML con un renglón o cargo fijo de la recepción.");
                    return;
                }
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.operaciones.ejecutar",
                    type: "POST",
                    args: {
                        accion: "conciliar_cfdi",
                        folio: frm.doc.name,
                        operacion: tokenOp,
                        datos: JSON.stringify(armarSeleccionPayload()),
                    },
                    freeze: true,
                    freeze_message: "Guardando conciliación inmutable del CFDI…",
                });
                d.hide();
                frappe.show_alert({ message: `CFDI conciliado correctamente (${res.folio})`, indicator: "green" });
                frm.reload_doc();
            },
        });

        const wrap = d.fields_dict._html.$wrapper;
        const diffTot = Number(evalIni.diferencia_total || 0);
        wrap.html(`
            <div class="p-2 mb-2 d-flex justify-content-between align-items-center flex-wrap" style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:8px;font-size:12px;gap:8px;">
                <div>
                    <b>Recepción:</b> ${esc(evalIni.recepcion)} &nbsp;|&nbsp;
                    <b>Moneda:</b> ${esc(evalIni.moneda)} &nbsp;|&nbsp;
                    <b>Total XML:</b> $${money.format(Number(evalIni.total_xml || 0))} &nbsp;|&nbsp;
                    <b>Total Recepción:</b> $${money.format(Number(evalIni.total_recepcion || 0))} &nbsp;|&nbsp;
                    <b style="color:${Math.abs(diffTot) < 0.01 ? "#059669" : "#dc2626"};">Dif. Total: $${money.format(diffTot)}</b>
                </div>
                <button type="button" class="btn btn-xs btn-default leb-cfdi-check">🔍 Verificar Cuadre Exacto</button>
            </div>
            <div class="leb-cfdi-status mb-2"></div>
            <div class="table-responsive">
                <table class="table table-bordered table-sm" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;">
                        <tr>
                            <th style="width:34px;">#</th>
                            <th>Concepto XML</th>
                            <th class="text-right" style="width:105px;">Cant. XML</th>
                            <th class="text-right" style="width:115px;">Neto XML</th>
                            <th style="width:290px;">Renglón o Cargo en Recepción</th>
                            <th style="width:105px;">Factor Inv.</th>
                        </tr>
                    </thead>
                    <tbody class="leb-cfdi-tbody"></tbody>
                </table>
            </div>
        `);

        const tbody = wrap.find(".leb-cfdi-tbody");
        for (const m of estadoMap) {
            const tr = $("<tr>").appendTo(tbody);
            $("<td>").text(m.concepto).appendTo(tr);
            $("<td>").html(`<b>${esc(m.codigo || "S/Clave")}</b> · ${esc(m.descripcion)}`).appendTo(tr);
            $("<td class='text-right'>").text(`${m.cantidad} ${m.unidad || ""}`).appendTo(tr);
            $("<td class='text-right'>").html(`<b>$${money.format(Number(m.importe_neto || 0))}</b>`).appendTo(tr);

            const tdSel = $("<td>").appendTo(tr);
            const sel = $('<select class="form-control form-control-sm">').appendTo(tdSel);
            for (const op of opcionesDestino) {
                $("<option>").val(op.val).text(op.lbl).prop("selected", op.val === m.destino).appendTo(sel);
            }

            const tdFac = $("<td>").appendTo(tr);
            const inpFac = $('<input type="number" step="0.0001" min="0.0001" class="form-control form-control-sm">')
                .val(m.factor_xml)
                .prop("disabled", m.destino.startsWith("C:"))
                .appendTo(tdFac);

            sel.on("change", function () {
                m.destino = $(this).val();
                const esCargo = m.destino.startsWith("C:");
                inpFac.prop("disabled", esCargo);
                if (esCargo) inpFac.val("1");
            });
            inpFac.on("input", function () {
                m.factor_xml = $(this).val();
            });
        }

        const verificar = async () => {
            if (estadoMap.some((m) => !m.destino)) {
                wrap.find(".leb-cfdi-status").html(`<div class="alert alert-warning py-2 px-3 mb-0" style="font-size:12px;">Selecciona un renglón o cargo para todos los conceptos XML antes de verificar.</div>`);
                return;
            }
            const { message: resCheck } = await frappe.call({
                method: "lebaref_compras.comprobantes.consultar",
                type: "GET",
                args: {
                    cfdi: frm.doc.name,
                    seleccion: JSON.stringify(armarSeleccionPayload()),
                },
            });
            if (resCheck && resCheck.coincide) {
                wrap.find(".leb-cfdi-status").html(`<div class="alert alert-success py-2 px-3 mb-0" style="font-size:12px;">✅ <b>Cuadre exacto verificado:</b> Moneda, cantidades convertidas, importes netos, cargos fijos y total coinciden al 100%. Puedes guardar la conciliación.</div>`);
            } else {
                const diffs = ((resCheck && resCheck.diferencias) || [])
                    .filter((x) => Number(x.diferencia_cantidad || 0) !== 0 || Number(x.diferencia_importe || 0) !== 0)
                    .map((x) => `${esc(x.renglon || x.cargo)}: dif. cant = <b>${esc(x.diferencia_cantidad)}</b>, dif. importe = <b>$${esc(x.diferencia_importe)}</b>`)
                    .join(" | ");
                wrap.find(".leb-cfdi-status").html(`<div class="alert alert-danger py-2 px-3 mb-0" style="font-size:12px;">⚠️ <b>Diferencias detectadas:</b> ${diffs || "Verifica moneda y total del comprobante."}</div>`);
            }
        };

        wrap.find(".leb-cfdi-check").on("click", verificar);
        d.show();
        setTimeout(verificar, 100);
    }

    frappe.ui.form.on("Lebaref CFDI", {
        async refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            if (frm.doc.recepcion) {
                frm.add_custom_button("Abrir Recepción", () =>
                    frappe.set_route("Form", "Purchase Receipt", frm.doc.recepcion)
                );
                let conciliacionId = null;
                let facturaActiva = null;
                try {
                    const concs = await frappe.db.get_list("Lebaref Conciliacion CFDI", {
                        filters: { cfdi: frm.doc.name },
                        fields: ["name"],
                        limit: 1,
                    });
                    if (concs && concs.length) conciliacionId = concs[0].name;
                } catch (e) {
                    // Sin lectura de conciliación
                }
                try {
                    const pinvs = await frappe.db.get_list("Purchase Invoice", {
                        filters: { custom_lebaref_cfdi: frm.doc.name, docstatus: ["!=", 2] },
                        fields: ["name", "docstatus", "status"],
                        limit: 1,
                    });
                    if (pinvs && pinvs.length) facturaActiva = pinvs[0];
                } catch (e) {
                    // Sin lectura de facturas
                }

                if (facturaActiva) {
                    frm.dashboard.set_headline_alert(
                        `CFDI 4.0 conciliado y vinculado a Factura de Compra <b>${frappe.utils.escape_html(facturaActiva.name)}</b> (${frappe.utils.escape_html(facturaActiva.status || "")}).`,
                        "green"
                    );
                    lebBtn(
                        frm.add_custom_button("Abrir factura de compra", () =>
                            frappe.set_route("Form", "Purchase Invoice", facturaActiva.name)
                        ).addClass("btn-primary"),
                        "file",
                        "Abrir factura de compra"
                    );
                } else if (conciliacionId) {
                    frm.dashboard.set_headline_alert(
                        `Conceptos XML conciliados contra Recepción <b>${frappe.utils.escape_html(frm.doc.recepcion)}</b>. Listo para generar Factura de Compra.`,
                        "green"
                    );
                    if (frappe.model.can_create("Purchase Invoice")) {
                        lebBtn(
                            frm.add_custom_button("Generar factura desde XML", () =>
                                abrirOperacion(
                                    "facturar_cfdi",
                                    frm.doc.name,
                                    `Generar Factura de Compra · ${frm.doc.name}`,
                                    (res) => frappe.set_route("Form", "Purchase Invoice", res.folio)
                                )
                            ).addClass("btn-primary"),
                            "file",
                            "Generar factura desde XML"
                        );
                    }
                } else {
                    frm.dashboard.set_headline_alert(
                        `CFDI 4.0 archivado contra Recepción <b>${frappe.utils.escape_html(frm.doc.recepcion)}</b> — Pendiente de conciliar conceptos.`,
                        "orange"
                    );
                    lebBtn(
                        frm.add_custom_button("Conciliar contra recepción", () =>
                            abrirDialogoConciliarCfdi(frm)
                        ).addClass("btn-primary"),
                        "check",
                        "Conciliar contra recepción"
                    );
                }
            } else if (frm.doc.custom_oc_origen) {
                frm.dashboard.set_headline_alert(
                    `Factura CFDI 4.0 Histórica Migrada · Serie/Folio: <b>${frappe.utils.escape_html(frm.doc.custom_serie_folio || "—")}</b> · Orden Origen: <b>${frappe.utils.escape_html(frm.doc.custom_oc_origen)}</b>`,
                    "blue"
                );
            }
        },
    });

    frappe.ui.form.on("Lebaref Conciliacion CFDI", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            if (frm.doc.cfdi) {
                frm.add_custom_button("Abrir CFDI 4.0", () => frappe.set_route("Form", "Lebaref CFDI", frm.doc.cfdi));
            }
            if (frm.doc.recepcion) {
                frm.add_custom_button("Abrir recepción", () => frappe.set_route("Form", "Purchase Receipt", frm.doc.recepcion));
            }
        },
    });

    frappe.ui.form.on("Stock Reconciliation", {
        refresh(frm) {
            if (frm.is_new()) return;
            if (frm.doc.custom_lebaref_tipo_ajuste) {
                const infoActa = `Tratamiento: <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_tipo_ajuste)}</b> · Acta: <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_referencia_ajuste || "—")}</b> · Motivo: ${frappe.utils.escape_html(frm.doc.custom_lebaref_motivo_ajuste || "—")}`;
                if (frm.doc.docstatus === 0) {
                    frm.dashboard.set_headline_alert(
                        `Ajuste / Merma en borrador pendiente de autorización — ${infoActa}`,
                        "orange"
                    );
                    if (frappe.model.can_submit("Stock Reconciliation")) {
                        lebBtn(
                            frm.add_custom_button("Autorizar ajuste", () =>
                                abrirOperacion(
                                    "autorizar_ajuste",
                                    frm.doc.name,
                                    `Autorizar ${frm.doc.custom_lebaref_tipo_ajuste} · ${frm.doc.name}`,
                                    () => frm.reload_doc()
                                )
                            ).addClass("btn-primary"),
                            "check",
                            "Autorizar ajuste"
                        );
                    }
                } else if (frm.doc.docstatus === 1) {
                    frm.dashboard.set_headline_alert(`Ajuste / Merma autorizado — ${infoActa}`, "green");
                }
            }
        },
    });

    frappe.ui.form.on("Purchase Invoice", {
        refresh(frm) {
            if (frm.is_new()) return;
            const tipoComp = frm.doc.custom_lebaref_tipo_comprobante || "";
            if (tipoComp) {
                const detalleComp = tipoComp === "CFDI"
                    ? `UUID CFDI: <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_cfdi || frm.doc.bill_no || "—")}</b>`
                    : `Nota / Ticket: <b>${frappe.utils.escape_html(frm.doc.bill_no || "—")}</b> · Motivo: ${frappe.utils.escape_html(frm.doc.custom_lebaref_motivo_nota || "—")}`;
                frm.dashboard.set_headline_alert(
                    `Comprobante de Compra (${frappe.utils.escape_html(tipoComp)}) — ${detalleComp}`,
                    frm.doc.docstatus === 1 ? "green" : "orange"
                );
            }
            if (frm.doc.custom_lebaref_recepcion_comprobante) {
                frm.add_custom_button("Abrir recepción", () =>
                    frappe.set_route("Form", "Purchase Receipt", frm.doc.custom_lebaref_recepcion_comprobante),
                    "Más acciones"
                );
            }
            if (frm.doc.docstatus === 0 && frappe.model.can_submit("Purchase Invoice")) {
                lebBtn(
                    frm.add_custom_button("Autorizar factura", () =>
                        abrirOperacion(
                            "autorizar_factura",
                            frm.doc.name,
                            `Autorizar Comprobante de Compra · ${frm.doc.name}`,
                            () => frm.reload_doc()
                        )
                    ).addClass("btn-primary"),
                    "check",
                    "Autorizar factura"
                );
            }
            if (frm.doc.docstatus === 1 && !frm.doc.is_return && Number(frm.doc.outstanding_amount || 0) > 0 && frappe.model.can_create("Payment Entry")) {
                lebBtn(
                    frm.add_custom_button("Registrar pago", () =>
                        abrirOperacion(
                            "pagar_factura",
                            frm.doc.name,
                            `Registrar Pago de Factura · ${frm.doc.name}`,
                            (res) => frappe.set_route("Form", "Payment Entry", res.folio)
                        )
                    ).addClass("btn-primary"),
                    "card",
                    "Registrar pago"
                );
            }
        },
    });

    frappe.ui.form.on("Payment Entry", {
        refresh(frm) {
            if (frm.is_new()) return;
            if (frm.doc.custom_lebaref_factura) {
                frm.dashboard.set_headline_alert(
                    `Pago vinculado a Factura de Compra <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_factura)}</b> · Ref: <b>${frappe.utils.escape_html(frm.doc.reference_no || "—")}</b>`,
                    frm.doc.docstatus === 1 ? "green" : "orange"
                );
                lebBtn(
                    frm.add_custom_button("Abrir factura", () =>
                        frappe.set_route("Form", "Purchase Invoice", frm.doc.custom_lebaref_factura)
                    ),
                    "file",
                    "Abrir factura"
                );
            }
            if (frm.doc.docstatus === 0 && frm.doc.custom_lebaref_factura && frappe.model.can_submit("Payment Entry")) {
                lebBtn(
                    frm.add_custom_button("Autorizar pago", () =>
                        abrirOperacion(
                            "autorizar_pago",
                            frm.doc.name,
                            `Autorizar Pago · ${frm.doc.name}`,
                            () => frm.reload_doc()
                        )
                    ).addClass("btn-primary"),
                    "check",
                    "Autorizar pago"
                );
            }
        },
    });

    frappe.ui.form.on("Request for Quotation", {
        refresh(frm) {
            if (frm.doc.docstatus === 1) {
                lebBtn(
                    frm.add_custom_button("Registrar oferta", () =>
                        abrirOperacion("oferta", frm.doc.name, `Registrar oferta comercial para ${frm.doc.name}`)
                    ).addClass("btn-primary"),
                    "plus",
                    "Registrar oferta"
                );
            }
            frm.add_custom_button("Directorio de proveedores", () => abrirDirectorioProveedores(), "Más acciones");
        },
    });

    frappe.ui.form.on("Purchase Order", {
        async refresh(frm) {
            if (frm.is_new()) return;
            const estOp = (frm.doc.custom_lebaref_estado_operativo || "").trim();
            if (estOp) {
                const extraPromesa = frm.doc.custom_lebaref_fecha_promesa_entrega ? ` · Promesa: <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_fecha_promesa_entrega)}</b>` : "";
                const extraFoto = frm.doc.custom_lebaref_foto_entrega_obra
                    ? ` · <a href="${frappe.utils.escape_html(frm.doc.custom_lebaref_foto_entrega_obra)}" target="_blank"><b>Ver foto en obra</b></a>`
                    : "";
                const tonoEst = estOp.includes("Entregado") ? "green" : ((estOp.includes("Esperando") || estOp.includes("En espera")) ? "purple" : "blue");
                frm.dashboard.set_headline_alert(`Estado actual: <b>${frappe.utils.escape_html(estOp)}</b>${extraPromesa}${extraFoto}`, tonoEst);
            }

            if (frm.doc.docstatus < 2) {
                lebBtn(
                    frm.add_custom_button("Pago y logística", () => {
                        if (window.lebaref && window.lebaref.abrirModalPagoLogisticaOc) {
                            window.lebaref.abrirModalPagoLogisticaOc(frm.doc.name, frm.doc.supplier, () => frm.reload_doc());
                        }
                    }),
                    "card",
                    "Pago y logística"
                );
                frm.add_custom_button("Confirmar entrega en obra (con foto)", () => {
                    if (window.lebaref && window.lebaref.abrirModalEntregaObraFoto) {
                        window.lebaref.abrirModalEntregaObraFoto(frm.doc.name, () => frm.reload_doc());
                    }
                }, "Más acciones");
                frm.add_custom_button("Hilo conductor conciliado", () => {
                    if (window.lebaref && window.lebaref.abrirHiloConductor) {
                        window.lebaref.abrirHiloConductor("Purchase Order", frm.doc.name);
                    }
                }, "Más acciones");
                if (frm.doc.supplier) {
                    frm.add_custom_button("Expediente fiscal proveedor (CSF/Banco)", () => {
                        if (window.lebaref && window.lebaref.abrirModalExpedienteFiscalProveedor) {
                            window.lebaref.abrirModalExpedienteFiscalProveedor(frm.doc.supplier, () => frm.reload_doc());
                        }
                    }, "Más acciones");
                }
            }

            if (frm.doc.docstatus === 0 && frappe.model.can_submit("Purchase Order")) {
                lebBtn(
                    frm.add_custom_button("Autorizar orden", () =>
                        abrirOperacion("autorizar", frm.doc.name, `Autorizar Orden de Compra ${frm.doc.name}`, () => frm.reload_doc())
                    ).addClass("btn-primary"),
                    "check",
                    "Autorizar orden"
                );
            }
            if (frm.doc.docstatus === 1 && frm.doc.status !== "Closed" && frm.doc.status !== "On Hold") {
                if (frappe.model.can_create("Purchase Invoice") && Number(frm.doc.per_billed || 0) < 100) {
                    lebBtn(
                        frm.add_custom_button("Registrar factura XML", () => {
                            if (window.lebaref && window.lebaref.abrirAsistenteFacturaXml) {
                                window.lebaref.abrirAsistenteFacturaXml({ purchaseOrder: frm.doc.name });
                            }
                        }).addClass("btn-primary"),
                        "upload",
                        "Registrar factura XML"
                    );
                }
                lebBtn(
                    frm.add_custom_button("Recibir en almacén", () =>
                        abrirOperacion("recibir", frm.doc.name, `Recibir mercancía de ${frm.doc.name}`)
                    ),
                    "box",
                    "Recibir en almacén"
                );
                if (frappe.model.can_write("Purchase Order") && frappe.model.can_submit("Purchase Order")) {
                    frm.add_custom_button("Autorizar sustitución", () =>
                        abrirOperacion(
                            "autorizar_sustitucion",
                            frm.doc.name,
                            `Autorizar sustitución de artículo · ${frm.doc.name}`,
                            () => frm.reload_doc()
                        ),
                        "Más acciones"
                    );
                    if (Number(frm.doc.per_received || 0) < 100 && frm.doc.status !== "Completed") {
                        frm.add_custom_button("Cerrar saldo pendiente", () =>
                            abrirDialogoCierreReaperturaOc(frm, "cerrar"),
                            "Más acciones"
                        );
                    }
                }
            }
            if (frm.doc.docstatus === 1 && frm.doc.status === "Closed") {
                frm.dashboard.set_headline_alert("Orden de Compra cerrada / saldo finiquitado.", "orange");
                if (frappe.model.can_write("Purchase Order") && frappe.model.can_submit("Purchase Order")) {
                    lebBtn(
                        frm.add_custom_button("Reabrir orden", () =>
                            abrirDialogoCierreReaperturaOc(frm, "reabrir")
                        ).addClass("btn-primary"),
                        "check",
                        "Reabrir orden"
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
                            `Sustitución autorizada para recibir: ${detalle}`,
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
                    "Entrada anticipada sin Orden de Compra — Pendiente de vincular.",
                    "orange"
                );
                lebBtn(
                    frm.add_custom_button("Vincular a orden de compra", () =>
                        abrirDialogoRegularizarEntrada(frm)
                    ).addClass("btn-primary"),
                    "check",
                    "Vincular a orden de compra"
                );
            }
            if (frm.doc.docstatus === 1 && !frm.doc.custom_lebaref_por_regularizar && !frm.doc.is_return) {
                lebBtn(
                    frm.add_custom_button("Archivar XML CFDI", () =>
                        abrirOperacion("cfdi", frm.doc.name, `Archivar XML CFDI contra ${frm.doc.name}`, (res) =>
                            frappe.set_route("Form", "Lebaref CFDI", res.folio)
                        )
                    ).addClass("btn-primary"),
                    "upload",
                    "Archivar XML CFDI"
                );
                if (frappe.model.can_create("Purchase Invoice")) {
                    lebBtn(
                        frm.add_custom_button("Registrar nota sin XML", () =>
                            abrirOperacion("nota_compra", frm.doc.name, `Documentar Compra con Nota o Ticket · ${frm.doc.name}`, (res) =>
                                frappe.set_route("Form", "Purchase Invoice", res.folio)
                            )
                        ),
                        "file",
                        "Registrar nota sin XML"
                    );
                }
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
                frm.dashboard.set_headline_alert(`Recepción con sustitución autorizada: ${txtSust}`, "blue");
            }
        },
    });

    frappe.ui.form.on("Lebaref Sustitucion", {
        refresh(frm) {
            if (!frm.is_new() && frm.doc.docstatus === 1) {
                frm.disable_save();
                if (frm.doc.orden) {
                    frm.add_custom_button("Abrir orden de compra", () =>
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

            if (frm.doc.custom_lebaref_tipo_sobrante) {
                const detalleActa = `Acta: <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_referencia_sobrante || "—")}</b> · Entrega: ${frappe.utils.escape_html(frm.doc.custom_lebaref_persona_sobrante || "—")} · Tratamiento: <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_tipo_sobrante)}</b>`;
                if (frm.doc.docstatus === 0) {
                    frm.dashboard.set_headline_alert(
                        `Sobrante histórico en borrador pendiente de autorización — ${detalleActa}`,
                        "orange"
                    );
                    if (frappe.model.can_submit("Stock Entry")) {
                        lebBtn(
                            frm.add_custom_button("Autorizar sobrante", () =>
                                abrirOperacion(
                                    "autorizar_sobrante",
                                    frm.doc.name,
                                    `Autorizar sobrante histórico · ${frm.doc.name}`,
                                    () => frm.reload_doc()
                                )
                            ).addClass("btn-primary"),
                            "check",
                            "Autorizar sobrante"
                        );
                    }
                } else if (frm.doc.docstatus === 1) {
                    frm.dashboard.set_headline_alert(
                        `Sobrante histórico autorizado — ${detalleActa}`,
                        "green"
                    );
                }
                return;
            }

            if (frm.doc.docstatus === 1 && esTransferencia && !frm.doc.custom_lebaref_origen) {
                lebBtn(
                    frm.add_custom_button("Registrar consumo", () =>
                        abrirOperacion("consumir", frm.doc.name, `Registrar consumo real en obra · ${frm.doc.name}`)
                    ).addClass("btn-primary"),
                    "check",
                    "Registrar consumo"
                );
                lebBtn(
                    frm.add_custom_button("Devolver sobrante", () =>
                        abrirOperacion("devolver", frm.doc.name, `Devolver sobrante de ${frm.doc.name}`)
                    ),
                    "box",
                    "Devolver sobrante"
                );
                frm.add_custom_button("Ver saldo en resguardo", () =>
                    verSaldosEntrega(frm.doc.name),
                    "Más acciones"
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
                        frm.dashboard.set_headline_alert(`Control de resguardo en obra: ${txt}`, "blue");
                    }
                } catch (e) {
                    // Ignorar si no aplica a este traslado
                }
            }

            if (frm.doc.docstatus === 1 && frm.doc.custom_lebaref_origen) {
                frm.dashboard.set_headline_alert(
                    esConsumo
                        ? `Consumo real en obra vinculado a la entrega <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_origen)}</b>.`
                        : `Retorno de sobrante a almacén vinculado a la entrega <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_origen)}</b>.`,
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
            lebBtn(frm.add_custom_button("Ver historial", () => verDetalleEquipo("Lebaref Herramienta", frm.doc.name)), "file", "Ver historial");
            lebBtn(
                frm.add_custom_button("Imprimir ficha", () =>
                    abrirImpresorCodigosCarta("herramientas", frm.doc.codigo || frm.doc.name)
                ),
                "printer",
                "Imprimir ficha"
            );
            frm.add_custom_button("Compra y garantía", () =>
                abrirOperacion("garantia_herramienta", frm.doc.name, `Compra y garantía · ${frm.doc.name}`, () => frm.reload_doc()), "Más acciones");
            frm.add_custom_button("Registrar mantenimiento", () =>
                abrirOperacion("mantenimiento_herramienta", frm.doc.name, `Mantenimiento · ${frm.doc.name}`, () => frm.reload_doc()), "Más acciones");
            frm.add_custom_button("Cambiar estado o ubicación", () =>
                abrirOperacion("estado_herramienta", frm.doc.name, `Estado de herramienta · ${frm.doc.name}`, () => frm.reload_doc()), "Más acciones");
            frm.add_custom_button("Mapa de zonas A–F", () => abrirMapaUbicaciones(), "Más acciones");
        },
    });

    frappe.ui.form.on("Lebaref Mantenimiento Herramienta", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            if (!frm.doc.anulado) {
                frm.add_custom_button("Anular mantenimiento", () =>
                    abrirOperacion("anular_mantenimiento", frm.doc.name, `Anular mantenimiento · ${frm.doc.name}`, () => frm.reload_doc())
                );
            }
        },
    });

    frappe.ui.form.on("Lebaref Kit", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            lebBtn(
                frm.add_custom_button("Prestar kit", () =>
                    abrirOperacion("prestamo_kit", frm.doc.name, `Prestar herramientas del kit ${frm.doc.name}`)
                ).addClass("btn-primary"),
                "box",
                "Prestar kit"
            );
            lebBtn(
                frm.add_custom_button("Imprimir etiqueta", () =>
                    abrirImpresorCodigosCarta("kits", frm.doc.codigo || frm.doc.name)
                ),
                "printer",
                "Imprimir etiqueta"
            );
            frm.add_custom_button("Ver contenido", () => verDetalleEquipo("Lebaref Kit", frm.doc.name), "Más acciones");
            frm.add_custom_button("Editar composición", () =>
                abrirOperacion("composicion_kit", frm.doc.name, `Composición de ${frm.doc.name}`, () => frm.reload_doc()), "Más acciones");
        },
    });

    frappe.ui.form.on("Lebaref Prestamo", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            if (frm.doc.estado !== "Devuelto") {
                lebBtn(
                    frm.add_custom_button("Registrar retorno", () =>
                        abrirOperacion("retorno_herramienta", frm.doc.name, `Retorno de préstamo ${frm.doc.name}`, () => frm.reload_doc())
                    ).addClass("btn-primary"),
                    "check",
                    "Registrar retorno"
                );
            }
            lebBtn(frm.add_custom_button("Ver detalle", () => verDetalleEquipo("Lebaref Prestamo", frm.doc.name)), "file", "Ver detalle");
        },
    });

    frappe.ui.form.on("Lebaref Vehiculo", {
        refresh(frm) {
            if (frm.is_new()) return;
            frm.disable_save();
            renderizarIndicadoresVehiculoEnFlotilla(frm);
            lebBtn(
                frm.add_custom_button("Registrar gasto", () =>
                    abrirOperacion("gasto_vehiculo", frm.doc.name, `Gasto o combustible · ${frm.doc.name}`, () => frm.reload_doc())
                ).addClass("btn-primary"),
                "plus",
                "Registrar gasto"
            );
            lebBtn(
                frm.add_custom_button("Check-list semanal", () =>
                    abrirOperacion("revision_vehiculo", frm.doc.name, `Check-list vehicular · ${frm.doc.name}`, () => frm.reload_doc())
                ),
                "check",
                "Check-list semanal"
            );
            frm.add_custom_button("Imprimir tarjetón", () =>
                abrirImpresorCodigosCarta("vehiculos", frm.doc.codigo || frm.doc.name), "Más acciones");
            frm.add_custom_button("Ver bitácora", () => verDetalleEquipo("Lebaref Vehiculo", frm.doc.name), "Más acciones");
            frm.add_custom_button("Estado y seguro", () =>
                abrirOperacion("estado_vehiculo", frm.doc.name, `Estado y seguro · ${frm.doc.name}`, () => frm.reload_doc()), "Más acciones");
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
            .leb-item-split-panel,
            .leb-sol-split-panel {
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
            .leb-sol-split-panel {
                width: 424px;
                flex: 0 0 424px;
            }
            .leb-item-split-panel.leb-hidden,
            .leb-sol-split-panel.leb-hidden {
                display: none !important;
            }
            @media (max-width: 1024px) {
                .leb-split-wrapper {
                    flex-direction: column !important;
                }
                .leb-item-split-panel,
                .leb-sol-split-panel {
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
                if (target.closest(".list-row-checkbox, input[type='checkbox'], .select-like, .list-row-like, .like-action, .list-row-head, .filterable, .indicator-pill")) {
                    return;
                }
                const rowContainer = target.closest(".list-row-container");
                if (!rowContainer) return;
                const code = extraerCodigoDeFila(rowContainer);
                if (!code) return;
                ev.preventDefault();
                ev.stopPropagation();
                if (typeof ev.stopImmediatePropagation === "function") ev.stopImmediatePropagation();
                const lv = wrapper.__lebListview || window.cur_list || null;
                if (lv && lv.doctype === "Item") {
                    seleccionarItemEnSplit(lv, code);
                } else if (lv && lv.doctype === "Material Request") {
                    seleccionarSolicitudEnSplit(lv, code);
                }
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
                const lv = wrapper.__lebListview || window.cur_list || null;
                const dt = (lv && lv.doctype) || "Item";
                if (code) frappe.set_route("Form", dt, code);
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
        const lnk = rowContainer.querySelector("a[data-doctype][data-name], a[data-name]");
        if (lnk && lnk.getAttribute("data-name")) return lnk.getAttribute("data-name");
        for (const pref of ["/app/item/", "/app/material-request/"]) {
            const hrefEl = rowContainer.querySelector(`a[href*='${pref}']`);
            if (hrefEl) {
                const p = hrefEl.getAttribute("href").split(pref)[1];
                if (p) return decodeURIComponent(p.split("?")[0]);
            }
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
        $body.html(lebSkeletonHtml("panel"));
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
            const famDlId = "leb-fam-dl-" + Date.now();
            const optsFamDl = (f.familias_disponibles || [])
                .map((fm) => `<option value="${esc(fm)}"></option>`)
                .join("");
            const gerActivas = new Set(f.gerencias_lista || []);
            const checksGerencias = (f.gerencias_disponibles || ["HVAC", "Eléctrico", "Obra Civil", "Mantenimiento Industrial", "Oficina"])
                .map((g) => `
                    <label class="d-inline-flex align-items-center mb-1 mr-2" style="font-size:11px;gap:4px;padding:2px 7px;border:1px solid #cbd5e1;border-radius:12px;background:#f8fafc;cursor:pointer;font-weight:600;">
                        <input type="checkbox" class="leb-ed-ger-chk" value="${esc(g)}" ${gerActivas.has(g) ? "checked" : ""}>
                        <span>${esc(g)}</span>
                    </label>
                `)
                .join("");
            const tplIni = f.plantilla_sugerida || {};
            const prevImg = f.imagen
                ? `<img src="${esc(f.imagen)}" style="max-height:108px;max-width:100%;object-fit:contain;">`
                : `<span class="text-muted small">Sin imagen (pega con Ctrl+V o sube archivo)</span>`;

            $body.html(`
                <div>
                    <datalist id="${famDlId}">${optsFamDl}</datalist>
                    <div style="height:118px;border:1px solid var(--border-color,#cbd5e1);border-radius:8px;background:#fff;display:flex;align-items:center;justify-content:center;padding:4px;margin-bottom:8px;">
                        ${prevImg}
                    </div>
                    <div class="d-flex mb-3" style="gap:6px;">
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-ed-upload">Subir foto</button>
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
                            <label class="control-label small mb-1">Familia Técnica</label>
                            <input type="text" list="${famDlId}" class="form-control input-xs leb-ed-fam" value="${esc(valLimpio(f.familia))}">
                        </div>
                    </div>

                    <div class="mb-2">
                        <label class="control-label small mb-1">Gerencias usuarias (multigerencia)</label>
                        <div>${checksGerencias}</div>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Ubicación / Anaquel</label>
                            <input type="text" class="form-control input-xs leb-ed-ubi" value="${esc(valLimpio(f.ubicacion))}">
                        </div>
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Marca</label>
                            <input type="text" class="form-control input-xs leb-ed-marca" value="${esc(valLimpio(f.marca))}">
                        </div>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Modelo</label>
                            <input type="text" class="form-control input-xs leb-ed-modelo" placeholder="${esc(tplIni.modelo || "")}" value="${esc(valLimpio(f.modelo))}">
                        </div>
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Medidas</label>
                            <input type="text" class="form-control input-xs leb-ed-medidas" placeholder="${esc(tplIni.medidas || "")}" value="${esc(valLimpio(f.medidas))}">
                        </div>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-12 px-1 mb-2">
                            <label class="control-label small mb-1">Capacidad / Uso</label>
                            <input type="text" class="form-control input-xs leb-ed-cap" placeholder="${esc(tplIni.capacidad || "")}" value="${esc(valLimpio(f.capacidad))}">
                        </div>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Especificación 1</label>
                            <input type="text" class="form-control input-xs leb-ed-esp1" placeholder="${esc(tplIni.espec1 || "")}" value="${esc(valLimpio(f.espec1))}">
                        </div>
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Especificación 2</label>
                            <input type="text" class="form-control input-xs leb-ed-esp2" placeholder="${esc(tplIni.espec2 || "")}" value="${esc(valLimpio(f.espec2))}">
                        </div>
                    </div>

                    <div class="row" style="margin:0 -4px;">
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Stock Mínimo</label>
                            <input type="number" step="0.01" min="0" class="form-control input-xs leb-ed-min" value="${f.stock_minimo || 0}">
                        </div>
                        <div class="col-6 px-1 mb-2">
                            <label class="control-label small mb-1">Último Costo s/IVA</label>
                            <input type="number" step="0.01" min="0" class="form-control input-xs leb-ed-costo" value="${f.ultimo_costo || 0}">
                        </div>
                    </div>

                    <div class="p-2 mb-3" style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;font-size:11.5px;">
                        <label class="d-flex align-items-center mb-1" style="gap:6px;cursor:pointer;font-weight:600;">
                            <input type="checkbox" class="leb-ed-recotizar" ${f.recotizar_siempre ? "checked" : ""}>
                            <span>Exige recotizar siempre (fabricación a medida / volátil)</span>
                        </label>
                        <label class="d-flex align-items-center mb-0" style="gap:6px;cursor:pointer;font-weight:500;color:#475569;">
                            <input type="checkbox" class="leb-ed-consdir" ${f.consumo_directo ? "checked" : ""}>
                            <span>Consumo directo (Oficina / no pasa por almacén)</span>
                        </label>
                    </div>

                    <button type="button" class="btn btn-primary btn-sm btn-block leb-ed-save">Guardar cambios en ficha</button>
                    <button type="button" class="btn btn-default btn-xs btn-block mt-2 leb-ed-full">Abrir formulario avanzado</button>
                </div>
            `);

            $body.find(".leb-ed-cat").on("change", function () {
                const catSel = $(this).val() || "";
                const mapaTpls = f.plantillas_por_categoria || {};
                const tpl = mapaTpls[catSel] || {};
                if (tpl.modelo) $body.find(".leb-ed-modelo").attr("placeholder", tpl.modelo);
                if (tpl.medidas) $body.find(".leb-ed-medidas").attr("placeholder", tpl.medidas);
                if (tpl.capacidad) $body.find(".leb-ed-cap").attr("placeholder", tpl.capacidad);
                if (tpl.espec1) $body.find(".leb-ed-esp1").attr("placeholder", tpl.espec1);
                if (tpl.espec2) $body.find(".leb-ed-esp2").attr("placeholder", tpl.espec2);
            });
            $body.find(".leb-ed-upload").on("click", () => $body.find(".leb-ed-file").trigger("click"));
            $body.find(".leb-ed-file").on("change", function () {
                const file = this.files && this.files[0];
                if (file) subirArchivoImagenSplit(listview, f.codigo_interno, file);
            });
            $body.find(".leb-ed-full").on("click", () => frappe.set_route("Form", "Item", f.codigo_interno));
            $body.find(".leb-ed-save").on("click", async () => {
                const gerSel = [];
                $body.find(".leb-ed-ger-chk:checked").each(function () {
                    gerSel.push($(this).val());
                });
                const payload = {
                    codigo_barras: $body.find(".leb-ed-cb").val(),
                    nombre: $body.find(".leb-ed-nom").val(),
                    categoria: $body.find(".leb-ed-cat").val(),
                    familia: $body.find(".leb-ed-fam").val(),
                    gerencias: gerSel,
                    recotizar_siempre: $body.find(".leb-ed-recotizar").prop("checked") ? 1 : 0,
                    consumo_directo: $body.find(".leb-ed-consdir").prop("checked") ? 1 : 0,
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
            ["Familia", f.familia || "—"],
            ["Gerencias", (f.gerencias_lista || []).join(", ") || "—"],
            ["Marca", f.marca],
            ["Modelo", f.modelo],
            ["Medidas", f.medidas],
            ["Capacidad / Uso", f.capacidad],
            ["Especificaciones", [f.espec1, f.espec2].filter((x) => x && x !== "—").join(" · ") || "—"],
            ["Unidad / Conv.", convDetalle],
        ]
            .map(([k, v]) => `<div class="leb-sp-kv"><div class="leb-sp-kv-lbl">${esc(k)}</div><div class="leb-sp-kv-val">${esc(v || "—")}</div></div>`)
            .join("");

        const flagsHtml = (f.recotizar_siempre || f.consumo_directo)
            ? `<div class="mb-2 d-flex flex-wrap" style="gap:4px;">
                ${f.recotizar_siempre ? `<span class="badge badge-warning" style="font-size:10.5px;">Exige recotizar siempre</span>` : ""}
                ${f.consumo_directo ? `<span class="badge badge-secondary" style="font-size:10.5px;">Consumo directo (Oficina)</span>` : ""}
               </div>`
            : "";

        const aliasItems = [
            ...(f.claves_proveedores || []).map((c) => `<span class="badge badge-light mr-1 mb-1" style="border:1px solid var(--border-color,#cbd5e1);">${esc(c.proveedor)}: <b>${esc(c.clave)}</b></span>`),
            ...(f.alias_equivalencias || []).map((a) => `<span class="badge badge-light mr-1 mb-1" style="border:1px solid #93c5fd;" title="${esc(a.dictamen)}">${esc(a.texto)} <small class="text-muted">(${esc(a.alcance)})</small></span>`),
        ];
        const xmlChips = aliasItems.length
            ? `<div class="mt-2 pt-2" style="border-top:1px solid var(--border-color,#e2e8f0);font-size:11px;">
                <div class="text-muted mb-1">Alias, equivalencias y claves XML:</div>
                ${aliasItems.join("")}
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
                <div style="font-family:Consolas,monospace;font-size:11.5px;color:var(--text-muted,#64748b);margin:2px 0 8px 0;">
                    Clave / Cód. Barras: <b style="color:var(--text-color,#0f172a);">${esc(f.codigo_barras)}</b>
                </div>
                ${flagsHtml}

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

                ${f.estimacion_oscar ? (() => {
                    const eo = f.estimacion_oscar;
                    const txtEst = eo.tiene_historial ? `$${money.format(eo.precio_estimado_mxn || 0)} MXN / ${esc(eo.uom_base || f.uom_almacen)}` : "Sin muestra histórica";
                    const subEst = eo.tiene_historial
                        ? `Muestra: <b>${eo.muestra_total}</b> (${eo.muestra_compras} compras · ${eo.muestra_cotizaciones} cotiz.) · ${esc(eo.tendencia_etiqueta)}`
                        : "Aún no existen cotizaciones ni órdenes registradas";
                    return `
                        <div style="padding:9px 11px;border-radius:8px;background:#f0f9ff;border:1px solid #bae6fd;margin-bottom:10px;">
                            <div class="d-flex justify-content-between align-items-center mb-1" style="gap:6px;">
                                <span style="font-size:10.5px;font-weight:800;color:#0369a1;letter-spacing:0.02em;">MOTOR DE ESTIMACIÓN (SOLO GERENCIA)</span>
                                <span class="indicator-pill ${esc(eo.semaforo_color || "gray")}">${eo.indice_confianza}% · ${esc(eo.semaforo)}</span>
                            </div>
                            <div style="font-size:15px;font-weight:800;color:#0f172a;">${txtEst}</div>
                            <div style="font-size:11px;color:#334155;margin-top:2px;">${subEst}</div>
                            <button type="button" class="btn btn-xs btn-default btn-block mt-2 leb-sp-grafica-oscar" style="border-color:#7dd3fc;color:#0369a1;font-weight:600;">Ver gráfica tiempo-precio</button>
                        </div>
                    `;
                })() : ""}

                <div class="d-flex justify-content-between align-items-center mb-2 pb-2" style="border-bottom:1px solid var(--border-color,#e2e8f0);">
                    <div>
                        <div style="font-size:11px;color:var(--text-muted,#64748b);">Ubicación en almacén</div>
                        <div style="font-family:Consolas,monospace;font-size:13.5px;font-weight:700;">${esc(f.ubicacion)}</div>
                    </div>
                    <button type="button" class="btn btn-default btn-xs leb-sp-mapa">Ver mapa</button>
                </div>

                <div class="mb-2">${filasKv}</div>
                ${xmlChips}

                <div class="mt-2 p-1 text-center" style="background:#fff;border:1px solid var(--border-color,#e2e8f0);border-radius:6px;">
                    ${f.svg_barcode || ""}
                </div>

                <div class="mt-3">
                    <button type="button" class="btn btn-primary btn-sm btn-block leb-sp-add-sol">+ Agregar a solicitud</button>
                    <div class="d-flex mt-2" style="gap:6px;">
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sp-edit">Editar</button>
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sp-lbl">Etiqueta</button>
                    </div>
                    <div class="d-flex mt-1" style="gap:6px;">
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sp-alias">+ Alias / Equiv.</button>
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sp-hist">Historial</button>
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sp-open">Abrir ficha</button>
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
        $body.find(".leb-sp-alias").on("click", () => {
            if (window.lebaref && window.lebaref.abrirModalProponerEquivalencia) {
                window.lebaref.abrirModalProponerEquivalencia({
                    articulo_propuesto: f.codigo_interno,
                    origen: "Revisión Manual",
                }, () => seleccionarItemEnSplit(listview, f.codigo_interno));
            }
        });
        $body.find(".leb-sp-lbl").on("click", () => abrirImpresorCodigosCarta("articulos", f.codigo_interno));
        $body.find(".leb-sp-hist").on("click", () => abrirArchivoHistorico("precios", f.codigo_interno));
        $body.find(".leb-sp-open").on("click", () => frappe.set_route("Form", "Item", f.codigo_interno));
        $body.find(".leb-sp-grafica-oscar").on("click", () => {
            if (window.lebaref && window.lebaref.abrirGraficaTiempoPrecioArticulo) {
                window.lebaref.abrirGraficaTiempoPrecioArticulo(f.codigo_interno, () => seleccionarItemEnSplit(listview, f.codigo_interno));
            }
        });
    }

    // --- Vista Dividida Maestro–Detalle en Solicitudes (Lista a la izquierda + Ficha y Expediente a la derecha) ---
    function obtenerVersionAssetLebaref() {
        try {
            const sc = document.querySelector('script[src*="lebaref_desk.js"]');
            if (sc && sc.src) {
                const m = sc.src.match(/[?&]v=([^&]+)/);
                if (m && m[1]) return m[1];
            }
        } catch (e) {}
        if (!window.__lebSessionAssetVer) {
            window.__lebSessionAssetVer = String(Math.floor(Date.now() / 60000));
        }
        return window.__lebSessionAssetVer;
    }

    async function asegurarModuloSolicitud() {
        if (window.lebarefSolicitud) return window.lebarefSolicitud;
        const ver = encodeURIComponent(obtenerVersionAssetLebaref());
        try {
            await $.getScript(`/files/material_request.js?v=${ver}`);
        } catch (e) {
            await $.getScript(`/assets/lebaref_compras/js/material_request.js?v=${ver}`);
        }
        return window.lebarefSolicitud || null;
    }

    function inicializarSplitSolicitudes(listview) {
        inyectarEstilosSplitCatalogo();
        asegurarListenersVentanaSplitCatalogo();
        const $page = listview.$page;
        if (!$page || !$page.length) return;

        if (!listview.__lebSplitSol) {
            const estado = {
                folioActual: "",
                tabActiva: "ficha",
                oculto: false,
                fichaCache: null,
            };
            listview.__lebSplitSol = estado;

            $page.addClass("leb-split-active-page");
            const $wrapper = $page.find(".layout-main-section-wrapper").first();
            $wrapper.addClass("leb-split-wrapper leb-split-active");
            if ($wrapper.get(0)) $wrapper.get(0).__lebListview = listview;

            const $panel = $(`
                <aside class="leb-sol-split-panel">
                    <div class="leb-sp-hdr">
                        <div style="font-weight:700;font-size:13.5px;">Solicitud</div>
                        <div class="d-flex align-items-center" style="gap:6px;">
                            <div class="leb-sp-tabs">
                                <button type="button" class="leb-sp-tab active" data-tab="ficha">Ficha</button>
                                <button type="button" class="leb-sp-tab" data-tab="expediente">Expediente</button>
                            </div>
                        </div>
                    </div>
                    <div class="leb-sp-body">
                        <div class="text-muted small py-4 text-center">Selecciona una solicitud de la lista izquierda para ver su ficha, materiales y seguimiento.</div>
                    </div>
                </aside>
            `).appendTo($wrapper);

            estado.$panel = $panel;
            estado.$wrapper = $wrapper;
            estado.$page = $page;

            $panel.find(".leb-sp-tab").on("click", function () {
                estado.tabActiva = $(this).data("tab");
                $panel.find(".leb-sp-tab").each(function () {
                    $(this).toggleClass("active", $(this).data("tab") === estado.tabActiva);
                });
                pintarContenidoSplitSolicitud(listview);
            });

            $(document).off("keydown.lebSolSplit").on("keydown.lebSolSplit", (ev) => {
                const route = frappe.get_route ? frappe.get_route() : [];
                if (!route || route[0] !== "List" || route[1] !== "Material Request" || estado.oculto) return;
                if ($(ev.target).is("input, textarea, select, [contenteditable]")) return;
                if (ev.key !== "ArrowDown" && ev.key !== "ArrowUp") return;
                const lista = (listview.data || []).map((d) => d.name).filter(Boolean);
                if (!lista.length) return;
                ev.preventDefault();
                const idx = lista.indexOf(estado.folioActual);
                const nextIdx = ev.key === "ArrowDown"
                    ? Math.min(lista.length - 1, idx + 1)
                    : Math.max(0, (idx === -1 ? 0 : idx - 1));
                seleccionarSolicitudEnSplit(listview, lista[nextIdx]);
            });
        }
    }

    function decorarSeleccionListaSolicitudes(listview) {
        const estado = listview.__lebSplitSol;
        if (!estado || !listview.$result) return;
        listview.$result.find(".list-row-container").each(function () {
            const folio = extraerCodigoDeFila(this);
            if (!folio) return;
            $(this).toggleClass("leb-row-selected", folio === estado.folioActual);
        });
    }

    async function seleccionarSolicitudEnSplit(listview, folio) {
        const estado = listview.__lebSplitSol;
        if (!estado || !folio) return;
        estado.folioActual = folio;
        decorarSeleccionListaSolicitudes(listview);

        const $body = estado.$panel.find(".leb-sp-body");
        $body.html(lebSkeletonHtml("panel"));
        try {
            const { message: s } = await frappe.call({
                method: "lebaref_compras.ux_lebaref.ficha_solicitud",
                type: "GET",
                args: { solicitud: folio },
            });
            if (estado.folioActual !== folio) return;
            estado.fichaCache = s;
            pintarContenidoSplitSolicitud(listview);
        } catch (e) {
            $body.html(`<div class="text-muted small py-3 text-center">No se pudo cargar la ficha de ${frappe.utils.escape_html(folio)}.</div>`);
        }
    }

    async function copiarTextoPortapapelesSeguro(texto) {
        if (navigator.clipboard && window.isSecureContext) {
            try {
                await navigator.clipboard.writeText(texto);
                return true;
            } catch (e) {}
        }
        const ta = document.createElement("textarea");
        ta.value = texto;
        ta.setAttribute("readonly", "");
        ta.style.position = "fixed";
        ta.style.top = "-9999px";
        ta.style.opacity = "0";
        document.body.appendChild(ta);
        ta.focus();
        ta.select();
        let ok = false;
        try {
            ok = document.execCommand("copy");
        } catch (e) {
            ok = false;
        }
        document.body.removeChild(ta);
        return ok;
    }

    function decodificarEntidadesHtml(str) {
        if (!str) return "";
        const txt = document.createElement("textarea");
        txt.innerHTML = String(str);
        return txt.value;
    }

    function agruparFilasPorPartida(lista, obtenerPartida) {
        const arr = Array.isArray(lista) ? lista : [];
        const hayPartida = arr.some((it) => decodificarEntidadesHtml(obtenerPartida(it) || "").trim() !== "");
        if (!hayPartida) {
            return [{ partida: "", items: arr }];
        }
        const orden = [];
        const mapa = new Map();
        for (const it of arr) {
            const p = decodificarEntidadesHtml(obtenerPartida(it) || "").trim() || "Partida general";
            if (!mapa.has(p)) {
                mapa.set(p, []);
                orden.push(p);
            }
            mapa.get(p).push(it);
        }
        return orden.map((p) => ({ partida: p, items: mapa.get(p) }));
    }

    function renderizarBloquesPartidaEnFormulario(frm) {
        if (!frm || !frm.fields_dict || !frm.fields_dict.items || !frm.fields_dict.items.$wrapper) return;
        const $gridWrap = frm.fields_dict.items.$wrapper;
        $gridWrap.find(".leb-form-partida-blocks").remove();
        const rows = Array.isArray(frm.doc && frm.doc.items) ? frm.doc.items : [];
        const tienePartidas = rows.some((r) => decodificarEntidadesHtml(r.custom_lebaref_partida || "").trim() !== "");
        if (!tienePartidas) return;

        const esc = frappe.utils.escape_html;
        const numFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 0, maximumFractionDigits: 4 });
        const moneyFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const mostrarImportes = frm.doctype !== "Material Request";
        const monedaDoc = (frm.doc && frm.doc.currency) || "MXN";
        const bloques = agruparFilasPorPartida(rows, (r) => r.custom_lebaref_partida);
        const colSpan = mostrarImportes ? 5 : 3;

        const trsHtml = bloques.map((g) => {
            const sep = `<tr style="background:#e2e8f0;color:#031e34;font-weight:800;font-size:11.5px;">
                <td colspan="${colSpan}" style="padding:6px 10px;border-top:2px solid #cbd5e1;">
                    ${esc(g.partida)} <span style="font-weight:600;color:#475569;">(${g.items.length})</span>
                </td>
            </tr>`;
            const itemsHtml = g.items.map((r) => {
                const clave = esc(r.custom_lebaref_clave_vista || (r.item_code === "LEBAREF-CONCEPTO-LIBRE" ? "Libre" : (r.item_code || "")));
                const desc = esc(decodificarEntidadesHtml(r.custom_lebaref_descripcion_vista || r.custom_lebaref_concepto || r.item_name || r.description || r.item_code || ""));
                const uom = esc(r.uom || r.stock_uom || "Pieza");
                const colsPrecio = mostrarImportes
                    ? `<td class="text-right" style="padding:5px 8px;white-space:nowrap;">$${moneyFmt.format(Number(r.rate || 0))}</td>
                       <td class="text-right" style="padding:5px 8px;white-space:nowrap;font-weight:700;">$${moneyFmt.format(Number(r.amount || (Number(r.qty || 0) * Number(r.rate || 0))))} ${esc(monedaDoc)}</td>`
                    : "";
                return `<tr>
                    <td style="padding:5px 8px;width:42px;color:#64748b;">#${r.idx || ""}</td>
                    <td style="padding:5px 8px;"><b>${desc}</b><span class="text-muted small ml-2">${clave}${r.project ? " · " + esc(r.project) : ""}</span></td>
                    <td class="text-right" style="padding:5px 8px;white-space:nowrap;font-weight:700;">${numFmt.format(Number(r.qty || 0))} ${uom}</td>
                    ${colsPrecio}
                </tr>`;
            }).join("");
            return sep + itemsHtml;
        }).join("");

        const hdrPrecio = mostrarImportes
            ? `<th class="text-right" style="padding:5px 8px;width:110px;">P. Unit.</th><th class="text-right" style="padding:5px 8px;width:135px;">Importe</th>`
            : "";

        const $card = $(`
            <details class="leb-form-partida-blocks mb-2" open style="border:1px solid #cbd5e1;border-radius:8px;background:#ffffff;overflow:hidden;">
                <summary style="cursor:pointer;padding:7px 12px;background:#f8fafc;font-size:12px;font-weight:700;color:#031e34;user-select:none;">
                    Vista agrupada por bloques de Partida (${bloques.length} ${bloques.length === 1 ? "bloque" : "bloques"} · ${rows.length} conceptos)
                </summary>
                <div class="table-responsive" style="max-height:310px;overflow-y:auto;">
                    <table class="table table-sm mb-0" style="font-size:12px;">
                        <thead style="background:#031e34;color:#fff;position:sticky;top:0;z-index:1;">
                            <tr>
                                <th style="padding:5px 8px;width:42px;">#</th>
                                <th style="padding:5px 8px;">Descripción / Concepto</th>
                                <th class="text-right" style="padding:5px 8px;width:110px;">Cantidad</th>
                                ${hdrPrecio}
                            </tr>
                        </thead>
                        <tbody>${trsHtml}</tbody>
                    </table>
                </div>
            </details>
        `);
        $gridWrap.prepend($card);
    }

    async function copiarSolicitudParaWhatsapp(folio, filasRaw = null) {
        let filas = filasRaw;
        if (!Array.isArray(filas)) {
            const doc = await frappe.db.get_doc("Material Request", folio);
            filas = (doc.items || []).map((r) => ({
                qty: r.qty,
                uom: r.uom || r.stock_uom || "Pieza",
                partida: r.custom_lebaref_partida || "",
                descripcion: r.custom_lebaref_descripcion_vista || r.custom_lebaref_concepto || r.item_name || r.description || r.item_code || "",
            }));
        }
        const limpias = (filas || []).filter((r) => {
            const d = decodificarEntidadesHtml(r.descripcion || r.articulo || "").trim();
            return d && !d.includes("Pendiente de cargar partidas");
        });
        if (!limpias.length) {
            frappe.msgprint("Agrega primero al menos un artículo o partida a la solicitud para copiar el mensaje de cotización.");
            return;
        }
        const numFmtWa = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 0, maximumFractionDigits: 2 });
        const hoy = frappe.datetime && frappe.datetime.get_today ? frappe.datetime.get_today() : new Date().toISOString().slice(0, 10);
        const lineas = [
            `SOLICITUD DE COTIZACIÓN — Folio: ${folio}`,
            `Fecha: ${hoy}`,
            "Favor de cotizar los siguientes materiales indicando precio unitario, tiempo de entrega y vigencia:",
            "------------------------------------------------------------",
        ];
        let partidaActual = "";
        limpias.forEach((r, idx) => {
            const part = decodificarEntidadesHtml(r.partida || r.custom_lebaref_partida || "").trim();
            if (part && part !== partidaActual) {
                if (idx > 0) lineas.push("");
                lineas.push(`*${part}*`);
                partidaActual = part;
            }
            const cant = numFmtWa.format(Number(r.qty !== undefined ? r.qty : (r.cantidad || 1)));
            const uom = decodificarEntidadesHtml(r.uom || r.unidad || "Pieza").trim();
            const desc = decodificarEntidadesHtml(r.descripcion || r.articulo || "").replace(/\s+/g, " ").trim();
            lineas.push(`${idx + 1}. (${cant} ${uom}) ${desc}`);
        });
        lineas.push("------------------------------------------------------------");
        lineas.push("Atentamente: Gerencia de Compras");

        const mensaje = lineas.join("\n");
        const ok = await copiarTextoPortapapelesSeguro(mensaje);
        if (ok) {
            frappe.show_alert({
                message: `Mensaje limpio para WhatsApp copiado (${limpias.length} partida(s) de ${folio}).`,
                indicator: "green",
            });
        } else {
            frappe.msgprint({
                title: "Copia manual para WhatsApp",
                message: `<textarea class="form-control" rows="10" readonly>${frappe.utils.escape_html(mensaje)}</textarea>`,
            });
        }
    }

    function reemplazarCopiaJsonNativa() {
        if (!window.frappe || !frappe.ui || !frappe.ui.form || !frappe.ui.form.Toolbar) return;
        const proto = frappe.ui.form.Toolbar.prototype;
        if (proto.__lebCopyPatched) return;
        proto.__lebCopyPatched = true;
        proto.add_copy_to_clipboard = function () {
            if (!this.frm || !this.page) return;
            if (this.frm.doctype === "Material Request") {
                this.page.add_menu_item(
                    "Copiar para WhatsApp",
                    () => {
                        const filas = (this.frm.doc.items || []).map((r) => ({
                            qty: r.qty,
                            uom: r.uom || r.stock_uom || "Pieza",
                            partida: r.custom_lebaref_partida || "",
                            descripcion: r.custom_lebaref_descripcion_vista || r.custom_lebaref_concepto || r.item_name || r.description || r.item_code || "",
                        }));
                        copiarSolicitudParaWhatsapp(this.frm.doc.name, filas);
                    },
                    true
                );
                return;
            }
            // En el resto de formularios, copiar folio legible en lugar de volcar JSON técnico
            this.page.add_menu_item(
                __("Copy to Clipboard"),
                () => {
                    copiarTextoPortapapelesSeguro(String(this.frm.doc.name || ""));
                    frappe.show_alert({ message: `Folio copiado: ${this.frm.doc.name}`, indicator: "green" });
                },
                true
            );
        };
    }
    reemplazarCopiaJsonNativa();
    setTimeout(reemplazarCopiaJsonNativa, 400);

    function pintarContenidoSplitSolicitud(listview) {
        const estado = listview.__lebSplitSol;
        if (!estado || !estado.$panel) return;
        const $body = estado.$panel.find(".leb-sp-body");
        const s = estado.fichaCache;

        estado.$panel.find(".leb-sp-tab").each(function () {
            $(this).toggleClass("active", $(this).data("tab") === estado.tabActiva);
        });

        if (!s) {
            $body.html(`
                <div class="text-muted text-center py-5">
                    <div style="opacity:0.45;">${LEB_SVGS.file}</div>
                    <div class="small mt-2">Selecciona una solicitud de la lista izquierda para ver su ficha y materiales.</div>
                </div>
            `);
            return;
        }

        const esc = frappe.utils.escape_html;
        const numFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 0, maximumFractionDigits: 4 });
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const exp = s.expediente || {};
        const totalDocsExp = (exp.anexos || []).length + (exp.cotizaciones || []).length + (exp.ordenes || []).length + (exp.almacen || []).length;

        // Actualizar contador sutil en pestaña Expediente
        estado.$panel.find(".leb-sp-tab[data-tab='expediente']").text(
            totalDocsExp ? `Expediente (${totalDocsExp})` : "Expediente"
        );

        const recargarActual = (nuevoFolio = "") => {
            listview.refresh();
            seleccionarSolicitudEnSplit(listview, nuevoFolio || s.folio);
        };

        if (estado.tabActiva === "expediente") {
            const secCot = (exp.cotizaciones || []).map((c) => `
                <div class="d-flex justify-content-between align-items-center py-1" style="border-bottom:1px solid var(--border-color,#f1f5f9);font-size:11.5px;">
                    <div>
                        <a href="/app/supplier-quotation/${encodeURIComponent(c.folio)}"><b>${esc(c.folio)}</b></a> · ${esc(c.proveedor)}
                        <div class="text-muted" style="font-size:10.5px;">${esc(c.fecha)} · ${esc(c.estado)}</div>
                    </div>
                    <div class="text-right"><b>$${money.format(c.total || 0)}</b> <small>${esc(c.moneda)}</small></div>
                </div>
            `).join("");

            const secOc = (exp.ordenes || []).map((o) => `
                <div class="d-flex justify-content-between align-items-center py-1" style="border-bottom:1px solid var(--border-color,#f1f5f9);font-size:11.5px;">
                    <div>
                        <a href="/app/purchase-order/${encodeURIComponent(o.folio)}"><b>${esc(o.folio)}</b></a> · ${esc(o.proveedor)}
                        <div class="text-muted" style="font-size:10.5px;">${esc(o.fecha)} · ${esc(o.estado)}${o.docstatus === 1 ? ` · Rec: ${o.pct_recibido}%` : ""}</div>
                    </div>
                    <div class="text-right"><b>$${money.format(o.total || 0)}</b> <small>${esc(o.moneda)}</small></div>
                </div>
            `).join("");

            const secAlm = (exp.almacen || []).map((m) => {
                const slug = m.doctype === "Purchase Receipt" ? "purchase-receipt" : "stock-entry";
                return `
                    <div class="d-flex justify-content-between align-items-center py-1" style="border-bottom:1px solid var(--border-color,#f1f5f9);font-size:11.5px;">
                        <div>
                            <a href="/app/${slug}/${encodeURIComponent(m.folio)}"><b>${esc(m.folio)}</b></a> · ${esc(m.tipo)}
                            <div class="text-muted" style="font-size:10.5px;">${esc(m.fecha)} ${m.detalle ? "· " + esc(m.detalle) : ""}</div>
                        </div>
                        <span class="badge badge-light" style="border:1px solid #cbd5e1;">${esc(m.estado)}</span>
                    </div>
                `;
            }).join("");

            const secAnx = (exp.anexos || []).map((a) => `
                <div class="d-flex justify-content-between align-items-center py-1" style="border-bottom:1px solid var(--border-color,#f1f5f9);font-size:11.5px;">
                    <div>
                        <a href="#" class="leb-sol-jump" data-folio="${esc(a.folio)}"><b>${esc(a.folio)}</b></a> · ${esc(a.nombre)}
                        <div class="text-muted" style="font-size:10.5px;">${esc(a.fecha)}</div>
                    </div>
                    <span class="badge badge-info">${esc(a.estado)}</span>
                </div>
            `).join("");

            $body.html(`
                <div>
                    <div class="d-flex justify-content-between align-items-center mb-2">
                        <div>
                            <div style="font-size:15px;font-weight:700;color:#0d5b94;">${esc(s.folio)}</div>
                            <div class="small text-muted">${esc(s.nombre)}</div>
                        </div>
                        <span class="indicator-pill ${esc(s.estatus_color)}">${esc(s.estatus_etiqueta)}</span>
                    </div>

                    <div style="display:grid;grid-template-columns:repeat(4,1fr);gap:6px;margin-bottom:12px;">
                        <div style="background:var(--subtle-fg,#f8fafc);border:1px solid var(--border-color,#e2e8f0);border-radius:6px;padding:6px;text-align:center;">
                            <div style="font-size:10px;color:#64748b;font-weight:700;">COTIZ.</div>
                            <div style="font-size:14px;font-weight:800;">${(exp.cotizaciones || []).length}</div>
                        </div>
                        <div style="background:var(--subtle-fg,#f8fafc);border:1px solid var(--border-color,#e2e8f0);border-radius:6px;padding:6px;text-align:center;">
                            <div style="font-size:10px;color:#64748b;font-weight:700;">ÓRDENES</div>
                            <div style="font-size:14px;font-weight:800;color:#059669;">${(exp.ordenes || []).length}</div>
                        </div>
                        <div style="background:var(--subtle-fg,#f8fafc);border:1px solid var(--border-color,#e2e8f0);border-radius:6px;padding:6px;text-align:center;">
                            <div style="font-size:10px;color:#64748b;font-weight:700;">ALMACÉN</div>
                            <div style="font-size:14px;font-weight:800;color:#0d5b94;">${(exp.almacen || []).length}</div>
                        </div>
                        <div style="background:var(--subtle-fg,#f8fafc);border:1px solid var(--border-color,#e2e8f0);border-radius:6px;padding:6px;text-align:center;">
                            <div style="font-size:10px;color:#64748b;font-weight:700;">ANEXOS</div>
                            <div style="font-size:14px;font-weight:800;">${(exp.anexos || []).length}</div>
                        </div>
                    </div>

                    <div class="mb-3">
                        <div style="font-size:11px;font-weight:700;color:#031e34;text-transform:uppercase;margin-bottom:4px;">1. Cotizaciones de Proveedores</div>
                        ${secCot || `<div class="text-muted small py-1">Sin cotizaciones registradas aún.</div>`}
                    </div>

                    <div class="mb-3">
                        <div style="font-size:11px;font-weight:700;color:#031e34;text-transform:uppercase;margin-bottom:4px;">2. Órdenes de Compra Generadas</div>
                        ${secOc || `<div class="text-muted small py-1">Sin órdenes de compra emitidas aún.</div>`}
                    </div>

                    <div class="mb-3">
                        <div style="font-size:11px;font-weight:700;color:#031e34;text-transform:uppercase;margin-bottom:4px;">3. Recepciones y Surtido en Almacén</div>
                        ${secAlm || `<div class="text-muted small py-1">Sin entradas o salidas registradas aún.</div>`}
                    </div>

                    ${(s.origen_anexo || secAnx) ? `
                        <div class="mb-3">
                            <div style="font-size:11px;font-weight:700;color:#031e34;text-transform:uppercase;margin-bottom:4px;">4. Solicitudes y Anexos Vinculados</div>
                            ${s.origen_anexo ? `<div class="small mb-1">Origen: <a href="#" class="leb-sol-jump" data-folio="${esc(s.origen_anexo)}"><b>${esc(s.origen_anexo)}</b></a></div>` : ""}
                            ${secAnx}
                        </div>
                    ` : ""}

                    <div class="d-flex mt-3" style="gap:6px;">
                        ${s.docstatus === 1 && s.tipo_raw === "Purchase" ? `<button type="button" class="btn btn-primary btn-xs flex-fill leb-sol-exp-comp">Cuadro comparativo</button>` : ""}
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sol-exp-print">Imprimir solicitud</button>
                    </div>
                </div>
            `);

            $body.find(".leb-sol-jump").on("click", function (ev) {
                ev.preventDefault();
                const destino = $(this).data("folio");
                if (destino) {
                    estado.tabActiva = "ficha";
                    seleccionarSolicitudEnSplit(listview, destino);
                }
            });
            $body.find(".leb-sol-exp-comp").on("click", async () => {
                const mod = await asegurarModuloSolicitud();
                if (mod && mod.abrirComparativa) mod.abrirComparativa(s.folio, () => recargarActual());
            });
            $body.find(".leb-sol-exp-print").on("click", () => {
                frappe.set_route("print", "Material Request", s.folio);
            });
            return;
        }

        // Pestaña 1: FICHA DE LA SOLICITUD (diseño limpio estilo ERP original integrado en ERPNext)
        const sig = s.siguiente_paso || {};
        const paletaSig = {
            blue: { bg: "#eff6ff", bd: "#bfdbfe", tit: "#1d4ed8", txt: "#1e3a8a" },
            amber: { bg: "#fffbeb", bd: "#fde68a", tit: "#b45309", txt: "#78350f" },
            purple: { bg: "#f5f3ff", bd: "#ddd6fe", tit: "#6d28d9", txt: "#4c1d95" },
            green: { bg: "#ecfdf5", bd: "#a7f3d0", tit: "#047857", txt: "#065f46" },
            gray: { bg: "#f8fafc", bd: "#cbd5e1", tit: "#475569", txt: "#334155" },
        }[sig.tono || "blue"] || { bg: "#eff6ff", bd: "#bfdbfe", tit: "#1d4ed8", txt: "#1e3a8a" };

        const botonesSecundarios = [];
        if (s.docstatus === 1 && s.status_raw !== "Stopped" && s.tipo_raw === "Purchase") {
            if (sig.accion !== "comparativa") {
                botonesSecundarios.push(`<button type="button" class="btn btn-xs btn-default leb-sol-act-comp">Comparativa</button>`);
            }
            if (sig.accion !== "cotizar") {
                botonesSecundarios.push(`<button type="button" class="btn btn-xs btn-default leb-sol-act-cot">+ Cotizar</button>`);
            }
            if (s.tiene_conceptos_libres) {
                botonesSecundarios.push(`<button type="button" class="btn btn-xs btn-default leb-sol-act-conv" title="Vincular concepto libre al catálogo de artículos">Vincular al catálogo</button>`);
                botonesSecundarios.push(`<button type="button" class="btn btn-xs btn-default leb-sol-act-eqv" title="Enviar concepto libre a la Bandeja de Equivalencias para revisión con gerencias">Enviar a equivalencias</button>`);
            } else {
                botonesSecundarios.push(`<button type="button" class="btn btn-xs btn-default leb-sol-act-stock" title="Canalizar a surtido con existencias actuales">Surtir de stock</button>`);
            }
        }

        if (s.docstatus !== 2 && s.puede_editar) {
            if (!s.en_espera_de && !s.concluida) {
                botonesSecundarios.push(`<button type="button" class="btn btn-xs btn-default leb-sol-act-espera" title="Pausar: depende de alguien externo (no cuenta como pendiente nuestro)">En espera</button>`);
            }
            if (!s.concluida) {
                botonesSecundarios.push(`<button type="button" class="btn btn-xs btn-default leb-sol-act-concluir" title="Declarar que ya no requiere más pasos">Concluida</button>`);
            }
        }

        const filasDatos = [
            ["Fecha", s.fecha_requerida && s.fecha_requerida !== s.fecha ? `${s.fecha} · Req: ${s.fecha_requerida}` : (s.fecha || "—")],
            ["Solicitante", s.solicitante || "—"],
            ["Departamento", s.departamento || "—"],
            ["Destino", s.destino || "—"],
            ["Origen", s.origen_texto || "—"],
        ]
            .map(([k, v]) => {
                const valHtml = k === "Origen" && s.origen_anexo
                    ? `Anexo de <a href="#" class="leb-sol-jump-orig" data-folio="${esc(s.origen_anexo)}"><b>${esc(s.origen_anexo)}</b></a>`
                    : esc(v);
                return `<div class="leb-sp-kv"><div class="leb-sp-kv-lbl">${esc(k)}</div><div class="leb-sp-kv-val">${valHtml}</div></div>`;
            })
            .join("");

        const bloquesSplit = agruparFilasPorPartida(s.partidas || [], (p) => p.partida);
        const filasPartidas = (s.partidas || []).length
            ? bloquesSplit.map((grupo) => {
                const sepRow = grupo.partida
                    ? `<tr style="background:#e2e8f0;color:#031e34;font-weight:800;font-size:11px;">
                        <td colspan="2" style="padding:5px 8px;border-top:1.5px solid #cbd5e1;">
                            ${esc(grupo.partida)} <span style="font-weight:600;color:#475569;">(${grupo.items.length})</span>
                        </td>
                    </tr>`
                    : "";
                const itemsRows = grupo.items.map((p) => {
                    const thumb = p.imagen
                        ? `<img src="${esc(p.imagen)}" style="width:24px;height:24px;border-radius:4px;object-fit:contain;border:1px solid #e2e8f0;background:#fff;flex-shrink:0;">`
                        : `<span style="display:inline-flex;align-items:center;justify-content:center;width:24px;height:24px;border-radius:4px;border:1px solid #e2e8f0;background:#f8fafc;color:#64748b;flex-shrink:0;">${p.es_libre ? LEB_SVGS.file : LEB_SVGS.box}</span>`;
                    const metaTags = [p.clave, p.obra].filter(Boolean).join(" · ");
                    const stockInfo = p.es_libre
                        ? `<span style="color:#b45309;">Concepto libre</span>`
                        : `<span style="color:${p.stock_actual > 0 ? "#059669" : "#64748b"};">Stock: ${numFmt.format(p.stock_actual)} ${esc(p.uom_almacen)}</span>`;
                    const avanceOc = p.ordered_qty > 0
                        ? `<div style="font-size:10px;color:#0d5b94;font-weight:600;">OC: ${numFmt.format(p.ordered_qty)}${p.received_qty > 0 ? ` · Rec: ${numFmt.format(p.received_qty)}` : ""}</div>`
                        : "";
                    const badgeValidado = p.no_recotizar
                        ? `<div class="mt-1"><span class="badge badge-success" style="font-size:10px;">Validado por Gerencia — No recotizar</span></div>`
                        : "";
                    let panelOscarFila = "";
                    if (s.es_motor_oscar && p.estimacion_oscar && s.docstatus < 2) {
                        const eo = p.estimacion_oscar;
                        const txtEst = eo.tiene_historial
                            ? `Est. $${money.format(eo.precio_estimado_uom_mxn)} · ${eo.indice_confianza}% (${esc(eo.semaforo)})`
                            : `Sin historial (${eo.indice_confianza}%)`;
                        const colorTxt = eo.semaforo_color === "green" ? "#047857" : (eo.semaforo_color === "orange" ? "#b45309" : "#dc2626");
                        panelOscarFila = `
                            <div class="d-flex align-items-center flex-wrap mt-1" style="gap:6px;font-size:10.5px;">
                                <a href="#" class="leb-sol-open-chart" data-sku="${esc(eo.item_code)}" style="font-weight:700;color:${colorTxt};text-decoration:underline;">${txtEst}</a>
                                ${s.status_raw !== "Stopped" && p.ordered_qty <= 0 ? `
                                    <button type="button" class="btn btn-xs ${p.no_recotizar ? "btn-default" : "btn-secondary"} leb-sol-toggle-val" data-mri="${esc(p.name)}" data-val="${p.no_recotizar ? "0" : "1"}" style="padding:1px 6px;font-size:10px;">
                                        ${p.no_recotizar ? "Reactivar cotización" : "Validado — No recotizar"}
                                    </button>
                                ` : ""}
                            </div>
                        `;
                    }
                    return `
                        <tr>
                            <td style="padding:6px 6px;">
                                <div class="d-flex align-items-start" style="gap:7px;">
                                    ${thumb}
                                    <div style="min-width:0;">
                                        <div style="font-weight:600;font-size:12px;line-height:1.25;color:var(--text-color,#0f172a);">${esc(decodificarEntidadesHtml(p.descripcion))}</div>
                                        <div class="text-muted" style="font-size:10.5px;">${metaTags ? `${esc(metaTags)} &nbsp;·&nbsp; ` : ""}${stockInfo}</div>
                                        ${badgeValidado}
                                        ${panelOscarFila}
                                    </div>
                                </div>
                            </td>
                            <td class="text-right" style="padding:6px 6px;white-space:nowrap;vertical-align:middle;">
                                <div style="font-weight:700;font-size:12px;">${numFmt.format(p.qty)} <span class="text-muted" style="font-weight:500;">${esc(p.uom)}</span></div>
                                ${avanceOc}
                            </td>
                        </tr>
                    `;
                }).join("");
                return sepRow + itemsRows;
            }).join("")
            : "";

        $body.html(`
            <div>
                <div class="d-flex justify-content-between align-items-start mb-1" style="gap:8px;">
                    <div style="min-width:0;">
                        <div style="font-size:16px;font-weight:800;color:#0d5b94;line-height:1.2;">${esc(s.folio)}</div>
                        <div style="font-size:11px;color:var(--text-muted,#64748b);">${esc(s.tipo_etiqueta)}</div>
                    </div>
                    <span class="indicator-pill ${esc(s.estatus_color)}" style="flex-shrink:0;">${esc(s.estatus_etiqueta)}</span>
                </div>

                <div class="d-flex justify-content-between align-items-center mb-2 pb-2" style="border-bottom:1px solid var(--border-color,#e2e8f0);gap:6px;">
                    <div style="font-size:13px;font-weight:700;color:var(--text-color,#0f172a);word-break:break-word;">
                        ${esc(s.nombre)}
                    </div>
                    ${s.puede_editar ? `<button type="button" class="btn btn-default btn-xs leb-sol-rename" title="Cambiar nombre descriptivo de la solicitud">${LEB_SVGS.edit}</button>` : ""}
                </div>

                <div style="padding:8px 11px;border-radius:8px;background:var(--subtle-fg,#f8fafc);border:1px solid var(--border-color,#e2e8f0);margin-bottom:10px;">
                    ${filasDatos}
                </div>

                <div style="padding:10px 12px;border-radius:8px;background:${paletaSig.bg};border:1px solid ${paletaSig.bd};margin-bottom:12px;">
                    <div style="font-size:10.5px;font-weight:800;color:${paletaSig.tit};letter-spacing:0.02em;">${esc(sig.titulo || "ESTADO")}</div>
                    <div style="font-size:11.8px;color:${paletaSig.txt};margin-top:3px;line-height:1.35;">${esc(sig.mensaje || "")}</div>
                    ${(sig.boton || botonesSecundarios.length) ? `
                        <div class="d-flex flex-wrap mt-2" style="gap:5px;">
                            ${sig.boton ? `<button type="button" class="btn btn-xs btn-primary leb-sol-sig-btn">${esc(sig.boton)}</button>` : ""}
                            ${botonesSecundarios.join("")}
                        </div>
                    ` : ""}
                </div>

                <div class="d-flex justify-content-between align-items-center mb-1" style="gap:6px;flex-wrap:wrap;">
                    <div style="font-size:12.5px;font-weight:700;color:var(--text-color,#0f172a);">
                        Materiales solicitados <span class="text-muted" style="font-size:11px;font-weight:600;">(${(s.partidas || []).length})</span>
                    </div>
                    <div class="d-flex flex-wrap" style="gap:4px;">
                        ${s.es_motor_oscar && s.docstatus < 2 && (s.partidas || []).some((p) => !p.no_recotizar && p.estimacion_oscar && p.estimacion_oscar.semaforo === "Alta") ? `
                            <button type="button" class="btn btn-xs btn-default leb-sol-val-alta" style="border-color:#86efac;color:#047857;font-weight:600;" title="Marcar todas las partidas de Confianza Alta como Validado por Gerencia — No recotizar">Validar confianza alta</button>
                        ` : ""}
                        ${s.puede_modificar_partidas ? `
                            <button type="button" class="btn btn-xs btn-default leb-sol-part-xlsx" title="Importar partidas desde plantilla Excel (.xlsx) o CSV">Importar Excel</button>
                            <button type="button" class="btn btn-xs btn-default leb-sol-part-edit" title="Agregar, modificar o quitar artículos y partidas">+ Partidas</button>
                        ` : ""}
                    </div>
                </div>
                <div style="border:1px solid var(--border-color,#e2e8f0);border-radius:7px;overflow:hidden;margin-bottom:10px;max-height:255px;overflow-y:auto;">
                    <table class="table table-sm mb-0" style="font-size:12px;">
                        <thead style="background:var(--subtle-fg,#f8fafc);position:sticky;top:0;z-index:1;">
                            <tr>
                                <th style="padding:5px 6px;border-top:none;">Descripción / Código</th>
                                <th class="text-right" style="padding:5px 6px;border-top:none;width:88px;">Cant.</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${filasPartidas || `<tr><td colspan="2" class="text-muted text-center py-3">Sin artículos capturados.</td></tr>`}
                        </tbody>
                    </table>
                </div>

                ${s.notas ? `
                    <div style="padding:7px 10px;border-radius:6px;background:var(--subtle-fg,#f8fafc);border:1px solid var(--border-color,#e2e8f0);font-size:11.5px;color:var(--text-muted,#475569);margin-bottom:10px;">
                        <b>Notas:</b> ${esc(s.notas)}
                    </div>
                ` : ""}

                <div class="mt-2">
                    <button type="button" class="btn btn-primary btn-sm btn-block leb-sol-open">Abrir solicitud</button>
                    <div class="d-flex mt-2" style="gap:6px;">
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sol-wa" title="Copiar lista limpia de materiales para enviar por WhatsApp o correo a proveedores">Copiar WhatsApp</button>
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sol-print">Imprimir</button>
                        ${s.docstatus === 1 && s.status_raw !== "Stopped" ? `<button type="button" class="btn btn-default btn-xs flex-fill leb-sol-anexo">Crear anexo</button>` : ""}
                        <button type="button" class="btn btn-default btn-xs flex-fill leb-sol-ver-exp">Expediente</button>
                    </div>
                </div>
            </div>
        `);

        $body.find(".leb-sol-open").on("click", () => frappe.set_route("Form", "Material Request", s.folio));
        $body.find(".leb-sol-wa").on("click", () => copiarSolicitudParaWhatsapp(s.folio, s.partidas || []));
        $body.find(".leb-sol-print").on("click", () => frappe.set_route("print", "Material Request", s.folio));
        $body.find(".leb-sol-part-xlsx").on("click", () => {
            if (window.lebaref && window.lebaref.abrirCargaExcelSolicitudExistente) {
                window.lebaref.abrirCargaExcelSolicitudExistente(s.folio, () => recargarActual());
            }
        });
        $body.find(".leb-sol-part-edit").on("click", () => {
            if (window.lebaref && window.lebaref.abrirEditorPartidasSolicitud) {
                window.lebaref.abrirEditorPartidasSolicitud(s.folio, () => recargarActual());
            }
        });
        $body.find(".leb-sol-open-chart").on("click", function (ev) {
            ev.preventDefault();
            const sku = $(this).data("sku");
            if (sku && window.lebaref && window.lebaref.abrirGraficaTiempoPrecioArticulo) {
                window.lebaref.abrirGraficaTiempoPrecioArticulo(sku, () => recargarActual());
            }
        });
        $body.find(".leb-sol-toggle-val").on("click", async function () {
            const mri = $(this).data("mri");
            const val = Number($(this).data("val") || 0);
            await frappe.call({
                method: "lebaref_compras.motor_precios.marcar_validado_no_recotizar",
                type: "POST",
                args: { solicitud: s.folio, mr_item: mri, validar: val },
                freeze: true,
            });
            frappe.show_alert({
                message: val ? "Marcado como Validado por Gerencia — No recotizar" : "Se reactivó cotización para la partida",
                indicator: "green",
            });
            recargarActual();
        });
        $body.find(".leb-sol-val-alta").on("click", async () => {
            const { message: resVal } = await frappe.call({
                method: "lebaref_compras.motor_precios.validar_lote_alta_confianza_solicitud",
                type: "POST",
                args: { solicitud: s.folio },
                freeze: true,
            });
            frappe.show_alert({
                message: `${(resVal && resVal.marcadas) || 0} partida(s) de confianza alta validadas`,
                indicator: "green",
            });
            recargarActual();
        });
        $body.find(".leb-sol-ver-exp").on("click", () => {
            estado.tabActiva = "expediente";
            pintarContenidoSplitSolicitud(listview);
        });
        $body.find(".leb-sol-jump-orig").on("click", function (ev) {
            ev.preventDefault();
            const orig = $(this).data("folio");
            if (orig) seleccionarSolicitudEnSplit(listview, orig);
        });
        $body.find(".leb-sol-rename").on("click", async () => {
            const mod = await asegurarModuloSolicitud();
            if (mod && mod.abrirNombre) {
                mod.abrirNombre(s.folio, s.nombre, () => recargarActual());
            }
        });
        $body.find(".leb-sol-anexo").on("click", () => {
            abrirOperacion("anexo", s.folio, `Crear anexo vinculado a ${s.folio}`, (res) => {
                recargarActual((res && res.folio) || s.folio);
            });
        });
        $body.find(".leb-sol-act-eqv").on("click", () => {
            const libre = (s.partidas || []).find((p) => p.es_libre) || {};
            if (window.lebaref && window.lebaref.abrirModalProponerEquivalencia) {
                window.lebaref.abrirModalProponerEquivalencia({
                    texto_original: decodificarEntidadesHtml(libre.descripcion || ""),
                    origen: "Solicitud de Ingeniero",
                    proyecto: libre.obra || "",
                });
            }
        });

        const cambiarEstadoManual = async (accion, detalle = "") => {
            await frappe.call({
                method: "lebaref_compras.ux_lebaref.cambiar_estado_manual",
                type: "POST",
                args: { solicitud: s.folio, accion, detalle },
                freeze: true,
                freeze_message: "Actualizando estatus…",
            });
            const msg = { espera: "Solicitud marcada En espera", concluir: "Solicitud marcada como Concluida", reanudar: "Solicitud reanudada" }[accion];
            frappe.show_alert({ message: msg, indicator: "green" });
            recargarActual();
        };

        const ejecutarAccionGuiada = async (acc) => {
            if (acc === "reanudar") {
                cambiarEstadoManual("reanudar");
            } else if (acc === "abrir_form") {
                frappe.set_route("Form", "Material Request", s.folio);
            } else if (acc === "confirmar_borrador") {
                frappe.confirm(
                    `¿Confirmar la solicitud <b>${esc(s.folio)}</b> (${esc(s.nombre)}) con ${(s.partidas || []).length} partida(s)?`,
                    async () => {
                        await frappe.call({
                            method: "lebaref_compras.ux_lebaref.confirmar_solicitud_borrador",
                            type: "POST",
                            args: { solicitud: s.folio },
                            freeze: true,
                            freeze_message: "Confirmando solicitud…",
                        });
                        frappe.show_alert({ message: `Solicitud ${s.folio} confirmada`, indicator: "green" });
                        recargarActual();
                    }
                );
            } else if (acc === "cotizar") {
                const mod = await asegurarModuloSolicitud();
                if (mod && mod.abrirCotizacionDirecta) {
                    mod.abrirCotizacionDirecta(s.folio, () => recargarActual());
                }
            } else if (acc === "comparativa") {
                const mod = await asegurarModuloSolicitud();
                if (mod && mod.abrirComparativa) {
                    mod.abrirComparativa(s.folio, () => recargarActual());
                }
            } else if (acc === "surtir") {
                abrirOperacion("surtir", s.folio, `Surtir desde almacén · ${s.folio}`, () => recargarActual());
            } else if (acc === "ver_expediente") {
                estado.tabActiva = "expediente";
                pintarContenidoSplitSolicitud(listview);
            }
        };

        $body.find(".leb-sol-sig-btn").on("click", () => ejecutarAccionGuiada(sig.accion));
        $body.find(".leb-sol-act-comp").on("click", () => ejecutarAccionGuiada("comparativa"));
        $body.find(".leb-sol-act-espera").on("click", () => {
            frappe.prompt(
                [{
                    fieldname: "de",
                    label: "¿De quién o de qué esperas respuesta?",
                    fieldtype: "Data",
                    reqd: 1,
                    description: "Ej. Gerente / Solicitante, Proveedor AME, Dirección, Cliente",
                }],
                (vals) => cambiarEstadoManual("espera", vals.de),
                `Poner en espera · ${s.folio}`,
                "Poner en espera"
            );
        });
        $body.find(".leb-sol-act-concluir").on("click", () => {
            frappe.confirm(
                `¿Marcar <b>${esc(s.folio)}</b> como <b>concluida</b>? Dejará de contar como pendiente (puedes reabrirla después).`,
                () => cambiarEstadoManual("concluir")
            );
        });
        $body.find(".leb-sol-act-cot").on("click", () => ejecutarAccionGuiada("cotizar"));
        $body.find(".leb-sol-act-stock").on("click", () => {
            abrirOperacion("canalizar_stock", s.folio, `Canalizar a stock · ${s.folio}`, () => recargarActual());
        });
        $body.find(".leb-sol-act-conv").on("click", async () => {
            const mod = await asegurarModuloSolicitud();
            if (mod && mod.abrirConvertirConcepto) {
                mod.abrirConvertirConcepto(s, (res) => recargarActual((res && res.folio) || s.folio));
            }
        });
    }

    // --- Registro idempotente de ListView Settings (máx. 4 botones visibles, iconos SVG limpios, sin emojis ni claves técnicas) ---
    const listActions = [
        ["Purchase Receipt", "Entrada sin orden", "entrada_sin_oc", "plus"],
        ["Stock Entry", "Entregar material a obra", "entregar", "box"],
        ["Stock Reconciliation", "Nuevo conteo físico o merma", "ajuste", "check"],
        ["Supplier", "Alta rápida de proveedor", "proveedor", "plus"],
        ["Lebaref Herramienta", "Nueva herramienta", "herramienta", "plus"],
        ["Lebaref Kit", "Nuevo maletín o kit", "kit", "plus"],
        ["Lebaref Prestamo", "Nuevo vale de préstamo", "prestamo", "plus"],
        ["Lebaref Vehiculo", "Nuevo vehículo", "vehiculo", "plus"],
    ];

    const ORIGEN_IMPRESION_LISTA = {
        "Lebaref Herramienta": "herramientas",
        "Lebaref Kit": "kits",
        "Lebaref Vehiculo": "vehiculos",
    };

    function aplicarListviewSettings() {
        if (!window.frappe || !frappe.listview_settings) return;

        for (const [dt, label, accion, iconKey] of listActions) {
            const prev = frappe.listview_settings[dt] || {};
            if (prev.__lebWrapped) continue;
            const prevOnload = prev.onload;
            frappe.listview_settings[dt] = Object.assign({}, prev, {
                __lebWrapped: true,
                onload(listview) {
                    if (typeof prevOnload === "function") prevOnload(listview);
                    if (listview.__lebBtnsReady) return;
                    listview.__lebBtnsReady = true;
                    if (dt === "Supplier") {
                        if (frappe.model.can_create("Supplier")) {
                            const $btnSup = listview.page.set_primary_action("Nuevo proveedor", () =>
                                abrirModalProveedorRapido("", () => listview.refresh()),
                                "add"
                            );
                            lebBtn($btnSup, "plus", "Nuevo proveedor");
                        }
                    } else {
                        lebBtn(
                            listview.page.add_inner_button(label, () => abrirOperacion(accion, "", label, () => listview.refresh())),
                            iconKey || "plus",
                            label
                        );
                    }
                    if (dt === "Stock Entry") {
                        listview.page.add_inner_button("Sobrante con acta", () =>
                            abrirOperacion("sobrante", "", "Registrar sobrante con acta", () => listview.refresh()),
                            "Herramientas"
                        );
                    }
                    if (dt === "Supplier") {
                        lebBtn(
                            listview.page.add_inner_button("Directorio", () => abrirDirectorioProveedores()),
                            "search",
                            "Directorio"
                        );
                        listview.page.add_inner_button("Histórico de precios y facturas", () => abrirArchivoHistorico("precios"), "Herramientas");
                    } else {
                        listview.page.add_inner_button("Escanear código (F2)", () => abrirEscaneoPistola(), "Herramientas");
                    }
                    if (ORIGEN_IMPRESION_LISTA[dt]) {
                        lebBtn(
                            listview.page.add_inner_button("Imprimir etiquetas", () =>
                                abrirImpresorCodigosCarta(ORIGEN_IMPRESION_LISTA[dt])
                            ),
                            "printer",
                            "Imprimir etiquetas"
                        );
                    }
                    if (dt === "Lebaref Herramienta") {
                        listview.page.add_inner_button("Mapa de zonas A–F", () => abrirMapaUbicaciones(), "Herramientas");
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
                    lebBtn(
                        listview.page.add_inner_button("Archivo histórico", () => abrirArchivoHistorico(tabDef)),
                        "layers",
                        "Archivo histórico"
                    );
                    if (dtHist === "Purchase Order") {
                        listview.page.add_inner_button("Escanear código (F2)", () => abrirEscaneoPistola(), "Herramientas");
                    }
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
                    lebBtn(
                        listview.page.add_inner_button("Nuevo cliente", () =>
                            abrirDialogoNuevoCliente(() => listview.refresh())
                        ),
                        "plus",
                        "Nuevo cliente"
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
                    lebBtn(
                        listview.page.add_inner_button("Nueva obra o proyecto", () =>
                            abrirOperacion("obra", "", "Nueva Obra / Proyecto", () => listview.refresh())
                        ),
                        "plus",
                        "Nueva obra o proyecto"
                    );
                },
            });
        }

        // Lista de Material Request (Solicitudes): botón primario único "Nueva solicitud" (solo cabecera), sin Importar Excel afuera, y filtros exactos por estado real
        const prevMR = frappe.listview_settings["Material Request"] || {};
        if (!prevMR.__lebWrapped) {
            const prevMROnload = prevMR.onload;
            const prevMRRefresh = prevMR.refresh;
            const camposExtraMR = [...new Set([
                ...(prevMR.add_fields || []),
                "title",
                "status",
                "docstatus",
                "material_request_type",
                "schedule_date",
                "custom_lebaref_nombre_solicitud",
                "custom_lebaref_estado_vista",
                "custom_lebaref_solicitante",
                "custom_lebaref_departamento",
                "custom_lebaref_concluida",
                "custom_lebaref_en_espera_de",
            ])];

            function configurarBotonPrimarioSolicitudes(listview) {
                if (!listview || !listview.page || !frappe.model.can_create("Material Request")) return;
                const $btn = listview.page.set_primary_action("Nueva solicitud", () => {
                    abrirModalNuevaSolicitudCabecera((res) => {
                        listview.refresh();
                        if (res && res.folio) seleccionarSolicitudEnSplit(listview, res.folio);
                    });
                }, "add");
                lebBtn($btn, "plus", "Nueva solicitud");
            }

            function normalizarFiltroEstadoNativoSolicitudes(listview) {
                if (!listview || !listview.filter_area || typeof listview.filter_area.get !== "function") return;
                if (listview.$page && listview.$page.length) {
                    listview.$page.find(".leb-mr-quick-filters").remove();
                }
                const actuales = listview.filter_area.get() || [];
                const filtroStatusNativo = actuales.find((f) => f[1] === "status" && f[2] === "=" && f[3] === "Pending");
                if (filtroStatusNativo && !listview.__lebTranslatingPending) {
                    listview.__lebTranslatingPending = true;
                    Promise.resolve()
                        .then(() => listview.filter_area.remove("status"))
                        .then(() => listview.filter_area.add([["Material Request", "custom_lebaref_estado_vista", "=", "Pendiente"]]))
                        .finally(() => {
                            listview.__lebTranslatingPending = false;
                        });
                }
            }

            frappe.listview_settings["Material Request"] = Object.assign({}, prevMR, {
                __lebWrapped: true,
                add_fields: camposExtraMR,
                get_indicator(doc) {
                    if (doc.docstatus === 2 || doc.status === "Cancelled") {
                        return ["Cancelada", "red", "custom_lebaref_estado_vista,=,Cancelada"];
                    }
                    if (doc.docstatus === 0 || doc.status === "Draft") {
                        return ["Borrador", "red", "custom_lebaref_estado_vista,=,Borrador"];
                    }
                    if (Number(doc.custom_lebaref_concluida || 0) === 1 || doc.status === "Stopped") {
                        return ["Concluida", "green", "custom_lebaref_estado_vista,=,Concluida"];
                    }
                    if ((doc.custom_lebaref_en_espera_de || "").trim()) {
                        return ["En espera", "purple", "custom_lebaref_estado_vista,=,En espera"];
                    }
                    if (doc.status === "Partially Ordered") {
                        return ["Parcialmente pedida", "orange", "custom_lebaref_estado_vista,=,Parcialmente pedida"];
                    }
                    if (doc.status === "Ordered") {
                        return ["Pedida", "blue", "custom_lebaref_estado_vista,=,Pedida"];
                    }
                    if (doc.status === "Partially Received") {
                        return ["Parcialmente recibida", "blue", "custom_lebaref_estado_vista,=,Parcialmente recibida"];
                    }
                    if (["Received", "Issued", "Transferred"].includes(doc.status)) {
                        return ["Recibida", "green", "custom_lebaref_estado_vista,=,Recibida"];
                    }
                    return ["Pendiente", "orange", "custom_lebaref_estado_vista,=,Pendiente"];
                },
                onload(listview) {
                    if (typeof prevMROnload === "function") prevMROnload(listview);
                    inicializarSplitSolicitudes(listview);
                    configurarBotonPrimarioSolicitudes(listview);
                    normalizarFiltroEstadoNativoSolicitudes(listview);
                    if (listview.__lebBtnsReady) return;
                    listview.__lebBtnsReady = true;
                    lebBtn(
                        listview.page.add_inner_button("Ficha", () => {
                            const est = listview.__lebSplitSol;
                            if (!est || !est.$panel) return;
                            est.oculto = !est.oculto;
                            est.$panel.toggleClass("leb-hidden", est.oculto);
                            est.$wrapper.toggleClass("leb-split-active", !est.oculto);
                            if (est.$page) est.$page.toggleClass("leb-split-active-page", !est.oculto);
                        }),
                        "sidebar",
                        "Ficha"
                    );
                    listview.page.add_inner_button("Stock mínimo", () => abrirAlertasStockMinimo(), "Herramientas");
                    listview.page.add_inner_button("Escanear código (F2)", () => abrirEscaneoPistola(), "Herramientas");
                },
                refresh(listview) {
                    if (typeof prevMRRefresh === "function") prevMRRefresh(listview);
                    inicializarSplitSolicitudes(listview);
                    configurarBotonPrimarioSolicitudes(listview);
                    normalizarFiltroEstadoNativoSolicitudes(listview);
                    decorarSeleccionListaSolicitudes(listview);
                    const est = listview.__lebSplitSol;
                    if (!est || est.oculto) return;
                    const visibles = (listview.data || []).map((r) => r.name).filter(Boolean);
                    if (!visibles.length) {
                        est.folioActual = "";
                        est.fichaCache = null;
                        pintarContenidoSplitSolicitud(listview);
                        return;
                    }
                    if (!est.folioActual || !visibles.includes(est.folioActual)) {
                        seleccionarSolicitudEnSplit(listview, visibles[0]);
                    } else {
                        seleccionarSolicitudEnSplit(listview, est.folioActual);
                    }
                },
            });
        }

        // Lista de Item (Artículos): máx. 4 botones visibles (Ficha, Stock mínimo, Etiquetas, Herramientas)
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
                    lebBtn(
                        listview.page.add_inner_button("Ficha", () => {
                            const est = listview.__lebSplit;
                            if (!est || !est.$panel) return;
                            est.oculto = !est.oculto;
                            est.$panel.toggleClass("leb-hidden", est.oculto);
                            est.$wrapper.toggleClass("leb-split-active", !est.oculto);
                            if (est.$page) est.$page.toggleClass("leb-split-active-page", !est.oculto);
                        }),
                        "sidebar",
                        "Ficha"
                    );
                    lebBtn(
                        listview.page.add_inner_button("Stock mínimo", () => abrirAlertasStockMinimo()),
                        "alert",
                        "Stock mínimo"
                    );
                    lebBtn(
                        listview.page.add_inner_button("Etiquetas", () => abrirImpresorCodigosCarta("articulos")),
                        "printer",
                        "Etiquetas"
                    );
                    if (window.lebaref && window.lebaref.esUsuarioMotorOscar && window.lebaref.esUsuarioMotorOscar()) {
                        listview.page.add_inner_button("Motor de precios y confianza", () => window.lebaref.abrirMotorPreciosConfianza("catalogo"), "Herramientas");
                    }
                    listview.page.add_inner_button("Bandeja de equivalencias y alias", () => abrirBandejaEquivalencias(), "Herramientas");
                    listview.page.add_inner_button("Importar cotizaciones históricas", () => abrirImportadorCotizacionesHistoricas(() => listview.refresh()), "Herramientas");
                    listview.page.add_inner_button("Escanear código (F2)", () => abrirEscaneoPistola(), "Herramientas");
                    listview.page.add_inner_button("Mapa de zonas A–F", () => abrirMapaUbicaciones(), "Herramientas");
                    listview.page.add_inner_button("Histórico de precios", () => abrirArchivoHistorico("precios"), "Herramientas");
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

        const prevBin = frappe.listview_settings["Bin"] || {};
        if (!prevBin.__lebWrapped) {
            const prevBinOnload = prevBin.onload;
            frappe.listview_settings["Bin"] = Object.assign({}, prevBin, {
                __lebWrapped: true,
                onload(listview) {
                    if (typeof prevBinOnload === "function") prevBinOnload(listview);
                    if (listview.__lebBtnsReady) return;
                    listview.__lebBtnsReady = true;
                    lebBtn(
                        listview.page.add_inner_button("Stock mínimo", () => abrirAlertasStockMinimo()),
                        "alert",
                        "Stock mínimo"
                    );
                    lebBtn(
                        listview.page.add_inner_button("Mapa de zonas A–F", () => abrirMapaUbicaciones()),
                        "layers",
                        "Mapa de zonas A–F"
                    );
                },
            });
        }
    }

    window.__lebarefAplicarListviewSettings = aplicarListviewSettings;
    aplicarListviewSettings();
    setTimeout(pintarFranjaPendientesWorkspace, 450);

    if (window.frappe && frappe.router && typeof frappe.router.on === "function") {
        frappe.router.on("change", () => {
            aplicarListviewSettings();
            setTimeout(() => {
                pintarFranjaPendientesWorkspace();
                const route = frappe.get_route ? frappe.get_route() : [];
                if (route[0] === "List" && route[1] === "Item" && window.cur_list && window.cur_list.doctype === "Item") {
                    inicializarSplitCatalogoItem(window.cur_list);
                    decorarMiniaturasYSeleccionLista(window.cur_list);
                    const est = window.cur_list.__lebSplit;
                    const visibles = (window.cur_list.data || []).map((r) => r.name).filter(Boolean);
                    if (est && !est.oculto && visibles.length && (!est.itemActual || !visibles.includes(est.itemActual))) {
                        seleccionarItemEnSplit(window.cur_list, visibles[0]);
                    }
                } else if (route[0] === "List" && route[1] === "Material Request" && window.cur_list && window.cur_list.doctype === "Material Request") {
                    inicializarSplitSolicitudes(window.cur_list);
                    decorarSeleccionListaSolicitudes(window.cur_list);
                    const estSol = window.cur_list.__lebSplitSol;
                    const visiblesSol = (window.cur_list.data || []).map((r) => r.name).filter(Boolean);
                    if (estSol && !estSol.oculto && visiblesSol.length && (!estSol.folioActual || !visiblesSol.includes(estSol.folioActual))) {
                        seleccionarSolicitudEnSplit(window.cur_list, visiblesSol[0]);
                    }
                }
            }, 150);
        });
    }
    // =========================================================================
    // FLUJO UNIFICADO:
    // 1. Solicitudes: Crear solo cabecera + Cargar Excel (.xlsx/.csv/Pegado) + Editor de Partidas
    // 2. Facturas de Compra: Asistente XML CFDI 4.0 (con Actualizar existencias, multi-OC y sinónimos Item Supplier)
    // 3. Cotizaciones: Carga masiva de partidas/precios desde Excel (.xlsx/.csv/Pegado)
    // 4. Barra lateral: Forzar secciones cerradas al iniciar sesión / recargar el ERP
    // =========================================================================

    const WORKSPACES_NATIVOS_OCULTOS = new Set([
        "buying",
        "compras",
        "stock",
        "inventario",
        "accounting",
        "accounts",
        "contabilidad",
        "projects",
        "proyectos",
        "selling",
        "manufacturing",
        "assets",
        "quality",
        "crm",
        "support",
        "home",
        "build",
        "users",
        "erpnext settings",
        "erpnext integrations",
    ]);

    function inyectarEstilosSinDock() {
        if (document.getElementById("leb-no-dock-style")) return;
        const st = document.createElement("style");
        st.id = "leb-no-dock-style";
        st.textContent = `
            #desk-dock, .dock { display: none !important; width: 0 !important; pointer-events: none !important; }
            body.dock-pinned, body.dock-active { --dock-width: 0px !important; }
            [data-name="workspace-selector"] { display: none !important; }
            .indicator-pill.purple, .indicator.purple {
                background-color: #ede9fe !important;
                color: #6d28d9 !important;
            }
            .indicator-pill.purple::before, .indicator.purple::before {
                background: #7c3aed !important;
            }
        `;
        document.head.appendChild(st);
    }

    function redirigirSiWorkspaceGenerico() {
        try {
            if (!window.frappe || !frappe.get_route || !frappe.set_route) return;
            const route = frappe.get_route() || [];
            if (!route.length) {
                const p = (window.location.pathname || "").replace(/\/+$/, "");
                if (p === "/app" || p === "/desk") {
                    frappe.set_route("Workspaces", "Lebaref");
                }
                return;
            }
            if (route[0] === "Workspaces") {
                const ws = String(route[route.length - 1] || "").trim().toLowerCase();
                if (ws && ws !== "lebaref" && WORKSPACES_NATIVOS_OCULTOS.has(ws)) {
                    frappe.set_route("Workspaces", "Lebaref");
                }
            }
        } catch (e) {}
    }

    function configurarBarraUnicaLebaref() {
        inyectarEstilosSinDock();
        if (window.frappe && frappe.boot) {
            if (frappe.boot.module_sidebars && frappe.boot.module_sidebars["Lebaref Compras"]) {
                frappe.boot.home_shell = "Lebaref Compras";
            }
        }
        if (window.frappe && frappe.ui && frappe.ui.Sidebar && !frappe.ui.Sidebar.prototype.__lebSingleSidebarPatched) {
            const proto = frappe.ui.Sidebar.prototype;
            proto.__lebSingleSidebarPatched = true;
            proto.dock_enabled = function () {
                return false;
            };
            const origDefaultShell = proto.default_shell;
            proto.default_shell = function () {
                if (frappe.boot && frappe.boot.module_sidebars && frappe.boot.module_sidebars["Lebaref Compras"]) {
                    return "Lebaref Compras";
                }
                return typeof origDefaultShell === "function" ? origDefaultShell.call(this) : null;
            };
            const origShellForCurrent = proto.shell_for_current_route;
            proto.shell_for_current_route = function (route) {
                if (frappe.boot && frappe.boot.module_sidebars && frappe.boot.module_sidebars["Lebaref Compras"]) {
                    return "Lebaref Compras";
                }
                return typeof origShellForCurrent === "function" ? origShellForCurrent.call(this, route) : null;
            };
        }
        const sb = window.frappe && frappe.app && frappe.app.sidebar;
        if (sb) {
            try {
                if (frappe.boot && frappe.boot.module_sidebars && frappe.boot.module_sidebars["Lebaref Compras"] && sb.current_module !== "Lebaref Compras") {
                    if (typeof sb.select_shell === "function") {
                        sb.select_shell("Lebaref Compras");
                    } else if (typeof sb.setup === "function") {
                        sb.setup("Lebaref Compras");
                    }
                }
                if (typeof sb.refresh_dock === "function") sb.refresh_dock();
                if (typeof sb.apply_expanded_state === "function") sb.apply_expanded_state();
            } catch (e) {}
        }
        redirigirSiWorkspaceGenerico();
    }

    function forzarSidebarCerradoAlIniciar() {
        configurarBarraUnicaLebaref();
        if (window.__lebSidebarInitialCollapseApplied) return;
        const sb = frappe && frappe.app && frappe.app.sidebar;
        if (!sb || !Array.isArray(sb.items) || !sb.items.length) return;
        if (sb.current_module !== "Lebaref Compras") return;
        window.__lebSidebarInitialCollapseApplied = true;
        try {
            for (const it of sb.items) {
                if (it && it.item && it.item.type === "Section Break" && typeof it.close === "function") {
                    it.close();
                }
            }
        } catch (e) {
            // Ignorar si algún componente del sidebar aún no monta su DOM
        }
    }
    configurarBarraUnicaLebaref();
    setTimeout(forzarSidebarCerradoAlIniciar, 180);
    setTimeout(forzarSidebarCerradoAlIniciar, 600);
    setTimeout(forzarSidebarCerradoAlIniciar, 1400);
    if (window.frappe && frappe.router && typeof frappe.router.on === "function") {
        frappe.router.on("change", () => {
            configurarBarraUnicaLebaref();
            setTimeout(redirigirSiWorkspaceGenerico, 60);
        });
    }

    async function abrirModalNuevaSolicitudCabecera(alTerminar = null) {
        const [{ message: depts }, { message: obras }] = await Promise.all([
            frappe.call({ method: "lebaref_compras.operaciones.esquema", type: "GET", args: { accion: "solicitud" } }).catch(() => ({ message: {} })),
            frappe.db.get_list("Project", { fields: ["name"], limit: 500 }).then((r) => ({ message: (r || []).map((x) => x.name) })).catch(() => ({ message: [] })),
        ]);
        const campoDept = ((depts && depts.fields) || []).find((f) => f.name === "departamento");
        const opcionesDept = (campoDept && campoDept.choices) || [];

        const d = new frappe.ui.Dialog({
            title: "Nueva Solicitud",
            fields: [
                {
                    fieldname: "_info",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        Registra únicamente los <b>datos principales</b> de la solicitud en Borrador (sin artículos). Desde el detalle de la solicitud podrás agregarlos manualmente o usar <b>Importar Excel</b>.
                    </div>`,
                },
                {
                    fieldname: "titulo",
                    label: "Nombre / Referencia de la Solicitud",
                    fieldtype: "Data",
                    reqd: 1,
                },
                {
                    fieldname: "departamento",
                    label: "Departamento / Área",
                    fieldtype: opcionesDept.length ? "Select" : "Data",
                    options: opcionesDept.length ? ["", ...opcionesDept].join("\n") : undefined,
                },
                {
                    fieldname: "solicitante",
                    label: "Solicitante",
                    fieldtype: "Data",
                    default: (frappe.session && frappe.session.user_fullname) || "",
                },
                { fieldtype: "Column Break" },
                {
                    fieldname: "schedule_date",
                    label: "Requerido para",
                    fieldtype: "Date",
                    default: frappe.datetime.add_days(frappe.datetime.get_today(), 7),
                    reqd: 1,
                },
                {
                    fieldname: "obra_default",
                    label: "Obra / Proyecto (opcional)",
                    fieldtype: (obras || []).length ? "Select" : "Data",
                    options: (obras || []).length ? ["", ...obras].join("\n") : undefined,
                },
                {
                    fieldname: "notas",
                    label: "Notas / Justificación (opcional)",
                    fieldtype: "Small Text",
                },
            ],
            primary_action_label: "Registrar solicitud",
            async primary_action(vals) {
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.ux_lebaref.crear_solicitud_solo_cabecera",
                    type: "POST",
                    args: { datos: JSON.stringify(vals) },
                    freeze: true,
                    freeze_message: "Registrando solicitud…",
                });
                d.hide();
                frappe.show_alert({ message: `Solicitud ${res.folio} registrada. Agrega o importa sus artículos desde el detalle.`, indicator: "green" });
                if (typeof alTerminar === "function") {
                    alTerminar(res);
                } else if (res && res.folio) {
                    frappe.set_route("Form", "Material Request", res.folio);
                }
            },
        });
        d.show();
    }

    function abrirCargaExcelSolicitudExistente(folio, alTerminar = null, obraInicial = "") {
        let archivoNombre = "";
        let archivoBase64 = "";

        const d = new frappe.ui.Dialog({
            title: `📥 Cargar Partidas desde Excel / CSV · ${folio}`,
            size: "extra-large",
            fields: [
                {
                    fieldname: "_bar",
                    fieldtype: "HTML",
                    options: `<div class="d-flex justify-content-between align-items-center p-2 mb-2" style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;font-size:12px;gap:8px;flex-wrap:wrap;">
                        <span>Sube un archivo <b>Excel (.xlsx)</b> o <b>.CSV</b>, o pega directo desde Excel con columnas: <code>Código_o_Concepto | Cantidad | Unidad | Obra | Partida</code>.</span>
                        <div class="d-flex" style="gap:6px;">
                            <button type="button" class="btn btn-xs btn-default leb-sol-dl-tpl">📄 Descargar Plantilla CSV</button>
                            <button type="button" class="btn btn-xs btn-primary leb-sol-pick-file">📁 Subir Excel (.xlsx) / CSV</button>
                            <input type="file" accept=".xlsx,.csv,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" class="d-none leb-sol-file-in">
                        </div>
                    </div>
                    <div class="small text-success font-weight-bold mb-2 d-none leb-sol-file-lbl"></div>`,
                },
                {
                    fieldname: "modo",
                    label: "¿Cómo aplicar las partidas?",
                    fieldtype: "Select",
                    options: "agregar\nreemplazar",
                    default: "agregar",
                    description: "'agregar' conserva lo que ya tiene la solicitud; 'reemplazar' sustituye todas las partidas.",
                },
                { fieldtype: "Column Break" },
                {
                    fieldname: "obra_default",
                    label: "Obra por defecto (si el Excel no indica columna Obra)",
                    fieldtype: "Link",
                    options: "Project",
                    default: obraInicial || "",
                },
                { fieldtype: "Section Break" },
                {
                    fieldname: "texto_csv",
                    label: "O pega aquí directamente las celdas copiadas desde Excel",
                    fieldtype: "Small Text",
                },
                {
                    fieldname: "_preview",
                    fieldtype: "HTML",
                },
            ],
            primary_action_label: "Guardar Partidas en " + folio,
            async primary_action(vals) {
                const txt = (vals.texto_csv || "").trim();
                if (!txt && !archivoBase64) {
                    frappe.msgprint("Sube un archivo Excel (.xlsx) / CSV o pega las filas desde Excel.");
                    return;
                }
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.ux_lebaref.guardar_partidas_solicitud",
                    type: "POST",
                    args: {
                        solicitud: folio,
                        datos: JSON.stringify({
                            modo: vals.modo || "agregar",
                            obra_default: vals.obra_default || "",
                            texto_csv: txt,
                            filename: archivoNombre,
                            content_base64: archivoBase64,
                        }),
                    },
                    freeze: true,
                    freeze_message: "Guardando partidas en la solicitud…",
                });
                d.hide();
                frappe.show_alert({
                    message: `${res.agregadas} partida(s) guardadas en ${res.folio} (Total: ${res.total_partidas}).`,
                    indicator: "green",
                });
                if (typeof alTerminar === "function") alTerminar(res);
            },
        });

        d.add_custom_action("🔍 Previsualizar", async () => {
            const txt = (d.get_value("texto_csv") || "").trim();
            if (!txt && !archivoBase64) {
                frappe.msgprint("Selecciona un archivo o pega filas antes de previsualizar.");
                return;
            }
            const { message: prev } = await frappe.call({
                method: "lebaref_compras.analitica_operativa.previsualizar_carga_masiva_solicitud",
                type: "POST",
                args: {
                    texto_csv: txt,
                    filename: archivoNombre,
                    content_base64: archivoBase64,
                },
                freeze: true,
            });
            const esc = frappe.utils.escape_html;
            const alertBox = (prev.errores || []).length
                ? `<div class="alert alert-danger py-2 px-3 mb-2" style="font-size:12px;"><b>Errores detectados:</b><br>• ${prev.errores.map(esc).join("<br>• ")}</div>`
                : `<div class="alert alert-success py-2 px-3 mb-2" style="font-size:12px;">✅ Listas para guardar: <b>${prev.total_skus} SKU(s)</b> y <b>${prev.total_libres} concepto(s) libre(s)</b>.</div>`;
            const bloquesPrevXlsx = agruparFilasPorPartida(prev.filas_validas || [], (f) => f.partida);
            const trs = (prev.filas_validas || []).length
                ? bloquesPrevXlsx.map((g) => {
                    const sep = g.partida
                        ? `<tr style="background:#e2e8f0;color:#031e34;font-weight:800;"><td colspan="5" style="padding:5px 8px;">${esc(g.partida)} (${g.items.length})</td></tr>`
                        : "";
                    const rows = g.items.map((f) => `
                        <tr>
                            <td>#${f.fila}</td>
                            <td><span class="badge ${f.es_sku ? "badge-success" : "badge-warning"}">${esc(f.coincidencia)}</span></td>
                            <td><b>${esc(f.item_code || "CONCEPTO LIBRE")}</b> · ${esc(f.descripcion)}</td>
                            <td class="text-right"><b>${f.qty} ${esc(f.uom)}</b></td>
                            <td>${esc(f.obra || "—")}</td>
                        </tr>
                    `).join("");
                    return sep + rows;
                }).join("")
                : "";
            d.fields_dict._preview.$wrapper.html(`
                ${alertBox}
                <div class="table-responsive" style="max-height:220px;overflow:auto;">
                    <table class="table table-bordered table-sm mb-0" style="font-size:12px;">
                        <thead class="thead-light"><tr><th>#</th><th>Origen</th><th>Artículo / Concepto</th><th class="text-right">Cant.</th><th>Obra</th></tr></thead>
                        <tbody>${trs || "<tr><td colspan='5' class='text-muted'>Sin filas válidas.</td></tr>"}</tbody>
                    </table>
                </div>
            `);
        });

        const barWrap = d.fields_dict._bar.$wrapper;
        barWrap.find(".leb-sol-dl-tpl").on("click", () => {
            const csvTpl = "\uFEFFCodigo_o_Concepto,Cantidad,Unidad,Obra,Partida\nPRD-0001,5,Nos,,HVAC\nTuberia PVC hidraulico 1 1/4,6,Nos,,Instalacion Hidraulica\n";
            const blob = new Blob([csvTpl], { type: "text/csv;charset=utf-8;" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = `Plantilla_Partidas_${folio}.csv`;
            a.click();
            URL.revokeObjectURL(url);
        });
        barWrap.find(".leb-sol-pick-file").on("click", () => barWrap.find(".leb-sol-file-in").trigger("click"));
        barWrap.find(".leb-sol-file-in").on("change", function () {
            const f = this.files && this.files[0];
            if (!f) return;
            archivoNombre = f.name;
            const reader = new FileReader();
            reader.onload = (ev) => {
                archivoBase64 = String(ev.target.result || "");
                barWrap.find(".leb-sol-file-lbl").removeClass("d-none").text(`📎 Archivo listo: ${archivoNombre}`);
            };
            reader.readAsDataURL(f);
        });

        d.show();
    }

    async function abrirEditorPartidasSolicitud(folio, alTerminar = null) {
        const { message: info } = await frappe.call({
            method: "lebaref_compras.ux_lebaref.estado_edicion_solicitud",
            type: "GET",
            args: { solicitud: folio },
            freeze: true,
        });
        if (!info || !info.puede_modificar) {
            frappe.msgprint((info && info.motivo_bloqueo) || "Esta solicitud ya no permite modificar sus partidas.");
            return;
        }

        const filas = (info.partidas || []).map((r) => Object.assign({}, r));
        if (!filas.length) {
            filas.push({ item_code: "", concepto_libre: "", qty: 1, uom: "Pieza", project: "", partida: "" });
        }

        const d = new frappe.ui.Dialog({
            title: `✏️ Modificar / Agregar Artículos y Partidas · ${folio}`,
            size: "extra-large",
            fields: [
                {
                    fieldname: "_nota",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        Edita cantidades, unidades, obras, <b>partidas</b> o agrega renglones nuevos (puedes elegir un <b>SKU de catálogo</b> o dejar SKU vacío y escribir un <b>Concepto libre</b>).
                    </div>`,
                },
                { fieldname: "_tabla", fieldtype: "HTML" },
            ],
            primary_action_label: "Guardar Cambios en " + folio,
            async primary_action() {
                const validas = filas.filter((r) => (r.item_code || "").trim() || (r.concepto_libre || "").trim());
                if (!validas.length) {
                    frappe.msgprint("Conserva o captura al menos una partida.");
                    return;
                }
                const filaInvalida = validas.findIndex((r) => !(Number(r.qty) > 0));
                if (filaInvalida >= 0) {
                    frappe.msgprint(`Revisa el renglón #${filaInvalida + 1}: la cantidad debe ser mayor que cero.`);
                    return;
                }
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.ux_lebaref.guardar_partidas_solicitud",
                    type: "POST",
                    args: {
                        solicitud: folio,
                        datos: JSON.stringify({ modo: "reemplazar", rows: validas }),
                    },
                    freeze: true,
                    freeze_message: "Actualizando partidas…",
                });
                d.hide();
                frappe.show_alert({ message: `Solicitud ${folio} actualizada (${res.total_partidas} partidas).`, indicator: "green" });
                if (typeof alTerminar === "function") alTerminar(res);
            },
        });

        const host = d.fields_dict._tabla.$wrapper;
        const skusDatalistId = "leb-skus-dl-" + Date.now();
        const uomsOpts = (info.uoms && info.uoms.length ? info.uoms : ["Pieza", "Pza", "Metro", "Rollo", "Tramo", "Lote"]);
        const obrasOpts = ["", ...(info.obras || [])];

        function pintarTabla() {
            host.empty();
            const esc = frappe.utils.escape_html;
            const dlHtml = `<datalist id="${skusDatalistId}">${(info.skus || []).map((s) => `<option value="${esc(s.name)}">${esc(s.item_name)}</option>`).join("")}</datalist>`;
            host.append(dlHtml);

            const wrap = $('<div class="table-responsive">').appendTo(host);
            const tbl = $('<table class="table table-bordered table-sm mb-2" style="font-size:12px;">').appendTo(wrap);
            $('<thead style="background:#031e34;color:#fff;"><tr><th style="width:32px;">#</th><th style="width:145px;">SKU Catálogo (opc.)</th><th>Descripción / Concepto Libre</th><th style="width:88px;">Cantidad</th><th style="width:95px;">Unidad</th><th style="width:145px;">Obra</th><th style="width:135px;">Partida</th><th style="width:38px;"></th></tr></thead>').appendTo(tbl);
            const tbody = $("<tbody>").appendTo(tbl);

            filas.forEach((r, idx) => {
                const tr = $("<tr>").appendTo(tbody);
                $("<td>").css("verticalAlign", "middle").text(idx + 1).appendTo(tr);

                const $sku = $(`<input type="text" list="${skusDatalistId}" class="form-control form-control-sm" placeholder="Vacío = Libre">`)
                    .val(r.item_code || "")
                    .on("change input", function () {
                        r.item_code = $(this).val().trim();
                        const matchSku = (info.skus || []).find((s) => s.name === r.item_code);
                        if (matchSku) {
                            r.concepto_libre = matchSku.item_name;
                            $conc.val(matchSku.item_name);
                            if (matchSku.purchase_uom || matchSku.stock_uom) {
                                r.uom = matchSku.purchase_uom || matchSku.stock_uom;
                                $uom.val(r.uom);
                            }
                        }
                    });
                $("<td>").append($sku).appendTo(tr);

                const $conc = $('<input type="text" class="form-control form-control-sm" placeholder="Descripción del material o concepto">')
                    .val(r.concepto_libre || "")
                    .on("input", function () {
                        r.concepto_libre = $(this).val();
                    });
                $("<td>").append($conc).appendTo(tr);

                const $qty = $('<input type="number" step="0.01" min="0.01" class="form-control form-control-sm text-right">')
                    .val(r.qty !== undefined && r.qty !== null ? r.qty : 1)
                    .on("input", function () {
                        const qv = parseFloat($(this).val());
                        r.qty = Number.isFinite(qv) ? qv : 0;
                        $(this).css("border-color", r.qty > 0 ? "" : "#dc2626");
                    });
                $("<td>").append($qty).appendTo(tr);

                const $uom = $('<select class="form-control form-control-sm">');
                const setUom = new Set([...uomsOpts, r.uom || "Pieza"]);
                for (const u of setUom) {
                    $("<option>").val(u).text(u).prop("selected", u === (r.uom || "Pieza")).appendTo($uom);
                }
                $uom.on("change", function () {
                    r.uom = $(this).val();
                });
                $("<td>").append($uom).appendTo(tr);

                const $obra = $('<select class="form-control form-control-sm">');
                for (const ob of new Set([...obrasOpts, r.project || ""])) {
                    $("<option>").val(ob).text(ob || "— Sin obra —").prop("selected", ob === (r.project || "")).appendTo($obra);
                }
                $obra.on("change", function () {
                    r.project = $(this).val();
                });
                $("<td>").append($obra).appendTo(tr);

                const $part = $('<input type="text" class="form-control form-control-sm" placeholder="Ej. HVAC / Tubería">')
                    .val(r.partida || "")
                    .on("input", function () {
                        r.partida = $(this).val();
                    });
                $("<td>").append($part).appendTo(tr);

                const $del = $('<button type="button" class="btn btn-xs btn-danger" title="Quitar fila">✕</button>').on("click", () => {
                    filas.splice(idx, 1);
                    if (!filas.length) filas.push({ item_code: "", concepto_libre: "", qty: 1, uom: "Pieza", project: "", partida: "" });
                    pintarTabla();
                });
                $('<td class="text-center">').css("verticalAlign", "middle").append($del).appendTo(tr);
            });

            const $btnAdd = $('<button type="button" class="btn btn-xs btn-default">➕ Agregar partida</button>').on("click", () => {
                const ult = filas[filas.length - 1] || {};
                filas.push({ item_code: "", concepto_libre: "", qty: 1, uom: "Pieza", project: ult.project || "", partida: ult.partida || "" });
                pintarTabla();
            });
            host.append($btnAdd);
        }

        pintarTabla();
        d.show();
    }

    function abrirAsistenteFacturaXml(opts = {}) {
        const poFolio = opts.purchaseOrder || "";
        const frmTarget = opts.frm || null;

        const input = document.createElement("input");
        input.type = "file";
        input.accept = ".xml,text/xml,application/xml";
        input.onchange = () => {
            const file = input.files && input.files[0];
            if (!file) return;
            const reader = new FileReader();
            reader.onload = async (ev) => {
                const xmlStr = String(ev.target.result || "");
                const { message: parsed } = await frappe.call({
                    method: "lebaref_compras.operaciones.cargar_xml_factura",
                    type: "POST",
                    args: {
                        xml: xmlStr,
                        purchase_invoice: frmTarget && !frmTarget.is_new() ? frmTarget.doc.name : null,
                        purchase_order: poFolio || null,
                    },
                    freeze: true,
                    freeze_message: "Analizando CFDI 4.0 y conciliando catálogo…",
                });
                if (!parsed) return;

                if (parsed.factura_existente && (!frmTarget || frmTarget.doc.name !== parsed.factura_existente)) {
                    frappe.msgprint({
                        title: "UUID ya registrado",
                        indicator: "orange",
                        message: `Este CFDI (<b>${frappe.utils.escape_html(parsed.uuid)}</b>) ya existe en la factura <a href="/app/purchase-invoice/${encodeURIComponent(parsed.factura_existente)}"><b>${frappe.utils.escape_html(parsed.factura_existente)}</b></a>.`,
                    });
                    return;
                }

                const mapeoElegido = {};
                for (const it of parsed.items || []) {
                    mapeoElegido[String(it.idx_xml)] = it.es_libre ? "" : it.item_code;
                }

                const esc = frappe.utils.escape_html;
                const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                const dlXmlId = "leb-xml-skus-" + Date.now();

                const d = new frappe.ui.Dialog({
                    title: `🧾 Registrar Factura de Compra desde XML CFDI 4.0`,
                    size: "extra-large",
                    fields: [
                        {
                            fieldname: "_resumen",
                            fieldtype: "HTML",
                        },
                        {
                            fieldname: "update_stock",
                            label: "Actualizar existencias en almacén al confirmar esta factura (sin requerir Recibo de Compra)",
                            fieldtype: "Check",
                            default: parsed.update_stock,
                        },
                        {
                            fieldname: "_conceptos",
                            fieldtype: "HTML",
                        },
                    ],
                    primary_action_label: frmTarget && !frmTarget.is_new() ? "Aplicar XML a esta Factura" : "Crear Factura de Compra (con XML)",
                    async primary_action(vals) {
                        if (frmTarget) {
                            frmTarget.set_value("update_stock", vals.update_stock ? 1 : 0);
                            if (parsed.supplier) frmTarget.set_value("supplier", parsed.supplier);
                            if (parsed.uuid) frmTarget.set_value("bill_no", parsed.uuid);
                            if (parsed.fecha) frmTarget.set_value("bill_date", parsed.fecha);
                            if (parsed.warehouse && vals.update_stock) frmTarget.set_value("set_warehouse", parsed.warehouse);
                            if (parsed.taxes_and_charges) frmTarget.set_value("taxes_and_charges", parsed.taxes_and_charges);
                            frmTarget.clear_table("items");
                            for (const it of parsed.items || []) {
                                const skuSel = (mapeoElegido[String(it.idx_xml)] || "").trim() || "LEBAREF-CONCEPTO-LIBRE";
                                const row = frmTarget.add_child("items");
                                row.item_code = skuSel;
                                row.item_name = it.item_name;
                                row.description = it.description;
                                row.custom_lebaref_concepto = skuSel === "LEBAREF-CONCEPTO-LIBRE" ? it.description : "";
                                row.qty = it.qty;
                                row.uom = it.uom;
                                row.stock_uom = it.stock_uom;
                                row.conversion_factor = it.conversion_factor || 1;
                                row.rate = it.rate;
                                if (it.warehouse && vals.update_stock) row.warehouse = it.warehouse;
                                if (it.expense_account) row.expense_account = it.expense_account;
                                if (it.purchase_order && it.po_detail) {
                                    row.purchase_order = it.purchase_order;
                                    row.po_detail = it.po_detail;
                                }
                                if (it.project) row.project = it.project;
                                if (it.custom_lebaref_partida) row.custom_lebaref_partida = it.custom_lebaref_partida;
                            }
                            frmTarget.refresh_fields();
                            d.hide();
                            frappe.show_alert({ message: `XML ${parsed.uuid} aplicado. Revisa y guarda la factura.`, indicator: "green" });
                            return;
                        }

                        const { message: creado } = await frappe.call({
                            method: "lebaref_compras.operaciones.crear_factura_desde_xml",
                            type: "POST",
                            args: {
                                xml: xmlStr,
                                purchase_order: poFolio || "",
                                opciones: JSON.stringify({
                                    update_stock: vals.update_stock ? 1 : 0,
                                    mapeo: mapeoElegido,
                                }),
                            },
                            freeze: true,
                            freeze_message: "Creando Factura de Compra, aprendiendo sinónimos y adjuntando XML…",
                        });
                        d.hide();
                        frappe.show_alert({
                            message: `Factura ${creado.folio} creada con su XML adjunto.`,
                            indicator: "green",
                        });
                        frappe.set_route("Form", "Purchase Invoice", creado.folio);
                    },
                });

                d.fields_dict._resumen.$wrapper.html(`
                    <div class="p-2 mb-2" style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:7px;font-size:12px;">
                        <div class="d-flex justify-content-between flex-wrap" style="gap:8px;">
                            <div><b>UUID:</b> <code>${esc(parsed.uuid)}</code> ${parsed.serie_folio ? `· <b>Folio:</b> ${esc(parsed.serie_folio)}` : ""}</div>
                            <div><b>Fecha:</b> ${esc(parsed.fecha)} · <b>Total XML:</b> <b style="color:#059669;">$${money.format(parsed.total || 0)} ${esc(parsed.moneda)}</b></div>
                        </div>
                        <div class="mt-1">
                            <b>Emisor XML:</b> ${esc(parsed.issuer_name)} (${esc(parsed.issuer_rfc)})
                            &nbsp;→&nbsp; <b>Proveedor ERP:</b> <span class="badge badge-info">${esc(parsed.supplier || "Auto-alta")}</span>
                        </div>
                        <div class="text-muted small mt-1">
                            💡 Si asocias un concepto del XML a un <b>SKU de tu catálogo</b>, el sistema guarda automáticamente el nombre/clave comercial del proveedor en <i>Item Supplier</i> para unificar precios históricos en futuras compras.
                        </div>
                    </div>
                `);

                const dlHtml = `<datalist id="${dlXmlId}">${(parsed.catalogo_skus || []).map((s) => `<option value="${esc(s.name)}">${esc(s.item_name)}</option>`).join("")}</datalist>`;
                const cWrap = d.fields_dict._conceptos.$wrapper;
                cWrap.html(dlHtml);
                const tbl = $('<table class="table table-bordered table-sm mb-0" style="font-size:12px;">').appendTo(
                    $('<div class="table-responsive" style="max-height:290px;overflow:auto;">').appendTo(cWrap)
                );
                $('<thead style="background:#031e34;color:#fff;"><tr><th style="width:32px;">#</th><th>Descripción Comercial en XML</th><th class="text-right" style="width:85px;">Cant.</th><th class="text-right" style="width:105px;">P. Unit.</th><th style="width:155px;">Vínculo OC / Solicitud</th><th style="width:210px;">Artículo Catálogo (SKU)</th></tr></thead>').appendTo(tbl);
                const tbody = $("<tbody>").appendTo(tbl);

                for (const it of parsed.items || []) {
                    const tr = $("<tr>").appendTo(tbody);
                    $("<td>").text(it.idx_xml).appendTo(tr);
                    $("<td>").html(`<b>${esc(it.description)}</b>${it.supplier_part_no && it.supplier_part_no !== it.description ? `<br><small class="text-muted">Clave prov: ${esc(it.supplier_part_no)}</small>` : ""}`).appendTo(tr);
                    $('<td class="text-right">').text(`${it.qty} ${it.uom}`).appendTo(tr);
                    $('<td class="text-right">').html(`<b>$${money.format(it.rate || 0)}</b>`).appendTo(tr);
                    const vincTxt = it.purchase_order
                        ? `<span class="badge badge-success">${esc(it.purchase_order)}</span>${it.material_request ? `<br><small>${esc(it.material_request)}</small>` : ""}`
                        : `<span class="text-muted small">Directo</span>`;
                    $("<td>").html(vincTxt).appendTo(tr);

                    const $skuIn = $(`<input type="text" list="${dlXmlId}" class="form-control form-control-sm" placeholder="Vacío = Concepto libre">`)
                        .val(mapeoElegido[String(it.idx_xml)] || "")
                        .on("change input", function () {
                            mapeoElegido[String(it.idx_xml)] = $(this).val().trim();
                        });
                    const tdSku = $("<td>").append($skuIn).appendTo(tr);
                    $(`<div class="small text-muted mt-1">${esc(it.origen_mapeo || "")}</div>`).appendTo(tdSku);
                }

                d.show();
            };
            reader.readAsText(file, "UTF-8");
        };
        input.click();
    }

    async function abrirModalExpedienteFiscalProveedor(proveedor, alGuardar = null) {
        const { message: ef } = await frappe.call({
            method: "lebaref_compras.flujo_operativo.estado_fiscal_proveedor",
            type: "GET",
            args: { proveedor },
            freeze: true,
        });
        if (!ef) return;
        const esc = frappe.utils.escape_html;
        let csfName = "", csfB64 = "", carName = "", carB64 = "";

        const d = new frappe.ui.Dialog({
            title: `Expediente Fiscal y Bancario · ${ef.nombre}`,
            fields: [
                {
                    fieldname: "_estado",
                    fieldtype: "HTML",
                    options: `<div class="alert ${ef.expediente_completo ? "alert-success" : "alert-warning"} py-2 px-3 mb-2" style="font-size:12px;">
                        ${ef.expediente_completo
                            ? "Constancia de Situación Fiscal (CSF) y Carátula Bancaria registradas."
                            : `Pendiente de subir: <b>${esc((ef.faltantes || []).join(" y "))}</b>`}
                        <div class="mt-1 small">
                            ${ef.csf_archivo ? `<a href="${esc(ef.csf_archivo)}" target="_blank"><b>Ver CSF actual</b></a> · ` : ""}
                            ${ef.caratula_archivo ? `<a href="${esc(ef.caratula_archivo)}" target="_blank"><b>Ver Carátula Bancaria actual</b></a>` : ""}
                        </div>
                    </div>`,
                },
                {
                    fieldname: "clabe",
                    label: "CLABE interbancaria / Cuenta bancaria",
                    fieldtype: "Data",
                    default: ef.clabe || "",
                },
                {
                    fieldname: "_archivos",
                    fieldtype: "HTML",
                    options: `
                        <div class="form-group">
                            <label class="control-label">Subir / Actualizar Constancia de Situación Fiscal (PDF / Imagen)</label>
                            <input type="file" accept=".pdf,image/*" class="form-control leb-in-csf">
                        </div>
                        <div class="form-group mb-0">
                            <label class="control-label">Subir / Actualizar Carátula Bancaria (PDF / Imagen)</label>
                            <input type="file" accept=".pdf,image/*" class="form-control leb-in-car">
                        </div>
                    `,
                },
            ],
            primary_action_label: "Guardar expediente",
            async primary_action(vals) {
                if (csfB64) {
                    await frappe.call({
                        method: "lebaref_compras.flujo_operativo.subir_documento_fiscal_proveedor",
                        type: "POST",
                        args: { proveedor, tipo: "csf", filename: csfName, content_base64: csfB64, clabe: vals.clabe || "" },
                        freeze: true,
                    });
                }
                if (carB64 || (!csfB64 && vals.clabe !== undefined)) {
                    await frappe.call({
                        method: "lebaref_compras.flujo_operativo.subir_documento_fiscal_proveedor",
                        type: "POST",
                        args: { proveedor, tipo: "caratula", filename: carName, content_base64: carB64, clabe: vals.clabe || "" },
                        freeze: true,
                    });
                }
                d.hide();
                frappe.show_alert({ message: "Expediente fiscal actualizado", indicator: "green" });
                if (typeof alGuardar === "function") alGuardar();
            },
        });
        const w = d.fields_dict._archivos.$wrapper;
        w.find(".leb-in-csf").on("change", function () {
            const f = this.files && this.files[0];
            if (!f) return;
            csfName = f.name;
            const r = new FileReader();
            r.onload = (e) => { csfB64 = String(e.target.result || ""); };
            r.readAsDataURL(f);
        });
        w.find(".leb-in-car").on("change", function () {
            const f = this.files && this.files[0];
            if (!f) return;
            carName = f.name;
            const r = new FileReader();
            r.onload = (e) => { carB64 = String(e.target.result || ""); };
            r.readAsDataURL(f);
        });
        d.show();
    }

    async function abrirModalPagoLogisticaOc(folioOc, proveedorId = "", alGuardar = null) {
        const [{ message: poDoc }, { message: ef }] = await Promise.all([
            frappe.db.get_doc("Purchase Order", folioOc),
            proveedorId
                ? frappe.call({ method: "lebaref_compras.flujo_operativo.estado_fiscal_proveedor", type: "GET", args: { proveedor: proveedorId } }).catch(() => ({ message: null }))
                : Promise.resolve({ message: null }),
        ]);
        const esc = frappe.utils.escape_html;
        let compName = "", compB64 = "";
        const bannerFiscal = ef
            ? `<div class="alert ${ef.expediente_completo ? "alert-success" : "alert-warning"} py-2 px-3 mb-2 d-flex justify-content-between align-items-center" style="font-size:12px;gap:8px;">
                <span>${ef.expediente_completo
                    ? "Expediente de proveedor completo (CSF y Carátula Bancaria listos)."
                    : `Atención antes de pedir pago a Dirección: falta <b>${esc((ef.faltantes || []).join(" y "))}</b>.`}</span>
                <button type="button" class="btn btn-xs btn-default leb-btn-subir-exp">Subir CSF / Carátula</button>
               </div>`
            : "";

        const d = new frappe.ui.Dialog({
            title: `Seguimiento de Pago y Logística · ${folioOc}`,
            fields: [
                { fieldname: "_fiscal", fieldtype: "HTML", options: bannerFiscal },
                {
                    fieldname: "nuevo_estado",
                    label: "Estado actual de pago y logística",
                    fieldtype: "Select",
                    options: [
                        "Pendiente de gestionar pago",
                        "Esperando pago Dirección",
                        "Pagado — Enviar comprobante",
                        "Liberado — Ir a recolectar",
                        "En flete",
                        "Entregado en almacén",
                        "Entregado en obra (con foto)",
                        "Consumo directo (Oficina)",
                    ].join("\n"),
                    default: (poDoc && poDoc.custom_lebaref_estado_operativo) || "Esperando pago Dirección",
                    reqd: 1,
                },
                {
                    fieldname: "modalidad_entrega",
                    label: "Modalidad de entrega acordada",
                    fieldtype: "Select",
                    options: "\nIr a recolectar\nEn flete (proveedor entrega)\nEntrega directa en obra\nConsumo directo (Oficina)",
                    default: (poDoc && poDoc.custom_lebaref_modalidad_entrega) || "Ir a recolectar",
                },
                {
                    fieldname: "dias_flete",
                    label: "Días promesa de entrega / flete",
                    fieldtype: "Int",
                    default: (poDoc && poDoc.custom_lebaref_dias_flete) || 1,
                },
                {
                    fieldname: "_comp_html",
                    fieldtype: "HTML",
                    options: `<div class="form-group">
                        <label class="control-label">Adjuntar comprobante de pago de Dirección (opcional, PDF / Imagen)</label>
                        <input type="file" accept=".pdf,image/*" class="form-control leb-in-comp">
                    </div>`,
                },
                {
                    fieldname: "notas",
                    label: "Instrucciones de recolección, guía de flete o comentarios",
                    fieldtype: "Small Text",
                    default: (poDoc && poDoc.custom_lebaref_notas_logisticas) || "",
                },
            ],
            primary_action_label: "Guardar estado",
            async primary_action(vals) {
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.flujo_operativo.actualizar_estado_pago_logistica",
                    type: "POST",
                    args: {
                        doctype: "Purchase Order",
                        folio: folioOc,
                        nuevo_estado: vals.nuevo_estado,
                        modalidad_entrega: vals.modalidad_entrega || "",
                        dias_flete: Number(vals.dias_flete || 0),
                        notas: vals.notas || "",
                        comprobante_nombre: compName,
                        comprobante_base64: compB64,
                    },
                    freeze: true,
                });
                d.hide();
                if (res && res.aviso_fiscal) {
                    frappe.show_alert({ message: res.aviso_fiscal, indicator: "orange" });
                } else {
                    frappe.show_alert({ message: `Estado actualizado: ${vals.nuevo_estado}`, indicator: "green" });
                }
                if (typeof alGuardar === "function") alGuardar(res);
            },
        });
        if (ef && proveedorId) {
            d.fields_dict._fiscal.$wrapper.find(".leb-btn-subir-exp").on("click", () => {
                d.hide();
                abrirModalExpedienteFiscalProveedor(proveedorId, () => abrirModalPagoLogisticaOc(folioOc, proveedorId, alGuardar));
            });
        }
        d.fields_dict._comp_html.$wrapper.find(".leb-in-comp").on("change", function () {
            const f = this.files && this.files[0];
            if (!f) return;
            compName = f.name;
            const r = new FileReader();
            r.onload = (e) => { compB64 = String(e.target.result || ""); };
            r.readAsDataURL(f);
        });
        d.show();
    }

    async function abrirModalEntregaObraFoto(folioOc, alGuardar = null) {
        const poDoc = await frappe.db.get_doc("Purchase Order", folioOc);
        const esc = frappe.utils.escape_html;
        let fotoName = "", fotoB64 = "";
        const renglones = (poDoc.items || []).map((it) => ({
            po_item: it.name,
            codigo: it.item_code,
            desc: it.item_name || it.description || it.item_code,
            pendiente: Math.max(0, Number(it.qty || 0) - Number(it.received_qty || 0)),
            qty: Math.max(0, Number(it.qty || 0) - Number(it.received_qty || 0)),
            uom: it.uom || "Pieza",
        })).filter((r) => r.pendiente > 0 || poDoc.docstatus === 0);

        const d = new frappe.ui.Dialog({
            title: `Confirmar Entrega en Obra / Servicio con Foto · ${folioOc}`,
            size: "large",
            fields: [
                {
                    fieldname: "_info",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        Registra la entrega directa en obra o servicio adjuntando <b>únicamente fotografía</b> (sin exigir remisión). Puedes ajustar cantidades si el proveedor entregó dividido.
                    </div>`,
                },
                {
                    fieldname: "reportado_por",
                    label: "Ingeniero o auxiliar que confirmó la recepción en sitio",
                    fieldtype: "Data",
                    reqd: 1,
                },
                {
                    fieldname: "_foto",
                    fieldtype: "HTML",
                    options: `<div class="form-group">
                        <label class="control-label">Fotografía de evidencia (obligatoria)</label>
                        <input type="file" accept="image/*" class="form-control leb-in-foto-obra">
                    </div>`,
                },
                { fieldname: "_partidas_div", fieldtype: "HTML" },
                {
                    fieldname: "notas",
                    label: "Notas u observaciones de la entrega",
                    fieldtype: "Small Text",
                },
            ],
            primary_action_label: "Confirmar entrega con foto",
            async primary_action(vals) {
                if (!fotoB64) {
                    frappe.msgprint("Selecciona la fotografía de evidencia de entrega.");
                    return;
                }
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.flujo_operativo.confirmar_entrega_obra_foto",
                    type: "POST",
                    args: {
                        orden: folioOc,
                        datos: JSON.stringify({
                            reportado_por: vals.reportado_por,
                            foto_nombre: fotoName,
                            foto_base64: fotoB64,
                            notas: vals.notas || "",
                            rows: renglones,
                        }),
                    },
                    freeze: true,
                    freeze_message: "Guardando fotografía y registrando entrega…",
                });
                d.hide();
                frappe.show_alert({ message: `Entrega en obra confirmada (${res.pct_recibido}% recibido)`, indicator: "green" });
                if (typeof alGuardar === "function") alGuardar(res);
            },
        });
        d.fields_dict._foto.$wrapper.find(".leb-in-foto-obra").on("change", function () {
            const f = this.files && this.files[0];
            if (!f) return;
            fotoName = f.name;
            const r = new FileReader();
            r.onload = (e) => { fotoB64 = String(e.target.result || ""); };
            r.readAsDataURL(f);
        });
        if (renglones.length) {
            const hostR = d.fields_dict._partidas_div.$wrapper;
            const tbl = $('<table class="table table-bordered table-sm mb-2" style="font-size:12px;"><thead><tr><th>Artículo</th><th class="text-right">Saldo</th><th style="width:120px;">Entregado hoy</th></tr></thead><tbody></tbody></table>').appendTo(hostR);
            const tb = tbl.find("tbody");
            renglones.forEach((r) => {
                const tr = $("<tr>").appendTo(tb);
                $("<td>").html(`<b>${esc(r.codigo)}</b> · ${esc(r.desc)}`).appendTo(tr);
                $('<td class="text-right">').text(`${r.pendiente} ${r.uom}`).appendTo(tr);
                const $in = $('<input type="number" step="0.01" min="0" class="form-control form-control-sm text-right">').val(r.qty).on("input", function () {
                    r.qty = parseFloat($(this).val()) || 0;
                });
                $("<td>").append($in).appendTo(tr);
            });
        }
        d.show();
    }

    async function abrirModalXmlCampoDirecto(alGuardar = null) {
        const input = document.createElement("input");
        input.type = "file";
        input.accept = ".xml,text/xml,application/xml";
        input.onchange = () => {
            const file = input.files && input.files[0];
            if (!file) return;
            const rd = new FileReader();
            rd.onload = async (ev) => {
                const xmlStr = String(ev.target.result || "");
                const { message: parsed } = await frappe.call({
                    method: "lebaref_compras.operaciones.cargar_xml_factura",
                    type: "POST",
                    args: { xml: xmlStr },
                    freeze: true,
                    freeze_message: "Leyendo factura XML de campo…",
                });
                if (!parsed) return;
                if (parsed.factura_existente) {
                    frappe.msgprint(`Este CFDI (${frappe.utils.escape_html(parsed.uuid)}) ya está registrado en ${frappe.utils.escape_html(parsed.factura_existente)}.`);
                    return;
                }
                const esc = frappe.utils.escape_html;
                const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                const d = new frappe.ui.Dialog({
                    title: "Registrar Compra Directa en Campo (Solo Factura XML)",
                    size: "large",
                    fields: [
                        {
                            fieldname: "_resumen",
                            fieldtype: "HTML",
                            options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                                <b>Emisor:</b> ${esc(parsed.issuer_name)} (${esc(parsed.issuer_rfc)}) · <b>Fecha:</b> ${esc(parsed.fecha)}<br>
                                <b>UUID:</b> <code>${esc(parsed.uuid)}</code> · <b>Total:</b> $${money.format(parsed.total || 0)} ${esc(parsed.moneda)}<br>
                                <span class="text-muted">No requiere Solicitud ni Orden de Compra previa y <b>no altera existencias de almacén</b>; alimenta costo de obra e historial de precios.</span>
                            </div>`,
                        },
                        {
                            fieldname: "obra",
                            label: "Obra / Proyecto al que corresponde el gasto",
                            fieldtype: "Link",
                            options: "Project",
                        },
                        {
                            fieldname: "departamento",
                            label: "Gerencia / Departamento",
                            fieldtype: "Link",
                            options: "Department",
                        },
                        {
                            fieldname: "comprador_campo",
                            label: "Ingeniero o personal que realizó la compra en campo",
                            fieldtype: "Data",
                            reqd: 1,
                        },
                        {
                            fieldname: "partida",
                            label: "Partida de obra (opcional)",
                            fieldtype: "Data",
                        },
                        {
                            fieldname: "tc_dof",
                            label: "Tipo de Cambio oficial DOF (obligatorio si el XML viene en USD)",
                            fieldtype: "Float",
                            precision: 4,
                            depends_on: `eval:'${parsed.moneda}' === 'USD'`,
                        },
                    ],
                    primary_action_label: "Registrar factura de campo",
                    async primary_action(vals) {
                        const { message: creado } = await frappe.call({
                            method: "lebaref_compras.flujo_operativo.registrar_xml_campo_directo",
                            type: "POST",
                            args: {
                                xml: xmlStr,
                                datos: JSON.stringify(vals),
                            },
                            freeze: true,
                            freeze_message: "Guardando compra de campo e historial de precios…",
                        });
                        d.hide();
                        frappe.show_alert({ message: `Factura de campo registrada: ${creado.folio}`, indicator: "green" });
                        if (typeof alGuardar === "function") alGuardar(creado);
                        else frappe.set_route("Form", "Purchase Invoice", creado.folio);
                    },
                });
                d.show();
            };
            rd.readAsText(file, "UTF-8");
        };
        input.click();
    }

    async function abrirModalConsolidarCotizacionesEnOc(alGenerar = null) {
        const cots = await frappe.db.get_list("Supplier Quotation", {
            filters: { docstatus: 1 },
            fields: ["name", "supplier", "supplier_name", "transaction_date", "currency", "grand_total"],
            order_by: "supplier asc, creation desc",
            limit: 100,
        });
        if (!cots || !cots.length) {
            frappe.msgprint("No hay cotizaciones confirmadas disponibles para consolidar.");
            return;
        }
        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const seleccion = new Set();
        const d = new frappe.ui.Dialog({
            title: "Consolidar Varias Cotizaciones en una sola Orden de Compra",
            size: "large",
            fields: [
                {
                    fieldname: "_info",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        Marca dos o más cotizaciones vigentes del <b>mismo proveedor y moneda</b> (de distintas solicitudes) para agruparlas en una sola Orden de Compra trazable.
                    </div>`,
                },
                { fieldname: "_lista", fieldtype: "HTML" },
            ],
            primary_action_label: "Generar OC consolidada",
            async primary_action() {
                if (!seleccion.size) {
                    frappe.msgprint("Marca al menos una cotización.");
                    return;
                }
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.flujo_operativo.consolidar_cotizaciones_en_oc",
                    type: "POST",
                    args: { cotizaciones_json: JSON.stringify([...seleccion]) },
                    freeze: true,
                    freeze_message: "Consolidando en Orden de Compra…",
                });
                d.hide();
                frappe.show_alert({ message: `OC consolidada creada: ${res.folio}`, indicator: "green" });
                if (typeof alGenerar === "function") alGenerar(res);
                frappe.set_route("Form", "Purchase Order", res.folio);
            },
        });
        const host = d.fields_dict._lista.$wrapper;
        const tbl = $('<table class="table table-bordered table-sm mb-0" style="font-size:12px;"><thead><tr><th style="width:34px;"></th><th>Cotización</th><th>Proveedor</th><th>Fecha</th><th class="text-right">Total</th></tr></thead><tbody></tbody></table>').appendTo(host);
        const tb = tbl.find("tbody");
        cots.forEach((c) => {
            const tr = $("<tr>").appendTo(tb);
            const $chk = $('<input type="checkbox">').on("change", function () {
                if (this.checked) seleccion.add(c.name);
                else seleccion.delete(c.name);
            });
            $('<td class="text-center">').append($chk).appendTo(tr);
            $("<td>").html(`<b>${esc(c.name)}</b>`).appendTo(tr);
            $("<td>").text(c.supplier_name || c.supplier).appendTo(tr);
            $("<td>").text(c.transaction_date || "").appendTo(tr);
            $('<td class="text-right">').text(`$${money.format(c.grand_total || 0)} ${c.currency || "MXN"}`).appendTo(tr);
        });
        d.show();
    }

    async function abrirHiloConductor(doctype, folio) {
        const { message: h } = await frappe.call({
            method: "lebaref_compras.flujo_operativo.hilo_conductor",
            type: "GET",
            args: { doctype, folio },
            freeze: true,
            freeze_message: "Consultando hilo conductor conciliado…",
        });
        if (!h) return;
        const esc = frappe.utils.escape_html;
        const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

        const d = new frappe.ui.Dialog({
            title: `Hilo Conductor Conciliado · ${folio}`,
            size: "extra-large",
            fields: [
                { fieldname: "_timeline", fieldtype: "HTML" },
                { fieldtype: "Section Break", label: "Registrar ajuste o cambio entre etapas (cantidad, precio, sustituto o condición)" },
                {
                    fieldname: "etapa",
                    label: "Etapa del ajuste",
                    fieldtype: "Select",
                    options: "Cotización vs. Solicitud\nOrden de Compra vs. Cotización\nEntrega / Obra vs. Orden\nFactura XML vs. Orden",
                    default: "Orden de Compra vs. Cotización",
                },
                {
                    fieldname: "detalle",
                    label: "Detalle del ajuste conciliado",
                    fieldtype: "Small Text",
                },
            ],
            primary_action_label: "Guardar nota en bitácora",
            async primary_action(vals) {
                if (!(vals.detalle || "").trim()) {
                    frappe.msgprint("Escribe el detalle del cambio o ajuste.");
                    return;
                }
                await frappe.call({
                    method: "lebaref_compras.flujo_operativo.registrar_nota_conciliacion",
                    type: "POST",
                    args: { doctype, folio, etapa: vals.etapa, detalle: vals.detalle },
                    freeze: true,
                });
                d.hide();
                frappe.show_alert({ message: "Nota de conciliación guardada en el hilo conductor", indicator: "green" });
                abrirHiloConductor(doctype, folio);
            },
        });

        const secSols = (h.solicitudes || []).map((s) => `<div><a href="/app/material-request/${encodeURIComponent(s.folio)}"><b>${esc(s.folio)}</b></a> · ${esc(s.nombre)} <small class="text-muted">(${esc(s.proposito)})</small></div>`).join("") || `<div class="text-muted small">Sin solicitud previa</div>`;
        const secCots = (h.cotizaciones || []).map((c) => `<div><a href="/app/supplier-quotation/${encodeURIComponent(c.folio)}"><b>${esc(c.folio)}</b></a> · ${esc(c.proveedor)} · <b>$${money.format(c.total || 0)} ${esc(c.moneda)}</b>${c.moneda === "USD" ? ` (DOF ${c.tc_dof})` : ""}</div>`).join("") || `<div class="text-muted small">Sin cotizaciones</div>`;
        const secOcs = (h.ordenes || []).map((o) => `<div><a href="/app/purchase-order/${encodeURIComponent(o.folio)}"><b>${esc(o.folio)}</b></a> · $${money.format(o.total || 0)} ${esc(o.moneda)} · <span class="badge badge-info">${esc(o.estado_logistico)}</span> · Rec: ${o.pct_recibido}%${o.foto_obra ? ` · <a href="${esc(o.foto_obra)}" target="_blank"><b>Foto obra</b></a>` : ""}${o.comprobante_pago ? ` · <a href="${esc(o.comprobante_pago)}" target="_blank"><b>Comprobante pago</b></a>` : ""}</div>`).join("") || `<div class="text-muted small">Sin órdenes emitidas</div>`;
        const secRecs = (h.recepciones || []).map((r) => `<div><a href="/app/purchase-receipt/${encodeURIComponent(r.folio)}"><b>${esc(r.folio)}</b></a> · ${esc(r.fecha)} · Recibió: ${esc(r.recibido_por)} (${esc(r.estado)})</div>`).join("") || `<div class="text-muted small">Sin recepciones registradas</div>`;
        const secXmls = (h.facturas || []).map((f) => `<div><a href="/app/purchase-invoice/${encodeURIComponent(f.folio)}"><b>${esc(f.folio)}</b></a> · $${money.format(f.total || 0)} ${esc(f.moneda)} · ${esc(f.estado)}</div>`).join("") || `<div class="text-muted small">Sin facturas XML conciliadas</div>`;
        const secBit = (h.bitacora || []).map((b) => `<div class="py-1" style="border-bottom:1px solid #f1f5f9;font-size:11.5px;"><b>${esc(b.etapa || "")}</b> (${esc(b.documento || "")}) · ${esc(b.detalle || "")} <span class="text-muted">— ${esc(b.usuario || "")} · ${esc(b.fecha || "")}</span></div>`).join("") || `<div class="text-muted small">Sin cambios o ajustes registrados aún.</div>`;

        d.fields_dict._timeline.$wrapper.html(`
            <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:8px;margin-bottom:12px;font-size:12px;">
                <div style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:7px;padding:8px 10px;"><b style="color:#031e34;">1. Solicitud</b><div class="mt-1">${secSols}</div></div>
                <div style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:7px;padding:8px 10px;"><b style="color:#031e34;">2. Cotizaciones</b><div class="mt-1">${secCots}</div></div>
                <div style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:7px;padding:8px 10px;"><b style="color:#031e34;">3. OC / Pago / Logística</b><div class="mt-1">${secOcs}</div></div>
                <div style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:7px;padding:8px 10px;"><b style="color:#031e34;">4. Entrega / Foto</b><div class="mt-1">${secRecs}</div></div>
                <div style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:7px;padding:8px 10px;"><b style="color:#031e34;">5. Factura XML</b><div class="mt-1">${secXmls}</div></div>
            </div>
            <div style="background:#ffffff;border:1px solid #e2e8f0;border-radius:7px;padding:8px 10px;">
                <b style="font-size:12px;color:#031e34;">Bitácora de seguimiento y ajustes entre etapas</b>
                <div class="mt-1" style="max-height:150px;overflow-y:auto;">${secBit}</div>
            </div>
        `);
        d.show();
    }

    // =========================================================================
    // FASE 4: Bandeja de Alias y Equivalencias (CAT-02) + Importador Histórico (MIG-02)
    // =========================================================================

    function abrirModalProponerEquivalencia(inicial = {}, alGuardar = null) {
        const d = new frappe.ui.Dialog({
            title: "Proponer Alias o Equivalencia de Artículo",
            fields: [
                {
                    fieldname: "_info",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        Conserva intacto el nombre o clave recibido y vincúlalo a un SKU como <b>Alias directo</b> o envíalo como <b>Pendiente</b> para revisarlo en junta con las gerencias.
                    </div>`,
                },
                {
                    fieldname: "texto_original",
                    label: "Nombre o descripción original recibida (Ingeniero / Proveedor / XML)",
                    fieldtype: "Data",
                    default: inicial.texto_original || "",
                    reqd: 1,
                },
                {
                    fieldname: "clave_externa",
                    label: "Clave o modelo del proveedor (opcional)",
                    fieldtype: "Data",
                    default: inicial.clave_externa || "",
                },
                {
                    fieldname: "origen",
                    label: "Origen del concepto",
                    fieldtype: "Select",
                    options: "Solicitud de Ingeniero\nCotización de Proveedor\nFactura XML\nImportación Histórica\nRevisión Manual",
                    default: inicial.origen || "Solicitud de Ingeniero",
                },
                {
                    fieldname: "gerencia",
                    label: "Gerencia relacionada",
                    fieldtype: "Select",
                    options: "\nHVAC\nEléctrico\nObra Civil\nMantenimiento Industrial\nOficina\nGeneral",
                    default: inicial.gerencia || "",
                },
                { fieldtype: "Column Break" },
                {
                    fieldname: "articulo_propuesto",
                    label: "Artículo del catálogo (SKU propuesto)",
                    fieldtype: "Link",
                    options: "Item",
                    default: inicial.articulo_propuesto || "",
                },
                {
                    fieldname: "proveedor",
                    label: "Proveedor origen (opcional)",
                    fieldtype: "Link",
                    options: "Supplier",
                    default: inicial.proveedor || "",
                },
                {
                    fieldname: "dictamen",
                    label: "Estado / Dictamen",
                    fieldtype: "Select",
                    options: "Pendiente\nExacto (Alias directo)\nEquivalente aprobado\nSustituto puntual\nDistinto (No agrupar)",
                    default: inicial.dictamen || "Pendiente",
                    reqd: 1,
                },
                {
                    fieldname: "alcance",
                    label: "Alcance de aplicación",
                    fieldtype: "Select",
                    options: "Toda la empresa\nSolo Cliente / Proyecto específico",
                    default: inicial.alcance || "Toda la empresa",
                },
                {
                    fieldname: "proyecto",
                    label: "Obra / Proyecto específico",
                    fieldtype: "Link",
                    options: "Project",
                    default: inicial.proyecto || "",
                    depends_on: "eval:doc.alcance === 'Solo Cliente / Proyecto específico'",
                },
                {
                    fieldname: "cliente",
                    label: "Cliente específico (opcional)",
                    fieldtype: "Link",
                    options: "Customer",
                    default: inicial.cliente || "",
                    depends_on: "eval:doc.alcance === 'Solo Cliente / Proyecto específico'",
                },
                { fieldtype: "Section Break" },
                {
                    fieldname: "notas_gerencia",
                    label: "Notas o acuerdo técnico con gerencia (opcional)",
                    fieldtype: "Small Text",
                    default: inicial.notas_gerencia || "",
                },
            ],
            primary_action_label: "Guardar en equivalencias",
            async primary_action(vals) {
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.catalogo_gobierno.proponer_alias_o_equivalencia",
                    type: "POST",
                    args: { datos: JSON.stringify(vals) },
                    freeze: true,
                    freeze_message: "Guardando alias / candidato de equivalencia…",
                });
                d.hide();
                frappe.show_alert({
                    message: `Registrado (${res.folio} · ${res.dictamen})`,
                    indicator: "green",
                });
                if (typeof alGuardar === "function") alGuardar(res);
            },
        });
        d.show();
    }

    function imprimirMinutaEquivalenciasCarta(datos, filtroGerencia = "") {
        const esc = frappe.utils.escape_html;
        const items = (datos && datos.items) || [];
        const trs = items.map((r, i) => `
            <tr>
                <td>${i + 1}</td>
                <td><b>${esc(r.texto_original || "")}</b>${r.clave_externa ? `<br><small>Clave: ${esc(r.clave_externa)}</small>` : ""}<br><small style="color:#64748b;">Origen: ${esc(r.origen || "")}${r.proveedor ? " · " + esc(r.proveedor) : ""}</small></td>
                <td>${esc(r.gerencia || "General")}</td>
                <td><b>${esc(r.articulo_propuesto || "Por asignar")}</b><br><small>${esc(r.nombre_articulo || "")}</small></td>
                <td><b>${esc(r.dictamen || "Pendiente")}</b><br><small>${esc(r.alcance || "Toda la empresa")}${r.proyecto ? " (" + esc(r.proyecto) + ")" : ""}</small></td>
                <td>${esc(r.notas_gerencia || "")}</td>
            </tr>
        `).join("");
        const w = window.open("", "_blank");
        if (!w) return;
        w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Revisión de Equivalencias Lebaref</title>
            <style>
                @page { size: letter portrait; margin: 12mm; }
                body { font-family: 'Segoe UI', Arial, sans-serif; color:#0f172a; font-size:11px; }
                .hdr { display:flex; justify-content:space-between; align-items:center; border-bottom:2.5px solid #031e34; padding-bottom:8px; margin-bottom:10px; }
                table { width:100%; border-collapse:collapse; margin-top:6px; }
                th { background:#031e34; color:#fff; padding:6px; text-align:left; border:1px solid #031e34; font-size:10.5px; }
                td { padding:6px; border:1px solid #cbd5e1; vertical-align:top; }
            </style></head><body>
            <div class="hdr">
                <img src="/files/lebaref_logo_horizontal.svg" alt="Lebaref" style="height:50px;">
                <div style="text-align:right;">
                    <b style="font-size:14px;color:#031e34;">MINUTA DE EQUIVALENCIAS Y ALIAS CON GERENCIAS</b><br>
                    <span style="color:#475569;">${filtroGerencia ? "Gerencia: " + esc(filtroGerencia) + " · " : ""}Impreso: ${new Date().toLocaleString("es-MX")}</span>
                </div>
            </div>
            <table>
                <thead><tr><th style="width:28px;">#</th><th>Texto / Clave Original</th><th style="width:95px;">Gerencia</th><th>SKU Catálogo Propuesto</th><th style="width:130px;">Dictamen / Alcance</th><th style="width:150px;">Acuerdo / Firma</th></tr></thead>
                <tbody>${trs || "<tr><td colspan='6'>Sin registros para este filtro.</td></tr>"}</tbody>
            </table>
            <script>window.onload = () => window.print();<\/script>
            </body></html>`);
        w.document.close();
    }

    async function abrirBandejaEquivalencias(estadoInicial = "pendientes") {
        let filtroEstado = estadoInicial;
        let filtroGerencia = "";
        let filtroQ = "";
        let ultimoResultado = null;

        const d = new frappe.ui.Dialog({
            title: "Bandeja de Alias y Equivalencias para Junta con Gerencias",
            size: "extra-large",
            fields: [{ fieldname: "_wrap", fieldtype: "HTML" }],
        });

        const host = d.fields_dict._wrap.$wrapper;
        host.html(`
            <div class="d-flex justify-content-between align-items-center mb-2 flex-wrap" style="gap:8px;padding:8px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;">
                <div class="d-flex align-items-center flex-wrap" style="gap:6px;">
                    <div class="btn-group btn-group-sm">
                        <button type="button" class="btn btn-default leb-eq-tab" data-est="pendientes">Pendientes</button>
                        <button type="button" class="btn btn-default leb-eq-tab" data-est="aprobadas">Aprobadas / Alias</button>
                        <button type="button" class="btn btn-default leb-eq-tab" data-est="">Todas</button>
                    </div>
                    <select class="form-control form-control-sm leb-eq-ger" style="width:165px;">
                        <option value="">Todas las gerencias</option>
                        <option value="HVAC">HVAC</option>
                        <option value="Eléctrico">Eléctrico</option>
                        <option value="Obra Civil">Obra Civil</option>
                        <option value="Mantenimiento Industrial">Mantenimiento Industrial</option>
                        <option value="Oficina">Oficina</option>
                    </select>
                    <input type="text" class="form-control form-control-sm leb-eq-q" placeholder="Buscar concepto, SKU o proveedor…" style="width:230px;">
                </div>
                <div class="d-flex" style="gap:6px;">
                    <button type="button" class="btn btn-xs btn-default leb-eq-print">Imprimir minuta Carta</button>
                    <button type="button" class="btn btn-xs btn-primary leb-eq-new">+ Proponer alias / equivalencia</button>
                </div>
            </div>
            <div class="leb-eq-kpis mb-2 small text-muted"></div>
            <div class="leb-eq-body table-responsive" style="max-height:430px;overflow:auto;"></div>
        `);

        const $body = host.find(".leb-eq-body");
        const $kpis = host.find(".leb-eq-kpis");
        const dlId = "leb-eq-skus-" + Date.now();

        async function recargarBandeja() {
            host.find(".leb-eq-tab").each(function () {
                const act = $(this).data("est") === filtroEstado;
                $(this).toggleClass("btn-primary", act).toggleClass("btn-default", !act);
            });
            $body.html(lebSkeletonHtml("tabla"));
            const { message: data } = await frappe.call({
                method: "lebaref_compras.catalogo_gobierno.listar_bandeja_equivalencias",
                type: "GET",
                args: { estado: filtroEstado, gerencia: filtroGerencia, q: filtroQ },
            });
            ultimoResultado = data || {};
            const esc = frappe.utils.escape_html;
            const res = ultimoResultado.resumen || {};
            $kpis.html(
                `Pendientes de junta: <b>${res.pendientes || 0}</b> &nbsp;·&nbsp; ` +
                `Alias y equivalencias activas: <b style="color:#059669;">${res.aprobadas || 0}</b> &nbsp;·&nbsp; ` +
                `Sustitutos puntuales: <b>${res.puntuales || 0}</b> &nbsp;·&nbsp; ` +
                `Distintos (no agrupar): <b>${res.rechazadas || 0}</b>`
            );

            const dlHtml = `<datalist id="${dlId}">${(ultimoResultado.skus || []).map((s) => `<option value="${esc(s.name)}">${esc(s.item_name)}</option>`).join("")}</datalist>`;
            const obrasOpts = ["", ...(ultimoResultado.obras || [])];

            const $tbl = $(`
                ${dlHtml}
                <table class="table table-bordered table-sm mb-0" style="font-size:12px;">
                    <thead style="background:#031e34;color:#fff;">
                        <tr>
                            <th>Texto Original Recibido</th>
                            <th style="width:110px;">Gerencia</th>
                            <th style="width:185px;">SKU Catálogo Destino</th>
                            <th style="width:175px;">Dictamen de Gerencia</th>
                            <th style="width:175px;">Alcance / Obra</th>
                            <th style="width:88px;">Acción</th>
                        </tr>
                    </thead>
                    <tbody></tbody>
                </table>
            `);
            const $tb = $tbl.filter("table").find("tbody");
            const items = ultimoResultado.items || [];
            if (!items.length) {
                $tb.append(`<tr><td colspan="6" class="text-muted text-center py-3">Sin candidatos en este filtro.</td></tr>`);
            } else {
                for (const row of items) {
                    const $tr = $("<tr>").appendTo($tb);
                    $("<td>").html(
                        `<b>${esc(row.texto_original || "")}</b>` +
                        (row.clave_externa ? `<br><small class="text-muted">Clave prov: ${esc(row.clave_externa)}</small>` : "") +
                        `<div class="text-muted" style="font-size:10.5px;">${esc(row.origen || "")}${row.proveedor ? " · " + esc(row.proveedor) : ""}</div>`
                    ).appendTo($tr);
                    $("<td>").text(row.gerencia || "General").appendTo($tr);

                    const $skuIn = $(`<input type="text" list="${dlId}" class="form-control form-control-sm" placeholder="Código SKU">`)
                        .val(row.articulo_propuesto || "");
                    const $tdSku = $("<td>").append($skuIn).appendTo($tr);
                    if (row.nombre_articulo) {
                        $(`<div class="text-muted mt-1" style="font-size:10.5px;">${esc(row.nombre_articulo)}</div>`).appendTo($tdSku);
                    }

                    const $selDict = $('<select class="form-control form-control-sm">');
                    for (const op of ["Pendiente", "Exacto (Alias directo)", "Equivalente aprobado", "Sustituto puntual", "Distinto (No agrupar)"]) {
                        $("<option>").val(op).text(op).prop("selected", op === row.dictamen).appendTo($selDict);
                    }
                    $("<td>").append($selDict).appendTo($tr);

                    const $tdAlc = $("<td>").appendTo($tr);
                    const $selAlc = $('<select class="form-control form-control-sm mb-1">').appendTo($tdAlc);
                    for (const al of ["Toda la empresa", "Solo Cliente / Proyecto específico"]) {
                        $("<option>").val(al).text(al === "Toda la empresa" ? "Toda la empresa" : "Solo Obra / Cliente").prop("selected", al === row.alcance).appendTo($selAlc);
                    }
                    const $selProy = $('<select class="form-control form-control-sm">')
                        .toggleClass("d-none", row.alcance !== "Solo Cliente / Proyecto específico")
                        .appendTo($tdAlc);
                    for (const ob of obrasOpts) {
                        $("<option>").val(ob).text(ob || "— Elegir obra —").prop("selected", ob === (row.proyecto || "")).appendTo($selProy);
                    }
                    $selAlc.on("change", function () {
                        $selProy.toggleClass("d-none", $(this).val() !== "Solo Cliente / Proyecto específico");
                    });

                    const $btnSave = $('<button type="button" class="btn btn-xs btn-primary btn-block">Aplicar</button>').on("click", async () => {
                        await frappe.call({
                            method: "lebaref_compras.catalogo_gobierno.dictaminar_equivalencia",
                            type: "POST",
                            args: {
                                datos: JSON.stringify({
                                    name: row.name,
                                    articulo_propuesto: $skuIn.val().trim(),
                                    dictamen: $selDict.val(),
                                    alcance: $selAlc.val(),
                                    proyecto: $selAlc.val() === "Solo Cliente / Proyecto específico" ? $selProy.val() : "",
                                }),
                            },
                            freeze: true,
                        });
                        frappe.show_alert({ message: `Dictamen guardado (${row.name})`, indicator: "green" });
                        recargarBandeja();
                    });
                    $("<td>").append($btnSave).appendTo($tr);
                }
            }
            $body.empty().append($tbl);
        }

        host.find(".leb-eq-tab").on("click", function () {
            filtroEstado = $(this).data("est");
            recargarBandeja();
        });
        host.find(".leb-eq-ger").on("change", function () {
            filtroGerencia = $(this).val() || "";
            recargarBandeja();
        });
        host.find(".leb-eq-q").on("input", lebDebounce(function () {
            filtroQ = $(this).val() || "";
            recargarBandeja();
        }, 220));
        host.find(".leb-eq-print").on("click", () => {
            if (ultimoResultado) imprimirMinutaEquivalenciasCarta(ultimoResultado, filtroGerencia);
        });
        host.find(".leb-eq-new").on("click", () => {
            abrirModalProponerEquivalencia({}, () => recargarBandeja());
        });

        d.show();
        recargarBandeja();
    }

    function abrirImportadorCotizacionesHistoricas(alImportar = null) {
        let fNombre = "";
        let fBase64 = "";
        let filasPrevisualizadas = [];

        const d = new frappe.ui.Dialog({
            title: "Importador Asistido de Cotizaciones Históricas (1 Año · MXN / USD)",
            size: "extra-large",
            fields: [
                {
                    fieldname: "_top",
                    fieldtype: "HTML",
                    options: `
                        <div class="d-flex justify-content-between align-items-center p-2 mb-2 flex-wrap" style="background:#f8fafc;border:1px solid #cbd5e1;border-radius:7px;font-size:12px;gap:8px;">
                            <div>
                                Carga el archivo <b>Plantilla_Cotizaciones_Historicas_Lebaref.xlsx</b>. Se guardan como <b>cotizaciones históricas</b> (nunca alteran existencias ni crean órdenes de compra) y vinculan alias hacia el catálogo.
                            </div>
                            <div class="d-flex" style="gap:6px;">
                                <a href="/files/Plantilla_Cotizaciones_Historicas_Lebaref.xlsx" target="_blank" class="btn btn-xs btn-default">Descargar plantilla (.xlsx)</a>
                                <button type="button" class="btn btn-xs btn-primary leb-ch-pick">Seleccionar archivo Excel / CSV</button>
                                <input type="file" accept=".xlsx,.csv,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" class="d-none leb-ch-file">
                            </div>
                        </div>
                        <div class="small text-success font-weight-bold mb-2 d-none leb-ch-lbl"></div>
                    `,
                },
                {
                    fieldname: "registrar_alias_vinculados",
                    label: "Registrar automáticamente el nombre/clave del proveedor como Alias directo cuando la fila tenga SKU asignado",
                    fieldtype: "Check",
                    default: 1,
                },
                {
                    fieldname: "enviar_sin_sku_a_bandeja",
                    label: "Enviar descripciones sin SKU a la Bandeja de Equivalencias para revisión posterior con gerencias",
                    fieldtype: "Check",
                    default: 0,
                },
                {
                    fieldname: "texto_csv",
                    label: "O pega directamente las filas copiadas desde Excel (opcional)",
                    fieldtype: "Small Text",
                },
                {
                    fieldname: "_preview",
                    fieldtype: "HTML",
                },
            ],
            primary_action_label: "Importar cotizaciones históricas",
            async primary_action(vals) {
                if (!filasPrevisualizadas.length) {
                    await ejecutarPrevisualizacion();
                    if (!filasPrevisualizadas.length) return;
                }
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.catalogo_gobierno.ejecutar_importacion_cotizaciones_historicas",
                    type: "POST",
                    args: {
                        datos: JSON.stringify({
                            filas: filasPrevisualizadas,
                            registrar_alias_vinculados: vals.registrar_alias_vinculados ? 1 : 0,
                            enviar_sin_sku_a_bandeja: vals.enviar_sin_sku_a_bandeja ? 1 : 0,
                        }),
                    },
                    freeze: true,
                    freeze_message: "Importando cotizaciones históricas y registrando alias…",
                });
                d.hide();
                frappe.show_alert({
                    message: `${res.importadas} cotización(es) histórica(s) importada(s) (${res.alias_creados} alias vinculados).`,
                    indicator: "green",
                });
                if (typeof alImportar === "function") alImportar(res);
            },
        });

        async function ejecutarPrevisualizacion() {
            const txt = (d.get_value("texto_csv") || "").trim();
            if (!txt && !fBase64) {
                frappe.msgprint("Selecciona primero el archivo Excel (.xlsx) / CSV o pega las filas.");
                return;
            }
            const $prev = d.fields_dict._preview.$wrapper;
            $prev.html(lebSkeletonHtml("tabla"));
            const { message: prev } = await frappe.call({
                method: "lebaref_compras.catalogo_gobierno.previsualizar_importacion_cotizaciones_historicas",
                type: "POST",
                args: {
                    texto_csv: txt,
                    filename: fNombre,
                    content_base64: fBase64,
                },
            });
            const esc = frappe.utils.escape_html;
            const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            const r = (prev && prev.resumen) || {};
            filasPrevisualizadas = (prev && prev.filas) || [];
            const dlSkuId = "leb-ch-skus-" + Date.now();
            const dlHtml = `<datalist id="${dlSkuId}">${((prev && prev.skus) || []).map((s) => `<option value="${esc(s.name)}">${esc(s.item_name)}</option>`).join("")}</datalist>`;

            const errHtml = (prev.errores || []).length
                ? `<div class="alert alert-danger py-2 px-3 mb-2" style="font-size:12px;"><b>Corrige estas filas antes de importar:</b><br>• ${prev.errores.map(esc).join("<br>• ")}</div>`
                : "";

            const resHtml = `
                <div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                    Listas para importar: <b>${r.listas_para_importar || 0}</b> &nbsp;|&nbsp;
                    Vinculadas a SKU: <b style="color:#059669;">${r.vinculadas_a_sku || 0}</b> &nbsp;|&nbsp;
                    Sin SKU (histórico libre): <b>${r.sin_sku || 0}</b> &nbsp;|&nbsp;
                    Duplicadas omitidas: <b>${r.duplicadas_omitidas || 0}</b>
                    ${r.ejemplos_ignorados ? ` &nbsp;|&nbsp; Ejemplos plantilla ignorados: <b>${r.ejemplos_ignorados}</b>` : ""}
                </div>
            `;

            const $wrapTbl = $(`
                ${errHtml}${resHtml}${dlHtml}
                <div class="table-responsive" style="max-height:260px;overflow:auto;">
                    <table class="table table-bordered table-sm mb-0" style="font-size:12px;">
                        <thead style="background:#031e34;color:#fff;">
                            <tr>
                                <th>Fecha</th>
                                <th>Proveedor</th>
                                <th>Descripción Original Cotizada</th>
                                <th class="text-right">Precio / MXN</th>
                                <th style="width:195px;">SKU Catálogo (ajustable)</th>
                            </tr>
                        </thead>
                        <tbody></tbody>
                    </table>
                </div>
            `);
            const $tb = $wrapTbl.find("tbody");
            if (!filasPrevisualizadas.length) {
                $tb.append(`<tr><td colspan="5" class="text-muted text-center py-2">No hay filas nuevas pendientes de importar.</td></tr>`);
            } else {
                filasPrevisualizadas.slice(0, 200).forEach((f) => {
                    const $tr = $("<tr>").appendTo($tb);
                    $("<td>").text(f.fecha_cotizacion).appendTo($tr);
                    $("<td>").text(f.proveedor_texto).appendTo($tr);
                    $("<td>").html(`<b>${esc(f.descripcion_original)}</b>${f.clave_modelo_proveedor ? `<br><small class="text-muted">Clave: ${esc(f.clave_modelo_proveedor)}</small>` : ""}`).appendTo($tr);
                    const subMon = f.moneda === "USD" ? `<br><small style="color:#0369a1;">USD ${money.format(f.precio_unitario_sin_iva)} · DOF ${f.tc_dof.toFixed(4)}</small>` : "";
                    $('<td class="text-right">').html(`<b>$${money.format(f.precio_unitario_mxn)} MXN</b> / ${esc(f.unidad)}${subMon}`).appendTo($tr);
                    const $inSku = $(`<input type="text" list="${dlSkuId}" class="form-control form-control-sm" placeholder="Vacío = Sin SKU">`)
                        .val(f.item_code || "")
                        .on("change input", function () {
                            f.item_code = $(this).val().trim();
                        });
                    const $tdS = $("<td>").append($inSku).appendTo($tr);
                    $(`<div class="text-muted mt-1" style="font-size:10px;">${esc(f.origen_vinculo || "")}</div>`).appendTo($tdS);
                });
            }
            $prev.empty().append($wrapTbl);
        }

        d.add_custom_action("Previsualizar archivo", ejecutarPrevisualizacion);

        const topW = d.fields_dict._top.$wrapper;
        topW.find(".leb-ch-pick").on("click", () => topW.find(".leb-ch-file").trigger("click"));
        topW.find(".leb-ch-file").on("change", function () {
            const file = this.files && this.files[0];
            if (!file) return;
            fNombre = file.name;
            const rd = new FileReader();
            rd.onload = (ev) => {
                fBase64 = String(ev.target.result || "");
                topW.find(".leb-ch-lbl").removeClass("d-none").text(`Archivo cargado: ${fNombre}`);
                ejecutarPrevisualizacion();
            };
            rd.readAsDataURL(file);
        });

        d.show();
    }

    window.lebaref = Object.assign(window.lebaref || {}, {
        abrirModalNuevaSolicitudCabecera,
        abrirCargaExcelSolicitudExistente,
        abrirEditorPartidasSolicitud,
        abrirAsistenteFacturaXml,
        abrirModalExpedienteFiscalProveedor,
        abrirModalPagoLogisticaOc,
        abrirModalEntregaObraFoto,
        abrirModalXmlCampoDirecto,
        abrirModalConsolidarCotizacionesEnOc,
        abrirHiloConductor,
        copiarSolicitudParaWhatsapp,
        renderizarBloquesPartidaEnFormulario,
        abrirModalProponerEquivalencia,
        abrirBandejaEquivalencias,
        abrirImportadorCotizacionesHistoricas,
    });

    if (frappe && frappe.listview_settings) {
        const prevPIList = frappe.listview_settings["Purchase Invoice"] || {};
        if (!prevPIList.__lebXmlWrapped) {
            const prevPIOnload = prevPIList.onload;
            frappe.listview_settings["Purchase Invoice"] = Object.assign({}, prevPIList, {
                __lebXmlWrapped: true,
                onload(listview) {
                    if (typeof prevPIOnload === "function") prevPIOnload(listview);
                    if (listview.__lebXmlBtnReady) return;
                    listview.__lebXmlBtnReady = true;
                    lebBtn(
                        listview.page.add_inner_button("Registrar factura XML", () => {
                            abrirAsistenteFacturaXml({});
                        }),
                        "upload",
                        "Registrar factura XML"
                    );
                    lebBtn(
                        listview.page.add_inner_button("XML directo de campo", () => {
                            abrirModalXmlCampoDirecto(() => listview.refresh());
                        }),
                        "file",
                        "XML directo de campo"
                    );
                },
            });
        }

        const prevSQList = frappe.listview_settings["Supplier Quotation"] || {};
        if (!prevSQList.__lebSqWrapped) {
            const prevSQOnload = prevSQList.onload;
            frappe.listview_settings["Supplier Quotation"] = Object.assign({}, prevSQList, {
                __lebSqWrapped: true,
                onload(listview) {
                    if (typeof prevSQOnload === "function") prevSQOnload(listview);
                    if (listview.__lebSqBtnReady) return;
                    listview.__lebSqBtnReady = true;
                    lebBtn(
                        listview.page.add_inner_button("Consolidar en una OC", () => {
                            abrirModalConsolidarCotizacionesEnOc(() => listview.refresh());
                        }),
                        "layers",
                        "Consolidar en una OC"
                    );
                    lebBtn(
                        listview.page.add_inner_button("Importar cotizaciones históricas", () => {
                            abrirImportadorCotizacionesHistoricas(() => listview.refresh());
                        }),
                        "upload",
                        "Importar cotizaciones históricas"
                    );
                },
            });
        }

        const prevPOList = frappe.listview_settings["Purchase Order"] || {};
        if (!prevPOList.__lebPoIndWrapped) {
            const camposExtraPO = [...new Set([...(prevPOList.add_fields || []), "status", "docstatus", "custom_lebaref_estado_operativo"])];
            frappe.listview_settings["Purchase Order"] = Object.assign({}, prevPOList, {
                __lebPoIndWrapped: true,
                add_fields: camposExtraPO,
                get_indicator(doc) {
                    const estOp = String(doc.custom_lebaref_estado_operativo || "").trim();
                    if (doc.docstatus !== 2 && (estOp.startsWith("Esperando") || estOp.startsWith("En espera"))) {
                        return [estOp, "purple", `custom_lebaref_estado_operativo,=,${estOp}`];
                    }
                    return typeof prevPOList.get_indicator === "function" ? prevPOList.get_indicator(doc) : undefined;
                },
            });
        }

        const prevEqList = frappe.listview_settings["Lebaref Equivalencia Articulo"] || {};
        if (!prevEqList.__lebEqWrapped) {
            const prevEqOnload = prevEqList.onload;
            frappe.listview_settings["Lebaref Equivalencia Articulo"] = Object.assign({}, prevEqList, {
                __lebEqWrapped: true,
                onload(listview) {
                    if (typeof prevEqOnload === "function") prevEqOnload(listview);
                    if (listview.__lebEqBtnsReady) return;
                    listview.__lebEqBtnsReady = true;
                    lebBtn(
                        listview.page.add_inner_button("Bandeja de junta con gerencias", () => abrirBandejaEquivalencias()),
                        "table",
                        "Bandeja de junta con gerencias"
                    );
                    lebBtn(
                        listview.page.add_inner_button("Proponer equivalencia", () => abrirModalProponerEquivalencia({}, () => listview.refresh())),
                        "plus",
                        "Proponer equivalencia"
                    );
                },
            });
        }

        const prevChList = frappe.listview_settings["Lebaref Cotizacion Historica"] || {};
        if (!prevChList.__lebChWrapped) {
            const prevChOnload = prevChList.onload;
            frappe.listview_settings["Lebaref Cotizacion Historica"] = Object.assign({}, prevChList, {
                __lebChWrapped: true,
                onload(listview) {
                    if (typeof prevChOnload === "function") prevChOnload(listview);
                    if (listview.__lebChBtnsReady) return;
                    listview.__lebChBtnsReady = true;
                    lebBtn(
                        listview.page.add_inner_button("Importar Excel histórico", () => abrirImportadorCotizacionesHistoricas(() => listview.refresh())),
                        "upload",
                        "Importar Excel histórico"
                    );
                    lebBtn(
                        listview.page.add_inner_button("Descargar plantilla (.xlsx)", () => window.open("/files/Plantilla_Cotizaciones_Historicas_Lebaref.xlsx", "_blank")),
                        "download",
                        "Descargar plantilla (.xlsx)"
                    );
                },
            });
        }
    }

    if (frappe && frappe.ui && frappe.ui.form) {
        frappe.ui.form.on("Purchase Invoice", {
            refresh(frm) {
                if (frm.doc.docstatus !== 0) return;
                lebBtn(
                    frm.add_custom_button("Cargar XML CFDI", () => {
                        abrirAsistenteFacturaXml({ frm });
                    }),
                    "upload",
                    "Cargar XML CFDI"
                );
            },
        });

        frappe.ui.form.on("Supplier Quotation", {
            refresh(frm) {
                if (frm.doc.docstatus !== 0) return;
                lebBtn(
                    frm.add_custom_button("Importar Excel", () => {
                        let fName = "";
                        let fB64 = "";
                        const d = new frappe.ui.Dialog({
                            title: "Carga masiva en cotización de proveedor",
                            fields: [
                                {
                                    fieldname: "_top",
                                    fieldtype: "HTML",
                                    options: `<div class="d-flex justify-content-between align-items-center p-2 mb-2" style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;font-size:12px;gap:8px;flex-wrap:wrap;">
                                        <span>Columnas: <code>Clave_o_Concepto | Cantidad | Unidad | Precio_Sin_IVA | Días_Entrega | Partida</code></span>
                                        <div class="d-flex" style="gap:6px;">
                                            <a href="/files/Plantilla_Cotizaciones_Historicas_Lebaref.xlsx" target="_blank" class="btn btn-xs btn-default">Plantilla histórica (.xlsx)</a>
                                            <button type="button" class="btn btn-xs btn-primary leb-sq-pick">Subir Excel (.xlsx) / CSV</button>
                                            <input type="file" accept=".xlsx,.csv,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" class="d-none leb-sq-file">
                                        </div>
                                    </div>
                                    <div class="small text-success font-weight-bold mb-2 d-none leb-sq-lbl"></div>`,
                                },
                                {
                                    fieldname: "pegado",
                                    fieldtype: "Small Text",
                                    label: "O pega aquí directo desde Excel",
                                },
                            ],
                            primary_action_label: "Importar a la cotización",
                            async primary_action(vals) {
                                const txt = (vals.pegado || "").trim();
                                if (!txt && !fB64) {
                                    frappe.msgprint("Sube un archivo Excel (.xlsx) / CSV o pega las filas.");
                                    return;
                                }
                                const { message: res } = await frappe.call({
                                    method: "lebaref_compras.ux_lebaref.parsear_excel_cotizacion",
                                    type: "POST",
                                    args: {
                                        texto_csv: txt,
                                        filename: fName,
                                        content_base64: fB64,
                                    },
                                    freeze: true,
                                });
                                const lista = (res && res.items) || [];
                                if (!lista.length) {
                                    frappe.msgprint("No se encontraron partidas válidas.");
                                    return;
                                }
                                if (frm.doc.items && frm.doc.items.length === 1 && !frm.doc.items[0].item_code) {
                                    frm.clear_table("items");
                                }
                                for (const it of lista) {
                                    const row = frm.add_child("items");
                                    Object.assign(row, it);
                                }
                                frm.refresh_field("items");
                                d.hide();
                                frappe.show_alert({ message: `${lista.length} partida(s) importadas a la cotización.`, indicator: "green" });
                            },
                        });
                        const w = d.fields_dict._top.$wrapper;
                        w.find(".leb-sq-pick").on("click", () => w.find(".leb-sq-file").trigger("click"));
                        w.find(".leb-sq-file").on("change", function () {
                            const f = this.files && this.files[0];
                            if (!f) return;
                            fName = f.name;
                            const rd = new FileReader();
                            rd.onload = (e) => {
                                fB64 = String(e.target.result || "");
                                w.find(".leb-sq-lbl").removeClass("d-none").text(`Archivo listo: ${fName}`);
                            };
                            rd.readAsDataURL(f);
                        });
                        d.show();
                    }),
                    "upload",
                    "Importar Excel"
                );
            },
        });

        // =====================================================================
        // FASE 3: Smart Defaults (CAL-01) + Poka-Yoke Preventivo (CAL-02)
        // =====================================================================
        function obtenerSmartDefaultsLebaref() {
            if (!window.__lebSmartDefaultsPromise) {
                window.__lebSmartDefaultsPromise = frappe
                    .call({
                        method: "lebaref_compras.ux_lebaref.valores_por_defecto_lebaref",
                        type: "GET",
                    })
                    .then((r) => (r && r.message) || {})
                    .catch(() => ({}));
            }
            return window.__lebSmartDefaultsPromise;
        }

        async function aplicarSmartDefaultsEnFormulario(frm) {
            if (!frm || !frm.is_new() || frm.__lebSmartDefaultsApplied) return;
            frm.__lebSmartDefaultsApplied = true;
            const defs = await obtenerSmartDefaultsLebaref();
            if (!defs || !frm.is_new()) return;

            const ponerSiVacio = (campo, valor) => {
                if (!valor || !frm.fields_dict || !frm.fields_dict[campo]) return;
                const actual = frm.doc[campo];
                if (actual === undefined || actual === null || String(actual).trim() === "") {
                    frm.set_value(campo, valor);
                }
            };

            ponerSiVacio("company", defs.company);

            if (frm.doctype === "Material Request") {
                ponerSiVacio("custom_lebaref_solicitante", defs.user_fullname);
                ponerSiVacio("schedule_date", defs.schedule_date_7d);
                if (!frm.doc.material_request_type || frm.doc.material_request_type === "Purchase") {
                    ponerSiVacio("set_warehouse", defs.warehouse);
                }
            } else if (frm.doctype === "Supplier Quotation") {
                ponerSiVacio("currency", defs.currency || "MXN");
                ponerSiVacio("valid_till", defs.valid_till_30d);
                ponerSiVacio("taxes_and_charges", defs.taxes_and_charges_iva16);
            } else if (frm.doctype === "Purchase Order") {
                ponerSiVacio("currency", defs.currency || "MXN");
                ponerSiVacio("schedule_date", defs.schedule_date_7d);
                ponerSiVacio("set_warehouse", defs.warehouse);
                ponerSiVacio("taxes_and_charges", defs.taxes_and_charges_iva16);
            } else if (frm.doctype === "Purchase Receipt") {
                ponerSiVacio("currency", defs.currency || "MXN");
                ponerSiVacio("set_warehouse", defs.warehouse);
                ponerSiVacio("taxes_and_charges", defs.taxes_and_charges_iva16);
            } else if (frm.doctype === "Purchase Invoice") {
                ponerSiVacio("currency", defs.currency || "MXN");
                ponerSiVacio("taxes_and_charges", defs.taxes_and_charges_iva16);
            } else if (frm.doctype === "Stock Entry") {
                if (frm.doc.purpose === "Material Issue" || frm.doc.purpose === "Material Transfer") {
                    ponerSiVacio("from_warehouse", defs.warehouse);
                }
            } else if (frm.doctype === "Item") {
                ponerSiVacio("stock_uom", defs.default_uom || "Pieza");
                if (frm.doc.is_purchase_item === undefined) {
                    frm.set_value("is_purchase_item", 1);
                }
            }
        }

        async function aplicarSmartDefaultsRenglonItem(frm, cdt, cdn) {
            const row = locals[cdt] && locals[cdt][cdn];
            if (!row || !row.item_code) return;
            const [defs, resItem] = await Promise.all([
                obtenerSmartDefaultsLebaref(),
                frappe
                    .call({
                        method: "lebaref_compras.ux_lebaref.info_rapida_item_default",
                        type: "GET",
                        args: { item_code: row.item_code },
                    })
                    .then((r) => (r && r.message) || {})
                    .catch(() => ({})),
            ]);
            if (!locals[cdt] || !locals[cdt][cdn]) return;
            if (resItem.uom && (!row.uom || row.uom === "Nos")) {
                frappe.model.set_value(cdt, cdn, "uom", resItem.uom);
            }
            if (cdt === "Material Request Item" && !row.schedule_date) {
                const fReq = (frm && frm.doc && frm.doc.schedule_date) || defs.schedule_date_7d;
                if (fReq) frappe.model.set_value(cdt, cdn, "schedule_date", fReq);
            }
            if (Object.prototype.hasOwnProperty.call(row, "warehouse")) {
                if (!resItem.requiere_almacen) {
                    if (cdt === "Material Request Item" || (cdt === "Purchase Invoice Item" && !(frm && frm.doc && frm.doc.update_stock))) {
                        frappe.model.set_value(cdt, cdn, "warehouse", "");
                    }
                } else if (!row.warehouse && defs.warehouse) {
                    if (cdt !== "Purchase Invoice Item" || (frm && frm.doc && frm.doc.update_stock)) {
                        frappe.model.set_value(cdt, cdn, "warehouse", (frm && frm.doc && frm.doc.set_warehouse) || defs.warehouse);
                    }
                }
            }
        }

        function actualizarAvisoUsdDofEnFormulario(frm) {
            if (!frm || !frm.doc) return;
            const $wrap = frm.$wrapper;
            if (!$wrap) return;
            $wrap.find(".leb-poka-usd-banner").remove();
            if (frm.doc.currency !== "USD" || frm.doc.docstatus === 2) return;
            const tc = Number(frm.doc.custom_lebaref_tc_dof || 0);
            if (tc > 0) return;
            const $host = $wrap.find(".form-layout").first();
            if (!$host.length) return;
            $(`
                <div class="leb-poka-usd-banner alert alert-warning py-2 px-3 mb-2" style="font-size:12px;border-left:4px solid #d97706;">
                    <b>Documento en USD:</b> captura el <b>Tipo de Cambio oficial del DOF</b> del día antes de guardar para calcular el equivalente auditable en MXN.
                </div>
            `).prependTo($host);
        }

        async function actualizarAvisoExpedienteFiscalEnOc(frm) {
            if (!frm || frm.doctype !== "Purchase Order" || !frm.$wrapper) return;
            frm.$wrapper.find(".leb-poka-fiscal-banner").remove();
            const prov = (frm.doc && frm.doc.supplier) || "";
            if (!prov || frm.doc.docstatus === 2) return;
            try {
                const { message: ef } = await frappe.call({
                    method: "lebaref_compras.flujo_operativo.estado_fiscal_proveedor",
                    type: "GET",
                    args: { proveedor: prov },
                });
                if (!ef || ef.expediente_completo || (frm.doc && frm.doc.supplier) !== prov) return;
                const esc = frappe.utils.escape_html;
                const $host = frm.$wrapper.find(".form-layout").first();
                if (!$host.length) return;
                const $ban = $(`
                    <div class="leb-poka-fiscal-banner alert alert-warning py-2 px-3 mb-2 d-flex justify-content-between align-items-center flex-wrap" style="font-size:12px;gap:8px;border-left:4px solid #d97706;">
                        <span><b>Prevención antes de solicitar pago a Dirección:</b> al proveedor <b>${esc(ef.nombre || prov)}</b> le falta registrar <b>${esc((ef.faltantes || []).join(" y "))}</b>.</span>
                        <button type="button" class="btn btn-xs btn-default leb-poka-subir-csf">Subir CSF / Carátula</button>
                    </div>
                `).prependTo($host);
                $ban.find(".leb-poka-subir-csf").on("click", () => {
                    abrirModalExpedienteFiscalProveedor(prov, () => actualizarAvisoExpedienteFiscalEnOc(frm));
                });
            } catch (e) {
                // Ignorar si el usuario no tiene permiso sobre Supplier
            }
        }

        function validarPokaYokeDocumentoCompras(frm) {
            if (!frm || !frm.doc) return;
            if (["Supplier Quotation", "Purchase Order", "Purchase Invoice"].includes(frm.doctype)) {
                if (frm.doc.currency === "USD" && !(Number(frm.doc.custom_lebaref_tc_dof || 0) > 0)) {
                    frappe.msgprint({
                        title: "Falta Tipo de Cambio DOF",
                        indicator: "orange",
                        message: "Cuando la moneda es <b>USD</b>, captura primero el <b>Tipo de Cambio oficial del DOF</b> mayor que cero.",
                    });
                    if (frm.fields_dict && frm.fields_dict.custom_lebaref_tc_dof && frm.scroll_to_field) {
                        frm.scroll_to_field("custom_lebaref_tc_dof");
                    }
                    frappe.validated = false;
                    return;
                }
            }

            const items = Array.isArray(frm.doc.items) ? frm.doc.items : [];
            const exigePrecioPositivo = ["Supplier Quotation", "Purchase Order", "Purchase Invoice"].includes(frm.doctype);
            const impidePrecioNegativo = exigePrecioPositivo || frm.doctype === "Purchase Receipt";

            for (const r of items) {
                const cant = Number(r.qty || 0);
                if (!(cant > 0)) {
                    frappe.msgprint({
                        title: "Cantidad inválida",
                        indicator: "red",
                        message: `En el renglón <b>#${r.idx}</b> la cantidad debe ser mayor que cero.`,
                    });
                    frappe.validated = false;
                    return;
                }
                const rate = Number(r.rate || 0);
                if (impidePrecioNegativo && rate < 0) {
                    frappe.msgprint({
                        title: "Precio unitario inválido",
                        indicator: "red",
                        message: `En el renglón <b>#${r.idx}</b> no se permiten precios unitarios negativos.`,
                    });
                    frappe.validated = false;
                    return;
                }
                if (exigePrecioPositivo && !frm.doc.is_return && !(rate > 0)) {
                    frappe.msgprint({
                        title: "Precio unitario en cero",
                        indicator: "orange",
                        message: `En el renglón <b>#${r.idx}</b> captura un precio unitario sin IVA mayor que cero antes de guardar.`,
                    });
                    frappe.validated = false;
                    return;
                }
            }
        }

        frappe.ui.form.on("Material Request", {
            onload(frm) {
                aplicarSmartDefaultsEnFormulario(frm);
            },
            validate(frm) {
                validarPokaYokeDocumentoCompras(frm);
            },
        });

        frappe.ui.form.on("Supplier Quotation", {
            onload(frm) {
                aplicarSmartDefaultsEnFormulario(frm);
            },
            refresh(frm) {
                renderizarBloquesPartidaEnFormulario(frm);
                actualizarAvisoUsdDofEnFormulario(frm);
            },
            currency(frm) {
                actualizarAvisoUsdDofEnFormulario(frm);
            },
            custom_lebaref_tc_dof(frm) {
                actualizarAvisoUsdDofEnFormulario(frm);
            },
            validate(frm) {
                validarPokaYokeDocumentoCompras(frm);
            },
        });

        frappe.ui.form.on("Purchase Order", {
            onload(frm) {
                aplicarSmartDefaultsEnFormulario(frm);
            },
            refresh(frm) {
                renderizarBloquesPartidaEnFormulario(frm);
                actualizarAvisoUsdDofEnFormulario(frm);
                actualizarAvisoExpedienteFiscalEnOc(frm);
            },
            supplier(frm) {
                actualizarAvisoExpedienteFiscalEnOc(frm);
            },
            currency(frm) {
                actualizarAvisoUsdDofEnFormulario(frm);
            },
            custom_lebaref_tc_dof(frm) {
                actualizarAvisoUsdDofEnFormulario(frm);
            },
            validate(frm) {
                validarPokaYokeDocumentoCompras(frm);
            },
        });

        frappe.ui.form.on("Purchase Receipt", {
            onload(frm) {
                aplicarSmartDefaultsEnFormulario(frm);
            },
            refresh(frm) {
                renderizarBloquesPartidaEnFormulario(frm);
            },
            validate(frm) {
                validarPokaYokeDocumentoCompras(frm);
            },
        });

        frappe.ui.form.on("Purchase Invoice", {
            onload(frm) {
                aplicarSmartDefaultsEnFormulario(frm);
            },
            refresh(frm) {
                renderizarBloquesPartidaEnFormulario(frm);
                actualizarAvisoUsdDofEnFormulario(frm);
            },
            currency(frm) {
                actualizarAvisoUsdDofEnFormulario(frm);
            },
            custom_lebaref_tc_dof(frm) {
                actualizarAvisoUsdDofEnFormulario(frm);
            },
            validate(frm) {
                validarPokaYokeDocumentoCompras(frm);
            },
        });

        frappe.ui.form.on("Stock Entry", {
            onload(frm) {
                aplicarSmartDefaultsEnFormulario(frm);
            },
            refresh(frm) {
                renderizarBloquesPartidaEnFormulario(frm);
            },
        });

        frappe.ui.form.on("Item", {
            onload(frm) {
                aplicarSmartDefaultsEnFormulario(frm);
            },
        });

        for (const childDt of [
            "Material Request Item",
            "Supplier Quotation Item",
            "Purchase Order Item",
            "Purchase Receipt Item",
            "Purchase Invoice Item",
        ]) {
            frappe.ui.form.on(childDt, {
                item_code(frm, cdt, cdn) {
                    setTimeout(() => aplicarSmartDefaultsRenglonItem(frm, cdt, cdn), 120);
                },
            });
        }
    }
})();
