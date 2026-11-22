@echo off
rem tests\ngx_oficial\compilar.bat - (1.2.4, DLSS5LEGAL) la prueba de la via OFICIAL de NGX para DLSS 5 (Feature 18): ngx_oficial.exe.
rem Necesita Visual Studio 2022 (o sus Build Tools) con C++ y el SDK de DLSS de NVIDIA (github.com/NVIDIA/DLSS: include\ y
rem lib\Windows_x86_64\x64\nvsdk_ngx_s.lib) en %DLSS_SDK_DIR%. Ni el SDK ni el exe van a git ni al zip.
rem Uso: ngx_oficial.exe <carpeta con nvngx_dlssnr.dll> <carpeta de datos>  -> dice si el NGX del driver crea DLSS 5 por su API publica.
setlocal
cd /d "%~dp0"
if "%DLSS_SDK_DIR%"=="" (echo *** Pon DLSS_SDK_DIR = la carpeta del SDK de DLSS de NVIDIA *** & exit /b 1)
if not exist "%DLSS_SDK_DIR%\lib\Windows_x86_64\x64\nvsdk_ngx_s.lib" (echo *** No esta %DLSS_SDK_DIR%\lib\Windows_x86_64\x64\nvsdk_ngx_s.lib *** & exit /b 1)
set VCVARS=
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VCVARS=%%i\VC\Auxiliary\Build\vcvars64.bat"
if not exist "%VCVARS%" (echo *** No esta Visual Studio con C++ *** & exit /b 1)
call "%VCVARS%" >nul
if not exist ..\..\obj\ngx_oficial mkdir ..\..\obj\ngx_oficial
if not exist ..\..\bin\tests mkdir ..\..\bin\tests
cl /nologo /O2 /EHsc /std:c++17 /MT /utf-8 /W3 /I"%DLSS_SDK_DIR%\include" ngx_oficial.cpp /Fe:..\..\bin\tests\ngx_oficial.exe /Fo:..\..\obj\ngx_oficial\ /link /NOLOGO "%DLSS_SDK_DIR%\lib\Windows_x86_64\x64\nvsdk_ngx_s.lib" d3d12.lib dxgi.lib advapi32.lib user32.lib version.lib shell32.lib ole32.lib
if errorlevel 1 (echo *** ERROR ngx_oficial.exe *** & exit /b 1)
echo Compilado: %~dp0..\..\bin\tests\ngx_oficial.exe
endlocal