@echo off
chcp 65001 >nul
title Conectar a ERP Lebaref (Red Local de Oficina)
set "SERVER_IP=192.168.0.62"
set "SERVER_HOST=LAPTOP-RB66QGA0"
set "PORT=8187"
set "TARGET_URL=http://%SERVER_IP%:%PORT%/app/lebaref"

echo ========================================================
echo   LEBAREF - Conexion de Equipo Auxiliar en Red Local
echo ========================================================
echo Buscando servidor Lebaref en la red de la oficina...

powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri 'http://%SERVER_IP%:%PORT%/api/method/ping' -UseBasicParsing -TimeoutSec 3; if ($r.StatusCode -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }"
if %ERRORLEVEL% NEQ 0 (
    powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri 'http://%SERVER_HOST%:%PORT%/api/method/ping' -UseBasicParsing -TimeoutSec 3; if ($r.StatusCode -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }"
    if %ERRORLEVEL% EQU 0 (
        set "TARGET_URL=http://%SERVER_HOST%:%PORT%/app/lebaref"
    ) else (
        echo.
        echo [AVISO] No se pudo contactar al servidor Lebaref (%SERVER_IP%:%PORT% / %SERVER_HOST%:%PORT%).
        echo Verifica que:
        echo   1. Ambos equipos esten en la misma red Wi-Fi / Ethernet de la oficina.
        echo   2. En la PC servidor este abierto ERPNext (Abrir_ERPNext_Lebaref.cmd).
        echo   3. Se haya ejecutado una sola vez Habilitar_Red_Oficina_Admin.cmd en la PC servidor.
        echo.
        pause
        exit /b 1
    )
)

if exist "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" (
    start "" "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" --app="%TARGET_URL%"
    exit /b 0
)
if exist "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" (
    start "" "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" --app="%TARGET_URL%"
    exit /b 0
)
if exist "%ProgramFiles%\Google\Chrome\Application\chrome.exe" (
    start "" "%ProgramFiles%\Google\Chrome\Application\chrome.exe" --app="%TARGET_URL%"
    exit /b 0
)
start "" "%TARGET_URL%"
exit /b 0
