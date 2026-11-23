# empaquetar.ps1 - monta la carpeta portable de ConsoleEnhancer (dist\ConsoleEnhancer y, con -Zip, el zip para GitHub Releases)
#
# Lista BLANCA. Va: el lanzador, el motor y sus DLL de NVIDIA Streamline / DLSS / Reflex (redistribuibles dentro de una aplicacion,
# con sus licencias), scripts\components.json, idiomas, recursos, licencias y avisos.
# NO va (y se comprueba al final):
#  - Intel XeSS ni AMD FSR: no los reparte ConsoleEnhancer. La app los baja la primera vez que se abre de sus releases oficiales
#    (con su SHA-256: scripts\components.json, src\Lanzador\Componentes.cs). (1.2.4, SINMPV: mpv ya no se usa ni se baja: la imagen,
#    las capturas y el sonido los hace el motor)
#    (1.2.4 ronda 6: sin Magpie; las DLL de XeSS y FSR que traia salen ahora de los SDK oficiales de Intel y AMD)
#  - nvngx_dlssnr.dll ("DLSS 5": DLL de NVIDIA modificada, sin firma), runtimes\dlssg_sm86 (autofirmado) ni ninguna otra DLL de NVIDIA que
#    no sea de las redistribuibles. (1.2.4) SI va la pieza propia del motor engine\PrismaNr.dll (build_prismanr.bat): carga el archivo
#    que pone el usuario saltandose la comprobacion de NGX (ver README). (1.2.4 ronda 6: sin DLSS FG, ni PrismaFg.dll ni las DLL de DLSS-G)
#  - el ini, logs, data\, games\ ni nada del PC donde se monta
# Antes: build.bat y build_motor.bat (o preparar.bat). Streamline sale de terceros\streamline (scripts\descargar_dependencias.ps1;
# si falta, se baja, salvo con -SinDescargar).
#
# Uso: powershell -ExecutionPolicy Bypass -File scripts\empaquetar.ps1 [-Zip] [-Version v1.0.0] [-Web https://github.com/<usuario>/ConsoleEnhancer]
#        [-Destino <carpeta>] [-Forzar] [-SinDescargar] [-Streamline <carpeta>]
#  -Zip       ademas dist\ConsoleEnhancer-<Version>.zip (o ConsoleEnhancer.zip) y su .sha256
#  -Version   la version que se escribe en README.txt y en el nombre del zip (la etiqueta de git en GitHub Actions)
#  -Web       la pagina del proyecto (codigo fuente) que se escribe en README.txt y licenses\README.txt
# Datos personales: ademas del usuario y el nombre de este PC, busca las lineas de scripts\privado.txt si existe (no va a git).
param(
    [string]$Raiz = (Split-Path -Parent $PSScriptRoot),
    [string]$Destino = '',
    [string]$Streamline = '',
    [string]$Version = '',
    [string]$Web = 'https://github.com/IanRog29/ConsoleEnhancer',
    [switch]$Forzar,
    [switch]$Zip,
    [switch]$SinDescargar
)
$ErrorActionPreference = 'Stop'
$sw = [Diagnostics.Stopwatch]::StartNew()
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
function Full([string]$p) { return [IO.Path]::GetFullPath($p).TrimEnd('\') }
$Raiz = Full $Raiz
if (-not $Destino) { $Destino = "$Raiz\dist\ConsoleEnhancer" }
$Destino = Full $Destino
$porDefecto = -not $Streamline
if (-not $Streamline) { $Streamline = "$Raiz\terceros\streamline" }
foreach ($o in @($Raiz, $Streamline)) { if ($Destino -ieq (Full $o) -or $Destino.StartsWith((Full $o) + '\', [StringComparison]::OrdinalIgnoreCase)) { if ($Destino.StartsWith("$Raiz\dist\", [StringComparison]::OrdinalIgnoreCase)) { continue }; throw "El destino no puede estar dentro de $o (salvo $Raiz\dist)" } }
$tabla = (Get-Content -LiteralPath "$Raiz\scripts\components.json" -Raw -Encoding UTF8 | ConvertFrom-Json).componentes
$sl = $tabla | Where-Object { $_.nombre -eq 'streamline' }

# ---- Streamline: si falta, se baja (version fija, con su SHA-256) ----
if (-not (Test-Path "$Streamline\bin\x64\sl.interposer.dll")) {
    if ($SinDescargar -or -not $porDefecto) { throw "Falta Streamline en $Streamline (scripts\descargar_dependencias.ps1 -Solo streamline)" }
    Write-Output 'Falta Streamline: scripts\descargar_dependencias.ps1 -Solo streamline'
    & "$PSScriptRoot\descargar_dependencias.ps1" -Raiz $Raiz -Solo streamline
}
foreach ($need in @("$Raiz\bin\ConsoleEnhancer.exe", "$Raiz\bin\engine\ConsoleEnhancerEngine.exe")) { if (-not (Test-Path $need)) { throw "Falta $need (compila con build.bat y build_motor.bat, o preparar.bat)" } }
# (1.2.4) las piezas propias del motor (C++, Visual Studio; PrismaNr con las cabeceras de NGX de scripts\descargar_cabeceras_ngx.ps1)
$propias = @('PrismaNr.dll', 'CeFsrFg.dll')   # (1.2.4 ronda 6, FGMULTI: + CeFsrFg.dll, la FSR FG x3 / x4: codigo MIT de AMD compilado aqui, build_fsrfg.bat)
foreach ($d in $propias) { if (-not (Test-Path "$Raiz\bin\engine\$d")) { throw "Falta $Raiz\bin\engine\$d (scripts\descargar_cabeceras_ngx.ps1, build_prismanr.bat y build_fsrfg.bat)" } }
# (1.2.3, DEFENDER) la version que llevan los exe en sus propiedades (src\Version.cs) = la del paquete: que no salga una version con los
# exe de otra, ni sin version
$verCs = [regex]::Match((Get-Content -LiteralPath "$Raiz\src\Version.cs" -Raw -Encoding UTF8), 'const string Texto = "([^"]+)"').Groups[1].Value
if (-not $verCs) { throw "No se lee CeVersion.Texto en $Raiz\src\Version.cs" }
foreach ($e in @("$Raiz\bin\ConsoleEnhancer.exe", "$Raiz\bin\ConsoleEnhancer Settings.exe", "$Raiz\bin\engine\ConsoleEnhancerEngine.exe")) {
    $pv = [Diagnostics.FileVersionInfo]::GetVersionInfo($e).ProductVersion
    if ($pv -ne $verCs) { throw "$e lleva la version '$pv' y src\Version.cs dice $verCs`: vuelve a compilar (build.bat y build_motor.bat)" }
}
if ($Version -and $Version.TrimStart('v') -ne $verCs) { throw "El paquete es la $Version y src\Version.cs dice $verCs`: pon CeVersion.Texto = `"$($Version.TrimStart('v'))`" en src\Version.cs, compila otra vez y vuelve a montarlo" }
if (Test-Path $Destino) {
    if (@(Get-ChildItem -LiteralPath $Destino -Force).Count -gt 0) {
        if ($Forzar -and (Test-Path "$Destino\ConsoleEnhancer.exe") -and (Test-Path "$Destino\licenses")) { Remove-Item -LiteralPath $Destino -Recurse -Force }
        else { throw "$Destino ya tiene cosas (con -Forzar se rehace, solo si es un paquete de antes)" }
    }
}
New-Item -ItemType Directory -Force $Destino | Out-Null
$script:n = 0; $script:bytes = 0
function Poner([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    Copy-Item -LiteralPath $from -Destination $to -Force
    $script:n++; $script:bytes += (Get-Item -LiteralPath $from).Length
}
function Texto([string]$to, [string[]]$lineas) {
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    [IO.File]::WriteAllText($to, (($lineas -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding($true)))
    $script:n++
}
$verTxt = if ($Version) { $Version } else { '(sin version)' }
$webTxt = if ($Web) { $Web } else { 'la pagina de GitHub del proyecto / the project''s GitHub page' }

# ---- la app ----
Poner "$Raiz\bin\ConsoleEnhancer.exe" "$Destino\ConsoleEnhancer.exe"
# (20-11, CIERRES de Prisma) legacyUnhandledExceptionPolicy: un fallo en un hilo secundario va al log y no cierra la app
Poner "$Raiz\resources\ConsoleEnhancer.exe.config" "$Destino\ConsoleEnhancer.exe.config"
Texto "$Destino\portable.txt" @(
    'ConsoleEnhancer portable: with this file the data (settings, log, games, calibration) is kept in the data\ folder next to it.',
    'Delete it to keep them in %LOCALAPPDATA%\ConsoleEnhancer (e.g. if the app folder cannot be written to).',
    'ConsoleEnhancer portable: con este fichero los datos (ajustes, log, juegos, calibracion) van en la carpeta data\ de aqui al lado.',
    'Borralo para guardarlos en %LOCALAPPDATA%\ConsoleEnhancer (p. ej. si la carpeta de la app no se puede escribir).')
# (23-11, 1.2.4 SINMPV: sin los lua ni los glsl de mpv: la imagen, las capturas y el sonido los hace el motor)
foreach ($f in @('components.json')) { Poner "$Raiz\scripts\$f" "$Destino\scripts\$f" }
Poner "$Raiz\resources\base.ini" "$Destino\resources\base.ini"
# (1.2.0) el panel de ajustes (sustituye a "Inicio y apagado.bat"): como se usa el PC, apagado, DLSS 5, componentes, desinstalar
Poner "$Raiz\bin\ConsoleEnhancer Settings.exe" "$Destino\ConsoleEnhancer Settings.exe"
Get-ChildItem "$Raiz\languages" -File -Filter *.txt | ForEach-Object { Poner $_.FullName "$Destino\languages\$($_.Name)" }

# ---- el motor y las DLL de produccion de Streamline (firmadas por NVIDIA) ----
Poner "$Raiz\bin\engine\ConsoleEnhancerEngine.exe" "$Destino\engine\ConsoleEnhancerEngine.exe"
foreach ($d in $propias) { Poner "$Raiz\bin\engine\$d" "$Destino\engine\$d" }   # (1.2.4: PrismaNr.dll, codigo propio)
# (1.2.4 ronda 6, QUITAFG: sin sl.dlss_g.dll ni nvngx_dlssg.dll: la app ya no tiene DLSS Frame Generation)
foreach ($d in @('sl.interposer.dll', 'sl.common.dll', 'sl.dlss.dll', 'sl.reflex.dll', 'sl.pcl.dll', 'nvngx_dlss.dll', 'sl.deepdvc.dll', 'nvngx_deepdvc.dll')) {
    Poner "$Streamline\bin\x64\$d" "$Destino\engine\$d"
}

# ---- licencias y avisos ----
$lic = "$Destino\licenses"
Poner "$Raiz\LICENSE" "$lic\ConsoleEnhancer-LICENSE.txt"
Poner "$Raiz\LICENSE-EXCEPTION.txt" "$lic\ConsoleEnhancer-LICENSE-EXCEPTION.txt"
Poner "$Raiz\THIRD-PARTY.md" "$lic\THIRD-PARTY.md"
# (1.2.4 ronda 6, FGMULTI) engine\CeFsrFg.dll lleva codigo de AMD FidelityFX SDK 1.1.4 (MIT): su aviso de copyright y su licencia
Texto "$lic\AMD-FidelityFX-SDK-1.1.4-LICENSE.txt" @(
    'engine\CeFsrFg.dll: AMD FidelityFX SDK v1.1.4 (frame interpolation + optical flow, DX12 backend), modified by ConsoleEnhancer',
    '(parches\fidelityfx-fi-multi.patch: frames at any time between two real frames). https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK',
    '', 'Copyright (C) 2024 Advanced Micro Devices, Inc.', '',
    'Permission is hereby granted, free of charge, to any person obtaining a copy',
    'of this software and associated documentation files(the "Software"), to deal',
    'in the Software without restriction, including without limitation the rights',
    'to use, copy, modify, merge, publish, distribute, sublicense, and /or sell',
    'copies of the Software, and to permit persons to whom the Software is',
    'furnished to do so, subject to the following conditions :', '',
    'The above copyright notice and this permission notice shall be included in',
    'all copies or substantial portions of the Software.', '',
    'THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR',
    'IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,',
    'FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE',
    'AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER',
    'LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,',
    'OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN',
    'THE SOFTWARE.')
foreach ($f in @('license.txt', '3rd-party-licenses.md', 'nvngx_dlss.license.txt', 'reflex.license.txt')) {
    if (-not (Test-Path "$Streamline\$f")) { throw "Falta la licencia $Streamline\$f (scripts\descargar_dependencias.ps1 -Solo streamline -Forzar)" }
    Poner "$Streamline\$f" "$lic\streamline\$f"
}
Texto "$lic\NVIDIA-NOTICE.txt" @(
    'Componentes de NVIDIA en engine\ / NVIDIA components in engine\',
    '',
    'engine\nvngx_dlss.dll (NVIDIA DLSS Super Resolution) y engine\nvngx_deepdvc.dll (NVIDIA RTX Dynamic Vibrance) son de NVIDIA Corporation',
    'y NO estan bajo la GPL de ConsoleEnhancer: se reparten sin cambios segun la NVIDIA RTX SDKs License (streamline\nvngx_dlss.license.txt).',
    'engine\sl.*.dll son NVIDIA Streamline (MIT, streamline\license.txt); sl.reflex.dll y sl.pcl.dll llevan NVIDIA Reflex (streamline\reflex.license.txt).',
    'Al usarlas aceptas tambien estas condiciones de NVIDIA: solo se usan como parte de ConsoleEnhancer; no se pueden vender, alquilar,',
    'sublicenciar ni repartir sueltas; no se puede hacer ingenieria inversa, descompilarlas o desensamblarlas, ni quitar sus avisos; no se',
    'puede eludir ninguna limitacion tecnica, cifrado o mecanismo de seguridad o autenticacion; NVIDIA las da TAL CUAL, sin garantia de',
    'ningun tipo, y no responde de ningun dano; estan sujetas a las leyes de exportacion de los EE. UU.; NVIDIA conserva todos sus derechos.',
    'DLSS solo funciona con graficas NVIDIA GeForce RTX. ConsoleEnhancer no esta patrocinado ni respaldado por NVIDIA.',
    '',
    'engine\nvngx_dlss.dll (NVIDIA DLSS Super Resolution) and engine\nvngx_deepdvc.dll (NVIDIA RTX Dynamic Vibrance) belong to NVIDIA',
    'Corporation and are NOT covered by ConsoleEnhancer''s GPL: they are redistributed unmodified under the NVIDIA RTX SDKs License',
    '(streamline\nvngx_dlss.license.txt). engine\sl.*.dll are NVIDIA Streamline (MIT, streamline\license.txt); sl.reflex.dll and sl.pcl.dll',
    'contain NVIDIA Reflex (streamline\reflex.license.txt). By using them you also accept these NVIDIA terms: they may only be used as part of',
    'ConsoleEnhancer; they may not be sold, rented, sublicensed or distributed on their own; you may not reverse engineer, decompile or',
    'disassemble them, or remove their notices; you may not bypass, disable or circumvent any technical limitation, encryption, security or',
    'authentication mechanism; NVIDIA provides them AS IS, without warranty of any kind, and is not liable for any damages; they are subject',
    'to U.S. export laws; NVIDIA reserves all rights. DLSS only works on NVIDIA GeForce RTX GPUs. ConsoleEnhancer is not sponsored or',
    'endorsed by NVIDIA.')
$ver = @('# Versiones de los componentes de terceros / third-party component versions (scripts\components.json)', '')
foreach ($c in $tabla) {
    $donde = if ($c.al_abrir) { 'NO va en el paquete: la app lo baja la primera vez de / NOT in the package: downloaded on first start from' } else { 'en el paquete / in the package:' }
    $ver += "$($c.nombre) $($c.version) - $donde"
    $ver += "  $($c.url)"
    $ver += "  sha256 $($c.sha256)"
    $ver += "  licencia / license: $($c.licencia)"
    $ver += "  codigo fuente / source: $($c.fuente)"
}
Texto "$lic\VERSIONS.txt" $ver
$xeC = $tabla | Where-Object { $_.nombre -eq 'xess' }; $fsC = $tabla | Where-Object { $_.nombre -eq 'fsr' }
Texto "$lic\README.txt" @(
    "ConsoleEnhancer $verTxt - licencias y componentes de terceros / licenses and third-party components (detalle: THIRD-PARTY.md)",
    '',
    " - ConsoleEnhancer: GPL-3.0 (ConsoleEnhancer-LICENSE.txt) con un permiso adicional de la seccion 7 para usarlo con las librerias de",
    "   NVIDIA, AMD, Intel y Microsoft (ConsoleEnhancer-LICENSE-EXCEPTION.txt). Codigo fuente: $webTxt",
    '   Incluye: FSR 1 EASU/RCAS y CAS (AMD FidelityFX, MIT), FXAA 3.11 (NVIDIA, BSD-3-Clause) y un filtro derivado de Magpie (GPL-3.0):',
    '   avisos completos en THIRD-PARTY.md.',
    " - NVIDIA Streamline $($sl.version) (engine\sl.*.dll): MIT (streamline\license.txt, 3rd-party-licenses.md).",
    ' - NVIDIA DLSS Super Resolution, RTX Dynamic Vibrance (engine\nvngx_dlss.dll, nvngx_deepdvc.dll): NVIDIA RTX SDKs License',
    '   (streamline\nvngx_dlss.license.txt) y NVIDIA-NOTICE.txt.',
    ' - NVIDIA Reflex (engine\sl.reflex.dll, sl.pcl.dll): streamline\reflex.license.txt.',
    ' - engine\PrismaNr.dll: codigo propio de ConsoleEnhancer (GPL-3.0); carga el archivo que pone el usuario sin la',
    '   comprobacion de NGX de NVIDIA (ver THIRD-PARTY.md) / own code; it loads the user-added file without NVIDIA''s NGX check.',
    '',
    'Bajados por la app la primera vez (NO van en el paquete; cada uno con su licencia, dentro de su carpeta):',
    "Downloaded by the app on first start (NOT in the package; each one keeps its license inside its folder):",
    " - Intel XeSS SDK $($xeC.version) (xess\): $($xeC.licencia). $($xeC.fuente)",
    " - AMD FSR SDK $($fsC.version) (fsr\): $($fsC.licencia). $($fsC.fuente)",
    '',
    'Marcas (NVIDIA, DLSS, Reflex, GeForce RTX, AMD, FSR, FidelityFX, Intel, XeSS, Nintendo, Sony, PlayStation, Microsoft, Xbox...): de sus',
    'duenos; aqui solo describen compatibilidad. ConsoleEnhancer no esta afiliado a ninguno. / Trademarks belong to their owners.',
    'No incluido / not included: nvngx_dlssnr.dll (ver extras\README.txt). Si va / included: engine\PrismaNr.dll.')
Texto "$Destino\extras\README.txt" @(
    'Extras NO OFICIALES (no vienen con ConsoleEnhancer y la app funciona entera sin ellos). Solo si ya los tienes:',
    ' - nvngx_dlssnr.dll ("DLSS 5"): DLL de NVIDIA modificada por la comunidad, SIN FIRMA, sin licencia.',
    '   Lo facil: icono de la bandeja > "Anadir DLSS 5 (tu propio archivo)..." (o el boton del ultimo paso del asistente): elige tu',
    '   archivo, la app comprueba que es una DLL de 64 bits, avisa y la copia aqui. "Quitar DLSS 5" la borra. Bajo tu responsabilidad.',
    'Anadir DLSS 5 la busca en ESTE PC si ya la tienes (Descargas, Escritorio, otras carpetas de la app...; solo local) y eliges tu.',
    'Con ella, engine\PrismaNr.dll (codigo propio de la app) la carga sin la comprobacion de NGX de NVIDIA.',
    'ConsoleEnhancer no reparte, no baja ni recomienda nvngx_dlssnr.dll: NVIDIA no la ha publicado asi y su uso puede ir',
    'contra sus condiciones. Si no sabes de donde salen, no los uses.',
    '',
    'UNOFFICIAL extra (not shipped; the app works fully without it): nvngx_dlssnr.dll (community-modified, unsigned NVIDIA DLL).',
    'If you already have it, use tray icon > "Add DLSS 5 (your own file)..." (it checks the file and copies it here) or put it here by',
    'hand (Add DLSS 5 also finds it on THIS PC if you already have it; local only); used at your own risk. With it, the app''s own',
    'engine\PrismaNr.dll loads it without NVIDIA''s NGX check. ConsoleEnhancer does not distribute, download or recommend nvngx_dlssnr.dll.')
Texto "$Destino\README.txt" @(
    "ConsoleEnhancer $verTxt - your console through a capture card, enhanced on your PC / tu consola por la capturadora, mejorada en el PC",
    '',
    'GETTING STARTED: open ConsoleEnhancer.exe. The first time it downloads Intel XeSS and AMD FSR (free components the app uses) from',
    'their official pages (about 205 MB, once; offline: put their zips in a downloads\ folder next to the exe). Then a wizard detects your capture device,',
    'GPU and display, tests picture and sound and picks the best chain for your PC. Run it again from the tray menu, where you can also make',
    'it start with Windows and choose what the PC does when the console is turned off.',
    'SETTINGS AND UNINSTALL: "ConsoleEnhancer Settings.exe" (next to it): how the PC is used, start with Windows, what the PC does',
    'when the console is turned off, DLSS 5 (your own file, in extras\), download the components again, logs and uninstall.',
    'Unzip it into one of your own folders, not Program Files.',
    "Help, issues and source code: $webTxt. Licenses: licenses\README.txt. No warranty (GPL-3.0).",
    'DLSS 5: not included and never downloaded. Legal DLSS 5 does not exist today outside NVIDIA''s own games on RTX 50;',
    'whatever you add yourself is yours and at your own risk (extras\README.txt). The app works fully without it. The engine''s own',
    'engine\PrismaNr.dll loads the file you add without NVIDIA''s NGX check, which NVIDIA''s terms do not allow.',
    'ConsoleEnhancer is free and complete. If you find it useful and want to support it (optional): https://ko-fi.com/ianrog29',
    '',
    '--- ESPANOL ---',
    '',
    'EMPEZAR: abre ConsoleEnhancer.exe.',
    ' 1. La primera vez baja Intel XeSS y AMD FSR (componentes libres que usa la app) de sus paginas oficiales: unos 205 MB, una vez.',
    '    Sin internet: pon sus zip (los enlaces salen en esa ventana y en licenses\VERSIONS.txt) en una carpeta downloads\ aqui al lado.',
    ' 2. Luego sale el asistente: detecta la capturadora, la grafica y la pantalla, prueba la imagen y el sonido y elige la mejor cadena',
    '    (DLSS / FSR 1, generacion de fotogramas segun la grafica). Otra vez: menu de la bandeja > Asistente de configuracion.',
    ' 3. En el ultimo paso (o en el menu de la bandeja): abrirse al iniciar Windows y apagar el PC al apagar la consola (con un aviso de',
    '    15 s que se cancela con el mando, una tecla o el raton).',
    'AJUSTES Y DESINSTALAR: "ConsoleEnhancer Settings.exe" (aqui al lado): como se usa el PC, abrirse con Windows, que hace el PC',
    '    al apagar la consola, DLSS 5 (tu propio archivo, en extras\), volver a bajar los componentes, registros y desinstalar.',
    'Descomprime en una carpeta tuya (Documentos, Escritorio, D:\Juegos...), no en Archivos de programa.',
    'Datos: data\ (con portable.txt) o %LOCALAPPDATA%\ConsoleEnhancer. Log: data\logs\consoleenhancer.log y engine.log.',
    'Requisitos: Windows 10 2004 o posterior (64 bits), .NET Framework 4.8, grafica con Direct3D 12, una capturadora.',
    "Ayuda, problemas y codigo fuente: $webTxt",
    'Licencias: licenses\README.txt. Extras no oficiales: extras\README.txt. Sin garantia (GPL-3.0).',
    'DLSS 5: no viene ni se baja nunca. DLSS 5 legal hoy no existe fuera de los juegos de NVIDIA en RTX 50; lo que',
    'anadas tu es tuyo y bajo tu responsabilidad (extras\README.txt). La app funciona entera sin el. Las piezas propias del motor',
    'engine\PrismaNr.dll carga el archivo que pones tu sin la comprobacion de NGX de NVIDIA, cosa que sus terminos no permiten.',
    'ConsoleEnhancer es gratis y completo. Si te es util y quieres apoyarlo (opcional): https://ko-fi.com/ianrog29')

# ---- comprobaciones ----
$noVan = @('nvngx_dlssnr.dll', 'DLSSNR-DLL-Options.zip', 'NGX_OTA_Switch.bat', 'switch2.ini', 'consoleenhancer.ini', 'hardware.txt', 'mpv.exe', 'Magpie.exe', 'PrismaNrTest.exe',
            'libxess.dll', 'libxess_fg.dll', 'libxell.dll', 'amd_fidelityfx_loader_dx12.dll',   # (1.2.4 ronda 6: XeSS / FSR los baja la app)
            'PrismaFg.dll', 'sl.dlss_g.dll', 'nvngx_dlssg.dll')   # (1.2.4 ronda 6: sin DLSS FG)
$malos = @(Get-ChildItem -LiteralPath $Destino -Recurse -File -Force | Where-Object { $_.Name -in $noVan -or $_.FullName -like '*dlssg_sm86*' -or $_.FullName -like '*\games\*' -or $_.FullName -like '*\data\*' -or $_.FullName -like "$Destino\magpie\*" -or $_.Extension -in @('.log', '.pdb', '.obj', '.zip') })
if ($malos.Count -gt 0) { throw ("En el paquete hay lo que no debe: " + (($malos | ForEach-Object { $_.FullName }) -join ', ')) }
# Solo DLL firmadas por NVIDIA, salvo la propia del motor (las nuestras: los exe y PrismaNr.dll en engine\)
foreach ($f in Get-ChildItem -LiteralPath $Destino -Recurse -File | Where-Object { $_.Extension -eq '.dll' -and -not ($propias -contains $_.Name -and $_.DirectoryName -eq "$Destino\engine") }) {
    $s = Get-AuthenticodeSignature -LiteralPath $f.FullName
    if ($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'NVIDIA') { throw "DLL sin la firma valida de NVIDIA: $($f.FullName) ($($s.Status))" }
}
# Nada de este PC: el usuario de Windows, el nombre del equipo, la carpeta de usuario y scripts\privado.txt, en los exe propios y en los textos
$personal = @($env:USERNAME, $env:COMPUTERNAME, $env:USERPROFILE)
if (Test-Path "$Raiz\scripts\privado.txt") { $personal += @(Get-Content -LiteralPath "$Raiz\scripts\privado.txt" -Encoding UTF8 | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $_.Trim() }) }
$personal = @($personal | Where-Object { $_ -and $_.Length -ge 3 } | Select-Object -Unique)
$mirar = @("$Destino\ConsoleEnhancer.exe", "$Destino\engine\ConsoleEnhancerEngine.exe", "$Destino\engine\PrismaNr.dll", "$Destino\engine\CeFsrFg.dll") + @(Get-ChildItem -LiteralPath $Destino -Recurse -File | Where-Object { $_.Extension -in @('.txt', '.ini', '.lua', '.glsl', '.conf', '.config', '.md', '.json') -and $_.FullName -notlike "$lic\streamline\*" } | ForEach-Object { $_.FullName })
$avisos = 0
foreach ($e in $mirar) {
    $b = [IO.File]::ReadAllBytes($e)
    $u = [Text.Encoding]::Unicode.GetString($b); $a = [Text.Encoding]::UTF8.GetString($b)
    foreach ($p in $personal) {
        if ($u.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or $a.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Write-Output ("AVISO: un dato de este PC dentro de " + $e.Substring($Destino.Length + 1)); $avisos++ }
    }
}
$firmas = @{}
foreach ($f in Get-ChildItem -LiteralPath $Destino -Recurse -File | Where-Object { $_.Extension -in @('.dll', '.exe') }) {
    $s = Get-AuthenticodeSignature -LiteralPath $f.FullName
    $quien = if ($s.SignerCertificate) { ($s.SignerCertificate.Subject -split ',')[0] -replace '^CN=', '' } else { 'sin firma' }
    $k = "$($s.Status) / $quien"; if (-not $firmas.ContainsKey($k)) { $firmas[$k] = New-Object Collections.Generic.List[string] }; $firmas[$k].Add($f.Name)
}
Write-Output ("Paquete: $Destino - $script:n ficheros, {0:N0} MB, {1:N1} s" -f ($script:bytes / 1MB), $sw.Elapsed.TotalSeconds)
foreach ($d in Get-ChildItem -LiteralPath $Destino -Directory) { $s = (Get-ChildItem -LiteralPath $d.FullName -Recurse -File | Measure-Object Length -Sum).Sum; Write-Output ("  {0,-10} {1,8:N1} MB" -f $d.Name, ($s / 1MB)) }
foreach ($k in ($firmas.Keys | Sort-Object)) { Write-Output ("  firma {0}: {1}" -f $k, (($firmas[$k] | Sort-Object) -join ', ')) }
if ($avisos -gt 0) { throw "$avisos datos de este PC en el paquete (arriba): quitalos antes de publicarlo" }
Write-Output 'Comprobado: nada no redistribuible, ni mpv ni XeSS / FSR (estos los baja la app), solo DLL firmadas por NVIDIA (y las propias del motor) y ningun dato de este PC'
if ($Zip) {
    $z = "$(Split-Path $Destino)\ConsoleEnhancer$(if ($Version) { '-' + $Version }).zip"
    if (Test-Path $z) { Remove-Item -LiteralPath $z -Force }
    # (a mano y no con ZipFile.CreateFromDirectory: el .NET de PowerShell 5.1 escribe las rutas con '\', y el formato zip pide '/')
    $fs = [IO.File]::Open($z, [IO.FileMode]::CreateNew)
    try {
        $za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($f in Get-ChildItem -LiteralPath $Destino -Recurse -File | Sort-Object FullName) {
                $nombre = $f.FullName.Substring($Destino.Length + 1).Replace('\', '/')
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, $nombre, [IO.Compression.CompressionLevel]::Optimal)
            }
        } finally { $za.Dispose() }
    } finally { $fs.Dispose() }
    $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $z).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$z.sha256", "$h  $(Split-Path $z -Leaf)`n", (New-Object Text.UTF8Encoding($false)))
    Write-Output ("Zip: $z {0:N0} MB, sha256 $h" -f ((Get-Item $z).Length / 1MB))
    # (1.2.3, DEFENDER) el SHA-256 de cada fichero del paquete, al lado del zip (el CI lo sube a la Release): para comprobar una copia
    # o mandar un falso positivo a Microsoft / a un antivirus con los ficheros exactos
    $lista = "$(Split-Path $z)\$([IO.Path]::GetFileNameWithoutExtension($z)).files.sha256"
    $lineas = foreach ($f in Get-ChildItem -LiteralPath $Destino -Recurse -File | Sort-Object FullName) {
        (Get-FileHash -Algorithm SHA256 -LiteralPath $f.FullName).Hash.ToLowerInvariant() + '  ' + $f.FullName.Substring($Destino.Length + 1).Replace('\', '/')
    }
    [IO.File]::WriteAllText($lista, (($lineas -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
    Write-Output "SHA-256 de cada fichero: $lista"
}
