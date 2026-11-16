param([string]$Destino = (Join-Path $PSScriptRoot 'bin\Lebaref-Escritorio.exe'))

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw 'Windows necesita .NET Framework 4.8 para compilar Lebaref Escritorio.'
}
$Destino = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destino)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destino) | Out-Null

& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu "/out:$Destino" `
    "/reference:$framework\System.Net.Http.dll" "/reference:$framework\System.Web.Extensions.dll" `
    (Join-Path $PSScriptRoot 'ErpClient.cs') (Join-Path $PSScriptRoot 'MainForm.cs')
if ($LASTEXITCODE -ne 0) { throw "El compilador devolvió el código $LASTEXITCODE." }
Write-Host "Ejecutable creado en $Destino"
