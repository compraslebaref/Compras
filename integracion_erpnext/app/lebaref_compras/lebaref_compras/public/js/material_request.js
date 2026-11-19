(() => {
    const api = "lebaref_compras.comparativa";
    const SHEETS_URL = "https://docs.google.com/spreadsheets/d/1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I/edit?gid=0#gid=0";
    const numFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 4 });
    const moneyFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    frappe.ui.form.on("Material Request", {
        refresh(frm) {
            if (frm.doc.docstatus === 1 && frm.doc.material_request_type === "Purchase" && frm.doc.status !== "Stopped") {
                frm.add_custom_button("Comparativa Lebaref", () => showComparison(frm)).addClass("btn-primary");
                frm.add_custom_button("Pedir cotización", () => {
                    if (window.lebaref && window.lebaref.abrirOperacion) {
                        window.lebaref.abrirOperacion("peticion", frm.doc.name, "Pedir cotización de " + frm.doc.name);
                    }
                }, "Lebaref");
                frm.add_custom_button("¿A quién cotizar? (Sheets)", () => window.open(SHEETS_URL, "_blank", "noopener"), "Lebaref");
            }
        },
    });

    async function showComparison(frm) {
        if (frm.is_dirty()) {
            frappe.msgprint("Guarda la solicitud antes de abrir la comparativa.");
            return;
        }

        const { message: comparison } = await frappe.call({
            method: api + ".consultar",
            type: "GET",
            args: { solicitud: frm.doc.name },
        });

        // Recolectar columnas únicas de proveedores que cotizaron al menos un renglón
        const suppliers = [];
        const seenSup = new Set();
        for (const line of comparison.renglones) {
            for (const opt of line.opciones || []) {
                if (!seenSup.has(opt.proveedor)) {
                    seenSup.add(opt.proveedor);
                    suppliers.push(opt.proveedor);
                }
            }
        }

        const dialog = new frappe.ui.Dialog({
            title: `Cuadro Comparativo Lebaref · ${frm.doc.name}`,
            size: "extra-large",
            fields: [{ fieldname: "comparativa", fieldtype: "HTML" }],
        });

        const wrapper = dialog.fields_dict.comparativa.$wrapper;
        wrapper.empty();

        // Barra superior de herramientas dentro del cuadro comparativo
        const toolbar = $(`
            <div class="d-flex justify-content-between align-items-center mb-3 flex-wrap" style="gap:8px;padding:8px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;">
                <div class="small text-muted">
                    <b>Renglones:</b> ${comparison.renglones.length} &nbsp;|&nbsp;
                    <b>Proveedores con oferta:</b> ${suppliers.length}
                </div>
                <div class="d-flex" style="gap:6px;">
                    <button type="button" class="btn btn-xs btn-default leb-btn-sheets">🌐 ¿A quién cotizar? (Sheets)</button>
                    <button type="button" class="btn btn-xs btn-default leb-btn-print">🖨️ Imprimir Comparativa Carta</button>
                    <button type="button" class="btn btn-xs btn-secondary leb-btn-best">★ Elegir Mejor Precio en Todo</button>
                </div>
            </div>
        `).appendTo(wrapper);

        toolbar.find(".leb-btn-sheets").on("click", () => window.open(SHEETS_URL, "_blank", "noopener"));

        const radios = [];
        const bestRadios = [];

        if (!suppliers.length) {
            wrapper.append(`
                <div class="alert alert-warning mb-2">
                    Aún no hay ofertas vigentes registradas para <b>${frappe.utils.escape_html(frm.doc.name)}</b>.<br>
                    1. Usa <b>Lebaref → Pedir cotización</b> para invitar proveedores.<br>
                    2. En la Petición de Cotización (<code>PUR-RFQ-...</code>), haz clic en <b>Registrar oferta de proveedor</b> (admite ofertas parciales).
                </div>
            `);
        } else {
            const tblWrap = $('<div class="table-responsive">').appendTo(wrapper);
            const table = $('<table class="table table-bordered table-sm" style="font-size:12px;">').appendTo(tblWrap);
            const thead = $('<thead style="background:#0f172a;color:#fff;">').appendTo(table);
            const hRow = $("<tr>").appendTo(thead);
            hRow.append("<th>Artículo / Concepto</th><th class='text-right'>Cant.</th>");
            for (const sup of suppliers) {
                $("<th>").text(sup).appendTo(hRow);
            }
            hRow.append("<th>Estado</th>");

            const tbody = $("<tbody>").appendTo(table);

            for (const line of comparison.renglones) {
                const tr = $("<tr>").appendTo(tbody);
                const subInfo = [line.codigo, line.obra, line.partida].filter(Boolean).join(" · ");
                const tdArt = $("<td>").appendTo(tr);
                $("<div>").css("font-weight", "600").text(line.articulo).appendTo(tdArt);
                if (subInfo) $("<div class='text-muted small'>").text(subInfo).appendTo(tdArt);

                $("<td class='text-right' style='white-space:nowrap;font-weight:600;'>")
                    .text(`${numFmt.format(line.cantidad)} ${line.unidad || ""}`)
                    .appendTo(tr);

                // Encontrar menor precio del renglón
                let minPrice = Infinity;
                for (const opt of line.opciones || []) {
                    if (opt.precio > 0 && opt.precio < minPrice) minPrice = opt.precio;
                }

                let bestInputForRow = null;

                for (const sup of suppliers) {
                    const td = $("<td>").appendTo(tr);
                    const opt = (line.opciones || []).find((o) => o.proveedor === sup);
                    if (!opt) {
                        td.css({ color: "#94a3b8", background: "#f8fafc" }).text("Sin cotizar");
                        continue;
                    }

                    const isBest = opt.precio === minPrice;
                    if (isBest) td.css("background", "#ecfdf5");

                    const subtotal = opt.precio * line.cantidad;
                    const label = $("<label class='d-flex align-items-start mb-0' style='gap:6px;cursor:pointer;font-weight:normal;'>").appendTo(td);
                    const input = $("<input>").attr({
                        type: "radio",
                        name: "lebaref-" + line.id,
                    }).prop("disabled", line.ordenado || !comparison.puede_generar).appendTo(label);

                    const box = $("<div>").appendTo(label);
                    const priceLine = $("<div>").css("font-weight", "700")
                        .text(`${moneyFmt.format(opt.precio)} ${opt.moneda} / u.`)
                        .appendTo(box);
                    if (isBest) {
                        $("<span class='badge badge-success ml-1'>").text("★ Mejor").appendTo(priceLine);
                    }
                    $("<div class='small text-muted'>")
                        .text(`Importe: ${moneyFmt.format(subtotal)} ${opt.moneda} · ${opt.entrega ? opt.entrega + " d" : "S/plazo"}`)
                        .appendTo(box);

                    if (!line.ordenado) {
                        radios.push({ input, line: line.id, option: opt.id, proveedor: sup, subtotal, moneda: opt.moneda });
                        if (isBest && !bestInputForRow) bestInputForRow = input;
                    }
                }

                if (bestInputForRow) bestRadios.push(bestInputForRow);

                const tdEst = $("<td>").appendTo(tr);
                if (line.ordenado) {
                    $("<span class='badge badge-info'>").text("OC generada").appendTo(tdEst);
                } else if (!(line.opciones || []).length) {
                    $("<span class='badge badge-warning'>").text("Sin oferta").appendTo(tdEst);
                } else {
                    $("<span class='badge badge-light'>").text("Por adjudicar").appendTo(tdEst);
                }
            }
        }

        const summaryBox = $('<div class="mt-2 p-2" style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:6px;font-size:12px;">')
            .text("Selecciona un proveedor en cada renglón que desees adjudicar.")
            .appendTo(wrapper);

        function updateSummary() {
            const chosen = radios.filter((r) => r.input.prop("checked"));
            if (!chosen.length) {
                summaryBox.text("Selecciona al menos un renglón cotizado para previsualizar las órdenes a generar.");
                if (comparison.puede_generar && dialog.get_primary_btn()) {
                    dialog.get_primary_btn().prop("disabled", true);
                }
                return;
            }
            const totals = {};
            for (const c of chosen) {
                totals[c.proveedor] = (totals[c.proveedor] || 0) + c.subtotal;
            }
            const parts = Object.entries(totals).map(([prov, sum]) => `${prov}: $${moneyFmt.format(sum)}`);
            summaryBox.html(`<b>Adjudicación seleccionada (${chosen.length} renglón/es, neto s/impuestos):</b> ` +
                parts.map((p) => frappe.utils.escape_html(p)).join(" &nbsp;|&nbsp; "));
            if (comparison.puede_generar && dialog.get_primary_btn()) {
                dialog.get_primary_btn().prop("disabled", false);
            }
        }

        for (const r of radios) {
            r.input.on("change", updateSummary);
        }

        toolbar.find(".leb-btn-best").on("click", () => {
            for (const input of bestRadios) {
                input.prop("checked", true);
            }
            updateSummary();
        });

        toolbar.find(".leb-btn-print").on("click", () => printComparisonCarta(frm.doc.name, comparison, suppliers));

        if (comparison.puede_generar && radios.length) {
            dialog.set_primary_action("Generar órdenes en borrador", async () => {
                const selection = Object.fromEntries(radios
                    .filter(({ input }) => input.prop("checked"))
                    .map(({ line, option }) => [line, option]));
                if (!Object.keys(selection).length) {
                    frappe.msgprint("Selecciona al menos un proveedor.");
                    return;
                }
                const button = dialog.get_primary_btn().prop("disabled", true);
                try {
                    const { message: orders } = await frappe.call({
                        method: api + ".generar_ordenes",
                        args: { solicitud: frm.doc.name, seleccion: selection },
                        freeze: true,
                        freeze_message: "Generando órdenes de compra en borrador…",
                    });
                    dialog.hide();
                    showOrders(orders);
                    frm.reload_doc();
                } finally {
                    button.prop("disabled", false);
                }
            });
            dialog.get_primary_btn().prop("disabled", true);
        }

        dialog.show();
    }

    function printComparisonCarta(folio, comparison, suppliers) {
        const esc = frappe.utils.escape_html;
        let headCols = suppliers.map((s) => `<th>${esc(s)}</th>`).join("");
        let bodyRows = comparison.renglones.map((line) => {
            let cells = suppliers.map((sup) => {
                const opt = (line.opciones || []).find((o) => o.proveedor === sup);
                if (!opt) return `<td style="color:#64748b;">Sin cotizar</td>`;
                return `<td><b>$${moneyFmt.format(opt.precio)} ${esc(opt.moneda)}</b><br><small>${opt.entrega || 0} días</small></td>`;
            }).join("");
            return `<tr>
                <td><b>${esc(line.articulo)}</b><br><small>${esc([line.codigo, line.obra, line.partida].filter(Boolean).join(" · "))}</small></td>
                <td style="text-align:right;">${numFmt.format(line.cantidad)} ${esc(line.unidad || "")}</td>
                ${cells}
            </tr>`;
        }).join("");

        const w = window.open("", "_blank");
        if (!w) return;
        w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Comparativa ${esc(folio)}</title>
            <style>
                @page { size: letter landscape; margin: 12mm; }
                body { font-family: 'Segoe UI', Arial, sans-serif; color:#0f172a; font-size:11px; }
                h2 { margin:0 0 4px 0; }
                table { width:100%; border-collapse:collapse; margin-top:10px; }
                th { background:#0f172a; color:#fff; padding:6px; text-align:left; border:1px solid #0f172a; }
                td { padding:6px; border:1px solid #cbd5e1; vertical-align:top; }
            </style></head><body>
            <h2>LEBAREF · Cuadro Comparativo de Cotizaciones (${esc(folio)})</h2>
            <div>Fecha de impresión: ${new Date().toLocaleString("es-MX")}</div>
            <table><thead><tr><th>Artículo / Concepto</th><th>Cantidad</th>${headCols}</tr></thead><tbody>${bodyRows}</tbody></table>
            <script>window.onload = () => window.print();<\/script>
            </body></html>`);
        w.document.close();
    }

    function showOrders(orders) {
        const list = $("<ul>");
        const result = new frappe.ui.Dialog({
            title: "Órdenes creadas en borrador (pendientes de Autorización)",
            fields: [{ fieldname: "ordenes", fieldtype: "HTML" }],
        });
        for (const order of orders) {
            $("<button>").attr("type", "button").addClass("btn btn-link p-0")
                .text(order.folio + " · " + order.proveedor)
                .on("click", () => {
                    result.hide();
                    frappe.set_route("Form", "Purchase Order", order.folio);
                })
                .appendTo($("<li>").appendTo(list));
        }
        result.fields_dict.ordenes.$wrapper.append(list);
        result.show();
    }
})();
