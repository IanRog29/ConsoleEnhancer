@echo off
rem Compila bin\engine\CeFsrFg.dll: la FSR FRAME GENERATION de varios instantes (x2 / x3 / x4) del motor (src\CeFsrFg\CeFsrFg.cpp,
rem src\Motor\Engine\EngineFsrMulti.cs). Es la interpolacion de imagenes + el flujo optico de FSR 3.1 de AMD (GPUOpen FidelityFX SDK 1.1.4,
rem licencia MIT) con el cambio de parches\fidelityfx-fi-multi.patch (inventar en cualquier instante, no solo a mitad), compilada aqui.
rem Necesita Visual Studio 2022 (o sus Build Tools) con C++, git y, la primera vez, internet: baja solo lo necesario del SDK (etiqueta v1.1.4,
rem ~80 MB) a obj\fidelityfx-1.1.4 (o usa el de FFX_SDK_DIR, ya con el parche) y compila sus shaders con el FidelityFX_SC.exe del SDK.
rem Sin esta DLL el motor sigue igual (sin FSR FG x3 / x4: fsrfg_multi no hace nada y lo dice en el log).
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "VCVARS="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VCVARS=%%i\VC\Auxiliary\Build\vcvars64.bat"
if not exist "%VCVARS%" (echo *** No esta Visual Studio con C++ *** & exit /b 1)
if "%FFX_SDK_DIR%"=="" (set "FFX=%~dp0obj\fidelityfx-1.1.4") else (set "FFX=%FFX_SDK_DIR%")
set "SDK=%FFX%\sdk"
if not exist "%SDK%\src\components\frameinterpolation\ffx_frameinterpolation.cpp" (
  if not "%FFX_SDK_DIR%"=="" (echo *** No esta el SDK en %FFX_SDK_DIR% *** & exit /b 1)
  echo Bajando FidelityFX SDK v1.1.4 ^(solo lo necesario^) a %FFX% ...
  if not exist obj mkdir obj
  git clone --depth 1 --branch v1.1.4 --filter=blob:none --no-checkout https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK.git "%FFX%" || (echo *** git clone *** & exit /b 1)
  git -C "%FFX%" sparse-checkout init --no-cone || exit /b 1
  (
    echo /sdk/include/
    echo /sdk/src/components/frameinterpolation/
    echo /sdk/src/components/opticalflow/
    echo /sdk/src/backends/dx12/*.cpp
    echo /sdk/src/backends/dx12/*.h
    echo /sdk/src/backends/dx12/shaders/frameinterpolation/
    echo /sdk/src/backends/dx12/shaders/opticalflow/
    echo /sdk/src/backends/shared/
    echo /sdk/src/shared/
    echo /sdk/tools/binary_store/FidelityFX_SC.exe
    echo /sdk/tools/binary_store/dxcompiler.dll
    echo /sdk/tools/binary_store/dxil.dll
    echo /sdk/LICENSE.txt
    echo /LICENSE.txt
  ) > "%FFX%\.git\info\sparse-checkout"
  git -C "%FFX%" checkout v1.1.4 || (echo *** git checkout *** & exit /b 1)
  git -C "%FFX%" apply "%~dp0parches\fidelityfx-fi-multi.patch" || (echo *** el parche no entra *** & exit /b 1)
)
set "SC=%SDK%\tools\binary_store\FidelityFX_SC.exe"
set "GPU=%SDK%\include\FidelityFX\gpu"
set "SH=%~dp0obj\cefsrfg\shaders"
if not exist "%SH%" mkdir "%SH%"
set "API=-E CS -Wno-for-redefinition -Wno-ambig-lit-shift -DFFX_HLSL=1"
set "FIB=-DFFX_FRAMEINTERPOLATION_OPTION_UPSAMPLE_SAMPLERS_USE_DATA_HALF=0 -DFFX_FRAMEINTERPOLATION_OPTION_ACCUMULATE_SAMPLERS_USE_DATA_HALF=0 -DFFX_FRAMEINTERPOLATION_OPTION_REPROJECT_SAMPLERS_USE_DATA_HALF=1 -DFFX_FRAMEINTERPOLATION_OPTION_POSTPROCESSLOCKSTATUS_SAMPLERS_USE_DATA_HALF=0 -DFFX_FRAMEINTERPOLATION_OPTION_UPSAMPLE_USE_LANCZOS_TYPE=2 -reflection -deps=gcc -DFFX_GPU=1"
set "FIP=-DFFX_FRAMEINTERPOLATION_OPTION_LOW_RES_MOTION_VECTORS={0,1} -DFFX_FRAMEINTERPOLATION_OPTION_JITTER_MOTION_VECTORS={0,1} -DFFX_FRAMEINTERPOLATION_OPTION_INVERTED_DEPTH={0,1}"
set "OFB=-reflection -deps=gcc -DFFX_GPU=1"
set "OFP=-DFFX_OPTICALFLOW_OPTION_HDR_COLOR_INPUT={0,1}"
echo Shaders de la interpolacion y del flujo optico ...
for %%f in ("%SDK%\src\backends\dx12\shaders\frameinterpolation\*.hlsl") do (
  call :shader "%%~f" "%%~nf" "!FIB!" "!FIP!" "%GPU%\frameinterpolation" || exit /b 1
)
for %%n in (prepare_luma_pass compute_luminance_pyramid_pass generate_scd_histogram_pass compute_scd_divergence_pass compute_optical_flow_advanced_pass_v5 filter_optical_flow_pass_v5 scale_optical_flow_advanced_pass_v5) do (
  call :shader "%SDK%\src\backends\dx12\shaders\opticalflow\ffx_opticalflow_%%n.hlsl" "ffx_opticalflow_%%n" "!OFB!" "!OFP!" "%GPU%\opticalflow" || exit /b 1
)
call "%VCVARS%" >nul
if not exist bin\engine mkdir bin\engine
if not exist obj\cefsrfg mkdir obj\cefsrfg
cl /nologo /O2 /EHsc /std:c++17 /MT /LD /utf-8 /W3 /wd4005 /DNOMINMAX /DWIN32_LEAN_AND_MEAN /DFFX_FI /DFFX_OF /DCE_NO_PIX /D_CRT_SECURE_NO_WARNINGS ^
 /I"%SDK%\include" /I"%SDK%\src\components" /I"%SDK%\src\shared" /I"%SDK%\src\backends\shared" /I"%SDK%\src\backends\dx12" /I"%SH%" ^
 src\CeFsrFg\CeFsrFg.cpp ^
 "%SDK%\src\components\frameinterpolation\ffx_frameinterpolation.cpp" "%SDK%\src\components\opticalflow\ffx_opticalflow.cpp" ^
 "%SDK%\src\backends\dx12\ffx_dx12.cpp" "%SDK%\src\backends\shared\ffx_shader_blobs.cpp" ^
 "%SDK%\src\backends\shared\blob_accessors\ffx_frameinterpolation_shaderblobs.cpp" "%SDK%\src\backends\shared\blob_accessors\ffx_opticalflow_shaderblobs.cpp" ^
 "%SDK%\src\shared\ffx_assert.cpp" "%SDK%\src\shared\ffx_message.cpp" "%SDK%\src\shared\ffx_object_management.cpp" "%SDK%\src\shared\ffx_breadcrumbs_list.cpp" ^
 /Fe:bin\engine\CeFsrFg.dll /Fo:obj\cefsrfg\ /link /NOLOGO /IMPLIB:obj\cefsrfg\CeFsrFg.lib d3d12.lib dxgi.lib dxguid.lib kernel32.lib user32.lib
if errorlevel 1 (echo *** ERROR CeFsrFg.dll *** & exit /b 1)
echo Compilado: %~dp0bin\engine\CeFsrFg.dll
endlocal
exit /b 0

rem :shader <fichero.hlsl> <nombre> <base> <permutaciones> <include del efecto>: las 4 variantes (como CMakeCompileShaders.txt del SDK)
:shader
"%SC%" %~3 %API% %~4 -name=%~2 -DFFX_HALF=0 -DFFX_HLSL_SM=62 -T cs_6_2 -I "%GPU%" -I "%~5" -output="%SH%" "%~1" >nul || (echo *** shader %~2 *** & exit /b 1)
"%SC%" %~3 %API% %~4 -name=%~2_wave64 -DFFX_HALF=0 -DFFX_PREFER_WAVE64=[WaveSize(64)] -DFFX_HLSL_SM=66 -T cs_6_6 -I "%GPU%" -I "%~5" -output="%SH%" "%~1" >nul || (echo *** shader %~2 wave64 *** & exit /b 1)
"%SC%" %~3 %API% %~4 -name=%~2_16bit -DFFX_HALF=1 -enable-16bit-types -DFFX_HLSL_SM=62 -T cs_6_2 -I "%GPU%" -I "%~5" -output="%SH%" "%~1" >nul || (echo *** shader %~2 16bit *** & exit /b 1)
"%SC%" %~3 %API% %~4 -name=%~2_wave64_16bit -DFFX_HALF=1 -enable-16bit-types -DFFX_PREFER_WAVE64=[WaveSize(64)] -DFFX_HLSL_SM=66 -T cs_6_6 -I "%GPU%" -I "%~5" -output="%SH%" "%~1" >nul || (echo *** shader %~2 wave64_16bit *** & exit /b 1)
exit /b 0
