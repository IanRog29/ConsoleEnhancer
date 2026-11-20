@echo off
rem Compila tests\bin\DeteccionTest.exe: la prueba del modulo de deteccion (src\Deteccion\*.cs) de ConsoleEnhancer.
rem Solo el compilador de C# de .NET Framework 4.8 (C# 5); nada que instalar.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0.."
if not exist tests\bin mkdir tests\bin
"%CSC%" /nologo /platform:x64 /optimize+ /codepage:65001 /target:exe /out:tests\bin\DeteccionTest.exe ^
  /r:System.dll /r:System.Core.dll ^
  tests\DeteccionTest.cs src\Deteccion\*.cs
if errorlevel 1 (echo *** ERROR de compilacion *** & exit /b 1)
echo Compilado: %CD%\tests\bin\DeteccionTest.exe
endlocal
