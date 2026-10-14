@echo off
title Sistema de Compras y Almacen (Windows 11 UI)
cd /d "%~dp0"
if exist "bin\Debug\net8.0-windows\SistemaComprasAlmacen.exe" (
    start "" "bin\Debug\net8.0-windows\SistemaComprasAlmacen.exe"
) else (
    "C:\Users\oscar\.dotnet\dotnet.exe" run --project "SistemaComprasAlmacen.csproj"
)
