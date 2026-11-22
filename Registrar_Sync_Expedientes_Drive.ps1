$ErrorActionPreference = 'Stop'
$taskName = 'Lebaref-Expedientes-Drive'
$wrapper = Join-Path $PSScriptRoot 'Sincronizar_Expedientes_Drive_Silent.vbs'
$syncScript = Join-Path $PSScriptRoot 'Sincronizar_Expedientes_Drive_Local.ps1'
$wscript = Join-Path $env:SystemRoot 'System32\wscript.exe'

foreach ($path in $wrapper, $syncScript, $wscript) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "No se encontro el archivo requerido: $path"
    }
}

$action = New-ScheduledTaskAction -Execute $wscript -Argument ('//B //Nologo "{0}"' -f $wrapper) -WorkingDirectory $PSScriptRoot
$existing = Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction SilentlyContinue
if ($existing) {
    Set-ScheduledTask -TaskName $taskName -TaskPath '\' -Action $action | Out-Null
} else {
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5)
    $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive
    $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings | Out-Null
}
