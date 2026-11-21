# inicio_y_apagado.ps1 - cambiar, sin abrir ConsoleEnhancer, lo de "Inicio con Windows y apagado" (21-11-2026, 1.0.13).
# Se abre con "Inicio y apagado.bat" (junto a ConsoleEnhancer.exe). Lo mismo que el ultimo paso del asistente o el menu de la bandeja:
#   iniciar_con_windows  (la tarea "ConsoleEnhancer - inicio": la pone o la quita ConsoleEnhancer.exe --iniciar-con-windows=1|0)
#   apagar_pc            apagar el PC al apagar la consola
#   apagar_pc_aviso_s    segundos del aviso antes de apagar (0 = sin aviso)
#   apagar_pc_modo       que hace el PC: espera (de serie) | suspender | apagar (1.0.14)
#   escritorio_negro     escritorio de Windows negro con la app abierta
# Escribe en datos\consoleenhancer.ini (con portable.txt) o en %LOCALAPPDATA%\ConsoleEnhancer\consoleenhancer.ini: cambia la ultima
# linea de cada clave (la que vale) o la pone al final, como la app. Con la app abierta, vale al volver a abrirla.
$ErrorActionPreference = 'Stop'
$app = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path "$app\ConsoleEnhancer.exe")) { $app = $PSScriptRoot }
$exe = Join-Path $app 'ConsoleEnhancer.exe'
$datos = if (Test-Path (Join-Path $app 'portable.txt')) { Join-Path $app 'datos' } else { Join-Path $env:LOCALAPPDATA 'ConsoleEnhancer' }
$ini = Join-Path $datos 'consoleenhancer.ini'
$utf8 = New-Object Text.UTF8Encoding($false)

function Leer([string]$k) {
    if (-not (Test-Path $ini)) { return $null }
    $v = $null
    foreach ($l in [IO.File]::ReadAllLines($ini)) {
        $t = $l.Trim()
        if ($t.Length -eq 0 -or $t[0] -eq '#' -or $t[0] -eq ';') { continue }
        $i = $t.IndexOf('=')
        if ($i -gt 0 -and $t.Substring(0, $i).Trim() -ieq $k) { $v = $t.Substring($i + 1).Trim() }
    }
    return $v
}

function Poner([string]$k, [string]$v) {
    New-Item -ItemType Directory -Force $datos | Out-Null
    $ls = New-Object Collections.Generic.List[string]
    if (Test-Path $ini) { $ls.AddRange([string[]][IO.File]::ReadAllLines($ini)) }
    $ultima = -1
    for ($i = 0; $i -lt $ls.Count; $i++) {
        $t = $ls[$i].Trim()
        if ($t.Length -eq 0 -or $t[0] -eq '#' -or $t[0] -eq ';') { continue }
        $j = $t.IndexOf('=')
        if ($j -gt 0 -and $t.Substring(0, $j).Trim() -ieq $k) { $ultima = $i }
    }
    if ($ultima -ge 0) { $ls[$ultima] = "$k=$v" } else { $ls.Add("$k=$v") }
    [IO.File]::WriteAllLines($ini, $ls.ToArray(), $utf8)
}

function SiNo($v) { if ($v -eq '1') { 'SI' } else { 'no' } }

function Abierta {
    try { $m = [Threading.Mutex]::OpenExisting('Local\ConsoleEnhancer_SingleInstance'); $m.Dispose(); return $true } catch { return $false }
}

function Tarea {
    $t = schtasks /Query /TN 'ConsoleEnhancer - inicio' 2>$null
    if ($LASTEXITCODE -eq 0) { return $true }
    $r = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ConsoleEnhancer' -ErrorAction SilentlyContinue
    return $null -ne $r
}

while ($true) {
    Clear-Host
    $inicio = if (Tarea) { '1' } else { '0' }
    $apagar = Leer 'apagar_pc'
    $aviso = Leer 'apagar_pc_aviso_s'; if (-not $aviso) { $aviso = '15' }
    $negro = Leer 'escritorio_negro'
    $modo = Leer 'apagar_pc_modo'; if (-not $modo) { $modo = 'espera' }
    Write-Host ''
    Write-Host '  ConsoleEnhancer - inicio con Windows y apagado' -ForegroundColor Cyan
    Write-Host "  Ajustes: $ini" -ForegroundColor DarkGray
    if (Abierta) { Write-Host '  La app esta abierta: lo que cambies aqui vale al volver a abrirla (el inicio con Windows, ya).' -ForegroundColor Yellow }
    Write-Host ''
    Write-Host ("  1. Abrir ConsoleEnhancer al iniciar Windows ........ " + (SiNo $inicio))
    Write-Host ("  2. Apagar el PC al apagar la consola ............... " + (SiNo $apagar))
    Write-Host ("  3. Aviso antes de apagar el PC ..................... " + $(if ($aviso -eq '0') { 'sin aviso' } else { "$aviso s" }))
    Write-Host ("  4. Escritorio negro con la app abierta ............. " + (SiNo $negro))
    Write-Host  '  5. Probar el aviso de apagado (no apaga nada)'
    Write-Host ("  6. Al apagar la consola, el PC queda ............... " + $(switch ($modo) { 'suspender' { 'suspendido (solo un teclado lo enciende)' } 'apagar' { 'apagado' } default { 'en espera (la consola, un mando o una tecla lo encienden)' } }))
    Write-Host  '  7. Probar la espera (apaga la pantalla; enciende la consola o un mando, o pulsa una tecla)'
    Write-Host  '  0. Salir'
    Write-Host ''
    $o = Read-Host '  Elige'
    switch ($o) {
        '1' {
            $on = if ($inicio -eq '1') { '0' } else { '1' }
            $p = Start-Process -FilePath $exe -ArgumentList "--iniciar-con-windows=$on" -Wait -PassThru
            if ($p.ExitCode -eq 0) { Poner 'iniciar_con_windows' $on }
            else { Write-Host '  No se pudo cambiar (mira datos\logs\consoleenhancer.log)' -ForegroundColor Red; Start-Sleep 3 }
        }
        '2' { Poner 'apagar_pc' $(if ($apagar -eq '1') { '0' } else { '1' }) }
        '3' {
            $s = Read-Host '  Segundos del aviso (0 = sin aviso, hasta 120)'
            $n = 0
            if ([int]::TryParse($s, [ref]$n) -and $n -ge 0 -and $n -le 120) { Poner 'apagar_pc_aviso_s' "$n" }
            else { Write-Host '  Numero no valido' -ForegroundColor Red; Start-Sleep 2 }
        }
        '4' { Poner 'escritorio_negro' $(if ($negro -eq '1') { '0' } else { '1' }) }
        '5' {
            $s = if ($aviso -eq '0') { '10' } else { $aviso }
            Write-Host "  Aviso de prueba de $s s: no toques nada para ver que llega a 0, o pulsa algo para cancelarlo." -ForegroundColor DarkGray
            $p = Start-Process -FilePath $exe -ArgumentList "--prueba-apagado=$s" -Wait -PassThru
            Write-Host $(if ($p.ExitCode -eq 0) { '  Llego a 0 (de verdad se habria apagado el PC).' } else { '  Cancelado.' })
            Read-Host '  Enter para seguir' | Out-Null
        }
        '6' { Poner 'apagar_pc_modo' $(switch ($modo) { 'espera' { 'suspender' } 'suspender' { 'apagar' } default { 'espera' } }) }
        '7' { Start-Process -FilePath $exe -ArgumentList '--prueba-espera' | Out-Null; exit 0 }
        '0' { exit 0 }
        '' { exit 0 }
    }
}
