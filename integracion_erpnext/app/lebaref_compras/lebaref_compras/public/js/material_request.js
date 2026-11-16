(() => {
    const api = "lebaref_compras.comparativa";
    const format = new Intl.NumberFormat("es-MX", { maximumFractionDigits: 4 });

    frappe.ui.form.on("Material Request", {
        refresh(frm) {
            if (frm.doc.docstatus === 1 && frm.doc.material_request_type === "Purchase"
                && frm.doc.status !== "Stopped") {
                frm.add_custom_button("Comparativa Lebaref", () => showComparison(frm));
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
        const dialog = new frappe.ui.Dialog({
            title: "Comparativa de proveedores",
            size: "extra-large",
            fields: [{ fieldname: "comparativa", fieldtype: "HTML" }],
        });
        const wrapper = dialog.fields_dict.comparativa.$wrapper;
        const radios = [];

        for (const line of comparison.renglones) {
            const section = $("<section>").addClass("mb-4").appendTo(wrapper);
            $("<h5>").text(line.articulo).appendTo(section);
            const details = [line.codigo, format.format(line.cantidad) + " " + line.unidad, line.obra, line.partida];
            $("<p>").addClass("text-muted").text(details.filter(Boolean).join(" · ")).appendTo(section);

            if (line.ordenado || !line.opciones.length) {
                $("<p>").text(line.ordenado
                    ? "Este renglón ya tiene una orden activa."
                    : "Sin cotizaciones vigentes por la cantidad solicitada.").appendTo(section);
                continue;
            }

            const table = $("<table>").addClass("table table-sm").appendTo(section);
            const headers = $("<tr>").appendTo($("<thead>").appendTo(table));
            for (const label of ["Elegir", "Proveedor", "Cotización", "Precio unitario sin impuestos", "Entrega"]) {
                $("<th>").attr("scope", "col").text(label).appendTo(headers);
            }
            const body = $("<tbody>").appendTo(table);
            for (const option of line.opciones) {
                const row = $("<tr>").appendTo(body);
                const input = $("<input>").attr({
                    type: "radio",
                    name: "lebaref-" + line.id,
                    "aria-label": "Elegir " + option.proveedor + " para " + line.articulo,
                }).prop("disabled", !comparison.puede_generar)
                    .appendTo($("<td>").appendTo(row));
                radios.push({ input, line: line.id, option: option.id });
                for (const value of [
                    option.proveedor,
                    option.cotizacion,
                    format.format(option.precio) + " " + option.moneda,
                    option.entrega ? option.entrega + " días" : "Sin plazo indicado",
                ]) {
                    $("<td>").text(value).appendTo(row);
                }
            }
        }

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
                        freeze_message: "Generando órdenes de compra…",
                    });
                    dialog.hide();
                    showOrders(orders);
                    frm.reload_doc();
                } finally {
                    button.prop("disabled", false);
                }
            });
            dialog.get_primary_btn().prop("disabled", true);
            for (const { input } of radios) {
                input.on("change", () => dialog.get_primary_btn().prop("disabled", false));
            }
        }

        dialog.show();
    }

    function showOrders(orders) {
        const list = $("<ul>");
        const result = new frappe.ui.Dialog({
            title: "Órdenes creadas en borrador",
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
