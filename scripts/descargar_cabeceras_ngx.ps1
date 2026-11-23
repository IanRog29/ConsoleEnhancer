# descargar_cabeceras_ngx.ps1 - baja las DOS cabeceras publicas de NGX con las que se compila src\PrismaNr\PrismaNr.cpp (y la licencia del
# SDK) del repositorio oficial de NVIDIA (github.com/NVIDIA/DLSS, carpeta include), en una version fija y comprobando el SHA-256 de cada
# fichero. Van a terceros\dlss_sdk (fuera de git: .gitignore terceros/). Solo sirven para COMPILAR: no van en el repositorio ni en el zip.
#
# Uso: powershell -ExecutionPolicy Bypass -File scripts\descargar_cabeceras_ngx.ps1 [-Destino <carpeta>] [-Forzar]
param(
    [string]$Raiz = (Split-Path -Parent $PSScriptRoot),
    [string]$Destino = '',
    [switch]$Forzar
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
if (-not $Destino) { $Destino = Join-Path $Raiz 'terceros\dlss_sdk' }
# DLSS SDK v310.9.16 (etiqueta v310.9.16 = commit 9981c6350dc58c0ce21f024fd65e31e851c31689)
$commit = '9981c6350dc58c0ce21f024fd65e31e851c31689'
$ficheros = @(
    @{ ruta = 'include/nvsdk_ngx_defs.h';   sha = 'ea23f33497cd274860d1c25a97644fce807dcb0037c594547203343103fad03e' },
    @{ ruta = 'include/nvsdk_ngx_params.h'; sha = '943bc8cc5cdae03b6303016fbad3183636f2335ae27a2d18776798c3b4efabbc' },
    @{ ruta = 'LICENSE.txt';                sha = 'd4216e39ebef5f9b50a6712ebb37beeb5379862a67733a9999c651f21592aaf0' }
)
foreach ($f in $ficheros) {
    $local = Join-Path $Destino ($f.ruta -replace '/', '\')
    New-Item -ItemType Directory -Force (Split-Path $local) | Out-Null
    if (-not $Forzar -and (Test-Path $local) -and (Get-FileHash -Algorithm SHA256 -LiteralPath $local).Hash.ToLowerInvariant() -eq $f.sha) {
        Write-Host "  ya esta: $($f.ruta)"; continue
    }
    $url = "https://raw.githubusercontent.com/NVIDIA/DLSS/$commit/$($f.ruta)"
    $tmp = "$local.parcial"
    Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -UserAgent 'ConsoleEnhancer-descargar_cabeceras_ngx'
    $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $tmp).Hash.ToLowerInvariant()
    if ($h -ne $f.sha) { Remove-Item -LiteralPath $tmp -Force; throw "SHA-256 distinto en $url`: $h (se esperaba $($f.sha))" }
    Move-Item -LiteralPath $tmp -Destination $local -Force
    Write-Host "  bajado: $($f.ruta) (SHA-256 bien)"
}
Write-Host "Cabeceras de NGX (DLSS SDK v310.9.16) en $Destino\include. Ahora: build_prismanr.bat"
