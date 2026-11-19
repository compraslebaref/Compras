param(
    [string]$DistribucionWSL = 'Ubuntu',
    [switch]$SoloCompilar,
    [switch]$OmitirServidor
)

$ErrorActionPreference = 'Stop'
$raizIntegracion = Split-Path -Parent $PSScriptRoot
$raizRepo = Split-Path -Parent $raizIntegracion

Write-Host "=== 1. Compilando proyecto WPF (.NET 8) ===" -ForegroundColor Cyan
$dotnet = if (Test-Path 'C:\Users\oscar\.dotnet\dotnet.exe') { 'C:\Users\oscar\.dotnet\dotnet.exe' } else { 'dotnet' }
$csproj = Join-Path $raizRepo 'SistemaComprasAlmacen.csproj'
if (Test-Path $csproj) {
    & $dotnet build $csproj --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Falló la compilación de SistemaComprasAlmacen.csproj" }
}

Write-Host "=== 2. Compilando cliente_windows (.NET Framework 4.8) ===" -ForegroundColor Cyan
$compilarWin = Join-Path $raizIntegracion 'cliente_windows\Compilar.ps1'
if (Test-Path $compilarWin) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $compilarWin
    if ($LASTEXITCODE -ne 0) { throw "Falló la compilación de cliente_windows" }
}

if ($SoloCompilar -or $OmitirServidor) {
    Write-Host "Compilaciones verificadas correctamente (validación de servidor omitida por parámetro)." -ForegroundColor Green
    exit 0
}

Write-Host "=== 3. Comprobando servidor local ERPNext (127.0.0.1:8186) ===" -ForegroundColor Cyan
try {
    $ping = Invoke-RestMethod 'http://127.0.0.1:8186/api/method/ping' -TimeoutSec 5
    if ($ping.message -ne 'pong') { throw "Respuesta inesperada: $($ping | ConvertTo-Json -Compress)" }
    Write-Host "Servidor activo: pong" -ForegroundColor Green
} catch {
    throw "El servidor local ERPNext no está respondiendo en http://127.0.0.1:8186. Inícialo primero con .\integracion_erpnext\pruebas\Entorno-Pruebas.ps1 -Accion Iniciar -DistribucionWSL $DistribucionWSL"
}

Write-Host "=== 4. Ejecutando suites transaccionales en lebaref-pruebas.local ===" -ForegroundColor Cyan
$suites = @(
    'lebaref_compras.verificar_integracion.verificar',
    'lebaref_compras.verificar_equipos.verificar',
    'lebaref_compras.verificar_kits.verificar'
)

foreach ($suite in $suites) {
    Write-Host " -> Ejecutando $suite ..." -ForegroundColor Yellow
    & wsl.exe --distribution $DistribucionWSL --cd $PSScriptRoot --exec docker compose --env-file .env -f compose.yaml exec -T backend bench --site lebaref-pruebas.local execute $suite
    if ($LASTEXITCODE -ne 0) {
        throw "Falló la suite $suite"
    }
}

Write-Host "=== VALIDACIÓN COMPLETA EXITOSA ===" -ForegroundColor Green
