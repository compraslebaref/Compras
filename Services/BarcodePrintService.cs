using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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

    public static void MostrarVistaPreviaEImprimirOrdenCompra(Window owner, OrdenCompra oc)
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
            ("Proyecto Destino:", oc.ProyectoNombre),
            ("Departamento / Solicitante:", $"{oc.DepartamentoNombre} — {oc.EncargadoNombre}"),
            ("Solicitud / Comparativa:", $"{oc.SolicitudFolio} / {oc.CotizacionFolio}"),
            ("Tiempo de Entrega:", $"{oc.TiempoEntregaDias} días hábiles"),
            ("Estatus de Recepción:", oc.Estatus)
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
            subtitulo: "Lebaref | Departamento de Compras",
            folioCodigoBarras: oc.Folio,
            fecha: oc.Fecha,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Descripción", "Cantidad", "P.U. s/IVA", "P.U. c/IVA", "Total" },
            filasTabla: filas,
            totales: totales,
            notas: oc.Notas,
            firmaIzquierda: "Autorizó Compras",
            firmaDerecha: "Recepción Almacén / Proveedor");

        AbrirVentanaVistaPreviaCarta(owner, $"Orden de Compra {oc.Folio}", hoja);
    }

    public static void MostrarVistaPreviaEImprimirEntrada(Window owner, EntradaAlmacen ent)
    {
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
            ("Proyecto:", ent.ProyectoNombre),
            ("Tipo:", ent.TipoEntrada)
        };

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: "VALE DE ENTRADA",
            subtitulo: "Lebaref | Departamento de Compras",
            folioCodigoBarras: ent.Folio,
            fecha: ent.Fecha,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Producto", "Recibido", "Conversión", "Ingreso a Almacén" },
            filasTabla: filas,
            totales: new List<(string, string)> { ("Total Partidas:", ent.Partidas.Count.ToString()) },
            notas: ent.Notas,
            firmaIzquierda: "Recibió Almacén",
            firmaDerecha: "Entregó");

        AbrirVentanaVistaPreviaCarta(owner, $"Vale de Entrada {ent.Folio}", hoja);
    }

    public static void MostrarVistaPreviaEImprimirSalida(Window owner, SalidaAlmacen sal)
    {
        var filas = sal.Partidas.Select((p, idx) => new[]
        {
            (idx + 1).ToString(),
            p.ProductoCodigo,
            p.ProductoNombre,
            $"{p.CantidadEntregada:0.##} {p.UnidadAlmacen}",
            $"${p.CostoUnitarioAlmacen:N2}",
            $"${p.ImporteCosto:N2}"
        }).ToList();

        var metadatos = new List<(string etiq, string val)>
        {
            ("Departamento:", sal.DepartamentoNombre),
            ("Recibe:", sal.EncargadoNombre),
            ("Proyecto:", sal.ProyectoNombre)
        };

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: "VALE DE ENTREGA DE MATERIAL",
            subtitulo: "Lebaref | Departamento de Compras",
            folioCodigoBarras: sal.Folio,
            fecha: sal.Fecha,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Descripción", "Cantidad", "Costo Unit.", "Importe" },
            filasTabla: filas,
            totales: new List<(string, string)> { ("Total:", $"${sal.CostoTotalEstimado:N2}") },
            notas: sal.Notas,
            firmaIzquierda: "Entregó Almacén",
            firmaDerecha: $"Recibió: {sal.EncargadoNombre}");

        AbrirVentanaVistaPreviaCarta(owner, $"Vale de Salida {sal.Folio}", hoja);
    }

    public static void MostrarVistaPreviaEImprimirPrestamo(Window owner, PrestamoHerramienta pre)
    {
        var filas = pre.Partidas.Select((p, idx) => new[]
        {
            (idx + 1).ToString(),
            p.CodigoIndividual,
            p.ProductoNombre,
            p.MarcaModelo,
            string.IsNullOrWhiteSpace(p.NumeroSerie) ? "S/N" : p.NumeroSerie,
            p.EstadoSalida,
            p.EstadoDevolucion
        }).ToList();

        var metadatos = new List<(string etiq, string val)>
        {
            ("Departamento:", pre.DepartamentoNombre),
            ("Responsable:", pre.EncargadoNombre),
            ("Proyecto:", pre.ProyectoNombre),
            ("Estatus:", pre.Estatus)
        };

        var hoja = ConstruirHojaCartaDocumento(
            tituloDocumento: "VALE DE PRÉSTAMO DE HERRAMIENTA",
            subtitulo: "Lebaref | Departamento de Compras",
            folioCodigoBarras: pre.Folio,
            fecha: pre.FechaSalida,
            metadatos: metadatos,
            encabezadosTabla: new[] { "#", "Código", "Herramienta", "Marca / Modelo", "Núm. Serie", "Estado Salida", "Estado Retorno" },
            filasTabla: filas,
            totales: new List<(string, string)> { ("Total Herramientas:", pre.Partidas.Count.ToString()) },
            notas: pre.Notas,
            firmaIzquierda: $"Recibió: {pre.EncargadoNombre}",
            firmaDerecha: "Devolución en Almacén");

        AbrirVentanaVistaPreviaCarta(owner, $"Vale de Préstamo {pre.Folio}", hoja);
    }

    public static void MostrarVistaPreviaCatalogoCodigosCarta(Window owner, List<Producto> productos, List<HerramientaActivo> herramientas)
    {
        var pagePanel = new StackPanel
        {
            Width = 760,
            Background = Brushes.White,
            Margin = new Thickness(24)
        };

        var header = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            BorderThickness = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(0, 0, 0, 10),
            Margin = new Thickness(0, 0, 0, 16)
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
            Text = "Lebaref | Departamento de Compras",
            FontSize = 11.5,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 3, 0, 0)
        });
        header.Child = hStack;
        pagePanel.Children.Add(header);

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

        foreach (var p in productos)
        {
            wrap.Children.Add(CrearTarjetaCodigoImprimible(
                p.CodigoEfectivo,
                p.Nombre,
                $"Stock: {p.StockActual:0.##} {p.UnidadAlmacen} | Ubic: {p.Ubicacion}",
                "#0078D4"));
        }

        foreach (var h in herramientas)
        {
            wrap.Children.Add(CrearTarjetaCodigoImprimible(
                h.CodigoIndividual,
                $"[HERRAMIENTA] {h.ProductoNombre}",
                $"{h.MarcaModelo} | Serie: {h.NumeroSerie}",
                "#5C2D91"));
        }

        pagePanel.Children.Add(wrap);

        AbrirVentanaVistaPreviaCarta(owner, "Catálogo de Códigos de Barras en Hoja Carta", pagePanel);
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
        string firmaDerecha)
    {
        var page = new StackPanel
        {
            Width = 760,
            Background = Brushes.White,
            Margin = new Thickness(28)
        };

        // Encabezado con Folio y Código de Barras escaneable
        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = tituloDocumento,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = subtitulo,
            FontSize = 11.5,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 4)
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"Fecha de Emisión: {fecha}",
            FontSize = 11,
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
            Padding = new Thickness(10, 6, 10, 6),
            Background = Brushes.White
        };
        var bcStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        bcStack.Children.Add(new Image
        {
            Source = CrearImagenCodigoBarras(folioCodigoBarras),
            Width = 210,
            Height = 48,
            Stretch = Stretch.Fill
        });
        bcStack.Children.Add(new TextBlock
        {
            Text = folioCodigoBarras,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });
        barcodeBorder.Child = bcStack;
        Grid.SetColumn(barcodeBorder, 1);
        topGrid.Children.Add(barcodeBorder);

        page.Children.Add(topGrid);

        // Línea divisoria azul Windows
        page.Children.Add(new Border
        {
            Height = 2,
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Cuadro de Metadatos
        var metaBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 14)
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
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Margin = new Thickness(0, 2, 8, 2)
            };
            var val = new TextBlock
            {
                Text = metadatos[i].val,
                FontSize = 11.5,
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
        page.Children.Add(metaBorder);

        // Tabla de Partidas
        var tableGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (int c = 0; c < encabezadosTabla.Length; c++)
        {
            tableGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = c == 2 ? new GridLength(2.4, GridUnitType.Star) : new GridLength(1, GridUnitType.Star)
            });
        }

        tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < encabezadosTabla.Length; c++)
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
                Text = encabezadosTabla[c],
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5
            };
            Grid.SetRow(hCell, 0);
            Grid.SetColumn(hCell, c);
            tableGrid.Children.Add(hCell);
        }

        for (int r = 0; r < filasTabla.Count; r++)
        {
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var rowValues = filasTabla[r];
            for (int c = 0; c < rowValues.Length && c < encabezadosTabla.Length; c++)
            {
                var cell = new Border
                {
                    Background = r % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6, 5, 6, 5)
                };
                cell.Child = new TextBlock
                {
                    Text = rowValues[c],
                    Foreground = Brushes.Black,
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c);
                tableGrid.Children.Add(cell);
            }
        }
        page.Children.Add(tableGrid);

        // Totales y Notas
        var bottomGrid = new Grid { Margin = new Thickness(0, 0, 0, 28) };
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
            FontSize = 10.5,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(notesBox, 0);
        bottomGrid.Children.Add(notesBox);

        var totStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (etiq, val) in totales)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = etiq + " ", FontWeight = FontWeights.SemiBold, FontSize = 11.5, Foreground = Brushes.DarkSlateGray });
            row.Children.Add(new TextBlock { Text = val, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.Black, Width = 110, TextAlignment = TextAlignment.Right });
            totStack.Children.Add(row);
        }
        Grid.SetColumn(totStack, 1);
        bottomGrid.Children.Add(totStack);
        page.Children.Add(bottomGrid);

        // Líneas de firma
        var sigGrid = new Grid { Margin = new Thickness(0, 24, 0, 8) };
        sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sig1 = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0) };
        sig1.Child = new TextBlock { Text = firmaIzquierda, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
        Grid.SetColumn(sig1, 0);
        sigGrid.Children.Add(sig1);

        var sig2 = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0) };
        sig2.Child = new TextBlock { Text = firmaDerecha, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
        Grid.SetColumn(sig2, 2);
        sigGrid.Children.Add(sig2);

        page.Children.Add(sigGrid);

        page.Children.Add(new TextBlock
        {
            Text = $"Lebaref | Departamento de Compras — Folio: {folioCodigoBarras}",
            FontSize = 9.5,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0)
        });

        return page;
    }

    private static void AbrirVentanaVistaPreviaCarta(Window owner, string tituloVentana, FrameworkElement contenidoHojaCarta)
    {
        var win = new Window
        {
            Title = $"Lebaref | {tituloVentana}",
            Width = 880,
            Height = 760,
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
        var lblInfo = new TextBlock
        {
            Text = tituloVentana,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(lblInfo, Dock.Left);
        topStack.Children.Add(lblInfo);

        var btnPrint = new Button
        {
            Content = "Imprimir",
            Padding = new Thickness(18, 7, 18, 7),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnPrint.Click += (_, _) =>
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                dlg.PrintVisual(contenidoHojaCarta, tituloVentana);
            }
        };
        DockPanel.SetDock(btnPrint, Dock.Right);
        topStack.Children.Add(btnPrint);

        topBar.Child = topStack;
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(20)
        };

        var paperBorder = new Border
        {
            Background = Brushes.White,
            Width = 816,
            MinHeight = 1020,
            HorizontalAlignment = HorizontalAlignment.Center,
            BorderBrush = Brushes.Silver,
            BorderThickness = new Thickness(1),
            Child = contenidoHojaCarta
        };

        scroll.Content = paperBorder;
        root.Children.Add(scroll);

        win.Content = root;
        win.ShowDialog();
    }
}
