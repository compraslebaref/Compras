# Sincroniza a Google Drive los archivos adjuntos en ERPNext de cada Solicitud (expediente).
# Idempotente: guarda lo ya subido en expedientes_drive_estado.json (fuera de Git). Requiere el CLI gdrive/gsheets del agente.
param([string]$HojaId = '12tPuqb-CCSp7Ruhvj46kDtQcRO_Lay_3vKmF1kaxT6c')
$ErrorActionPreference = 'Stop'
$pruebasDir = 'C:\Users\oscar\Proyectos\Lebaref\app\integracion_erpnext\pruebas'
$pyTool = 'C:\Users\oscar\Proyectos\Lebaref\app\integracion_erpnext\herramientas\listar_adjuntos_expediente.py'
$estadoPath = 'C:\Users\oscar\Proyectos\Lebaref\expedientes_drive_estado.json'
$tmp = Join-Path $env:TEMP 'lebaref_expediente'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

function Compose { & wsl.exe -d Ubuntu --cd $pruebasDir --exec docker compose --env-file .env -f compose.yaml @args }

# 1. Adjuntos del ERP
$backendId = ([string](Compose ps -q backend)).Trim()
$wslPy = ([string](& wsl.exe -d Ubuntu --exec wslpath -a $pyTool)).Trim()
& wsl.exe -d Ubuntu --exec docker cp $wslPy "${backendId}:/tmp/listar_adjuntos_expediente.py"
$out = (Compose exec -T backend /home/frappe/frappe-bench/env/bin/python /tmp/listar_adjuntos_expediente.py) -join "`n"
Compose exec -T -u root backend rm -f /tmp/listar_adjuntos_expediente.py | Out-Null
$linea = ($out -split "`n" | Where-Object { $_ -like 'EXPEDIENTE_JSON:*' } | Select-Object -First 1)
if (-not $linea) { throw "No se pudo listar adjuntos: $out" }
$adjuntos = $linea.Substring('EXPEDIENTE_JSON:'.Length) | ConvertFrom-Json
Write-Host "Adjuntos en ERP ligados a solicitudes: $(@($adjuntos).Count)" -ForegroundColor Cyan

# 2. Carpeta de Drive por solicitud (hoja General, pestaña Solicitudes)
$sol = gsheets readonly read $HojaId 'Solicitudes!A2:I300' --json | ConvertFrom-Json
$carpeta = @{}
foreach ($fila in $sol) {
    $vals = if ($fila.PSObject.Properties.Name -contains 'values') { $fila.values } else { $fila }
    if ($vals.Count -ge 9 -and $vals[8] -match '/folders/([\w-]+)') { $carpeta[[string]$vals[0]] = $Matches[1] }
}
$cot = gsheets readonly read $HojaId 'Cotizaciones!A2:I500' --json | ConvertFrom-Json
$filaCot = @{}
$n = 2
foreach ($fila in $cot) { $vals = if ($fila.PSObject.Properties.Name -contains 'values') { $fila.values } else { $fila }; if ($vals.Count -ge 2) { $filaCot[[string]$vals[1]] = $n }; $n++ }

# 3. Subir lo pendiente
$estado = @{}
if (Test-Path $estadoPath) { (Get-Content $estadoPath -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $estado[$_.Name] = $_.Value } }
$subidos = 0; $sinCarpeta = @()
foreach ($a in $adjuntos) {
    if ($estado.ContainsKey($a.id)) { continue }
    if (-not $carpeta.ContainsKey($a.solicitud)) { $sinCarpeta += "$($a.solicitud): $($a.archivo)"; continue }
    $partes = @($a.tipo, $a.proveedor, $a.documento) | Where-Object { $_ }
    $nombre = ($partes -join ' - ') + ' - ' + $a.archivo
    $nombre = ($nombre -replace '[\\/:*?"<>|]', '_')
    $local = Join-Path $tmp $nombre
    $wslDest = ([string](& wsl.exe -d Ubuntu --exec wslpath -a $local)).Trim()
    & wsl.exe -d Ubuntu --exec docker cp "${backendId}:$($a.ruta)" $wslDest
    if (-not (Test-Path -LiteralPath $local)) { throw "No se copió $($a.ruta)" }
    function Buscar-EnCarpeta($carp, $nom) {
        $lista = gdrive readonly ls $carp --json | ConvertFrom-Json
        foreach ($x in $lista) { if ($x.name -eq $nom) { return $x.id } }
        return $null
    }
    $id = Buscar-EnCarpeta $carpeta[$a.solicitud] $nombre
    if (-not $id) {
        gdrive mutate upload $local --parent $carpeta[$a.solicitud] | Out-Null
        $id = Buscar-EnCarpeta $carpeta[$a.solicitud] $nombre
    }
    if (-not $id) { throw "Drive no confirmó la carga de $nombre" }
    $link = "https://drive.google.com/file/d/$id/view"
    $estado[$a.id] = @{ drive = $id; solicitud = $a.solicitud; nombre = $nombre; fecha = (Get-Date -Format s) }
    if ($a.tipo -eq 'Cotización' -and $filaCot.ContainsKey($a.documento)) {
        gsheets mutate write $HojaId "Cotizaciones!E$($filaCot[$a.documento])" $link | Out-Null
    }
    Remove-Item -LiteralPath $local -Force
    $subidos++
    Write-Host "OK  $($a.solicitud)  <-  $nombre" -ForegroundColor Green
}
$estado | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $estadoPath -Encoding utf8
Write-Host "Subidos: $subidos | Sin carpeta de expediente: $($sinCarpeta.Count)" -ForegroundColor Cyan
$sinCarpeta | ForEach-Object { Write-Host "  SIN CARPETA $_" -ForegroundColor Yellow }
