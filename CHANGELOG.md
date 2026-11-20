# Cambios / Changelog

Formato: [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Versiones: [SemVer](https://semver.org/lang/es/). Cada versión
publicada es una etiqueta `vX.Y.Z` de git; GitHub Actions monta su zip y copia aquí sus notas en la Release.

## [Sin publicar]

## [1.0.0] - 2026-11-20

Primera versión pública. *First public release.*

**Tu consola en la tele, mejor que nunca, sin tocar el teclado.** ConsoleEnhancer convierte el PC del salón en la "caja" entre tu
consola y tu tele: enciendes la consola y la app se abre sola, con la imagen **reescalada a 4K** (DLSS, FSR, XeSS, RTX Video),
**generación de fotogramas** si tu gráfica puede, **HDR** y **VRR**, y el **menor retraso posible**. Todo con el **mando** desde el sofá;
al apagar la consola se cierra sola y, si quieres, apaga el PC. Un asistente lo detecta todo (gráfica, tele, capturadora, sonido, mando)
y adapta las escalas a tu PC. Gratis, libre (GPL-3.0), sin anuncios ni telemetría. Creado y mantenido por ianu29.

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
- Autor: ianu29 y colaboradores de ConsoleEnhancer (`LICENSE-EXCEPTION.txt`, *Acerca de*). Donaciones por Ko-fi, voluntarias.

[Sin publicar]: ../../compare/v1.0.0...HEAD
[1.0.0]: ../../releases/tag/v1.0.0
