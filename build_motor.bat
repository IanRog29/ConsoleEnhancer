@echo off
rem Compila bin\engine\ConsoleEnhancerEngine.exe (el motor de imagen de ConsoleEnhancer: C# 5, .NET Framework 4.8, nada que instalar)
rem y copia al lado (si faltan) las DLL de produccion de Streamline (DLSS, generacion, Reflex; firmadas por NVIDIA) de
rem terceros\streamline (scripts\descargar_dependencias.ps1) o de la raiz del SDK que diga la variable STREAMLINE_DIR.
rem Sin ellas (o con streamline=0, o sin grafica NVIDIA) el motor va sin Streamline: FSR 1 + RCAS y swapchain con espera.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
if "%STREAMLINE_DIR%"=="" (set "SL=%~dp0terceros\streamline\bin\x64") else (set "SL=%STREAMLINE_DIR%\bin\x64")
if not exist "%CSC%" (echo *** No esta %CSC% ^(.NET Framework 4.8^) *** & exit /b 1)
if not exist bin\engine mkdir bin\engine
rem (1.2.3) src\Version.cs: la version y el editor en las propiedades del exe; resources\engine.manifest: su manifiesto (asInvoker,
rem Windows 10/11, PerMonitorV2 como SetProcessDpiAwarenessContext al empezar Main)
"%CSC%" /nologo /unsafe /platform:x64 /optimize+ /codepage:65001 /target:winexe /out:bin\engine\ConsoleEnhancerEngine.exe ^
  /win32icon:resources\prisma.ico /win32manifest:resources\engine.manifest /resource:resources\prisma.ico,Prisma.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /recurse:src\Motor\*.cs src\Version.cs
if errorlevel 1 (echo *** ERROR de compilacion *** & exit /b 1)
for %%f in (sl.interposer.dll sl.common.dll sl.dlss.dll sl.dlss_g.dll sl.reflex.dll sl.pcl.dll nvngx_dlss.dll nvngx_dlssg.dll sl.deepdvc.dll nvngx_deepdvc.dll) do (
  if not exist "bin\engine\%%f" (
    if exist "%SL%\%%f" (copy /y "%SL%\%%f" bin\engine\ >nul) else (echo Aviso: no esta %SL%\%%f)
  )
)
echo Compilado: %~dp0bin\engine\ConsoleEnhancerEngine.exe
endlocal
