@echo off
rem Compila bin\ConsoleEnhancer.exe (el lanzador, C# 5 con el csc de .NET Framework 4.8 que trae Windows: nada que instalar).
rem src\Lanzador\*.cs (la app de bandeja) + src\Deteccion\*.cs (deteccion y plan). El motor: build_motor.bat
rem Despues deja bin\ lista para probar: resources\base.ini, languages\, scripts\ (lua y glsl de mpv) y, si ya se descargaron con
rem scripts\descargar_dependencias.ps1, uniones de carpeta (sin admin) bin\mpv -> terceros\mpv, bin\xess -> terceros\xess y bin\fsr -> terceros\fsr
rem (1.2.4 ronda 6: las DLL de Intel XeSS y AMD FSR para el motor; sin Magpie).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe
cd /d "%~dp0"
if not exist "%CSC%" (echo *** No esta %CSC% ^(.NET Framework 4.8^) *** & exit /b 1)
if not exist bin mkdir bin
rem (1.2.3) src\Version.cs: la version y el editor en las propiedades de los exe; resources\app.manifest: su manifiesto (asInvoker,
rem Windows 10/11, DPI del sistema como SetProcessDPIAware). La version se cambia al publicar en src\Version.cs
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /out:bin\ConsoleEnhancer.exe ^
  /win32icon:resources\prisma.ico /win32manifest:resources\app.manifest /resource:resources\prisma.ico,Prisma.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Management.dll ^
  /r:"%FW%\System.Web.Extensions.dll" /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\System.IO.Compression.FileSystem.dll" ^
  /r:"%FW%\WPF\UIAutomationClient.dll" /r:"%FW%\WPF\UIAutomationTypes.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  src\Lanzador\*.cs src\Deteccion\*.cs src\Version.cs
if errorlevel 1 (echo *** ERROR de compilacion *** & exit /b 1)
rem (1.2.0) "ConsoleEnhancer Settings.exe": abre el panel de ajustes (ConsoleEnhancer.exe --ajustes, src\Lanzador\Panel.cs)
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /out:"bin\ConsoleEnhancer Settings.exe" /win32icon:resources\prisma.ico ^
  /win32manifest:resources\app.manifest /r:System.dll /r:System.Windows.Forms.dll src\Ajustes\Ajustes.cs src\Version.cs
if errorlevel 1 (echo *** ERROR de compilacion de Ajustes *** & exit /b 1)
rem ---- lo que la app busca junto al exe ----
if not exist bin\resources mkdir bin\resources
if not exist bin\languages mkdir bin\languages
if not exist bin\scripts mkdir bin\scripts
copy /y resources\base.ini bin\resources\ >nul
rem (20-11, CIERRES) un fallo en un hilo secundario no cierra la app (legacyUnhandledExceptionPolicy): junto al exe
copy /y resources\ConsoleEnhancer.exe.config bin\ >nul
copy /y languages\*.txt bin\languages\ >nul
copy /y scripts\components.json bin\scripts\ >nul
if not exist bin\mpv if exist terceros\mpv\mpv.exe mklink /J bin\mpv terceros\mpv >nul
if not exist bin\xess if exist terceros\xess\libxess_fg.dll mklink /J bin\xess terceros\xess >nul
if not exist bin\fsr if exist terceros\fsr\amd_fidelityfx_loader_dx12.dll mklink /J bin\fsr terceros\fsr >nul
if not exist bin\mpv\mpv.exe echo Aviso: sin mpv en bin\ (powershell -ExecutionPolicy Bypass -File scripts\descargar_dependencias.ps1)
echo Compilado: %~dp0bin\ConsoleEnhancer.exe
endlocal
