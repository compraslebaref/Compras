@echo off
chcp 65001 >nul
title Plataforma ERP Lebaref (Estacion Servidor)
set "ERP_URL=http://127.0.0.1:8186/app/lebaref"
set "PRUEBAS_DIR=%~dp0integracion_erpnext\pruebas"

echo ========================================================
echo   LEBAREF - Plataforma de Compras, Almacen y Activos
echo ========================================================
echo Verificando servidor ERPNext en %ERP_URL% ...

powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri 'http://127.0.0.1:8186/api/method/ping' -UseBasicParsing -TimeoutSec 3; if ($r.StatusCode -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }"
if %ERRORLEVEL% NEQ 0 (
    echo Iniciando contenedor ERPNext Lebaref en segundo plano...
    schtasks.exe /Run /TN "Lebaref-Pruebas-WSL" >nul 2>&1
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PRUEBAS_DIR%\Entorno-Pruebas.ps1" -Accion Iniciar -DistribucionWSL Ubuntu
)
schtasks.exe /Run /TN "Lebaref-Pruebas-WSL" >nul 2>&1

if exist "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" (
    start "" "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" --app="%ERP_URL%"
    exit /b 0
)
if exist "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" (
    start "" "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" --app="%ERP_URL%"
    exit /b 0
)
if exist "%ProgramFiles%\Google\Chrome\Application\chrome.exe" (
    start "" "%ProgramFiles%\Google\Chrome\Application\chrome.exe" --app="%ERP_URL%"
    exit /b 0
)
start "" "%ERP_URL%"
exit /b 0
