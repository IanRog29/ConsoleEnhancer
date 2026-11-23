# firmar.ps1 - firma de codigo (Authenticode) de los exe propios de ConsoleEnhancer con signtool, SOLO si hay certificado
# (1.2.3, DEFENDER). Sin certificado no hace falta nada: los exe van sin firmar, como hasta ahora.
# Firma bin\ConsoleEnhancer.exe, "bin\ConsoleEnhancer Settings.exe" y bin\engine\ConsoleEnhancerEngine.exe: despues de build.bat y
# build_motor.bat y antes de scripts\empaquetar.ps1 (que copia los exe de bin\ al paquete). Las DLL de NVIDIA ya vienen firmadas por
# NVIDIA y no se tocan.
#
# Uso: powershell -ExecutionPolicy Bypass -File scripts\firmar.ps1 (-Pfx <fichero.pfx> | -Huella <sha1>) [-Sello <url>] [-Raiz <carpeta>]
#   -Pfx     certificado y clave en un .pfx; la contrasena en la variable de entorno CE_FIRMA_CLAVE. Se mete un momento en el almacen del
#            usuario, se firma por su huella y se quita. Lo usa el CI de GitHub con los secretos CE_FIRMA_PFX (el .pfx en base64) y
#            CE_FIRMA_CLAVE (.github\workflows\compilar.yml)
#   -Huella  un certificado que ya esta en el almacen de Windows (Personal), por su huella SHA-1: una tarjeta o token USB, o la tarjeta
#            virtual de Certum SimplySign con SimplySign Desktop conectado. Para firmar en este PC
#   -Sello   servidor de sello de tiempo RFC 3161 (de serie http://timestamp.digicert.com; con Certum: http://time.certum.pl). Con sello,
#            la firma sigue valiendo cuando caduque el certificado
# Codigo de salida 0 = firmados y comprobados (signtool verify /pa).
param(
    [string]$Pfx = '',
    [string]$Huella = '',
    [string]$Sello = 'http://timestamp.digicert.com',
    [string]$Raiz = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
if (-not $Pfx -and -not $Huella) { throw 'Falta -Pfx <fichero.pfx> o -Huella <sha1 del certificado>' }
$exes = @("$Raiz\bin\ConsoleEnhancer.exe", "$Raiz\bin\ConsoleEnhancer Settings.exe", "$Raiz\bin\engine\ConsoleEnhancerEngine.exe")
# (1.2.4) y la pieza propia del motor si esta compilada (build_prismanr.bat)
foreach ($d in @("$Raiz\bin\engine\PrismaNr.dll")) { if (Test-Path $d) { $exes += $d } }
foreach ($e in $exes) { if (-not (Test-Path -LiteralPath $e)) { throw "Falta $e (compila antes con build.bat y build_motor.bat)" } }

# signtool del Windows SDK (los runners windows-latest de GitHub lo traen); el mas nuevo
$st = @(Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending) | Select-Object -First 1
if (-not $st) { $c = Get-Command signtool.exe -ErrorAction SilentlyContinue; if ($c) { $st = Get-Item $c.Source } }
if (-not $st) { throw 'No esta signtool.exe (Windows SDK: https://developer.microsoft.com/windows/downloads/windows-sdk/)' }
Write-Output "signtool: $($st.FullName)"

$importado = $null
try {
    if ($Pfx) {
        if (-not $env:CE_FIRMA_CLAVE) { throw 'Falta la contrasena del .pfx en la variable de entorno CE_FIRMA_CLAVE' }
        $clave = ConvertTo-SecureString $env:CE_FIRMA_CLAVE -AsPlainText -Force
        $importado = Import-PfxCertificate -FilePath $Pfx -CertStoreLocation Cert:\CurrentUser\My -Password $clave
        $Huella = @($importado | Where-Object { $_.HasPrivateKey })[0].Thumbprint
        if (-not $Huella) { throw "El .pfx no trae la clave privada" }
    }
    $Huella = $Huella -replace '\s', ''
    $cert = Get-Item "Cert:\CurrentUser\My\$Huella" -ErrorAction SilentlyContinue
    if (-not $cert) { throw "No hay ningun certificado con la huella $Huella en Cert:\CurrentUser\My" }
    Write-Output ("Certificado: {0} (caduca {1:yyyy-MM-dd})" -f $cert.Subject, $cert.NotAfter)
    & $st.FullName sign /fd SHA256 /td SHA256 /tr $Sello /sha1 $Huella /s My /d 'ConsoleEnhancer' /du 'https://github.com/IanRog29/ConsoleEnhancer' $exes
    if ($LASTEXITCODE -ne 0) { throw "signtool sign ha fallado ($LASTEXITCODE)" }
    & $st.FullName verify /pa $exes
    if ($LASTEXITCODE -ne 0) { throw "signtool verify ha fallado ($LASTEXITCODE)" }
    foreach ($e in $exes) { $s = Get-AuthenticodeSignature -LiteralPath $e; Write-Output ("  {0}: {1} ({2})" -f (Split-Path $e -Leaf), $s.Status, $s.SignerCertificate.Subject) }
}
finally {
    # (el del .pfx no se queda en el almacen)
    if ($importado) { foreach ($c in @($importado)) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($c.Thumbprint)" -Force -ErrorAction SilentlyContinue } }
}
