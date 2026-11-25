(() => {
    if (window.__lebarefMaterialRequestJsLoaded) return;
    window.__lebarefMaterialRequestJsLoaded = true;
    const api = "lebaref_compras.comparativa";
    const SHEETS_URL = "https://docs.google.com/spreadsheets/d/1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I/edit?gid=0#gid=0";
    const numFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 4 });
    const moneyFmt = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    function decorarBtnIcono($btn, iconName, label) {
        if (!$btn || !$btn.length) return $btn;
        const ic = (frappe.utils && typeof frappe.utils.icon === "function") ? frappe.utils.icon(iconName, "xs") : "";
        $btn.html(`<span class="d-inline-flex align-items-center" style="gap:5px;">${ic}<span>${frappe.utils.escape_html(label)}</span></span>`);
        return $btn;
    }

    function decodificarHtmlLocal(str) {
        if (!str) return "";
        const txt = document.createElement("textarea");
        txt.innerHTML = String(str);
        return txt.value;
    }

    function agruparFilasPorPartidaLocal(lista, obtenerPartida) {
        const arr = Array.isArray(lista) ? lista : [];
        const hayPartida = arr.some((it) => decodificarHtmlLocal(obtenerPartida(it) || "").trim() !== "");
        if (!hayPartida) {
            return [{ partida: "", items: arr }];
        }
        const orden = [];
        const mapa = new Map();
        for (const it of arr) {
            const p = decodificarHtmlLocal(obtenerPartida(it) || "").trim() || "Partida general";
            if (!mapa.has(p)) {
                mapa.set(p, []);
                orden.push(p);
            }
            mapa.get(p).push(it);
        }
        return orden.map((p) => ({ partida: p, items: mapa.get(p) }));
    }

    frappe.ui.form.on("Material Request", {
        async refresh(frm) {
            if (window.lebaref && typeof window.lebaref.renderizarBloquesPartidaEnFormulario === "function") {
                window.lebaref.renderizarBloquesPartidaEnFormulario(frm);
            }
            if (!frm.is_new() && frm.doc.docstatus !== 2) {
                if (Number(frm.doc.custom_lebaref_concluida || 0) === 1) {
                    frm.page.set_indicator("Concluida", "green");
                } else if ((frm.doc.custom_lebaref_en_espera_de || "").trim()) {
                    frm.page.set_indicator("En espera", "purple");
                    frm.dashboard.set_headline_alert(
                        `En espera de: <b>${frappe.utils.escape_html(frm.doc.custom_lebaref_en_espera_de)}</b>`,
                        "purple"
                    );
                }
            }
            if (!frm.is_new() && frm.doc.docstatus < 2) {
                decorarBtnIcono(
                    frm.add_custom_button("Copiar para WhatsApp", () => {
                        if (window.lebaref && window.lebaref.copiarSolicitudParaWhatsapp) {
                            const filas = (frm.doc.items || []).map((r) => ({
                                qty: r.qty,
                                uom: r.uom || r.stock_uom || "Pieza",
                                partida: r.custom_lebaref_partida || "",
                                descripcion: r.custom_lebaref_descripcion_vista || r.custom_lebaref_concepto || r.item_name || r.description || r.item_code || "",
                            }));
                            window.lebaref.copiarSolicitudParaWhatsapp(frm.doc.name, filas);
                        }
                    }),
                    "share",
                    "Copiar para WhatsApp"
                );
            }
            if (!frm.is_new() && frappe.model.can_write("Material Request")) {
                frm.add_custom_button("Renombrar solicitud", () => abrirDialogoNombreSolicitud(frm), "Más acciones");
            }

            if (!frm.is_new() && frm.doc.docstatus < 2 && frm.doc.status !== "Stopped" && frm.doc.status !== "Cancelled") {
                try {
                    const { message: estEd } = await frappe.call({
                        method: "lebaref_compras.ux_lebaref.estado_edicion_solicitud",
                        type: "GET",
                        args: { solicitud: frm.doc.name },
                    });
                    if (estEd && estEd.puede_modificar && window.lebaref) {
                        const grupoEd = frm.doc.docstatus === 0 ? undefined : "Más acciones";
                        const btnXlsx = frm.add_custom_button("Importar Excel", () => {
                            if (window.lebaref.abrirCargaExcelSolicitudExistente) {
                                window.lebaref.abrirCargaExcelSolicitudExistente(frm.doc.name, () => frm.reload_doc());
                            }
                        }, grupoEd);
                        const btnPart = frm.add_custom_button("Editar partidas", () => {
                            if (window.lebaref.abrirEditorPartidasSolicitud) {
                                window.lebaref.abrirEditorPartidasSolicitud(frm.doc.name, () => frm.reload_doc());
                            }
                        }, grupoEd);
                        if (!grupoEd) {
                            decorarBtnIcono(btnXlsx, "upload", "Importar Excel");
                            decorarBtnIcono(btnPart, "edit", "Editar partidas");
                        }
                    }
                } catch (e) {
                    // Continuar sin botones de edición extendida si no hay permiso
                }
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
                decorarBtnIcono(
                    frm.add_custom_button("Cuadro comparativo", () => showComparison(frm)).addClass("btn-primary"),
                    "table",
                    "Cuadro comparativo"
                );
                decorarBtnIcono(
                    frm.add_custom_button("Capturar cotización", () =>
                        abrirDialogoCotizacionDirecta(frm, () => frm.reload_doc())
                    ),
                    "file-add",
                    "Capturar cotización"
                );
                frm.add_custom_button("Crear anexo", () => abrirOp("anexo", "Crear anexo vinculado a " + frm.doc.name), "Más acciones");
                frm.add_custom_button("En espera de cliente (Presupuesto)", async () => {
                    await frappe.call({
                        method: "lebaref_compras.flujo_operativo.actualizar_estado_pago_logistica",
                        type: "POST",
                        args: {
                            doctype: "Material Request",
                            folio: frm.doc.name,
                            nuevo_estado: "En espera de aprobación de cliente",
                        },
                        freeze: true,
                    });
                    frappe.show_alert({ message: "Marcada en espera de aprobación de cliente", indicator: "purple" });
                    frm.reload_doc();
                }, "Más acciones");
                if (window.lebaref && window.lebaref.abrirHiloConductor) {
                    frm.add_custom_button("Hilo conductor conciliado", () => window.lebaref.abrirHiloConductor("Material Request", frm.doc.name), "Más acciones");
                }
                if (window.lebaref && window.lebaref.esUsuarioMotorOscar && window.lebaref.esUsuarioMotorOscar() && window.lebaref.abrirMotorPreciosConfianza) {
                    frm.add_custom_button("Motor de precios y confianza", () => window.lebaref.abrirMotorPreciosConfianza("catalogo"), "Más acciones");
                }

                const tieneConceptosLibres = (frm.doc.items || []).some(
                    (r) => r.item_code === "LEBAREF-CONCEPTO-LIBRE" || Boolean(r.custom_lebaref_concepto)
                );
                if (tieneConceptosLibres) {
                    frm.add_custom_button("Vincular al catálogo", () => abrirDialogoConvertirConcepto(frm), "Más acciones");
                    frm.add_custom_button("Enviar a equivalencias", () => {
                        const rLibre = (frm.doc.items || []).find(
                            (r) => r.item_code === "LEBAREF-CONCEPTO-LIBRE" || Boolean(r.custom_lebaref_concepto)
                        );
                        if (window.lebaref && window.lebaref.abrirModalProponerEquivalencia) {
                            window.lebaref.abrirModalProponerEquivalencia({
                                texto_original: decodificarHtmlLocal((rLibre && (rLibre.custom_lebaref_concepto || rLibre.description)) || ""),
                                origen: "Solicitud de Ingeniero",
                                proyecto: (rLibre && rLibre.project) || "",
                            });
                        }
                    }, "Más acciones");
                } else {
                    frm.add_custom_button("Surtir de existencias", () => abrirOp("canalizar_stock", "Surtir de existencias · " + frm.doc.name), "Más acciones");
                }
            } else if (frm.doc.material_request_type === "Material Issue" || frm.doc.material_request_type === "Material Transfer") {
                decorarBtnIcono(
                    frm.add_custom_button("Entregar desde almacén", () => abrirOp("surtir", "Entregar desde almacén · " + frm.doc.name)).addClass("btn-primary"),
                    "check",
                    "Entregar desde almacén"
                );
                frm.add_custom_button("Crear anexo", () => abrirOp("anexo", "Crear anexo vinculado a " + frm.doc.name), "Más acciones");
                frm.add_custom_button("En espera de cliente (Presupuesto)", async () => {
                    await frappe.call({
                        method: "lebaref_compras.flujo_operativo.actualizar_estado_pago_logistica",
                        type: "POST",
                        args: {
                            doctype: "Material Request",
                            folio: frm.doc.name,
                            nuevo_estado: "En espera de aprobación de cliente",
                        },
                        freeze: true,
                    });
                    frappe.show_alert({ message: "Marcada en espera de aprobación de cliente", indicator: "purple" });
                    frm.reload_doc();
                }, "Más acciones");
                if (window.lebaref && window.lebaref.abrirHiloConductor) {
                    frm.add_custom_button("Hilo conductor conciliado", () => window.lebaref.abrirHiloConductor("Material Request", frm.doc.name), "Más acciones");
                }
                if (window.lebaref && window.lebaref.esUsuarioMotorOscar && window.lebaref.esUsuarioMotorOscar() && window.lebaref.abrirMotorPreciosConfianza) {
                    frm.add_custom_button("Motor de precios y confianza", () => window.lebaref.abrirMotorPreciosConfianza("catalogo"), "Más acciones");
                }
            }
        },
    });

    function abrirDialogoNombreSolicitud(frmOrFolio, nombreInicial = "", alGuardar = null) {
        const esFrm = frmOrFolio && typeof frmOrFolio === "object" && frmOrFolio.doc;
        const folio = esFrm ? frmOrFolio.doc.name : String(frmOrFolio || "");
        const actual = esFrm
            ? (frmOrFolio.doc.custom_lebaref_nombre_solicitud || frmOrFolio.doc.title || "")
            : (nombreInicial && nombreInicial !== "Sin nombre asignado" ? nombreInicial : "");
        const d = new frappe.ui.Dialog({
            title: `✏️ Nombre / Referencia de la Solicitud · ${folio}`,
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
                    args: { solicitud: folio, nombre: vals.nombre },
                    freeze: true,
                });
                d.hide();
                frappe.show_alert({ message: "Nombre de solicitud actualizado", indicator: "green" });
                if (typeof alGuardar === "function") alGuardar(vals.nombre);
                else if (esFrm) frmOrFolio.reload_doc();
            },
        });
        d.show();
    }

    async function abrirDialogoCotizacionDirecta(frmOrFolio, alGuardar = null) {
        const folio = frmOrFolio && typeof frmOrFolio === "object" && frmOrFolio.doc
            ? frmOrFolio.doc.name
            : String(frmOrFolio || "");
        const [{ message: info }, { message: refsHist }] = await Promise.all([
            frappe.call({
                method: "lebaref_compras.ux_lebaref.esquema_cotizacion_directa",
                type: "GET",
                args: { solicitud: folio },
                freeze: true,
                freeze_message: "Cargando partidas y referencias históricas…",
            }),
            frappe.call({
                method: "lebaref_compras.analitica_operativa.referencias_historicas_solicitud",
                type: "GET",
                args: { solicitud: folio },
            }).catch(() => ({ message: {} })),
        ]);
        const mapaRefs = refsHist || {};

        const preciosEstado = (info.partidas || []).map((p) => ({
            mr_item: p.mr_item,
            idx: p.idx,
            clave: p.clave,
            descripcion: p.descripcion,
            qty: p.qty,
            uom: p.uom,
            obra: p.obra || "",
            partida: p.partida || "",
            no_recotizar: Boolean(p.no_recotizar),
            ref: mapaRefs[p.mr_item] || null,
            rate: "",
            lead_time_days: "1",
        }));

        const adjuntosCotizacion = [];

        const d = new frappe.ui.Dialog({
            title: `Capturar Cotización Directa · ${folio}`,
            size: "extra-large",
            fields: [
                {
                    fieldname: "_ayuda",
                    fieldtype: "HTML",
                    options: `<div class="alert alert-info py-2 px-3 mb-2" style="font-size:12px;">
                        <div class="d-flex justify-content-between align-items-center" style="gap:10px;flex-wrap:wrap;">
                            <span>Captura precios unitarios <b>sin IVA</b> en <b>MXN o USD (con TC DOF del día)</b> y separa el flete si aplica.</span>
                            <div class="d-flex align-items-center flex-wrap" style="gap:6px;">
                                ${frappe.model.can_create("Supplier") ? `<button type="button" class="btn btn-xs btn-default leb-cot-new-sup">+ Nuevo proveedor</button>` : ""}
                                <button type="button" class="btn btn-xs btn-default leb-cot-adjuntar" style="font-weight:600;border-color:#0284c7;color:#0369a1;">Adjuntar cotización (PDF / Imagen)</button>
                                <input type="file" multiple accept=".pdf,.png,.jpg,.jpeg,.webp,.xlsx,.xls,.doc,.docx,application/pdf,image/*" class="d-none leb-cot-adjunto-file">
                                <button type="button" class="btn btn-xs btn-default leb-cot-tpl">Plantilla CSV</button>
                                <button type="button" class="btn btn-xs btn-primary leb-cot-xlsx">Cargar Excel / CSV</button>
                                <input type="file" accept=".xlsx,.csv,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" class="d-none leb-cot-file">
                            </div>
                        </div>
                        <div class="leb-cot-adjuntos-lista d-none mt-2 pt-2" style="border-top:1px dashed #93c5fd;"></div>
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
                    fieldname: "currency",
                    label: "Moneda de la cotización",
                    fieldtype: "Select",
                    options: "MXN\nUSD",
                    default: "MXN",
                    reqd: 1,
                },
                {
                    fieldname: "tc_dof",
                    label: "Tipo de Cambio oficial DOF del día (obligatorio en USD)",
                    fieldtype: "Float",
                    precision: 4,
                    depends_on: "eval:doc.currency === 'USD'",
                    mandatory_depends_on: "eval:doc.currency === 'USD'",
                },
                {
                    fieldname: "fecha_dof",
                    label: "Fecha publicación DOF",
                    fieldtype: "Date",
                    default: frappe.datetime.get_today(),
                    depends_on: "eval:doc.currency === 'USD'",
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
                    fieldname: "modalidad_entrega",
                    label: "Modalidad de entrega",
                    fieldtype: "Select",
                    options: "\nIr a recolectar\nEn flete (proveedor entrega)\nEntrega directa en obra",
                    default: "Ir a recolectar",
                },
                {
                    fieldname: "dias_entrega_promesa",
                    label: "Días promesa de entrega / flete",
                    fieldtype: "Int",
                    default: 1,
                },
                {
                    fieldname: "flete_sin_iva",
                    label: "Flete separado sin IVA (0 si no cobra flete)",
                    fieldtype: "Float",
                    default: 0,
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
                if (vals.currency === "USD" && !(Number(vals.tc_dof || 0) > 0)) {
                    frappe.msgprint("Para cotizaciones en USD debes ingresar manualmente el Tipo de Cambio oficial del DOF de ese día.");
                    return;
                }
                const btn = d.get_primary_btn().prop("disabled", true);
                try {
                    const { message: res } = await frappe.call({
                        method: "lebaref_compras.ux_lebaref.registrar_cotizacion_directa",
                        type: "POST",
                        args: {
                            solicitud: folio,
                            datos: JSON.stringify({
                                supplier: vals.supplier,
                                currency: vals.currency || "MXN",
                                tc_dof: vals.currency === "USD" ? Number(vals.tc_dof) : 1,
                                fecha_dof: vals.fecha_dof || frappe.datetime.get_today(),
                                modalidad_entrega: vals.modalidad_entrega || "",
                                dias_entrega_promesa: Number(vals.dias_entrega_promesa || 0),
                                flete_sin_iva: Number(vals.flete_sin_iva || 0),
                                valid_till: vals.valid_till,
                                taxes_and_charges: vals.taxes_and_charges || "",
                                payment_terms_template: vals.payment_terms_template || "",
                                rows: filasConPrecio,
                                adjuntos: adjuntosCotizacion.map((a) => ({
                                    filename: a.filename,
                                    content_base64: a.content_base64,
                                })),
                            }),
                        },
                        freeze: true,
                        freeze_message: adjuntosCotizacion.length
                            ? "Guardando cotización y adjuntando archivo(s) para Drive…"
                            : "Guardando cotización de proveedor…",
                    });
                    d.hide();
                    const nAdj = Number((res && res.adjuntos_guardados) || 0);
                    const txtAdj = nAdj > 0 ? ` · ${nAdj} adjunto(s) listo(s) para Drive` : "";
                    frappe.show_alert({
                        message: `Cotización registrada: ${res.folio} (${vals.supplier} · ${vals.currency}${txtAdj})`,
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
        $('<thead style="background:#031e34;color:#fff;"><tr><th style="width:34px;">#</th><th style="width:130px;">Código / Clave</th><th>Descripción del Artículo / Concepto</th><th class="text-right" style="width:90px;">Cantidad</th><th class="text-right" style="width:128px;">Ref. Histórica</th><th style="width:165px;">Precio Unit. s/IVA ($)</th><th style="width:96px;">Entrega (días)</th></tr></thead>').appendTo(tbl);
        const tbody = $("<tbody>").appendTo(tbl);

        function evaluarPokaYokePrecio(p) {
            if (!p.$rateInput || !p.$rateWarn) return;
            const raw = String(p.rate == null ? "" : p.rate).trim();
            if (!raw) {
                p.$rateInput.css("border-color", "");
                p.$rateWarn.empty().addClass("d-none");
                return;
            }
            const val = Number(raw);
            if (!Number.isFinite(val) || val < 0) {
                p.$rateInput.css("border-color", "#dc2626");
                p.$rateWarn
                    .removeClass("d-none")
                    .css("color", "#dc2626")
                    .text("No se permiten precios negativos");
                return;
            }
            if (val === 0) {
                p.$rateInput.css("border-color", "");
                p.$rateWarn.empty().addClass("d-none");
                return;
            }
            const monedaActual = d.get_value("currency") || "MXN";
            const tcActual = monedaActual === "USD" ? Number(d.get_value("tc_dof") || 0) : 1;
            if (monedaActual === "USD" && !(tcActual > 0)) {
                p.$rateInput.css("border-color", "#d97706");
                p.$rateWarn
                    .removeClass("d-none")
                    .css("color", "#b45309")
                    .text("Ingresa arriba el TC DOF del día para validar en MXN");
                return;
            }
            const eqMxn = val * (monedaActual === "USD" ? tcActual : 1);
            if (p.ref && p.ref.ref_precio > 0) {
                const pct = ((eqMxn - p.ref.ref_precio) / p.ref.ref_precio) * 100;
                if (pct >= 25) {
                    p.$rateInput.css("border-color", "#d97706");
                    p.$rateWarn
                        .removeClass("d-none")
                        .css("color", "#b45309")
                        .text(`Variación +${pct.toFixed(0)}% vs histórico — verifica unidad o moneda`);
                    return;
                }
                if (pct <= -40) {
                    p.$rateInput.css("border-color", "#0284c7");
                    p.$rateWarn
                        .removeClass("d-none")
                        .css("color", "#0369a1")
                        .text(`Variación ${pct.toFixed(0)}% vs histórico — confirma que la unidad coincida`);
                    return;
                }
            }
            p.$rateInput.css("border-color", "#10b981");
            p.$rateWarn.empty().addClass("d-none");
        }

        const bloquesCot = agruparFilasPorPartidaLocal(preciosEstado, (p) => p.partida);
        for (const grupo of bloquesCot) {
            if (grupo.partida) {
                $(`<tr class="leb-partida-block-row" style="background:#e2e8f0;color:#031e34;font-weight:800;font-size:11.5px;">
                    <td colspan="7" style="padding:6px 10px;border-top:2px solid #cbd5e1;">
                        ${frappe.utils.escape_html(grupo.partida)} <span style="font-weight:600;color:#475569;">(${grupo.items.length})</span>
                    </td>
                </tr>`).appendTo(tbody);
            }
            for (const p of grupo.items) {
                const tr = $("<tr>").appendTo(tbody);
                $("<td>").text(p.idx).appendTo(tr);
                $("<td>").html(`<b>${frappe.utils.escape_html(p.clave)}</b>`).appendTo(tr);
                const tdDescCot = $("<td>").appendTo(tr);
                $("<div>").text(decodificarHtmlLocal(p.descripcion)).appendTo(tdDescCot);
                if (p.no_recotizar) {
                    $('<div class="mt-1"><span class="badge badge-success" style="font-size:10.5px;">Validado por Gerencia — No recotizar</span></div>').appendTo(tdDescCot);
                }
                $("<td class='text-right'>").html(`<b>${numFmt.format(p.qty)} ${frappe.utils.escape_html(p.uom)}</b>`).appendTo(tr);

                const tdRef = $("<td class='text-right small'>").appendTo(tr);
                if (p.ref && p.ref.ref_precio > 0) {
                    tdRef.html(`<b>$${moneyFmt.format(p.ref.ref_precio)}</b><br><span class="text-muted">${frappe.utils.escape_html(p.ref.ref_proveedor || p.ref.ref_fuente || "")}</span>`);
                } else {
                    tdRef.html(`<span class="text-muted">Sin ref.</span>`);
                }

                const tdRate = $("<td>").appendTo(tr);
                p.$rateInput = $('<input type="number" step="0.01" min="0" class="form-control form-control-sm" placeholder="0.00 (Sin cotizar)">')
                    .on("input", function () {
                        p.rate = $(this).val();
                        evaluarPokaYokePrecio(p);
                    })
                    .appendTo(tdRate);
                p.$rateWarn = $('<div class="d-none" style="font-size:10.5px;font-weight:600;line-height:1.25;margin-top:3px;">').appendTo(tdRate);

                const tdDays = $("<td>").appendTo(tr);
                p.$daysInput = $('<input type="number" step="1" min="0" class="form-control form-control-sm">')
                    .val(p.lead_time_days)
                    .on("input", function () {
                        const rawD = Number($(this).val());
                        p.lead_time_days = Number.isFinite(rawD) && rawD >= 0 ? String(Math.round(rawD)) : "0";
                    })
                    .appendTo(tdDays);
            }
        }

        if (d.fields_dict.currency && d.fields_dict.currency.$input) {
            d.fields_dict.currency.$input.on("change", () => preciosEstado.forEach(evaluarPokaYokePrecio));
        }
        if (d.fields_dict.tc_dof && d.fields_dict.tc_dof.$input) {
            d.fields_dict.tc_dof.$input.on("input change", () => preciosEstado.forEach(evaluarPokaYokePrecio));
        }
        d.fields_dict._ayuda.$wrapper.find(".leb-cot-new-sup").on("click", () => {
            if (window.lebaref && window.lebaref.abrirModalProveedorRapido) {
                window.lebaref.abrirModalProveedorRapido("", (res) => {
                    if (res && res.folio && d.fields_dict.supplier) {
                        const listaSup = info.proveedores || [];
                        if (!listaSup.includes(res.folio)) {
                            listaSup.push(res.folio);
                            listaSup.sort();
                        }
                        d.fields_dict.supplier.df.options = ["", ...listaSup].join("\n");
                        d.fields_dict.supplier.refresh();
                        d.set_value("supplier", res.folio);
                    }
                });
            }
        });

        const ayudaCotWrap = d.fields_dict._ayuda.$wrapper;
        const pintarAdjuntosCotizacion = () => {
            const $box = ayudaCotWrap.find(".leb-cot-adjuntos-lista");
            const $btnAdj = ayudaCotWrap.find(".leb-cot-adjuntar");
            if (!adjuntosCotizacion.length) {
                $box.empty().addClass("d-none");
                $btnAdj.text("Adjuntar cotización (PDF / Imagen)");
                return;
            }
            $btnAdj.text(`Adjuntar cotización (${adjuntosCotizacion.length})`);
            const chips = adjuntosCotizacion.map((a, idx) => {
                const kb = Math.max(1, Math.round((a.size || 0) / 1024));
                return `<span class="badge badge-light d-inline-flex align-items-center mr-1 mb-1" style="border:1px solid #93c5fd;color:#0f172a;font-size:11px;padding:4px 8px;gap:6px;">
                    <span>${frappe.utils.escape_html(a.filename)} <span class="text-muted">(${kb} KB)</span></span>
                    <a href="#" class="leb-cot-del-adj text-danger" data-idx="${idx}" title="Quitar archivo" style="font-weight:800;text-decoration:none;">×</a>
                </span>`;
            }).join("");
            $box.removeClass("d-none").html(
                `<div class="small mb-1" style="color:#0369a1;font-weight:600;">Archivo(s) que se guardarán en esta cotización y se subirán al expediente de Drive:</div>${chips}`
            );
        };
        ayudaCotWrap.on("click", ".leb-cot-del-adj", function (ev) {
            ev.preventDefault();
            const i = Number($(this).attr("data-idx"));
            if (Number.isInteger(i) && i >= 0 && i < adjuntosCotizacion.length) {
                adjuntosCotizacion.splice(i, 1);
                pintarAdjuntosCotizacion();
            }
        });
        ayudaCotWrap.find(".leb-cot-adjuntar").on("click", () => ayudaCotWrap.find(".leb-cot-adjunto-file").trigger("click"));
        ayudaCotWrap.find(".leb-cot-adjunto-file").on("change", function () {
            const files = Array.from(this.files || []);
            if (!files.length) return;
            for (const f of files) {
                if (f.size > 25 * 1024 * 1024) {
                    frappe.msgprint(`El archivo <b>${frappe.utils.escape_html(f.name)}</b> supera el límite de 25 MB.`);
                    continue;
                }
                if (adjuntosCotizacion.length >= 10) break;
                const rd = new FileReader();
                rd.onload = (ev) => {
                    adjuntosCotizacion.push({
                        filename: f.name,
                        size: f.size,
                        content_base64: String(ev.target.result || ""),
                    });
                    pintarAdjuntosCotizacion();
                };
                rd.readAsDataURL(f);
            }
            this.value = "";
        });
        ayudaCotWrap.find(".leb-cot-tpl").on("click", () => {
            const escCsv = (v) => `"${String(v == null ? "" : v).replace(/"/g, '""')}"`;
            const lineas = ["Renglon,MR_Item_ID,Clave,Descripcion,Cantidad,Precio_Sin_IVA,Dias_Entrega"];
            for (const p of preciosEstado) {
                lineas.push([p.idx, p.mr_item, escCsv(p.clave), escCsv(p.descripcion), `${p.qty} ${p.uom}`, "", "1"].join(","));
            }
            const blob = new Blob(["\uFEFF" + lineas.join("\n") + "\n"], { type: "text/csv;charset=utf-8;" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = `Plantilla_Cotizacion_${folio}.csv`;
            a.click();
            URL.revokeObjectURL(url);
        });
        ayudaCotWrap.find(".leb-cot-xlsx").on("click", () => ayudaCotWrap.find(".leb-cot-file").trigger("click"));
        ayudaCotWrap.find(".leb-cot-file").on("change", function () {
            const file = this.files && this.files[0];
            if (!file) return;
            const reader = new FileReader();
            reader.onload = async (ev) => {
                const { message: res } = await frappe.call({
                    method: "lebaref_compras.ux_lebaref.parsear_excel_cotizacion",
                    type: "POST",
                    args: {
                        solicitud: folio,
                        filename: file.name,
                        content_base64: String(ev.target.result || ""),
                    },
                    freeze: true,
                    freeze_message: "Leyendo precios desde Excel / CSV…",
                });
                const mapaPrecios = (res && res.precios) || {};
                let aplicados = 0;
                for (const p of preciosEstado) {
                    const hit = mapaPrecios[p.mr_item];
                    if (hit && Number(hit.rate) > 0) {
                        p.rate = String(hit.rate);
                        p.lead_time_days = String(hit.lead_time_days != null ? hit.lead_time_days : 1);
                        if (p.$rateInput) p.$rateInput.val(p.rate);
                        if (p.$daysInput) p.$daysInput.val(p.lead_time_days);
                        evaluarPokaYokePrecio(p);
                        aplicados++;
                    }
                }
                frappe.show_alert({
                    message: aplicados ? `Precios aplicados en ${aplicados} partida(s).` : "No se encontraron precios mayores a cero que coincidan.",
                    indicator: aplicados ? "green" : "orange",
                });
                this.value = "";
            };
            reader.readAsDataURL(file);
        });

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

    function abrirDialogoConvertirConcepto(frmOrFicha, alConvertir = null) {
        const esFrm = frmOrFicha && typeof frmOrFicha === "object" && frmOrFicha.doc;
        const folio = esFrm ? frmOrFicha.doc.name : String((frmOrFicha && frmOrFicha.folio) || "");
        const company = esFrm ? frmOrFicha.doc.company : String((frmOrFicha && frmOrFicha.company) || "");
        const itemsRaw = esFrm ? (frmOrFicha.doc.items || []) : ((frmOrFicha && frmOrFicha.partidas) || []);
        const libres = itemsRaw.filter(
            (r) => r.es_libre || r.item_code === "LEBAREF-CONCEPTO-LIBRE" || Boolean(r.custom_lebaref_concepto)
        );
        if (!libres.length) {
            frappe.msgprint("Esta solicitud no tiene renglones de concepto libre pendientes de convertir.");
            return;
        }
        const opcionesRenglon = libres.map((r) => ({
            value: r.name,
            label: `#${r.idx} · ${r.custom_lebaref_concepto || r.descripcion || r.description || r.name} (${r.qty} ${r.uom || ""})`,
        }));
        const tokenOp = nuevoUuidLocal();

        const dlg = new frappe.ui.Dialog({
            title: `Convertir concepto libre a SKU inventariable · ${folio}`,
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
                    default: "Pieza",
                    depends_on: "eval:doc.modo_sku === 'Crear nuevo SKU inventariable ahora'",
                },
                {
                    fieldname: "warehouse",
                    label: "Almacén destino del renglón",
                    fieldtype: "Link",
                    options: "Warehouse",
                    get_query: () => ({ filters: { company, is_group: 0, disabled: 0 } }),
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
                            folio,
                            operacion: tokenOp,
                            datos: JSON.stringify(payload),
                        },
                        freeze: true,
                        freeze_message: "Generando enmienda con SKU inventariable…",
                    });
                    dlg.hide();
                    frappe.show_alert({ message: `Concepto convertido en ${res.folio}`, indicator: "green" });
                    if (typeof alConvertir === "function") alConvertir(res);
                    else frappe.set_route("Form", "Material Request", res.folio);
                } finally {
                    btn.prop("disabled", false);
                }
            },
        });
        dlg.show();
    }

    async function showComparison(frmOrFolio, alGenerar = null) {
        const esFrm = frmOrFolio && typeof frmOrFolio === "object" && frmOrFolio.doc;
        if (esFrm && frmOrFolio.is_dirty()) {
            frappe.msgprint("Guarda la solicitud antes de abrir la comparativa.");
            return;
        }
        const folio = esFrm ? frmOrFolio.doc.name : String(frmOrFolio || "");

        const [{ message: comparison }, { message: refsHistComp }] = await Promise.all([
            frappe.call({
                method: api + ".consultar",
                type: "GET",
                args: { solicitud: folio },
            }),
            frappe.call({
                method: "lebaref_compras.analitica_operativa.referencias_historicas_solicitud",
                type: "GET",
                args: { solicitud: folio },
            }).catch(() => ({ message: {} })),
        ]);
        const mapaRefsComp = refsHistComp || {};

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
            title: `Cuadro Comparativo Lebaref · ${folio}`,
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
                    <button type="button" class="btn btn-xs btn-default leb-btn-wa">Copiar para WhatsApp</button>
                    <button type="button" class="btn btn-xs btn-primary leb-btn-add-quote">+ Capturar cotización</button>
                    <button type="button" class="btn btn-xs btn-default leb-btn-dir">Directorio proveedores</button>
                    <button type="button" class="btn btn-xs btn-default leb-btn-print">Imprimir comparativa</button>
                    <button type="button" class="btn btn-xs btn-secondary leb-btn-best">★ Mejor precio en todo</button>
                </div>
            </div>
        `).appendTo(wrapper);

        toolbar.find(".leb-btn-wa").on("click", () => {
            if (window.lebaref && window.lebaref.copiarSolicitudParaWhatsapp) {
                window.lebaref.copiarSolicitudParaWhatsapp(folio, comparison.renglones || []);
            }
        });

        toolbar.find(".leb-btn-add-quote").on("click", () => {
            dialog.hide();
            abrirDialogoCotizacionDirecta(folio, () => showComparison(frmOrFolio, alGenerar));
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
            $(`
                <div class="alert alert-warning mb-2">
                    Aún no hay ofertas registradas para <b>${frappe.utils.escape_html(folio)}</b>.<br>
                    Haz clic en <b>+ Capturar cotización</b> aquí arriba para ingresar precios unitarios, IVA, flete y condiciones de pago directamente sobre esta solicitud.
                </div>
            `).appendTo(wrapper);
            if (comparison.es_motor_oscar && (comparison.renglones || []).length) {
                const triajeWrap = $('<div class="table-responsive mt-2">').appendTo(wrapper);
                const triajeTbl = $('<table class="table table-bordered table-sm" style="font-size:12px;">').appendTo(triajeWrap);
                $('<thead style="background:#031e34;color:#fff;"><tr><th>Artículo / Concepto</th><th class="text-right">Cant.</th><th class="text-right">Estimado Gerencia (MXN)</th><th class="text-center">Confianza</th><th class="text-right">Triaje Gerencial</th></tr></thead>').appendTo(triajeTbl);
                const tBodyTriaje = $("<tbody>").appendTo(triajeTbl);
                for (const line of comparison.renglones) {
                    const trT = $("<tr>").appendTo(tBodyTriaje);
                    const tdA = $("<td>").appendTo(trT);
                    $("<div>").css("font-weight", "600").text(decodificarHtmlLocal(line.articulo)).appendTo(tdA);
                    if (line.no_recotizar) {
                        $('<span class="badge badge-success mt-1">Validado por Gerencia — No recotizar</span>').appendTo(tdA);
                    }
                    $("<td class='text-right'>").text(`${numFmt.format(line.cantidad)} ${line.unidad || ""}`).appendTo(trT);
                    const eo = line.estimacion_oscar || null;
                    if (eo && eo.tiene_historial) {
                        $("<td class='text-right'>").html(`<b>$${moneyFmt.format(eo.precio_estimado_uom_mxn)}</b>`).appendTo(trT);
                        $("<td class='text-center'>").html(`<span class="indicator-pill ${frappe.utils.escape_html(eo.semaforo_color)}">${eo.indice_confianza}% · ${frappe.utils.escape_html(eo.semaforo)}</span>`).appendTo(trT);
                    } else {
                        $("<td class='text-right text-muted'>").text("—").appendTo(trT);
                        $("<td class='text-center text-muted'>").text("Sin historial").appendTo(trT);
                    }
                    const tdAccT = $("<td class='text-right' style='white-space:nowrap;'>").appendTo(trT);
                    if (eo && eo.item_code && window.lebaref && window.lebaref.abrirGraficaTiempoPrecioArticulo) {
                        $('<button type="button" class="btn btn-xs btn-default mr-1">Gráfica</button>')
                            .on("click", () => window.lebaref.abrirGraficaTiempoPrecioArticulo(eo.item_code))
                            .appendTo(tdAccT);
                    }
                    if (!line.ordenado) {
                        $(`<button type="button" class="btn btn-xs ${line.no_recotizar ? "btn-default" : "btn-primary"}">${line.no_recotizar ? "Reactivar cotización" : "Validado — No recotizar"}</button>`)
                            .on("click", async () => {
                                await frappe.call({
                                    method: "lebaref_compras.motor_precios.marcar_validado_no_recotizar",
                                    type: "POST",
                                    args: { solicitud: folio, mr_item: line.id, validar: line.no_recotizar ? 0 : 1 },
                                    freeze: true,
                                });
                                dialog.hide();
                                showComparison(frmOrFolio, alGenerar);
                            })
                            .appendTo(tdAccT);
                    }
                }
            }
        } else {
            const tblWrap = $('<div class="table-responsive">').appendTo(wrapper);
            const table = $('<table class="table table-bordered table-sm" style="font-size:12px;">').appendTo(tblWrap);
            const thead = $('<thead style="background:#031e34;color:#fff;">').appendTo(table);
            const hRow = $("<tr>").appendTo(thead);
            hRow.append("<th>Artículo / Concepto</th><th class='text-right'>Cant.</th><th class='text-right'>Ref. Histórica</th>");

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
                        .text(`Cotización completa: Subtotal $${moneyFmt.format(com.neto || 0)} | Total $${moneyFmt.format(com.total || 0)} ${meta.moneda}`)
                        .appendTo(th);
                }
            }
            hRow.append("<th>Estado</th>");

            const tbody = $("<tbody>").appendTo(table);
            const totalColsComp = 4 + suppliers.length;
            const bloquesComp = agruparFilasPorPartidaLocal(comparison.renglones || [], (l) => l.partida);

            for (const grupo of bloquesComp) {
                if (grupo.partida) {
                    $(`<tr class="leb-partida-block-row" style="background:#e2e8f0;color:#031e34;font-weight:800;font-size:11.5px;">
                        <td colspan="${totalColsComp}" style="padding:6px 10px;border-top:2px solid #cbd5e1;">
                            ${frappe.utils.escape_html(grupo.partida)} <span style="font-weight:600;color:#475569;">(${grupo.items.length})</span>
                        </td>
                    </tr>`).appendTo(tbody);
                }

                for (const line of grupo.items) {
                    const tr = $("<tr>").appendTo(tbody);
                    const subInfo = [line.codigo || "CONCEPTO LIBRE", line.obra].filter(Boolean).join(" · ");
                    const tdArt = $("<td>").appendTo(tr);
                    $("<div>").css("font-weight", "600").text(decodificarHtmlLocal(line.articulo)).appendTo(tdArt);
                    if (subInfo) $("<div class='text-muted small'>").text(subInfo).appendTo(tdArt);
                    if (line.no_recotizar) {
                        $('<div class="mt-1"><span class="badge badge-success" style="font-size:10px;">Validado por Gerencia — No recotizar</span></div>').appendTo(tdArt);
                    }
                    if (comparison.es_motor_oscar && line.estimacion_oscar) {
                        const eo = line.estimacion_oscar;
                        const rowOsc = $('<div class="d-flex align-items-center flex-wrap mt-1" style="gap:5px;font-size:10.5px;">').appendTo(tdArt);
                        const txtEo = eo.tiene_historial
                            ? `Est. $${moneyFmt.format(eo.precio_estimado_uom_mxn)} · ${eo.indice_confianza}% (${eo.semaforo})`
                            : `Sin historial (${eo.indice_confianza}%)`;
                        $('<a href="#" style="font-weight:700;color:#0369a1;text-decoration:underline;">')
                            .text(txtEo)
                            .on("click", (ev) => {
                                ev.preventDefault();
                                if (window.lebaref && window.lebaref.abrirGraficaTiempoPrecioArticulo) {
                                    window.lebaref.abrirGraficaTiempoPrecioArticulo(eo.item_code);
                                }
                            })
                            .appendTo(rowOsc);
                        if (!line.ordenado) {
                            $(`<button type="button" class="btn btn-xs btn-default" style="padding:1px 6px;font-size:10px;">${line.no_recotizar ? "Reactivar" : "No recotizar"}</button>`)
                                .on("click", async () => {
                                    await frappe.call({
                                        method: "lebaref_compras.motor_precios.marcar_validado_no_recotizar",
                                        type: "POST",
                                        args: { solicitud: folio, mr_item: line.id, validar: line.no_recotizar ? 0 : 1 },
                                        freeze: true,
                                    });
                                    dialog.hide();
                                    showComparison(frmOrFolio, alGenerar);
                                })
                                .appendTo(rowOsc);
                        }
                    }

                    $("<td class='text-right' style='white-space:nowrap;font-weight:600;'>")
                        .text(`${numFmt.format(line.cantidad)} ${line.unidad || ""}`)
                        .appendTo(tr);

                    const refRow = mapaRefsComp[line.id] || null;
                    const tdRefComp = $("<td class='text-right small' style='background:#f8fafc;white-space:nowrap;'>").appendTo(tr);
                    if (refRow && refRow.ref_precio > 0) {
                        tdRefComp.html(
                            `<b>$${moneyFmt.format(refRow.ref_precio)}</b><br><span class="text-muted" style="font-size:10.5px;">${frappe.utils.escape_html(refRow.ref_proveedor || refRow.ref_fuente || "")}</span>`
                        );
                    } else {
                        tdRefComp.html(`<span class="text-muted">—</span>`);
                    }

                    let minPriceMxn = Infinity;
                    for (const opt of line.opciones || []) {
                        const eqMxn = Number(opt.precio_mxn || opt.precio || 0);
                        if (eqMxn > 0 && eqMxn < minPriceMxn) minPriceMxn = eqMxn;
                    }

                    let bestInputForRow = null;

                    for (const sup of suppliers) {
                        const td = $("<td>").appendTo(tr);
                        const opt = (line.opciones || []).find((o) => o.proveedor === sup);
                        if (!opt) {
                            td.css({ color: "#94a3b8", background: "#f8fafc" }).text("Sin cotizar");
                            continue;
                        }

                        const eqMxn = Number(opt.precio_mxn || opt.precio || 0);
                        const isBest = Math.abs(eqMxn - minPriceMxn) < 0.005;
                        if (isBest) td.css("background", "#ecfdf5");

                        const subtotal = opt.precio * line.cantidad;
                        const com = opt.comercial || null;
                        const tasaPct = com && typeof com.tasa_impuesto_pct === "number"
                            ? com.tasa_impuesto_pct
                            : (com && Number(com.neto) > 0 && Number(com.total) >= Number(com.neto)
                                ? ((Number(com.total) - Number(com.neto)) / Number(com.neto)) * 100
                                : 0);
                        const totalPartida = subtotal * (1 + tasaPct / 100);

                        const label = $("<label class='d-flex align-items-start mb-0' style='gap:6px;cursor:pointer;font-weight:normal;'>").appendTo(td);
                        const input = $("<input>").attr({
                            type: "radio",
                            name: "lebaref-" + line.id,
                        }).prop("disabled", line.ordenado || !comparison.puede_generar).appendTo(label);

                        const box = $("<div>").appendTo(label);
                        const priceLine = $("<div>").css("font-weight", "700")
                            .text(`$${moneyFmt.format(opt.precio)} ${opt.moneda}`)
                            .appendTo(box);
                        if (isBest) {
                            $("<span class='badge badge-success ml-1'>").text("★ Mejor MXN").appendTo(priceLine);
                        }
                        if (opt.moneda === "USD" && opt.tc_dof > 0) {
                            $("<div class='small' style='color:#0369a1;font-size:10.5px;'>")
                                .text(`TC DOF ${opt.tc_dof.toFixed(4)} → $${moneyFmt.format(eqMxn)} MXN`)
                                .appendTo(box);
                        }
                        $("<div class='small text-muted' style='line-height:1.35;margin-top:2px;'>")
                            .html(
                                `Subtotal: $${moneyFmt.format(subtotal)}<br>` +
                                `<b style="color:#0f172a;">Total: $${moneyFmt.format(totalPartida)}</b>`
                            )
                            .appendTo(box);

                        if (!line.ordenado) {
                            const ahorroPpv = refRow && refRow.ref_precio > 0 ? (refRow.ref_precio - eqMxn) * line.cantidad : 0;
                            radios.push({
                                input,
                                line: line.id,
                                option: opt.id,
                                proveedor: sup,
                                subtotal,
                                totalPartida,
                                ahorroPpv,
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
            const grandTotals = {};
            const monedasProv = {};
            let ppvNeto = 0;
            for (const c of chosen) {
                totals[c.proveedor] = (totals[c.proveedor] || 0) + c.subtotal;
                grandTotals[c.proveedor] = (grandTotals[c.proveedor] || 0) + c.totalPartida;
                monedasProv[c.proveedor] = c.moneda || "MXN";
                ppvNeto += Number(c.ahorroPpv || 0);
            }
            const parts = Object.entries(totals).map(
                ([prov, sum]) => `${prov}: Subtotal $${moneyFmt.format(sum)} | Total $${moneyFmt.format(grandTotals[prov] || sum)} ${monedasProv[prov]}`
            );
            const ppvHtml = Math.abs(ppvNeto) >= 0.01
                ? `<br><span style="font-weight:700;color:${ppvNeto >= 0 ? "#059669" : "#b45309"};">${ppvNeto >= 0 ? `Ahorro frente a referencia histórica: $${moneyFmt.format(ppvNeto)} MXN` : `Variación sobre referencia histórica: +$${moneyFmt.format(Math.abs(ppvNeto))} MXN`}</span>`
                : "";
            summaryBox.html(
                `<b>Adjudicación seleccionada (${chosen.length} renglón/es):</b><br>` +
                parts.map((p) => frappe.utils.escape_html(p)).join(" &nbsp;&nbsp;·&nbsp;&nbsp; ") +
                ppvHtml
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

        toolbar.find(".leb-btn-print").on("click", () => printComparisonCarta(folio, comparison, suppliers, supplierMeta));

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
                        args: { solicitud: folio, seleccion: selection },
                        freeze: true,
                        freeze_message: "Generando órdenes de compra con impuestos, flete y programación de pago…",
                    });
                    dialog.hide();
                    showOrders(orders);
                    if (typeof alGenerar === "function") alGenerar(orders);
                    else if (esFrm) frmOrFolio.reload_doc();
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
        const totalPrintCols = 2 + suppliers.length;
        const bloquesPrint = agruparFilasPorPartidaLocal(comparison.renglones || [], (l) => l.partida);
        const bodyRows = bloquesPrint.map((grupo) => {
            const sepHtml = grupo.partida
                ? `<tr style="background:#e2e8f0;color:#031e34;font-weight:800;"><td colspan="${totalPrintCols}" style="padding:5px 8px;">${esc(grupo.partida)}</td></tr>`
                : "";
            const rowsHtml = grupo.items.map((line) => {
                const cells = suppliers.map((sup) => {
                    const opt = (line.opciones || []).find((o) => o.proveedor === sup);
                    if (!opt) return `<td style="color:#64748b;">Sin cotizar</td>`;
                    const sub = opt.precio * line.cantidad;
                    const comP = opt.comercial || null;
                    const tasaP = comP && typeof comP.tasa_impuesto_pct === "number" ? comP.tasa_impuesto_pct : 0;
                    const tot = sub * (1 + tasaP / 100);
                    return `<td><b>$${moneyFmt.format(opt.precio)} ${esc(opt.moneda)}</b><br><small>Subtotal: $${moneyFmt.format(sub)}<br><b>Total: $${moneyFmt.format(tot)}</b></small></td>`;
                }).join("");
                return `<tr>
                    <td><b>${esc(decodificarHtmlLocal(line.articulo))}</b><br><small>${esc([line.codigo || "LIBRE", line.obra].filter(Boolean).join(" · "))}</small></td>
                    <td style="text-align:right;">${numFmt.format(line.cantidad)} ${esc(line.unidad || "")}</td>
                    ${cells}
                </tr>`;
            }).join("");
            return sepHtml + rowsHtml;
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

    window.lebarefSolicitud = {
        abrirNombre: abrirDialogoNombreSolicitud,
        abrirCotizacionDirecta: abrirDialogoCotizacionDirecta,
        abrirComparativa: showComparison,
        abrirConvertirConcepto: abrirDialogoConvertirConcepto,
    };
})();
