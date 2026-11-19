# Reproducir los sondeos de auditoría

Ejecutar desde `integracion_erpnext` en esta PC, con el sitio ficticio existente
encendido y sus datos de ejemplo. No usar en producción. Los scripts comprueban
el nombre del sitio y revierten sus transacciones al terminar.

```powershell
$directorioPruebas = (Resolve-Path .\pruebas).Path
Get-Content -Raw .\auditoria\verificar_auditoria.py | wsl -d Ubuntu --cd "$directorioPruebas" --exec docker compose exec -T backend env/bin/python -
Get-Content -Raw .\auditoria\verificar_unidades.py | wsl -d Ubuntu --cd "$directorioPruebas" --exec docker compose exec -T backend env/bin/python -
```

Se necesitan los ejemplos MAT-MR-2026-00001, MAT-PRE-2026-00001,
PUR-ORD-2026-00007 y los tres usuarios ficticios existentes. No se necesitan
contraseñas en estos archivos: los sondeos se ejecutan dentro del contenedor
administrado. Las pruebas de autorización usan las identidades indicadas,
pero esto no sustituye pruebas de autenticación HTTP desde otras estaciones.

`resultados.json` y `unidades.json` son las salidas conservadas del 19/11/2026.
`BASELINE.json` identifica el commit auditado y las huellas del código original.
`HASHES-SERVIDOR.json` contiene SHA256 de ocho módulos instalados, leídos como
UTF-8 y con saltos de línea normalizados a LF; coincidieron con el código local.

La matriz en Markdown y JSON contiene las mismas 55 capacidades. P1/P2 indican
prioridad para cerrar equivalencia, no vulnerabilidades de seguridad. Las funciones
no integradas no se consideran aprobadas por el hecho de pasar las tres suites.
