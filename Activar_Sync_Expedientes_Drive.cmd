@echo off
chcp 65001 >nul
title Activar sincronizacion automatica de expedientes con Google Drive
set "SCRIPT=%~dp0Sincronizar_Expedientes_Drive_Local.ps1"
schtasks /Create /TN "Lebaref-Expedientes-Drive" /SC MINUTE /MO 5 /F /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"%SCRIPT%\""
if %ERRORLEVEL% NEQ 0 (
    echo No se pudo crear la tarea programada.
    pause
    exit /b 1
)
echo.
echo [LISTO] Cada 5 minutos se copian los archivos adjuntos del ERP a Google Drive (carpeta Solicitudes).
echo Requisito: Google Drive para escritorio instalado e iniciado con compras.lebaref@gmail.com.
echo Bitacora: C:\Users\oscar\Proyectos\Lebaref\expedientes_drive.log
pause
