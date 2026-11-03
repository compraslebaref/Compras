using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SistemaComprasAlmacen.Services;

/// <summary>
/// Módulos principales de navegación de la aplicación.
/// </summary>
public enum ModuloSistema
{
    Dashboard = 0,
    Catalogo = 1,
    Solicitudes = 2,
    Comparativa = 3,
    OrdenesCompra = 4,
    AlmacenPos = 5,
    FacturasXml = 6,
    Analisis = 7,
    Directorios = 8
}

// ==================== MENSAJES Y EVENTOS DEL PATRÓN MEDIATOR ====================

/// <summary>
/// Solicita navegar hacia un módulo específico con un filtro o folio opcional.
/// </summary>
public sealed record NavegarModuloMessage(
    ModuloSistema Modulo,
    string? FiltroBusqueda = null,
    string? SubVista = null);

/// <summary>
/// Solicita abrir el selector/mapa gráfico 3×3 de anaqueles en una coordenada específica.
/// </summary>
public sealed record AbrirMapaAnaquelesMessage(
    string? CoordenadaInicial = null,
    Action<string>? AlSeleccionarCoordenada = null);

/// <summary>
/// Solicita procesar cualquier código de barras o folio desde cualquier componente de la UI.
/// </summary>
public sealed record ProcesarScannerMessage(string CodigoRaw);

/// <summary>
/// Notifica que los datos de inventario, órdenes o solicitudes cambiaron y que los contadores/vistas suscritas deben actualizarse.
/// </summary>
public sealed record DatosActualizadosMessage(
    string OrigenCambio,
    bool RefrescarCatalogo = true,
    bool RefrescarDashboard = true,
    bool ConservarScrollCatalogo = true);

/// <summary>
/// Notifica un cambio en el estado del carrito temporal de compras/salidas.
/// </summary>
public sealed record CarritoActualizadoMessage(int TotalSkus, decimal TotalUnidades);

/// <summary>
/// Solicita alternar entre el Tema Claro de Alto Contraste y el Tema Oscuro Slate.
/// </summary>
public sealed record AlternarTemaVisualMessage();

/// <summary>
/// Implementación del Patrón de Diseño Mediator (GoF) para desacoplar la comunicación
/// entre módulos de UI, escáner omnisciente, tarjetas KPI, alertas y servicios de datos.
/// </summary>
public sealed class AppMediator
{
    public static AppMediator Instance { get; } = new();

    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscriptores = new();
    private readonly object _syncLock = new();

    private AppMediator()
    {
    }

    /// <summary>
    /// Registra un suscriptor tipado para recibir notificaciones o comandos de tipo <typeparamref name="TMessage"/>.
    /// </summary>
    public IDisposable Suscribir<TMessage>(Action<TMessage> handler) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        var msgType = typeof(TMessage);

        lock (_syncLock)
        {
            if (!_subscriptores.TryGetValue(msgType, out var lista))
            {
                lista = new List<Delegate>();
                _subscriptores[msgType] = lista;
            }

            if (!lista.Contains(handler))
            {
                lista.Add(handler);
            }
        }

        return new SuscripcionToken(() => Desuscribir(handler));
    }

    /// <summary>
    /// Elimina un manejador previamente suscrito.
    /// </summary>
    public void Desuscribir<TMessage>(Action<TMessage> handler) where TMessage : class
    {
        var msgType = typeof(TMessage);
        lock (_syncLock)
        {
            if (_subscriptores.TryGetValue(msgType, out var lista))
            {
                lista.Remove(handler);
            }
        }
    }

    /// <summary>
    /// Publica un comando o evento hacia todos los componentes suscritos de forma desacoplada.
    /// </summary>
    public void Publicar<TMessage>(TMessage mensaje) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        var msgType = typeof(TMessage);

        List<Delegate>? copiaHandlers = null;
        lock (_syncLock)
        {
            if (_subscriptores.TryGetValue(msgType, out var lista) && lista.Count > 0)
            {
                copiaHandlers = lista.ToList();
            }
        }

        if (copiaHandlers == null)
            return;

        foreach (var delegado in copiaHandlers)
        {
            if (delegado is Action<TMessage> accion)
            {
                accion(mensaje);
            }
        }
    }

    private sealed class SuscripcionToken : IDisposable
    {
        private Action? _onDispose;

        public SuscripcionToken(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            _onDispose?.Invoke();
            _onDispose = null;
        }
    }
}
