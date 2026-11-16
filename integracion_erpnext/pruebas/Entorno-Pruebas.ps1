param(
    [ValidateSet('Iniciar', 'Detener', 'Estado', 'Ejemplo')]
    [string]$Accion = 'Iniciar',
    [string]$DistribucionWSL
)

$ErrorActionPreference = 'Stop'
if ($DistribucionWSL) {
    $motor = 'wsl.exe'
    $opcionesMotor = @('--distribution', $DistribucionWSL, '--cd', $PSScriptRoot, '--exec', 'docker')
} else {
    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue
    if (-not $docker) {
        throw 'Instala Docker con contenedores Linux y vuelve a ejecutar este archivo.'
    }
    $motor = $docker.Source
    $opcionesMotor = @()
}

function Invoke-Docker {
    & $motor @opcionesMotor @args
    if ($LASTEXITCODE -ne 0) {
        throw "Docker devolvió el código $LASTEXITCODE."
    }
}

$engine = Invoke-Docker info --format '{{.OSType}}'
if ($engine -ne 'linux') {
    throw 'Inicia el motor de Docker en modo de contenedores Linux.'
}
$variables = Join-Path $PSScriptRoot '.env'
if (-not (Test-Path -LiteralPath $variables)) {
    if ($Accion -ne 'Iniciar') {
        throw 'Ejecuta Iniciar para crear el entorno de pruebas.'
    }
    $credenciales = @(
        "LEBAREF_TEST_DB_PASSWORD=$([guid]::NewGuid().ToString('N'))"
        "LEBAREF_TEST_ADMIN_PASSWORD=$([guid]::NewGuid().ToString('N'))"
    )
    Set-Content -LiteralPath $variables -Value $credenciales -Encoding utf8
}

function Invoke-Compose {
    Push-Location -LiteralPath $PSScriptRoot
    try {
        Invoke-Docker compose --env-file .env -f compose.yaml @args
    } finally {
        Pop-Location
    }
}

$url = 'http://127.0.0.1:8186'
switch ($Accion) {
    'Iniciar' {
        Invoke-Compose build backend
        Invoke-Compose up -d --no-build
        $plazo = [DateTime]::UtcNow.AddMinutes(2)
        do {
            try {
                $respuesta = Invoke-RestMethod "$url/api/method/ping" -TimeoutSec 5 -ErrorAction Stop
            } catch {
                $respuesta = $null
            }
            if ($respuesta.message -eq 'pong') {
                Write-Host "ERPNext de pruebas disponible en $url"
                Write-Host "Usuario Administrator. Contraseña en $variables (LEBAREF_TEST_ADMIN_PASSWORD)."
                return
            }
            Start-Sleep -Seconds 2
        } while ([DateTime]::UtcNow -lt $plazo)
        throw 'El sitio todavía no responde. Ejecuta Estado para ver los contenedores.'
    }
    'Estado' {
        Invoke-Compose ps --all
    }
    'Detener' {
        Invoke-Compose stop
    }
    'Ejemplo' {
        Invoke-Compose exec -T backend bench --site lebaref-pruebas.local execute lebaref_compras.demo.crear_solicitud
    }
}
