# Copia los archivos adjuntos del ERP a la carpeta de expediente de cada Solicitud dentro de Google Drive para escritorio.
# No usa tokens ni sesiones: Google Drive para escritorio (iniciado una vez con la cuenta de Compras) sube lo que aparezca.
# Estructura:  <Mi unidad>\Solicitudes\<Departamento>\<SOL-AAAA-####>\<Tipo> - <Proveedor> - <Documento> - <archivo>
# Idempotente: si el archivo ya existe en la carpeta no se vuelve a copiar. Se ejecuta cada 5 min con una tarea de Windows.
$ErrorActionPreference = 'Stop'
$base = 'C:\Users\oscar\Proyectos\Lebaref'
$pruebasDir = "$base\app\integracion_erpnext\pruebas"
$pyTool = "$base\app\integracion_erpnext\herramientas\listar_adjuntos_expediente.py"
$log = "$base\expedientes_drive.log"
function Log($m) { Add-Content -LiteralPath $log -Value ("{0}  {1}" -f (Get-Date -Format 's'), $m) -Encoding utf8 }

try {
    # 1. Localizar "Mi unidad" de Google Drive para escritorio
    $raiz = $null
    foreach ($d in (Get-PSDrive -PSProvider FileSystem)) {
        foreach ($n in 'Mi unidad', 'My Drive') {
            $p = Join-Path $d.Root $n
            if (Test-Path -LiteralPath $p) { $raiz = $p; break }
        }
        if ($raiz) { break }
    }
    if (-not $raiz) {
        foreach ($p in "$env:USERPROFILE\Google Drive\Mi unidad", "$env:USERPROFILE\Google Drive\My Drive", "$env:USERPROFILE\Google Drive") {
            if (Test-Path -LiteralPath $p) { $raiz = $p; break }
        }
    }
    if (-not $raiz) { Log 'Google Drive para escritorio no esta instalado o no ha iniciado sesion; no se copio nada.'; exit 0 }
    $solicitudes = Join-Path $raiz 'Solicitudes'
    if (-not (Test-Path -LiteralPath $solicitudes)) { New-Item -ItemType Directory -Path $solicitudes | Out-Null }

    # 2. Adjuntos del ERP ligados a solicitudes
    function Compose { & wsl.exe -d Ubuntu --cd $pruebasDir --exec docker compose --env-file .env -f compose.yaml @args }
    $backendId = ([string](Compose ps -q backend)).Trim()
    if (-not $backendId) { Log 'ERP apagado (sin contenedor backend); se reintenta en el siguiente ciclo.'; exit 0 }
    $wslPy = ([string](& wsl.exe -d Ubuntu --exec wslpath -a $pyTool)).Trim()
    & wsl.exe -d Ubuntu --exec docker cp $wslPy "${backendId}:/tmp/listar_adjuntos_expediente.py" | Out-Null
    $out = (Compose exec -T backend /home/frappe/frappe-bench/env/bin/python /tmp/listar_adjuntos_expediente.py) -join "`n"
    Compose exec -T -u root backend rm -f /tmp/listar_adjuntos_expediente.py | Out-Null
    $linea = ($out -split "`n" | Where-Object { $_ -like 'EXPEDIENTE_JSON:*' } | Select-Object -First 1)
    if (-not $linea) { Log "No se pudo listar adjuntos: $out"; exit 1 }
    $adjuntos = $linea.Substring('EXPEDIENTE_JSON:'.Length) | ConvertFrom-Json

    # 3. Copiar los que falten
    $copiados = 0
    foreach ($a in $adjuntos) {
        $carpeta = Get-ChildItem -LiteralPath $solicitudes -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName $a.solicitud } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $carpeta) {
            $depto = if ($a.departamento) { $a.departamento } else { 'Sin clasificar' }
            $depto = ($depto -replace '[\\/:*?"<>|]', '_')
            $carpeta = Join-Path (Join-Path $solicitudes $depto) $a.solicitud
            New-Item -ItemType Directory -Force -Path $carpeta | Out-Null
        }
        $partes = @($a.tipo, $a.proveedor, $a.documento) | Where-Object { $_ }
        $nombre = (($partes -join ' - ') + ' - ' + $a.archivo) -replace '[\\/:*?"<>|]', '_'
        $destino = Join-Path $carpeta $nombre
        if (Test-Path -LiteralPath $destino) { continue }
        $wslDest = ([string](& wsl.exe -d Ubuntu --exec wslpath -a $destino)).Trim()
        & wsl.exe -d Ubuntu --exec docker cp "${backendId}:$($a.ruta)" $wslDest | Out-Null
        if (-not (Test-Path -LiteralPath $destino)) { Log "FALLO copiar $($a.ruta) -> $destino"; continue }
        $copiados++
        Log "COPIADO $($a.solicitud) <- $nombre"
    }
    if ($copiados -eq 0) { Log "Sin archivos nuevos ($(@($adjuntos).Count) adjuntos revisados)." }
} catch {
    Log "ERROR: $($_.Exception.Message)"
    exit 1
}
