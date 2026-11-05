using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SistemaComprasAlmacen.Models;

namespace SistemaComprasAlmacen.Services;

public static class BarcodePrintService
{
    // Tabla oficial ISO/IEC 15417 para Code 128 (valores 0 a 106)
    private static readonly string[] Code128Patterns =
    {
        "11011001100","11001101100","11001100110","10010011000","10010001100","10001001100","10011001000","10011000100","10001100100","11001001000",
        "11001000100","11000100100","10110011100","10011011100","10011001110","10111001100","10011101100","10011100110","11001110010","11001011100",
        "11001001110","11011100100","11001110100","11101101110","11101001100","11100101100","11100100110","11101100100","11100110100","11100110010",
        "11011011000","11011000110","11000110110","10100011000","10001011000","10001000110","10110001000","10001101000","10001100010","11010001000",
        "11000101000","11000100010","10110111000","10110001110","10001101110","10111011000","10111000110","10001110110","11101110110","11010001110",
        "11000101110","11011101000","11011100010","11011101110","11101011000","11101000110","11100010110","11101101000","11101100010","11100011010",
        "11101111010","11001000010","11110001010","10100110000","10100001100","10010110000","10010000110","10000101100","10000100110","10110010000",
        "10110000100","10011010000","10011000010","10000110100","10000110010","11000010010","11001010000","11110111010","11000010100","10001111010",
        "10100111100","10010111100","10010011110","10111100100","10011110100","10011110010","11110100100","11110010100","11110010010","11011011110",
        "11011110110","11110110110","10101111000","10100011110","10001011110","10111101000","10111100010","11110101000","11110100010","10111011110",
        "10111101110","11101011110","11110101110","11010000100","11010010000","11010011100","1100011101011"
    };

    public static DrawingImage CrearImagenCodigoBarras(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            texto = "SIN-CODIGO";

        string limpio = new string(texto.Trim().Where(c => c >= 32 && c <= 126).ToArray());
        if (string.IsNullOrEmpty(limpio))
            limpio = "CODE128";

        var codes = new List<int> { 104 }; // Start Code B
        int checksum = 104;
        for (int i = 0; i < limpio.Length; i++)
        {
            int val = limpio[i] - 32;
            codes.Add(val);
            checksum += val * (i + 1);
        }
        codes.Add(checksum % 103);
        codes.Add(106); // Stop

        string bits = string.Concat(codes.Select(c => Code128Patterns[c]));

        var group = new DrawingGroup();
        double moduleWidth = 2.0;
        double quietZone = 20.0;
        double height = 56.0;
        double totalWidth = quietZone * 2 + bits.Length * moduleWidth;

        group.Children.Add(new GeometryDrawing(
            Brushes.White,
            null,
            new RectangleGeometry(new Rect(0, 0, totalWidth, height))));

        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
            {
                double x = quietZone + i * moduleWidth;
                group.Children.Add(new GeometryDrawing(
                    Brushes.Black,
                    null,
                    new RectangleGeometry(new Rect(x, 4, moduleWidth, height - 8))));
            }
        }

        var img = new DrawingImage(group);
        img.Freeze();
        return img;
    }

    public static void MostrarVistaPreviaEImprimirSolicitud(Window owner, Solicitud sol, bool soloDescargar = false)
    {
        var filas = sol.Partidas.Select((p, idx) => new[]
        {
            (idx + 1).ToString(),
            string.IsNullOrWhiteSpace(p.ProductoCodigo) ? "CONCEPTO" : p.ProductoCodigo,
            p.ProductoNombre,
            $"{p.CantidadSolicitada:0.##}",
            p.UnidadCompra,
            string.IsNullOrWhiteSpace(p.NotasPartida) ? "-" : p.NotasPartida
        }).ToList();

        var metadatos = new List<(string etiq, string val)>
        {
            ("Departamento Solicitante:", sol.DepartamentoNombre),
            ("Modalidad de Solicitud:", sol.TipoSolicitud),
            ("Proyecto / Destino:", sol.ProyectoMostrar),
            ("Origen:", sol.Origen),
            ("Estatus:", sol.Estatus)
        };

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: sol.EsSoloCotizacion ? "SOLICITUD DE COTIZACIÓN / PRESUPUESTO" : "SOLICITUD DE MATERIAL / REQUERIMIENTO",
            subtitulo: "Lebaref | Departamento de Compras y Abastecimiento",
            folioCodigoBarras: sol.Folio,
            fecha: sol.Fecha,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Descripción / Concepto", "Cant.", "Unidad", "Observación" },
            filasTabla: filas,
            totales: new List<(string, string)>
            {
                ("Total Partidas:", sol.Partidas.Count.ToString()),
                ("Total Unidades:", $"{sol.TotalArticulos:0.##}")
            },
            notas: sol.Notas,
            firmaIzquierda: $"Solicitó: Gerencia de {sol.DepartamentoNombre}",
            firmaDerecha: "Recibió: Gerencia de Compras");

        if (soloDescargar)
        {
            GuardarDocumentoEnArchivo(owner, $"Solicitud_{sol.Folio}", hoja);
            return;
        }

        AbrirVentanaVistaPreviaCarta(owner, $"Solicitud {sol.Folio}", hoja);
    }

    public static void MostrarVistaPreviaEImprimirComparativa(Window owner, Solicitud sol, List<CotizacionRenglonComparativo> renglones, bool soloDescargar = false)
    {
        var columnasProveedores = new List<(int slotBase, int provId, string nombreCorto)>();
        var usados = new HashSet<int>();

        for (int slot = 1; slot <= 3; slot++)
        {
            var grupo = renglones
                .Select(r => slot switch
                {
                    1 => (id: r.Proveedor1Id, nom: r.Proveedor1Nombre, conPrecio: r.PrecioSinIva1 > 0),
                    2 => (id: r.Proveedor2Id, nom: r.Proveedor2Nombre, conPrecio: r.PrecioSinIva2 > 0),
                    3 => (id: r.Proveedor3Id, nom: r.Proveedor3Nombre, conPrecio: r.PrecioSinIva3 > 0),
                    _ => (id: 0, nom: string.Empty, conPrecio: false)
                })
                .Where(x => x.id > 0 && !string.IsNullOrWhiteSpace(x.nom) && x.nom != "Sin asignar" && !usados.Contains(x.id))
                .GroupBy(x => x.id)
                .OrderByDescending(g => g.Count(x => x.conPrecio))
                .ThenByDescending(g => g.Count())
                .FirstOrDefault();

            if (grupo != null)
            {
                usados.Add(grupo.Key);
                string nomCorto = Proveedor.ExtraerNombreComercialCorto(grupo.First().nom);
                columnasProveedores.Add((slot, grupo.Key, string.IsNullOrWhiteSpace(nomCorto) ? $"Proveedor {slot}" : nomCorto));
            }
            else
            {
                columnasProveedores.Add((slot, 0, $"Proveedor {slot}"));
            }
        }

        // Agregar cualquier 4to+ proveedor concursante (agregado en Detalle Individual o en ProveedoresExtra)
        var extras = new List<(int provId, string nombreCorto)>();
        foreach (var r in renglones)
        {
            if (r.Proveedor1Id > 0 && !usados.Contains(r.Proveedor1Id) && !string.IsNullOrWhiteSpace(r.Proveedor1Nombre) && r.Proveedor1Nombre != "Sin asignar")
            {
                usados.Add(r.Proveedor1Id);
                extras.Add((r.Proveedor1Id, Proveedor.ExtraerNombreComercialCorto(r.Proveedor1Nombre)));
            }
            if (r.Proveedor2Id > 0 && (r.NumeroOpcionesVisibles >= 2 || r.PrecioSinIva2 > 0) && !usados.Contains(r.Proveedor2Id) && !string.IsNullOrWhiteSpace(r.Proveedor2Nombre) && r.Proveedor2Nombre != "Sin asignar")
            {
                usados.Add(r.Proveedor2Id);
                extras.Add((r.Proveedor2Id, Proveedor.ExtraerNombreComercialCorto(r.Proveedor2Nombre)));
            }
            if (r.Proveedor3Id > 0 && (r.NumeroOpcionesVisibles >= 3 || r.PrecioSinIva3 > 0) && !usados.Contains(r.Proveedor3Id) && !string.IsNullOrWhiteSpace(r.Proveedor3Nombre) && r.Proveedor3Nombre != "Sin asignar")
            {
                usados.Add(r.Proveedor3Id);
                extras.Add((r.Proveedor3Id, Proveedor.ExtraerNombreComercialCorto(r.Proveedor3Nombre)));
            }
            foreach (var ex in r.ProveedoresExtra)
            {
                if (ex.ProveedorId > 0 && !usados.Contains(ex.ProveedorId) && !string.IsNullOrWhiteSpace(ex.ProveedorNombre))
                {
                    usados.Add(ex.ProveedorId);
                    extras.Add((ex.ProveedorId, Proveedor.ExtraerNombreComercialCorto(ex.ProveedorNombre)));
                }
            }
        }

        foreach (var ex in extras)
        {
            int idxVacio = columnasProveedores.FindIndex(c => c.slotBase <= 3 && c.provId <= 0);
            if (idxVacio >= 0)
            {
                int sb = columnasProveedores[idxVacio].slotBase;
                columnasProveedores[idxVacio] = (sb, ex.provId, ex.nombreCorto);
            }
            else
            {
                columnasProveedores.Add((columnasProveedores.Count + 1, ex.provId, ex.nombreCorto));
            }
        }

        static string FormatearCeldaProveedorEnRenglon(CotizacionRenglonComparativo r, int provId)
        {
            if (provId <= 0)
                return "Sin cotizar";

            if (r.Proveedor1Id == provId)
                return r.PrecioSinIva1 > 0 ? $"${r.PrecioConIva1:N2} c/u ({r.DiasEntrega1}d)" : "Sin cotizar";
            if (r.Proveedor2Id == provId)
                return r.PrecioSinIva2 > 0 ? $"${r.PrecioConIva2:N2} c/u ({r.DiasEntrega2}d)" : "Sin cotizar";
            if (r.Proveedor3Id == provId)
                return r.PrecioSinIva3 > 0 ? $"${r.PrecioConIva3:N2} c/u ({r.DiasEntrega3}d)" : "Sin cotizar";

            var ex = r.ProveedoresExtra.FirstOrDefault(x => x.ProveedorId == provId);
            if (ex != null)
                return ex.PrecioSinIva > 0 ? $"${ex.PrecioConIva:N2} c/u ({ex.DiasEntrega}d)" : "Sin cotizar ($0.00)";

            return "Sin cotizar";
        }

        var filas = renglones.Select((r, idx) =>
        {
            var celdas = new List<string>
            {
                (idx + 1).ToString(),
                string.IsNullOrWhiteSpace(r.ProductoCodigo) ? "CONCEPTO" : r.ProductoCodigo,
                !string.IsNullOrWhiteSpace(r.DescripcionLibre) ? r.DescripcionLibre : r.ProductoNombre,
                $"{r.Cantidad:0.##} {r.UnidadCompra}"
            };

            foreach (var col in columnasProveedores)
            {
                celdas.Add(FormatearCeldaProveedorEnRenglon(r, col.provId));
            }

            celdas.Add(r.ProveedorGanadorId > 0 ? $"{Proveedor.ExtraerNombreComercialCorto(r.ProveedorGanadorNombre)} (${r.PrecioUnitarioGanadorConIva:N2})" : "Sin adjudicar");

            return celdas.ToArray();
        }).ToList();

        var encabezadosLista = new List<string> { "#", "Código", "Artículo / Concepto", "Cant." };
        encabezadosLista.AddRange(columnasProveedores.Select(c => c.nombreCorto));
        encabezadosLista.Add("Selección");

        string[] encabezadosTablaComp = encabezadosLista.ToArray();

        var adjudicadosPorProv = renglones
            .Where(r => r.ProveedorGanadorId > 0 && r.PrecioUnitarioGanadorSinIva > 0)
            .GroupBy(r => r.ProveedorGanadorId)
            .Select(g =>
            {
                string nomCom = Proveedor.ExtraerNombreComercialCorto(g.First().ProveedorGanadorNombre);
                int cantPartidas = g.Count();
                string codigos = string.Join(", ", g.Select(x => string.IsNullOrWhiteSpace(x.ProductoCodigo) ? "CONCEPTO" : x.ProductoCodigo));
                int diasMax = g.Max(x => x.DiasEntregaGanador);
                decimal subPartidasSinIva = g.Sum(x => Math.Round(x.Cantidad * x.PrecioUnitarioGanadorSinIva, 2));
                decimal fleteConIva = g.Sum(x => x.FleteGanadorConIva);
                decimal fleteSinIva = Math.Round(fleteConIva / 1.16m, 2);
                decimal subtotalSinIva = Math.Round(subPartidasSinIva + fleteSinIva, 2);
                decimal iva = Math.Round(subtotalSinIva * 0.16m, 2);
                decimal totalConIva = Math.Round(subtotalSinIva + iva, 2);
                string foliosOc = string.Join(", ", g.Where(x => x.TieneOcGenerada).Select(x => x.OrdenCompraFolio).Distinct());
                string etiquetaProv = string.IsNullOrWhiteSpace(foliosOc) ? nomCom : $"{nomCom} [{foliosOc}]";

                return new
                {
                    NombreComercial = etiquetaProv,
                    PartidasTexto = $"{cantPartidas} ({codigos})",
                    DiasTexto = $"{diasMax} días",
                    SubtotalSinIva = subtotalSinIva,
                    FleteConIva = fleteConIva,
                    Iva = iva,
                    TotalConIva = totalConIva,
                    CantPartidas = cantPartidas
                };
            })
            .OrderByDescending(x => x.TotalConIva)
            .ToList();

        string[] encTablaFinal =
        {
            "#",
            "Proveedor Adjudicado",
            "Partidas Adjudicadas",
            "Entrega",
            "Subtotal s/IVA",
            "Envío c/IVA",
            "IVA (16%)",
            "Total Adjudicado c/IVA"
        };

        var filasTablaFinal = new List<string[]>();
        for (int i = 0; i < adjudicadosPorProv.Count; i++)
        {
            var item = adjudicadosPorProv[i];
            filasTablaFinal.Add(new[]
            {
                (i + 1).ToString(),
                item.NombreComercial,
                item.PartidasTexto,
                item.DiasTexto,
                $"${item.SubtotalSinIva:N2}",
                $"${item.FleteConIva:N2}",
                $"${item.Iva:N2}",
                $"${item.TotalConIva:N2}"
            });
        }

        if (adjudicadosPorProv.Count > 0)
        {
            decimal sumSub = adjudicadosPorProv.Sum(x => x.SubtotalSinIva);
            decimal sumFlete = adjudicadosPorProv.Sum(x => x.FleteConIva);
            decimal sumIva = adjudicadosPorProv.Sum(x => x.Iva);
            decimal sumTot = adjudicadosPorProv.Sum(x => x.TotalConIva);
            int sumPartidas = adjudicadosPorProv.Sum(x => x.CantPartidas);

            filasTablaFinal.Add(new[]
            {
                "TOT",
                $"TOTAL GENERAL ({adjudicadosPorProv.Count} Proveedor(es))",
                $"{sumPartidas} partida(s) adjudicadas",
                "—",
                $"${sumSub:N2}",
                $"${sumFlete:N2}",
                $"${sumIva:N2}",
                $"${sumTot:N2}"
            });
        }
        else
        {
            filasTablaFinal.Add(new[]
            {
                "—",
                "Sin proveedores adjudicados aún",
                "0 partidas",
                "—",
                "$0.00",
                "$0.00",
                "$0.00",
                "$0.00"
            });
        }

        decimal totalEstimadoConIva = adjudicadosPorProv.Sum(x => x.TotalConIva);

        var metadatos = new List<(string etiq, string val)>
        {
            ("Solicitud Origen:", $"{sol.Folio} ({sol.TipoSolicitud})"),
            ("Departamento Solicitante:", $"Gerencia de {sol.DepartamentoNombre}"),
            ("Proyecto / Destino:", sol.ProyectoMostrar),
            ("Estatus:", sol.Estatus)
        };

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: "CUADRO COMPARATIVO DE COTIZACIONES",
            subtitulo: "Lebaref | Análisis de Precios y Proveedores Concursantes",
            folioCodigoBarras: $"COT-{sol.Id:D4}",
            fecha: DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            metadatos: metadatos,
            encabezadosTabla: encabezadosTablaComp,
            filasTabla: filas,
            totales: new List<(string, string)>
            {
                ("Partidas Analizadas:", renglones.Count.ToString()),
                ("Proveedores Adjudicados:", adjudicadosPorProv.Count.ToString()),
                ("Total Adjudicado c/IVA:", $"${totalEstimadoConIva:N2}")
            },
            notas: sol.Notas,
            firmaIzquierda: "Elaboró: Gerencia de Compras",
            firmaCentro: $"Revisó: Gerencia de {sol.DepartamentoNombre}",
            firmaDerecha: "Vo.Bo.: Dirección General",
            tituloTablaSecundaria: "TABLA FINAL DE TOTALES POR PROVEEDOR ADJUDICADO",
            encabezadosTablaSecundaria: encTablaFinal,
            filasTablaSecundaria: filasTablaFinal);

        if (soloDescargar)
        {
            GuardarDocumentoEnArchivo(owner, $"Comparativa_{sol.Folio}", hoja);
            return;
        }

        AbrirVentanaVistaPreviaCarta(owner, $"Comparativa {sol.Folio}", hoja);
    }

    public static void MostrarVistaPreviaEImprimirOrdenCompra(Window owner, OrdenCompra oc, bool soloDescargar = false)
    {
        var filas = oc.Partidas.Select((p, idx) => new[]
        {
            (idx + 1).ToString(),
            p.ProductoCodigo,
            p.ProductoNombre,
            $"{p.CantidadPedida:0.##} {p.UnidadCompra}",
            $"${p.PrecioUnitarioSinIva:N2}",
            $"${p.PrecioUnitarioConIva:N2}",
            $"${p.ImporteConIva:N2}"
        }).ToList();

        var metadatos = new List<(string etiq, string val)>
        {
            ("Proveedor:", $"{oc.ProveedorNombre} (RFC: {oc.ProveedorRfc})"),
            ("Proyecto / Destino:", string.IsNullOrWhiteSpace(oc.ProyectoNombre) ? "Consumo Interno de Departamento" : oc.ProyectoNombre),
            ("Área Solicitante:", $"Gerencia de {oc.DepartamentoNombre}"),
            ("Solicitud / Comparativa:", $"{oc.SolicitudFolio} / {oc.CotizacionFolio}"),
            ("Condición de Pago:", $"{oc.CondicionPago}{(string.IsNullOrWhiteSpace(oc.FechaProgramadaPago) ? "" : " | Fecha prog.: " + oc.FechaProgramadaPago)}"),
            ("Tiempo de Entrega:", $"{oc.TiempoEntregaDias} días hábiles"),
            ("Estatus Operativo:", oc.Estatus)
        };

        var totales = new List<(string etiq, string val)>
        {
            ("Subtotal (sin IVA):", $"${oc.Subtotal:N2}"),
            ("Costo de Envío / Flete:", $"${oc.CostoEnvioConIva:N2}"),
            ("IVA (16%):", $"${oc.Iva:N2}"),
            ("TOTAL CON IVA:", $"${oc.TotalConIva:N2}")
        };

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: "ORDEN DE COMPRA",
            subtitulo: "Lebaref | Departamento de Compras y Abastecimiento",
            folioCodigoBarras: oc.Folio,
            fecha: oc.Fecha,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Descripción", "Cantidad", "P.U. s/IVA", "P.U. c/IVA", "Total" },
            filasTabla: filas,
            totales: totales,
            notas: oc.Notas,
            firmaIzquierda: "Emitió: Gerencia de Compras",
            firmaCentro: "Vo.Bo.: Dirección General",
            firmaDerecha: "Recepción: Jefatura de Almacén");

        if (soloDescargar)
        {
            GuardarDocumentoEnArchivo(owner, $"OrdenCompra_{oc.Folio}", hoja);
            return;
        }

        AbrirVentanaVistaPreviaCarta(owner, $"Orden de Compra {oc.Folio}", hoja);
    }

    public static void MostrarVistaPreviaEImprimirEntrada(Window owner, EntradaAlmacen ent, bool soloDescargar = false)
    {
        if (!string.IsNullOrWhiteSpace(ent.TipoEntrada) &&
            ent.TipoEntrada.Contains("Devolución", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ent.OrdenCompraFolio) &&
            ent.OrdenCompraFolio.StartsWith("SAL-", StringComparison.OrdinalIgnoreCase))
        {
            var salVinculada = Data.DatabaseService.ObtenerSalidasAlmacen()
                .FirstOrDefault(s => s.Folio.Equals(ent.OrdenCompraFolio, StringComparison.OrdinalIgnoreCase));
            if (salVinculada != null)
            {
                MostrarVistaPreviaEImprimirDevolucionMaterial(owner, salVinculada, ent, soloDescargar);
                return;
            }
        }

        var filas = ent.Partidas.Select((p, idx) => new[]
        {
            (idx + 1).ToString(),
            p.ProductoCodigo,
            p.ProductoNombre,
            $"{p.CantidadCompraRecibida:0.##} {p.UnidadCompra}",
            $"x {p.FactorConversion:0.##}",
            $"+{p.CantidadIngresadaAlmacen:0.##} {p.UnidadAlmacen}"
        }).ToList();

        var metadatos = new List<(string etiq, string val)>
        {
            ("Orden de Compra:", string.IsNullOrWhiteSpace(ent.OrdenCompraFolio) ? "Entrada Directa" : ent.OrdenCompraFolio),
            ("Proveedor:", ent.ProveedorNombre),
            ("Proyecto / Destino:", string.IsNullOrWhiteSpace(ent.ProyectoNombre) ? "Stock General / Consumo Interno" : ent.ProyectoNombre),
            ("Tipo de Movimiento:", ent.TipoEntrada)
        };

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: "VALE DE ENTRADA A ALMACÉN",
            subtitulo: "Lebaref | Jefatura de Almacén y Control de Inventarios",
            folioCodigoBarras: ent.Folio,
            fecha: ent.Fecha,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Producto", "Recibido", "Conversión", "Ingreso a Almacén" },
            filasTabla: filas,
            totales: new List<(string, string)> { ("Total Partidas:", ent.Partidas.Count.ToString()) },
            notas: ent.Notas,
            firmaIzquierda: "Recibió y Verificó: Jefatura de Almacén",
            firmaDerecha: "Entregó: Proveedor / Transporte");

        if (soloDescargar)
        {
            GuardarDocumentoEnArchivo(owner, $"ValeEntrada_{ent.Folio}", hoja);
            return;
        }

        AbrirVentanaVistaPreviaCarta(owner, $"Vale de Entrada {ent.Folio}", hoja);
    }

    public static void MostrarVistaPreviaEImprimirSalida(Window owner, SalidaAlmacen sal, bool soloDescargar = false)
    {
        bool tieneDevoluciones = sal.TotalUnidadesDevueltas > 0;

        List<string[]> filas;
        string[] encabezados;
        List<(string, string)> totales;

        if (tieneDevoluciones)
        {
            encabezados = new[] { "#", "Código", "Descripción", "Entregado", "Devuelto", "Consumo Neto", "Unidad" };
            filas = sal.Partidas.Select((p, idx) => new[]
            {
                (idx + 1).ToString(),
                p.ProductoCodigo,
                p.ProductoNombre,
                $"{p.CantidadEntregada:0.##}",
                $"{p.CantidadDevuelta:0.##}",
                $"{p.CantidadNetaConsumida:0.##}",
                p.UnidadAlmacen
            }).ToList();

            totales = new List<(string, string)>
            {
                ("Total Partidas:", sal.Partidas.Count.ToString()),
                ("Unidades Entregadas:", $"{sal.TotalUnidadesEntregadas:0.##}"),
                ("Unidades Devueltas:", $"{sal.TotalUnidadesDevueltas:0.##}"),
                ("Consumo Neto:", $"{(sal.TotalUnidadesEntregadas - sal.TotalUnidadesDevueltas):0.##}")
            };
        }
        else
        {
            encabezados = new[] { "#", "Código", "Descripción", "Cantidad", "Unidad" };
            filas = sal.Partidas.Select((p, idx) => new[]
            {
                (idx + 1).ToString(),
                p.ProductoCodigo,
                p.ProductoNombre,
                $"{p.CantidadEntregada:0.##}",
                p.UnidadAlmacen
            }).ToList();

            totales = new List<(string, string)>
            {
                ("Total Partidas:", sal.Partidas.Count.ToString()),
                ("Total Unidades:", $"{sal.TotalUnidadesEntregadas:0.##}")
            };
        }

        string destinoMostrar = string.IsNullOrWhiteSpace(sal.ProyectoNombre) || sal.ProyectoNombre.Equals("Sin proyecto", StringComparison.OrdinalIgnoreCase)
            ? $"Consumo Interno ({sal.DepartamentoNombre})"
            : sal.ProyectoNombre;

        var metadatos = new List<(string etiq, string val)>
        {
            ("Gerencia que Autoriza:", $"Gerencia de {sal.DepartamentoNombre}"),
            ("Personal Técnico que Recibe:", sal.PersonaRecibeEfectiva),
            ("Proyecto / Destino:", destinoMostrar),
            ("Estatus:", sal.EstatusDevolucionTexto)
        };

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: tieneDevoluciones ? "VALE DE ENTREGA Y DEVOLUCIÓN DE MATERIAL" : "VALE DE ENTREGA DE MATERIAL",
            subtitulo: "Lebaref | Jefatura de Almacén (Comprobante de Trazabilidad sin Precios)",
            folioCodigoBarras: sal.Folio,
            fecha: sal.Fecha,
            metadatos: metadatos,
            encabezadosTabla: encabezados,
            filasTabla: filas,
            totales: totales,
            notas: sal.Notas,
            firmaIzquierda: "Entregó: Jefatura de Almacén",
            firmaCentro: $"Autorizó: Gerencia de {sal.DepartamentoNombre}",
            firmaDerecha: $"Recibió para Obra / Área: {sal.PersonaRecibeEfectiva}");

        if (soloDescargar)
        {
            GuardarDocumentoEnArchivo(owner, $"ValeSalida_{sal.Folio}", hoja);
            return;
        }

        AbrirVentanaVistaPreviaCarta(owner, $"Vale de Salida {sal.Folio}", hoja);
    }

    public static void MostrarVistaPreviaEImprimirCartaResponsabilidadEpp(Window owner, SalidaAlmacen sal, bool soloDescargar = false)
    {
        var catalogoDict = Data.DatabaseService.ObtenerProductos().ToDictionary(p => p.Id);

        var filas = sal.Partidas.Select((p, idx) =>
        {
            catalogoDict.TryGetValue(p.ProductoId, out var prodCat);
            string detalleEquipo = prodCat != null
                ? string.Join(" | ", new[] { prodCat.Marca, prodCat.Modelo, prodCat.Medidas, prodCat.Especificacion1 }
                    .Where(s => !string.IsNullOrWhiteSpace(s)))
                : string.Empty;
            if (string.IsNullOrWhiteSpace(detalleEquipo))
                detalleEquipo = "Estándar de Seguridad Industrial";

            return new[]
            {
                (idx + 1).ToString(),
                p.ProductoCodigo,
                p.ProductoNombre,
                detalleEquipo,
                $"{p.CantidadEntregada:0.##} {p.UnidadAlmacen}",
                "Nuevo / En buen estado"
            };
        }).ToList();

        string destinoMostrar = string.IsNullOrWhiteSpace(sal.ProyectoNombre) || sal.ProyectoNombre.Equals("Sin proyecto", StringComparison.OrdinalIgnoreCase)
            ? $"Operación General ({sal.DepartamentoNombre})"
            : sal.ProyectoNombre;

        var metadatos = new List<(string etiq, string val)>
        {
            ("Folio de Salida Vinculado:", sal.Folio),
            ("Colaborador / Técnico Receptor:", sal.PersonaRecibeEfectiva),
            ("Departamento / Gerencia:", $"Gerencia de {sal.DepartamentoNombre}"),
            ("Proyecto / Área de Asignación:", destinoMostrar),
            ("Carácter de la Entrega:", "Dotación de Equipo de Protección Personal (EPP) / Resguardo Individual")
        };

        string clausulaResponsabilidad =
            "CARTA DE ACEPTACIÓN DE RESPONSABILIDAD Y USO DE EQUIPO DE PROTECCIÓN PERSONAL (EPP):\n" +
            $"Por medio de la presente, el colaborador receptor ({sal.PersonaRecibeEfectiva}) hace constar que recibe de conformidad y en óptimas condiciones físicas los artículos de Equipo de Protección Personal y/o indumentaria de trabajo descritos en este documento, obligándose a:\n" +
            "1. Utilizar el equipo de forma obligatoria y permanente durante el desempeño de sus labores operativas y en sitio de obra, conforme a las normas de seguridad industrial de la empresa.\n" +
            "2. Conservar, limpiar y resguardar adecuadamente cada artículo asignado, reportando de inmediato a su Gerencia de Área o a Jefatura de Almacén cualquier desgaste, daño o deterioro que comprometa su seguridad.\n" +
            "3. Asumir la responsabilidad por el extravío, mal uso intencional o negligencia en el resguardo del equipo entregado, así como presentar el equipo desgastado al solicitar su reposición.";

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: "CARTA RESPONSIVA DE ENTREGA DE EPP",
            subtitulo: "Lebaref | Seguridad Industrial y Jefatura de Almacén",
            folioCodigoBarras: sal.Folio,
            fecha: sal.Fecha,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Descripción del EPP / Equipo", "Marca / Talla / Especificación", "Cantidad", "Condición" },
            filasTabla: filas,
            totales: new List<(string, string)>
            {
                ("Total Conceptos EPP:", sal.Partidas.Count.ToString()),
                ("Total Unidades Entregadas:", $"{sal.TotalUnidadesEntregadas:0.##}")
            },
            notas: sal.Notas,
            firmaIzquierda: $"Conformidad Técnico / Empleado:\n{sal.PersonaRecibeEfectiva}",
            firmaCentro: $"Autorizó Gerencia Correspondiente:\nGerencia de {sal.DepartamentoNombre}",
            firmaDerecha: "Entregó y Registró:\nJefatura de Almacén",
            clausulaLegal: clausulaResponsabilidad);

        if (soloDescargar)
        {
            GuardarDocumentoEnArchivo(owner, $"CartaResponsivaEPP_{sal.Folio}", hoja);
            return;
        }

        AbrirVentanaVistaPreviaCarta(owner, $"Carta Responsiva de EPP - {sal.Folio}", hoja);
    }

    public static void MostrarVistaPreviaEImprimirDevolucionMaterial(Window owner, SalidaAlmacen sal, EntradaAlmacen entDevolucion, bool soloDescargar = false)
    {
        var filas = sal.Partidas.Select((p, idx) =>
        {
            var partHoy = entDevolucion.Partidas.FirstOrDefault(ep => ep.ProductoId == p.ProductoId);
            decimal devHoy = partHoy?.CantidadIngresadaAlmacen ?? 0m;
            return new[]
            {
                (idx + 1).ToString(),
                p.ProductoCodigo,
                p.ProductoNombre,
                $"{p.CantidadEntregada:0.##}",
                devHoy > 0 ? $"+{devHoy:0.##}" : "0",
                $"{p.CantidadDevuelta:0.##}",
                $"{p.CantidadNetaConsumida:0.##}",
                p.UnidadAlmacen
            };
        }).ToList();

        var metadatos = new List<(string etiq, string val)>
        {
            ("Vale de Salida Origen:", $"{sal.Folio} (Fecha salida: {sal.Fecha})"),
            ("Tipo de Reingreso:", $"{entDevolucion.TipoEntrada} ({sal.EstatusDevolucionTexto})"),
            ("Personal que Devuelve:", sal.PersonaRecibeEfectiva),
            ("Gerencia Correspondiente:", $"Gerencia de {sal.DepartamentoNombre}"),
            ("Proyecto / Destino:", sal.ProyectoNombre)
        };

        decimal totalDevHoy = entDevolucion.Partidas.Sum(x => x.CantidadIngresadaAlmacen);

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: "VALE DE DEVOLUCIÓN DE MATERIAL A ALMACÉN",
            subtitulo: "Lebaref | Comprobante de Reingreso de Material (Sin Precios)",
            folioCodigoBarras: entDevolucion.Folio,
            fecha: entDevolucion.Fecha,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Descripción", "Entregado", "Devuelto Hoy", "Total Dev.", "Consumo Neto", "Unidad" },
            filasTabla: filas,
            totales: new List<(string, string)>
            {
                ("Entregado Orig.:", $"{sal.TotalUnidadesEntregadas:0.##}"),
                ("Devuelto en Vale:", $"{totalDevHoy:0.##}"),
                ("Total Devuelto:", $"{sal.TotalUnidadesDevueltas:0.##}"),
                ("Consumo Neto:", $"{(sal.TotalUnidadesEntregadas - sal.TotalUnidadesDevueltas):0.##}")
            },
            notas: entDevolucion.Notas,
            firmaIzquierda: $"Entregó / Devolvió: {sal.PersonaRecibeEfectiva}",
            firmaCentro: $"Vo.Bo.: Gerencia de {sal.DepartamentoNombre}",
            firmaDerecha: "Recibió: Jefatura de Almacén");

        if (soloDescargar)
        {
            GuardarDocumentoEnArchivo(owner, $"ValeDevolucion_{entDevolucion.Folio}", hoja);
            return;
        }

        AbrirVentanaVistaPreviaCarta(owner, $"Vale de Devolución de Material {entDevolucion.Folio} ({sal.Folio})", hoja);
    }

    public static void MostrarVistaPreviaEImprimirPrestamo(Window owner, PrestamoHerramienta pre, bool soloDescargar = false)
    {
        bool tieneDevolucion = pre.Partidas.Any(p => !string.Equals(p.EstadoDevolucion, "Pendiente", StringComparison.OrdinalIgnoreCase));
        int devueltasCount = pre.Partidas.Count(p => !string.Equals(p.EstadoDevolucion, "Pendiente", StringComparison.OrdinalIgnoreCase));
        int pendientesCount = pre.Partidas.Count - devueltasCount;

        string fechaPromesa = string.IsNullOrWhiteSpace(pre.FechaCompromiso) ? "-" : pre.FechaCompromiso;

        var filas = pre.Partidas.Select((p, idx) => new[]
        {
            (idx + 1).ToString(),
            p.CodigoIndividual,
            p.ProductoNombre,
            string.IsNullOrWhiteSpace(p.NumeroSerie) ? p.MarcaModelo : $"{p.MarcaModelo} (S/N: {p.NumeroSerie})",
            p.EstadoSalida,
            p.EstadoDevolucion,
            fechaPromesa
        }).ToList();

        var metadatos = new List<(string etiq, string val)>
        {
            ("Gerencia que Autoriza:", $"Gerencia de {pre.DepartamentoNombre}"),
            ("Personal Técnico Responsable:", pre.PersonaRecibeEfectiva),
            ("Proyecto / Destino:", pre.ProyectoNombre),
            ("Fecha Salida / Fecha Promesa:", $"{pre.FechaSalida}  |  Fecha Promesa: {fechaPromesa}"),
            ("Estatus del Préstamo:", $"{pre.Estatus} ({devueltasCount} de {pre.Partidas.Count} devueltas)")
        };

        if (!string.IsNullOrWhiteSpace(pre.FechaDevolucion))
        {
            metadatos.Add(("Última Fecha Devolución:", pre.FechaDevolucion));
        }

        var totales = new List<(string, string)>
        {
            ("Total Herramientas:", pre.Partidas.Count.ToString()),
            ("Devueltas a Almacén:", devueltasCount.ToString()),
            ("Pendientes por Devolver:", pendientesCount.ToString())
        };

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: tieneDevolucion
                ? "VALE DE DEVOLUCIÓN / RESGUARDO DE HERRAMIENTA"
                : "VALE DE PRÉSTAMO DE HERRAMIENTA",
            subtitulo: "Lebaref | Control de Activos y Herramientas de Almacén",
            folioCodigoBarras: pre.Folio,
            fecha: tieneDevolucion && !string.IsNullOrWhiteSpace(pre.FechaDevolucion) ? pre.FechaDevolucion : pre.FechaSalida,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Herramienta", "Marca / Serie", "Estado Salida", "Estado Retorno", "Fecha Promesa" },
            filasTabla: filas,
            totales: totales,
            notas: pre.Notas,
            firmaIzquierda: tieneDevolucion ? $"Devolvió: {pre.PersonaRecibeEfectiva}" : $"Recibió: {pre.PersonaRecibeEfectiva}",
            firmaCentro: $"Autorizó: Gerencia de {pre.DepartamentoNombre}",
            firmaDerecha: "Jefatura de Almacén");

        if (soloDescargar)
        {
            GuardarDocumentoEnArchivo(owner, $"ValeHerramienta_{pre.Folio}", hoja);
            return;
        }

        AbrirVentanaVistaPreviaCarta(owner, tieneDevolucion ? $"Vale de Devolución de Herramienta {pre.Folio}" : $"Vale de Préstamo {pre.Folio}", hoja);
    }

    // Dimensiones estándar fijas de Hoja Tamaño Carta (US Letter: 8.5" x 11.0" a 96 DPI)
    public const double AnchoHojaCartaDip = 816.0;
    public const double AltoHojaCartaDip = 1056.0;
    public const double MargenHojaCartaDip = 28.0;
    public const double AnchoContenidoCartaDip = AnchoHojaCartaDip - (MargenHojaCartaDip * 2); // 760 DIPs
    public const double AltoContenidoUtilCartaDip = AltoHojaCartaDip - (MargenHojaCartaDip * 2); // 1000 DIPs

    public static void MostrarVistaPreviaCatalogoCodigosCarta(Window owner, List<Producto> productos, List<HerramientaActivo> herramientas)
    {
        var tarjetas = new List<(string codigo, string titulo, string subtitulo, string colorHex)>();

        foreach (var p in productos)
        {
            tarjetas.Add((
                p.CodigoEfectivo,
                p.Nombre,
                $"Stock: {p.StockActual:0.##} {p.UnidadAlmacen} | Ubic: {p.Ubicacion}",
                "#0078D4"));
        }

        foreach (var h in herramientas)
        {
            tarjetas.Add((
                h.CodigoIndividual,
                $"[HERRAMIENTA] {h.ProductoNombre}",
                $"{h.MarcaModelo} | Serie: {h.NumeroSerie}",
                "#5C2D91"));
        }

        // 3 columnas x 6 filas = 18 etiquetas por cada hoja Tamaño Carta (816 x 1056 DIPs)
        const int tarjetasPorHoja = 18;
        int totalPaginas = Math.Max(1, (int)Math.Ceiling(tarjetas.Count / (double)tarjetasPorHoja));
        var paginasCarta = new List<FrameworkElement>();
        var contenedorPaginas = new StackPanel { Background = Brushes.Transparent };

        for (int pIndex = 0; pIndex < totalPaginas; pIndex++)
        {
            var dockHoja = new DockPanel
            {
                Width = AnchoContenidoCartaDip,
                Height = AltoContenidoUtilCartaDip,
                LastChildFill = true
            };

            var header = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                BorderThickness = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(0, 0, 0, 10),
                Margin = new Thickness(0, 0, 0, 14)
            };

            var hStack = new StackPanel();
            hStack.Children.Add(new TextBlock
            {
                Text = "CATÁLOGO DE CÓDIGOS DE BARRAS",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            });
            hStack.Children.Add(new TextBlock
            {
                Text = $"Lebaref | Departamento de Compras — Hoja {pIndex + 1} de {totalPaginas}",
                FontSize = 11.5,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 3, 0, 0)
            });
            header.Child = hStack;
            DockPanel.SetDock(header, Dock.Top);
            dockHoja.Children.Add(header);

            var pie = new TextBlock
            {
                Text = $"Lebaref | Catálogo de Códigos de Barras — Página {pIndex + 1} de {totalPaginas}",
                FontSize = 9.5,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };
            DockPanel.SetDock(pie, Dock.Bottom);
            dockHoja.Children.Add(pie);

            var wrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top
            };

            var lote = tarjetas.Skip(pIndex * tarjetasPorHoja).Take(tarjetasPorHoja);
            foreach (var item in lote)
            {
                wrap.Children.Add(CrearTarjetaCodigoImprimible(item.codigo, item.titulo, item.subtitulo, item.colorHex));
            }

            dockHoja.Children.Add(wrap);

            var hojaCarta = EnvolverEnHojaCartaEstandar(dockHoja);
            paginasCarta.Add(hojaCarta);

            var sombraHoja = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.Silver,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, pIndex < totalPaginas - 1 ? 22 : 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = hojaCarta
            };
            contenedorPaginas.Children.Add(sombraHoja);
        }

        var filasCsv = tarjetas
            .Select(t => new[] { t.codigo, t.titulo, t.subtitulo })
            .ToList();

        contenedorPaginas.Tag = new DatosExportablesDocumento
        {
            TituloDocumento = "CATÁLOGO DE CÓDIGOS DE BARRAS",
            Subtitulo = "Lebaref | Departamento de Compras",
            Folio = "CATALOGO-BARRAS",
            Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            EncabezadosTabla = new[] { "Código", "Artículo / Herramienta", "Detalle / Ubicación" },
            FilasTabla = filasCsv,
            PaginasCarta = paginasCarta
        };

        AbrirVentanaVistaPreviaCarta(owner, "Catálogo de Códigos de Barras en Hoja Carta", contenedorPaginas);
    }

    private static Border CrearTarjetaCodigoImprimible(string codigo, string titulo, string subtitulo, string colorBordeHex)
    {
        var card = new Border
        {
            Width = 238,
            Margin = new Thickness(6),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorBordeHex)!,
            BorderThickness = new Thickness(1.5),
            Background = Brushes.White
        };

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = titulo,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = Brushes.Black,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        sp.Children.Add(new TextBlock
        {
            Text = subtitulo,
            FontSize = 9.5,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 6),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        sp.Children.Add(new Image
        {
            Source = CrearImagenCodigoBarras(codigo),
            Height = 46,
            Stretch = Stretch.Fill
        });
        sp.Children.Add(new TextBlock
        {
            Text = codigo,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });

        card.Child = sp;
        return card;
    }

    private static Border EnvolverEnHojaCartaEstandar(UIElement contenidoInterno)
    {
        return new Border
        {
            Width = AnchoHojaCartaDip,
            Height = AltoHojaCartaDip,
            Background = Brushes.White,
            Padding = new Thickness(MargenHojaCartaDip),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Child = contenidoInterno
        };
    }

    private static double MedirAltoElementoCarta(FrameworkElement elemento)
    {
        elemento.Width = AnchoContenidoCartaDip;
        elemento.Measure(new Size(AnchoContenidoCartaDip, double.PositiveInfinity));
        return Math.Ceiling(elemento.DesiredSize.Height);
    }

    private static StackPanel CrearEncabezadoHojaDocumento(
        string tituloDocumento,
        string subtitulo,
        string folioCodigoBarras,
        string fecha,
        List<(string etiq, string val)> metadatos,
        bool esContinuacion)
    {
        var panel = new StackPanel { Width = AnchoContenidoCartaDip };

        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = esContinuacion ? $"{tituloDocumento} (Continuación)" : tituloDocumento,
            FontSize = esContinuacion ? 17 : 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            TextWrapping = TextWrapping.Wrap
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = subtitulo,
            FontSize = 11,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 3)
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"Fecha de Emisión: {fecha}   |   Folio: {folioCodigoBarras}",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black
        });
        Grid.SetColumn(titleStack, 0);
        topGrid.Children.Add(titleStack);

        var barcodeBorder = new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Background = Brushes.White
        };
        var bcStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        bcStack.Children.Add(new Image
        {
            Source = CrearImagenCodigoBarras(folioCodigoBarras),
            Width = esContinuacion ? 180 : 210,
            Height = esContinuacion ? 38 : 46,
            Stretch = Stretch.Fill
        });
        bcStack.Children.Add(new TextBlock
        {
            Text = folioCodigoBarras,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });
        barcodeBorder.Child = bcStack;
        Grid.SetColumn(barcodeBorder, 1);
        topGrid.Children.Add(barcodeBorder);

        panel.Children.Add(topGrid);

        panel.Children.Add(new Border
        {
            Height = 2,
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        if (!esContinuacion && metadatos.Count > 0)
        {
            var metaBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var metaGrid = new Grid();
            metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < metadatos.Count; i++)
            {
                metaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var lbl = new TextBlock
                {
                    Text = metadatos[i].etiq,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                    Margin = new Thickness(0, 2, 8, 2)
                };
                var val = new TextBlock
                {
                    Text = metadatos[i].val,
                    FontSize = 11,
                    Foreground = Brushes.Black,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                Grid.SetRow(lbl, i);
                Grid.SetColumn(lbl, 0);
                Grid.SetRow(val, i);
                Grid.SetColumn(val, 1);
                metaGrid.Children.Add(lbl);
                metaGrid.Children.Add(val);
            }
            metaBorder.Child = metaGrid;
            panel.Children.Add(metaBorder);
        }

        return panel;
    }

    private static Grid CrearFilaTablaDocumento(string[] encabezadosTabla, string[] valores, bool esEncabezado, int indiceFilaOriginal)
    {
        var rowGrid = new Grid { Width = AnchoContenidoCartaDip };
        for (int c = 0; c < encabezadosTabla.Length; c++)
        {
            double starWidth = c switch
            {
                0 when encabezadosTabla[0] == "#" => 0.38,
                1 when encabezadosTabla[0] == "#" && encabezadosTabla.Length >= 7 => 0.85,
                2 when encabezadosTabla.Length >= 7 => 2.05,
                2 => 2.4,
                3 when encabezadosTabla.Length >= 7 && encabezadosTabla[3].StartsWith("Cant", StringComparison.OrdinalIgnoreCase) => 0.72,
                _ => 1.0
            };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(starWidth, GridUnitType.Star)
            });
        }

        for (int c = 0; c < encabezadosTabla.Length; c++)
        {
            string texto = c < valores.Length ? (valores[c] ?? string.Empty) : string.Empty;
            if (esEncabezado)
            {
                var hCell = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    Padding = new Thickness(6, 5, 6, 5),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(0, 0, 1, 0)
                };
                hCell.Child = new TextBlock
                {
                    Text = texto,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(hCell, c);
                rowGrid.Children.Add(hCell);
            }
            else
            {
                var cell = new Border
                {
                    Background = indiceFilaOriginal % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6, 4.5, 6, 4.5)
                };
                cell.Child = new TextBlock
                {
                    Text = texto,
                    Foreground = Brushes.Black,
                    FontSize = 10.2,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(cell, c);
                rowGrid.Children.Add(cell);
            }
        }

        return rowGrid;
    }

    private static StackPanel CrearBloqueCierreDocumento(
        List<(string etiq, string val)> totales,
        string notas,
        string firmaIzquierda,
        string firmaDerecha,
        string? firmaCentro,
        string? clausulaLegal,
        string? tituloTablaSecundaria = null,
        string[]? encabezadosTablaSecundaria = null,
        List<string[]>? filasTablaSecundaria = null)
    {
        var bloque = new StackPanel
        {
            Width = AnchoContenidoCartaDip,
            Margin = new Thickness(0, 10, 0, 0)
        };

        if (encabezadosTablaSecundaria != null && encabezadosTablaSecundaria.Length > 0 &&
            filasTablaSecundaria != null && filasTablaSecundaria.Count > 0)
        {
            var secContainer = new StackPanel { Margin = new Thickness(0, 2, 0, 14) };
            if (!string.IsNullOrWhiteSpace(tituloTablaSecundaria))
            {
                secContainer.Children.Add(new TextBlock
                {
                    Text = tituloTablaSecundaria,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    Margin = new Thickness(0, 0, 0, 5)
                });
            }

            secContainer.Children.Add(CrearFilaTablaDocumento(encabezadosTablaSecundaria, encabezadosTablaSecundaria, true, 0));
            for (int i = 0; i < filasTablaSecundaria.Count; i++)
            {
                var filaSec = CrearFilaTablaDocumento(encabezadosTablaSecundaria, filasTablaSecundaria[i], false, i);
                if (filasTablaSecundaria[i].Length > 0 && filasTablaSecundaria[i][0] == "TOT")
                {
                    foreach (var child in filaSec.Children.OfType<Border>())
                    {
                        child.Background = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                        if (child.Child is TextBlock tb)
                            tb.FontWeight = FontWeights.Bold;
                    }
                }
                secContainer.Children.Add(filaSec);
            }
            bloque.Children.Add(secContainer);
        }

        var bottomGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var notesBox = new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 12, 0)
        };
        notesBox.Child = new TextBlock
        {
            Text = $"Observaciones / Notas:\n{(string.IsNullOrWhiteSpace(notas) ? "Sin observaciones adicionales." : notas)}",
            FontSize = 10.2,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(notesBox, 0);
        bottomGrid.Children.Add(notesBox);

        var totStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (etiq, val) in totales)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 0, 2)
            };
            row.Children.Add(new TextBlock
            {
                Text = etiq + " ",
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = Brushes.DarkSlateGray
            });
            row.Children.Add(new TextBlock
            {
                Text = val,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = Brushes.Black,
                Width = 115,
                TextAlignment = TextAlignment.Right
            });
            totStack.Children.Add(row);
        }
        Grid.SetColumn(totStack, 1);
        bottomGrid.Children.Add(totStack);
        bloque.Children.Add(bottomGrid);

        if (!string.IsNullOrWhiteSpace(clausulaLegal))
        {
            var legalBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 22)
            };
            legalBox.Child = new TextBlock
            {
                Text = clausulaLegal,
                FontSize = 9.8,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 14
            };
            bloque.Children.Add(legalBox);
        }

        var sigGrid = new Grid { Margin = new Thickness(0, 24, 0, 4) };
        if (!string.IsNullOrWhiteSpace(firmaCentro))
        {
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sig1 = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0) };
            sig1.Child = new TextBlock { Text = firmaIzquierda, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, FontSize = 10.2, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(sig1, 0);
            sigGrid.Children.Add(sig1);

            var sig2 = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0) };
            sig2.Child = new TextBlock { Text = firmaCentro, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, FontSize = 10.2, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(sig2, 2);
            sigGrid.Children.Add(sig2);

            var sig3 = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0) };
            sig3.Child = new TextBlock { Text = firmaDerecha, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, FontSize = 10.2, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(sig3, 4);
            sigGrid.Children.Add(sig3);
        }
        else
        {
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sig1 = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0) };
            sig1.Child = new TextBlock { Text = firmaIzquierda, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(sig1, 0);
            sigGrid.Children.Add(sig1);

            var sig2 = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0) };
            sig2.Child = new TextBlock { Text = firmaDerecha, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(sig2, 2);
            sigGrid.Children.Add(sig2);
        }

        bloque.Children.Add(sigGrid);
        return bloque;
    }

    private static FrameworkElement ConstruirHojaCartaDocumento(
        string tituloDocumento,
        string subtitulo,
        string folioCodigoBarras,
        string fecha,
        List<(string etiq, string val)> metadatos,
        string[] encabezadosTabla,
        List<string[]> filasTabla,
        List<(string etiq, string val)> totales,
        string notas,
        string firmaIzquierda,
        string firmaDerecha,
        string? firmaCentro = null,
        string? clausulaLegal = null,
        string? tituloTablaSecundaria = null,
        string[]? encabezadosTablaSecundaria = null,
        List<string[]>? filasTablaSecundaria = null)
    {
        const double altoPiePagina = 26.0;
        double altoDisponiblePorHoja = AltoContenidoUtilCartaDip - altoPiePagina; // 974 DIPs útiles por hoja

        // Medimos encabezados de Página 1 y de Páginas de continuación
        var muestraEncabezadoP1 = CrearEncabezadoHojaDocumento(tituloDocumento, subtitulo, folioCodigoBarras, fecha, metadatos, false);
        double altoEncabezadoP1 = MedirAltoElementoCarta(muestraEncabezadoP1);

        var muestraEncabezadoCont = CrearEncabezadoHojaDocumento(tituloDocumento, subtitulo, folioCodigoBarras, fecha, metadatos, true);
        double altoEncabezadoCont = MedirAltoElementoCarta(muestraEncabezadoCont);

        var muestraCabeceraTabla = CrearFilaTablaDocumento(encabezadosTabla, encabezadosTabla, true, 0);
        double altoCabeceraTabla = MedirAltoElementoCarta(muestraCabeceraTabla);

        var bloqueCierre = CrearBloqueCierreDocumento(totales, notas, firmaIzquierda, firmaDerecha, firmaCentro, clausulaLegal, tituloTablaSecundaria, encabezadosTablaSecundaria, filasTablaSecundaria);
        double altoBloqueCierre = MedirAltoElementoCarta(bloqueCierre);

        // Pre-medimos cada fila de datos para paginar sin cortar ninguna fila a la mitad
        var altosFilas = new double[filasTabla.Count];
        for (int i = 0; i < filasTabla.Count; i++)
        {
            var filaVisual = CrearFilaTablaDocumento(encabezadosTabla, filasTabla[i], false, i);
            altosFilas[i] = MedirAltoElementoCarta(filaVisual);
        }

        // Planificamos qué filas van en cada página Tamaño Carta y en cuál va el bloque de cierre
        var definicionPaginas = new List<(bool esContinuacion, List<int> indicesFilas, bool incluyeCierre)>();

        int filaActual = 0;
        bool esPrimeraPagina = true;

        if (filasTabla.Count == 0)
        {
            definicionPaginas.Add((false, new List<int>(), true));
        }
        else
        {
            while (filaActual < filasTabla.Count)
            {
                double altoEnc = esPrimeraPagina ? altoEncabezadoP1 : altoEncabezadoCont;
                double presupuestoTabla = altoDisponiblePorHoja - altoEnc - altoCabeceraTabla - 8.0;
                var filasDeEstaPagina = new List<int>();
                double acumuladoFilas = 0;

                while (filaActual < filasTabla.Count)
                {
                    double altoFila = altosFilas[filaActual];
                    if (filasDeEstaPagina.Count > 0 && acumuladoFilas + altoFila > presupuestoTabla)
                    {
                        break;
                    }
                    filasDeEstaPagina.Add(filaActual);
                    acumuladoFilas += altoFila;
                    filaActual++;
                }

                bool esUltimaTandaDeFilas = filaActual >= filasTabla.Count;
                bool cabeCierreEnEstaHoja = false;

                if (esUltimaTandaDeFilas)
                {
                    double espacioRestante = altoDisponiblePorHoja - altoEnc - altoCabeceraTabla - acumuladoFilas - 8.0;
                    cabeCierreEnEstaHoja = espacioRestante >= altoBloqueCierre;
                }

                definicionPaginas.Add((!esPrimeraPagina, filasDeEstaPagina, cabeCierreEnEstaHoja));
                esPrimeraPagina = false;

                if (esUltimaTandaDeFilas && !cabeCierreEnEstaHoja)
                {
                    // Agregamos una hoja Carta adicional para que Totales, Notas, Cláusula y Firmas salgan 100% completos
                    definicionPaginas.Add((true, new List<int>(), true));
                }
            }
        }

        int totalPaginas = definicionPaginas.Count;
        var paginasCarta = new List<FrameworkElement>();
        var contenedorPaginas = new StackPanel { Background = Brushes.Transparent };

        for (int pIndex = 0; pIndex < totalPaginas; pIndex++)
        {
            var def = definicionPaginas[pIndex];

            var dockHoja = new DockPanel
            {
                Width = AnchoContenidoCartaDip,
                Height = AltoContenidoUtilCartaDip,
                LastChildFill = true
            };

            var piePagina = new TextBlock
            {
                Text = $"Lebaref | Departamento de Compras — Folio: {folioCodigoBarras} — Página {pIndex + 1} de {totalPaginas}",
                FontSize = 9.5,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            DockPanel.SetDock(piePagina, Dock.Bottom);
            dockHoja.Children.Add(piePagina);

            var cuerpoHoja = new StackPanel
            {
                Width = AnchoContenidoCartaDip,
                VerticalAlignment = VerticalAlignment.Top
            };

            var encabezadoVisual = CrearEncabezadoHojaDocumento(
                tituloDocumento,
                subtitulo,
                folioCodigoBarras,
                fecha,
                metadatos,
                def.esContinuacion);
            cuerpoHoja.Children.Add(encabezadoVisual);

            if (def.indicesFilas.Count > 0 || pIndex == 0)
            {
                var tablaPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                tablaPanel.Children.Add(CrearFilaTablaDocumento(encabezadosTabla, encabezadosTabla, true, 0));

                foreach (int idxFila in def.indicesFilas)
                {
                    tablaPanel.Children.Add(CrearFilaTablaDocumento(encabezadosTabla, filasTabla[idxFila], false, idxFila));
                }

                if (!def.incluyeCierre && pIndex < totalPaginas - 1)
                {
                    tablaPanel.Children.Add(new TextBlock
                    {
                        Text = $"Continúa en la página {pIndex + 2} de {totalPaginas}...",
                        FontSize = 9.5,
                        FontStyle = FontStyles.Italic,
                        Foreground = Brushes.DimGray,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 6, 0, 0)
                    });
                }

                cuerpoHoja.Children.Add(tablaPanel);
            }

            if (def.incluyeCierre)
            {
                cuerpoHoja.Children.Add(CrearBloqueCierreDocumento(
                    totales,
                    notas,
                    firmaIzquierda,
                    firmaDerecha,
                    firmaCentro,
                    clausulaLegal,
                    tituloTablaSecundaria,
                    encabezadosTablaSecundaria,
                    filasTablaSecundaria));
            }

            dockHoja.Children.Add(cuerpoHoja);

            var hojaCarta = EnvolverEnHojaCartaEstandar(dockHoja);
            paginasCarta.Add(hojaCarta);

            var sombraHoja = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.Silver,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, pIndex < totalPaginas - 1 ? 22 : 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = hojaCarta
            };
            contenedorPaginas.Children.Add(sombraHoja);
        }

        contenedorPaginas.Tag = new DatosExportablesDocumento
        {
            TituloDocumento = tituloDocumento,
            Subtitulo = subtitulo,
            Folio = folioCodigoBarras,
            Fecha = fecha,
            Metadatos = metadatos,
            EncabezadosTabla = encabezadosTabla,
            FilasTabla = filasTabla,
            TituloTablaSecundaria = tituloTablaSecundaria,
            EncabezadosTablaSecundaria = encabezadosTablaSecundaria ?? Array.Empty<string>(),
            FilasTablaSecundaria = filasTablaSecundaria ?? new List<string[]>(),
            Totales = totales,
            Notas = notas,
            ClausulaLegal = clausulaLegal,
            PaginasCarta = paginasCarta
        };

        return contenedorPaginas;
    }

    private sealed class DatosExportablesDocumento
    {
        public string TituloDocumento { get; init; } = string.Empty;
        public string Subtitulo { get; init; } = string.Empty;
        public string Folio { get; init; } = string.Empty;
        public string Fecha { get; init; } = string.Empty;
        public List<(string etiq, string val)> Metadatos { get; init; } = new();
        public string[] EncabezadosTabla { get; init; } = Array.Empty<string>();
        public List<string[]> FilasTabla { get; init; } = new();
        public string? TituloTablaSecundaria { get; init; }
        public string[] EncabezadosTablaSecundaria { get; init; } = Array.Empty<string>();
        public List<string[]> FilasTablaSecundaria { get; init; } = new();
        public List<(string etiq, string val)> Totales { get; init; } = new();
        public string Notas { get; init; } = string.Empty;
        public string? ClausulaLegal { get; init; }
        public List<FrameworkElement> PaginasCarta { get; init; } = new();
    }

    private static List<FrameworkElement> ObtenerPaginasCarta(FrameworkElement contenidoHojaCarta)
    {
        if (contenidoHojaCarta.Tag is DatosExportablesDocumento datos && datos.PaginasCarta.Count > 0)
        {
            return datos.PaginasCarta;
        }
        return new List<FrameworkElement> { contenidoHojaCarta };
    }

    // Paginador multipágina oficial para que PrintDialog imprima todas las hojas Tamaño Carta completas
    private sealed class PaginadorHojasCarta : DocumentPaginator
    {
        private readonly IReadOnlyList<FrameworkElement> _paginas;
        private Size _pageSize;

        public PaginadorHojasCarta(IReadOnlyList<FrameworkElement> paginas, Size pageSize)
        {
            _paginas = paginas;
            _pageSize = pageSize.Width > 0 && pageSize.Height > 0
                ? pageSize
                : new Size(AnchoHojaCartaDip, AltoHojaCartaDip);
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => _paginas.Count;

        public override Size PageSize
        {
            get => _pageSize;
            set => _pageSize = value;
        }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            var rtb = RenderizarElementoHojaCartaBitmap(_paginas[pageNumber], 192.0);

            var img = new Image
            {
                Source = rtb,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var contenedorImpresion = new Border
            {
                Width = _pageSize.Width,
                Height = _pageSize.Height,
                Background = Brushes.White,
                Child = img
            };

            contenedorImpresion.Measure(_pageSize);
            contenedorImpresion.Arrange(new Rect(new Point(0, 0), _pageSize));
            contenedorImpresion.UpdateLayout();

            return new DocumentPage(contenedorImpresion, _pageSize, new Rect(_pageSize), new Rect(_pageSize));
        }
    }

    private static void AbrirVentanaVistaPreviaCarta(Window owner, string tituloVentana, FrameworkElement contenidoHojaCarta)
    {
        var paginas = ObtenerPaginasCarta(contenidoHojaCarta);
        int numHojas = paginas.Count;

        var win = new Window
        {
            Title = $"Lebaref | {tituloVentana} ({numHojas} {(numHojas == 1 ? "Hoja Carta" : "Hojas Carta")})",
            Width = 910,
            Height = 780,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = new SolidColorBrush(Color.FromRgb(226, 232, 240))
        };

        var root = new DockPanel();

        var topBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Padding = new Thickness(16, 10, 16, 10)
        };
        var topStack = new DockPanel();

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var btnDescargar = new Button
        {
            Content = "Descargar Archivo (PDF / PNG / Excel)",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnDescargar.Click += (_, _) =>
        {
            GuardarDocumentoEnArchivo(win, tituloVentana, contenidoHojaCarta);
        };
        actionsPanel.Children.Add(btnDescargar);

        var btnPrint = new Button
        {
            Content = numHojas > 1 ? $"Imprimir ({numHojas} Hojas Carta)" : "Imprimir (Tamaño Carta)",
            Padding = new Thickness(18, 7, 18, 7),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnPrint.Click += (_, _) =>
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                var tamanoImpresora = new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
                var paginador = new PaginadorHojasCarta(paginas, tamanoImpresora);
                dlg.PrintDocument(paginador, tituloVentana);
            }
        };
        actionsPanel.Children.Add(btnPrint);

        DockPanel.SetDock(actionsPanel, Dock.Right);
        topStack.Children.Add(actionsPanel);

        var lblInfo = new TextBlock
        {
            Text = $"{tituloVentana}   •   Formato Carta (8.5\" x 11\") — {numHojas} {(numHojas == 1 ? "página" : "páginas")}",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        topStack.Children.Add(lblInfo);

        topBar.Child = topStack;
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(20)
        };

        contenidoHojaCarta.HorizontalAlignment = HorizontalAlignment.Center;
        scroll.Content = contenidoHojaCarta;
        root.Children.Add(scroll);

        win.Content = root;
        win.ShowDialog();
    }

    public static void GuardarDocumentoEnArchivo(Window owner, string nombreBase, FrameworkElement contenidoHojaCarta)
    {
        string nombreLimpio = string.Concat(nombreBase.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(nombreLimpio))
            nombreLimpio = $"Documento_{DateTime.Now:yyyyMMdd_HHmm}";

        var dlg = new SaveFileDialog
        {
            Title = "Descargar / Guardar Documento",
            FileName = $"{nombreLimpio}.pdf",
            DefaultExt = ".pdf",
            Filter = "Documento PDF Tamaño Carta (*.pdf)|*.pdf|Imagen PNG de Alta Resolución (*.png)|*.png|Archivo CSV para Excel (*.csv)|*.csv"
        };

        if (dlg.ShowDialog(owner) != true)
            return;

        string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
        try
        {
            if (ext == ".csv")
            {
                ExportarDocumentoComoCsv(dlg.FileName, contenidoHojaCarta);
            }
            else if (ext == ".png")
            {
                ExportarDocumentoComoPng(dlg.FileName, contenidoHojaCarta);
            }
            else
            {
                ExportarDocumentoComoPdf(dlg.FileName, contenidoHojaCarta);
            }

            var abrir = DialogoFluent.Show(owner,
                $"Archivo guardado correctamente en:\n{dlg.FileName}\n\n¿Desea abrir el archivo descargado ahora?",
                "Archivo Descargado",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (abrir == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dlg.FileName,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            DialogoFluent.Show(owner,
                $"No se pudo guardar el archivo:\n{ex.Message}",
                "Error al descargar archivo",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static RenderTargetBitmap RenderizarElementoHojaCartaBitmap(FrameworkElement hojaCarta, double dpi = 192.0)
    {
        double widthDip = AnchoHojaCartaDip;
        double heightDip = AltoHojaCartaDip;

        hojaCarta.Measure(new Size(widthDip, heightDip));
        hojaCarta.Arrange(new Rect(0, 0, widthDip, heightDip));
        hojaCarta.UpdateLayout();

        int pixelWidth = Math.Max(1, (int)Math.Round(widthDip * dpi / 96.0));
        int pixelHeight = Math.Max(1, (int)Math.Round(heightDip * dpi / 96.0));

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthDip, heightDip));
            var vb = new VisualBrush(hojaCarta)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
            dc.DrawRectangle(vb, null, new Rect(0, 0, widthDip, heightDip));
        }

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    private static void ExportarDocumentoComoPng(string rutaArchivo, FrameworkElement contenidoHojaCarta)
    {
        var paginas = ObtenerPaginasCarta(contenidoHojaCarta);
        const double dpi = 192.0;

        if (paginas.Count == 1)
        {
            var rtb = RenderizarElementoHojaCartaBitmap(paginas[0], dpi);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = new FileStream(rutaArchivo, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
            return;
        }

        // Si tiene múltiples páginas, las apilamos verticalmente con una línea divisoria gris para que el PNG contenga todas las hojas completas
        var bitmaps = paginas.Select(p => RenderizarElementoHojaCartaBitmap(p, dpi)).ToList();
        int widthPx = bitmaps[0].PixelWidth;
        int gapPx = 24;
        int totalHeightPx = bitmaps.Sum(b => b.PixelHeight) + (gapPx * (bitmaps.Count - 1));

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(226, 232, 240)), null, new Rect(0, 0, widthPx, totalHeightPx));
            int yOffset = 0;
            foreach (var bmp in bitmaps)
            {
                dc.DrawImage(bmp, new Rect(0, yOffset, bmp.PixelWidth, bmp.PixelHeight));
                yOffset += bmp.PixelHeight + gapPx;
            }
        }

        var combinado = new RenderTargetBitmap(widthPx, totalHeightPx, 96.0, 96.0, PixelFormats.Pbgra32);
        combinado.Render(dv);

        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(combinado));
        using var fsOut = new FileStream(rutaArchivo, FileMode.Create, FileAccess.Write);
        pngEncoder.Save(fsOut);
    }

    private static void ExportarDocumentoComoPdf(string rutaArchivo, FrameworkElement contenidoHojaCarta)
    {
        var paginas = ObtenerPaginasCarta(contenidoHojaCarta);

        // Página tamaño Carta exacta en puntos PDF (72 pt/in): 612 x 792 pt (8.5" x 11.0")
        const string pageWStr = "612";
        const string pageHStr = "792";

        using var fs = new FileStream(rutaArchivo, FileMode.Create, FileAccess.Write);
        var offsets = new List<long>();

        void WriteAscii(string text)
        {
            byte[] b = Encoding.ASCII.GetBytes(text);
            fs.Write(b, 0, b.Length);
        }

        WriteAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

        // Objeto 1: Catalog
        offsets.Add(fs.Position);
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        // Cada página pIndex (0..N-1) utiliza 3 objetos PDF:
        // pageObjId = 3 + (pIndex * 3)
        // imgObjId  = 4 + (pIndex * 3)
        // cntObjId  = 5 + (pIndex * 3)
        var kidsRefs = Enumerable.Range(0, paginas.Count)
            .Select(i => $"{3 + (i * 3)} 0 R");

        // Objeto 2: Pages (raíz de todas las hojas Tamaño Carta)
        offsets.Add(fs.Position);
        WriteAscii($"2 0 obj\n<< /Type /Pages /Kids [{string.Join(" ", kidsRefs)}] /Count {paginas.Count} >>\nendobj\n");

        for (int i = 0; i < paginas.Count; i++)
        {
            var rtb = RenderizarElementoHojaCartaBitmap(paginas[i], 192.0);
            var jpegEncoder = new JpegBitmapEncoder { QualityLevel = 94 };
            jpegEncoder.Frames.Add(BitmapFrame.Create(rtb));

            using var msImg = new MemoryStream();
            jpegEncoder.Save(msImg);
            byte[] jpegBytes = msImg.ToArray();

            int imgW = rtb.PixelWidth;
            int imgH = rtb.PixelHeight;

            int pageObjId = 3 + (i * 3);
            int imgObjId = 4 + (i * 3);
            int cntObjId = 5 + (i * 3);

            // Page object
            offsets.Add(fs.Position);
            WriteAscii($"{pageObjId} 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /XObject << /Im0 {imgObjId} 0 R >> >> /MediaBox [0 0 {pageWStr} {pageHStr}] /Contents {cntObjId} 0 R >>\nendobj\n");

            // Image XObject
            offsets.Add(fs.Position);
            WriteAscii($"{imgObjId} 0 obj\n<< /Type /XObject /Subtype /Image /Width {imgW} /Height {imgH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
            fs.Write(jpegBytes, 0, jpegBytes.Length);
            WriteAscii("\nendstream\nendobj\n");

            // Content stream
            string contentCmd = $"q\n{pageWStr} 0 0 {pageHStr} 0 0 cm\n/Im0 Do\nQ\n";
            byte[] contentBytes = Encoding.ASCII.GetBytes(contentCmd);
            offsets.Add(fs.Position);
            WriteAscii($"{cntObjId} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
            fs.Write(contentBytes, 0, contentBytes.Length);
            WriteAscii("endstream\nendobj\n");
        }

        // Tabla xref
        long xrefPos = fs.Position;
        WriteAscii($"xref\n0 {offsets.Count + 1}\n");
        WriteAscii("0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            WriteAscii($"{off:D10} 00000 n \n");
        }

        WriteAscii($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
    }

    private static void ExportarDocumentoComoCsv(string rutaArchivo, FrameworkElement contenidoHojaCarta)
    {
        var sb = new StringBuilder();
        if (contenidoHojaCarta.Tag is DatosExportablesDocumento datos)
        {
            sb.AppendLine($"\"{datos.TituloDocumento.Replace("\"", "\"\"")}\"");
            sb.AppendLine($"\"Folio:\",\"{datos.Folio}\",\"Fecha:\",\"{datos.Fecha}\"");
            foreach (var (etiq, val) in datos.Metadatos)
            {
                sb.AppendLine($"\"{etiq.Replace("\"", "\"\"")}\",\"{val.Replace("\"", "\"\"")}\"");
            }
            sb.AppendLine();

            if (datos.EncabezadosTabla.Length > 0)
            {
                sb.AppendLine(string.Join(",", datos.EncabezadosTabla.Select(h => $"\"{h.Replace("\"", "\"\"")}\"")));
            }
            foreach (var fila in datos.FilasTabla)
            {
                sb.AppendLine(string.Join(",", fila.Select(c => $"\"{(c ?? string.Empty).Replace("\"", "\"\"")}\"")));
            }
            sb.AppendLine();

            if (datos.EncabezadosTablaSecundaria.Length > 0 && datos.FilasTablaSecundaria.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(datos.TituloTablaSecundaria))
                    sb.AppendLine($"\"{datos.TituloTablaSecundaria.Replace("\"", "\"\"")}\"");
                sb.AppendLine(string.Join(",", datos.EncabezadosTablaSecundaria.Select(h => $"\"{h.Replace("\"", "\"\"")}\"")));
                foreach (var filaSec in datos.FilasTablaSecundaria)
                {
                    sb.AppendLine(string.Join(",", filaSec.Select(c => $"\"{(c ?? string.Empty).Replace("\"", "\"\"")}\"")));
                }
                sb.AppendLine();
            }

            foreach (var (etiq, val) in datos.Totales)
            {
                sb.AppendLine($"\"{etiq.Replace("\"", "\"\"")}\",\"{val.Replace("\"", "\"\"")}\"");
            }
            if (!string.IsNullOrWhiteSpace(datos.Notas))
            {
                sb.AppendLine($"\"Observaciones:\",\"{datos.Notas.Replace("\"", "\"\"")}\"");
            }
        }
        else
        {
            sb.AppendLine("Documento exportado");
        }

        File.WriteAllText(rutaArchivo, sb.ToString(), new UTF8Encoding(true));
    }
}
