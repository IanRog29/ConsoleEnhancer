# Componentes de terceros / Third-party components

ConsoleEnhancer se distribuye bajo la **GPL-3.0** (`LICENSE`) con un **permiso adicional de la sección 7** para poder usarlo y
repartirlo con las librerías oficiales de NVIDIA, AMD, Intel y Microsoft (`LICENSE-EXCEPTION.txt`). Este fichero recoge todo lo que
**no** es código propio: lo que va en el zip de la release, lo que la app baja la primera vez y el código de terceros que hay dentro
del repositorio. En el zip, las licencias completas van en `licenses\`.

> **English summary.** The repository contains ConsoleEnhancer's own source (GPL-3.0 + a section 7 linking permission), a few
> permissively licensed shader ports (AMD FidelityFX FSR 1 / CAS, MIT; NVIDIA FXAA 3.11, BSD-3-Clause) and one shader adapted from
> Magpie (GPL-3.0). No third-party binaries are in the repository. The release zip contains ConsoleEnhancer's own executables, the
> unmodified, NVIDIA-signed Streamline / DLSS Super Resolution / Reflex DLLs, which NVIDIA allows applications to redistribute, and
> `engine\CeFsrFg.dll` (FSR 3 FG ×3 / ×4), built by ConsoleEnhancer from AMD's MIT-licensed FidelityFX SDK 1.1.4 frame interpolation with
> its own patch (`parches\fidelityfx-fi-multi.patch`; AMD's notice in `licenses\`). Enhanced FG is ConsoleEnhancer's own code.
> **The Intel XeSS SDK and the AMD FSR SDK are not redistributed**: on first start the app downloads them from their official
> GitHub releases (pinned versions, SHA-256 checked). Since 1.3 everything runs in ConsoleEnhancer's own engine: the app no longer
> uses or downloads Magpie or mpv (picture, detection grabs and console sound are the engine's), and NVIDIA DLSS Frame Generation
> (and the community `dlssg_sm86` package) is gone. The community-modified
> `nvngx_dlssnr.dll` ("DLSS 5") is never distributed, downloaded or recommended; the app only uses it if you add it yourself. The
> repository and the zip carry the engine's own native piece `PrismaNr` (own code, GPL-3.0), which loads that user-added file without
> going through NVIDIA's check (NGX); NVIDIA's terms do not allow that, and the user decided to publish it knowing it. PrismaNr is
> compiled with NVIDIA's two public NGX headers, which are only used to build it and are not in the repository.

## 1. Lo que va en el zip de la release

| Componente | Versión y origen | Licencia | ¿Se puede repartir? |
|---|---|---|---|
| **ConsoleEnhancer** (`ConsoleEnhancer.exe`, `engine\ConsoleEnhancerEngine.exe`, `scripts\`, `resources\`, `languages\`) | este repositorio | GPL-3.0 + `LICENSE-EXCEPTION.txt` | Sí. El código fuente de cada versión está en GitHub, junto a su release |
| **Pieza propia del motor** (`engine\PrismaNr.dll`, desde la 1.3) | este repositorio (`src\PrismaNr`, C++); se compila con las cabeceras públicas de NGX del SDK de DLSS v310.9.16 (`scripts\descargar_cabeceras_ngx.ps1`, no van a git) | GPL-3.0 + `LICENSE-EXCEPTION.txt` (código propio) | Es código propio. Pero carga la `nvngx_dlssnr.dll` que pone el usuario sin pasar por la comprobación de NGX de NVIDIA, cosa que los términos de NVIDIA no permiten (ver abajo); el usuario decidió publicarla sabiéndolo |
| **FSR FG de varios instantes** (`engine\CeFsrFg.dll`, desde la 1.3) | Compilada por ConsoleEnhancer (`build_fsrfg.bat`) desde el código de AMD FidelityFX SDK v1.1.4 (https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK, etiqueta `v1.1.4`, commit `c6efa6bf7f2027b3ec94f28578bb5965eabb9e55`, que `build_fsrfg.bat` comprueba: interpolación de imágenes y flujo óptico de FSR 3.1, backend DX12) **modificado** con `parches\fidelityfx-fi-multi.patch` (inventar en cualquier instante, para x3 / x4) + `src\CeFsrFg\CeFsrFg.cpp` | MIT, Copyright (C) 2024 Advanced Micro Devices, Inc. (texto abajo y en `licenses\`) | Sí (MIT). No es la DLL firmada de AMD (esa, `amd_fidelityfx_framegeneration_dx12.dll`, sigue haciendo la FSR 3 FG x2) |
| **NVIDIA Streamline** (`engine\sl.interposer.dll`, `sl.common.dll`, `sl.dlss.dll`, `sl.reflex.dll`, `sl.pcl.dll`, `sl.deepdvc.dll`) | v2.14.16, DLL de producción firmadas por NVIDIA, de `streamline-sdk-v2.14.16.zip` (https://github.com/NVIDIA-RTX/Streamline/releases) | MIT (`licenses\streamline\license.txt`, `3rd-party-licenses.md`) | Sí (MIT) |
| **NVIDIA DLSS Super Resolution, RTX Dynamic Vibrance** (`engine\nvngx_dlss.dll`, `engine\nvngx_deepdvc.dll`) | Las del mismo SDK de Streamline 2.14.16, sin cambios | NVIDIA RTX SDKs License (`licenses\streamline\nvngx_dlss.license.txt`) | Sí, en código objeto y como parte de una aplicación con funciones propias (punto 1.c y 2 de su licencia), con condiciones al menos igual de protectoras para NVIDIA: `licenses\NVIDIA-NOTICE.txt` |
| **NVIDIA Reflex** (dentro de `sl.reflex.dll`, `sl.pcl.dll`) | Streamline 2.14.16 | Licencia de los SDK de NVIDIA (`licenses\streamline\reflex.license.txt`) | Sí, como parte de la aplicación (1.2 de su licencia) |

Lo que pide NVIDIA y cómo se cumple:

- *Distribuir con condiciones al menos igual de protectoras* (RTX SDKs 2.c, Reflex 1.2.v): `licenses\NVIDIA-NOTICE.txt` (es / en),
  sus licencias completas en `licenses\streamline\` y el permiso de `LICENSE-EXCEPTION.txt`, que deja claro que esas DLL no pasan a
  ser GPL (RTX SDKs 4.e: el SDK no puede quedar sujeto a una licencia de código abierto).
- *Nombrar DLSS en el "acerca de"* (suplemento, 7.1.b): menú de la bandeja > **Acerca de y licencias**, este fichero y el README. Solo
  texto: ningún logotipo de NVIDIA.
- *No decir que NVIDIA patrocina o respalda la app* (4.c): dicho en el README y en el aviso.
- *DLSS solo con gráficas NVIDIA* (suplemento, 1): el plan solo usa DLSS en GeForce RTX; en las demás va FSR 1.
- *Aviso a NVIDIA antes de un lanzamiento comercial* (suplemento, 4): ConsoleEnhancer es gratuito y no comercial; quien lo venda o lo
  meta en un producto comercial debe avisar a NVIDIA (https://developer.nvidia.com/sw-notification).
- *No eludir mecanismos de autenticación* (4.d): el motor usa Streamline tal cual, con su identificador de aplicación de serie (0), y
  solo pide lo que el runtime de NVIDIA ofrece. **Excepción, desde la 1.3**: **PrismaNr** (carga la `nvngx_dlssnr.dll` del usuario
  para "DLSS 5" en el motor) se salta la comprobación de NGX: eso no cumple este punto. Va en el repositorio y en el zip por decisión del
  usuario, que asume el riesgo. Sin el archivo del usuario no hace nada, y la app no reparte ni baja ese archivo.
- *Intel XeSS / XeSS FG / XeLL y AMD FSR en el motor* (1.3): `libxess.dll`, `libxess_fg.dll`, `libxell.dll` del SDK oficial de Intel
  y las `amd_fidelityfx_*_dx12.dll` firmadas del SDK oficial de AMD, sin cambios y por su API pública (ver el punto 2); fuera de las
  gráficas Intel XeSS FG va en x2, como dice el runtime de Intel (su licencia no permite modificar la DLL ni su funcionamiento, tampoco
  en memoria). Los x3 / x4 que ofrece la app son legales: FSR 3 FG x3 / x4 con el código MIT de AMD (que sí permite cambiarlo),
  Smooth Motion x4 como XeSS FG x2 del motor + el Smooth Motion del driver tal cual, y Enhanced FG, que es código propio.

## 2. Lo que la app baja la primera vez (no va en el zip)

ConsoleEnhancer **no reparte** las DLL de Intel XeSS y AMD FSR. La primera vez que se abre, con permiso del usuario, las baja de
sus releases oficiales de GitHub a la carpeta de la app y comprueba su SHA-256 (`scripts\components.json`, `src\Lanzador\Componentes.cs`).
De los SDK de Intel y AMD solo se sacan sus DLL de ejecución (y, de Intel, su licencia). Desde la 1.3 la app ya no usa ni baja mpv:
la imagen, las capturas para la detección, los telones, la sonda de la espera y el sonido de la consola los hace el motor propio.

| Componente | Versión y descarga | Licencia | Código fuente / web |
|---|---|---|---|
| **Intel XeSS SDK** (`xess\libxess.dll`, `libxess_fg.dll`, `libxell.dll`: XeSS de escalador, XeSS Frame Generation y XeLL del motor) | v3.0.2, `XeSS_SDK_3.0.2.zip` de https://github.com/intel/xess/releases | Intel Simplified Software License (`xess\LICENSE.txt`, `xess\third-party-programs.txt`) | https://github.com/intel/xess/releases/tag/v3.0.2 |
| **AMD FSR SDK** (`fsr\amd_fidelityfx_loader_dx12.dll`, `_upscaler_dx12.dll`, `_framegeneration_dx12.dll`: FSR 3.1 / 4 de escalador y FSR Frame Generation del motor; firmadas por AMD) | v2.3.0, `FidelityFX-Samples-v2.3.0-prebuilt.zip` de https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/releases | MIT (AMD FidelityFX SDK) | https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/releases/tag/v2.3.0 |

Son las mismas versiones de esas DLL que traía Magpie hasta la 1.2.3 (desde la 1.3 la app no usa Magpie; si en la carpeta de la app
queda un `magpie\` de antes, la app copia de ahí esas DLL de Intel y AMD a `xess\` / `fsr\` y no baja nada). Sin internet, valen los
mismos zip puestos a mano en `<app>\downloads\` (se comprueba igual su SHA-256).

Para desarrollar, `scripts\descargar_dependencias.ps1` baja lo mismo (y Streamline) a `terceros\`, que no va a git.

### Lo que no se distribuye nunca

- `nvngx_dlssnr.dll` ("DLSS 5"): una DLL de NVIDIA **modificada por la comunidad, sin firma y sin licencia**.
  Ni en el repositorio, ni en el zip, ni en lo que baja la app (`.gitignore`, `scripts\empaquetar.ps1` y `Componentes.cs` lo impiden).
  La app solo la usa si el usuario la añade él mismo con su propio archivo (bandeja > *Añadir DLSS 5*, que comprueba que es una DLL
  x64 y la copia a `extras\`, o a mano en `extras\`), y avisa de que no es oficial. La app no enlaza ni dice de dónde sacarla.
- `runtimes\dlssg_sm86`: paquete de la comunidad que hacía que NGX cargara DLSS FG saltándose la comprobación de NVIDIA. No va en el
  zip, la app no lo baja (de la 1.2.07 a la 1.2.3 lo bajaba sola) y, desde la 1.3, sin DLSS FG, tampoco lo usa.
- Las cabeceras del SDK de DLSS de NVIDIA (`terceros\dlss_sdk`, las dos de NGX con las que se compila PrismaNr): solo para compilar,
  fuera de git y del zip (`scripts\descargar_cabeceras_ngx.ps1` las baja de github.com/NVIDIA/DLSS con su SHA-256).

## 3. Código de terceros dentro del repositorio

| Dónde | Qué | Licencia |
|---|---|---|
| `src\Motor\Engine\Shaders.cs` (`Easu`, `Rcas` dentro de `Cas`) | Port a HLSL de `FsrEasuF` y `FsrRcasF` de `ffx_fsr1.h` (AMD FidelityFX Super Resolution 1, https://github.com/GPUOpen-Effects/FidelityFX-FSR) | MIT, Copyright (c) 2021 Advanced Micro Devices, Inc. (texto abajo y en el fichero) |
| `src\Motor\Engine\Shaders.cs` (`Cas`) | AMD FidelityFX Contrast Adaptive Sharpening (`ffx_cas.h`, https://github.com/GPUOpen-Effects/FidelityFX-CAS) | MIT, Copyright (c) 2020 Advanced Micro Devices, Inc. |
| `src\Motor\Engine\Shaders.cs` (`Aa`) | Suavizado de bordes que sigue NVIDIA FXAA 3.11 (Timothy Lottes), reescrito en HLSL (hasta la 1.2.3 también como shader de mpv); el quitar el tramado es propio | BSD-3-Clause, Copyright (c) 2014-2015 NVIDIA Corporation (https://github.com/NVIDIAGameWorks/GraphicsSamples; texto abajo y en el fichero) |
| `src\Motor\Engine\EngineDlss5.cs` (`NrAntiFlickerShader`) | Filtro temporal adaptado de `src/Magpie.Core/DLSSNRTemporalShader.h` de Magpie Experimental v0.6.12 (https://github.com/SAOG0721/Magpie) | GPL-3.0, (c) los autores de Magpie. Sin el permiso adicional de `LICENSE-EXCEPTION.txt` (solo lo pueden dar sus autores). Solo se usa con la DLL no oficial de DLSS 5 |
| `src\Deteccion\NvApi.cs`, `src\Lanzador\Switch2Magpie.cs` (`NvProfiles`), `src\Motor\Engine\NvDrs.cs` | Identificadores de funciones y ajustes de NVAPI (`nvapi_interface.h`, `NvApiDriverSettings.h`) para llamar al driver | NVIDIA NVAPI SDK, MIT (https://github.com/NVIDIA/nvapi) |
| `src\Motor\Native\*.cs`, `src\Deteccion\DirectShow.cs` | Declaraciones de interop de Direct3D 12, DXGI, D3DCompiler, DirectShow y Streamline escritas a partir de sus cabeceras públicas | Direct3D: https://github.com/microsoft/DirectX-Headers (MIT); Streamline: MIT |
| `parches\fidelityfx-fi-multi.patch`, `src\CeFsrFg\CeFsrFg.cpp` | Cambio de 11 ficheros de AMD FidelityFX SDK v1.1.4 (interpolación de FSR 3.1: el instante de la inventada en vez de 0,5; sin PIX) y el envoltorio C que llama a su API desde el motor. `build_fsrfg.bat` baja el SDK (solo lo necesario) a `obj\` y aplica el parche: el SDK no está en el repositorio | MIT, Copyright (C) 2024 Advanced Micro Devices, Inc. (texto abajo) |

Nada más se ha copiado de Magpie, mpv u otros proyectos: el resto se escribió para ConsoleEnhancer (o para la app de la que sale, del
mismo autor). De otros programas solo se han tomado ideas, nunca su código.

### Texto de la licencia MIT de AMD FidelityFX (FSR 1, CAS)

```
Copyright (c) 2021 Advanced Micro Devices, Inc. All rights reserved.   (FSR 1)
Copyright (c) 2020 Advanced Micro Devices, Inc. All rights reserved.   (CAS)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

### Texto de la licencia MIT de AMD FidelityFX SDK 1.1.4 (`engine\CeFsrFg.dll`, `parches\fidelityfx-fi-multi.patch`)

```
This file is part of the FidelityFX SDK.

Copyright (C) 2024 Advanced Micro Devices, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files(the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and /or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions :

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

### Texto de la licencia BSD-3-Clause de NVIDIA FXAA 3.11

```
Copyright (c) 2014-2015, NVIDIA CORPORATION. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:
 * Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.
 * Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.
 * Neither the name of NVIDIA CORPORATION nor the names of its
   contributors may be used to endorse or promote products derived
   from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ``AS IS'' AND ANY
EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT OWNER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY
OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## 4. Marcas

NVIDIA, GeForce RTX, DLSS, Reflex, G-SYNC, AMD, Radeon, FSR, FidelityFX, AFMF, Intel, Arc, XeSS, Nintendo Switch, PlayStation,
DualSense, Xbox, Windows y las demás marcas son de sus dueños y aquí solo se nombran para describir con qué funciona la app.
ConsoleEnhancer no está afiliado, patrocinado ni respaldado por ninguno de ellos. El repositorio y el zip no llevan logotipos, iconos ni
imágenes de terceros (el icono de la app es propio) ni capturas de juegos.

## 5. HDCP y contenido de las consolas

ConsoleEnhancer muestra en tu pantalla la imagen que tu consola manda a tu capturadora; no graba, no emite y **no quita ninguna
protección anticopia**. Si la consola cifra su salida con HDCP, la imagen sale negra: hay que apagarlo desde los ajustes de la propia
consola (por ejemplo, en PS5: Ajustes > Sistema > HDMI > Activar HDCP). Los juegos y su imagen son de sus dueños.
