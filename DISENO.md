# ConsoleEnhancer: diseño (fase 1, 18-11-2026, agente UNIVERSAL)

ConsoleEnhancer es Switch2Magpie (lanzador de bandeja, `Switch2Magpie`) + Switch2Engine (motor D3D12 propio, `Switch2Engine`)
+ Magpie + mpv, pero **para cualquier consola, cualquier capturadora y cualquier PC**. Se descarga, se abre y en el primer arranque
detecta el hardware, pregunta lo que no se puede saber solo y elige la mejor cadena de imagen para ese PC.

**19-11-2026 (agente CE)**: listo para GitHub (README, LICENSE GPL-3.0, THIRD-PARTY, dependencias con
`scripts\descargar_dependencias.ps1`), inicio con Windows y apagado del PC con aviso (`src\Lanzador\InicioWindows.cs`) y Prisma del
19-11 fusionado a tres bandas (menú de la PS5/Xbox, `DetectorMenu.cs`, selector de generación `Generacion.cs` con la lista sacada del
plan de la gráfica — `Plan.ListasGeneracion` —, escritorio negro opcional `Escritorio.cs`, DLSS 5 en 2 pasadas). Detalle en `LEEME.txt`.

**20-11-2026 (agente CE-FUNCIONES)**: las escalas se adaptan a cada PC (estimación por gráfica + ajuste jugando, §9), RTX HDR y VRR (§8,
`src\Lanzador\ImagenCe.cs`, `src\Deteccion\HdrVrr.cs`), y los botones en cualquier mando: Options / Menú / + = modo 30 ↔ 60, Share / Crear /
Vista / − = generación (`src\Lanzador\BotonesCe.cs`, `mando_botones=ce|prisma`); asistente con los pasos Imagen y Mando. Detalle en `LEEME.txt`.

**20-11-2026 06:00 (agente FUSION)**: Prisma y su motor de las 06:00 fusionados (CIERRES: la imagen parada no es la consola apagada,
`src\Lanzador\Cierres.cs` + `src\Motor\Engine\EngineCuelgue.cs`, `recursos\ConsoleEnhancer.exe.config`; LATENCIA4: VRR del motor y menú sin
DLSS 5). Decisiones propias de CE: un solo VRR, el de `vrr=` (§8), y el guardián de DLSS 5 de Prisma como freno de emergencia sobre el
mismo estado de las escalas que se adaptan (§9). Las escalas siguen sin tablas medidas en `base.ini`. Detalle y pruebas en `LEEME.txt`.

**Fase 2 (18-11-2026, hecha)**: la app ya existe en `src\Lanzador` (copia de Switch2Magpie con los cambios de MANDOS, INTEGRADO,
PASADAS, NATIVO, CONSOLAS y LATENCIA hasta las 13:07, fusionados con `git merge-file` desde una instantánea base local) y `src\Motor`
(Switch2Engine con FSR 1 propio y sin Streamline fuera de NVIDIA). Asistente: `src\Lanzador\Asistente.cs`; enganche y claves nuevas:
`src\Lanzador\ConsoleEnhancerApp.cs`; compilar `build.bat` / `build_motor.bat`; paquete `scripts\empaquetar.ps1`. Ver `LEEME.txt`.
Estado: **fase 1** = este diseño + el módulo de detección (`src\Deteccion\*.cs`, compila y funciona) + su prueba
(`tests\DeteccionTest.cs`). La **fase 2** (copiar el código final de las dos apps, renombrar, quitar rutas fijas, enganchar la
detección, empaquetar) está hecha: ver `LEEME.txt` y `README.md`.

---------------------------------------------------------------------------------------------------------------------------------

## 1. Lo que hoy está atado a este PC (inventario)

Leído en `Switch2Magpie\src\Switch2Magpie.cs` (13.100 líneas), `SinTelon.cs`, `TelonVivo.cs`, `PrismaPreview.cs`, `scripts\`,
`Switch2Engine\src\**`, `DESIGN.md`, `LEEME.txt`, `docs\SL_NOTES.md` y `docs\STANDBY.md` (números de línea del 18-11, antes de la fase 2).

### 1.1 Rutas fijas
| Qué | Dónde | En ConsoleEnhancer |
|---|---|---|
| `Paths.AppDir = Switch2Magpie` y todo lo que cuelga: `switch2.ini`, `switch2.log`, `scripts\*.lua/.glsl`, `juegos\*.txt` (captura, nivel, modo, generación, último juego, audio_pin, home_ref, perfiles, iconos...) | Switch2Magpie.cs:42-95 | `Paths` relativo al exe (ver §3). INTEGRADO ya hace `src\Integrado.cs`: AppDir = carpeta del exe si es una carpeta completa |
| `mpv_path = mpv.exe instalado aparte` (también en el ini de serie y en `PrismaPreview.cs:209`) | :199, :936 | `<app>\mpv\mpv.exe` |
| `MagpieSource = Magpie aparte` (enlaces duros a la instalación del usuario) y la config base leída de `%LOCALAPPDATA%\Magpie\config\v4e\config.json` | :277, :87-94, :943 | Magpie dentro de la app (`<app>\magpie`, config portátil propia); nunca se lee ni se toca el Magpie del usuario |
| `EnginePath = Switch2Engine\bin\Switch2Engine.exe` y los mensajes "ver Switch2Engine\bin\engine.log" | :688, :12462, :12637 | `<app>\motor\ConsoleEnhancerEngine.exe`, log en la carpeta de datos |
| Streaming Center (programa de AVerMedia) `C:\ProgramData\...\Streaming Center.lnk` como audio de reserva | :213, :11104-11130, :11385 | Fuera. El audio se elige en el asistente (DirectShow / WASAPI); sin reserva de un fabricante |
| MSI Afterburner (`ProgramFilesX86\MSI Afterburner`, tarea `\MSIAfterburner`) | :2567-2674 | Se queda como opción genérica "programas que molestan" (desactivada de serie); sin rutas fijas |
| Playnite (`segundo_plano = Playnite.FullscreenApp, Playnite.DesktopApp`, reactivarlo al salir) | :271, :2894-2983 | Lista vacía de serie; el asistente ofrece los lanzadores que vea abiertos (Playnite, Steam Big Picture) |
| Motor: `TestTexture = Switch2Engine\tests\zoom_oot.png` | Common.cs:139 | Relativa al exe (solo pruebas) |
| Scripts y herramientas: `banco.ps1`, `explora.ps1`, `latencia_*.ps1`, `montajes.ps1`, `resumen.ps1`, `turno.ps1`, `latencia.cs`, `empaquetar.ps1` (36 rutas `Switch2Magpie` / `mpv aparte` / `Magpie aparte`) | scripts\ | Herramientas de desarrollo: no van en el paquete; las que sirvan, con rutas por parámetro |
| Fichero de apagado `%TEMP%\switch2_poweroff.flag`, tuberías `switch2-mpv-<pid>`, `\\.\pipe\switch2engine`, `switch2-audio-*` | :51-54, :101-124, Common.cs:87 | Nombres `consoleenhancer-*` |

### 1.2 Capturadora (AVerMedia Live Gamer 4K GC573, PCIe)
| Qué | Dónde |
|---|---|
| `device = AVerMedia HD Capture GC573 1` (nombre DirectShow) y `video_size=2560x1440`, `framerate=60000/1001`, `pixel_format=bgr24` | Switch2Magpie.cs:200-204, :938-941 |
| mpv abre `av://dshow:video=<device>` y el audio `dshow:audio=<device>` con `audio_pin_name` 2 / 1 ("Audio" / "Audio Mixer": pines de audio **del propio filtro de vídeo**; la categoría de audio de DirectShow de este PC está vacía) y 8 canales con mezcla a estéreo (orden de canales de HDMI) | :11089, :11222-11249, :12724 |
| La pantalla "No Signal" que pinta **la AVerMedia** (franjas negras + recuadro gris) = consola apagada | `HomeLabel.IsNoSignal` :4126-4152, `scripts\switch2_power.lua`, `Switch2Engine\src\Engine\PowerWatch.cs` |
| Motor: `Device = "GC573"` (subcadena), `MfCapture.DeviceName = "GC573"`; Media Foundation no abre la GC573 (0xC00D36B4) → DirectShow | Common.cs:53, MfCapture.cs:516/590, DsCapture.cs:1-7 |
| La GC573 compartida por dos procesos con el mismo formato (relevo motor ↔ mpv sin telón) | SinTelon.cs:5 |
| A 4K en RGB24 llega a ~53 fps (PCIe); el HOME a 4K nativo cuenta con que la capturadora coge 4K60 | LEEME del motor, `CaptureNow` :222 |

### 1.3 Gráfica (RTX 4060 8 GB, Ada, driver 616.92)
| Qué | Dónde |
|---|---|
| Motor: Streamline obligatorio en la práctica (DLSS SR, DLSS-G, Reflex, PCL); sin `sl.interposer.dll` arranca, pero sin escalado decente (solo Catmull-Rom) | Engine.cs:118-174, build.bat |
| Perfiles de NVIDIA por exe con NVAPI (`nvapi64.dll`): máximo rendimiento, latencia ultra baja, sincronización vertical forzada, y **Smooth Motion** con el ajuste oculto `0xB0D384C0` puesto con la función privada `NvAPI_DRS_SetSettingEx` (ids sacados de NvBackend64.dll del driver 616.92); perfiles "Switch 2 - Magpie.exe / mpv.exe / Switch2Engine.exe" | `NvProfiles` :3039-3436, `NvDrs.cs` del motor, `scripts\nvdrs.exe` |
| Reinicios de la gráfica vigilados por el proveedor `nvlddmkm` (solo NVIDIA) | `GpuReset` :127-160 |
| Cadena de Magpie fija a funciones NVIDIA: DLSSNR ("DLSS 5"), DLSS SR con flujo óptico NVOF (`dlss_flujo=2`), DLSS FG, RTX Video (`escalador_alternativo=vsr`), RTX HDR, VFG; regla del usuario "DLSS E como mínimo" | Config :279-680 |
| **Tablas medidas en esta GPU** (con su captura y su tele): `ModelTable`, `Dlss5PctTable` (+ `_sm`, `_4k`, `_1080`, `_720`), `LightTable`, `D5ScalerTable`, `D5FlowTable`, `D5AntiFlickerTable`, `D5EdgeAaTable`, `D5MaskTable`, `D5MpvScaleTable`, `D5Flow5Table`, `D5SharpenTable`, `Dlss5EveryTable`, `EnginePreset60/60Fg/30/30Fg`, `EnginePresetByCap`, y en el motor `motor_extra` (mvconsenso, mvlargo...) y `mv_ligero=3000000` | Config :289-754, switch2.ini |
| Costes medidos (motor, salida 4K, 4060): DLSS E 4,8 / K 6,2 / M 10,1 / L 12,1 ms; vectores 1,3 ms; 60 M 11,9 ms; 60+FG K 8,0; 30 L 13,6 | LEEME del motor |
| Motor en espera: deja ~1-2 GB de VRAM ocupados (pensado para 8 GB) | LEEME :125-137 |
| DLSS 5: `nvngx_dlssnr.dll` 310.9.16 **DLL de NVIDIA filtrada y parcheada por la comunidad, sin firma**; también `runtimes\dlssg_sm86` (DLSS-G para RTX 30 de la comunidad, autofirmado) | magpie\ |

### 1.4 Pantalla (tele Panasonic 4K 120 Hz, HDMI 2.1)
| Qué | Dónde |
|---|---|
| Salida a 3840x2160 dada por hecha: ventana de mpv 2560x1440 o 3840x2160 (`WindowFor`), `Win4k`, HOME a 4K nativo, motor `OutW/OutH = 3840x2160` de serie | :222, :331, :849, Common.cs:58, Parallax/ZoomScene |
| Solo la pantalla principal (`Screen.PrimaryScreen`, `EnumDisplaySettings(null)`) | :2555, :5487, :5736, :6269, :6390, :6436 |
| Reglas de 120 Hz: `Pantalla.Is119` (119,88 → DLSS-G en vez de Smooth Motion), `Hz() >= 100` para la sincronización "cada 2 refrescos" del modo 30 | :2540-2565, :610, :676, :744 |
| Rarezas de esta tele: se pone a 60 Hz ~30 min después de encenderla; Windows la apagaba a los 15 min sin ratón (`pantalla_despierta`) | LEEME :91-92, motor `KeepAwake` |
| HDR: no se usa (opción RTX HDR de Magpie); VRR: opciones de Magpie | :621-635 |

### 1.5 Consola (Switch 2)
Reconocimiento del HOME (color cian del nombre, OCR de Windows, `HomeHints` en español "cambiar de", "ver más"), iconos y perfiles por
juego (`juegos\`), HOME a 4K nativo, filtro de tramado de 5 bits (`s2_aa.glsl`, `s2_aa_strong.glsl`), esperas de "No Signal" al
arrancar juegos y en el menú de apagado, detección de juego a 30/60, apagar el PC al apagar la consola, 44 textos "Switch 2". Lo
generaliza el agente CONSOLAS (`src\Consolas.cs`); aquí solo se usa su identificador (§5).

### 1.6 Mando (GameSir Nova Lite, Joyxoff abierto)
Solo `XInputGetState` (`xinput1_4.dll`), el primer mando; botones fijos (cruceta = captura, X/Y/B/A = pasadas, L1/L2/R2/R1 = nivel,
Share/Options = modo 30/60, L3, R3); el botón de captura del GameSir llega por el teclado de su receptor (Win DERECHA + Impr Pant,
VID_3537); mpv sin atajos para no chocar con Joyxoff (:7032-7060, :7425-7445, :12789). El agente MANDOS hace la entrada sin Joyxoff
(`src\Mandos.cs`).

### 1.7 Idioma
Todo en español escrito en el código (avisos, bandeja, telón, log); OCR con los idiomas del perfil de Windows; números con coma.

---------------------------------------------------------------------------------------------------------------------------------

## 2. Principios
1. **Funciona perfecto sin nada no oficial.** DLSS 5 (DLSSNR) y DLSS-G para RTX 30 son extras que solo se activan si el usuario ya
   tiene esos archivos, con aviso de que no son oficiales. Nada del paquete depende de ellos.
2. **Detectar antes que preguntar, preguntar antes que suponer.** Lo que no se puede saber solo (qué consola, qué entrada de audio
   suena, si la imagen se ve) lo pregunta el asistente una vez.
3. **Mismo motor, distinta cadena.** El motor propio es el camino principal en todas las gráficas (menos latencia que mpv + Magpie);
   cambia el escalador y la generación según el hardware. mpv + Magpie quedan para lo que solo tiene Magpie (DLSS 5, FSR 3 FG,
   XeSS FG, RTX Video, RTX HDR) y como reserva.
4. **Las tablas no se escriben a mano**: salen de un banco corto en el primer arranque (y cuando cambia el hardware o el driver).
5. **No tocar el sistema** más que lo mínimo y reversible: perfiles de NVIDIA solo para los exe de la app, nunca el global.

---------------------------------------------------------------------------------------------------------------------------------

## 3. Carpetas (portable)
```
ConsoleEnhancer\                      (donde se descomprima: escritorio, D:\, un USB...; también Program Files)
  ConsoleEnhancer.exe                 lanzador de bandeja (el Switch2Magpie de ahora, renombrado)
  portable.txt                        si está (y la carpeta se puede escribir): los datos van en .\datos
  motor\ConsoleEnhancerEngine.exe     el motor (Switch2Engine renombrado) + sl.*.dll + nvngx_dlss.dll + nvngx_dlssg.dll
  mpv\mpv.exe (+ d3dcompiler_43.dll)  audio siempre; vídeo cuando va Magpie
  magpie\                             Magpie Experimental con config portátil propia (sin nvngx_dlssnr.dll ni runtimes\dlssg_sm86)
  scripts\                            ce_power.lua, s2_aa*.glsl (shaders de mpv), fsr/cas del motor van dentro del exe
  idiomas\es.txt, en.txt              textos de pantalla
  licencias\                          todas las licencias y avisos (§12)
  extras\                             vacío: aquí pondría el usuario nvngx_dlssnr.dll o dlssg_sm86 si los tiene (no oficiales)
datos (portable: .\datos ; si no: %LOCALAPPDATA%\ConsoleEnhancer)
  consoleenhancer.ini                 ajustes (los de serie los escribe el plan; el usuario puede cambiarlos como en switch2.ini)
  hardware.txt                        la última detección (huella para saber si algo cambió) y el plan elegido
  calibracion.txt                     el banco corto (ms por pieza en esta gráfica/driver/captura/salida)
  consolas\<id>\                      lo que era juegos\ (perfiles por juego, iconos, HOME...), por consola
  logs\consoleenhancer.log, engine.log, magpie.log
```
Reglas: `Paths.AppDir = Path.GetDirectoryName(Application.ExecutablePath)`; `Paths.Datos` = `.\datos` si existe `portable.txt` y
se puede escribir, si no `%LOCALAPPDATA%\ConsoleEnhancer`. Nada en `C:\` fijo, nada en el registro salvo los perfiles de NVIDIA (y
el inicio con Windows si el usuario lo pide). El motor recibe la carpeta de datos por argumento (`datos=<ruta>`) para su log.

---------------------------------------------------------------------------------------------------------------------------------

## 4. Primer arranque: asistente
Ventana de pantalla completa en la tele (estilo Prisma de ahora), manejable con mando, teclado y ratón. Cada paso enseña lo detectado
y un botón "Cambiar". Se puede volver a abrir desde la bandeja ("Configurar de nuevo").

1. **Idioma**: español / inglés (de serie el de Windows).
2. **Detección** (~1 s, sin abrir la capturadora): resumen de gráfica, pantalla, capturadoras, mandos. Avisos que se pueden arreglar
   en el momento: HAGS apagada (DLSS-G no va), driver viejo, tele a 60 Hz cuando admite 120, tele a 119,88 Hz, sin capturadora.
3. **Capturadora**: lista de dispositivos DirectShow ordenada por puntos (capturadoras arriba; webcams y cámaras virtuales abajo,
   marcadas). Al elegir una se abre (aquí sí) y se leen sus formatos; **prueba de imagen**: vista previa en vivo 5 s con el formato
   elegido y el ritmo medido ("llegan 59,94 imágenes/s"). Se elige resolución y Hz (sugeridos por el plan: sin comprimir, la mayor
   que convenga a la pantalla, 60000/1001). Si solo hay MJPEG/H.264 se avisa (más latencia) y se usa el descompresor de DirectShow.
4. **Sin señal**: "Apaga la consola" → se graba cómo se ve la capturadora sin señal (AVerMedia pinta su pantalla "No Signal",
   Elgato da negro, las UVC baratas azul o barras, algunas dejan de mandar imágenes). Esa firma (rejilla 48x27 + si llegan imágenes)
   sustituye a `IsNoSignal` de AVerMedia. Se puede saltar (queda negro + silencio + imagen parada, como el script de mpv).
5. **Audio**: la entrada emparejada por la detección (mismo filtro DirectShow, mismo aparato USB, mismo contenedor, nombre parecido)
   y un medidor en vivo: "¿se mueve la barra con el sonido de la consola?". Si no, se prueba la siguiente (y los pines 1/2 en las que
   llevan el audio en el filtro de vídeo, como hace hoy `ConsoleAudioLoop`). 2 u 8 canales, como ahora.
6. **Consola**: lista (claves de `Consolas.cs`: switch2, switch1, ps5, ps4, xboxseries, xboxone, otra; y "auto", que la deduce de la imagen). Decide: resolución máxima de entrada, si
   hay 120 Hz, si hay HOME que reconocer (solo Switch 2 hoy), filtro de tramado, esperas de "No Signal". La lista y sus datos son de
   `src\Consolas.cs` (agente CONSOLAS).
7. **Pantalla**: la de la tele si hay varias (la más grande / la de más Hz, o la que diga el usuario); resolución y Hz actuales y
   máximos; HDR y VRR. Recomendación: "120 Hz (120,000, no 119,88)" si se va a usar Smooth Motion.
8. **Mandos**: los detectados (XInput y HID), "pulsa un botón" para saber cuál es; la tabla de botones de la app según el mando
   (Nintendo / PlayStation / Xbox) y qué botón hace de "captura" (se aprende pulsándolo: tecla, botón HID o combinación).
9. **Banco corto** (§9, 30-60 s, con la imagen de prueba y la capturadora): mide y fija modelos y escalas. Se ve una barra.
10. **Resumen** con la cadena elegida y los motivos (el texto de `PlanImagen.Informe()`), "Empezar".

Si algo del hardware cambia en un arranque normal (otra gráfica o driver, otra tele/modo, otra capturadora), la app lo dice y ofrece
repetir solo el paso que toca (y el banco si cambia gráfica, driver, captura o salida).

---------------------------------------------------------------------------------------------------------------------------------

## 5. Detección y adaptación (módulo `src\Deteccion`)
Escrito ya en C# 5 con P/Invoke y COM a mano, sin NuGet; no escribe nada en el sistema. API:
`Hardware.Detectar(OpcionesDeteccion)` → `Hardware` (Gpus, Pantallas, Capturadoras, Audios, Mandos, Sistema, Errores, Tiempos);
`Planificador.Elegir(hw, PreferenciasPlan)` → `PlanImagen` (cadena, modelos por modo, generación, captura, avisos, `ComoIni()`).

| Pieza | Archivo | Cómo |
|---|---|---|
| Gráficas | `Gpu.cs`, `NvApi.cs` | DXGI (nombre, VendorId/DeviceId, VRAM, LUID, driver UMD, presupuesto de VRAM), un dispositivo D3D12 de un momento (nivel 12_x, shader model), D3DKMT (HAGS, WDDM), registro de la clase Display (VRAM real, fecha, versión de AMD Software). NVIDIA: NVAPI (arquitectura, nombre, driver y rama, portátil, si el driver lista el ajuste de Smooth Motion). Familias: RTX 20/30/40/50, GTX, RDNA 1-4, GCN, Intel Arc A/B, Xe, antiguas, software. Funciones con su motivo: DLSS SR, transformer, DLSS-G, x3/x4, Smooth Motion, Reflex, NVOF, DLSS 5, DLSS-G de la comunidad, RTX Video, RTX HDR, FSR 1, FSR 3 FG, FSR 4, XeSS, XeSS FG, AFMF, Anti-Lag, motor propio. Índice de potencia aproximado (RTX 4060 = 1,0) |
| Capturadoras | `Capturadoras.cs`, `DirectShow.cs` | DirectShow (vídeo y audio) por la "property bag" = **sin abrir**; Media Foundation (`MFEnumDeviceSources`, sin activar) como alternativa; nodo PnP (bus, VID/PID, fabricante, driver, contenedor); catálogo (AVerMedia GC573/GC575/GC553/GC551/GC311, Elgato HD60 X/S+/S, 4K X/S, 4K60 Pro, Cam Link, Magewell, MS2109, MS2130, Razer, Hauppauge, Blackmagic...); clasificación capturadora/webcam/virtual; emparejado del audio; **formatos solo si se pide** (`AbrirCapturadoras`, con `AntesDeAbrir`/`DespuesDeAbrir`: en la prueba, el turno de `gpu.lock`) |
| Pantallas | `Pantallas.cs` | QueryDisplayConfig (refresco exacto racional, nombre del monitor, HDMI/DP, HDR "advanced color"), EnumDisplaySettings (modo actual, máximos, Hz de la máxima), EDID (nativa, VRR HDMI Forum, FreeSync, ALLM, FRL, HDR10/HLG, rango), DXGI_OUTPUT_DESC1 (HDR activo, nits), tearing de DXGI, G-SYNC por NVAPI |
| Mandos | `Controles.cs` | XInput (4 ranuras, tipo, batería), HID con acceso 0 (VID/PID, uso gamepad/joystick, nombre), tabla de marcas, programas de mandos abiertos (Joyxoff, DS4Windows, Steam, reWASD...) y drivers virtuales (ViGEmBus, HidHide) |
| Sistema | `Sistema.cs` | Windows de verdad (RtlGetVersion), CPU y núcleos P/E, RAM, portátil, plan de energía, "optimizaciones/VRR para juegos en ventana", idioma, administrador |
| Simulación | `Simulador.cs` | 19 casos: sin NVIDIA y sin capturadora (camino real del código), RTX 2060/3060/3050 4 GB/4090/5080, GTX 1660, RX 7800 XT/9070 XT/580, Arc B580, Iris Xe, MS2109, HD60 X, pantallas 1080p60/1440p240, tele a 119,88, HAGS apagada |

Medido en este PC (18-11, `tests\bin\DeteccionTest.exe`): detección completa en ~0,8 s (gráfica ~0,4-0,5 s por el dispositivo
D3D12 y NVAPI; pantallas 50-80 ms; capturadoras 6-100 ms sin abrir; mandos 4-80 ms). Sale la RTX 4060 (Ada 0x190, 8 GB, 616.92,
HAGS encendida, D3D12 12_2, SM 6.8), la Panasonic-TV a 3840x2160 a 120,000 Hz (HDMI 2.1 FRL 6, VRR 40-120, HDR10/HLG, ALLM), la
GC573 (PCI 1461:0054, driver AVXGC573Cap, 160 puntos) con su audio en el filtro y la cámara virtual de Streaming Center apartada.
Abriéndola (`--formatos`, con el turno de `gpu.lock`): 80 formatos (NV12/YUY2/RGB24/P010; 4K 24-60, 3440x1440 100, 1440p 144/120,
1080p 240/144/120, y hasta 640x480) y 2 pines de audio en el filtro; el plan elige 2560x1440 NV12 a 60000/1001 (hoy: bgr24) y,
con `consola=switch2`, HOME a 4K nativo y "120 Hz posibles".

Cuándo se detecta: al arrancar (rápido, sin abrir nada) y se compara con `hardware.txt`; el asistente hace la completa (abre la
capturadora). Lo que comprueba el motor al arrancar (`slIsFeatureSupported`, el perfil de Smooth Motion aceptado o no) manda sobre lo
deducido: si DLSS-G o Smooth Motion fallan, el plan se rehace sin ellos y se apunta (como hoy `NvProfiles.SmoothBroken`).

**La consola con `consola=auto`** (`src\Lanzador\Consolas.cs`, `DetectorMenu.cs`, `MenuPs5.cs`; de Prisma, agente CONSOLA, fusión del 20-11
06:38). La consola se deduce de la imagen, y una consola tomada por otra cambia el apagado (negro con silencio, espera de "No Signal"). Reglas:

| Regla | Detalle |
|---|---|
| Pruebas **propias** y de **apoyo** | Propias: el HOME de la Switch 2 con su referencia (concluyente), la hora de la PS5 en su sitio exacto, la marca escrita, pantallas que solo tiene una. Apoyo: la hora en la franja de arriba, "PlayStation" / "Xbox" / "Game Pass" / "Nintendo" sueltas, un juego a 40. Las de apoyo suman, pero solas nunca cambian la consola |
| Repetidas en el tiempo | Cambiar pide 2 pruebas propias con 5 s entre la primera y la última; de una probada (Switch 2, PS5) a una SIN PROBAR (Xbox, PS4, Switch 1), 3 en 20 s |
| Ante la duda, la puesta | Su menú visto con su lector la refuerza; si lo vio hace menos de 20 s, solo una prueba concluyente la cambia. Con una PlayStation puesta, la hora de arriba no es pista de la Xbox |
| `juegos\consola.txt` | Solo se escribe con la consola confirmada por sus pruebas propias |
| Apagado prudente | Con `consola=auto` y una consola SIN PROBAR u "otra" elegida sola: sin cierre por negro y silencio y "No Signal" jugando con la espera más larga (10 s). Al cambiar de consola en marcha, su espera y su negro con silencio van al motor ya abierto |

Prueba sin consola: `--test-consolas=<informe>` (0 fallos) y `--test-deteccion=<carpeta de capturas>;<informe>` (las capturas no van en el
repositorio: son de juegos con copyright).

---------------------------------------------------------------------------------------------------------------------------------

## 6. Cadena de imagen según el hardware
Siempre: capturadora (DirectShow) → GPU → bordes y tramado (s2_aa, según consola) → vectores de movimiento propios → **escalador** →
enfoque CAS → aviso a la resolución de salida → **generación** → pantalla.

| Gráfica | Escalador (motor) | Generación modo 60 (pantalla ≥ 120 Hz) | Modo 30 | Latencia | DLSS 5 (extra) |
|---|---|---|---|---|---|
| **RTX 50** | DLSS SR (L/M/K/E según banco) | DLSS-G x2; x3/x4 si la pantalla tiene ≥ 180/240 Hz (`MfgMax`=4); alternativa Smooth Motion x2 | DLSS-G x2 | Reflex | Sí si está la DLL (la original de NVIDIA para RTX 50, la de la comunidad no) |
| **RTX 40** | DLSS SR | **Smooth Motion x2** (perfil del exe; preferido por el usuario); DLSS-G x2 si la tele está a 119,88 Hz, si el driver no deja poner Smooth Motion o si se elige | igual | Reflex | Sí si está la DLL (con el modelo FP8) |
| **RTX 20 / 30** | DLSS SR (transformer más caro: factor 1,8 / 1,35 en la estimación; el banco decide, a menudo E) | **ninguna** de serie. Opciones: FSR 3 FG x2 por Magpie; en RTX 30, DLSS-G de la comunidad (`dlssg_sm86`) si el usuario lo tiene | igual | Reflex | Sí si está la DLL, sin FP8 |
| **GTX 10/16** | **FSR 1 (EASU + RCAS)** en shader propio + CAS | FSR 3 FG x2 por Magpie (apagada de serie) | igual | Reflex (Maxwell o posterior) | No |
| **AMD RX 6000-9000** (y 680M/7x0M/8x0M) | FSR 1 + CAS (RX 9000: FSR 4 por Magpie como alternativa) | **AFMF 2 del driver** (la app no puede encenderlo: el asistente enseña dónde, AMD Software > el exe del motor); alternativa FSR 3 FG por Magpie | igual | Radeon Anti-Lag (driver) | No |
| **AMD RX 400/500/5000** | FSR 1 + CAS | FSR 3 FG por Magpie si SM 6.2 (Polaris no) | | | No |
| **Intel Arc A/B** | FSR 1 + CAS (XeSS XMX por Magpie como alternativa) | XeSS FG por Magpie (nativo en Arc; x2-x4) | igual | ninguna (cola de 1 imagen) | No |
| **Intel Xe / UHD** | FSR 1, captura 1080p | FSR 3 FG por Magpie si cabe (casi nunca) | | | No |
| Sin D3D12 12_0 o solo la gráfica de software | mpv solo, sin escalado ni generación; aviso "instala el driver" | | | | |

- **Sin Streamline cuando no hay NVIDIA**: el motor no carga `sl.interposer.dll` (crea DXGI y D3D12 normales), no hay DLSS ni Reflex;
  el escalador es FSR 1 propio. Con NVIDIA GTX se carga solo para Reflex.
- **Generación por Magpie** (FSR 3 FG, XeSS FG): pasa la imagen a mpv + Magpie (como hoy DLSS 5): más latencia (~+20-30 ms) → apagada
  de serie; el botón de captura la enciende. Magpie trae `amd_fidelityfx_*` y `libxess*` firmados; los dos necesitan flujo óptico
  (AMDOF, cualquier gráfica).
- **Pantalla de 60 Hz**: sin generación en el modo 60 (no aporta), sí en el modo 30 (30 → 60).
- **Captura = resolución de la pantalla** (1080p en 1080p, 1440p en 1440p): sin escalado; quedan bordes, enfoque y generación.
- Modelos de DLSS por modo: el mejor que cabe en el 75 % del tiempo de una imagen real (16,7 ms a 60, 33,4 ms a 30) según la
  estimación (costes de la 4060 escalados por resolución de salida e índice de la gráfica) o, mejor, lo medido por el banco. En este
  PC la estimación da lo mismo que se midió a mano: 60 M, 60+generación K, 30 L, 30+generación L. Si ni E cabe en el 60 con captura
  de más de 1080p, el plan baja la captura a 1080p, y si aun así no cabe, FSR 1 de serie con DLSS en R3 (probado con la RTX 3050
  portátil de 4 GB simulada: 1440p → 1080p → FSR 1, 6,1 ms de 12,5). El `auto_preset` del motor (bajar de modelo si no cabe 2 s
  seguidos) sigue de red. (La regla "DLSS E como mínimo" del usuario es de este PC, donde E siempre cabe.)
- **DLSS 5** (Magpie DLSSNR, no oficial): solo si existe `nvngx_dlssnr.dll` en `extras\` o en `magpie\` (la pone el usuario), gráfica
  RTX y ≥ 6 GB; hasta 3 pasadas con ≥ 8 GB, 1 con 6-7 GB; modelo CC Small FP8 solo en RTX 40/50. Aviso en el asistente y en el
  primer uso: "DLL de NVIDIA modificada por la comunidad, sin firma; úsala bajo tu responsabilidad". Sus escalas las mide el banco
  (las tablas `tabla_dlss5_*` de hoy son de la 4060 y solo sirven de punto de partida escaladas por el índice).

---------------------------------------------------------------------------------------------------------------------------------

## 7. Capturadoras
- **Cualquier dispositivo de vídeo DirectShow**: el motor abre por `DevicePath` (no por subcadena del nombre), con el formato
  elegido por `IAMStreamConfig`. Los que solo están en Media Foundation (cámaras del "Frame Server") se abren por MF
  (`MfCapture.cs` del motor ya existe).
- **Formatos**: NV12 > YUY2 > RGB24 > RGB32 > P010 (HDR) > MJPEG (descompresor de DirectShow `CLSID_MjpegDec` en el grafo, o mpv) >
  H.264/HEVC (solo mpv). El motor ya desempaqueta bgr24/rgb32/yuy2/nv12; falta P010 (HDR) y la entrada MJPEG por el descompresor.
- **Hz**: 60000/1001 de serie (lo que mandan las consolas); 120 si consola, capturadora y pantalla lo admiten (PS5/Xbox Series/Switch 2
  en juegos de 120): entonces sin generación.
- **Audio**: emparejado automático (orden de confianza): mismo nombre en la categoría de audio de DirectShow → pines de audio del
  propio filtro de vídeo (GC573; se ve al abrirla) → el mismo aparato USB (padre común del nodo de audio y del de vídeo) → el mismo
  contenedor de Windows (no vale el de "este PC") → mismo VID/PID → nombre parecido. Se confirma en el asistente con el medidor.
  WASAPI (endpoints de captura) se lee del registro sin abrir nada; el mpv de audio abre `dshow:audio=` o `wasapi` según el origen.
- **UVC baratas (MS2109 USB 2.0)**: a 1080p solo MJPEG a 30; el plan elige 720p60 MJPEG y avisa; MS2130 (USB 3.0) YUY2 1080p60.
- **Sin señal**: firma aprendida en el asistente (§4.4).
- **HOME a 4K nativo**: solo con Switch 2, pantalla 4K y capturadora que coja 4K60 sin comprimir.

## 8. Pantallas
- El motor pinta a la resolución **actual** de la pantalla elegida (no 3840x2160 fijo); ventana en esa pantalla (no `PrimaryScreen`).
- Hz: el exacto (racional de QueryDisplayConfig). 119,88 → DLSS-G en vez de Smooth Motion (regla de hoy, generalizada a "Hz exacto
  = 2 × 59,94"). Si admite más Hz de los que tiene puestos, aviso.
- Generación: multiplicador = ⌊Hz / 59,94⌋ con tope de la gráfica (x2 RTX 40/Smooth Motion/AFMF/FSR 3; x4 RTX 50 y XeSS FG).
- HDR: el motor pinta en SDR (con el HDR de Windows encendido, Windows lo compone con el brillo SDR). Captura HDR (P010) = fase 3.
  **20-11 (CE-FUNCIONES)**: RTX HDR del driver (`rtx_hdr=1`, apagado de serie; NVIDIA RTX, driver 551.52+, Windows 11 22H2+, pantalla
  HDR) por perfil de NVIDIA de los tres exe que presentan, puesto al abrir y quitado al salir; el HDR de Windows se enciende mientras
  la app está abierta (`hdr_windows=auto`). Auto HDR de Windows no (sin documentar, solo juegos de su lista). `src\Lanzador\ImagenCe.cs`.
- VRR: se detecta (EDID + NVAPI + tearing de DXGI). **20-11**: `vrr=auto|0|1`; auto = encendido solo si G-SYNC se ha visto funcionando
  en esa pantalla (NVIDIA; los bits de NvAPI_Disp_GetVRRInfo dependen de lo que esté abierto: se recuerda) y no está apagado en el perfil
  global; AMD / Intel apagado. Encendido: `vrr=1` al motor, VRR de Magpie en los modos con DLSS FG y G-SYNC en ventana en el perfil de los
  tres exe. `src\Deteccion\HdrVrr.cs`.
  **20-11 (FUSION, VRR del motor de Prisma LATENCIA4)**: el motor con `vrr=1` crea la cadena con `ALLOW_TEARING` y presenta sin generación
  con `Present(0, ALLOW_TEARING)` (cada imagen al acabarla); con la generación, `vrr_fg_vsync` (-1 auto: sin sincronizar si 2 x las reales
  caben bajo el 90 % del refresco, el modo 30; 0 nunca; 1 siempre). **Un solo VRR**: manda `vrr=` de CE; `CeImagen.AlAbrir` pone el
  `motor_vrr` de Prisma (`Config.EngineVrr`, el del ini no cuenta) y la línea del motor de Prisma añade `vrr=1` y `vrr_fg_vsync=` una vez.
  Al motor solo si la pantalla dice tener VRR (EDID / driver) y DXGI deja presentar sin sincronizar: sin NVIDIA el motor no puede leer el
  estado de VRR (NVAPI) y en una pantalla sin VRR `ALLOW_TEARING` daría cortes. Magpie no tiene ese riesgo (su VRR va con la
  sincronización forzada). Sin NVIDIA el motor no carga NVAPI para el estado de VRR.
  **20-11 (FUSION G, refresco fijo de Prisma LATENCIA5)**: con G-SYNC, Reflex limita la salida a ~hz − hz²/3600 (116 a 120 Hz, 138 a 144):
  en el modo 60 con DLSS FG (2 × 59,94 = 119,88) se pierden ~2 imágenes reales por segundo. Prisma pone el refresco fijo (0x10A879CF y
  0x10A879AC = 4) solo en el perfil de NVIDIA del exe del motor mientras se juega en ese modo (el driver lo mira al abrir el proceso: el motor
  se reabre al entrar o salir de él; el motor en espera con otro refresco se descarta) y lo quita al salir: medido 59,9 reales/s y 25 ms en
  vez de 57,8 y 32,6, sin cortes. En CE, `motor_60fg_refresco_fijo=auto|0|1` (de serie `auto`; la de Prisma era 0|1): `auto` = NVIDIA, VRR en
  uso con el motor (`Config.EngineVrr`, de `vrr=`), el tope por debajo de 2 × las imágenes/s de la captura y la pantalla más rápida que esa
  salida (a 119,88 Hz no: la cola se llena, medido en Prisma) → 120 Hz sí; 144 Hz, 119,88 Hz, sin VRR, AMD / Intel, no
  (`Config.CeRefrescoFijoAhora`, `ImagenCe.cs`). Convive con lo de CE en el mismo perfil: G-SYNC en ventana (0x1194F158 / 0x1094F1F7) y RTX
  HDR van por `NvDrsCe`, que solo escribe y devuelve sus ids, y el refresco fijo solo pone y borra los suyos (probado: durante la partida los
  cuatro a la vez; al salir el perfil vuelve a no tener nada). Devolución: cada vez que se pone o se quita, la línea `fijo_exe=` en
  `datos\imagen_restaurar.txt`; si la app se cae, `CeImagen.Restaurar` lo quita al volver a abrirla (probado matando la app con él puesto).
  El perfil, si no existe, se crea como "ConsoleEnhancer - ConsoleEnhancerEngine.exe"; sin NVIDIA no se llama a NVAPI.

## 9. Tablas que se calibran solas

**20-11 (CE-FUNCIONES), lo hecho** (en vez del banco corto, que sigue sin hacer): estimación por gráfica en el asistente
(`src\Lanzador\Escalas.cs`: lo medido en la RTX 4060 Ti del autor escalado por el índice de la gráfica y la salida, con margen; las tablas ya no
están en `recursos\base.ini`) y ajuste jugando: el motor baja y sube % de DLSS 5, pasadas y modelo de DLSS SR con el tiempo de gráfica medido,
con histéresis, y lo recuerda por gráfica + driver + modo + generación + captura + salida (`src\Motor\Engine\EngineAdapta.cs`,
`datos\escalas_motor.txt`); con Magpie (que no cambia en caliente) la escala de DLSS 5 de ese modo baja si Magpie pierde imágenes reales y
sube tras 10 min sin perder (`datos\escalas_magpie.txt`). Lo de abajo es el plan original del banco.

**20-11 (FUSION): dos controladores sobre DLSS 5 en el motor, un solo estado.** Prisma trae el guardián (CIERRES, `EngineDlss5.cs`,
`dlss5_guardian=1`: p95 > 1,6 x tope 2 s o > 1,15 x tope 5 s → una pasada menos, luego el % a 75 y 50; sus topes valían solo para esa
sesión del motor) y CE las escalas que se adaptan (`EngineAdapta.cs`: > tope 2 s → -10 puntos hasta 25 y luego una pasada; sobra 20 s → +5,
recordado). Juntos sin más, se pisaban: los dos bajaban a la vez y las subidas de adapta no hacían nada por encima de los topes del
guardián (que adapta no veía) aunque las apuntaba como aprendidas. Decidido:

| | Qué |
|---|---|
| Tope | Uno (`AdTope`, el de `AutoPresetTick` con algo menos con Smooth Motion) para los dos |
| Papeles | El guardián es el **freno de emergencia** (de golpe); adapta, el **ajuste fino** (despacio, en los dos sentidos) |
| Quién baja | Muy por encima (> 1,6 x tope) y con el guardián aún pudiendo bajar (más de 1 pasada o más del 50 %): solo él (`AdGuardianDelante`); si no, adapta |
| Estado | Con `adapta=1`, lo que baja el guardián no queda en sus topes sino en el de adapta (`AdaptaFreno`): `s.NrPasses` / `s.NrPct`, la fila aprendida (pasadas, %, y el % que no cupo como techo `no_pct`) y 8 s sin juzgar (ni `AutoPresetTick` ese segundo) |
| Sin oscilar | Cada cambio recrea DLSS 5: el guardián no juzga en 5 s y adapta en 8 s; adapta sube +5 cada 20 s con lo previsto por debajo del 85 % del tope y nunca hasta el `no_pct` |
| Sin adapta | (`escalas_adaptables=0`) el guardián como en Prisma |

Probado (20-11, fuente de prueba, 2 pasadas al 100 % en el 60): el guardián bajó 2 → 1 pasada y 100 → 75 → 50 % sin que adapta bajara a
la vez, todo quedó en `escalas_motor.txt` y la vez siguiente se empezó por ahí y adapta subió 50 → 55 → 60 % cada ~27 s sin llegar al 75.

- Qué: ms de GPU por imagen real de cada pieza **en esta gráfica, driver, resolución de captura y de salida**: `base` (descomprimir +
  bordes + CAS + copia), `mv` (vectores), `dlss_E/K/L/M` o `fsr1`, `fg_dlssg`, `fg_smooth`; y si hay DLSS 5, la mayor escala de
  entrada que cabe con 1/2/3 pasadas en cada modo.
- Cómo: el motor ya tiene `bench=N bench_warm=N bench_out=` y la línea `pasos=` (GPU de cada paso); se arranca **una vez** con la
  imagen de prueba a la resolución de la captura (`test=<png>`), y se cambian modelo y generación en caliente por la tubería
  (`set preset=L`, `set fg=1`) midiendo 4-5 s cada uno: ~30 s. DLSS 5 con el banco de Magpie de hoy (`banco_s`), ~20 s por punto.
- Margen: se elige lo que deja el 25 % libre (la captura real cuesta 7-15 puntos más que la de prueba, medido el 16-11).
- Se guarda en `calibracion.txt` (clave = gráfica + driver UMD + captura + salida). Se repite si cambia la clave, y se puede pedir
  desde la bandeja. Sin banco (aún no hecho o falló): la estimación de `Plan.cs`.
- Las tablas de hoy (`ModelTable`, `Dlss5PctTable`, `EnginePreset*`, `LightTable`...) pasan a ser **salidas del banco** con los mismos
  nombres de clave en el ini, para que se puedan seguir ajustando a mano.

## 10. Poca VRAM
| VRAM | Qué cambia |
|---|---|
| < 4 GB | captura ≤ 1080p, sin generación, sin motor en espera, modelos E/FSR 1 |
| 4-6 GB | sin DLSS 5, sin motor en espera (se lleva 1-2 GB), DLSS-G "justo" (~1,1 GB a 4K) |
| 6-8 GB | DLSS 5 con 1 pasada (si está la DLL), sin motor en espera por debajo de 8 |
| ≥ 8 GB | todo (lo de este PC) |
Además, el presupuesto de VRAM de DXGI (`QueryVideoMemoryInfo`) se mira al arrancar: si otro programa la está usando, el motor baja
de modelo y lo dice.

## 11. Idiomas
Español e inglés. Textos de pantalla en `idiomas\es.txt` / `en.txt` (`clave=texto`), con `Tx.T(...)` mientras tanto. Log en el idioma
elegido (los mensajes de hoy en español se quedan como claves). El OCR del HOME (Switch 2) usa el idioma de la consola (español,
inglés...): si Windows no tiene ese paquete de OCR, el asistente lo dice (sin OCR se reconocen los juegos solo por el icono).

## 12. Empaquetado y licencias
ZIP portable (descomprimir y abrir) + instalador opcional (mismo contenido en Program Files, datos en `%LOCALAPPDATA%`).
Requisitos: Windows 10 2004 (19041) o posterior, .NET Framework 4.8 (viene con Windows), gráfica con Direct3D 12 nivel 12_0.

Firmas comprobadas el 18-11 con `Get-AuthenticodeSignature`:
| Va en el paquete | Firma | Licencia / condición |
|---|---|---|
| ConsoleEnhancer.exe, motor | (nuestra; firmar con certificado propio si se puede) | la nuestra (el código de las dos apps es del usuario) |
| `sl.interposer/common/dlss/dlss_g/reflex/pcl.dll` (Streamline 2.14.16) | NVIDIA, válida | Streamline: MIT (`license.txt`); los plugins y `nvngx_dlss.dll` / `nvngx_dlssg.dll`: NVIDIA RTX SDK / DLSS: redistribuibles dentro de una aplicación, con su aviso (`NVIDIA-DLSS-LICENSE.txt`) |
| mpv.exe (+ `d3dcompiler_43.dll`, Microsoft, válida) | sin firma | GPL-3 / LGPL (ffmpeg dentro): ofrecer el código fuente exacto (enlace a la versión y su compilación) |
| Magpie Experimental 0.6.12 (`Magpie.exe`, `TouchHelper.exe`, `Updater.exe`, `Magpie.RtxVideo.dll`) | sin firma | GPL-3 (`LICENSE-Magpie.txt`, `THIRD-PARTY-NOTICES.md`, `build-manifest.json`): código fuente de esa versión y de la parte de XeSS FG adaptada de OptiScaler (GPL-3, `XESSFG-COMPATIBILITY-NOTICE.md`) |
| `amd_fidelityfx_loader/upscaler/framegeneration_dx12.dll` | AMD, válida | AMD FSR SDK (`AMD-FSR-SDK-LICENSE.md`, `-THIRD-PARTY.md`) |
| `ffx_fsr2_api_x64.dll`, `ffx_fsr2_api_dx11_x64.dll` | sin firma (compiladas del FSR 2 abierto) | MIT (`AMD-FSR2-DX11-LICENSE.txt`) |
| `libxess.dll`, `libxess_fg.dll`, `libxell.dll` | Intel, válida | Intel Simplified Software License (`INTEL-XESS-LICENSE.txt`, `-THIRD-PARTY.txt`) |
| `nvngx_vsr.dll`, `nvngx_truehdr.dll`, `nvngxruntime.dll` (RTX Video / RTX HDR) | NVIDIA, válida | RTX Video SDK (`NVIDIA-RTX-VIDEO-LICENSE.pdf`) |
| `Microsoft.UI.Xaml.dll` | Microsoft, válida | MIT (WinUI 2) |
| **Paquete aparte opcional "extras NVIDIA VFX"**: `nvVFX*.dll`, `NVVideoEffects.dll`, `NVCVImage.dll`, `npp*64_12.dll`, `cudart64_12.dll` (~480 MB; VFX Upscale, VFG, TrueHDR de VFX) | NVIDIA, válida | NVIDIA VFX SDK (`NVIDIA-VFX-Licenses\`) y CUDA redistributables. Por tamaño, no en el ZIP base |

**No va en el paquete** (y la app funciona entera sin ellos):
| Archivo | Por qué |
|---|---|
| `magpie\nvngx_dlssnr.dll` ("DLSS 5", 310.9.16) | DLL de NVIDIA **filtrada y parcheada** por la comunidad, **sin firma**, sin SDK ni licencia; Magpie la carga saltándose la comprobación de NGX. Solo si el usuario la pone en `extras\` (la app la detecta y avisa) |
| `magpie\runtimes\dlssg_sm86\` (DLSS-G en RTX 30) | runtime de la comunidad (MIT de su autor) con DLL **autofirmadas** ("DLSSG for SM86 (self-signed)") que enganchan el DLSS-G de NVIDIA; el `nvngx_dlssg.dll` que lleva sí es de NVIDIA. Mismo trato: extra del usuario |
| `DLSSNR-DLL-Options.zip`, `NGX_OTA_Switch.bat` | de lo mismo |
| Joyxoff, Streaming Center, MSI Afterburner, Playnite, PresentMon, Python/MSVC de `deps\` | programas del usuario o herramientas de desarrollo |
| `juegos\` de este PC (perfiles, iconos, capturas), `switch2.ini` de este PC, logs | datos del usuario (el asistente crea los suyos) |

Avisos legales en el asistente y en `licencias\LEEME.txt`: marcas (NVIDIA DLSS/Reflex/G-SYNC, AMD FSR/AFMF, Intel XeSS, Nintendo,
Sony, Microsoft) solo para describir compatibilidad; la app no es de ninguna de ellas.

## 13. Lo que queda abierto
- Smooth Motion se pone con una función **privada** de NVAPI (`SetSettingEx`, ids sacados del driver 616.92): puede cambiar con otro
  driver. La detección ya dice si el driver lista el ajuste (en 616.92 no lo lista: es oculto) y la app debe tratar el fallo como hoy
  (`SmoothBroken` → DLSS-G). Umbrales de driver de Smooth Motion en RTX 40 (590+) y de DLSS 4.5 (L/M) son aproximados.
- Perfiles de NVIDIA por nombre de exe: `Magpie.exe` y `mpv.exe` coinciden con los del usuario si los tiene instalados aparte. mpv se
  puede renombrar (`ce-mpv.exe`); Magpie no conviene: el perfil se llamará "ConsoleEnhancer - Magpie.exe" y se avisa.
- AFMF y Anti-Lag no se pueden encender desde fuera: dependen de que el usuario los active en AMD Software.
- FSR 1 (EASU + RCAS) en el motor está por escribir (shader de AMD, MIT): fase 2.
- Captura MJPEG en el motor (descompresor de DirectShow) y P010 (HDR): fase 2 / 3.
- Detección del HOME y de juegos para consolas que no son Switch 2: agente CONSOLAS.
- Sin probar en un PC de verdad sin NVIDIA / con AMD / con Intel / con una UVC: solo simulado (lo que cambia el código: NVAPI no se
  carga, D3D12 y DXGI genéricos, DirectShow genérico).
