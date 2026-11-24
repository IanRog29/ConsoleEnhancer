# Cambios / Changelog

Formato: [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Versiones: [SemVer](https://semver.org/lang/es/). Cada versión
publicada es una etiqueta `vX.Y.Z` de git; GitHub Actions monta su zip y copia aquí sus notas en la Release.

## [Sin publicar]

## [1.3.2] - 2026-11-24

**Enhanced FG reaches its frame rate and holds together on fast camera turns at a 30 fps base, the frame-rate ladder only lowers things
when the GPU is really full (and never into the game profile), DLSS sharper in motion, Smooth Motion ×2 at 120 fps in the 60 fps mode, a
new 40 fps mode and the Switch 2 back to HOME.** Measured without the console on an RTX 4060 Ti with a 4K 120 Hz VRR TV, with real
gameplay video played through the engine like the capture card and the settings of the person who tests it with the console. Compared
with 1.3.1. *En castellano, más abajo.*

### English

#### Added
- **40 fps mode**, next to 30 and 60 (settings menu > *Mode*, the controller's Menu / Options / + button now goes 30 → 40 → 60 → 120
  when it fits, the tray menu, and the game profile remembers it). For the 40 fps modes of console games (the "balanced" modes of PS5 /
  Xbox Series, which only exist with the console outputting 120 Hz: on the PS5, *Settings > Screen and Video > Video Output > 120 Hz
  Output: Automatic* and 1440p or 1080p). With a 1440p or 1080p capture it captures at 120 Hz and keeps one new frame of every 3; at 4K
  (the capture card can't do 4K at 120) it captures at 60 and evens out the 2-1 cadence. Without frame generation each frame stays
  exactly 25 ms on screen (3 refreshes at 120 Hz); with Enhanced FG ×3 it becomes 120 fps (×3 is the default in this mode, its multiplier
  is kept apart from the other modes), ×2 gives 80 fps with VRR, Smooth Motion ×2 80 fps; no ×4 (160 > 120). The type of frame generation
  is the one of the 30 fps mode. DLSS 5 starts a bit lower than in the 30 fps mode (25 ms per frame instead of 33) and the frame-rate
  ladder aims at 40. The notice tells you what to set on the console ("PS5: 120 Hz output and the game's 40 fps mode"). The console menu
  still goes to native 4K at 60 as always. Measured in the whole app with real gameplay video at a 40 fps cadence (1440p, TV at 120 Hz
  VRR): Enhanced FG ×3 119.95 frames/s with every frame for one refresh, ×2 80.0, Smooth Motion ×2 79.9, no frame generation 40.07 with
  every frame for 3 refreshes.

#### Changed
- **Missing the target frame rate only lowers settings when the GPU is really full.** Before lowering anything (DLSS 5, the DLSS model,
  FSR 1, the multiplier) the engine now checks how busy the whole GPU is (NVIDIA's own counter, the one nvidia-smi shows): if it hasn't
  gone over 90 %, the frame rate is missed for another reason (Enhanced FG pacing, the frame cap with VRR, the source) and nothing is
  changed; a short notice says so once ("Enhanced FG x4 · 113 of 120 fps: not the GPU (41 % busy), nothing changed"). Before, Enhanced FG
  ×4 dropped to ×2 and DLSS 5 was lowered or switched off with the GPU at 40-67 %. With Enhanced FG, generated frames still being
  skipped while each real frame takes long to be ready (the engine's GPU time over 40 % of a real frame, as with DLSS 5) still lower
  DLSS 5 / the model, which shortens it. Enhanced FG is not judged in its first 4.5 s after a change.
- **Enhanced FG ×4 that doesn't fit goes to ×3, not ×2** (and ×3 to ×2), for this session only.
- **What the app lowers on its own is never saved in the game profile**: DLSS 5 switched off because it didn't fit is for this session
  only (before it was saved), and the same for passes, mode, capture or FSR 1.
- **DLSS 5 never goes below 33 % at 1440p** (845 pixels wide: 44 % at 1080p, 25 % at 4K as before).
- **A bit more GPU before settings are lowered when applying them**: the engine's GPU limit is 94 % of a frame (was 90 %; a 90 kept
  from an older ini counts as 94).
- **Smooth Motion ×4 removed** (Smooth Motion ×2 only, also in the menu, L3, the controller and the lists); a saved Smooth Motion ×4
  becomes Smooth Motion ×2 and the app says so once.
- **Enhanced FG ×4 (and ×3 of the 40 fps mode) with less latency on a 120 Hz VRR screen**: when a frame shown a bit late leaves another
  one waiting in the TV's queue (which then stays there for tens of seconds), the next generated frame is not presented so the TV
  catches up (one every 250 ms at most). Measured on the TV at 120 Hz VRR with real gameplay video, from the console frame to the screen:
  ×4 quality at 4K 53-67 → 39 ms, at 1440p + DLSS M 69 → 48 ms, ×4 low latency 25 → 23 ms, with the same pacing (every frame for one
  refresh, 119.9 frames/s).
- **Enhanced FG ×4 low latency with DLSS 5 also shows the real frame one output frame later**, as it does without DLSS 5 since 1.3.1: the
  first generated frame of each group is interpolated and the others are predicted less far ahead (fewer glitches when turning the
  camera). It was left out in 1.3.1 because it made generated frames collide with the next real frame; with the new pacing it doesn't
  (4K + DLSS 5 at 65 %: 119.9 frames/s, every frame for one refresh), for +9 ms at the median (30 → 39 ms) and +1 ms at the 95th
  percentile.

#### Improved
- **Enhanced FG no longer breaks apart on fast camera turns at a 30 fps base.** Its optical flow searched up to 128 points of its working
  resolution per real frame (6.7 % of the width at 4K): with a 30 fps base a fast turn moves twice as far per real frame as with 60 and
  went out of reach, so the generated frame showed the previous image in the wrong place with broken patches. Now each pair of real frames
  first gets the camera's whole-image motion with a search of ±25 % of the width, which seeds the flow, and the flow pyramid has six
  levels (twice the reach). Measured against the true in-between frames of real gameplay video (Fallout 4 turning twice as fast at a
  30 fps base): error 22.4 → 18.1, the worst frames 42-46 → 20-27, broken blocks 39 % → 32 % (worst frames 73-79 % → 35-53 %); a real
  4K30 clip at half rate 12.9 → 10.4 (worst frames 31-33 → 10-12); where the turn already fitted (GTA V, Fallout 4, Counter-Strike 2 at
  a 30 fps base, 60 fps base) the same or 1-2 % better. It also helps low latency on fast turns (-7 %). No measurable GPU cost.

#### Fixed
- **Enhanced FG reaches its frame rate when the GPU has room: ×4 of the 30 mode and ×2 of the 60 mode, also at 1440p / 1080p with DLSS
  and with DLSS 5.** The presentation thread dropped every generated frame still left for a real frame as soon as the next real frame
  entered its queue, which happens right after the console frame arrives, long before it is ready. Whenever a real frame took more than
  ~17 ms to be ready (1440p / 1080p + DLSS, DLSS 5, the first seconds after a change) the last generated frames were lost with the GPU
  idle: in the tester's session ×4 low latency lost 10-18 % of its generated frames, the frame-rate ladder took it as "not reaching
  120 fps" and dropped to ×2 within 2 s, and ×2 of the 60 mode on the Switch 2 lost 8 % (almost all of them in the first seconds). A real
  frame was also taken as "late" (shown at once with no generated frames) just because the next one was already queued. Now a generated
  frame is dropped only if it would collide with the next real frame, one that gives way to the next real frame (the game cadence) no
  longer counts as missing, and a real frame is late only if the next one is already ready. The copy of each frame to the screen is sent
  as soon as possible instead of right before its time, and its duration is no longer overestimated (it included the work queued ahead:
  2-5 ms instead of ~0.5 ms, which delayed real frames). ×3 quality of the 30 mode (90 fps with VRR) is now paced by when each console
  frame really arrived, like ×2. Measured with real gameplay video as the capture card (PresentMon on the TV at 120 Hz VRR): ×4 low
  latency at 1080p + DLSS 119.9 → 120.0 frames/s with no skipped frame once running (147 lost in the first 3 s before); ×2 low latency of
  the 60 mode at 1440p + DLSS 105.6 → 118.2 frames/s (worst second 63 → 119, refreshes without a new frame 12 % → 1.5 %); ×3 quality
  with an irregular game cadence, frames more than 4 ms off their time 12 % → 0.2 %; ×3 low latency 26 % → 6.6 %; ×4 low latency + DLSS 5
  at 4K, 119.96 frames/s with every frame for one refresh instead of one generated frame in four lost (89.9 frames/s in 1.3.1); in the
  whole app at 1440p + DLSS M + DLSS 5 at 90 %, ×4 low latency 119.9 frames/s with every frame for one refresh and nothing lowered (64.9
  with the old pacing).
- **DLSS no longer smears moving floors (DLSS M / L) or the picture during and right after camera turns (DLSS K / J).** DLSS SR was
  blending a history that did not belong to that pixel: the engine's motion vectors cannot fully explain a floor seen in perspective or a
  camera turn, and DLSS kept mixing the shifted history in, so the road became a smooth paste (with no history at all the very same DLSS M
  was sharper). Now, for every pixel, the engine checks whether the previous frame moved by its vector actually matches the current one;
  where it does not, DLSS starts that pixel fresh instead of blending the wrong history. Measured on real game video against the original
  frames: road under a moving car with DLSS M, floor sharpness 0.56 → 0.83 of the original (mid-band detail 0.56 → 0.95); Battlefront
  camera turn with DLSS K / J 0.76 / 0.78 → 0.85 and much less shimmer; Counter-Strike 2 turns with K / J 0.72 / 0.73 → 0.79; 1440p +
  DLSS 5 + DLSS M the same or better; still scenes unchanged. No extra sharpening and no own upscaler: only what DLSS receives changes.
  About +0.3-0.5 ms of GPU at 1440p.
- **Switch 2: leaving a game for the HOME menu is noticed also when the selected game's name is not on screen** (cursor on the bottom
  bar, the name still appearing, a notification over it): the HOME menu is recognised by its top and bottom strips as well, so the app no
  longer stays in game mode (game capture, DLSS 5, frame generation) on the HOME menu.
- **No black screen when a game's profile is applied** (entering a game from the HOME / console menu, and going back): with the engine
  the change happens in the same engine, so the previous picture stays until the new one instead of a black curtain; the engine also no
  longer builds DLSS 5 for the previous capture size first (0.2-0.4 s) and starts DLSS 5 while the new capture arrives. Measured in the
  app with real video: no black at all from the app (was a 330-690 ms black curtain plus its fade), and the first game picture ready
  0.3 s sooner the first time.
- A console changing video mode while a game starts (No Signal for a moment) is no longer reported as a stalled engine.
- **Smooth Motion ×2 in the 60 fps mode reaches 120 fps on a 120 Hz VRR TV** (it stayed at 116: 58 of the console's 59.94 frames, a
  hitch every ~0.5 s, and the frame-rate ladder lowered DLSS 5 and the model for it): with G-SYNC + V-Sync the driver caps the output at
  116, so, like Enhanced FG in that mode, it now runs at a fixed refresh rate (G-SYNC off only in the engine's own NVIDIA profile while you
  play like this). Measured in the whole app with real gameplay video: 59.94 real frames and 119.9 fps on the TV, none lost (35-39 lost
  every 20 s before), at 1440p + DLSS 5 and at 1080p; the median latency about the same, the 95th percentile 8-12 ms higher.

#### Settings to go back
Engine settings go at the end of the `motor_extra=` line of `consoleenhancer.ini`; the others, as their own line at the end of the file.
- In `motor_extra`: `efg_salto_fino=0 efg_copia_pronto=0 efg_l_nuevo=0 efg_atrasada=0 efg_ritmo_x3=0 efg_cola_tele=0` (Enhanced FG pacing
  as in 1.3.1) · `efg_retraso_dlss5=0` (×4 low latency with DLSS 5 all ahead, as in 1.3.1) ·
  `efg_giro=0 efg_niveles=5` (the 1.3.1 optical flow) · `sr_sin_historia=0` (DLSS history as in 1.3.1; `sr_sin_historia_mov=0.5` keeps
  the history of still areas if a dark or grainy game shimmers) · `escalera_grafica_llena=0` (lower as in 1.3.1) · `escalera_efg_p95=0`
  (only the whole-GPU check) · `dlss5_suelo_px=0` (25 % floor) · `cambio_captura_espera=0` · `m40_reparto=0` (40 fps mode: the 2-1
  cadence as it arrives) · `m40_ritmo=0` (each frame as soon as it is ready).
- In the ini: `motor_uso_grafica=90.5` (the 90 % GPU limit) · `motor_60sm_refresco_fijo=0` (Smooth Motion ×2 of the 60 fps mode with
  VRR, 116 fps) · `home_por_franjas=0` · `telon_home_juego=1` (the black curtain between HOME and game) · `modo40_captura_120=0` (40 fps
  mode always captured at 60).
- To try, in `motor_extra`: `efg_retraso_lat3=0.25` (the later real frame also in ×3 low latency: fewer glitches when turning, the same
  pacing with the new thread, but 16 → 30 ms at 1440p + DLSS M) · `efg_retraso_lat4=0.5` (×4 low latency with the real frame two output
  frames later: about a third fewer glitches than all-ahead and close to quality mode, +8 ms) · `efg_lat_ganancia` (how far low latency
  predicts, 1 = default).

#### Known issues
- Enhanced FG **low latency still shows more glitches than quality mode when the camera turns**, above all from a 30 fps base (about twice
  the badly wrong pixels in a turn, +20-50 % in calmer scenes): every generated frame is predicted 17 ms (×2) to 25 ms (×4) ahead of the
  last real frame, and when the turn speeds up, slows down or changes that cannot be known before the next real frame arrives. Smoothing,
  damping or accelerating the prediction made it worse, so it is unchanged; quality mode looks better when the camera moves a lot.
- Where the camera turn already fitted (most scenes at a 60 fps base and slower turns at 30) the generated frames barely change: what
  still shows is what gets uncovered behind close objects, thin things with parallax, the strip entering through the screen edge on a fast
  turn and animated HUD elements; zooming maps were not measured. The graphics card's hardware optical flow is the next step.
- The DLSS history fix is only for DLSS SR (not XeSS / FSR).
- The 40 fps mode uses the frame generation type of the 30 fps mode (picking one changes both); the Enhanced FG multiplier is its own.
- Everything in this version was measured with real gameplay video through the engine, not yet with the PS5 or the Switch 2.

### Castellano

**Enhanced FG llega a sus fps y aguanta los giros rápidos de cámara con base 30, la escalera de fps solo baja cosas si la gráfica va
llena de verdad (y nunca en el perfil del juego), DLSS más nítido en movimiento, Smooth Motion ×2 a 120 fps en el modo 60, un modo 40 fps
nuevo y la Switch 2 vuelve al HOME.**
Medido sin la consola en una RTX 4060 Ti con una tele 4K de 120 Hz con VRR, con vídeo real de juego por el motor como si fuera la
capturadora y los ajustes de quien la prueba con la consola. Comparado con la 1.3.1.

#### Añadido
- **Modo 40 fps**, junto al 30 y al 60 (menú de ajustes > *Modo*, el botón Menú / Options / + del mando ahora va 30 → 40 → 60 → 120 si
  cabe, el menú de la bandeja, y el perfil del juego lo recuerda). Para los modos de 40 de los juegos de consola (los "equilibrados" de la
  PS5 / Xbox Series, que solo existen con la consola a 120 Hz: en la PS5, *Ajustes > Pantalla y vídeo > Salida de vídeo > Salida a 120 Hz:
  Automático* y 1440p o 1080p). Con captura de 1440p o 1080p captura a 120 Hz y se queda con una nueva de cada 3; a 4K (la capturadora no
  da 4K a 120) captura a 60 y reparte parejas las imágenes de la cadencia 2-1. Sin generación cada imagen dura 25 ms justos en la tele (3
  refrescos a 120 Hz); con Enhanced FG ×3 salen 120 fps (×3 es lo de serie en este modo, su multiplicador va aparte del de los otros
  modos), ×2 da 80 fps con VRR y Smooth Motion ×2, 80; sin ×4 (160 > 120). El tipo de generación es el del modo 30. DLSS 5 empieza algo
  más bajo que en el modo 30 (25 ms por imagen en vez de 33) y la escalera de fps apunta a 40. El aviso dice lo que hay que poner en la
  consola ("PS5: salida a 120 Hz y el modo de 40 del juego"). El menú de la consola sigue a 4K nativo a 60 como siempre. Medido en la
  app entera con vídeo real de juego a cadencia de 40 (1440p, tele a 120 Hz VRR): Enhanced FG ×3 119,95 imágenes/s con cada imagen un
  refresco, ×2 80,0, Smooth Motion ×2 79,9, sin generación 40,07 con cada imagen 3 refrescos.

#### Cambiado
- **No llegar a los fps solo baja ajustes si la gráfica va llena de verdad.** Antes de bajar nada (DLSS 5, el modelo de DLSS, FSR 1, el
  multiplicador) el motor mira lo ocupada que va la gráfica entera (el contador de NVIDIA, el de nvidia-smi): si no ha pasado del 90 %,
  no se llega por otra cosa (el ritmo de Enhanced FG, el tope de imágenes con VRR, la fuente) y no se cambia nada; un aviso corto lo dice
  una vez ("Enhanced FG x4 · 113 de 120 fps: no es la gráfica (al 41 %), no se cambia nada"). Antes Enhanced FG ×4 pasaba a ×2 y DLSS 5 se
  bajaba o se apagaba con la gráfica al 40-67 %. Con Enhanced FG, si se siguen saltando inventadas mientras cada real tarda en estar (la
  gráfica del motor por encima del 40 % de una real, como con DLSS 5), sí se baja DLSS 5 / el modelo, que es lo que lo acorta. Enhanced FG
  no se juzga los primeros 4,5 s tras un cambio.
- **Enhanced FG ×4 que no cabe pasa a ×3, no a ×2** (y ×3 a ×2), solo esta sesión.
- **Lo que la app baja sola nunca se guarda en el perfil del juego**: DLSS 5 apagado por no caber es solo de esta sesión (antes se
  guardaba), igual que las pasadas, el modo, la captura o FSR 1.
- **DLSS 5 nunca por debajo del 33 % a 1440p** (845 píxeles de ancho: 44 % a 1080p, 25 % a 4K como antes).
- **Un poco más de gráfica antes de bajar ajustes al aplicarlos**: el tope de la gráfica del motor es el 94 % de una imagen (antes 90 %;
  un 90 de un ini de antes cuenta como 94).
- **Smooth Motion ×4 quitado** (solo Smooth Motion ×2, también en el menú, L3, el mando y las listas); un Smooth Motion ×4 guardado pasa a
  Smooth Motion ×2 y la app lo dice una vez.
- **Enhanced FG ×4 (y ×3 del modo 40) con menos latencia en una pantalla de 120 Hz con VRR**: cuando una imagen que sale un poco tarde
  deja otra esperando en la cola de la tele (y ahí se queda decenas de segundos), la siguiente inventada no se presenta y la tele se pone
  al día (una cada 250 ms como mucho). Medido en la tele a 120 Hz VRR con vídeo real de juego, de la imagen de la consola a la pantalla:
  ×4 calidad a 4K 53-67 → 39 ms, a 1440p + DLSS M 69 → 48 ms, ×4 baja latencia 25 → 23 ms, con el mismo ritmo (cada imagen un refresco,
  119,9 imágenes/s).
- **Enhanced FG ×4 baja latencia con DLSS 5 también enseña la real una imagen después**, como sin DLSS 5 desde la 1.3.1: la primera
  inventada de cada grupo se interpola y las demás se adivinan menos lejos (menos fallos al girar la cámara). En la 1.3.1 se dejó fuera
  porque las inventadas chocaban con la real siguiente; con el ritmo nuevo ya no (4K + DLSS 5 al 65 %: 119,9 imágenes/s, cada imagen un
  refresco), a cambio de +9 ms en la mediana (30 → 39 ms) y +1 ms en el percentil 95.

#### Mejorado
- **Enhanced FG ya no se rompe en los giros rápidos de cámara con base 30.** Su flujo óptico buscaba hasta 128 puntos de su resolución de
  trabajo por imagen real (el 6,7 % del ancho a 4K): con base 30 un giro rápido mueve el doble por imagen real que con 60 y se salía, así
  que la inventada enseñaba la imagen anterior en el sitio equivocado con trozos rotos. Ahora cada pareja de reales calcula primero el
  movimiento de toda la imagen (la cámara) con una búsqueda de ±25 % del ancho, que sirve de semilla al flujo, y la pirámide del flujo
  tiene seis niveles (el doble de alcance). Medido contra las imágenes de en medio de verdad de vídeo real de juego (Fallout 4 girando el
  doble de rápido con base 30): error 22,4 → 18,1, las peores 42-46 → 20-27, bloques rotos 39 % → 32 % (en las peores 73-79 % → 35-53 %);
  un vídeo real de 4K30 a la mitad de imágenes 12,9 → 10,4 (las peores 31-33 → 10-12); donde el giro ya cabía (GTA V, Fallout 4,
  Counter-Strike 2 con base 30, base 60) igual o un 1-2 % mejor. También ayuda a la baja latencia en los giros rápidos (-7 %). Sin coste
  de gráfica medible.

#### Arreglado
- **Enhanced FG llega a sus fps cuando la gráfica tiene margen: ×4 del modo 30 y ×2 del modo 60, también a 1440p / 1080p con DLSS y con
  DLSS 5.** El hilo de presentación tiraba todas las inventadas que le quedaban a una imagen real en cuanto la siguiente real entraba en
  su cola, y eso pasa nada más llegar la imagen de la consola, mucho antes de estar lista. Siempre que una real tardaba más de ~17 ms en
  estar (1440p / 1080p + DLSS, DLSS 5, los primeros segundos tras un cambio) se perdían las últimas inventadas con la gráfica de sobra: en
  la sesión de prueba ×4 baja latencia perdía el 10-18 % de sus inventadas, la escalera lo tomaba por "no se llega a 120 fps" y bajaba a
  ×2 en 2 s, y ×2 del modo 60 en la Switch 2 perdía el 8 % (casi todas en los primeros segundos). Además una real se daba por "atrasada"
  (se enseñaba ya y sin inventadas) solo porque la siguiente ya estaba en la cola. Ahora una inventada solo se deja si chocaría con la
  real siguiente, la que cede el sitio a la real siguiente (la cadencia del juego) ya no cuenta como que falta y una real solo va atrasada
  si la siguiente ya está lista. La copia de cada imagen a la pantalla se manda en cuanto se puede y no justo antes de su hora, y lo que
  tarda ya no se mide de más (contaba lo que esperaba delante en la cola: 2-5 ms en vez de ~0,5, y retrasaba las reales). ×3 calidad del
  modo 30 (90 fps con VRR) va ahora por la hora en que llegó de verdad cada imagen de la consola, como ×2. Medido con vídeo real de juego
  como capturadora (PresentMon en la tele a 120 Hz VRR): ×4 baja latencia a 1080p + DLSS 119,9 → 120,0 imágenes/s sin una inventada
  saltada ya en marcha (antes 147 en los 3 primeros s); ×2 baja latencia del modo 60 a 1440p + DLSS 105,6 → 118,2 imágenes/s (el peor
  segundo 63 → 119, refrescos sin imagen nueva 12 % → 1,5 %); ×3 calidad con una cadencia irregular del juego, imágenes a más de 4 ms de su
  hora 12 % → 0,2 %; ×3 baja latencia 26 % → 6,6 %; ×4 baja latencia + DLSS 5 a 4K, 119,96 imágenes/s con cada imagen un refresco en vez
  de perder una inventada de cada cuatro (89,9 imágenes/s con la 1.3.1); en la app entera a 1440p + DLSS M + DLSS 5 al 90 %, ×4 baja
  latencia 119,9 imágenes/s con cada imagen un refresco y sin bajar nada (64,9 con el ritmo de antes).
- **DLSS ya no emborrona el suelo en marcha (DLSS M / L) ni la imagen al girar la cámara y justo después (DLSS K / J).** DLSS SR mezclaba
  una historia que no era la de ese punto: los vectores del motor no explican del todo un suelo en perspectiva ni un giro, y la carretera
  quedaba en una pasta lisa (sin historia ninguna el mismo DLSS M salía más nítido). Ahora el motor mira en cada punto si la imagen
  anterior movida con su vector casa con la de ahora; donde no casa, DLSS empieza ese punto de cero. Medido con vídeo real contra las
  imágenes originales: carretera con DLSS M, nitidez del suelo 0,56 → 0,83 del original (detalle de banda media 0,56 → 0,95); giro de
  Battlefront con K / J 0,76 / 0,78 → 0,85 y mucho menos parpadeo; giros de Counter-Strike 2 con K / J 0,72 / 0,73 → 0,79; 1440p + DLSS 5
  + DLSS M igual o mejor; lo quieto, igual. Sin enfoque añadido ni reescalador propio: solo cambia lo que recibe DLSS. Unos +0,3-0,5 ms de
  gráfica a 1440p.
- **Switch 2: la salida de un juego al HOME se ve también sin el nombre del juego seleccionado a la vista** (el cursor en la barra de
  abajo, el nombre apareciendo, una notificación encima): el HOME se reconoce también por sus franjas de arriba y de abajo, así la app ya
  no se queda en el modo juego (captura del juego, DLSS 5, generación) con el HOME delante.
- **Sin pantallazo negro al aplicar el perfil de un juego** (al entrar desde el HOME / el menú de la consola y al volver): con el motor el
  cambio va en el mismo motor y la imagen de antes sigue hasta la nueva en vez de un telón negro; además el motor ya no crea DLSS 5 para
  la captura de antes (0,2-0,4 s) y lo inicia mientras llega la nueva. Medido en la app con vídeo real: nada de negro de la app (antes un
  telón negro de 330-690 ms y su fundido) y la primera imagen del juego lista 0,3 s antes la primera vez.
- Una consola que cambia de modo de vídeo al arrancar un juego (Sin señal un momento) ya no cuenta como el motor colgado.
- **Smooth Motion ×2 en el modo 60 llega a 120 fps en una tele de 120 Hz con VRR** (se quedaba en 116: 58 de las 59,94 imágenes de la
  consola, un tirón cada ~0,5 s, y la escalera bajaba DLSS 5 y el modelo por eso): con G-SYNC + V-Sync el driver topa la salida a 116, así
  que, como Enhanced FG en ese modo, ahora va con refresco fijo (G-SYNC apagado solo en el perfil de NVIDIA del motor mientras juegas
  así). Medido en la app entera con vídeo real de juego: 59,94 reales y 119,9 en la tele, ninguna perdida (antes 35-39 cada 20 s), a 1440p
  + DLSS 5 y a 1080p; la latencia en la mediana parecida, en el percentil 95 8-12 ms más.

#### Ajustes para volver atrás
Las claves del motor van al final de la línea `motor_extra=` de `consoleenhancer.ini`; las demás, en su propia línea al final del fichero.
- En `motor_extra`: `efg_salto_fino=0 efg_copia_pronto=0 efg_l_nuevo=0 efg_atrasada=0 efg_ritmo_x3=0 efg_cola_tele=0` (el ritmo de
  Enhanced FG de la 1.3.1) · `efg_retraso_dlss5=0` (×4 baja latencia con DLSS 5 todo hacia delante, como la 1.3.1) · `efg_giro=0
  efg_niveles=5` (el flujo óptico de la 1.3.1) · `sr_sin_historia=0` (la historia de DLSS como en la 1.3.1; `sr_sin_historia_mov=0.5`
  deja su historia a lo quieto si un juego oscuro o con grano "hierve") · `escalera_grafica_llena=0` (bajar como en la 1.3.1) ·
  `escalera_efg_p95=0` (solo la gráfica entera) · `dlss5_suelo_px=0` (suelo del 25 %) · `cambio_captura_espera=0` · `m40_reparto=0`
  (modo 40: la cadencia 2-1 tal cual llega) · `m40_ritmo=0` (cada imagen en cuanto está).
- En el ini: `motor_uso_grafica=90.5` (el tope del 90 %) · `motor_60sm_refresco_fijo=0` (Smooth Motion ×2 del modo 60 con VRR, a 116) ·
  `home_por_franjas=0` · `telon_home_juego=1` (el telón negro entre el HOME y el juego) · `modo40_captura_120=0` (el modo 40 siempre con
  la captura a 60).
- Para probar, en `motor_extra`: `efg_retraso_lat3=0.25` (la real más tarde también en ×3 baja latencia: menos fallos al girar, el mismo
  ritmo con el hilo nuevo, pero 16 → 30 ms a 1440p + DLSS M) · `efg_retraso_lat4=0.5` (×4 baja latencia con la real dos imágenes
  después: un tercio menos de fallos que todo hacia delante y cerca del modo calidad, +8 ms) · `efg_lat_ganancia` (lo lejos que adivina
  la baja latencia, 1 = lo de serie).

#### Problemas conocidos
- Enhanced FG **baja latencia sigue con más fallos que el modo calidad al girar la cámara**, sobre todo con base 30 (más o menos el doble
  de puntos muy errados en un giro, un 20-50 % más en escenas tranquilas): cada inventada se adivina 17 ms (×2) a 25 ms (×4) por delante
  de la última real, y cuando el giro acelera, frena o cambia eso no se puede saber hasta que llega la siguiente real. Suavizar, amortiguar
  o acelerar la predicción salió peor, así que no cambia; con la cámara moviéndose mucho, el modo calidad se ve mejor.
- Donde el giro ya cabía (casi todo con base 60 y los giros más lentos con base 30) las inventadas casi no cambian: lo que sigue
  viéndose es lo que se destapa detrás de lo cercano, lo fino con paralaje, la franja que entra por el borde en un giro rápido y la
  interfaz animada; el zoom de los mapas no se ha medido. El flujo óptico por hardware de la gráfica es lo siguiente.
- El arreglo de la historia de DLSS es solo para DLSS SR (no XeSS / FSR).
- El modo 40 usa el tipo de generación del modo 30 (elegir uno cambia los dos); el multiplicador de Enhanced FG es suyo.
- Todo lo de esta versión se ha medido con vídeo real de juego por el motor, aún no con la PS5 ni con la Switch 2.

## [1.3.1] - 2026-11-24

**Your settings kept on the PC across versions, a frame-rate ladder that keeps the multiplier, a smoother Enhanced FG and the Switch 2
after the PS5 fixed.** Measured without the console on an RTX 4060 Ti with a 4K 120 Hz VRR TV, with real gameplay video played through
the engine like the capture card and the settings of the person who tests it with the console (DLSS 5 level 4, 4K and 1440p + DLSS M,
30 fps mode, 120 Hz VRR). Compared with 1.3. *En castellano, más abajo.*

### English

#### Changed
- **Your settings now live on this PC, not in the app folder**: `%LOCALAPPDATA%\ConsoleEnhancer\settings` keeps the settings, game
  profiles, console, language, learned scales and logs for every version, written on every change. A new version unzipped anywhere
  (Downloads, Desktop, another drive, with no older version next to it) uses them as they are from the first start. The first time,
  they are brought over once from the most recently used data folder (the previous version's `data\` or the backup), noted in
  `settings\inherited.txt` and said once on screen. The zip no longer ships `portable.txt`; create an empty one next to the exe to keep
  everything in `data\` there. An older version opened later works on a copy (`settings-older\<version>`) and never rewrites a newer
  one's settings. Start from scratch: close the app and delete that folder. The logs are now in `settings\logs`.
- **XeSS FG removed for now** from the settings menu, L3, the lists and every automatic fallback. Profiles and choices that had it (or
  FSR 3 FG, removed in 1.3) switch to the closest one on this PC (Smooth Motion on RTX 40 / 50, the same ×2), noted in the log and said
  once on screen. Smooth Motion ×4 still uses the engine's XeSS FG ×2 underneath. `generacion_xess=1` in the ini brings it back.
- **Missing the target frame rate is seen in 1-2 s, and frame generation keeps its multiplier as long as it can.** Every quarter of a
  second the engine counts the console frames it had to drop (and, with Enhanced FG, the generated frames it skipped); a single hitch, a
  slow game on the console, waking up, the hand-over between engines, the settings menu or losing focus don't count. When it doesn't fit
  it lowers, in this order and without reopening anything: DLSS 5 (big steps first, down to 25 % and one pass), then the DLSS upscaler
  (a lighter model and, as a last resort, FSR 1 instead of DLSS for that session), and only then the multiplier (Smooth Motion ×4 /
  Enhanced FG ×3-×4 → ×2) **for this session only**: what you picked stays in the game profile. What fits is learned for next time and
  raised again slowly when there is room. A short notice says what was lowered ("Enhanced FG x4 · DLSS 5 at 25 % and DLSS K so it
  fits"). Before, the multiplier dropped after 5-21 s, often while DLSS 5 was still being lowered and without touching the upscaler.
  Measured in the whole app with real gameplay video: Smooth Motion ×4 + DLSS 5 at 1440p (+ DLSS M) now stays ×4 with DLSS 5 at 25 %
  and DLSS K (in 1.3 it went ×2 after 21 s); Enhanced FG ×4 low latency + DLSS 5 at 1440p fits in 21 s with no skipped frames (in 1.3
  DLSS 5 went down 90 → 50 % over 37 s while skipping 30 generated frames per second).
- **Enhanced FG ×2 is smooth when the console game doesn't keep a steady 30 fps.** A console game at 30 fps doesn't always hand over a
  new frame every two refreshes: when it dips, a frame lasts three refreshes and the next one one. Enhanced FG used to place its
  generated frame at a fixed time and halfway in motion, so after a long frame what you saw no longer matched its time (a stop and a
  jump). Now the real frames are timed by when their content first arrived and the generated frames fill the actual gap, each one with
  the motion of its own moment; in low-latency mode a generated frame gives way when the next real one is already there. Measured on
  the TV with real gameplay video at an irregular game cadence (4K, 120 Hz VRR): what is seen strays from its time 8.2 → 1.3 ms (95th
  percentile) in quality mode, 7.8 → 1.2 ms in low-latency mode and 5.5 → 2.3 ms in quality mode with DLSS 5; latency unchanged
  (quality ~32 ms, low latency ~9.5 ms). With a steady game nothing changes.
- **Enhanced FG ×4 low latency predicts less far ahead**: the real frame is shown one output frame later, so the first generated frame
  of each group is interpolated between the two real frames and the others go 1/4 less far: about 22 % less error when turning the
  camera (27 % fewer badly wrong pixels) against the true frames of real gameplay video, for +5 ms (measured on the TV: 23 → 28 ms, the
  same pacing, every frame for one refresh). Not with DLSS 5 (there it would not fit and the ladder would drop to ×2) and not in ×3
  (there it cost +18 ms and uneven pacing): those stay as in 1.3.
- **Enhanced FG low latency has its own forward pass**: the generated frame no longer picks pixel by pixel (fewer loose dark / bright
  specks along object edges), what gets uncovered behind the weapon or the character is filled from the background instead of smearing
  its dark edge, and what enters through the screen edges keeps moving with the picture instead of a still strip. The overall error
  against real gameplay video stays the same (within 1 %): most of what still shows when turning comes from predicting the next camera
  move.

#### Fixed
- **Switch 2 after the PS5**: the first game no longer starts as "Game 1" with the PS5 settings. The launch animation of the selected
  tile (and a dimmed HOME with a notice on top) is never taken for another game: the selected game's name is only looked for in its row
  above the tiles (a blue logo inside a tile was read as an unknown game), and the last recognised game is kept while leaving the HOME.
  On start-up, the "Game N" created that way (23-11 20:22:52) is removed with its icon.
- **A new game starts with the settings of its own console** (the Switch 2: its last game; PS5 / Xbox: their console profile; otherwise
  the defaults), never with the ones of the console used before. The console profile now also carries its frame generation type.
  Garbled PS5 menu reads no longer create a profile.

#### Settings to go back
Engine settings go at the end of the `motor_extra=` line of `consoleenhancer.ini`; the others, as their own line at the end of the file.
- In `motor_extra`: `escalera=0` (the 1.3 frame-rate checks) · `escalera_modelo=0` / `escalera_fsr1=0` (the ladder without the DLSS model
  / FSR 1 steps) · `escalera_pasos=10` (DLSS 5 in steps of 10 %) · `efg_ritmo=0 repetidas_30_efg=0` (Enhanced FG pacing as in 1.3) ·
  `efg_retraso_lat4=-1` (×4 low latency all forward, as in 1.3) · `efg_lat=0` (the 1.3 low-latency forward pass).
- To try, in `motor_extra`: `efg_colchon_ms=17` (Enhanced FG quality ×2 perfectly even with an irregular game, +17 ms) · `efg_ritmo=2`
  (the new pacing also for ×3 / ×4 quality: smoother ×3, but ×4 less even on a 120 Hz TV) · `efg_retraso_lat3=0.25` /
  `efg_retraso_dlss5=1` (the later real frame also in ×3 / with DLSS 5).
- In the ini: `generacion_xess=1` (XeSS FG back). An empty `portable.txt` next to the exe: settings in `data\` there.

#### Known issues
- Enhanced FG: the **picture quality of the generated frames barely changes** in this version. Measured against the true in-between
  frames of real gameplay video, nothing tried gave a clear gain (finer motion blocks, a memory for animated HUD elements, sharper
  sampling; the new test options `efg_nitido`, `efg_sel_vecinos`, `efg_sel_local` are off by default). The graphics card's hardware
  optical flow (NVIDIA RTX) lowered the error by 2.5-5.5 % in tests and is being looked at for a later version.
- Enhanced FG **low latency still has errors when turning the camera**, fewer at ×4 (without DLSS 5): most come from predicting the next
  camera move, and they grow with how far ahead the last generated frame is. For ×3 / ×4 with DLSS 5, the quality mode looks better.
- Enhanced FG ×4 low latency + DLSS 5 at 4K capture doesn't fit on an RTX 4060 Ti even at 25 %: after ~16 s it goes ×2 for the session
  with a notice (the profile keeps ×4).
- Smooth Motion ×4 + DLSS 5 at 1440p fits only with DLSS 5 at 25 % and DLSS K on an RTX 4060 Ti.
- The Xbox shares the "Console" profile with the PS5 (one profile for every console without a HOME).
- Everything in this version was measured with real gameplay video through the engine, not yet with the console.

### Castellano

**Tus ajustes se guardan en el PC entre versiones, una escalera de fps que conserva el multiplicador, Enhanced FG más fluida y la
Switch 2 tras la PS5 arreglada.** Medido sin la consola en una RTX 4060 Ti con una tele 4K de 120 Hz con VRR, con vídeo real de juego
por el motor como si fuera la capturadora y los ajustes de quien la prueba con la consola (DLSS 5 nivel 4, 4K y 1440p + DLSS M, modo
30, 120 Hz VRR). Comparado con la 1.3.

#### Cambiado
- **Tus ajustes viven ahora en este PC, no en la carpeta de la app**: `%LOCALAPPDATA%\ConsoleEnhancer\settings` guarda los ajustes, los
  perfiles de juegos, la consola, el idioma, las escalas aprendidas y los registros de todas las versiones, y se escribe en cada cambio.
  Una versión nueva descomprimida donde sea (Descargas, Escritorio, otra unidad, sin ninguna anterior al lado) los usa tal cual desde el
  primer arranque. La primera vez se traen una vez de la carpeta de datos usada más recientemente (el `data\` de la versión anterior o la
  copia de seguridad); se apunta en `settings\inherited.txt` y se dice una vez en pantalla. El zip ya no trae `portable.txt`; crea uno
  vacío junto al exe para guardarlo todo en su `data\`. Una versión más antigua abierta después trabaja en una copia
  (`settings-older\<versión>`) y nunca reescribe los ajustes de una más nueva. Empezar de cero: cierra la app y borra esa carpeta. Los
  registros están ahora en `settings\logs`.
- **XeSS FG quitada de momento** del menú de ajustes, L3, las listas y cualquier salida automática. Los perfiles y lo elegido con ella (o
  con FSR 3 FG, quitada en la 1.3) pasan a lo más parecido de este PC (Smooth Motion en RTX 40 / 50, el mismo ×2); se apunta en el
  registro y se dice una vez en pantalla. Smooth Motion ×4 sigue usando por dentro XeSS FG ×2 del motor. `generacion_xess=1` en el ini
  la vuelve a poner.
- **No llegar a los fps objetivo se ve en 1-2 s, y la generación conserva su multiplicador mientras puede.** Cada cuarto de segundo el
  motor cuenta las imágenes de la consola que ha tenido que tirar (y, con Enhanced FG, las inventadas que se ha saltado); un tirón
  suelto, un juego lento en la consola, despertar, el relevo entre motores, el menú de ajustes o perder el foco no cuentan. Si no cabe
  baja, por este orden y sin reabrir nada: DLSS 5 (primero a pasos grandes, hasta el 25 % y una pasada), luego el reescalado DLSS (un
  modelo más ligero y, como último recurso, FSR 1 en vez de DLSS esa sesión) y solo al final el multiplicador (Smooth Motion ×4 /
  Enhanced FG ×3-×4 → ×2) **solo esta sesión**: lo elegido sigue en el perfil del juego. Lo que cabe se aprende para la próxima vez y se
  vuelve a subir despacio cuando sobra. Un aviso corto dice lo que se ha bajado ("Enhanced FG x4 · DLSS 5 al 25 % y DLSS K para que
  quepa"). Antes el multiplicador caía a los 5-21 s, muchas veces mientras DLSS 5 aún bajaba y sin tocar el reescalado. Medido en la app
  entera con vídeo real de juego: Smooth Motion ×4 + DLSS 5 a 1440p (+ DLSS M) sigue ahora en ×4 con DLSS 5 al 25 % y DLSS K (en la 1.3
  pasaba a ×2 a los 21 s); Enhanced FG ×4 baja latencia + DLSS 5 a 1440p cabe a los 21 s sin inventadas saltadas (en la 1.3 DLSS 5
  bajaba 90 → 50 % en 37 s saltándose 30 inventadas por segundo).
- **Enhanced FG ×2 va fluido aunque el juego de la consola no mantenga los 30 fps.** Un juego de consola a 30 no siempre entrega una
  imagen nueva cada dos refrescos: cuando baja, una imagen dura tres refrescos y la siguiente uno. Enhanced FG ponía su inventada a una
  hora fija y a mitad de movimiento, así que tras una imagen larga lo que se veía no casaba con su hora (se paraba y saltaba). Ahora las
  reales van por la hora en que llegó su contenido y las inventadas rellenan el hueco de verdad, cada una con el movimiento de su
  instante; en baja latencia una inventada cede el sitio si la siguiente real ya está. Medido en la tele con vídeo real de juego con una
  cadencia irregular (4K, 120 Hz VRR): lo que se ve se aparta de su hora 8,2 → 1,3 ms (percentil 95) en el modo calidad, 7,8 → 1,2 ms en
  baja latencia y 5,5 → 2,3 ms en calidad con DLSS 5; la latencia no cambia (calidad ~32 ms, baja latencia ~9,5 ms). Con un juego
  estable no cambia nada.
- **Enhanced FG ×4 en baja latencia predice menos lejos**: la real se enseña una imagen de salida más tarde, así que la primera
  inventada de cada grupo se interpola entre las dos reales y las demás van 1/4 menos lejos: un 22 % menos de error al girar la cámara
  (un 27 % menos de puntos muy errados) contra las imágenes de verdad de vídeo real de juego, por +5 ms (medido en la tele: 23 → 28 ms,
  el mismo ritmo, todas a un refresco). No con DLSS 5 (ahí no cabría y la escalera bajaría a ×2) ni en ×3 (ahí costaba +18 ms y un ritmo
  irregular): esos siguen como en la 1.3.
- **Enhanced FG baja latencia tiene su propia pasada hacia delante**: la inventada ya no elige punto a punto (menos puntos sueltos
  claros / oscuros en los bordes de las cosas), lo que se destapa detrás del arma o del personaje se rellena con el fondo en vez de
  estirar su borde oscuro, y lo que entra por los bordes de la pantalla sigue moviéndose con la imagen en vez de quedarse en una franja
  quieta. El error total contra vídeo real de juego queda igual (menos de un 1 % arriba o abajo): casi todo lo que aún se ve al girar
  viene de adivinar el siguiente movimiento de la cámara.

#### Arreglado
- **La Switch 2 tras la PS5**: el primer juego ya no empieza como "Juego 1" con los ajustes de la PS5. La animación de arranque de la
  casilla elegida (y el HOME atenuado con un aviso encima) nunca cuenta como otro juego: el nombre del juego seleccionado solo se busca
  en su fila, encima de las casillas (un logo azul dentro de una casilla se leía como un juego desconocido), y al salir del HOME se
  mantiene el último juego reconocido. Al abrir, se quita el "Juego N" creado así (23-11 20:22:52) con su icono.
- **Un juego nuevo empieza con los ajustes de su propia consola** (la Switch 2: los de su último juego; PS5 / Xbox: su perfil de
  consola; si no, los de serie), nunca con los de la consola de antes. El perfil de la consola lleva también su tipo de generación. Las
  lecturas malas del menú de la PS5 ya no crean perfil.

#### Ajustes para volver atrás
Las claves del motor van al final de la línea `motor_extra=` de `consoleenhancer.ini`; las demás, en una línea propia al final.
- En `motor_extra`: `escalera=0` (las comprobaciones de fps de la 1.3) · `escalera_modelo=0` / `escalera_fsr1=0` (la escalera sin el
  paso del modelo de DLSS / de FSR 1) · `escalera_pasos=10` (DLSS 5 de 10 en 10 %) · `efg_ritmo=0 repetidas_30_efg=0` (el ritmo de
  Enhanced FG de la 1.3) · `efg_retraso_lat4=-1` (×4 baja latencia todo hacia delante, como en la 1.3) · `efg_lat=0` (la pasada hacia
  delante de la 1.3).
- Para probar, en `motor_extra`: `efg_colchon_ms=17` (Enhanced FG calidad ×2 del todo parejo con un juego irregular, +17 ms) ·
  `efg_ritmo=2` (el ritmo nuevo también en ×3 / ×4 calidad: ×3 más fluido, pero ×4 menos parejo en una tele de 120 Hz) ·
  `efg_retraso_lat3=0.25` / `efg_retraso_dlss5=1` (la real más tarde también en ×3 / con DLSS 5).
- En el ini: `generacion_xess=1` (vuelve XeSS FG). Un `portable.txt` vacío junto al exe: los ajustes en su `data\`.

#### Problemas conocidos
- Enhanced FG: la **calidad de imagen de las inventadas casi no cambia** en esta versión. Medido contra las imágenes de en medio de
  verdad de vídeo real de juego, nada de lo probado dio una mejora clara (bloques de movimiento más finos, una memoria para la interfaz
  animada, un muestreo más nítido; las opciones nuevas de prueba `efg_nitido`, `efg_sel_vecinos`, `efg_sel_local` van apagadas de serie).
  El flujo óptico por hardware de la gráfica (NVIDIA RTX) bajó el error un 2,5-5,5 % en las pruebas y se estudia para una versión
  posterior.
- Enhanced FG **baja latencia sigue teniendo errores al girar la cámara**, menos en ×4 (sin DLSS 5): casi todos vienen de adivinar el
  siguiente movimiento de la cámara y crecen con lo lejos que va la última inventada. Para ×3 / ×4 con DLSS 5 se ve mejor el modo calidad.
- Enhanced FG ×4 baja latencia + DLSS 5 con captura 4K no cabe en una RTX 4060 Ti ni al 25 %: a los ~16 s pasa a ×2 esa sesión con un
  aviso (el perfil sigue con ×4).
- Smooth Motion ×4 + DLSS 5 a 1440p solo cabe con DLSS 5 al 25 % y DLSS K en una RTX 4060 Ti.
- La Xbox comparte con la PS5 el perfil "Consola" (uno para todas las consolas sin HOME).
- Todo lo de esta versión se ha medido con vídeo real de juego por el motor, aún no con la consola.

## [1.3] - 2026-11-23

**Everything in ConsoleEnhancer's own engine, without Magpie, mpv or NVIDIA DLSS Frame Generation**, with a new frame generation of
its own (Enhanced FG, experimental), Smooth Motion ×4, sharper upscaling in motion and faster console switching. Measured without the
console on an RTX 4060 Ti with a 4K 120 Hz VRR TV, with **real gameplay video** (public-domain and Creative Commons recordings, only
for testing) played through the engine like the capture card and the settings of the person who tests it with the console (DLSS 5
level 4, 4K and 1440p + DLSS M, 30 fps mode, 120 Hz VRR). Compared with 1.2.3. *En castellano, más abajo.*

### English

#### Changed
- **The whole app runs in the built-in engine.** Magpie and mpv are no longer used or downloaded: the picture, the console menu at
  native 4K, the detection grabs, the curtains, the low-power probe, the wizard's picture and sound tests and the console sound are all
  done by `ConsoleEnhancerEngine.exe`. **DLSS 5** (with your own `nvngx_dlssnr.dll`) always runs in the engine through
  `engine\PrismaNr.dll`, with 1 to 3 passes and any frame generation type. The first start downloads only the **Intel XeSS SDK 3.0.2**
  and the **AMD FSR SDK 2.3.0** (about 205 MB instead of ~670 MB); an old `magpie\` folder is reused for the Intel and AMD files. If a
  frame generation type loses the GPU, the rest of the session uses another one and the last picture stays on screen while it recovers.
- **You only pick the frame generation type**: Smooth Motion (RTX 40 / 50, the default there), XeSS FG, the new **Enhanced FG**
  (experimental, never the default) or none. Smooth Motion has a ×2 / ×4 **Multiplier** and Enhanced FG ×2 / ×3 / ×4 (30 fps mode);
  XeSS FG is ×2. Old profiles and settings with DLSS FG, FSR 3 FG or the old ×3 / ×4 move by themselves to the closest type (Smooth
  Motion on NVIDIA), keeping everything else, and the menu says why.
- **FSR 3 FG is no longer offered.** Measured against real gameplay video it was the worst of all (artifacts and poor smoothness: its
  generated frames barely move). `generacion_fsr3=1` in the ini brings it back as "FSR 3 FG (experimental)", with ×2 (AMD's) and
  ×3 / ×4 (`engine\CeFsrFg.dll`: our build of AMD's MIT-licensed FSR 3.1 frame interpolation, changed to generate several frames between
  two real ones). With real gameplay video Smooth Motion ×2 and XeSS FG ×2 generate about as well as each other, and both stay.
- **On-screen notices say what is really running**, taken from what the engine and the driver get, not from what was asked: the
  settings menu "Apply" lists only what changed (capture, frame generation type with its real multiplier and "30 → 120 fps", DLSS 5,
  upscaler) and, when something asked can't run, why (e.g. "Smooth Motion x2 · 30 → 60 fps · x4 unavailable: no VRR (G-SYNC) on the
  screen"). The same for the View / Menu / A / R3 / L3 / level / passes buttons, the D-pad capture, the tray and Ctrl+I. One notice per
  change: when the engine is swapped it shows on the new engine as it comes to the front; the automatic step-downs when the real frame
  rate isn't held show a single notice. Every on-screen notice is written to the log, and the notice pill can be wider at 4K.
- **Console switching is recognised at once.** A strong proof of the console on screen (the Switch 2 HOME with the game name in cyan
  and the clock with the battery, the PS5 menu clock or its home tabs, the Xbox home or guide with two of its phrases) switches on the
  second capture (~0.5 s; the Switch 2 after a PS5 took ~16 s); loose hints of the Xbox still need 3 proofs in 20 s. While the picture
  points to another console, nothing of the previous console's game recognition (covers, icons, names) runs, and the console remembered
  from last time creates no games, "Game N", icons, covers or names until the picture confirms it.
- **The engine reserves GPU time for its own frame generation** (FSR, and XeSS FG under Smooth Motion ×4), so the DLSS 5
  scale makes room for it.
- With DLSS 5 at 1440p / 1080p / 720p the default DLSS model is **M instead of K** (K is the blurriest model in motion on real game
  video); the model you pick in the menu still rules.
- **Smooth Motion: lower latency right after the picture starts.** The engine now also counts when the GPU really finishes each frame
  before it queues one for the TV: the first frame after starting or waking with DLSS 5 takes 0.6-0.8 s on the GPU and the ones behind
  it reached the TV together, adding 10-30 ms of latency for 15-25 s (8 of 12 start-ups; now 0 of 6). Reflex stays on with Smooth
  Motion.
- **Console sound through the engine** (`ConsoleEnhancerEngine.exe --audio`, a separate sound-only process): the same capture pin,
  format and 20 ms blocks, Windows audio output (WASAPI), the same 7.1 → stereo mix with a peak limiter, and clock-drift correction
  (no growing delay; measured click → output 78.5 ms, no dropouts). Log: `data\logs\audio.log`.
- **Detection grabs in colour** (with YUY2 / NV12 capture the engine gave the app grey pictures): the Switch 2 HOME is recognised again
  and the frozen-picture curtains are in colour.
- **MJPEG capture** (USB 2.0 cards such as MS2109) is decoded in the engine, up to 720p. H.264 / HEVC capture is no longer supported
  (pick an uncompressed format).
- **R3's alternative upscaler is FSR 1**; old profiles saved with the alternative upscaler get FSR 1, and the menu shows it.
- **Engine switches without black**: the leaving engine goes to standby and the cut waits for the new engine's 4th painted frame
  (~0.5 s); DLSS 5 is rebuilt without flashes or sudden colour changes; XeSS FG with DLSS SR presents through Streamline's swap-chain
  proxy (no warning or crash dump when the engine closes); the engine's GPU margin is 90 %.
- **The ini is brought up to date once** (from 1.2.3 or older, and from the 1.2.4 test builds): the new `base.ini` on top, the plan's
  frame generation with Smooth Motion, the plan's DLSS model tables with DLSS 5 (now M), and every setting that no longer exists removed;
  a copy stays in `consoleenhancer.ini.antes-1.3`.

#### Added
- **Enhanced FG (experimental): ConsoleEnhancer's own frame generation ×2 / ×3 / ×4 for any GPU with Direct3D 12**, written from
  scratch for the engine (no NVIDIA, AMD or Intel SDK or library): its own hierarchical optical flow in both directions (5 levels, with
  a forward / backward check for what gets covered and uncovered), a per-pixel choice of motion in every generated frame, the still HUD
  kept sharp, no blended frame on scene cuts, and its own present thread on its own GPU queue that shows every frame at a fixed time
  counted from the capture's arrival. Two modes: quality (interpolation) and **low latency** (the generated frames go forward from the
  latest real frame, shown as soon as it is ready: at 4K ×2 the real frame is on screen 9-10 ms after it arrives, against ~35 ms with
  Smooth Motion ×2). Measured with real gameplay video, 30 fps mode, 30 real frames in every case: ×2 60 fps, every frame for 2
  refreshes; **×4 120 fps, every frame for one refresh, also with DLSS 5** (4K with DLSS 5 at 65-80 %, and 1440p + DLSS M with DLSS 5
  at 90 %). Its generated frames against the true in-between frames of real gameplay video: GTA V 4.61, Counter-Strike 2 3.14, The
  Witcher 3 4.02, Fallout 4 6.59 (Smooth Motion ×2: 5.56 / 3.21 / 4.20 / 8.85), but in a third-person driving game they look close to a
  blend of the two real frames, so it is marked experimental. It needs a screen at 100 Hz or more (at 60 Hz it is not offered); its
  shaders are compiled in the background by the first engine that starts, so picking it is ready in ~5 s. If the GPU fails with it, it
  stays in the menu with the reason and the time, and picking it again retries.
- **Smooth Motion ×4** (30 fps mode, 120 Hz VRR screens, RTX 40 / 50; *Multiplier* ×4, off by default): Smooth Motion only ever adds
  one frame between two, so the engine runs XeSS FG ×2 underneath and Smooth Motion doubles it, 30 → 120. Measured with real gameplay
  video at 4K: **30 real frames, 119.7 frames/s, 99.4 % of them for exactly one refresh**. Also with DLSS 5: the engine lowers DLSS 5
  (down to 25 %) until ×4 keeps 30 real frames and says the % (4K, DLSS 5 level 4: 30 real frames at 35 %, 119 frames/s, 92 % for one
  refresh); if it can't, ×2 for the session with that capture, saying so (see *Known issues*). It needs VRR and XeSS FG in the engine;
  otherwise the row shows ×2 and why, and keeps your ×4, which comes back by itself when it can run. There is no good ×3 (90 fps can't
  be even on a 120 Hz screen).
- **×4 with DLSS 5 can be picked** (Smooth Motion ×4, Enhanced FG ×3 / ×4): a guard in the engine watches the real frames actually shown
  and lowers DLSS 5 10 points at a time until ×4 fits; the notice says the %. If the real frame rate can't be held, the first step down
  is the multiplier (×4 → ×2).
- **XeSS upscaling in the settings menu and in game profiles**, through the engine (also on NVIDIA).
- **The Switch 2 games of the older Switch-2-only version of the app are imported once** (with their settings and HOME icons), and the
  HOME screen reference if it was missing.
- For developers: the engine's file test source (`test=archivo:`, the capture card's stand-in) also plays compressed video (mp4 / mkv /
  webm: H.264, HEVC, VP9, AV1) decoded on the GPU and delivered like the capture card, so real game footage can drive the whole app
  without the console (`test_archivo_paso`, `test_archivo_desde` / `_hasta` / ...); `--test-avisos` checks the notice of every
  settings-menu combination against what the engine really gets; `--test-tipos-generacion`, `--test-smooth-x4`, `--test-switch2`;
  `build_fsrfg.bat` downloads only what it needs of AMD FidelityFX SDK v1.1.4 (commit `c6efa6b`), applies
  `parches\fidelityfx-fi-multi.patch` and builds `engine\CeFsrFg.dll`; the CI builds `PrismaNr.dll` and `CeFsrFg.dll` too.

#### Fixed
- **Upscalers no longer blur in motion or smear dark and low-detail areas (DLSS SR K / L / M / E, also with DLSS 5; XeSS, FSR).** The
  engine's motion vectors (an 8×8 block search on the console picture) were 0.5-1 px off where there is texture and stuck at zero with
  slow camera moves, so the upscaler blended a misaligned history. Each 8×8 tile now refines its vector to a fraction of a pixel for the
  upscaler, and a correction is used only where it is reliable (measured against the true picture, 1440p → 4K in motion: K 1.32 → 0.82,
  M 0.92 → 0.44, L 0.79 → 0.44, E 1.05 → 0.72; a dark night scene of real game video: DLSS M sharpness 0.42 → 0.82, DLSS 5 + M 0.52 →
  0.89). The transformer models (K, L, M) get a light CAS only where the picture moves (moving DLSS M 0.75 → 0.84, DLSS 5 + M 0.73 →
  0.87; still scenes unchanged). The capture goes to a temporal upscaler (and to DLSS 5 when an upscaler follows) without the edge
  smoothing / dither removal / flat denoise that smeared fine floor textures even with the camera still. About +1.3 ms of GPU at 1440p.
- **The DLSS model you pick is the one you get**: the engine replaced it with the model "learned last time" even when it was a more
  expensive one or a cheaper one for no reason, and the GPU spike of rebuilding DLSS 5 swapped M for K. Now it only steps down from
  what you asked for when that did not fit; with DLSS 5 the engine lowers the DLSS 5 percentage first.
- **The engine's frame generation keeps the character's head when the camera turns**: it gave the frame generation one camera vector
  for the whole picture, so the smooth parts of a third-person character were dragged half a camera step. It now tells the character
  and the HUD apart from the background, gives each its own motion and puts the character in front in the depth (with FSR 3 FG: head
  error −84 %, HUD text −91 %; XeSS FG, which already kept the head, as good or slightly better; about 0.6 ms of GPU at 4K).
- **Smooth Motion no longer freezes the picture for 1-3 s** after changing the capture (4K ↔ 1440p) or when an engine wakes up.
- **The 120 fps mode no longer reopens the engine with G-SYNC off** (it took 7-10 s more and then played with a fixed refresh).
- **Switch 2 after a PS5 (and the other way round)**: the Switch 2 took ~16 s to be recognised and meanwhile the PS5 logic saved a PS5
  game cover from the Switch 2 HOME (that cover is removed by itself); with the Switch 2 remembered and the PS5 on screen, a "Game 1"
  was created from the PS5 picture (removed by itself) and the PS5 took 76 s.
- **Switch 2 game names read in the HOME are kept for the profile**: the reads were lost when the cursor moved to another game, so
  entering it after coming back saved it as "Game 1". Now they last the whole session per icon; the "Game N" profiles saved by this bug
  get their real name from the log of that session.
- **The settings menu no longer closes as it opens** when opened with the controller's centre button (that press also arrived as Back
  2-5 ms later).
- Launching another game from the console menu switches the title and settings right away; a game name read with the ™ / ® mark no
  longer overwrites the good one; *Add DLSS 5* finds your own file if it is already on your PC (only locally) and says whether it is
  NVIDIA's signed original or the community-modified one.

#### Removed
- **NVIDIA DLSS Frame Generation (DLSS-G)** from the whole app (engine, menus, tray, Settings, wizard), with the community
  `dlssg_sm86` package, the old ×3 / ×4 multipliers, the engine's old own frame generation and the HUD mask. DLSS Super Resolution,
  DLSS 5 and Reflex are unchanged.
- **Magpie** and **mpv**: no longer downloaded or opened (`mpv\`, `scripts\*.lua`, the mpv shaders and `resources\mpv.conf` are gone).
  The "strong dither filter" (an mpv shader) is not in the engine yet (the normal one is).
- The upscalers only Magpie had (RTX Video, NVIDIA VFX, SGSR, CAS, Lanczos, Bicubic): FSR 1 or no upscaling in the engine.
- Settings that no longer do anything (accepted silently and removed from the ini on update): the DLSS FG / ×3 / ×4 / own-FG keys,
  `motor_dlss5`, `motor_dlss5_pasadas`, every `magpie_*`, `pasadas_vigilancia*`, `puente_ligero`, `telon_esperar_magpie_max_ms`,
  `rtx_hdr_metodo`, `mpv_path`, `mpv_cola_ms`, `mpv_cscale`, `mpv_sin_marco`, `captura_png`, `test_mpv_args`.

#### Settings to go back
Engine settings go at the end of the `motor_extra=` line of `consoleenhancer.ini`; the others, as their own line at the end of the file.
- In `motor_extra`: `sr_vectores=0` (upscaler without the refined vectors) · `sr_vec_incert=0` (refined vectors also where unreliable)
  · `sr_aa=1` / `sr_aa_dlss5=0` (edge smoothing / dither removal / flat denoise before a temporal upscaler / before DLSS 5 with an
  upscaler) · `sr_nitidez_mov=0` (no motion-only sharpening) · `sr_completos_xess=0` (XeSS / FSR with the light vectors) ·
  `adapta_modelo_sobre_pedido=1` · `fg_cabeza=0` · `cola_grafica=0` · `cola_sin_repintar=0` · `cola_tirar_seguidas=0` · `grab_gris=1` ·
  `sl_proxy_present=0` · `dlss5_suave=0` · `x4d5=0` (no ×4-with-DLSS-5 guard) · `x4d5_inicio=0` (Smooth Motion ×4 + DLSS 5 starts at
  what you asked instead of 35 %) · `xefg_prioridad_x4=0` (Smooth Motion ×4 + DLSS 5 with the engine queue at normal priority) ·
  `efg_prioridad=1` (Enhanced FG with the engine queue at normal priority: it hung the GPU with DLSS 5) · `efg_precompilar=0`.
- In the ini: `generacion_fsr3=1` (FSR 3 FG back, experimental) · `smooth_x4_dlss5=0` (Smooth Motion ×4 only without DLSS 5) ·
  `efg_multi_dlss5=0` (Enhanced FG ×2 with DLSS 5) · `motor_fsr_multi=0` / `motor_fsr_multi_dlss5=0` (FSR 3 FG only ×2 / ×3-×4 only
  without DLSS 5) · `relevo_pintadas=1` (engine switch cut at the first painted frame) · `audio_motor_extra=audio_cola_ms=30
  audio_cola_max_ms=100 audio_salida_ms=10 audio_deriva=1` (the sound process's defaults).

#### Known issues
- **Smooth Motion ×4 with DLSS 5 at 1440p capture (+ DLSS M)** doesn't fit on an RTX 4060 Ti even with DLSS 5 at 25 % (the upscaler and
  XeSS FG at 4K are the cost, not DLSS 5): after 20-25 s it goes ×2 for the session with a red notice (3 of 4 runs). Enhanced FG ×4 does
  fit there. At 4K capture it holds 30 at 35 %.
- Enhanced FG is experimental. For ×4 with DLSS 5 use its quality mode (every frame shown, DLSS 5 at 65-90 %): in low-latency mode
  ×3 / ×4 with DLSS 5 the guard lowers DLSS 5 (to 35 % or less at 4K) and some generated frames are still skipped (98 of 120 frames/s
  at 35 % in the last whole-app test). `efg_salto_fino=1` in `motor_extra` skips none at 65 %, but it hung the GPU once in testing.
- A memory overclock on the graphics card can hang the GPU with DLSS 5 plus frame generation even when games look fine.
- The Xbox has only been tested with official screenshots; the Switch (1) HOME looks like the Switch 2's (with it, `consola=switch1`).

#### Notes
- **DLSS 5 and the law (see the README)**: the repository and the zip carry the engine's own native piece `PrismaNr` (own code,
  GPL-3.0), which loads the `nvngx_dlssnr.dll` the user adds without going through NVIDIA's check (NGX), which NVIDIA's terms do not
  allow; the user decided to publish it knowing that. The app does not ship, download or link to any modified NVIDIA file, and *Add DLSS
  5* only looks on the user's own PC. PrismaNr is built with NVIDIA's two public NGX headers (pinned, SHA-256 checked), which are not in
  the repository. Without the user's file it does nothing.
- The zip also carries `engine\CeFsrFg.dll`: AMD FidelityFX SDK 1.1.4 code (MIT), modified by ConsoleEnhancer; AMD's notice is in
  `licenses\`. Smooth Motion ×3 / ×4 on its own and XeSS FG ×3 / ×4 outside Intel GPUs are not offered: they would need patching
  NVIDIA's driver or Intel's library.
- The gameplay video used for the measurements is only for testing: it is not in the repository or the zip.

### Castellano

**Todo en el motor propio de ConsoleEnhancer, sin Magpie, sin mpv y sin DLSS Frame Generation de NVIDIA**, con una generación de
imágenes propia nueva (Enhanced FG, experimental), Smooth Motion ×4, reescalado más nítido en movimiento y cambio de consola al momento.
Medido sin la consola en una RTX 4060 Ti con una tele 4K 120 Hz con VRR, con **vídeo real de juego** (grabaciones de dominio público y
Creative Commons, solo para medir) metido en el motor como si fuera la capturadora y los ajustes de quien la prueba con la consola
(DLSS 5 nivel 4, 4K y 1440p + DLSS M, modo 30, 120 Hz con VRR). Comparado con la 1.2.3.

#### Cambiado
- **Toda la app va por el motor propio.** Ya no usa ni baja Magpie ni mpv: la imagen, el menú de la consola a 4K nativo, las capturas
  para la detección, los telones, la sonda del bajo consumo, las pruebas de imagen y sonido del asistente y el sonido de la consola los
  hace `ConsoleEnhancerEngine.exe`. **DLSS 5** (con tu propia `nvngx_dlssnr.dll`) va siempre en el motor por `engine\PrismaNr.dll`, de 1
  a 3 pasadas y con cualquier tipo de generación. La primera vez solo se bajan el **SDK de Intel XeSS 3.0.2** y el **SDK de AMD FSR
  2.3.0** (unos 205 MB en vez de ~670); de una carpeta `magpie\` de antes se reaprovechan los archivos de Intel y AMD. Si un tipo de
  generación pierde la gráfica, el resto de la sesión va otro, con la última imagen en pantalla mientras se recupera.
- **Solo se elige el tipo de generación**: Smooth Motion (RTX 40 / 50, de serie en ellas), XeSS FG, la nueva **Enhanced FG**
  (experimental, nunca de serie) o ninguna. Smooth Motion tiene **Multiplicador** ×2 / ×4 y Enhanced FG ×2 / ×3 / ×4 (modo 30); XeSS FG
  va ×2. Los perfiles y ajustes viejos con DLSS FG, FSR 3 FG o los ×3 / ×4 de antes pasan solos al tipo más parecido (Smooth Motion en
  NVIDIA), con todo lo demás igual, y el menú dice por qué.
- **FSR 3 FG ya no se ofrece.** Medida con vídeo real de juego era la peor de todas (fallos y poca fluidez: sus inventadas casi no se
  mueven). `generacion_fsr3=1` en el ini la vuelve a poner como «FSR 3 FG (experimental)», con ×2 (la de AMD) y ×3 / ×4
  (`engine\CeFsrFg.dll`: la interpolación de FSR 3.1 de AMD, código MIT, compilada aquí y cambiada para inventar varias imágenes entre dos
  reales). Con vídeo real Smooth Motion ×2 y XeSS FG ×2 inventan parecido y se quedan las dos.
- **Los avisos en pantalla dicen lo que va de verdad**, sacado de lo que reciben el motor y el driver y no de lo pedido: el «Aplicar» del
  menú de ajustes dice solo lo que cambia (captura, tipo de generación con su multiplicador de verdad y «30 → 120 fps», DLSS 5,
  escalador) y, si algo de lo pedido no puede ir, por qué (p. ej. «Smooth Motion x2 · 30 → 60 fps · x4 no: sin VRR (G-SYNC) en la
  pantalla»). Lo mismo con los botones Vista / Menú / A / R3 / L3 / nivel / pasadas, la cruceta, la bandeja y Ctrl+I. Un aviso por
  cambio: con cambio de motor sale en el nuevo al ponerse delante; las bajadas solas cuando no se llega a las reales dan un solo aviso.
  Todos los avisos quedan en el log y la pastilla de avisos puede ser más ancha a 4K.
- **El cambio de consola se reconoce al momento.** Una prueba fuerte de la consola en pantalla (el HOME de la Switch 2 con el nombre del
  juego en cian y la hora con la batería, la hora del menú de la PS5 o las pestañas de su inicio, el Inicio o la guía de la Xbox con dos
  frases suyas) cambia a la segunda captura (~0,5 s; la Switch 2 tras la PS5 tardaba ~16 s); lo suelto de la Xbox sigue pidiendo 3
  pruebas en 20 s. Mientras la imagen apunte a otra consola no actúa nada del reconocimiento de juegos de la anterior (portadas, iconos,
  nombres), y la consola recordada de la vez anterior no crea juegos, «Juego N», iconos, portadas ni nombres hasta que la imagen la
  confirme.
- **El motor reserva la gráfica de su propia generación** (FSR, y XeSS FG debajo de Smooth Motion ×4), para que la escala de DLSS 5 le
  haga sitio.
- Con DLSS 5 a 1440p / 1080p / 720p el modelo de DLSS de serie es **M y no K** (K es el más borroso en movimiento con vídeo real de
  juego); el que eliges en el menú sigue mandando.
- **Smooth Motion: menos latencia justo al empezar la imagen.** El motor cuenta también cuándo acaba de verdad la gráfica cada imagen
  antes de mandar una a la cola de la tele: la primera tras arrancar o despertar con DLSS 5 tarda 0,6-0,8 s en la gráfica y las de detrás
  llegaban juntas a la tele, con 10-30 ms de latencia de más durante 15-25 s (8 de 12 arranques; ahora 0 de 6). Reflex sigue puesto con
  Smooth Motion.
- **El sonido de la consola, por el motor** (`ConsoleEnhancerEngine.exe --audio`, un proceso aparte solo de sonido): el mismo pin de la
  capturadora, formato y bloques de 20 ms, la salida de audio de Windows (WASAPI), la misma mezcla 7.1 → estéreo con limitador y la
  corrección de la deriva de reloj (sin retraso que crezca; medido clic → salida 78,5 ms, sin cortes). Log: `data\logs\audio.log`.
- **Capturas para la detección en color** (con captura YUY2 / NV12 el motor le daba a la app imágenes en gris): el HOME de la Switch 2
  se vuelve a reconocer y los telones con la imagen congelada salen en color.
- **Captura MJPEG** (capturadoras USB 2.0 como la MS2109): la descomprime el motor, hasta 720p. La captura H.264 / HEVC ya no vale
  (elige un formato sin comprimir).
- **El escalador alternativo de R3 es FSR 1**; los perfiles guardados con el alternativo pasan a FSR 1 y el menú lo enseña.
- **Cambios de motor sin negro**: el motor que se va pasa a la espera y el corte espera a la 4ª imagen pintada del nuevo (~0,5 s); DLSS 5
  se rehace sin destellos ni cambios bruscos de color; XeSS FG con DLSS SR presenta por el proxy de la cadena de Streamline (sin aviso ni
  volcado al cerrar el motor); el margen de gráfica del motor es el 90 %.
- **El ini se pone al día una vez** (desde la 1.2.3 o anteriores, y desde las versiones de prueba de la 1.2.4): el `base.ini` nuevo
  arriba, la generación del plan con Smooth Motion, las tablas del modelo de DLSS con DLSS 5 (ahora M) y fuera todos los ajustes que ya no
  existen; queda una copia en `consoleenhancer.ini.antes-1.3`.

#### Añadido
- **Enhanced FG (experimental): la generación de imágenes propia de ConsoleEnhancer ×2 / ×3 / ×4 para cualquier gráfica con Direct3D
  12**, escrita desde cero para el motor (sin SDK ni librería de NVIDIA, AMD ni Intel): su propio flujo óptico jerárquico en los dos
  sentidos (5 niveles, con comprobación de ida y vuelta para lo que se tapa y se destapa), la elección del movimiento punto a punto en
  cada inventada, el HUD quieto nítido, sin imagen mezclada en los cortes de escena, y su propio hilo de presentación en su propia cola
  de la gráfica que enseña cada imagen a una hora fija contada desde la llegada de la captura. Dos modos: calidad (interpolación) y **baja
  latencia** (las inventadas van hacia delante desde la última real, que sale en cuanto está: a 4K ×2 la real está en pantalla 9-10 ms
  después de llegar, frente a ~35 ms con Smooth Motion ×2). Medido con vídeo real de juego, modo 30, con las 30 reales siempre: ×2 60
  imágenes/s, todas a 2 refrescos; **×4 120 imágenes/s, todas a un refresco, también con DLSS 5** (4K con DLSS 5 al 65-80 %, y 1440p +
  DLSS M con DLSS 5 al 90 %). Sus inventadas contra las de en medio de verdad del vídeo real: GTA V 4,61, Counter-Strike 2 3,14, The
  Witcher 3 4,02, Fallout 4 6,59 (Smooth Motion ×2: 5,56 / 3,21 / 4,20 / 8,85), pero en un juego en tercera persona conduciendo se
  parecen a una mezcla de las dos reales, y por eso va como experimental. Necesita una pantalla a 100 Hz o más (a 60 Hz no se ofrece);
  sus sombreadores los compila de fondo el primer motor que se abre, así que al elegirla está lista en ~5 s. Si la gráfica se cae con
  ella, sigue en el menú con el motivo y la hora, y elegirla otra vez es reintentar.
- **Smooth Motion ×4** (modo 30, pantallas de 120 Hz con VRR, RTX 40 / 50; *Multiplicador* ×4, de serie no): Smooth Motion solo añade
  una imagen entre dos, así que el motor pone XeSS FG ×2 debajo y Smooth Motion lo dobla, 30 → 120. Medido con vídeo real de juego a 4K:
  **30 reales, 119,7 imágenes/s, el 99,4 % a un refresco justo**. También con DLSS 5: el motor baja DLSS 5 (hasta el 25 %) hasta que ×4
  mantiene las 30 reales y dice el % (4K, DLSS 5 nivel 4: 30 reales al 35 %, 119 imágenes/s, el 92 % a un refresco); si no llega, ×2
  esa sesión con esa captura, y lo dice (ver *Problemas conocidos*). Necesita VRR y XeSS FG en el motor; si no, la fila enseña ×2 y por
  qué y guarda tu ×4, que vuelve solo cuando puede ir. No hay un ×3 bueno (90 imágenes/s no van parejas en una pantalla de 120 Hz).
- **×4 con DLSS 5 se puede elegir** (Smooth Motion ×4, Enhanced FG ×3 / ×4): un vigilante del motor mira las reales que se ven de verdad
  y baja DLSS 5 de 10 en 10 puntos hasta que ×4 cabe; el aviso dice el %. Si no se llega a las reales, lo primero que se baja es el
  multiplicador (×4 → ×2).
- **Reescalado XeSS en el menú de ajustes y en los perfiles de juego**, por el motor (también en NVIDIA).
- **Los juegos de la Switch 2 de la versión de antes de la app (la que era solo para la Switch 2) se importan una vez** (con sus ajustes y
  sus iconos del HOME), y la referencia del HOME si faltaba.
- Para desarrolladores: la fuente de prueba de archivo del motor (`test=archivo:`, la que hace de capturadora) reproduce también vídeo
  comprimido (mp4 / mkv / webm: H.264, HEVC, VP9, AV1) descomprimido en la gráfica y entregado como la capturadora, para mover la app
  entera con vídeo real de juego sin la consola (`test_archivo_paso`, `test_archivo_desde` / `_hasta` / ...); `--test-avisos` comprueba
  el aviso de cada combinación del menú de ajustes contra lo que recibe el motor de verdad; `--test-tipos-generacion`,
  `--test-smooth-x4`, `--test-switch2`; `build_fsrfg.bat` baja solo lo necesario del FidelityFX SDK v1.1.4 de AMD (commit `c6efa6b`),
  aplica `parches\fidelityfx-fi-multi.patch` y compila `engine\CeFsrFg.dll`; el CI compila también `PrismaNr.dll` y `CeFsrFg.dll`.

#### Arreglado
- **Los escaladores ya no emborronan en movimiento ni lo oscuro y con poco detalle (DLSS SR K / L / M / E, también con DLSS 5; XeSS,
  FSR).** Los vectores de movimiento del motor (una búsqueda por bloques de 8×8 en la imagen de la consola) iban 0,5-1 px desviados donde
  hay textura y se quedaban a cero con movimientos lentos de cámara, y el escalador mezclaba un historial mal alineado. Ahora cada
  bloque de 8×8 afina su vector a una fracción de píxel para el escalador, y la corrección solo se usa donde es fiable (medido contra la
  imagen de verdad, 1440p → 4K en movimiento: K 1,32 → 0,82, M 0,92 → 0,44, L 0,79 → 0,44, E 1,05 → 0,72; una escena nocturna oscura de
  vídeo real: nitidez de DLSS M 0,42 → 0,82, DLSS 5 + M 0,52 → 0,89). Los modelos transformer (K, L, M) llevan un CAS suave solo donde
  la imagen se mueve (DLSS M en movimiento 0,75 → 0,84, DLSS 5 + M 0,73 → 0,87; lo quieto igual). La captura llega al escalador temporal
  (y a DLSS 5 cuando detrás va un escalador) sin el suavizado de bordes / quitar tramado / quitar ruido en lo liso que emborronaba las
  texturas finas del suelo incluso con la cámara quieta. Unos +1,3 ms de gráfica a 1440p.
- **El modelo de DLSS que eliges es el que va**: el motor lo cambiaba por el «aprendido la vez anterior» aunque fuera uno más caro o uno
  más barato sin motivo, y el pico de gráfica al rehacer DLSS 5 cambiaba M por K. Ahora solo baja de lo pedido cuando no cabía; con DLSS 5
  el motor baja primero el % de DLSS 5.
- **La generación del motor mantiene la cabeza del personaje al girar la cámara**: le daba un solo vector de cámara para toda la imagen,
  y las partes lisas de un personaje en tercera persona se arrastraban medio paso de cámara. Ahora separa al personaje y el HUD del
  fondo, da a cada uno su movimiento y pone al personaje delante en la profundidad (con FSR 3 FG: error en la cabeza −84 %, en el texto
  del HUD −91 %; XeSS FG, que ya la mantenía, igual o algo mejor; unos 0,6 ms de gráfica a 4K).
- **Smooth Motion ya no deja la imagen quieta 1-3 s** al cambiar la captura (4K ↔ 1440p) o al despertar un motor.
- **El modo 120 ya no vuelve a abrir el motor con el G-SYNC quitado** (tardaba 7-10 s más y luego iba con refresco fijo).
- **Switch 2 tras la PS5 (y al revés)**: la Switch 2 tardaba ~16 s en reconocerse y mientras la PS5 guardaba la portada de un juego suyo
  sacada del HOME de la Switch 2 (esa portada se quita sola); con la Switch 2 recordada y la PS5 en pantalla se creaba un «Juego 1» con la
  imagen de la PS5 (se quita solo) y la PS5 tardaba 76 s.
- **El nombre de cada juego de la Switch 2 leído en el HOME sirve para su perfil**: las lecturas se perdían al mover el cursor a otro
  juego y, al volver a él y entrar, se guardaba como «Juego 1». Ahora duran toda la sesión por icono; los «Juego N» guardados por este
  fallo pasan a su nombre, sacado del log de esa sesión.
- **El menú de ajustes ya no se cierra al abrirlo** con el botón central del mando (esa pulsación llegaba también como Atrás 2-5 ms
  después).
- Lanzar otro juego desde el menú de la consola cambia el título y los ajustes al momento; un nombre leído con la ™ / ® pegada ya no
  pisa el bueno; *Añadir DLSS 5* encuentra tu archivo si ya está en tu PC (solo en local) y dice si es el original firmado de NVIDIA o el
  modificado por la comunidad.

#### Quitado
- **DLSS Frame Generation de NVIDIA (DLSS-G)** de toda la app (motor, menús, bandeja, Settings, asistente), con el paquete de la
  comunidad `dlssg_sm86`, los ×3 / ×4 de antes, la generación propia vieja del motor y la máscara del HUD. DLSS Super Resolution, DLSS 5
  y Reflex siguen igual.
- **Magpie** y **mpv**: ya no se bajan ni se abren (fuera `mpv\`, `scripts\*.lua`, los shaders de mpv y `resources\mpv.conf`). El «filtro
  de tramado fuerte» (un shader de mpv) aún no está en el motor (el normal sí).
- Los escaladores que solo tenía Magpie (RTX Video, NVIDIA VFX, SGSR, CAS, Lanczos, Bicubic): FSR 1 o sin reescalar en el motor.
- Ajustes que ya no hacen nada (se aceptan sin decir nada y se quitan del ini al ponerlo al día): las claves de DLSS FG / ×3 / ×4 /
  generación propia, `motor_dlss5`, `motor_dlss5_pasadas`, todas las `magpie_*`, `pasadas_vigilancia*`, `puente_ligero`,
  `telon_esperar_magpie_max_ms`, `rtx_hdr_metodo`, `mpv_path`, `mpv_cola_ms`, `mpv_cscale`, `mpv_sin_marco`, `captura_png`,
  `test_mpv_args`.

#### Ajustes para volver atrás
Los del motor van al final de la línea `motor_extra=` de `consoleenhancer.ini`; los demás, como línea propia al final del fichero.
- En `motor_extra`: `sr_vectores=0` (escalador sin los vectores afinados) · `sr_vec_incert=0` (los vectores afinados también donde no
  son fiables) · `sr_aa=1` / `sr_aa_dlss5=0` (suavizado de bordes / quitar tramado / quitar ruido en lo liso antes de un escalador
  temporal / antes de DLSS 5 con escalador) · `sr_nitidez_mov=0` (sin la nitidez solo en movimiento) · `sr_completos_xess=0` (XeSS / FSR
  con los vectores ligeros) · `adapta_modelo_sobre_pedido=1` · `fg_cabeza=0` · `cola_grafica=0` · `cola_sin_repintar=0` ·
  `cola_tirar_seguidas=0` · `grab_gris=1` · `sl_proxy_present=0` · `dlss5_suave=0` · `x4d5=0` (sin el vigilante de ×4 con DLSS 5) ·
  `x4d5_inicio=0` (Smooth Motion ×4 + DLSS 5 empieza en lo pedido y no en el 35 %) · `xefg_prioridad_x4=0` (Smooth Motion ×4 + DLSS 5
  con la cola del motor en prioridad normal) · `efg_prioridad=1` (Enhanced FG con la cola del motor en prioridad normal: con DLSS 5
  colgaba la gráfica) · `efg_precompilar=0`.
- En el ini: `generacion_fsr3=1` (FSR 3 FG otra vez, experimental) · `smooth_x4_dlss5=0` (Smooth Motion ×4 solo sin DLSS 5) ·
  `efg_multi_dlss5=0` (Enhanced FG ×2 con DLSS 5) · `motor_fsr_multi=0` / `motor_fsr_multi_dlss5=0` (FSR 3 FG solo ×2 / ×3-×4 solo sin
  DLSS 5) · `relevo_pintadas=1` (el corte del cambio de motor en la primera imagen pintada) · `audio_motor_extra=audio_cola_ms=30
  audio_cola_max_ms=100 audio_salida_ms=10 audio_deriva=1` (lo de serie del proceso de sonido).

#### Problemas conocidos
- **Smooth Motion ×4 con DLSS 5 y captura a 1440p (+ DLSS M)** no cabe en una RTX 4060 Ti ni con DLSS 5 al 25 % (lo que cuesta es el
  escalador y XeSS FG a 4K, no DLSS 5): a los 20-25 s pasa a ×2 esa sesión con un aviso en rojo (3 de 4 pasadas). Enhanced FG ×4 sí cabe
  ahí. Con captura a 4K aguanta las 30 al 35 %.
- Enhanced FG es experimental. Para ×4 con DLSS 5, su modo calidad (se ven todas, DLSS 5 al 65-90 %): en baja latencia ×3 / ×4 con
  DLSS 5 el vigilante baja DLSS 5 (al 35 % o menos a 4K) y aún se saltan inventadas (98 de 120 imágenes/s al 35 % en la última prueba de
  la app entera). `efg_salto_fino=1` en `motor_extra` no se salta ninguna al 65 %, pero colgó la gráfica una vez en las pruebas.
- Subir el reloj de la memoria de la gráfica puede colgarla con DLSS 5 y generación de imágenes aunque los juegos vayan bien.
- La Xbox solo se ha probado con capturas oficiales; el HOME de la Switch (1) se parece al de la Switch 2 (con ella, `consola=switch1`).

#### Notas
- **DLSS 5 y la ley (ver el README)**: el repositorio y el zip llevan la pieza nativa propia del motor `PrismaNr` (código propio,
  GPL-3.0), que carga la `nvngx_dlssnr.dll` que pone el usuario sin pasar por la comprobación de NVIDIA (NGX), cosa que los términos de
  NVIDIA no permiten; el usuario decidió publicarla sabiéndolo. La app no reparte, baja ni enlaza ningún archivo de NVIDIA modificado, y
  *Añadir DLSS 5* solo busca en el PC del propio usuario. PrismaNr se compila con las dos cabeceras públicas de NGX de NVIDIA (versión
  fija, con su SHA-256), que no van en el repositorio. Sin el archivo del usuario no hace nada.
- El zip lleva también `engine\CeFsrFg.dll`: código del AMD FidelityFX SDK 1.1.4 (MIT), cambiado por ConsoleEnhancer; el aviso de AMD
  va en `licenses\`. Smooth Motion ×3 / ×4 por sí solo y XeSS FG ×3 / ×4 fuera de las gráficas Intel no se ofrecen: harían falta parchear
  el driver de NVIDIA o la librería de Intel.
- El vídeo de juego usado para medir es solo para las pruebas: no va en el repositorio ni en el zip.

## [1.2.3] - 2026-11-22

### Added / Añadido
- **DLSS 5 in the app's own engine, the lowest-latency path**, on PCs that have the engine's optional native helper
  (`engine\PrismaNr.dll`, not shipped: without it nothing changes or shows up). With it, DLSS 5 (1 pass) is done by the engine straight
  from the capture card instead of mpv + Windows capture + Magpie, and that is the default there (`motor_dlss5=auto`). Measured on an
  RTX 4060 Ti with a 4K 120 Hz VRR TV, 30 fps mode + DLSS FG ×2, DLSS 5 level 2: **4K real frame 43 ms / generated 27 ms, every frame
  shown for 2 refreshes** (5329 of 5330 in 100 s), against ~70-73 / ~56-59 ms and a much less regular pace through Magpie; 1440p → 4K
  42 / 25 ms; at 4K it also fits more DLSS 5 input (70 % vs 40-45 %). New row in the settings menu, **DLSS 5 through: Engine · lower
  latency / Magpie · more options**, and in the tray. What the engine doesn't do (2-3 passes, ×3 / ×4, XeSS FG, NVIDIA VFG) still goes
  through Magpie without a black screen, and the row says so.
  *DLSS 5 en el motor propio, la vía con menos retraso, en los PC que tengan su complemento nativo (`engine\PrismaNr.dll`, no se
  distribuye; sin él nada cambia ni se ve). Medido (RTX 4060 Ti, tele 4K 120 Hz con VRR, modo 30 + DLSS FG ×2): a 4K 43 ms la imagen
  real y 27 la inventada, todas a 2 refrescos, frente a ~70-73 / ~56-59 ms y un ritmo mucho menos regular por Magpie. Fila nueva en el
  menú de ajustes, "DLSS 5 por: Motor · menos retraso / Magpie · más opciones", y en la bandeja. Lo que el motor no hace (2-3 pasadas,
  ×3 / ×4, XeSS FG) sigue por Magpie sin telón.*
- **Your DLSS 5 files survive a clean update**: `extras\nvngx_dlssnr.dll`, `extras\runtimes\dlssg_sm86` and `engine\PrismaNr.dll` are
  also kept in `%LOCALAPPDATA%\ConsoleEnhancer\extras`, and a new version unzipped into a new folder picks them up by itself (checked:
  64-bit DLL and its size, dlssg_sm86 against Magpie's manifest), saying so in the log. *Add DLSS 5* also takes `PrismaNr.dll` when it is
  next to the chosen DLL; *Remove DLSS 5* and uninstalling with "delete everything" remove that copy too.
  *Tus archivos de DLSS 5 sobreviven a una actualización limpia: se guardan también en `%LOCALAPPDATA%\ConsoleEnhancer\extras` y una
  versión nueva en otra carpeta los recoge sola (comprobados), y lo dice el log.*
- **Version and publisher in the exes** (*Properties > Details*, Task Manager, Startup apps): ConsoleEnhancer.exe, ConsoleEnhancer
  Settings.exe and ConsoleEnhancerEngine.exe carry version 1.2.3, publisher IanRog29 and the licence, plus their own application
  manifest (current user, Windows 10/11, the same DPI handling as before); they were 0.0.0.0 with no publisher. Each release also
  lists the **SHA-256 of every file** (`ConsoleEnhancer-vX.Y.Z.files.sha256`). For maintainers: optional code signing in the release
  workflow and `scripts\firmar.ps1`.
  *Versión y editor en las propiedades de los tres exe, con su manifiesto (antes 0.0.0.0 sin editor), y el SHA-256 de cada fichero de
  la release. Para mantenedores: firma de código opcional.*

### Fixed / Arreglado
- **Microsoft Defender no longer has a reason to remove a freshly downloaded version.** Up to 1.2.2 a new copy that inherited *Start
  with Windows* pointed the Startup shortcut to itself a fraction of a second after opening; for a new, unsigned exe that is exactly
  what Defender's behaviour detection blocks (it quarantined 1.2.07 and 1.2.09 that way on 21-11; the very same files scan clean).
  Now the app never adds itself to the startup on its own: with *Start with Windows* on and the entry pointing to another copy (or
  missing), it asks once when you open it yourself (Yes = this copy, No = off). The old logon task is checked and removed through the
  Task Scheduler API (no more `schtasks.exe` on every start), and **uninstalling no longer uses PowerShell**: the app deletes its own
  files when it closes (its running exe goes to the temporary folder and is deleted the next time any copy opens) and says what it
  could not delete. Without a code-signing certificate a new exe can still start without reputation: see the FAQ.
  *Microsoft Defender ya no tiene motivo para borrar una versión recién bajada: hasta la 1.2.2 una copia nueva que heredaba «Iniciar con
  Windows» cambiaba el acceso directo de Inicio a sí misma nada más abrirse, justo lo que Defender bloquea en un exe nuevo sin firmar.
  Ahora nunca se pone sola: pregunta una vez al abrirla tú. Y desinstalar ya no usa PowerShell.*
- **16.7 ms less latency in the 30 fps mode in many cases**: the repeated-frame filter could stay one refresh late for good (every
  frame 16.7 ms late) once it had passed a late copy: with a steady 30 fps game whenever it started on the second copy of a frame
  (half of the time) and after every uneven frame pair. It now gets back on time with the next frame, both in mpv (the Magpie path)
  and in the app's own engine (except with Smooth Motion). Measured on synthetic cadences: steady 30 fps 100 % of the frames late →
  0-6 %, uneven 3,1 pairs 93-95 % → 46-48 %; 45 fps content still goes out at ~30 fps.
  *16,7 ms menos de latencia en el modo 30 en muchos casos: el filtro de repetidas podía quedarse un refresco tarde para siempre; ahora
  vuelve a tiempo en la imagen siguiente, en mpv (camino de Magpie) y en el motor propio (salvo con Smooth Motion).*
- **DLSS FG ×3 / ×4 at 30 fps: no more real-frame drops with DLSS 5, and much better generated frames at 4K.** What kept the 30 fps
  mode from 30 real frames was not a full GPU (50-77 % busy) but Magpie's image thread, which runs in series (capture, optical flow,
  DLSS 5, then each generated frame). The automatic DLSS 5 scale now models that thread and picks a scale that holds 30 (4K: ~35-45 %
  with ×2, 35 % with ×3, measured 29.96 real fps); with ×3 / ×4 DLSS 5 never goes below 35 % (at 25 % of 4K it was a 960x540
  picture, part of the artefacts) and is removed when it doesn't fit, which the menu says. DLSS FG ×3 / ×4 at 4K now gets NVIDIA
  optical-flow vectors even without DLSS 5: without them its generated frames had about 3× the error. Measured: DLSS FG ×4 29.94 /
  119.9 fps with 98 % of the frames on one refresh.
  *DLSS FG ×3 / ×4 a 30 fps: sin imágenes reales perdidas con DLSS 5 y mejores inventadas a 4K. La escala de DLSS 5 cuenta ahora el hilo
  de Magpie (en serie), DLSS 5 no baja del 35 % con ×3 / ×4 (al 25 % era una imagen de 960x540) y DLSS FG ×3 / ×4 a 4K recibe vectores
  de NVIDIA también sin DLSS 5 (sin ellos, 3 veces más error).*
- **XeSS FG ×3 / ×4 stutter**: on NVIDIA / AMD, Magpie paces XeSS FG ×3 / ×4 in a compatibility mode that waits inside each present,
  so the rest of the chain only gets 1/3 or 1/4 of a frame; when it doesn't fit, real frames drop (×4 at 4K on an RTX 4060 Ti: 27.4 of
  30). ×3 / ×4 are now offered only where the chain fits (4K on a 4060 Ti: ×3 yes, ×4 no); otherwise they show up locked with the
  reason. `xess_fg_multi_cabe=0` = as before.
  *XeSS FG ×3 / ×4 a saltos: ahora solo donde la cadena cabe (a 4K en una 4060 Ti, ×3 sí y ×4 no); si no, bloqueados con el motivo.*
- **Opening the app with a game already running: one trip to the console home is enough to know which game it is.** When you open
  the PS5 home, the game in focus is the one running; before, a short visit (2-3 s) often read nothing (the home was still appearing,
  and while the app switched to the menu it took no captures at all). Now it keeps reading the game in focus while switching and every
  0.25 s until it knows it, and that game becomes the running one: leaving the menu applies its profile. Recognising a game without
  any menu visit was tried and not shipped: it couldn't be made reliable enough. A new console profile also keeps the saved 30 / 60 /
  120 mode (it was created with mode 60).
  *Abrir la app con un juego ya en marcha: basta con ir una vez al inicio de la consola (antes una visita corta no leía nada). Reconocer
  el juego sin pasar por el menú se probó y no se activa: no salía fiable. Y un perfil nuevo de la consola respeta el modo guardado.*
- **The stutter log no longer blames Windows capture for what isn't a stutter**: Magpie's first seconds after (re)opening, the settings
  menu or another window over the picture, a still picture and frames Magpie drops itself with its queue full are told apart.
  *El log de tirones ya no culpa a la captura de Windows de lo que no es un tirón.*
- Also: with DLSS 5 in the engine and ×3 / ×4 or 2-3 passes, Magpie ran without its automatic DLSS 5 scale; the engine bridging to
  Magpie drops its own DLSS 5 meanwhile (8 GB cards); if the engine's DLSS 5 fails, DLSS 5 goes through Magpie for that session; the
  engine's DLSS 5 % goes up in one step instead of 5 points every 20 s (each change froze the picture ~0.2 s); consoles without their
  own menu at 4K check it every 1.5 s instead of 0.6 s.
  *También: el reparto de casos con DLSS 5 en el motor, el puente suelta su DLSS 5, el % de DLSS 5 del motor sube de una vez y las
  consolas sin menú propio a 4K lo miran cada 1,5 s.*

### Changed / Cambiado
- **Smoother 30 fps mode through Magpie on 120 Hz screens**: DLSS FG ×2 now uses the same fixed "every 2 refreshes" sync as XeSS FG
  instead of VRR, and the 30 fps mode without frame generation uses it too instead of Windows composition. Measured on a 4K 120 Hz
  VRR TV with a PS5-like 30 fps source and DLSS 5: with DLSS FG ×2, 97 % of the shown frames land on time (67 % with VRR), the worst
  gaps drop from ~49 to 25-33 ms and Magpie loses 0.2 % of the real frames instead of 4 %, with the same latency; without frame
  generation the worst gaps drop from 67-75 to 33 ms. ×3 / ×4 keep VRR, and so does any refresh rate other than 120 Hz.
  `magpie_fijo_30_dlss=0` / `magpie_fijo_30=0` = as before.
  *El modo 30 por Magpie, más fluido con pantallas de 120 Hz: DLSS FG ×2 (y el modo 30 sin generación) con la sincronización fija "cada
  2 refrescos" de XeSS FG en vez de VRR: 97 % de las imágenes a su sitio (67 % por VRR) y el 0,2 % de reales perdidas en vez del 4 %.*
- **More GPU used where it really is free**: the automatic DLSS 5 scale lets the GPU fill up to 88 % (was ~75 %) and Magpie's image
  thread up to 95 % of each real frame (`dlss5_escala_hilo`, `dlss5_escala_grafica`; the old `dlss5_escala_uso` no longer applies), so
  at 1440p DLSS 5 goes up (e.g. XeSS FG ×2 55 → 65 %, DLSS FG ×3 25 → 40 %) and at 4K it is the scale that actually reaches 30 real
  fps. What was learned with the old model is relearned.
  *La escala de DLSS 5 usa más gráfica donde sobra de verdad (88 % de gráfica, 95 % del hilo de Magpie): a 1440p sube; a 4K es la que
  llega a las 30 reales.*

## [1.2.2] - 2026-11-22

### Added / Añadido
- **×3 and ×4 frame generation with DLSS FG and XeSS FG.** A new **Multiplier** row in the on-screen settings menu (it only shows up
  with DLSS FG or XeSS FG; Smooth Motion, FSR 3 FG and NVIDIA VFG stay at ×2), **Frame generation multiplier** in the tray and
  **Ctrl+X** over the picture. It is saved per game and per mode, like the generation type. Only what fits your screen is offered:
  real fps × multiplier can't go over the refresh rate, so with a 120 Hz TV ×3 / ×4 need the 30 fps mode (90 / 120 fps) and show up
  **locked, with the reason**, in the 60 fps mode; 144 / 165 / 180 / 240 Hz screens get their own limits, and without VRR ×3 is locked
  (90 fps on 120 Hz judders). Going from the 30 to the 60 fps mode drops to ×2 on the fly and says so; going back to 30 brings the
  game's ×3 / ×4 back. ×3 / ×4 never use the "every 2 refreshes" sync (it capped the output at 60): they go through VRR, or forced
  V-Sync without VRR. DLSS FG ×3 / ×4 runs through Magpie (the app's own engine does ×2) and needs the dlssg_sm86 component, which the
  app now fetches by itself whenever it is needed. Measured on an RTX 4060 Ti: DLSS FG ×4 = 120 fps with every frame shown for one
  refresh, ×3 = 90 fps. If the output can't be reached, the multiplier is the first thing lowered (4 → 3 → 2), with a notice; if Magpie
  applies less than asked, a notice too. `generacion_multiplicador=0` goes back to ×2 only.
  *×3 y ×4 con DLSS FG y XeSS FG: fila "Multiplicador" en el menú de ajustes (solo con esas dos), en la bandeja y con Ctrl+X, por juego y
  por modo. Solo se ofrece lo que cabe en la pantalla: con una tele de 120 Hz, ×3 / ×4 necesitan el modo 30 (90 / 120 fps) y en el de 60
  salen bloqueados con el motivo; sin VRR, ×3 bloqueado. Al pasar al modo 60 baja a ×2 y lo dice; al volver al 30 recupera el ×3 / ×4 del
  juego. Si no se llega, lo primero que baja es el multiplicador.*
- **Controls tables in the README**: every controller (Xbox, PlayStation, Nintendo) and keyboard control side by side, the settings
  menu and the PC mode.
  *Tablas de controles en el README: mando (Xbox, PlayStation, Nintendo) y teclado juntos, el menú de ajustes y el modo PC.*
- **The log says where each stutter comes from** (the game, the app's own captures, mpv, Windows capture, the GPU or Magpie's
  output) and how long frames wait in Magpie's queue, with a summary every 5 minutes (`ritmo_resumen_s`). New keys to try other
  pacing, off by default: `magpie_ritmo_margen_fg`, `magpie_sigue_fuente_30_fg`, `magpie_fijo_30_dlss`, `magpie_vrr_30_otras`,
  `magpie_vrr_30`.
  *El log dice de dónde viene cada tirón (juego, capturas de la app, mpv, captura de Windows, gráfica, salida de Magpie) y cuánto esperan
  las imágenes en la cola de Magpie. Claves nuevas para probar otro ritmo, apagadas.*
- Self-tests without console or GPU: `--test-multiplicador`, `--test-reescalado`, `--test-menu-ajustes`; engine test scenes
  `test=finas` (fences, wires, grass) and `test=cabeza` (a character in front of a moving city); a 4680-plan simulation of GPUs ×
  displays × capture cards in the detection test.
  *Pruebas nuevas sin consola ni gráfica y escenas de prueba del motor para la generación.*

### Fixed / Arreglado
- **The centre button (Xbox / PS / HOME) always opens the settings menu, or says why not.** Right after entering a game or pressing
  Apply, while Magpie prepares the picture (7-11 s) and the engine bridge already shows the game, the button did nothing. Now the
  menu opens on top of the bridge or the curtain ("preparing the picture…" in its header). In the few moments when it really can't
  open yet (the first seconds of a new picture, just after leaving the console menu, a capture change) it shows "Settings · opening in
  a moment" and opens by itself as soon as it can. Every press counts: the button is watched every 10 ms, so short presses are no
  longer lost. And **Apply while Magpie is still preparing applies at once** (Magpie restarts right away with the new settings, with
  the bridge on screen) instead of waiting and then doing nothing.
  *El botón central del mando abre siempre el menú de ajustes o dice por qué no, también mientras Magpie prepara la imagen; si aún no
  se puede, avisa y se abre solo en cuanto se puede. Ninguna pulsación corta se pierde, y "Aplicar" con Magpie preparando se aplica ya.*
- **Leaving the console menu on another game is now told apart from going back to the one you were playing** (GTA VI → menu →
  Bloodborne stayed as GTA VI). While Magpie prepared (~10 s) the app looked at nothing and gave up after 8 s; it now keeps watching
  during that time and also uses the captures that took it out of the menu: the same picture as before the menu means you went back;
  the console's black screen, a video-mode change, the game's known cover or a new cover followed by its loading screen mean the other
  game started, and its profile is applied (even if the proof arrives once Magpie is ready). Without proof it keeps the game you were
  playing, and settings changed meanwhile go back to the right game. A cover is only remembered when a game was really started.
  *Al salir del menú con el foco en otro juego la app distingue si has vuelto al de antes o has arrancado el otro: sigue mirando
  mientras Magpie prepara y cambia al perfil bueno en cuanto ve el arranque (negro, sin señal, su portada).*
- **Console game names without OCR garbage**: the ™ / ® next to the name was read as stuck letters ("Bloodbornevy", "BloodborneJY",
  "Rocket Leagueñ") and a profile was created with them. The name now comes from the agreement of all the reads of the label;
  profiles, icons and covers saved with garbage are renamed (or merged with the good one, keeping the last played) when the app
  starts, and a half-read name is fixed just by passing over that game in the console home.
  *Nombres de juegos sin basura del OCR: el nombre sale del consenso de las lecturas y los perfiles con basura se renombran o se juntan
  solos al abrir la app.*
- **Waking the console no longer loads a game profile**: the PS5 "Welcome back / Who's using this controller?" screen has the clock
  at the top and counted as the real menu, so the dark screens right after took the app out of the menu with DLSS 5 and frame
  generation for ~10 s. The welcome and user screens now count as console screens until the real menu shows up.
  *Al despertar la consola ya no se carga un perfil de juego: la Bienvenida / elegir usuario no cuenta como el menú.*
- **XeSS FG no longer disappears at 1080p**: with a 1080p capture the app silently swapped XeSS FG for DLSS FG, and changing the
  capture to 1080p in the settings menu replaced the type and saved it in the game's profile. The current Magpie runs XeSS FG at 1080p
  (measured), so it is offered and used there (`xess_fg_1080=0` = as before). When a chosen type can't run, the menu keeps your
  choice and says why, and a notice says what runs instead: the type is never swapped silently any more.
  *XeSS FG ya no desaparece a 1080p: va a 1080p (medido) y, si un tipo elegido no puede ir, el menú y un aviso lo dicen y se guarda lo
  elegido.*
- **Micro-stutters caused by the app itself**: while you play, the app looks at the picture to notice the console menu, and each
  look stopped mpv for 52-80 ms at 4K (more than the 33 ms between two real frames in the 30 fps mode): the next real frame came late
  and the frame generation's pacing followed it. Now mpv hands the picture over as a PNG through a pipe written in its own thread (it
  stops ~18 ms, below one frame), pixel-identical, and **nothing is written to disk** any more (before: up to ~26 GB per hour in the
  console menu at 4K). `captura_png=0` = as before.
  *Microtirones que metía la propia app: cada mirada al menú de la consola paraba a mpv 52-80 ms a 4K (más que el hueco entre dos
  imágenes reales). Ahora mpv entrega la imagen por una tubería desde su propio hilo (~18 ms) y ya no se escribe nada en el disco.*
- **DLSS 5 with 1 pass no longer flickers when the camera moves in the 30 fps mode**: it now uses the same optical-flow
  anti-flicker as 2-3 passes (Magpie already computed the flow, only the anti-flicker pass is added, and the automatic DLSS 5 scale
  counts it: about 5 points less at 4K). `dlss5_antiparpadeo_30_1pasada=0` = as before.
  *DLSS 5 con 1 pasada ya no parpadea al mover la cámara en el modo 30: lleva el antiparpadeo con flujo de 2-3 pasadas.*
- **No more blur and double edges when moving with DLSS 4.5 models (L / M) and DLSS 5**: Magpie's DLSS SR had no motion vectors in
  the 30 fps mode with frame generation. L / M now get the optical flow DLSS 5 already computes, at no extra cost: fast pans keep 0.73
  of the original detail instead of 0.48. K and E unchanged. `dlss_flujo_transformer=` (empty) = as before.
  *Sin borrosidad ni bordes dobles al mover la cámara con L / M y DLSS 5: DLSS SR recibe el flujo óptico que DLSS 5 ya calcula.*
- **The first start of a new version no longer drops the console to 1080p**: the app's engine compiles its shaders the first time
  (25-35 s) and the capture safety net took that for "the capture card can't do 4K". Now a just-launched engine is judged from the
  moment it is ready, "Turn on your console" is not shown while it starts, and the backup keeps the compiled shaders.
  *La primera vez de una versión nueva la consola ya no se queda a 1080p (el motor compilaba sus shaders y se tomaba por "la capturadora
  no da 4K").*
- **Applying a setting no longer takes 24 s sometimes**: when Magpie's log rotated in the middle of a start the app didn't see the
  scaling begin and pressed Magpie's scale shortcut, which toggles and stopped it. Now it reads the rotated log too, doesn't press the
  shortcut once the scaling started, and leaves the foreground alone while Magpie prepares.
  *Aplicar un ajuste ya no tarda a veces 24 s (el atajo de Magpie paraba un escalado que ya había empezado).*
- **If the app is closed by force, mpv, Magpie and the engine close with it** instead of staying open with the capture card and the
  NVIDIA models; a hidden engine can't survive either.
  *Si la app se cierra a la fuerza, mpv, Magpie y el motor se cierran con ella.*
- **Magpie used its default profile when the app's folder went through a junction or a symbolic link** (60 fps base, no VRR, no
  automatic scaling): the profile now uses mpv's real path, the one Magpie compares.
  *Magpie usaba su perfil de serie si la carpeta de la app pasaba por una unión o un enlace: el perfil lleva ahora la ruta real de mpv.*
- **Standby**: the screen (the TV) turns off again even with the PC's controller switched on (a switched-on controller counted as
  someone using the PC, so the TV stayed on in black all night); if the PC can't be suspended it is shut down as before 1.2.1
  (it stayed on with nothing running); a shutdown cancelled during standby no longer leaves the taskbar hidden; no data backup every
  2 minutes while in standby.
  *Espera: la pantalla se vuelve a apagar aunque el mando del PC esté encendido; si el PC no se puede suspender se apaga como antes; la
  barra de tareas ya no se queda oculta; sin copia de seguridad cada 2 min.*
- **Updating to a new version no longer loses settings**: the one-time settings update of a new version dropped every line the app had
  added after the previous update (so the Microsoft Defender notice could come back) and reset choices made in the tray or the
  settings app whose line was in the old defaults (what the PC does when the console turns off, the black desktop, DLSS 5 in the
  engine). Now they are kept. (The PC-mode controller resident no longer makes the 2-minute data backup either.)
  *Actualizar a una versión nueva ya no pierde ajustes: se conservan las líneas que la app había añadido (el aviso de Defender ya no
  vuelve a salir) y lo elegido en la bandeja (qué hace el PC al apagar la consola, el escritorio negro, DLSS 5 en el motor).*
- Also fixed: the reserve engine starting at the wrong moment together with Magpie; "your hardware changed" with the TV off; a new
  version "restoring" a session that had closed fine; "Uninstall, delete everything" leaving the settings backup; the engine using
  another GPU after Windows restarts (its LUID changes); "No upscaling" in the engine being sharpened (halos); Ctrl+G now works like
  the other shortcuts; the wizard says the picture goes to the primary display; self-tests that failed because of the PC they ran on.
  *También: el motor de reserva a destiempo, "ha cambiado el hardware" con la tele apagada, una versión nueva que "devolvía" una sesión
  bien cerrada, desinstalar sin borrar la copia de seguridad, la gráfica del motor tras reiniciar Windows, "sin reescalado" con enfoque,
  Ctrl+G, el aviso de la pantalla principal y pruebas con falsos fallos.*

### Changed / Cambiado
- **Faster loading at 4K**: Magpie no longer loads DLSS Super Resolution when its chain doesn't use it (4K capture: DLSS 5 and frame
  generation only), so it is ready ~1.5-2 s sooner every time it prepares: entering a game 8.0-8.9 s → 6.3-7.2 s, Apply 9.2-10.0 s →
  7.0-7.7 s (the game is visible within 0.2-0.7 s thanks to the bridge). The hidden standby engine now starts together with mpv, so
  it is ready for the first game. `magpie_dlss_sr_solo_cadena=0` and `motor_en_espera_al_abrir_ms=-1` = as before.
  *Cargas más rápidas a 4K: Magpie ya no carga DLSS SR si su cadena no lo usa (~1,5-2 s menos cada vez que prepara) y el motor en espera
  arranca con mpv al abrir la app.*
- **Frame generation on fences, wires and other thin repeated detail (DLSS FG through Magpie)**: at 4K DLSS FG got no motion vectors
  and blended the two real frames on thin detail; with DLSS 5 on it now gets the NVIDIA optical flow DLSS 5 already computes, at no
  extra cost. With a 1080p / 1440p capture the 30 fps mode used flow quality 3, which measured worse on thin bars than quality 2 at
  twice the cost: now 2 (5.9 → 3.1 ms per real frame at 1080p, 11 → 6 ms at 1440p). `fg_dlss_flujo_4k=ninguno` and
  `fg_flujo_calidad=3` = as before.
  *Generación por Magpie en vallas, cables y detalle fino: DLSS FG a 4K usa el flujo óptico que DLSS 5 ya calcula, y a 1080p/1440p la
  calidad 2 (mejores vectores, la mitad de coste).*
- **XeSS FG and characters' heads when the camera turns**: every optical-flow setting was measured; what breaks next to a character
  is the background the head covers and uncovers, which no optical flow can fix without depth (Magpie gives XeSS FG a flat depth). The
  finer grid only cleans the head's border and costs ~11 ms more at 1440p, so XeSS FG keeps quality 2; `fg_xess_flujo` /
  `fg_xess_flujo_calidad` let you try others.
  *XeSS FG y la cabeza al girar la cámara: lo que falla es el fondo que la cabeza tapa y destapa, que ningún flujo arregla sin
  profundidad; claves nuevas para probar otras calidades.*
- **Less halo around edges and text with DLSS K / L / M through Magpie** (sharpening after DLSS SR is 0.6× the old value, like the
  engine; `magpie_nitidez_transformer=1` = as before), and **DLSS 5 with 2-3 passes and a 4K capture keeps its color** (0.82 → 0.90
  with 2 passes, 0.79 → 0.88 with 3) with a bit less halo.
  *Menos halo con DLSS K / L / M por Magpie y DLSS 5 con 2-3 pasadas a 4K sin el color lavado.*
- **Headroom on every GPU**: while playing, the engine now keeps the GPU at or under **85 %** of each frame (minus NVIDIA frame
  generation) instead of 93-96 %, and only steps up when the next step still leaves room (`motor_uso_grafica`, 93 = as before). The
  first plan on an unknown PC is prudent: realistic costs for frame generation, XeSS and FSR 4 (they counted as almost free), GPUs
  missing from the table count 15 % slower, what doesn't fit falls back to the next option, 120 fps only where it keeps headroom,
  VRAM under 85 %, and much slower GPUs also use Magpie's light mode in the 30 fps mode.
  *Margen en cualquier gráfica: jugando, el motor deja la gráfica como mucho al 85 % de cada imagen (antes 93-96 %), y el plan del primer
  arranque es prudente en cualquier PC.*
- **Capture cards**: a data table of ~75 real models (AVerMedia, Elgato, Magewell, j5create, Genki, Razer, EVGA, NZXT, ezcap, USB
  dongles…) with their IDs, DirectShow names, formats per resolution, modes they list but don't deliver, and quirks. The plan picks a
  format that really gives 59.94-60 fps, uncompressed and 16:9 first (YUY2 > NV12 > RGB), never P010; cards that only send MJPEG or
  H.264 go through mpv + Magpie (the engine opened them at 5-10 fps), with MJPEG limited to what the CPU can decode; USB cards that
  only list whole frame rates get 60 instead of 59.94 (the safety net tries both), and a USB 3 card that lists far less than it should
  gets a "plug it into a motherboard USB 3 port, no hub" warning.
  *Capturadoras: tabla con ~75 modelos reales; el plan elige un formato que dé 60 fps de verdad y sin comprimir, las que solo dan MJPEG o
  H.264 van por mpv, y las USB que solo listan velocidades enteras reciben 60.*
- **Standby uses less power**: instead of a hidden mpv keeping the capture card open all the time to look at one picture a second
  (10-13 % of a core, 300-400 wake-ups/s), the card is checked for a moment every 2 seconds (under 1 % of a core, ~13 wake-ups/s,
  CPU package ~0.6 W lower). The console is noticed ~0.5 s later on average (`espera_sonda_cada_ms`: 2000; 1000 = almost as quick as
  before; 0 = the mpv watcher as before).
  *La espera gasta menos: la capturadora se mira un momento cada 2 s (de ~400 despertares por segundo a ~13); la consola se ve de media
  ~0,5 s más tarde.*

## [1.2.1] - 2026-11-22

### Fixed / Arreglado
- **The app froze when going from a 120 fps game to the console menu** (Rocket League → PS5 menu: "turn on the console" forever).
  The menu is shown at 4K native, but the capture was asked for 4K **at 120 fps**, which the capture card cannot do. The card refused
  it, mpv stayed open with nothing loaded and the app waited forever. The menu now always uses 60 fps. There is also a safety net
  for any capture change (menu, game profile, settings menu, 30/60/120 mode): if no picture arrives right after the change while the
  console was on, the app switches within seconds to a format the card does give (same size at 59.94 fps, the last one that
  worked, 1080p) and says so on screen. Without that evidence it retries instead of waiting forever, and a mode change to or from
  120 fps is no longer taken for the console turning off. The app's own engine also picks the closest size and frame rate the card
  offers instead of failing, and now follows the frame rate on switches that don't reopen it.
  *La app se quedaba congelada al ir de un juego a 120 fps (Rocket League) al menú de la consola: se pedía 4K a 120, que la
  capturadora no da. El menú va siempre a 60, y si un cambio de captura no da imagen con la consola encendida, la app pasa en
  segundos a un formato que sí da y lo avisa.*
- **Turning the console on no longer loads a game profile**: when the app opened with the console just switching on (e.g. from
  standby), the PS5's dark start-up screens were taken for a game: the console-wide profile started with DLSS 5 and frame
  generation, ~8 s of black, then back to the menu. Until the console menu has been seen, only a bright, steady game picture for
  5 s (60 s if it is dark) leaves the menu; the welcome, user-selection and shutdown screens count as console screens. Once the
  menu has been seen, starting a game is as fast as before.
  *Al encender la consola ya no se carga un perfil de juego con sus pantallas de arranque oscuras.*
- **Turning the console off no longer loads the last game**: after the power menu, the shutting-down screen took the app into the
  last game's profile. For 20 s after the power menu only a bright picture with the console's sound counts, and the "will enter
  rest mode in N minutes" notification no longer counts as the power menu.
  *Al apagar la consola ya no se carga el último juego tras su menú de apagado.*
- **PS5 game names are no longer saved half-read** ("PS5: Rocket" for Rocket League): a label read while it fades or slides never
  beats the full name; a profile with a half-read name is renamed (with its icons and covers) the next time the full name is read; a
  game's icon is only remembered from captures where label and icon agree; control-center cards and game options ("Check for
  update") are no longer taken for game names.
  *Los nombres de los juegos de la PS5 ya no se guardan a medias: gana el nombre entero y el perfil mal nombrado se renombra solo.*
- **Fewer black screens when entering a game or changing the capture**: two hidden standby engines were launched at once and
  closed each other, so the "bridge" (the console picture shown while Magpie prepares) was missing and you got 8-10 s of black.
  Now there is one, it comes up as soon as it is ready, it also bridges when mpv and Magpie reopen (settings menu), and it runs
  lighter at 120 fps; with no bridge, the live console picture is shown instead of black.
  *Menos negro al entrar en un juego o cambiar la captura: el motor en espera ya no se cierra solo, hace de puente en más casos y,
  si no hay puente, se ve la imagen en vivo.*
- **No taskbar (or anything of Windows) when the console turns off in console mode**: the picture was closed with nothing in front
  and the screen only went off about a second later, so the desktop, the mouse pointer and the taskbar showed. Now a black cover
  without pointer goes over every screen the moment the console is seen off, and the taskbar stays hidden through the shutdown
  notice, the whole standby and the wake-up, until the console picture is back. It always comes back: when the picture returns,
  when you close the app, switch to "Like a normal PC", uninstall, or even if the app is closed by force. `espera_ocultar_barra=0`
  leaves the taskbar alone.
  *Al apagar la consola en modo consola ya no se ve la barra de tareas ni nada de Windows: una cubierta negra sin cursor hasta que
  vuelve la imagen de la consola, y la barra vuelve siempre, también si la app se cierra a la fuerza.*
- **Magpie (DLSS 5, XeSS FG...) no longer crops the picture's edges nor adds a thin black band**: mpv's borderless window keeps
  Windows' title and resize-frame styles, so Magpie treated it as a window with a border and cut it off, scaled by the display DPI (3
  px per side at 300 %): a 1440p capture became 2554x1434, upscaled ×1.5035 to 3840x2156 with 2 px black bands; a 4K capture showed
  inside a 3 px black frame. Those two styles are now removed from mpv's window before Magpie starts: it captures the whole
  2560x1440 and upscales exactly ×1.5 to the full screen (measured in Magpie's log, before and after). `mpv_sin_marco=0` restores the
  previous behaviour.
  *Magpie ya no recorta los bordes de la imagen ni deja una franja negra: la ventana de mpv tenía estilos de título y marco y Magpie
  le quitaba 3 px por lado (a 300 %); ahora captura la imagen entera y escala ×1,5 exacto a pantalla completa.*
- **Frame generation in the app's own engine (DLSS FG) is much cleaner when the camera moves**: the generated frames get one
  camera-motion vector, but it came from a histogram limited to ±32 px and from block vectors that stay at 0 on medium pans at 4K:
  every turn faster than ~32 px per real frame was sent as 32 px and 5-64 px pans as 0, so DLSS FG guessed the in-between frame on its
  own (doubled or smeared edges, the HUD dragged). The camera motion now has its own search over the whole picture (up to ±384 px,
  sub-pixel), and still, textured parts (HUD, the character the camera follows) get no motion and are marked in front. Measured on
  synthetic test scenes against the ideal in-between frame (4K, 30 fps mode): 100 px pan 2.80 → 0.03 average error, 200 px 7.9 → 0.07,
  24 px 0.70 → 0.47, around a still HUD 11.7 → 0.6, parallax −46 % / −55 %, 1440p and 1080p with DLSS SR −92 / −97 %. It also costs less:
  at 4K without DLSS 5 the block search is no longer needed (~2 ms less GPU per real frame, which is also latency).
  `fg_global=0 fg_quieto=0 fg_quieto_prof=0 fg_solo_camara=0` in `motor_extra` restores the previous behaviour.
  *La generación de fotogramas del motor propio (DLSS FG), mucho más limpia al mover la cámara: el vector del giro salía como mucho
  de 32 px (y de 0 en los paneos medios a 4K); ahora tiene su propia búsqueda y lo quieto (interfaz, el personaje que sigue la cámara)
  va sin movimiento y delante. Y ~2 ms menos de gráfica a 4K.*
- **XeSS, FSR 3.1/4 and FSR 3 FG in the app's own engine had no motion vectors** (only DLSS asked for them), so moving things trailed
  and blurred. They now get them (FSR 3 FG the same ones as DLSS FG). Error against the true 4K picture while panning, 1440p capture:
  XeSS 7.9 → 2.7, FSR 5.2 → 2.8 (DLSS L 2.5); 1080p FSR 6.4 → 3.7. Costs ~1.3-1.9 ms of GPU on an RTX 4060 Ti. `mvsr_xess_ffx=0`
  restores the previous behaviour.
  *XeSS, FSR 3.1/4 y FSR 3 FG del motor propio iban sin vectores de movimiento (rastro y emborronado al moverse): ahora los reciben.*
- **Built-in engine without NVIDIA Reflex (AMD, Intel or `motor_streamline=0`)**: after turning DLSS 5 on and off, 2-3 frames
  stayed queued for the screen for the rest of the session (+1-2 refreshes of delay). Now it stays at 1. NVIDIA with Reflex is not
  affected.
  *Motor propio sin Reflex (AMD, Intel): la cola de la pantalla ya no crece a 2-3 imágenes tras pasar por DLSS 5.*
- Also fixed: keyboard and mouse hooks reinstalled every 2 s while playing with a controller (and the log spam); audio meter error
  at startup (`MMDeviceEnumeratorCom`); "unknown ini key: defender_aviso"; first-run `SetUnhandledExceptionMode` error; the settings
  menu losing the frame generation type on refresh; the frame generation substitute ignoring the mode's list; Magpie's scaling
  hotkey sent while Magpie was still loading or not running; game entry and the settings menu's Apply at the same time clearing
  each other's "applying" state; low-fps notices only in Spanish; the low-fps helper not noticing the shortfall at 120 fps; the
  diagnostic capture delaying the exit from the console menu; a new version could inherit the standby power-plan marker.
  *También: ganchos cada 2 s con el mando, error del medidor de audio, clave defender_aviso, error del primer arranque, tipo de
  generación del menú, atajo de Magpie a destiempo, avisos de fps bajos en el idioma elegido y a 120 fps, y otros detalles.*

### Changed / Cambiado
- **The DLSS 5 scale now follows your whole picture chain**: with DLSS 5 on, its input resolution is no longer a fixed value per
  mode. The app works it out from the capture, the output (your TV), the 30 / 60 / 120 fps mode, the frame generation type, the
  DLSS upscaler model and the DLSS 5 passes, scaled for your GPU. A lighter upscaler leaves room for more DLSS 5: on an RTX
  4060 Ti, 1440p 30 fps with frame generation to 4K now gets ~75 % with DLSS E and ~50 % with M (before, 40 % with any). In 120 fps
  mode, where DLSS 5 couldn't keep up (~100 of 120 fps), it steps aside and the 120 fps are kept. `dlss5_escala_auto=0` restores
  the old tables.
  *La escala de DLSS 5 sigue a toda la cadena (captura, tele, modo, generación, modelo de DLSS, pasadas y tu gráfica); a 120 fps,
  si no cabe, se quita y se mantienen los 120.*
- **Console-mode standby is closer to "almost off"**: it runs in its own small process (the closed app's controller loops kept
  running every 50 ms), the screen goes off first, the console watcher no longer raises Windows' timer resolution and uses about
  30 % fewer CPU cycles and half the wake-ups, a temporary power plan (Power saver with the processor capped at 50 % and no boost)
  is used and deleted on waking (`espera_cpu_max`, 0 = Power saver as before), the screen is turned off again if another program
  turns it on with nobody touching anything, and the Windows key no longer opens Start during standby. Turning the console on is
  noticed sooner. Measured on an RTX 4060 Ti: graphics card 11.5 W → 8.5-9 W; the standby process uses 0.1 % of one core.
  *La espera del modo consola, más parecida a un PC casi apagado: proceso propio pequeño, pantalla apagada lo primero, menos
  consumo y un plan de energía propio que se borra al despertar.*

### Added / Añadido
- **The app checks that the fps you chose are really reached** (real frames, and output frames with frame generation) and, if
  not, acts at once: first the DLSS 5 scale, then one pass less, a lighter DLSS model, a cheaper frame generation type, and only
  then the old steps (DLSS 5 off, FSR 1, 120 → 60…). It is done in one reopen without a black screen, and it tells you on screen
  what it changed ("60 fps not reached (52): DLSS 5 scale 50% -> 40%"). When there is GPU to spare for a few minutes, the scale goes
  back up the next time you start the game (Magpie can't change it while running), never to a value that already failed. What
  works is remembered per game and GPU in `data\scales_auto.txt`.
  *La app comprueba que se llegan los fps elegidos y, si no, baja primero la escala de DLSS 5 (luego pasadas, modelo de DLSS,
  tipo de generación…) y te dice qué ha cambiado; si sobra gráfica la sube al volver a entrar en el juego.*
- **Latency diagnostics in the log** (mpv's capture queue every 20 s, the console sound queue every 5 min, the capture thread
  priority, and a note when NVIDIA Reflex makes captured frames wait) and settings to try lower latency, off or unchanged by
  default: `mpv_cola_ms`, `audio_bloque_ms`, `audio_cola_alta_ms`, `audio_cola_baja_ms`, and `mmcss=1` in `motor_extra`.
  *Diagnóstico de latencia en el log y ajustes nuevos para probar menos retraso (apagados o como antes).*

## [1.2.09] - 2026-11-21

### Fixed / Arreglado
- **DLSS FG with DLSS 5 still not starting ("Frame generation did not start") with your own dlssg_sm86**: Magpie 0.6.12 checks
  every file of `runtimes\dlssg_sm86` against its `build-manifest.json` (SHA-256) and only accepts dlssg_for_sm86 **v0.4.0**. A
  different version placed next to the DLSS 5 DLL was copied as it was, so Magpie refused it and never scaled. The app now checks
  the version: another one is not copied (and an existing wrong copy in `extras\` or `magpie\` is removed), and the official v0.4.0
  is used instead (from your PC or downloaded from its release, as in 1.2.07). Nothing to do: open the new version and it fixes itself.
  *DLSS FG con DLSS 5 no arrancaba con un dlssg_sm86 de otra versión: Magpie solo valida la 0.4.0. Ahora la app comprueba la versión,
  quita la que no vale y pone la oficial sola.*

## [1.2.08] - 2026-11-21

### Fixed / Arreglado
- **Microsoft Defender closing the app (no sound, controller not responding)**: with **Start with Windows** on, the app created a
  scheduled task at logon. Defender's behaviour detection (`Behavior:Win32/Persistence.A!ml`) took a freshly unzipped, unsigned exe
  doing that for malware: it killed ConsoleEnhancer a few seconds later (the picture stayed, but sound and the controller died) and
  moved `ConsoleEnhancer.exe` to quarantine. Start with Windows is now a normal shortcut in your **Startup** folder (it shows in
  Task Manager > Startup apps); the old task is removed automatically.
  *Defender cerraba la app (sin sonido ni mando) y se llevaba el exe a cuarentena por la tarea programada de inicio: ahora es un
  acceso directo en la carpeta Inicio y la tarea vieja se quita sola.*

### Added / Añadido
- If Defender has closed or quarantined the app in the last 30 days, the app explains it once when it opens and can open
  **Windows Security** with the app folder copied, so you can add an exclusion yourself (the app never changes Defender settings).
  Also in the tray: **Microsoft Defender: exclusion for the app...**. The README explains how to get the exe back from quarantine.
  *Si Defender ha cerrado la app, un aviso al abrirla (y en la bandeja) para abrir Seguridad de Windows y añadir tú la exclusión.*

## [1.2.07] - 2026-11-21

### Fixed / Arreglado
- **DLSS FG no longer asks for a file**: with DLSS 5 the picture goes through Magpie, whose DLSS Frame Generation needs the
  `dlssg_for_sm86` runtime on any RTX. A new version in a new folder started without it, the settings menu said
  "DLSS FG · dlssg_sm86 missing" and Magpie could show "Frame generation did not start". Now the app gets it by itself: from your PC
  if it is already there (Magpie's own copy in `%LOCALAPPDATA%\Magpie\DlssgSm86` or an older ConsoleEnhancer folder) or from its
  official release (https://github.com/sdli1995/dlssg_for_sm86 v0.4.0, MIT, SHA-256 checked), in the background, and also writes the
  two small `.ini` files Magpie checks. Only on NVIDIA with DLSS 5 added.
  *DLSS FG ya no pide ningún archivo: la app coge o baja sola dlssg_for_sm86 (de su release oficial) y pone los .ini que comprueba Magpie.*
- **Game names missing in the settings menu after updating**: deleting the old version's folder meant the new one started without
  knowing your console was a PS5 (one generic "Console" profile, no game names) and without your game profiles. The app now keeps a
  backup of its settings in `%LOCALAPPDATA%\ConsoleEnhancer\backup` (every 2 minutes, only what changed) and a new version takes
  them from there when there is no older folder next to it.
  *Sin nombre de juego en el menú tras actualizar: la app guarda una copia de sus datos fuera de su carpeta y la versión nueva la hereda.*

## [1.2.06] - 2026-11-21

### Changed / Cambiado
- **English by default, with a language picker when the app opens**: the first window (the one that downloads mpv and Magpie) has
  **English / Español** buttons at the top right; the choice is remembered (`data\language.txt`) and also applies to the wizard. It
  can be changed later from the tray menu (**Language / Idioma**, the app reopens) or the first step of the wizard. Before, the app
  followed the Windows language.
  *En inglés de serie, con selector de idioma al abrir la app (primera ventana, asistente y bandeja); se recuerda lo elegido.*
- **File and folder names in English**: `engine\` (was `motor\`), `languages\`, `licenses\`, `resources\`, `downloads\`, `data\`
  (portable data, was `datos\`), `data\games\` (was `juegos\`), `README.txt` (was `LEEME.txt`) and `ConsoleEnhancer Settings.exe`
  (was `Ajustes de ConsoleEnhancer.exe`); the files the app saves also have English names (`capture.txt`, `profiles.txt`,
  `console.txt`...). Data from earlier versions is renamed automatically the first time the app opens, and inherited from an older
  portable folder with the new names, so your settings and game profiles are kept.
  *Nombres de archivos y carpetas en inglés; los datos de versiones anteriores se renombran solos y no se pierde nada.*
- **No more "Do you also have the dlssg_sm86 folder?" question** when adding DLSS 5: if that folder is next to the
  `nvngx_dlssnr.dll` you pick (or one or two folders up), it is copied without asking; if not, the app carries on without it (the
  settings menu still says "DLSS FG · dlssg_sm86 missing" and where to put it).
  *Ya no se pregunta por la carpeta dlssg_sm86 al añadir DLSS 5: si está junto a la DLL se copia sola.*

## [1.2.05] - 2026-11-21

### Fixed / Arreglado
- **Starting another game while the app was switching engines left the old game's settings** (Rocket League -> GTA VI right after
  changing the capture to 1440p/120: the settings menu still said Rocket League). During an engine swap (~6 s) the app took no
  captures, so it never saw the PS5 menu. Now it keeps watching for the console menu during the swap; if a different game is focused
  there, the app switches to it as soon as it sees it start (black screen, no signal or its cover).
  *Entrar en otro juego mientras la app cambiaba de motor dejaba los ajustes del anterior (GTA VI tras Rocket League): ahora lo ve.*
- **The game focused when you open the PS5 menu is the one running**: if it isn't the one the app thought, the app corrects itself
  right there (a safety net for any game start it missed).
  *Al abrir el menú de la PS5, el juego en foco es el que está en marcha: si la app se había equivocado, se corrige.*

### Changed / Cambiado
- The "Add DLSS 5" warning no longer says `dlssg_sm86` is for RTX 30 (since 1.2.02 Magpie's DLSS FG with DLSS 5 needs it on RTX 40/50 too).
  *El aviso de "Añadir DLSS 5" ya no dice que dlssg_sm86 es para RTX 30.*

## [1.2.04] - 2026-11-21

### Fixed / Arreglado
- **No more black screen for ~2 s every time you go from the console menu to a game** with a 4K capture when Magpie lacks
  `dlssg_sm86`: DLSS FG was swapped for XeSS FG (only Magpie does it) even though DLSS 5 doesn't run at 4K, so every return from the
  menu closed the app's own engine and reopened mpv + Magpie. Now DLSS FG stays in the app's own engine and the picture isn't reopened.
  *Sin pantallazo negro de ~2 s al pasar del menú de la consola a un juego con captura 4K (XeSS FG por Magpie sin hacer falta).*
- **PS5 game names read with an extra letter** ("Rocket Leaguen": the ® read as an "n"): if the last word isn't in the Windows
  dictionary (English or Spanish) and without that letter it is, the letter is removed. A name read cut at the start ("Theft Auto VI")
  is now the same game as the full one ("Grand Theft Auto VI"), so it no longer switches to a wrong game for a moment.
  *Nombres de juegos de la PS5 con una letra de más (el ® leído como "n") o cortados por delante: ya son el juego correcto.*
## [1.2.03] - 2026-11-21

### Fixed / Arreglado
- **No more black screen with no way out when something fails badly**: the error message used to open *under* the picture and the
  loading screen (both always on top), with mpv and Magpie still running. Now the picture, the sound and Magpie are closed first and the
  message shows on top of everything. *Un error grave ya no deja la pantalla negra sin salida: el aviso sale encima de todo.*
- **A capture card that can't be opened no longer leaves the loading screen up forever**: the app retried every 2 s endlessly. Now it
  waits longer after 5 failures, writes in the log which open programs may be holding the card (OBS, RECentral, Streamlabs…), and after
  20 tries tells you and closes. A picture process stuck in a driver no longer freezes the app either.
  *La capturadora que no se abre ya no deja la carga para siempre: tras 20 intentos se avisa (y quién puede estar usándola).*
- **Laptops with two GPUs (and monitors plugged into the motherboard)**: the plan was made for the integrated GPU (no DLSS, no Reflex,
  no frame generation of NVIDIA). Now it uses the dedicated GPU. *Portátiles con dos gráficas: el plan es el de la dedicada.*
- **Intel Arc with a 1080p capture**: frame generation became "DLSS FG" and did nothing. Now it uses FSR 3 FG.
  *Intel Arc con captura de 1080p: FSR 3 FG en vez de un DLSS FG que no hacía nada.*
- **RTX 50 without DLSS Frame Generation available** (for example with hardware-accelerated GPU scheduling off) now gets Smooth Motion,
  like the RTX 40. *RTX 50 sin DLSS FG: Smooth Motion.*
- **"No upscaling" (settings menu) through Magpie** showed the picture small in a corner; now it is stretched to the screen (bicubic, no
  sharpening). *"Sin reescalado" por Magpie ya ocupa toda la pantalla.*
- **R3 works again after choosing FSR 1 / No upscaling** for a game (or after the low-fps helper chose FSR 1): it goes back to the
  normal upscaler, then toggles as always. Applying the settings menu no longer resets the R3 upscaler when you didn't touch that row.
  *R3 vuelve a funcionar tras elegir FSR 1 / Sin reescalado; aplicar el menú no cambia el escalador si no lo tocas.*
- **No internet on first run**: the Magpie zip downloaded by hand from the link in the window is now found (it has a different name than
  the app expected), and the `descargas` folder is created for you. *Sin internet: el zip de Magpie bajado a mano ya se encuentra.*
- **Setup wizard**: a capture device whose formats can't be read no longer makes the wizard try to open it again and again. A capture card
  that isn't in the catalog and couldn't be opened gets 1080p instead of 1440p. *Asistente: sin bucle al leer los formatos; capturadora
  desconocida a 1080p.*
- **`Ajustes de ConsoleEnhancer.exe`** now uses the language chosen in the wizard, and also says when DLSS FG needs the `dlssg_sm86`
  folder. *El panel de ajustes usa el idioma elegido y avisa de `dlssg_sm86`.*
- A `dlssg_sm86` copy left half-done is copied again; the settings menu checks `extras\` at most every 10 s instead of twice a second.
  *Una copia de `dlssg_sm86` a medias se repite.*

## [1.2.02] - 2026-11-21

### Fixed / Arreglado
- **DLSS FG no longer disappears from the frame generation list without a reason.** With DLSS 5 on, the picture goes through Magpie,
  and Magpie's DLSS Frame Generation needs the community `dlssg_sm86` folder on every RTX (not only RTX 30). A fresh install
  downloads Magpie without it, so since 1.2.0 the app quietly took DLSS FG out of the list and used XeSS FG or Smooth Motion. Now the
  settings menu (Guide button) shows **"DLSS FG · dlssg_sm86 missing"** with the folder to put it in (`extras\runtimes\dlssg_sm86`);
  a folder dropped there is picked up the next time the menu opens. The "Add DLSS 5" dialog no longer says the folder is only for
  RTX 30. *DLSS FG ya no desaparece de la lista sin explicación: con DLSS 5 (por Magpie) necesita la carpeta `dlssg_sm86` en cualquier
  RTX; el menú de ajustes dice "DLSS FG · Falta dlssg_sm86" y dónde ponerla (`extras\runtimes\dlssg_sm86`).*

## [1.2.01] - 2026-11-21

### Added / Añadido
- **Settings menu (Guide button): "FSR 1" and "No upscaling" in the Upscaler row**, saved per game. "No upscaling" is a plain stretch
  to the screen (Catmull-Rom in the app's engine), with no AI upscaler: the lightest option.
  *Menú de ajustes: "FSR 1" y "Sin reescalado" en la fila del escalador, guardados por juego.*

### Fixed / Arreglado
- **Switching the capture to 4K while in 120 fps mode no longer leaves the app stuck loading**: the capture card can't capture 4K at
  120 fps, so the app opened it at 4K/120 and waited for a picture forever. Now 4K switches to 60 fps mode, and the settings menu
  doesn't offer 120 fps at 4K. *Pasar la captura a 4K en el modo 120 ya no deja la app cargando: a 4K va el modo 60.*
- **The low-fps helper now also works when Magpie makes the picture** (DLSS 5 through Magpie): if Magpie gets clearly fewer real
  frames than the mode needs for ~10 s (for example 70 of 120 with DLSS 5 in Rocket League), it steps down: DLSS 5 passes, DLSS 5 off,
  FSR 1 upscaler, 120 → 60 fps, lower capture, 30 fps with frame generation. It shows on screen what it changed and saves it in the
  game's profile. The "DLSS 5 off" step now really reopens the picture (before, it only changed the setting).
  *El aviso de fps bajos también con Magpie (DLSS 5): baja un paso (con FSR 1 como paso nuevo antes de bajar el modo) y lo guarda.*

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
- **The console menu no longer gets DLSS 5 by mistake right after coming back to it**: a second or so without the clock on screen
  (for example a screen change in the PS5 settings) was enough to "leave" the menu, so Magpie started DLSS 5 and the picture went to
  30 fps for ~10 s over the menu. After a recent return to the menu, leaving it now also needs 2.5 s without the menu, and the capture
  that made it leave is saved to `datos\juegos\debug` (the first 3 per session). *El menú de la consola ya no se toma por un juego
  justo tras volver a él (salía DLSS 5 y se veía a 30 fps unos 10 s).*

### Changed / Cambiado
- **README: the power section now explains the low-power mode** (the PC stays on and almost idle, which is why turning the console on
  brings it back) and no longer suggests that the PC shuts down and is turned on with the controller. *README: explicado el modo de
  bajo consumo; ya no dice que el PC se apaga y se enciende con el mando.*

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
