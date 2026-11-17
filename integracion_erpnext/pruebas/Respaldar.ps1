param([string]$DistribucionWSL = 'Ubuntu', [Parameter(Mandatory=$true)][string]$Destino)
$ErrorActionPreference = 'Stop'
$Destino = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destino)
if (Test-Path -LiteralPath $Destino) { throw 'Usa una carpeta nueva para no mezclar respaldos.' }
& wsl.exe -d $DistribucionWSL --cd $PSScriptRoot --exec docker compose exec -T backend bench --site lebaref-pruebas.local backup --with-files --compress
if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar el respaldo.' }
New-Item -ItemType Directory -Path $Destino | Out-Null
# docker cp runs in WSL, so translate the Windows destination before copying.
$linuxDestination = & wsl.exe -d $DistribucionWSL --exec wslpath -a $Destino
if ($LASTEXITCODE -ne 0) { throw 'No se pudo resolver la carpeta de respaldo.' }
& wsl.exe -d $DistribucionWSL --cd $PSScriptRoot --exec docker compose cp backend:/home/frappe/frappe-bench/sites/lebaref-pruebas.local/private/backups/. $linuxDestination
if ($LASTEXITCODE -ne 0) { throw 'No se pudo copiar el respaldo.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.env') -Destination (Join-Path $Destino 'entorno.env')
$files = Get-ChildItem -LiteralPath $Destino -File
if (-not ($files.Name -like '*database.sql.gz') -or -not ($files.Name -like '*site_config_backup.json')) { throw 'Faltan componentes del respaldo.' }
$files | Get-FileHash -Algorithm SHA256 | Select-Object Hash, @{Name='Archivo'; Expression={Split-Path -Leaf $_.Path}} | Export-Csv -LiteralPath (Join-Path $Destino 'SHA256.csv') -NoTypeInformation -Encoding utf8
Write-Host "Respaldo creado en $Destino. Contiene credenciales y datos privados: no distribuir junto con la aplicación."
