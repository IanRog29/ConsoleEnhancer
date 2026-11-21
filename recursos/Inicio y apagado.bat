@echo off
rem ConsoleEnhancer: cambiar el inicio con Windows y el apagado del PC con la consola sin abrir la app (scripts\inicio_y_apagado.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\inicio_y_apagado.ps1"
