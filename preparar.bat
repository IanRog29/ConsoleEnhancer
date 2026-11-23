@echo off
rem preparar.bat - todo en un clic para quien baja el codigo fuente de ConsoleEnhancer (git clone o el zip de "Code"):
rem   1. baja mpv, Intel XeSS, AMD FSR y NVIDIA Streamline a terceros\ (versiones fijas de sus releases oficiales, comprobando el SHA-256:
rem      scripts\components.json y scripts\descargar_dependencias.ps1; unos 950 MB la primera vez, luego ya no)
rem   2. compila el lanzador (build.bat) y el motor (build_motor.bat) con el csc de .NET Framework 4.8 que trae Windows
rem   3. pasa la prueba de deteccion con los PCs simulados (tests\build_deteccion.bat)
rem   4. con "paquete": monta tambien el zip de la release (scripts\empaquetar.ps1 -Zip) en dist\
rem Al final, bin\ConsoleEnhancer.exe esta listo para abrir. Uso: preparar.bat [paquete] [sindescargar]
rem Everything in one click for people who download the source: downloads the pinned dependencies (SHA-256 checked), builds the
rem launcher and the engine, runs the tests and leaves bin\ConsoleEnhancer.exe ready. "preparar.bat paquete" also builds the zip.
setlocal
set "SELF=%~nx0"
cd /d "%~dp0"
set PAQUETE=
set DESCARGAR=1
for %%a in (%*) do (
  if /i "%%~a"=="paquete" set PAQUETE=1
  if /i "%%~a"=="package" set PAQUETE=1
  if /i "%%~a"=="sindescargar" set DESCARGAR=
)
set PS=powershell -NoProfile -ExecutionPolicy Bypass -File
echo.
echo ==== ConsoleEnhancer: preparar ====
if not exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
  echo *** Falta .NET Framework 4.8 ^(viene con Windows 10/11^): https://dotnet.microsoft.com/download/dotnet-framework/net48
  goto :mal
)
if defined DESCARGAR (
  echo.
  echo [1/4] Dependencias ^(terceros\^)...
  %PS% "%~dp0scripts\descargar_dependencias.ps1"
  if errorlevel 1 (echo *** No se pudieron bajar las dependencias: mira la conexion y vuelve a probar & goto :mal)
) else (echo [1/4] Dependencias: sin descargar ^(sindescargar^))
echo.
echo [2/4] Lanzador ^(build.bat^)...
call "%~dp0build.bat"
if errorlevel 1 goto :mal
echo.
echo [3/4] Motor ^(build_motor.bat^)...
call "%~dp0build_motor.bat"
if errorlevel 1 goto :mal
rem (1.2.4) la pieza propia del motor en C++ (necesita Visual Studio 2022 o sus Build Tools con C++): sin ella la app va igual
rem (sin DLSS 5 en el motor); el paquete si la necesita. (1.2.4 ronda 6: PrismaFg -DLSS FG x3 / x4- ya no existe)
echo.
echo [3b] Pieza del motor ^(PrismaNr^)...
if defined DESCARGAR %PS% "%~dp0scripts\descargar_cabeceras_ngx.ps1"
call "%~dp0build_prismanr.bat"
if errorlevel 1 (echo Aviso: sin PrismaNr.dll ^(hace falta Visual Studio con C++^) & if defined PAQUETE goto :mal)
echo.
echo [4/4] Pruebas ^(tests\build_deteccion.bat^)...
call "%~dp0tests\build_deteccion.bat"
if errorlevel 1 goto :mal
"%~dp0tests\bin\DeteccionTest.exe" --simula=todo --sin-d3d12 --salida=tests\bin\deteccion.txt
if errorlevel 1 (echo *** Falla la prueba de deteccion: tests\bin\deteccion.txt & goto :mal)
echo Prueba de deteccion: bien ^(tests\bin\deteccion.txt^)
if defined PAQUETE (
  echo.
  echo [+] Paquete ^(scripts\empaquetar.ps1 -Zip^)...
  %PS% "%~dp0scripts\empaquetar.ps1" -Zip -Forzar
  if errorlevel 1 goto :mal
)
echo.
echo ==== Listo: %~dp0bin\ConsoleEnhancer.exe ====
if defined PAQUETE echo Paquete: %~dp0dist\
call :pausa
endlocal
exit /b 0

:mal
echo.
echo ==== preparar.bat ha fallado (arriba esta el motivo) ====
call :pausa
endlocal
exit /b 1

:pausa
rem (solo con doble clic: si se abrio desde una consola o un script, no se para)
if defined CI exit /b 0
if defined PREPARAR_SIN_PAUSA exit /b 0
echo %cmdcmdline% | find /i "%SELF%" >nul && pause
exit /b 0
