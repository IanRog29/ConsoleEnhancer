# Cambios / Changelog

Formato: [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Versiones: [SemVer](https://semver.org/lang/es/). Cada versión
publicada es una etiqueta `vX.Y.Z` de git; GitHub Actions monta su zip y copia aquí sus notas en la Release.

## [Sin publicar]

## [1.0.4] - 2026-11-20

### Arreglado
- **DLSS 5 del motor propio respeta los cortes de escena**: el aviso de corte (cambio de plano, cargas, menús que entran de golpe)
  llegaba a DLSS SR y a la generación, pero no a DLSS 5 ni a su antiparpadeo, que mezclaban la escena anterior con la nueva durante
  unas imágenes (estela o fantasma tras cada corte). *DLSS 5 in the built-in engine now resets its history on scene cuts (no more
  ghosting of the previous shot).*

### Añadido
- **Generación DLSS con detección de menús** (`fg_menus=1` en `motor_extra`, de serie): sobre un menú a pantalla completa NVIDIA deja
  de inventar imágenes y las letras no se deforman. Sin coste medible (banco 4K y 1440p, modo 30 + generación + DLSS 5: misma
  gráfica, 2 imágenes presentadas por cada real). Quitarlo de `motor_extra` vuelve a lo de antes. *DLSS frame generation now uses
  NVIDIA fullscreen menu detection (fewer warped letters on pause menus).*

## [1.0.3] - 2026-11-20

### Arreglado
- **DLSS 5 en el motor propio ya no parpadea al mover la cámara** en zonas lisas o con mucha luz (cielo, paredes claras). Con la captura
  a 1440p o 4K el motor calculaba los vectores de movimiento en su modo ligero y lo liso se quedaba quieto en los paneos: DLSS 5 cogía la
  imagen anterior del sitio equivocado. Ahora, con DLSS 5 en el modo 30, los vectores van completos (+~0,7 ms de gráfica a 4K). Medido con
  la prueba de paralaje: temblor 1,55 → 0,42, casi igual que sin DLSS 5. `mv_ligero_dlss5=1` vuelve a lo de antes. *DLSS 5 in the
  built-in engine no longer flickers on camera pans over flat or bright areas.*
- **DLSS 5 + generación DLSS a la vez sin errores en la imagen**: la generación sigue con los vectores de siempre (los que le van bien
  sola) y solo DLSS 5 recibe los completos (una segunda pasada del nivel fino; ~1 ms de gráfica en total a 4K). *DLSS 5 together with
  DLSS frame generation no longer causes image errors.*

## [1.0.2] - 2026-11-20

### Añadido
- **Modo 120 fps (esports)**: para los juegos de 120 Hz, captura a 120 sin generación de fotogramas y con el menor retraso. El botón de
  modo pasa 30 → 60 → 120 solo si la consola saca 120, la tele va a 120 Hz o más y la captura de ahora es una que tu capturadora lista a
  120 (normalmente 1080p / 1440p; el asistente lo lee de la capturadora: `capturas_120`). A 4K sigue 30 ↔ 60. `permitir_120=auto`.
  *120 fps esports mode (1080p / 1440p capture and a 120 Hz TV).*

### Arreglado
- **Falso aviso de HDCP**: solo salta con la imagen negra de verdad y la consola en silencio 20 s seguidos (antes, escenas oscuras o
  fundidos lo hacían saltar). `hdcp_aviso`, `hdcp_aviso_s`. *No more false HDCP warnings.*
- **Ventana de la primera descarga** (mpv y Magpie): ya no corta el texto, en español ni en inglés. *The first-run download window no
  longer cuts off text.*
- Recordatorio de la 1.0.1: la PS5 (y, preliminar, la Xbox) reconocen cada juego y le guardan su perfil; además, su menú siempre va a
  4K nativo aunque un ini viejo traiga `home_4k_nativo=0` (si no, no se podía reconocer el juego).

## [1.0.1] - 2026-11-20

### Añadido
- **PS5: perfil por juego.** Cada juego se reconoce por su nombre en el centro de control o el conmutador de juegos (botón PS) y se
  le guarda su perfil (captura, DLSS 5, escalador, modo 30/60, tipo de generación), como en la Switch 2. *PS5: per-game profiles.*
- **Xbox Series: perfil por juego (preliminar, sin probar con una Xbox de verdad)**, por la Guía y las fichas de Inicio.
- Si no se reconoce el juego, el perfil único de la consola, como en la 1.0.0. Claves `ps5_reconocer_juegos=1` y
  `xbox_reconocer_juegos=1` (0 = un solo perfil). Los perfiles van marcados por consola ("PS5: <juego>"): no se mezclan entre consolas.

### Cambiado
- El enlace de apoyo (Ko-fi) es https://ko-fi.com/ianrog29 y el repositorio, IanRog29/ConsoleEnhancer.

## [1.0.0] - 2026-11-20

Primera versión pública. *First public release.*

**Tu consola en la tele, mejor que nunca, sin tocar el teclado.** ConsoleEnhancer convierte el PC del salón en la "caja" entre tu
consola y tu tele: enciendes la consola y la app se abre sola, con la imagen **reescalada a 4K** (DLSS, FSR, XeSS, RTX Video),
**generación de fotogramas** si tu gráfica puede, **HDR** y **VRR**, y el **menor retraso posible**. Todo con el **mando** desde el sofá;
al apagar la consola se cierra sola y, si quieres, apaga el PC. Un asistente lo detecta todo (gráfica, tele, capturadora, sonido, mando)
y adapta las escalas a tu PC. Gratis, libre (GPL-3.0), sin anuncios ni telemetría. Creado y mantenido por IanRog29.

*Your console on the TV, better than ever, without touching a keyboard: the living-room PC becomes the box between console and TV. It
opens when you turn the console on, upscales to 4K with frame generation, HDR and VRR at low latency, is driven with the controller,
closes (and can shut down the PC) when the console turns off, and a wizard detects everything and tunes it for your PC.*

**Inicio rápido / Quick start**
1. Baja `ConsoleEnhancer-v1.0.0.zip` (abajo, en *Assets*). / Download `ConsoleEnhancer-v1.0.0.zip` (below, under *Assets*).
2. Descomprímelo en una carpeta tuya (no en *Archivos de programa*). / Unzip it into one of your own folders (not *Program Files*).
3. Abre `ConsoleEnhancer.exe` y sigue el asistente. / Open `ConsoleEnhancer.exe` and follow the wizard.

ConsoleEnhancer es gratis y completo; si quieres apoyarlo (opcional): https://ko-fi.com/ianrog29 · *Free and complete; optional
support:* https://ko-fi.com/ianrog29

### Añadido
- Asistente del primer arranque en 9 pasos: idioma, detección de gráfica, pantallas, capturadoras, audio y mandos, *Probar imagen* y
  *Probar sonido*, consola, pantalla, imagen (HDR y VRR), mando y plan de imagen según la gráfica (23 PCs simulados en las pruebas).
- Motor de imagen propio (Direct3D 12): captura DirectShow, NVIDIA DLSS SR, DLSS Frame Generation y Reflex por NVIDIA Streamline; FSR 1
  (EASU + RCAS) en gráficas sin DLSS. mpv + Magpie Experimental cuando hacen falta (FSR 3 FG, XeSS FG, RTX Video).
- Escalas que se adaptan a cada PC (`escalas_adaptables`): estimación por la gráfica y la pantalla, ajuste en marcha con el tiempo de
  gráfica medido (motor propio) o entre partidas (mpv + Magpie), recordado por gráfica, driver y modo. Con "DLSS 5" en el motor, un
  freno de emergencia (`dlss5_guardian`) que comparte el mismo estado aprendido.
- La imagen parada con la consola encendida (gráfica atascada o reiniciada, motor reconfigurándose) ya no cierra la app: se distingue de
  la consola apagada y se vuelve a abrir la imagen (`apagado_confirmar`, `cuelgue_relanzar_ms`); no apaga el PC si la gráfica falló en
  el último minuto (`apagar_pc_confirmar`). Fallos en hilos secundarios al log sin cerrar la app (`ConsoleEnhancer.exe.config`).
- HDR: RTX HDR (NVIDIA) o Auto HDR de Windows, apagados de serie. VRR automático (`vrr=auto`) con G-SYNC / FreeSync / HDMI VRR; con él,
  el motor propio presenta cada imagen al acabarla (solo en pantallas que dicen tener VRR; `motor_vrr_fg_vsync` con la generación).
  En el modo 60 con DLSS FG y una pantalla de 120 Hz con G-SYNC, refresco fijo solo en el perfil de NVIDIA del motor mientras se juega
  así (`motor_60fg_refresco_fijo=auto`): sin el tope de ~116/s de Reflex con G-SYNC, todas las imágenes de la consola y menos retraso;
  se quita al salir o, si la app se cae, al volver a abrirla.
- Selector del tipo de generación de fotogramas (L3 o Ctrl+G), recordado por juego.
- Menú de la consola (Switch 2, PS5, Xbox) a 4K nativo sin generación ni "DLSS 5" (`motor_dlss5_home=0`).
- `consola=auto` no toma una consola por otra por una sola pista (p. ej. la hora de la PS5 por la guía de la Xbox): hacen falta pruebas
  propias de la nueva repetidas en el tiempo; con una consola aún sin probar, apagado prudente (sin cierre por negro y silencio).
- Mandos sin programas aparte (XInput, DualSense / DualShock 4, Switch / Switch 2, 8BitDo, GameSir, HID) y modo PC. En cualquier mando,
  el botón de menú alterna el modo 30 / 60 y el de vista / compartir / − la generación de fotogramas (`mando_botones=ce`).
- Iniciar con Windows con la pantalla en negro hasta que hay imagen; escritorio en negro opcional; apagar el PC al apagar la consola.
- Portable (`portable.txt`) o datos en `%LOCALAPPDATA%\ConsoleEnhancer`. Interfaz en español e inglés.
- La primera vez baja mpv y Magpie de sus releases oficiales (SHA-256 comprobado; sin internet, con los zip en `descargas\`).
- *Acerca de y licencias* en el menú de la bandeja.
- Bandeja > *Componentes: comprobar / volver a descargar*: estado de mpv y Magpie y volver a bajarlos de sus páginas oficiales
  (`--componentes-reparar`); el último paso del asistente dice si están y cómo repararlos.
- *Añadir DLSS 5 (tu propio archivo)…* en la bandeja y en el último paso del asistente (GeForce RTX): el usuario elige su
  `nvngx_dlssnr.dll` (y, en RTX 30, su carpeta `dlssg_sm86`); se comprueba que es una DLL x64 con el nombre esperado, se avisa de que
  no es oficial ni la soporta NVIDIA, se copia a `extras\` y se puede encender ya. *Quitar DLSS 5* la borra. Sin enlaces ni descargas.
- *Apoyar el proyecto (Ko-fi)* en la bandeja, una línea en *Acerca de* y un enlace discreto al final del asistente (voluntario).

### Legal
- GPL-3.0 con un permiso adicional de la sección 7 para las librerías de NVIDIA, AMD, Intel y Microsoft (`LICENSE-EXCEPTION.txt`).
- El zip lleva solo la app y las DLL de NVIDIA Streamline / DLSS / Reflex sin cambios, con sus licencias y `licencias\NVIDIA-AVISO.txt`.
  mpv y Magpie no se reparten (los baja la app). Nada no oficial ("DLSS 5", `dlssg_sm86`).
- Autor: IanRog29 y colaboradores de ConsoleEnhancer (`LICENSE-EXCEPTION.txt`, *Acerca de*). Donaciones por Ko-fi, voluntarias.

[Sin publicar]: ../../compare/v1.0.2...HEAD
[1.0.2]: ../../releases/tag/v1.0.2
[1.0.1]: ../../releases/tag/v1.0.1
[1.0.0]: ../../releases/tag/v1.0.0
