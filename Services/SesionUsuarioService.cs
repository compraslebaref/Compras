using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;

namespace SistemaComprasAlmacen.Services;

public sealed class PermisosRolLebaref
{
    public string TipoUsuario { get; init; } = "Auxiliar de Compras";
    public string VistaOperativaInicial { get; init; } = "Compras";
    public string ResumenCapacidades { get; init; } = string.Empty;
    public string ResumenLimitaciones { get; init; } = string.Empty;

    public bool EsAdministrador { get; init; }
    public bool PuedeVerPrecios { get; init; }
    public bool PuedeCrearSolicitudes { get; init; }
    public bool PuedeCotizarComparativa { get; init; }
    public bool PuedeGenerarBorradorOc { get; init; }
    public bool PuedeAutorizarOc { get; init; }
    public bool PuedeProgramarPagosOc { get; init; }
    public bool PuedeRecibirAlmacen { get; init; }
    public bool PuedeDespacharAlmacen { get; init; }
    public bool PuedeGestionarFacturas { get; init; }
    public bool PuedeVerAnalisis { get; init; }
    public bool PuedeVerDirectorios { get; init; }
    public bool PuedeEditarCatalogosMaestros { get; init; }
    public bool PuedeEliminarRegistrosMaestros { get; init; }
    public bool PuedeVerFlotilla { get; init; }
    public bool PuedeAdministrarUsuarios { get; init; }
}

/// <summary>
/// Administra la sesión autenticada del usuario actual (contra ERPNext multiusuario o base local)
/// y calcula sus permisos y limitaciones exactas en toda la interfaz WPF.
/// </summary>
public static class SesionUsuarioService
{
    public static readonly string[] TiposUsuarioOficiales =
    {
        "Responsable de Compras",
        "Auxiliar de Compras",
        "Auxiliar de Almacén",
        "Dirección",
        "Finanzas",
        "Solicitante"
    };

    public static bool SesionIniciada { get; private set; }
    public static UsuarioSistema UsuarioActual { get; private set; } = new()
    {
        Id = 1,
        Username = "oscar",
        NombreCompleto = "Oscar (Jefe de Compras)",
        Rol = "Responsable de Compras",
        Activo = true
    };

    public static PermisosRolLebaref Permisos { get; private set; } = ObtenerPermisosPorTipo("Responsable de Compras");
    public static bool ConectadoAErpNext { get; private set; }
    public static string IdentidadErpNext { get; private set; } = string.Empty;
    public static List<string> RolesErpNext { get; private set; } = new();
    public static ErpNextApiClient? ClienteErp { get; private set; }

    public static string EtiquetaModoConexion => ConectadoAErpNext
        ? $"ERPNext ({IdentidadErpNext})"
        : "Modo Local";

    public static PermisosRolLebaref ObtenerPermisosPorTipo(string? tipoUsuarioRaw)
    {
        string tipo = UsuarioSistema.NormalizarTipoUsuario(tipoUsuarioRaw);
        return tipo switch
        {
            "Responsable de Compras" => new PermisosRolLebaref
            {
                TipoUsuario = "Responsable de Compras",
                VistaOperativaInicial = "Compras",
                ResumenCapacidades = "Acceso total: Solicitudes, Comparativas, Emisión y Autorización de OC, Recepción/Almacén, Facturas XML, Análisis, Catálogos, Google Sheets y Administración de Usuarios.",
                ResumenLimitaciones = "Sin restricciones operativas (Perfil Administrador integrado).",
                EsAdministrador = true,
                PuedeVerPrecios = true,
                PuedeCrearSolicitudes = true,
                PuedeCotizarComparativa = true,
                PuedeGenerarBorradorOc = true,
                PuedeAutorizarOc = true,
                PuedeProgramarPagosOc = true,
                PuedeRecibirAlmacen = true,
                PuedeDespacharAlmacen = true,
                PuedeGestionarFacturas = true,
                PuedeVerAnalisis = true,
                PuedeVerDirectorios = true,
                PuedeEditarCatalogosMaestros = true,
                PuedeEliminarRegistrosMaestros = true,
                PuedeVerFlotilla = true,
                PuedeAdministrarUsuarios = true
            },
            "Auxiliar de Compras" => new PermisosRolLebaref
            {
                TipoUsuario = "Auxiliar de Compras",
                VistaOperativaInicial = "Compras",
                ResumenCapacidades = "Crea y atiende Solicitudes, cotiza proveedores en Comparativa (con hoja ¿A quién cotizar?), genera borradores de OC, sube Facturas CFDI y da de alta insumos/proveedores.",
                ResumenLimitaciones = "No puede autorizar/forzar cierre de OCs, no registra entradas físicas de almacén, no elimina maestros y no administra cuentas de usuarios.",
                EsAdministrador = false,
                PuedeVerPrecios = true,
                PuedeCrearSolicitudes = true,
                PuedeCotizarComparativa = true,
                PuedeGenerarBorradorOc = true,
                PuedeAutorizarOc = false,
                PuedeProgramarPagosOc = false,
                PuedeRecibirAlmacen = false,
                PuedeDespacharAlmacen = false,
                PuedeGestionarFacturas = true,
                PuedeVerAnalisis = true,
                PuedeVerDirectorios = true,
                PuedeEditarCatalogosMaestros = true,
                PuedeEliminarRegistrosMaestros = false,
                PuedeVerFlotilla = true,
                PuedeAdministrarUsuarios = false
            },
            "Auxiliar de Almacén" => new PermisosRolLebaref
            {
                TipoUsuario = "Auxiliar de Almacén",
                VistaOperativaInicial = "Almacén",
                ResumenCapacidades = "Recibe mercancía contra OC (ENT), despacha Salidas (SAL), reingresa Sobrantes (SOB), controla Kits/Préstamos (PRE), ubicaciones A–F y check-list vehicular.",
                ResumenLimitaciones = "Privacidad total de precios e importes. Sin acceso a Comparativa, Facturas ni Análisis financiero; no emite ni autoriza OCs ni elimina catálogos.",
                EsAdministrador = false,
                PuedeVerPrecios = false,
                PuedeCrearSolicitudes = true,
                PuedeCotizarComparativa = false,
                PuedeGenerarBorradorOc = false,
                PuedeAutorizarOc = false,
                PuedeProgramarPagosOc = false,
                PuedeRecibirAlmacen = true,
                PuedeDespacharAlmacen = true,
                PuedeGestionarFacturas = false,
                PuedeVerAnalisis = false,
                PuedeVerDirectorios = true,
                PuedeEditarCatalogosMaestros = false,
                PuedeEliminarRegistrosMaestros = false,
                PuedeVerFlotilla = true,
                PuedeAdministrarUsuarios = false
            },
            "Dirección" => new PermisosRolLebaref
            {
                TipoUsuario = "Dirección",
                VistaOperativaInicial = "Compras",
                ResumenCapacidades = "Consulta ejecutiva completa de costos, Comparativas, Análisis (Kraljic, PPV, CPK), Expedientes y Autorización (Vo.Bo.) de Órdenes de Compra.",
                ResumenLimitaciones = "No opera movimientos físicos de bodega (recepciones/salidas) ni administra configuraciones técnicas de usuarios.",
                EsAdministrador = false,
                PuedeVerPrecios = true,
                PuedeCrearSolicitudes = true,
                PuedeCotizarComparativa = false,
                PuedeGenerarBorradorOc = false,
                PuedeAutorizarOc = true,
                PuedeProgramarPagosOc = true,
                PuedeRecibirAlmacen = false,
                PuedeDespacharAlmacen = false,
                PuedeGestionarFacturas = true,
                PuedeVerAnalisis = true,
                PuedeVerDirectorios = true,
                PuedeEditarCatalogosMaestros = false,
                PuedeEliminarRegistrosMaestros = false,
                PuedeVerFlotilla = true,
                PuedeAdministrarUsuarios = false
            },
            "Finanzas" => new PermisosRolLebaref
            {
                TipoUsuario = "Finanzas",
                VistaOperativaInicial = "Compras",
                ResumenCapacidades = "Programa y confirma Pagos de Órdenes de Compra, concilia Facturas CFDI 4.0 / Notas, consulta Proveedores (cuentas bancarias) y Análisis de gasto.",
                ResumenLimitaciones = "No opera entradas/salidas físicas de almacén, no adjudica comparativas y no elimina catálogos ni usuarios.",
                EsAdministrador = false,
                PuedeVerPrecios = true,
                PuedeCrearSolicitudes = true,
                PuedeCotizarComparativa = false,
                PuedeGenerarBorradorOc = false,
                PuedeAutorizarOc = false,
                PuedeProgramarPagosOc = true,
                PuedeRecibirAlmacen = false,
                PuedeDespacharAlmacen = false,
                PuedeGestionarFacturas = true,
                PuedeVerAnalisis = true,
                PuedeVerDirectorios = true,
                PuedeEditarCatalogosMaestros = false,
                PuedeEliminarRegistrosMaestros = false,
                PuedeVerFlotilla = true,
                PuedeAdministrarUsuarios = false
            },
            _ => new PermisosRolLebaref
            {
                TipoUsuario = "Solicitante",
                VistaOperativaInicial = "Almacén",
                ResumenCapacidades = "Consulta existencias del Catálogo y crea/da seguimiento a sus Solicitudes de Compra o Cotización (SOL-xxxx).",
                ResumenLimitaciones = "Sin visibilidad de precios ni proveedores. Sin acceso a Comparativa, Órdenes de Compra, Facturas, Movimientos de Bodega ni Directorios.",
                EsAdministrador = false,
                PuedeVerPrecios = false,
                PuedeCrearSolicitudes = true,
                PuedeCotizarComparativa = false,
                PuedeGenerarBorradorOc = false,
                PuedeAutorizarOc = false,
                PuedeProgramarPagosOc = false,
                PuedeRecibirAlmacen = false,
                PuedeDespacharAlmacen = false,
                PuedeGestionarFacturas = false,
                PuedeVerAnalisis = false,
                PuedeVerDirectorios = false,
                PuedeEditarCatalogosMaestros = false,
                PuedeEliminarRegistrosMaestros = false,
                PuedeVerFlotilla = false,
                PuedeAdministrarUsuarios = false
            }
        };
    }

    public static string MapearRolDesdeErpNext(ErpNextUserInfo info)
    {
        if (string.Equals(info.Identidad, "Administrator", StringComparison.OrdinalIgnoreCase))
            return "Responsable de Compras";

        string perfil = info.PerfilRol ?? string.Empty;
        if (perfil.Contains("Responsable", StringComparison.OrdinalIgnoreCase))
            return "Responsable de Compras";
        if (perfil.Contains("Almacén", StringComparison.OrdinalIgnoreCase) || perfil.Contains("Almacen", StringComparison.OrdinalIgnoreCase))
            return "Auxiliar de Almacén";
        if (perfil.Contains("Auxiliar de Compras", StringComparison.OrdinalIgnoreCase))
            return "Auxiliar de Compras";

        bool esSystemManager = info.Roles.Any(r => r.Equals("System Manager", StringComparison.OrdinalIgnoreCase));
        bool esPurchaseManager = info.Roles.Any(r => r.Equals("Purchase Manager", StringComparison.OrdinalIgnoreCase));
        bool esPurchaseUser = info.Roles.Any(r => r.Equals("Purchase User", StringComparison.OrdinalIgnoreCase));
        bool esStockUser = info.Roles.Any(r => r.Equals("Stock User", StringComparison.OrdinalIgnoreCase) || r.Equals("Stock Manager", StringComparison.OrdinalIgnoreCase));
        bool esAccounts = info.Roles.Any(r => r.Equals("Accounts Manager", StringComparison.OrdinalIgnoreCase) || r.Equals("Accounts User", StringComparison.OrdinalIgnoreCase));

        if (esSystemManager || esPurchaseManager)
            return "Responsable de Compras";
        if (esPurchaseUser)
            return "Auxiliar de Compras";
        if (esStockUser)
            return "Auxiliar de Almacén";
        if (esAccounts)
            return "Finanzas";

        return "Solicitante";
    }

    public static async Task<(bool Exito, string MensajeError)> IniciarSesionAsync(string usuarioOCorreo, string password)
    {
        string input = (usuarioOCorreo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
            return (false, "Ingrese su usuario o correo electrónico.");
        if (string.IsNullOrWhiteSpace(password))
            return (false, "Ingrese su contraseña.");

        // Resolver si el usuario local tiene un CorreoErp vinculado para intentar sesión multiusuario en ERPNext
        var candidatosLocales = DatabaseService.ObtenerUsuariosSistema(soloActivos: false);
        string limpio = input.StartsWith("@", StringComparison.Ordinal) ? input[1..] : input;
        var matchLocal = candidatosLocales.FirstOrDefault(u =>
            string.Equals(u.Username, limpio, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(u.CorreoErp) && string.Equals(u.CorreoErp, limpio, StringComparison.OrdinalIgnoreCase)));

        if (matchLocal is { Activo: false })
            return (false, "Esta cuenta está desactivada. Consulte al Responsable de Compras.");

        var (erpEnLinea, _) = await ErpNextApiClient.ProbarConexionServidorAsync(timeoutMs: 1800);
        string? errorErp = null;

        if (erpEnLinea)
        {
            string usuarioParaErp = input.Contains('@') || string.Equals(input, "Administrator", StringComparison.OrdinalIgnoreCase)
                ? input
                : (!string.IsNullOrWhiteSpace(matchLocal?.CorreoErp) ? matchLocal!.CorreoErp : input);

            var cliente = new ErpNextApiClient();
            try
            {
                var infoErp = await cliente.SignInAsync(usuarioParaErp, password);
                string tipoMapeado = MapearRolDesdeErpNext(infoErp);
                var usrSincronizado = DatabaseService.SincronizarUsuarioDesdeErpNext(
                    infoErp.Identidad,
                    infoErp.NombreCompleto,
                    tipoMapeado,
                    infoErp.PerfilRol,
                    password);

                EstablecerSesionActiva(usrSincronizado, cliente, infoErp.Identidad, infoErp.Roles);
                DatabaseService.RegistrarBitacoraUsuario("Sesión", "INICIO_SESION_ERP", $"@{usrSincronizado.Username}",
                    $"Inicio de sesión en servidor ERPNext ({infoErp.Identidad}) con perfil '{Permisos.TipoUsuario}'.");
                return (true, string.Empty);
            }
            catch (ErpNextException ex)
            {
                cliente.Dispose();
                errorErp = ex.Message;

                // Si escribió directamente un correo de ERPNext o Administrator y la clave falló en el servidor activo, reportarlo directo
                if (input.Contains('@') || string.Equals(input, "Administrator", StringComparison.OrdinalIgnoreCase))
                {
                    var intentoCache = DatabaseService.AutenticarUsuarioLocal(input, password);
                    if (!intentoCache.Exito)
                        return (false, $"Servidor ERPNext: {errorErp}");
                }
            }
            catch
            {
                cliente.Dispose();
            }
        }

        // Autenticación contra UsuariosSistema local (cuando ERPNext está apagado o la cuenta es local)
        var (okLocal, usrLocal, msgLocal) = DatabaseService.AutenticarUsuarioLocal(input, password);
        if (!okLocal || usrLocal == null)
        {
            return (false, !string.IsNullOrWhiteSpace(errorErp) && input.Contains('@') ? $"ERPNext: {errorErp}" : msgLocal);
        }

        EstablecerSesionActiva(usrLocal, clienteErp: null, identidadErp: string.Empty, rolesErp: null);
        DatabaseService.RegistrarBitacoraUsuario("Sesión", "INICIO_SESION", $"@{usrLocal.Username}",
            $"Inicio de sesión local con tipo de usuario '{Permisos.TipoUsuario}'.");
        return (true, string.Empty);
    }

    public static void EstablecerSesionActiva(
        UsuarioSistema usuario,
        ErpNextApiClient? clienteErp = null,
        string identidadErp = "",
        List<string>? rolesErp = null)
    {
        if (ClienteErp != null && !ReferenceEquals(ClienteErp, clienteErp))
        {
            try { ClienteErp.Dispose(); } catch { }
        }

        UsuarioActual = usuario;
        Permisos = ObtenerPermisosPorTipo(usuario.TipoUsuarioNormalizado);
        ClienteErp = clienteErp;
        ConectadoAErpNext = clienteErp != null;
        IdentidadErpNext = identidadErp ?? string.Empty;
        RolesErpNext = rolesErp ?? new List<string>();
        SesionIniciada = true;

        DatabaseService.GuardarUsuarioActivo(usuario.Id);
        DatabaseService.GuardarRolOperativoActivo(Permisos.VistaOperativaInicial);
    }

    public static async Task CerrarSesionAsync()
    {
        if (SesionIniciada)
        {
            DatabaseService.RegistrarBitacoraUsuario("Sesión", "CIERRE_SESION", $"@{UsuarioActual.Username}",
                $"Cierre de sesión de '{UsuarioActual.NombreCompleto}' ({Permisos.TipoUsuario}).");
        }

        if (ClienteErp != null)
        {
            try { await ClienteErp.SignOutAsync(); } catch { }
            try { ClienteErp.Dispose(); } catch { }
            ClienteErp = null;
        }

        ConectadoAErpNext = false;
        IdentidadErpNext = string.Empty;
        RolesErpNext.Clear();
        SesionIniciada = false;
    }
}
