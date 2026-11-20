@echo off
rem Compila bin\motor\ConsoleEnhancerEngine.exe (el motor de imagen de ConsoleEnhancer: C# 5, .NET Framework 4.8, nada que instalar)
rem y copia al lado (si faltan) las DLL de produccion de Streamline (DLSS, generacion, Reflex; firmadas por NVIDIA) de
rem terceros\streamline (scripts\descargar_dependencias.ps1) o de la raiz del SDK que diga la variable STREAMLINE_DIR.
rem Sin ellas (o con streamline=0, o sin grafica NVIDIA) el motor va sin Streamline: FSR 1 + RCAS y swapchain con espera.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
if "%STREAMLINE_DIR%"=="" (set "SL=%~dp0terceros\streamline\bin\x64") else (set "SL=%STREAMLINE_DIR%\bin\x64")
if not exist "%CSC%" (echo *** No esta %CSC% ^(.NET Framework 4.8^) *** & exit /b 1)
if not exist bin\motor mkdir bin\motor
"%CSC%" /nologo /unsafe /platform:x64 /optimize+ /codepage:65001 /target:winexe /out:bin\motor\ConsoleEnhancerEngine.exe ^
  /win32icon:recursos\prisma.ico /resource:recursos\prisma.ico,Prisma.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /recurse:src\Motor\*.cs
if errorlevel 1 (echo *** ERROR de compilacion *** & exit /b 1)
for %%f in (sl.interposer.dll sl.common.dll sl.dlss.dll sl.dlss_g.dll sl.reflex.dll sl.pcl.dll nvngx_dlss.dll nvngx_dlssg.dll) do (
  if not exist "bin\motor\%%f" (
    if exist "%SL%\%%f" (copy /y "%SL%\%%f" bin\motor\ >nul) else (echo Aviso: no esta %SL%\%%f)
  )
)
echo Compilado: %~dp0bin\motor\ConsoleEnhancerEngine.exe
endlocal
