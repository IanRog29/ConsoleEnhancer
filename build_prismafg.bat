@echo off
rem Compila bin\engine\PrismaFg.dll = DLSS FG x3 / x4 en el motor en RTX 40 con el paquete comunitario dlssg_sm86 que pone el usuario
rem (la app no lo reparte ni lo baja). Necesita Visual Studio 2022 (o sus Build Tools) con C++. Sin esta DLL el motor
rem sigue igual (DLSS FG x2 en RTX 40). Ver src\PrismaFg\PrismaFg.cpp.
setlocal
cd /d "%~dp0"
set VCVARS=
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`call "%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VCVARS=%%i\VC\Auxiliary\Build\vcvars64.bat"
if not exist "%VCVARS%" (echo *** No esta Visual Studio con C++ *** & exit /b 1)
call "%VCVARS%" >nul
if not exist bin\engine mkdir bin\engine
if not exist obj\prismafg mkdir obj\prismafg
cl /nologo /O2 /EHsc /std:c++17 /MT /LD /utf-8 /W3 src\PrismaFg\PrismaFg.cpp /Fe:bin\engine\PrismaFg.dll /Fo:obj\prismafg\ /link /NOLOGO /IMPLIB:obj\prismafg\PrismaFg.lib kernel32.lib bcrypt.lib
if errorlevel 1 (echo *** ERROR PrismaFg.dll *** & exit /b 1)
echo Compilado: %~dp0bin\engine\PrismaFg.dll
endlocal
