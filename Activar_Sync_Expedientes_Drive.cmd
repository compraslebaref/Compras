@echo off
chcp 65001 >nul
title Activar sincronizacion automatica de expedientes con Google Drive
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0Registrar_Sync_Expedientes_Drive.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo No se pudo crear la tarea programada.
    pause
    exit /b 1
)
echo.
echo [LISTO] Cada 5 minutos se copian los archivos adjuntos del ERP a Google Drive (carpeta Solicitudes).
echo Requisito: Google Drive para escritorio instalado e iniciado con compras.lebaref@gmail.com.
echo Bitacora: "%~dp0..\expedientes_drive.log"
pause
