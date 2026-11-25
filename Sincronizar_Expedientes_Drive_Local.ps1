# Copia los archivos adjuntos del ERP a la carpeta de expediente de cada Solicitud dentro de Google Drive para escritorio.
# No usa tokens ni sesiones: Google Drive para escritorio (iniciado una vez con la cuenta de Compras) sube lo que aparezca.
# Estructura:  <Mi unidad>\Solicitudes\<Departamento>\<SOL-AAAA-####>\<Tipo> - <Proveedor> - <Documento> - <archivo>
# Idempotente: si el archivo ya existe en la carpeta no se vuelve a copiar. Se ejecuta cada 5 min con una tarea de Windows.
$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$pyTool = Join-Path $PSScriptRoot 'integracion_erpnext\herramientas\listar_adjuntos_expediente.py'
$log = Join-Path (Split-Path -Parent $PSScriptRoot) 'expedientes_drive.log'
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
    $backends = @(& wsl.exe -d Ubuntu --exec docker ps -q --filter 'label=com.docker.compose.project=lebaref-pruebas' --filter 'label=com.docker.compose.service=backend')
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar Docker en Ubuntu.' }
    if ($backends.Count -eq 0) { Log 'ERP apagado (sin contenedor backend); se reintenta en el siguiente ciclo.'; exit 0 }
    if ($backends.Count -ne 1 -or $backends[0] -notmatch '^[0-9a-f]{12,64}$') { throw 'No se pudo identificar un unico backend del ERP.' }
    $backendId = $backends[0]
    $source = Get-Content -LiteralPath $pyTool -Encoding utf8 -Raw
    $out = ($source | & wsl.exe -d Ubuntu --exec docker exec -i -e PYTHONIOENCODING=utf-8 $backendId /home/frappe/frappe-bench/env/bin/python -) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'El backend no pudo listar los adjuntos.' }
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
        $rutaB64 = [Convert]::ToBase64String($utf8.GetBytes([string]$a.ruta))
        $pyRead = 'import base64,sys; p=base64.b64decode(sys.argv[1]).decode("utf-8"); sys.stdout.write(base64.b64encode(open(p,"rb").read()).decode("ascii"))'
        $b64Out = ($pyRead | & wsl.exe -d Ubuntu --exec docker exec -i $backendId /home/frappe/frappe-bench/env/bin/python - $rutaB64) -join ''
        if ($LASTEXITCODE -ne 0 -or -not $b64Out) { Log "FALLO extraer adjunto del contenedor: $($a.ruta)"; continue }
        [System.IO.File]::WriteAllBytes($destino, [Convert]::FromBase64String($b64Out.Trim()))
        if (-not (Test-Path -LiteralPath $destino)) { Log "FALLO copiar $($a.ruta) -> $destino"; continue }
        $copiados++
        Log "COPIADO $($a.solicitud) <- $nombre"
    }
    if ($copiados -eq 0) { Log "Sin archivos nuevos ($(@($adjuntos).Count) adjuntos revisados)." }
} catch {
    Log "ERROR: $($_.Exception.Message)"
    exit 1
}
