(() => {
    const api = "lebaref_compras.comparativa";
    const SHEETS_URL = "https://docs.google.com/spreadsheets/d/1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I/edit?gid=0#gid=0";
    const numFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 4 });
    const moneyFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    frappe.ui.form.on("Material Request", {
        refresh(frm) {
            if (!frm.is_new() && frappe.model.can_write("Material Request")) {
                frm.add_custom_button("✏️ Asignar Nombre a Solicitud", () => abrirDialogoNombreSolicitud(frm), "Lebaref");
            }

            if (frm.doc.docstatus !== 1 || frm.doc.status === "Stopped" || frm.doc.status === "Cancelled") {
                return;
            }

            const abrirOp = (accion, titulo) => {
                if (window.lebaref && window.lebaref.abrirOperacion) {
                    window.lebaref.abrirOperacion(accion, frm.doc.name, titulo);
                }
            };

            if (frm.doc.material_request_type === "Purchase") {
                frm.add_custom_button("Comparativa Lebaref", () => showComparison(frm)).addClass("btn-primary");
                frm.add_custom_button("💰 Capturar Cotización de Proveedor", () =>
                    abrirDialogoCotizacionDirecta(frm, () => frm.reload_doc())
                );
                frm.add_custom_button("Crear anexo vinculado (SOL-03)", () => abrirOp("anexo", "Crear anexo vinculado a " + frm.doc.name), "Lebaref");

                const tieneConceptosLibres = (frm.doc.items || []).some(
                    (r) => r.item_code === "LEBAREF-CONCEPTO-LIBRE" || Boolean(r.custom_lebaref_concepto)
                );
                if (tieneConceptosLibres) {
                    frm.add_custom_button("Convertir concepto libre a SKU (CAT-04)", () => abrirDialogoConvertirConcepto(frm), "Lebaref");
                } else {
                    frm.add_custom_button("Canalizar a surtido de almacén (SOL-05)", () => abrirOp("canalizar_stock", "Canalizar a stock · " + frm.doc.name), "Lebaref");
                }
            } else if (frm.doc.material_request_type === "Material Issue" || frm.doc.material_request_type === "Material Transfer") {
                frm.add_custom_button("Surtir desde almacén (SOL-05)", () => abrirOp("surtir", "Surtir desde almacén · " + frm.doc.name)).addClass("btn-primary");
                frm.add_custom_button("Crear anexo vinculado (SOL-03)", () => abrirOp("anexo", "Crear anexo vinculado a " + frm.doc.name), "Lebaref");
            }
        },
    });

    function abrirDialogoNombreSolicitud(frm) {
        const actual = frm.doc.custom_lebaref_nombre_solicitud || frm.doc.title || "";
        const d = new frappe.ui.Dialog({
            title: `✏️ Nombre / Referencia de la Solicitud · ${frm.doc.name}`,
            fields: [
                {
                    fieldname: "nombre",
                    label: "Nombre descriptivo para ubicar fácilmente esta solicitud",
                    fieldtype: "Data",
                    default: actual,
                    reqd: 1,
                },
            ],
            primary_action_label: "Guardar Nombre",
            async primary_action(vals) {
                await frappe.call({
                    method: "lebaref_compras.ux_lebaref.guardar_nombre_solicitud",
                    type: "POST",
                    args: { solicitud: frm.doc.name, nombre: vals.nombre },
                    freeze: true,
                });
                d.hide();
                frappe.show_alert({ message: "Nombre de solicitud actualizado", indicator: "green" });
                frm.reload_doc();
            },
        });
        d.show();
    }

    async function abrirDialogoCotizacionDirecta(frm, alGuardar = null) {
        const { message: info } = await frappe.call({
            method: "lebaref_compras.ux_lebaref.esquema_cotizacion_directa",
            type: "GET",
            args: { solicitud: frm.doc.name },
            freeze: true,
            freeze_message: "Cargando partidas de la solicitud…",
        });

        const preciosEstado = (info.partidas || []).map((p) => ({
            mr_item: p.mr_item,
            idx: p.idx,
            clave: p.clave,
            descripcion: p.descripcion,
            qty: p.qty,
            uom: p.uom,
            rate: "",
            lead_time_days: "1",
        }));

        const d = new frappe.ui.Dialog({
            title: `💰 Capturar Cotización Directa · ${frm.doc.name}`,
            size: "extra-large",
            fields: [
                {
                    fieldname: "_ayuda",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        Captura directamente los precios unitarios <b>sin IVA</b> del proveedor para esta solicitud (admite ofertas parciales dejando en blanco lo que no cotice). Se integrará de inmediato al <b>Cuadro Comparativo</b>.
                    </div>`,
                },
                {
                    fieldname: "supplier",
                    label: "Proveedor que cotiza",
                    fieldtype: "Select",
                    options: ["", ...(info.proveedores || [])].join("\n"),
                    reqd: 1,
                },
                {
                    fieldname: "taxes_and_charges",
                    label: "Impuestos aplicables",
                    fieldtype: "Select",
                    options: ["", ...(info.impuestos || [])].join("\n"),
                    default: info.default_iva || "",
                },
                {
                    fieldname: "valid_till",
                    label: "Vigente hasta",
                    fieldtype: "Date",
                    default: info.valid_till,
                },
                { fieldtype: "Column Break" },
                {
                    fieldname: "payment_terms_template",
                    label: "Condición de pago",
                    fieldtype: "Select",
                    options: ["", ...(info.pagos || [])].join("\n"),
                    default: "",
                },
                {
                    fieldname: "shipping_rule",
                    label: "Regla de flete (opcional)",
                    fieldtype: "Select",
                    options: ["", ...(info.fletes || [])].join("\n"),
                    default: "",
                },
                {
                    fieldname: "fecha_programada_pago",
                    label: "Fecha programada de pago (opcional)",
                    fieldtype: "Date",
                },
                { fieldtype: "Section Break", label: "Precios Cotizados por Partida" },
                { fieldname: "_tabla_partidas", fieldtype: "HTML" },
            ],
            primary_action_label: "Guardar Cotización de Proveedor",
            async primary_action(vals) {
                const filasConPrecio = preciosEstado.filter((r) => Number(r.rate || 0) > 0);
                if (!filasConPrecio.length) {
                    frappe.msgprint("Captura al menos un precio unitario mayor que cero.");
                    return;
                }
                const btn = d.get_primary_btn().prop("disabled", true);
                try {
                    const { message: res } = await frappe.call({
                        method: "lebaref_compras.ux_lebaref.registrar_cotizacion_directa",
                        type: "POST",
                        args: {
                            solicitud: frm.doc.name,
                            datos: JSON.stringify({
                                supplier: vals.supplier,
                                valid_till: vals.valid_till,
                                taxes_and_charges: vals.taxes_and_charges || "",
                                shipping_rule: vals.shipping_rule || "",
                                payment_terms_template: vals.payment_terms_template || "",
                                fecha_programada_pago: vals.fecha_programada_pago || "",
                                rows: filasConPrecio,
                            }),
                        },
                        freeze: true,
                        freeze_message: "Guardando cotización de proveedor…",
                    });
                    d.hide();
                    frappe.show_alert({
                        message: `Cotización registrada: ${res.folio} (${vals.supplier})`,
                        indicator: "green",
                    });
                    if (typeof alGuardar === "function") alGuardar(res);
                } finally {
                    btn.prop("disabled", false);
                }
            },
        });

        const host = d.fields_dict._tabla_partidas.$wrapper;
        const tbl = $('<table class="table table-bordered table-sm" style="font-size:12px;">').appendTo(
            $('<div class="table-responsive">').appendTo(host)
        );
        $('<thead style="background:#031e34;color:#fff;"><tr><th style="width:36px;">#</th><th style="width:140px;">Código / Clave</th><th>Descripción del Artículo / Concepto</th><th class="text-right" style="width:95px;">Cantidad</th><th style="width:145px;">Precio Unit. s/IVA ($)</th><th style="width:105px;">Entrega (días)</th></tr></thead>').appendTo(tbl);
        const tbody = $("<tbody>").appendTo(tbl);

        for (const p of preciosEstado) {
            const tr = $("<tr>").appendTo(tbody);
            $("<td>").text(p.idx).appendTo(tr);
            $("<td>").html(`<b>${frappe.utils.escape_html(p.clave)}</b>`).appendTo(tr);
            $("<td>").text(p.descripcion).appendTo(tr);
            $("<td class='text-right'>").html(`<b>${numFmt.format(p.qty)} ${frappe.utils.escape_html(p.uom)}</b>`).appendTo(tr);

            const tdRate = $("<td>").appendTo(tr);
            $('<input type="number" step="0.01" min="0" class="form-control form-control-sm" placeholder="0.00 (Sin cotizar)">')
                .on("input", function () {
                    p.rate = $(this).val();
                })
                .appendTo(tdRate);

            const tdDays = $("<td>").appendTo(tr);
            $('<input type="number" step="1" min="0" class="form-control form-control-sm">')
                .val(p.lead_time_days)
                .on("input", function () {
                    p.lead_time_days = $(this).val();
                })
                .appendTo(tdDays);
        }

        d.show();
    }

    function nuevoUuidLocal() {
        if (window.crypto && typeof window.crypto.randomUUID === "function") return window.crypto.randomUUID();
        const bytes = new Uint8Array(16);
        window.crypto.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;
        const hex = [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }

    function abrirDialogoConvertirConcepto(frm) {
        const libres = (frm.doc.items || []).filter(
            (r) => r.item_code === "LEBAREF-CONCEPTO-LIBRE" || Boolean(r.custom_lebaref_concepto)
        );
        if (!libres.length) {
            frappe.msgprint("Esta solicitud no tiene renglones de concepto libre pendientes de convertir.");
            return;
        }
        const opcionesRenglon = libres.map((r) => ({
            value: r.name,
            label: `#${r.idx} · ${r.custom_lebaref_concepto || r.description || r.name} (${r.qty} ${r.uom || ""})`,
        }));
        const tokenOp = nuevoUuidLocal();

        const dlg = new frappe.ui.Dialog({
            title: `Convertir concepto libre a SKU inventariable · ${frm.doc.name}`,
            size: "large",
            fields: [
                {
                    fieldname: "_nota",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        Esta acción reemplaza el concepto libre por un SKU con control de stock mediante una enmienda trazable (cancela el original y confirma la versión enmendada). Debe ejecutarse antes de emitir peticiones u órdenes activas.
                    </div>`,
                },
                {
                    fieldname: "renglon",
                    label: "Renglón de concepto libre",
                    fieldtype: "Select",
                    options: opcionesRenglon,
                    default: opcionesRenglon[0].value,
                    reqd: 1,
                },
                {
                    fieldname: "modo_sku",
                    label: "¿Cómo deseas asignar el SKU?",
                    fieldtype: "Select",
                    options: "Seleccionar SKU existente del catálogo\nCrear nuevo SKU inventariable ahora",
                    default: "Seleccionar SKU existente del catálogo",
                    reqd: 1,
                },
                {
                    fieldname: "item_code",
                    label: "SKU existente",
                    fieldtype: "Link",
                    options: "Item",
                    depends_on: "eval:doc.modo_sku === 'Seleccionar SKU existente del catálogo'",
                },
                {
                    fieldname: "uom",
                    label: "Unidad de compra (opcional)",
                    fieldtype: "Link",
                    options: "UOM",
                    depends_on: "eval:doc.modo_sku === 'Seleccionar SKU existente del catálogo'",
                },
                {
                    fieldname: "new_item_code",
                    label: "Código único del nuevo SKU",
                    fieldtype: "Data",
                    depends_on: "eval:doc.modo_sku === 'Crear nuevo SKU inventariable ahora'",
                },
                {
                    fieldname: "new_item_name",
                    label: "Nombre descriptivo del artículo",
                    fieldtype: "Data",
                    depends_on: "eval:doc.modo_sku === 'Crear nuevo SKU inventariable ahora'",
                },
                {
                    fieldname: "new_item_group",
                    label: "Grupo de artículos",
                    fieldtype: "Link",
                    options: "Item Group",
                    depends_on: "eval:doc.modo_sku === 'Crear nuevo SKU inventariable ahora'",
                },
                {
                    fieldname: "new_stock_uom",
                    label: "Unidad de inventario",
                    fieldtype: "Link",
                    options: "UOM",
                    default: "Nos",
                    depends_on: "eval:doc.modo_sku === 'Crear nuevo SKU inventariable ahora'",
                },
                {
                    fieldname: "warehouse",
                    label: "Almacén destino del renglón",
                    fieldtype: "Link",
                    options: "Warehouse",
                    get_query: () => ({ filters: { company: frm.doc.company, is_group: 0, disabled: 0 } }),
                },
            ],
            primary_action_label: "Convertir y Confirmar Enmienda",
            async primary_action(vals) {
                const payload = { renglon: vals.renglon };
                if (vals.warehouse) payload.warehouse = vals.warehouse;
                if (vals.modo_sku === "Seleccionar SKU existente del catálogo") {
                    if (!vals.item_code) {
                        frappe.msgprint("Selecciona un SKU existente del catálogo.");
                        return;
                    }
                    payload.item_code = vals.item_code;
                    if (vals.uom) payload.uom = vals.uom;
                } else {
                    if (!vals.new_item_code || !vals.new_item_name || !vals.new_item_group || !vals.new_stock_uom) {
                        frappe.msgprint("Completa Código, Nombre, Grupo y Unidad para el nuevo SKU.");
                        return;
                    }
                    payload.articulo = {
                        item_code: vals.new_item_code,
                        item_name: vals.new_item_name,
                        item_group: vals.new_item_group,
                        stock_uom: vals.new_stock_uom,
                    };
                }

                const btn = dlg.get_primary_btn().prop("disabled", true);
                try {
                    const { message: res } = await frappe.call({
                        method: "lebaref_compras.operaciones.ejecutar",
                        type: "POST",
                        args: {
                            accion: "convertir_concepto",
                            folio: frm.doc.name,
                            operacion: tokenOp,
                            datos: JSON.stringify(payload),
                        },
                        freeze: true,
                        freeze_message: "Generando enmienda con SKU inventariable…",
                    });
                    dlg.hide();
                    frappe.show_alert({ message: `Concepto convertido en ${res.folio}`, indicator: "green" });
                    frappe.set_route("Form", "Material Request", res.folio);
                } finally {
                    btn.prop("disabled", false);
                }
            },
        });
        dlg.show();
    }

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

        // Recolectar proveedores únicos y el resumen comercial de su cotización vigente (COM-04 / COM-07)
        const suppliers = [];
        const supplierMeta = {};
        for (const line of comparison.renglones) {
            for (const opt of line.opciones || []) {
                if (!supplierMeta[opt.proveedor]) {
                    suppliers.push(opt.proveedor);
                    supplierMeta[opt.proveedor] = {
                        cotizacion: opt.cotizacion,
                        moneda: opt.moneda || "MXN",
                        comercial: opt.comercial || null,
                    };
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

        const toolbar = $(`
            <div class="d-flex justify-content-between align-items-center mb-3 flex-wrap" style="gap:8px;padding:8px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;">
                <div class="small text-muted">
                    <b>Renglones:</b> ${comparison.renglones.length} &nbsp;|&nbsp;
                    <b>Proveedores con oferta:</b> ${suppliers.length}
                </div>
                <div class="d-flex flex-wrap" style="gap:6px;">
                    <button type="button" class="btn btn-xs btn-primary leb-btn-add-quote">➕ Capturar Cotización de Proveedor</button>
                    <button type="button" class="btn btn-xs btn-default leb-btn-dir">📊 Proveedores por Especialidad</button>
                    <button type="button" class="btn btn-xs btn-default leb-btn-print">🖨️ Imprimir Comparativa Carta</button>
                    <button type="button" class="btn btn-xs btn-secondary leb-btn-best">★ Elegir Mejor Precio en Todo</button>
                </div>
            </div>
        `).appendTo(wrapper);

        toolbar.find(".leb-btn-add-quote").on("click", () => {
            dialog.hide();
            abrirDialogoCotizacionDirecta(frm, () => showComparison(frm));
        });

        toolbar.find(".leb-btn-dir").on("click", () => {
            if (window.lebaref && window.lebaref.abrirDirectorioProveedores) {
                window.lebaref.abrirDirectorioProveedores();
            } else {
                window.open(SHEETS_URL, "_blank", "noopener");
            }
        });

        const radios = [];
        const bestRadios = [];

        if (!suppliers.length) {
            const emptyBox = $(`
                <div class="alert alert-warning mb-2">
                    Aún no hay ofertas registradas para <b>${frappe.utils.escape_html(frm.doc.name)}</b>.<br>
                    Haz clic en <b>➕ Capturar Cotización de Proveedor</b> aquí arriba para ingresar precios unitarios, IVA, flete y condiciones de pago directamente sobre esta solicitud.
                </div>
            `).appendTo(wrapper);
            void emptyBox;
        } else {
            const tblWrap = $('<div class="table-responsive">').appendTo(wrapper);
            const table = $('<table class="table table-bordered table-sm" style="font-size:12px;">').appendTo(tblWrap);
            const thead = $('<thead style="background:#031e34;color:#fff;">').appendTo(table);
            const hRow = $("<tr>").appendTo(thead);
            hRow.append("<th>Artículo / Concepto</th><th class='text-right'>Cant.</th>");

            for (const sup of suppliers) {
                const th = $("<th>").appendTo(hRow);
                $("<div>").css("font-weight", "700").text(sup).appendTo(th);
                const meta = supplierMeta[sup];
                const com = meta && meta.comercial;
                if (com) {
                    const tags = [];
                    if (com.taxes_and_charges) tags.push(com.taxes_and_charges);
                    if (com.shipping_rule) tags.push(com.shipping_rule);
                    if (com.payment_terms_template) tags.push(com.payment_terms_template);
                    if (tags.length) {
                        $("<div style='font-size:10.5px;font-weight:400;color:#cbd5e1;margin-top:2px;'>")
                            .text(tags.join(" · "))
                            .appendTo(th);
                    }
                    $("<div style='font-size:10.5px;font-weight:600;color:#93c5fd;margin-top:1px;'>")
                        .text(`Cotización completa: Neto $${moneyFmt.format(com.neto || 0)} | Total $${moneyFmt.format(com.total || 0)} ${meta.moneda}`)
                        .appendTo(th);
                }
            }
            hRow.append("<th>Estado</th>");

            const tbody = $("<tbody>").appendTo(table);

            for (const line of comparison.renglones) {
                const tr = $("<tr>").appendTo(tbody);
                const subInfo = [line.codigo || "CONCEPTO LIBRE", line.obra, line.partida].filter(Boolean).join(" · ");
                const tdArt = $("<td>").appendTo(tr);
                $("<div>").css("font-weight", "600").text(line.articulo).appendTo(tdArt);
                if (subInfo) $("<div class='text-muted small'>").text(subInfo).appendTo(tdArt);

                $("<td class='text-right' style='white-space:nowrap;font-weight:600;'>")
                    .text(`${numFmt.format(line.cantidad)} ${line.unidad || ""}`)
                    .appendTo(tr);

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
                    const com = opt.comercial || null;
                    const label = $("<label class='d-flex align-items-start mb-0' style='gap:6px;cursor:pointer;font-weight:normal;'>").appendTo(td);
                    const input = $("<input>").attr({
                        type: "radio",
                        name: "lebaref-" + line.id,
                    }).prop("disabled", line.ordenado || !comparison.puede_generar).appendTo(label);

                    const box = $("<div>").appendTo(label);
                    const priceLine = $("<div>").css("font-weight", "700")
                        .text(`$${moneyFmt.format(opt.precio)} ${opt.moneda} / u. (s/IVA)`)
                        .appendTo(box);
                    if (isBest) {
                        $("<span class='badge badge-success ml-1'>").text("★ Mejor").appendTo(priceLine);
                    }
                    $("<div class='small text-muted'>")
                        .text(`Subtotal partida: $${moneyFmt.format(subtotal)} · ${opt.entrega ? opt.entrega + " d" : "S/plazo"}`)
                        .appendTo(box);

                    if (com && (com.impuestos_y_cargos || com.payment_terms_template || com.fecha_programada_pago)) {
                        const extraParts = [];
                        if (com.impuestos_y_cargos) {
                            extraParts.push(`+Imp/Flete cot.: $${moneyFmt.format(com.impuestos_y_cargos)}`);
                        }
                        if (com.payment_terms_template) {
                            extraParts.push(com.payment_terms_template);
                        }
                        if (com.fecha_programada_pago) {
                            extraParts.push(`Prog: ${com.fecha_programada_pago}`);
                        }
                        $("<div class='small' style='color:#0d5b94;font-size:10.5px;'>")
                            .text(extraParts.join(" · "))
                            .appendTo(box);
                    }

                    if (!line.ordenado) {
                        radios.push({
                            input,
                            line: line.id,
                            option: opt.id,
                            proveedor: sup,
                            subtotal,
                            moneda: opt.moneda,
                            comercial: com,
                        });
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
            const conds = {};
            for (const c of chosen) {
                totals[c.proveedor] = (totals[c.proveedor] || 0) + c.subtotal;
                if (!conds[c.proveedor] && c.comercial) {
                    const tags = [c.comercial.taxes_and_charges, c.comercial.shipping_rule, c.comercial.payment_terms_template].filter(Boolean);
                    conds[c.proveedor] = tags.length ? ` (${tags.join(", ")})` : "";
                }
            }
            const parts = Object.entries(totals).map(
                ([prov, sum]) => `${prov}: Neto $${moneyFmt.format(sum)}${conds[prov] || ""}`
            );
            summaryBox.html(
                `<b>Adjudicación seleccionada (${chosen.length} renglón/es — ERPNext aplicará automáticamente IVA, flete y vencimientos en cada OC):</b><br>` +
                parts.map((p) => frappe.utils.escape_html(p)).join(" &nbsp;|&nbsp; ")
            );
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

        toolbar.find(".leb-btn-print").on("click", () => printComparisonCarta(frm.doc.name, comparison, suppliers, supplierMeta));

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
                        freeze_message: "Generando órdenes de compra con impuestos, flete y programación de pago…",
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

    function printComparisonCarta(folio, comparison, suppliers, supplierMeta) {
        const esc = frappe.utils.escape_html;
        const headCols = suppliers.map((s) => `<th>${esc(s)}</th>`).join("");
        const bodyRows = comparison.renglones.map((line) => {
            const cells = suppliers.map((sup) => {
                const opt = (line.opciones || []).find((o) => o.proveedor === sup);
                if (!opt) return `<td style="color:#64748b;">Sin cotizar</td>`;
                const sub = opt.precio * line.cantidad;
                return `<td><b>$${moneyFmt.format(opt.precio)} ${esc(opt.moneda)}</b><br><small>Sub: $${moneyFmt.format(sub)} · ${opt.entrega || 0} días</small></td>`;
            }).join("");
            return `<tr>
                <td><b>${esc(line.articulo)}</b><br><small>${esc([line.codigo || "LIBRE", line.obra, line.partida].filter(Boolean).join(" · "))}</small></td>
                <td style="text-align:right;">${numFmt.format(line.cantidad)} ${esc(line.unidad || "")}</td>
                ${cells}
            </tr>`;
        }).join("");

        const comRows = suppliers.map((sup) => {
            const meta = (supplierMeta && supplierMeta[sup]) || {};
            const com = meta.comercial || {};
            const desglose = (com.cargos || []).map((c) => `${c.descripcion}: $${moneyFmt.format(c.importe || 0)}`).join(" | ") || "Sin cargos adicionales";
            return `<tr>
                <td><b>${esc(sup)}</b> (${esc(meta.cotizacion || "")})</td>
                <td style="text-align:right;">$${moneyFmt.format(com.neto || 0)}</td>
                <td style="text-align:right;">$${moneyFmt.format(com.impuestos_y_cargos || 0)}<br><small>${esc(desglose)}</small></td>
                <td style="text-align:right;"><b>$${moneyFmt.format(com.total || 0)} ${esc(meta.moneda || "MXN")}</b></td>
                <td>${esc(com.payment_terms_template || "Contado / S/dato")}${com.fecha_programada_pago ? "<br><small>Prog: " + esc(String(com.fecha_programada_pago)) + "</small>" : ""}</td>
            </tr>`;
        }).join("");

        const w = window.open("", "_blank");
        if (!w) return;
        w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Comparativa ${esc(folio)}</title>
            <style>
                @page { size: letter landscape; margin: 12mm; }
                body { font-family: 'Segoe UI', Arial, sans-serif; color:#0f172a; font-size:11px; }
                .hdr { display:flex; justify-content:space-between; align-items:center; border-bottom:2.5px solid #031e34; padding-bottom:8px; margin-bottom:10px; }
                .hdr-left { display:flex; align-items:center; gap:14px; }
                .hdr-title { border-left:1.5px solid #cbd5e1; padding-left:12px; }
                h2 { margin:0 0 2px 0; font-size:15px; color:#0d5b94; text-transform:uppercase; }
                h3 { margin:14px 0 4px 0; font-size:12px; color:#031e34; text-transform:uppercase; }
                table { width:100%; border-collapse:collapse; margin-top:6px; }
                th { background:#031e34; color:#fff; padding:6px; text-align:left; border:1px solid #031e34; }
                td { padding:6px; border:1px solid #cbd5e1; vertical-align:top; }
            </style></head><body>
            <div class="hdr">
                <div class="hdr-left">
                    <img src="/files/lebaref_logo_horizontal.svg" alt="Lebaref" style="height:56px;width:auto;display:block;">
                    <div class="hdr-title">
                        <h2>Cuadro Comparativo de Cotizaciones (${esc(folio)})</h2>
                        <div style="color:#475569;">Fecha de impresión: ${new Date().toLocaleString("es-MX")}</div>
                    </div>
                </div>
            </div>
            <table><thead><tr><th>Artículo / Concepto</th><th>Cantidad</th>${headCols}</tr></thead><tbody>${bodyRows}</tbody></table>
            ${suppliers.length ? `<h3>Resumen Comercial de Ofertas Completas (Subtotal, IVA, Flete y Condiciones de Pago)</h3>
            <table><thead><tr><th>Proveedor / Cotización</th><th style="text-align:right;">Subtotal Neto</th><th style="text-align:right;">Impuestos y Flete</th><th style="text-align:right;">Total Cotizado</th><th>Condición y Prog. Pago</th></tr></thead><tbody>${comRows}</tbody></table>` : ""}
            <script>window.onload = () => window.print();<\/script>
            </body></html>`);
        w.document.close();
    }

    function showOrders(orders) {
        const list = $("<ul>");
        const result = new frappe.ui.Dialog({
            title: "Órdenes creadas en borrador (con impuestos, flete y vencimientos heredados)",
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
