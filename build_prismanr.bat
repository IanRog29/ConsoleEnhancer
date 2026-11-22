@echo off
rem Compila bin\engine\PrismaNr.dll ("DLSS 5" en el motor: carga la nvngx_dlssnr.dll que pone el usuario, que NO se distribuye) y
rem bin\engine\PrismaNrTest.exe (prueba sin el motor). Necesita Visual Studio 2022 (o sus Build Tools) con C++ y las cabeceras de NGX
rem del SDK de DLSS de NVIDIA (github.com/NVIDIA/DLSS, carpeta include) en terceros\dlss_sdk\include (scripts\descargar_cabeceras_ngx.ps1)
rem o en %DLSS_SDK_DIR%\include. Las cabeceras solo se usan para compilar: no van en el repositorio ni en el zip.
rem Sin esta DLL el motor sigue igual (sin DLSS 5). Ver src\PrismaNr\PrismaNr.cpp.
setlocal
cd /d "%~dp0"
if "%DLSS_SDK_DIR%"=="" (set "NGX=%~dp0terceros\dlss_sdk\include") else (set "NGX=%DLSS_SDK_DIR%\include")
if not exist "%NGX%\nvsdk_ngx_params.h" (echo *** No estan las cabeceras de NGX en %NGX% *** & exit /b 1)
set VCVARS=
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`call "%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VCVARS=%%i\VC\Auxiliary\Build\vcvars64.bat"
if not exist "%VCVARS%" (echo *** No esta Visual Studio con C++ *** & exit /b 1)
call "%VCVARS%" >nul
if not exist bin\engine mkdir bin\engine
if not exist obj\prismanr mkdir obj\prismanr
cl /nologo /O2 /EHsc /std:c++17 /MT /LD /utf-8 /I"%NGX%" src\PrismaNr\PrismaNr.cpp /Fe:bin\engine\PrismaNr.dll /Fo:obj\prismanr\ /link /NOLOGO /IMPLIB:obj\prismanr\PrismaNr.lib kernel32.lib
if errorlevel 1 (echo *** ERROR PrismaNr.dll *** & exit /b 1)
cl /nologo /O2 /EHsc /std:c++17 /MT /utf-8 src\PrismaNr\PrismaNrTest.cpp /Fe:bin\engine\PrismaNrTest.exe /Fo:obj\prismanr\ /link /NOLOGO
if errorlevel 1 (echo *** ERROR PrismaNrTest.exe *** & exit /b 1)
echo Compilado: %~dp0bin\engine\PrismaNr.dll
endlocal
