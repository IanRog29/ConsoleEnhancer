# descargar_dependencias.ps1 - baja los programas de terceros que usa ConsoleEnhancer (versiones fijas, de sus releases oficiales de
# GitHub, comprobando el SHA-256) y los deja donde los buscan build.bat, build_motor.bat y empaquetar.ps1:
#
#   terceros\mpv\          mpv (mpv-player/mpv, build oficial x86_64-w64-mingw32 "full": FFmpeg con dshow dentro) + su
#                          portable_config\mpv.conf (resources\mpv.conf: asi el mpv.conf del usuario, si tiene, no cambia nada)
#   terceros\xess\         Intel XeSS SDK (intel/xess): libxess.dll, libxess_fg.dll, libxell.dll y su licencia (XeSS y XeSS FG del motor)
#   terceros\fsr\          AMD FSR SDK (GPUOpen FidelityFX-SDK): las amd_fidelityfx_*_dx12.dll firmadas (FSR 3.1 / 4 y FSR 3 FG del motor)
#                          (1.2.4 ronda 6: sin Magpie, que antes traia estas DLL)
#   terceros\streamline\   NVIDIA Streamline SDK (NVIDIA-RTX/Streamline): solo bin\x64\ (las DLL de produccion) y las licencias
#
# Versiones, URL, SHA-256 y que se coge de cada zip: scripts\components.json (la misma tabla que usa la app para bajar mpv, XeSS y FSR
# la primera vez que se abre). Las descargas se guardan en terceros\_descargas (si ya estan y el hash cuadra, no se bajan otra vez).
# Nada de esto va al repositorio (.gitignore). Solo PowerShell 5.1 de Windows, nada que instalar.
#
# Uso: powershell -ExecutionPolicy Bypass -File scripts\descargar_dependencias.ps1 [-Solo mpv,xess,fsr,streamline] [-Forzar]
#        [-Destino <carpeta>] [-Cache <carpeta>] [-ConservarNoOficial]
#  -Forzar               rehace las carpetas aunque ya esten en la version buena
#  -ConservarNoOficial   (de antes, con Magpie: ya no hace nada)
param(
    [string[]]$Solo = @('mpv', 'xess', 'fsr', 'streamline'),
    [string]$Raiz = (Split-Path -Parent $PSScriptRoot),
    [string]$Destino = '',
    [string]$Cache = '',
    [switch]$Forzar,
    [switch]$ConservarNoOficial
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # (con la barra de progreso Invoke-WebRequest va muchisimo mas lento)
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not $Destino) { $Destino = Join-Path $Raiz 'terceros' }
if (-not $Cache) { $Cache = Join-Path $Destino '_descargas' }
New-Item -ItemType Directory -Force $Destino, $Cache | Out-Null

# ---- la tabla (scripts\components.json) ----
$tabla = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'components.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$deps = [ordered]@{}
foreach ($c in $tabla.componentes) { $deps[$c.nombre] = $c }

function Bajar($d) {
    $f = Join-Path $Cache $d.archivo
    if (Test-Path $f) {
        $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $f).Hash.ToLowerInvariant()
        if ($h -eq $d.sha256) { Write-Host "  ya descargado: $($d.archivo)"; return $f }
        Write-Host "  $($d.archivo) en la cache con otro hash ($h): se baja otra vez"
        Remove-Item -LiteralPath $f -Force
    }
    Write-Host ("  bajando {0} ({1:N0} MB)" -f $d.url, ($d.bytes / 1MB))
    $tmp = "$f.parcial"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-WebRequest -Uri $d.url -OutFile $tmp -UseBasicParsing -UserAgent 'ConsoleEnhancer-descargar_dependencias'
    $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $tmp).Hash.ToLowerInvariant()
    if ($h -ne $d.sha256) { Remove-Item -LiteralPath $tmp -Force; throw "SHA-256 distinto en $($d.url): $h (se esperaba $($d.sha256))" }
    Move-Item -LiteralPath $tmp -Destination $f -Force
    Write-Host ("  {0:N0} MB en {1:N0} s, SHA-256 bien" -f ((Get-Item $f).Length / 1MB), $sw.Elapsed.TotalSeconds)
    return $f
}

# La ruta de dentro del zip ('/') que se queda, ya cambiada (quitar_prefijo, renombrar), o $null. La misma regla que la app
# (Componentes.Ruta en src\Lanzador\Componentes.cs)
function Ruta($d, [string]$r, [bool]$noOficialTambien) {
    if ($d.quitar_prefijo) { $r = $r -replace $d.quitar_prefijo, '' }
    foreach ($x in $d.renombrar) { if ($r -match $x.de) { $r = $r -replace $x.de, $x.a } }
    if (-not $r) { return $null }
    if (@($d.incluir).Count -gt 0 -and -not (@($d.incluir) | Where-Object { $r -match $_ })) { return $null }
    if (@($d.excluir) | Where-Object { $r -match $_ }) { return $null }
    if (-not $noOficialTambien -and (@($d.no_oficial) | Where-Object { $r -match $_ })) { return $null }
    return $r
}

function Sacar($d, [string]$zip, [string]$carpeta) {
    $n = 0
    $full = ([IO.Path]::GetFullPath($carpeta)).TrimEnd('\') + '\'
    $z = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($e in $z.Entries) {
            if ($e.FullName.EndsWith('/')) { continue }
            $rel = Ruta $d $e.FullName.Replace('\', '/') ([bool]$ConservarNoOficial)
            if (-not $rel) { continue }
            $to = Join-Path $carpeta $rel.Replace('/', '\')
            # (nada fuera de la carpeta: rutas con ..)
            if (-not ([IO.Path]::GetFullPath($to)).StartsWith($full, [StringComparison]::OrdinalIgnoreCase)) { throw "Ruta rara en el zip: $($e.FullName)" }
            New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $to, $true)
            $n++
        }
    } finally { $z.Dispose() }
    return $n
}

function Preparar([string]$nombre) {
    $d = $deps[$nombre]
    $dir = Join-Path $Destino $nombre
    $marca = Join-Path $dir '.version'
    if (-not $Forzar -and (Test-Path $marca) -and ((Get-Content -LiteralPath $marca -Raw).Trim() -eq "$($d.version) $($d.sha256)")) {
        Write-Host "$nombre $($d.version): ya esta en $dir"
        return
    }
    Write-Host "$nombre $($d.version):"
    $zip = Bajar $d
    if (Test-Path $dir) {
        # (puede ser el destino de una union bin\mpv, bin\xess o bin\fsr: se vacia por dentro, la carpeta se queda)
        Get-ChildItem -LiteralPath $dir -Force | Remove-Item -Recurse -Force
    }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $n = Sacar $d $zip $dir
    if (-not (Test-Path (Join-Path $dir $d.comprobar.Replace('/', '\')))) { throw "$($nombre): no esta $($d.comprobar) en el zip" }
    if ($nombre -eq 'mpv') {
        $conf = Join-Path $Raiz 'resources\mpv.conf'
        if (Test-Path $conf) { New-Item -ItemType Directory -Force "$dir\portable_config" | Out-Null; Copy-Item -LiteralPath $conf "$dir\portable_config\mpv.conf" -Force; $n++ }
    }
    if ($nombre -eq 'streamline') {
        foreach ($f in @('sl.interposer.dll', 'sl.common.dll', 'sl.dlss.dll', 'sl.reflex.dll', 'sl.pcl.dll', 'nvngx_dlss.dll')) {   # (1.2.4 ronda 6: sin DLSS-G)
            if (-not (Test-Path "$dir\bin\x64\$f")) { throw "Streamline: no esta bin\x64\$f en el zip" }
        }
    }
    Set-Content -LiteralPath $marca -Value "$($d.version) $($d.sha256)" -Encoding ASCII
    Write-Host "  $n ficheros en $dir"
}

$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($s in $Solo) {
    foreach ($nombre in ($s -split ',')) {
        $nombre = $nombre.Trim().ToLowerInvariant()
        if (-not $nombre) { continue }
        if (-not $deps.Contains($nombre)) { throw "No se que es '$nombre' ($($deps.Keys -join ', '))" }
        Preparar $nombre
    }
}
# Lo que hay (para THIRD-PARTY y para quien empaquete)
$txt = @('# terceros\ - lo que ha bajado scripts\descargar_dependencias.ps1 (no va al repositorio)', '')
foreach ($k in $deps.Keys) {
    $d = $deps[$k]
    $ok = Test-Path (Join-Path $Destino "$k\.version")
    $txt += "$k $($d.version) $(if ($ok) { '' } else { '(no descargado)' })"
    $txt += "  $($d.url)"
    $txt += "  sha256 $($d.sha256)"
    $txt += "  licencia: $($d.licencia)"
    $txt += "  codigo fuente: $($d.fuente)"
}
Set-Content -LiteralPath (Join-Path $Destino 'VERSIONS.txt') -Value $txt -Encoding UTF8
Write-Host ("Hecho en {0:N0} s. Ahora: build.bat, build_motor.bat y scripts\empaquetar.ps1 (o todo junto: preparar.bat)" -f $sw.Elapsed.TotalSeconds)
