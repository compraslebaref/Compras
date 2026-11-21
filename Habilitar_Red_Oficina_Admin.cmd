@echo off
chcp 65001 >nul
title Habilitar Acceso de Red Local Oficina para ERP Lebaref (WSL2 -> LAN 8187)
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Solicitando autorizacion de Administrador de Windows para habilitar el acceso de red local...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 0
)

echo ====================================================================
echo   Configurando puente oficial WSL2 -> Red Local Oficina (Puerto 8187)
echo ====================================================================
netsh interface portproxy delete v4tov4 listenport=8187 listenaddress=0.0.0.0 >nul 2>&1
netsh interface portproxy add v4tov4 listenport=8187 listenaddress=0.0.0.0 connectport=8186 connectaddress=127.0.0.1

powershell -NoProfile -Command "Remove-NetFirewallRule -DisplayName 'Lebaref ERPNext Red Oficina (8187)' -ErrorAction SilentlyContinue; New-NetFirewallRule -DisplayName 'Lebaref ERPNext Red Oficina (8187)' -Direction Inbound -Protocol TCP -LocalPort 8187 -RemoteAddress LocalSubnet -Action Allow | Out-Null"

echo.
echo [LISTO] Acceso de red local habilitado de forma permanente (solo subred local).
echo Los equipos de tus auxiliares ya pueden abrir Conectar_Lebaref_Auxiliar.cmd
echo o entrar a: http://192.168.0.62:8187/app/lebaref
echo.
netsh interface portproxy show v4tov4
echo.
pause
