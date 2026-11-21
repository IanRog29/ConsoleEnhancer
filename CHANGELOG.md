# Cambios / Changelog

Formato: [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Versiones: [SemVer](https://semver.org/lang/es/). Cada versión
publicada es una etiqueta `vX.Y.Z` de git; GitHub Actions monta su zip y copia aquí sus notas en la Release.

## [Sin publicar]

## [1.2.0] - 2026-11-21

### Added / Añadido
- **`Ajustes de ConsoleEnhancer.exe`** next to the app: one window with everything you change outside the picture, with the app open
  or closed: open the app or the setup wizard, how the PC is used (like a console / like a normal PC), start with Windows, black
  desktop, what the PC does when the console turns off (with the power explanation), DLSS 5 (status, add/change/remove your own file,
  open `extras\`), download mpv/Magpie again, test the shutdown notice or standby, open the logs and your settings, and **uninstall**.
  If the app is open, it offers to restart it to apply the changes. It replaces `Inicio y apagado.bat`.
  *`Ajustes de ConsoleEnhancer.exe` junto a la app: todo lo que se cambia fuera de la imagen y desinstalar, en una ventana (sustituye a
  `Inicio y apagado.bat`).*
- **The settings menu (Guide button) updates itself** while open when something changes elsewhere (controller buttons, keyboard
  shortcuts, the tray, another game's profile, an Apply that finishes, the low-fps helper). Changes you made in the menu and haven't
  applied are kept. *El menú de ajustes se actualiza solo con lo de ahora mientras está abierto.*
- **Settings menu (Guide button): "DLSS 5 · Not installed"** on NVIDIA when `nvngx_dlssnr.dll` is missing, with the folder to put
  your own file in (`extras\` next to the app) or the tray option to add it. A file dropped there is picked up the next time the menu
  opens. *Menú de ajustes: "DLSS 5 · No instalado" y la carpeta donde poner tu archivo (`extras\`).*

### Fixed / Arreglado
- **DLSS FG with DLSS 5 no longer leaves the picture stuck**: with DLSS 5 on, frame generation runs in Magpie, whose DLSS FG needs the
  community `runtimes\dlssg_sm86` folder (not included). Without it Magpie failed again and again ("Frame generation did not start").
  Now, in that case, DLSS FG is not offered and the app uses XeSS FG (or Smooth Motion / FSR 3 FG) instead; with DLSS 5 off, the
  app's own engine keeps DLSS FG. *Con DLSS 5 y sin `dlssg_sm86`, ya no se elige DLSS FG (Magpie no arrancaba): va XeSS FG.*
- **No more administrator prompt (UAC) from Magpie** when switching to XeSS FG (or anything that opens Magpie) for the first time:
  with a new config, the bundled Magpie 0.6.12 "migrated" it to *always run as administrator* and restarted elevated. The app now
  writes the marks that tell Magpie the migration is done. *Ya no pide permisos de administrador al pasar a XeSS FG.*
- **The settings menu now disappears at once when you go to the console menu** (it stayed ~5 s because the frozen "curtain" picture
  of the screen was taken with the menu on top). *El menú de ajustes se quita en cuanto se va al menú de la consola.*

## [1.1.1] - 2026-11-21

### Added / Añadido
- **Tray > "How the PC is used"**: *Like a console* (opens with Windows, black desktop, standby when the console turns off) or *Like a
  normal PC* (regular Windows: none of that; the app only opens when you open it), each with an explanation; switch any time.
  *Bandeja > "Cómo se usa el PC": como una consola o como un PC normal (Windows de siempre).*
- **Tray > "Uninstall ConsoleEnhancer…"**: removes the start-with-Windows task, restores the normal desktop and power plan, and
  deletes only the app's own files (anything else in its folder stays; folder links are removed, never their contents). Your
  settings and game profiles can be kept (portable copies keep them in %LOCALAPPDATA%\ConsoleEnhancer for a later install).
  *Bandeja > "Desinstalar ConsoleEnhancer…": deja Windows como estaba y borra solo los archivos de la app; tus ajustes y perfiles, si quieres, se guardan.*

## [1.1.0] - 2026-11-21

### Added / Añadido
- **The PC behaves like a console in standby** when the console is turned off (with "Shut down the PC when the console is turned
  off"): with no countdown, the screen goes off, Windows switches to its Power saver plan and almost nothing runs (a tiny watcher,
  under 1% CPU; the graphics card idles at ~12 W on an RTX 4060 Ti); turning the console on (seen by the capture card), turning on a controller
  or pressing a key brings the screen back and reopens ConsoleEnhancer. `apagar_pc_modo=espera` (default) | `suspender` (real
  sleep, only keyboards/controllers Windows allows can wake it) | `apagar` (as before). Tray: "When the console is turned off, the
  PC" (with "How does each one work and how much power?") and the last step of the setup wizard. If it wakes up and the console is not
  turned on within 90 s (`espera_volver_sin_consola_s`), it goes back to standby. `Inicio y apagado.bat` options 6 and 7 (test it).
  *El PC hace como una consola en reposo al apagar la consola: pantalla apagada y casi sin gasto; al encender la consola, un mando o
  pulsar una tecla vuelve la pantalla y se abre ConsoleEnhancer. `apagar_pc_modo=espera` (de serie) | `suspender` | `apagar`.*

## [1.0.13] - 2026-11-21

### Arreglado
- **"Apagar el PC al apagar la consola" no apagaba el PC**: el aviso de 15 s salía, pero se cancelaba solo a los 2 s sin tocar
  nada. Lo cancelaba cualquier entrada que Windows apunta como del usuario, también la que inyectan otros programas (Joyxoff,
  mapeadores del mando, Steam Input). Ahora solo lo cancela un botón del mando, una tecla o un clic de verdad, o mover el ratón de
  verdad (más de 40 px); el log apunta cuántas entradas inyectadas no se contaron.
  *Shutting down the PC with the console: the warning was cancelled by injected input from other programs.*

### Añadido
- **`Inicio y apagado.bat`** junto a `ConsoleEnhancer.exe`: cambia sin abrir la app el inicio con Windows, apagar el PC al apagar la
  consola, los segundos del aviso y el escritorio negro, y prueba el aviso de apagado sin apagar nada.
  *`Inicio y apagado.bat`: change start with Windows / PC shutdown settings without opening the app.*

## [1.0.12] - 2026-11-21

### Arreglado
- **Cambiar a una generación que hace Magpie (XeSS FG, FSR 3 FG) dejaba la app sin imagen** desde la 1.0.11 ("No se pudo lanzar
  mpv"): Magpie se abría solo (con un aviso de administrador) y el botón central ya no abría el menú. Lo causaba la comprobación del
  cierre desde la barra de tareas. *Switching to a Magpie frame generation broke the image in 1.0.11.*
- **Atajos de teclado que dejaban de funcionar** (Ctrl+O, Ctrl+G, Ctrl+F...) y la tecla Windows rara: Windows se quedaba con Alt
  "pulsado" porque el bloqueo de Joyxoff se tragaba el Alt que suelta el motor al recuperar el foco. Ya no se traga nunca un soltar
  tecla y las pulsaciones de la app van marcadas. Además, si Windows quita los ganchos de teclado por lentos, se vuelven a poner solos.
  *Keyboard shortcuts stopped working because Alt got stuck.*

- **El menú de ajustes se abría encima del menú de la consola** y nada más abrir la app, porque la app tomaba el menú de la PS5 por
  un juego: con un fondo claro junto a la hora (GTA VI) no la leía, y el OCR a veces la leía sin los dos puntos ("1232") o con ellos
  como un 1 ("12147"). Ahora el inicio de la PS5 se reconoce también por sus pestañas "Juegos / Contenido multimedia" (aunque una
  notificación las tape a medias), la hora sin separador vale en la PS5 y la Switch, y con `consola=auto` esas pantallas ya no se
  toman por la Xbox. En todas las consolas el menú de ajustes espera unos segundos al abrirse la imagen y al salir de su menú.
  Prueba de detección: 107 capturas, 0 fallos (antes, con las nuevas de GTA VI, 10 de PS5, 10 escenas y 16 de consola=auto).
  *Settings menu no longer opens over the console's home menu or right at startup.*

- **Entrar en la Store, Remote Play, la galería y otras apps de la consola aplicaba un perfil de juego** (y creaba juegos falsos como
  "PS5: STANDARD" o "PS5: Remote Play", con una "portada" que era una página de la Store). Ahora:
  - salir del menú pide un tiempo seguido sin verlo: 1,2 s desde el inicio de la consola (un juego se reconoce en ~1,8 s) y 2,5 s
    desde otras pantallas suyas (antes bastaban 2 capturas en medio segundo: la transición a la Store);
  - las apps de la consola en la fila de juegos (Remote Play, Galería, Store, PS Plus, Ajustes, Share Factory; en la Xbox Microsoft
    Store, Game Pass, Configuración, Capturas...; y Netflix, YouTube, Spotify, Twitch...) se tratan como el menú: 4K nativo, sin perfil
    ni generación, hasta volver al menú de la consola (o al juego de antes, si su imagen vuelve);
  - más frases de pantallas de la consola (la Store por dentro, la galería, Remote Play) y ninguna pantalla de la consola se guarda
    como portada de un juego.
  *Store, Remote Play, the media gallery and other console apps no longer count as entering a game.*

### Cambiado
- Menú de ajustes: **el escalador ofrece cada modelo de DLSS** (automático, M, L, K, J, E) además del alternativo; se guarda para
  todos los juegos. Fuera la fila del filtro de tramado. *Settings menu: pick the DLSS model; dither row removed.*

## [1.0.11] - 2026-11-21

### Nuevo
- **Menú de ajustes en pantalla con el botón central del mando** (Xbox / PS / HOME), también con Ctrl+O o desde la bandeja. Muestra
  todo lo que se puede cambiar (captura, modo 30/60/120, generación de fotogramas y su tipo, DLSS 5 con nivel y pasadas, escalador,
  filtro de tramado) con lo de ahora marcado; se cambia con la cruceta o el stick (o las flechas), lo cambiado queda señalado y
  **Aplicar** lo pone todo a la vez con una sola reapertura de la imagen. B o el botón central cierran sin cambiar nada. No le quita el
  foco a la imagen (la generación sigue). Si el botón Xbox abre también la Game Bar de Windows, apágalo en Configuración > Juegos.
  *On-screen settings menu on the controller's Guide/PS/HOME button (or Ctrl+O): change everything and apply it all at once.*

### Arreglado
- **Cerrar desde la barra de tareas** (o la X de la ventana) ahora cierra la app entera; antes la app lo tomaba por un corte de la
  señal y volvía a abrir la imagen. *Closing the window from the taskbar now quits the app.*
- **La tecla Windows** jugando: el menú Inicio se cerraba solo (la app recuperaba el foco para la generación). Ahora, con Inicio, la
  barra de tareas o Alt+Tab delante, el foco no se toca. *The Windows key / Start menu now works while playing.*

## [1.0.10] - 2026-11-21

### Nuevo
- **Todo lo del mando, también en el menú de la bandeja** (clic derecho en el icono), con lo puesto marcado: generación de fotogramas
  sí / no, tipo de generación, modo 30 / 60 / 120, captura 720p / 1080p / 1440p / 4K, DLSS 5 (sí / no, nivel, pasadas), escalador
  alternativo, filtro de tramado y "ver lo que va puesto". Para quien juega sin mando conectado al PC.
  *Everything the controller does is now in the tray icon's right-click menu too.*
- **Atajos de teclado** sobre la imagen de la consola: Ctrl+F generación sí / no, Ctrl+G tipo de generación, Ctrl+M modo 30/60/120,
  Ctrl+1…4 captura, Ctrl+R escalador, Ctrl+D DLSS 5, Ctrl+I lo que va puesto, Alt+F4 salir.
  *Keyboard shortcuts: Ctrl+F/G/M/1-4/R/D/I, Alt+F4 to quit.*

### Arreglado
- **DLSS FG en RTX 30 con la carpeta `dlssg_sm86`**: el plan no la ofrecía nunca (y sin FSR 3 la generación quedaba desactivada del
  todo). Ahora aparece en el selector (L3 / Ctrl+G / bandeja) y va por Magpie; apagada de serie, se enciende con Ctrl+F, el botón o la
  bandeja. Al actualizar, el ini se pone al día solo.
  *DLSS FG on RTX 30 with the community dlssg_sm86 runtime can now be selected.*

## [1.0.9] - 2026-11-21

### Nuevo
- **XeSS y FSR 3.1 / FSR 4 como escaladores del motor propio** (antes solo por mpv + Magpie, y con XeSS pedido el motor ponía FSR 1 sin
  decirlo). Usan las DLL de Intel y AMD que trae Magpie (`magpie\`). Medido en una RTX 4060 Ti, 1080p → 4K: XeSS 2.0.2 a 59,9
  imágenes/s con 5,5 ms de gráfica; FSR 3.1.5 a 59,9 con 5,2 ms. FSR 4 solo lo ofrece AMD en las RX 9000; en las demás va FSR 3.1.
  *XeSS and FSR 3.1/4 upscaling inside the own engine.*
- **FSR 3 FG (generación de fotogramas de AMD) en el motor propio**, para cualquier gráfica: la cadena de presentación de FidelityFX
  intercala las imágenes generadas. Medido con PresentMon: 59,9 reales → 120 presentadas por segundo. Con DLSS de escalador sigue
  yendo por Magpie (con FSR 3 FG el motor va sin Streamline). XeSS FG sigue por Magpie. *AMD FSR 3 frame generation in the engine.*
- **Escalado de serie sin DLSS**: GTX e Intel Arc → XeSS; AMD RX 6000 / 7000 / 9000 → FSR 4 (oficial en las RX 9000; en RX 6000 y
  7000 con el desbloqueo de FSR 4.1.1 del Magpie que trae la app, experimental). *Default upscaler without DLSS: XeSS / FSR 4.*
- **Xbox Series X|S: un perfil por juego**, como con la PS5: el nombre y el dibujo de la tarjeta en foco del Inicio (la grande con el
  borde de color) y la imagen de arranque de cada juego (fija en cada juego de Xbox, también al volver con Reanudación rápida).
  Probado con capturas oficiales del Inicio; **sin probar con una Xbox de verdad**. *Per-game profiles for Xbox Series X|S (untested on
  real hardware).*
- **Aviso y bajada automática si no se llega a los fps del modo** (motor propio): si 5 s seguidos no llega por culpa del PC (no del
  juego de la consola), sale un aviso y se baja un paso (DLSS 5, 120 → 60, captura más baja, 60 → 30 con generación), guardado en el
  perfil del juego. *Warns and steps settings down when the PC can't keep up.*
- **Motor de reserva**: jugando, la app deja preparado en segundo plano un motor con el otro tipo de generación del modo (Smooth
  Motion / DLSS FG); cambiar entre ellos pasa de 4-5 s a ~0,5 s. `motor_reserva=0` lo quita (ocupa ~0,9 GB de memoria gráfica).

### Arreglado
- **Perfiles por juego de la PS5**: el juego se reconoce por el icono en foco del Inicio y por su portada al arrancar (antes por textos
  del menú que el OCR leía mal, y con el foco movido justo antes de entrar se guardaba en el juego anterior). Volver al juego en marcha
  con el botón PS ya no cambia de perfil. *PS5 per-game profiles now follow the focused icon and the launch splash.*
- **Al cambiar el tipo de generación salía "Generación desactivada / activada" sin tocarla**: ahora dice el tipo que queda.
- **Sin aviso de "Sin señal" en los cortes cortos** (la PS5 cambiando a 120 Hz con un juego a 120): se queda la última imagen; si dura
  más de 5 s, el aviso de siempre. *No "no signal" text on short HDMI mode changes.*
- **No había modo 120 fps** con un ini de antes del 20-11: ahora se pone al día leyendo la capturadora (`capturas_120`).
- **XeSS FG no salía en el modo 60** con una NVIDIA: vuelve a estar en el selector (L3).


## [1.0.8] - 2026-11-21

### Arreglado
- **Los perfiles por juego se guardaban en un "juego" que no existe**: al encender la PS5, su pantalla de bienvenida ("Te damos la
  bienvenida de nuevo a PlayStation · ¿Quién está usando…?") se leía como el nombre de un juego, y todo lo jugado después se guardaba
  en ese perfil. Ahora las pantallas del sistema (bienvenida, elegir usuario, iniciar sesión, actualización, reposo) no cuentan como
  juego. *The PS5 welcome / user-select screen was taken as a game name, so every game's settings went into that one profile.*
- **La herencia de la versión anterior cogía datos de otra instalación**: ahora manda la carpeta `ConsoleEnhancer*` de al lado
  (`%LOCALAPPDATA%\ConsoleEnhancer` solo si no hay ninguna), y no copia las copias de seguridad (`.antes-*`).

### Mejorado
- **El primer arranque de una versión nueva, mucho más rápido**: bajaba otra vez mpv y Magpie (~40 s) y compilaba todos los shaders
  del motor (~18 s). Ahora se traen de la versión anterior (solo se compila lo que ha cambiado y solo se baja lo que es de otra
  versión). Medido: de ~1 min a 11 s hasta el motor listo. *First start of a new version reuses mpv, Magpie and the compiled shaders
  from the previous one (~1 min → 11 s).*
- **Selector de la generación (L3)**: ya no tiene "Sin generación" (se apaga con el botón de captura, como siempre) y sus textos se
  leen bien: "Generación: DLSS FG", la lista con el elegido entre « » (antes unas flechas que la fuente no tenía y salían como
  cuadros, el tipo repetido y el final cortado) y DLSS FG con el mismo nombre en todas partes. *L3 frame-generation selector: no
  "off" entry, fixed garbled text.*

## [1.0.7] - 2026-11-21

### Arreglado
- **Los colores con capturadoras YUY2 (y NV12) ya no salen en escalones**: el motor reconstruía el color de cada par de puntos con
  el mismo valor (vecino más cercano), y los bordes de color (sobre todo el rojo, que casi no lleva brillo) salían pixelados. Ahora el
  color de los puntos intermedios se interpola con 4 tomas y, donde hay un borde de brillo, del lado al que se parece (`croma=2` de
  serie en `motor_extra`; `croma=1` solo la interpolación, `croma=0` como antes). Medido con una imagen con bordes rojos y el color
  como lo manda el HDMI: error en los bordes de color 27,4 → 18,5 y medio 4,4 → 2,3, con el mismo tiempo de gráfica. *YUY2/NV12
  capture: chroma is now properly upsampled (4-tap, luma-guided at edges) instead of nearest-neighbour; red edges are no longer
  blocky. `croma=0` restores the old behaviour.*
- **Los perfiles por juego se pierden al actualizar**: con el zip portable cada versión empezaba en su propia carpeta de datos, sin el
  ini (otra vez el asistente), sin los perfiles de cada juego y sin la consola reconocida (sin ella, un solo perfil "Consola" hasta ver
  el menú de la PS5). Ahora, la primera vez que se abre una versión nueva, se copian los datos de la anterior (las carpetas
  `ConsoleEnhancer*` de al lado o `%LOCALAPPDATA%\ConsoleEnhancer`; se apunta en `datos\heredado.txt`). *Per-game profiles, the
  detected console and settings are now carried over from the previous version's folder.*
- **Sin generación DLSS en RTX 40**: el plan ponía Smooth Motion de serie en las RTX 40 y quitaba DLSS FG del modo 60. Ahora DLSS FG
  es la de serie en los dos modos y Smooth Motion queda como alternativa en el selector (L3). *DLSS frame generation is the default
  on RTX 40 again (Smooth Motion stays selectable).*
- **Los ajustes generales nuevos no llegaban a quien actualizaba**: `base.ini` solo se escribía en el ini con el asistente (lo que
  ponía la 1.0.6 sobre los halos no se aplicaba a quien venía de la 1.0.5). Ahora, una vez por versión, la parte general del ini se
  cambia por la nueva y la generación se vuelve a elegir para este PC; lo del asistente se queda (copia en `.antes-1.0.7`).
  *Settings from new versions are now applied on update (backup kept).*

### Mejorado
- **Generación DLSS en los giros sin halos**: la generación recibe un solo vector, el del giro de la cámara (`fg_vectores=3`), y el
  giro vuelve a encenderse (un giro rápido no se toma por un corte de escena). Los vectores por bloque daban halos alrededor del
  personaje en GTA. *Frame generation gets a single camera-pan vector: smoother fast turns without halos.*

## [1.0.6] - 2026-11-21

### Arreglado
- **Halos en las imágenes que inventa la generación DLSS desde la 1.0.5** (a 4K y a 1440p, sobre todo alrededor del personaje y de
  lo que se mueve distinto que la cámara). Los causaban dos cambios de la 1.0.5, cada uno por su cuenta: el giro de la cámara como
  candidato de los vectores (`corte_giro`, `mvglobal`, `mvlargo_grueso`) y los vectores completos por punto para la generación
  (`fg_vectores_completos`). Quedan apagados de serie en base.ini mientras se arreglan; a cambio, en los giros muy rápidos la
  generación vuelve a ir como en la 1.0.4. Se pueden encender en `motor_extra` (`fg_vectores_completos=1`, `corte_giro=1 mvglobal=1
  mvlargo_grueso=1`). El motor ya trae además `fg_relleno` (lo liso con vector 0 sigue a lo de alrededor, solo con los vectores
  completos). *Halos in DLSS frame generation since 1.0.5: camera-turn motion candidates and full per-pixel vectors for frame
  generation are off by default until fixed (very fast turns behave as in 1.0.4 again).*
- **Sonido doble (eco) con la AVerMedia GC573 y otras capturadoras**: el sonido de la consola se oía dos veces cuando otro camino lo
  reproducía a la vez que la app. Ahora, mientras suena por la app: se quita *Escuchar este dispositivo* de Windows en la entrada de la
  capturadora (se devuelve al salir, también tras un cierre de golpe), se cierra el audio de una sesión anterior que siguiera sonando
  (y el nuevo se cierra con la app aunque esta muera) y se avisa en pantalla si RECentral, OBS, Streamlabs o XSplit también suenan.
  `sonido_doble=0` lo deja como antes. *Double audio (echo) with the GC573 and other capture cards: Windows "Listen to this device"
  is turned off on the capture input while the app plays it (restored on exit), leftover audio players are closed and other capture
  apps playing sound are reported.*

## [1.0.5] - 2026-11-20

### Arreglado
- **Giros rápidos de cámara con generación DLSS**: al girar deprisa parecía ir a 30 fps en vez de 30 + generación. Los vectores de
  movimiento propios no llegaban a más de ~96 px por imagen (un giro rápido a 4K mueve 150-400 px) y el giro se tomaba a veces por un
  corte de escena, tras el cual la generación no inventa la imagen intermedia. Ahora el motor estima el giro de la cámara y lo usa
  como candidato en la búsqueda (paneo de 240 px a 4K: 91,8 % de vectores bien, antes 0 %), y un giro ya no es un corte (cortes de
  verdad, menús y cargas se siguen viendo). *Fast camera turns no longer drop to 30 fps with DLSS frame generation: camera-pan
  estimation for the motion vectors and no more false scene cuts while turning.*
- **DLSS 4.5 (modelos L/M) al reescalar**: con captura de 1440p DLSS SR recibía los vectores "ligeros" y dejaba rastro y temblor
  al mover la cámara. Ahora recibe los completos en todos los modos (temblor de bordes 2,52 → 0,97 con L), con una guarda que vuelve a
  los ligeros si no caben antes de bajar el modelo. *DLSS 4.5 super resolution gets full motion vectors at 1440p (less trailing
  and shimmer in motion).*
- **DLSS 5 a 4K nativo sin generación**: no se calculaban vectores (nadie más los usaba) y DLSS 5 y su antiparpadeo iban con
  vectores a cero en movimiento. Ahora se calculan en el modo 30 (`mv_nativo_dlss5=1`).

### Mejorado
- **Menos fallos en las imágenes que inventa la generación DLSS con DLSS 5** (`fg_vectores_completos=1` en `motor_extra`, de serie
  en base.ini): la generación recibe los vectores completos por punto. Medido contra la interpolación ideal: -35 % de error
  (interfaz arrastrada -73 %, bordes dobles -53 %) y ~1 ms menos de gráfica. *Fewer frame-generation artifacts with DLSS 5.*
- **Latencia**: la captura se copia y sube a la gráfica por bandas, solapadas con la descompresión (`copia_bandas=4`,
  `bandas_grafica=1`), y la mezcla de DLSS 5 escribe directamente en su destino. 4K + generación + DLSS 5: ~2 ms menos de la
  llegada a verse la imagen (PresentMon), misma imagen byte a byte; 1440p -0,7 a -1 ms. *About 2 ms lower latency at 4K.*

### Nota
- La detección de menús de la generación (`fg_menus=1`, 1.0.4) solo actúa si el motor pasa la etiqueta de interfaz (`fgui=2`); con
  la configuración de serie no cambia nada (tampoco estorba).

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
