# ConsoleEnhancer

[![Licencia: GPL-3.0](https://img.shields.io/badge/licencia-GPL--3.0-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/IanRog29/ConsoleEnhancer?label=descargar&sort=semver)](https://github.com/IanRog29/ConsoleEnhancer/releases)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%2F%2011%20(64%20bits)-0078D6.svg)](#requisitos)
[![Ko-fi](https://img.shields.io/badge/Ko--fi-apoya%20el%20proyecto-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/ianrog29)

**Tu consola en la tele, mejor que nunca, sin tocar el teclado.** ConsoleEnhancer convierte el PC del salón en la "caja" que va entre
tu consola y tu tele: conectas la consola a una capturadora, el PC a la tele, y listo. Enciendes la consola y la app se abre sola; la
imagen llega **reescalada a 4K** (DLSS, FSR, XeSS, RTX Video), con **generación de fotogramas** si tu gráfica puede (DLSS Frame
Generation, Smooth Motion, FSR 3 / XeSS FG), **HDR** y **VRR**, y con el **menor retraso posible**. Todo se maneja **con el mando** desde el
sofá, sin ratón ni teclado; y al apagar la consola, la app se cierra sola y, si quieres, **apaga también el PC**.

- **Se configura sola**: un asistente detecta la gráfica, la tele, la capturadora, el sonido y el mando, prueba la imagen y el sonido y
  elige la mejor cadena para tu PC. Las escalas **se adaptan a tu gráfica** mientras juegas (y lo recuerda).
- **Cualquier consola**: Nintendo Switch / Switch 2, PlayStation, Xbox... Reconoce el menú de la consola y lo enseña a 4K nativo.
- **Pensada para el salón**: se abre al iniciar Windows con la pantalla en negro hasta que hay imagen, sin ventanas ni avisos por medio;
  cambias de modo (30 / 60 fps, generación sí / no, resolución de captura) con los botones del mando.
- **Gratis, libre (GPL-3.0) y sin anuncios ni telemetría.**

Creado y mantenido por **IanRog29** · [Código y descargas](https://github.com/IanRog29/ConsoleEnhancer) ·
**[English below](#english)**

---

## Inicio rápido

1. Baja el último **`ConsoleEnhancer-vX.Y.Z.zip`** de **[Releases](https://github.com/IanRog29/ConsoleEnhancer/releases)** (no el botón
   verde "Code": eso es el código fuente).
2. **Descomprímelo** en una carpeta tuya: Documentos, el Escritorio, `D:\Juegos`... (no en *Archivos de programa*).
3. Abre **`ConsoleEnhancer.exe`** y sigue el asistente. Ya está: enciende la consola.

Detalles de la primera vez:

- Si Windows dice *"Windows protegió su PC"*: **Más información > Ejecutar de todas formas** (el exe no está firmado con un certificado
  de pago; el código está aquí para quien quiera revisarlo).
- La primera vez baja **mpv** y **Magpie** (dos programas libres que hacen el reescalado y la generación de fotogramas por Magpie) de sus
  páginas oficiales: unos 670 MB, una sola vez, comprobando su huella SHA-256. Pulsa **Descargar**. Si algo se estropea más tarde:
  icono de la bandeja > **Componentes: comprobar / volver a descargar**.
- El **asistente** pregunta el idioma y enseña lo detectado, la capturadora con **Probar imagen**, el sonido con **Probar sonido**, la
  consola, la pantalla y el plan. Al final eliges si se abre al iniciar Windows y si apaga el PC al apagar la consola.

Para volver a configurarla: icono de la bandeja > **Asistente de configuración**. Para quitarla: icono de la bandeja > desmarca
"Abrir al iniciar Windows", cierra la app y borra su carpeta (y `%LOCALAPPDATA%\ConsoleEnhancer` si no era portable).

## Apoya el proyecto

ConsoleEnhancer es **gratis y completo**: no hay versión de pago, funciones bloqueadas ni anuncios. Si te resulta útil y quieres
invitarme a un café para seguir mejorándolo, puedes hacerlo en Ko-fi. Es **totalmente voluntario**.

[![Apóyame en Ko-fi](https://img.shields.io/badge/Ko--fi-invítame%20a%20un%20café-FF5E5B?style=for-the-badge&logo=ko-fi&logoColor=white)](https://ko-fi.com/ianrog29)

También desde la app: icono de la bandeja > **Apoyar el proyecto (Ko-fi)**. Y ayuda mucho dejar una estrella en GitHub, avisar de fallos
y contar en qué PC y con qué consola lo usas.

## Requisitos

| | Mínimo | Para lo mejor |
|---|---|---|
| Sistema | Windows 10 2004 o posterior, 64 bits, .NET Framework 4.8 (viene con Windows 10/11) | Windows 11 |
| Gráfica | Cualquiera con Direct3D 12 (NVIDIA, AMD o Intel) | NVIDIA GeForce RTX (DLSS; RTX 40/50: Smooth Motion y DLSS Frame Generation) |
| Capturadora | Cualquiera DirectShow (USB/UVC o PCIe) con la consola conectada | Una que capture a 1440p o 4K y con poco retraso |
| Pantalla | Cualquiera | Tele o monitor 4K de 120 Hz (la generación de fotogramas necesita 100 Hz o más) |
| Disco | Unos 1,2 GB | |

## Qué hace

- **Asistente del primer arranque** (y otra vez desde la bandeja), 9 pasos: idioma, lo detectado, la capturadora con *Probar imagen*,
  el sonido con *Probar sonido*, la consola, la pantalla, la **imagen** (HDR y VRR), el **mando** (tabla de botones del tuyo) y el plan.
- **Cadena según la gráfica** (`src\Deteccion\Plan.cs`): RTX 50, DLSS SR + Reflex + DLSS-G (x2-x4); RTX 40, DLSS SR + Reflex + Smooth
  Motion; RTX 20/30, DLSS SR + Reflex (FSR 3 FG por Magpie opcional); GTX / AMD / Intel, el motor propio con FSR 1 (EASU + RCAS) y la
  generación que tenga cada una (AFMF del driver, FSR 3 FG, XeSS FG).
- **Motor de imagen propio** (D3D12, `src\Motor`): captura DirectShow, DLSS / FSR 1, generación de fotogramas y Reflex. Cuando hace
  falta, mpv + Magpie.
- **Escalas que se adaptan a tu PC** (`escalas_adaptables=1`, de serie): el asistente estima el modelo de DLSS y los porcentajes para tu
  gráfica y tu pantalla; jugando, el motor mide el tiempo de gráfica cada segundo y baja la calidad si no llega (en 2 s) o la sube poco
  a poco si sobra (tras 20 s), y lo recuerda para la próxima vez (`datos\escalas_motor.txt`). Con mpv + Magpie se ajusta entre una
  partida y otra. Bandeja > Imagen > *Escalas que se adaptan solas* / *Olvidar las escalas aprendidas*. Con el extra "DLSS 5" en el
  motor hay además un **freno de emergencia**: si la gráfica va muy por encima de lo que cabe, quita una pasada o baja el % de golpe
  (para no atascar la gráfica) y eso queda como lo aprendido, sin pelearse con el ajuste fino.
- **No se cierra sola si la gráfica se para**: si la imagen deja de avanzar con la consola encendida (la gráfica atascada o reiniciada,
  el motor cambiando de ajustes), la app lo distingue de la consola apagada (el motor avisa, la capturadora le sigue mandando imágenes o
  la consola sigue sonando), espera y, si dura más de 8 s, vuelve a abrir solo la imagen (*Recuperando la imagen*). Con la consola
  apagada de verdad (pantalla *No Signal* o sin imagen ni sonido) se cierra como siempre, y no apaga el PC si la gráfica falló en el
  último minuto (`apagado_confirmar`, `cuelgue_relanzar_ms`, `apagar_pc_confirmar`). Un fallo en un hilo secundario va al log y la app
  sigue (`ConsoleEnhancer.exe.config`).
- **HDR**: RTX HDR (NVIDIA) o Auto HDR de Windows para convertir la imagen SDR de la consola, en el paso *Imagen* del asistente
  (apagados de serie; necesitan el HDR de Windows encendido en la pantalla).
- **VRR** (`vrr=auto`): se enciende solo si la pantalla tiene VRR (G-SYNC / G-SYNC Compatible / FreeSync / HDMI VRR) y Windows y el
  driver lo permiten; en AMD / Intel, actívalo en el asistente (`vrr=1`). También en bandeja > Imagen > VRR. Encendido, el motor propio
  presenta cada imagen en cuanto la acaba (sin esperar al refresco) y, con la generación, sincroniza solo donde haría falta
  (`motor_vrr_fg_vsync=-1`); solo si la pantalla dice tener VRR, para no dar cortes en una que no lo tenga. En el modo 60 con DLSS FG y
  una pantalla de 120 Hz con G-SYNC, el motor va con **refresco fijo** (G-SYNC apagado solo en el perfil de NVIDIA de su exe mientras
  juegas así; se quita al salir, y si la app se cae, al volver a abrirla): con G-SYNC, Reflex limita la salida a ~116 imágenes/s y se
  perdían ~2 imágenes de la consola por segundo; así salen todas y con menos retraso (`motor_60fg_refresco_fijo=auto`; con 144 Hz o
  más, a 119,88 Hz, sin VRR o sin NVIDIA no se toca nada).
- **Selector de generación de fotogramas**: L3 jugando (o Ctrl+G con el teclado sobre la imagen) recorre los tipos que tiene tu
  gráfica y lo recuerda por juego. Solo se ofrece lo que llega a ritmo.
- **Modo 120 fps (esports)**: para los juegos de 120 Hz de la consola, captura a 120 sin generación de fotogramas y con el menor
  retraso. El botón de modo pasa 30 → 60 → 120 solo si la consola saca 120, la tele va a **120 Hz o más** y la captura de ahora es una
  que tu capturadora da a 120 (normalmente **1080p o 1440p**; el asistente lo lee de la capturadora, capturas_120). A 4K sigue 30 ↔ 60:
  la app no te baja la resolución sola. Si no cabe, un aviso dice por qué. permitir_120=auto (1 / 0 para forzarlo / quitarlo).
- **Menú de la consola a 4K nativo**: el HOME de la Switch 2, el menú de la PS5 y el de Xbox se reconocen y van sin escalado ni
  generación, y sin "DLSS 5" (tal cual, con ~10 ms menos de retraso; `motor_dlss5_home=0`); los cambios menú ↔ juego tardan menos de 1 s.
- **Reconoce cada juego y le guarda su perfil** (`juegos\perfiles.txt`): en la Switch / Switch 2 lee el nombre del juego en el HOME
  y guarda su icono, así que lo que cambies con el mando (captura, modelo y pasadas de DLSS 5, escalador, modo 30/60 y tipo de
  generación) se recuerda por juego y se vuelve a poner solo la próxima vez que entras. En la **PS5** lee el nombre del juego en el
  centro de control o el conmutador de juegos (botón PS) y le guarda su perfil igual; en la **Xbox Series**, en la Guía y las fichas de
  Inicio (*preliminar: sin probar aún con una Xbox de verdad*). Si no reconoce el juego, usa el perfil único de la consola, como antes
  (`ps5_reconocer_juegos=1`, `xbox_reconocer_juegos=1`; 0 = un solo perfil para toda la consola).
  **Importante en la PS5/Xbox:** el nombre del juego solo aparece en el centro de control / conmutador (botón PS) o en la Guía, no durante
  el juego. Para que la app recuerde cada juego con su perfil, **pulsa el botón PS una vez al entrar en el juego** (se abre el centro de
  control, la app lee el nombre y ya queda guardado); a partir de ahí se re-aplica solo. El menú de la consola se muestra a 4K nativo
  siempre que haga falta para esto, independientemente de `home_4k_nativo` (esa opción solo afecta al HOME de la Switch 2).
- **Mandos**: Xbox / XInput, DualSense / DualShock 4, mandos de Switch y Switch 2, 8BitDo, GameSir y genéricos HID, sin programas
  aparte. La app lee el mando sin quitárselo a la consola y, fuera de la imagen, maneja el PC (modo PC).
- **Iniciar con Windows** (tarea del usuario, sin permisos de administrador) con la pantalla en negro hasta que hay imagen y,
  opcional, el escritorio en negro mientras la app está abierta.
- **Apagar el PC al apagar la consola**, con una cuenta atrás de 15 s que se cancela con cualquier botón, tecla o el ratón.
- **Portable**: con `portable.txt` junto al exe (viene en el zip), todo se guarda en `datos\`; si lo borras, en
  `%LOCALAPPDATA%\ConsoleEnhancer`.

## DLSS 5 (opcional)

"DLSS 5" es como la comunidad llama a un filtro neuronal de NVIDIA (DLSSNR, el de Magpie) que redibuja la imagen de la consola con más
detalle, antes del escalado. **No es una función oficial de NVIDIA y no viene con ConsoleEnhancer**: necesita `nvngx_dlssnr.dll`, una
DLL de NVIDIA modificada por la comunidad, sin firma ni licencia, que no se puede repartir. Por eso la app **no la incluye, no la
descarga ni dice de dónde sacarla**. Todo lo demás (DLSS, generación de fotogramas, HDR, VRR...) funciona igual sin ella.

Si ya tienes tu propio archivo, añadirlo es un clic:

1. Icono de la bandeja > **Añadir DLSS 5 (tu propio archivo)…** (o el botón **Añadir DLSS 5** del último paso del asistente; solo con
   una GeForce RTX y 6 GB de VRAM o más).
2. Lee el aviso (no es oficial, NVIDIA no lo soporta y es bajo tu responsabilidad) y elige tu `nvngx_dlssnr.dll`.
3. La app comprueba que es una DLL de Windows de 64 bits con ese nombre y la copia a `extras\` junto al exe. Opcionalmente, en RTX 30,
   también tu carpeta `dlssg_sm86` (DLSS Frame Generation por Magpie).
4. Dices si lo enciendes ya. Jugando se enciende y se apaga con el botón **A** del mando (X / Y / B: pasadas; L1 / L2 / R2 / R1: nivel).

Para quitarlo: icono de la bandeja > **Quitar DLSS 5** (borra la copia de `extras\`; tu archivo original no se toca).

## Mandos (chuleta)

Con la imagen de la consola delante. La app lee el mando sin quitárselo a la consola: la pulsación también le llega a ella (Options
abre el menú del juego en la PS5, etc.).

| Mando | Modo **30 → 60 → 120** fps | Generación de fotogramas **sí / no** |
|---|---|---|
| Xbox / XInput (GameSir, 8BitDo en modo X) | Menú (≡) | Vista (⧉) |
| PlayStation 5 (DualSense / Edge) | Options | Crear |
| PlayStation 4 (DualShock 4) | Options | Share |
| Nintendo Switch / Switch 2 (Pro, Joy-Con, 8BitDo en modo Switch) | + | − |
| Genérico HID | Start | Select |

La generación también va con Captura (Switch, 8BitDo), el panel táctil (PlayStation) o el botón de captura del GameSir. Para la
distribución de antes (Vista = modo 30, Menú = modo 60): `mando_botones=prisma` o bandeja > Imagen. El 120 solo aparece donde cabe (ver
*Modo 120 fps*); si no, el botón alterna 30 ↔ 60.

| Botón | Qué hace |
|---|---|
| **Cruceta** ← ↑ → ↓ | Captura a 720p / 1080p / 1440p / 4K |
| **L3** | Tipo de generación (Smooth Motion, DLSS FG, XeSS FG, FSR 3 FG: lo que tenga tu gráfica). Mantenido 1 s: filtro de tramado nuevo / el de antes |
| **R3** | Escalador normal / alternativo (p. ej. DLSS ↔ RTX Video) |
| **A**, **X / Y / B**, **L1 / L2 / R2 / R1** | Solo con el extra no oficial "DLSS 5" (ver *Avisos legales*): sí / no, 1-3 pasadas y nivel |
| Teclado: **Ctrl+G** sobre la imagen | Como L3 |

**Modo PC** (el mando fuera de la imagen de la consola; `mando_pc=1`): stick izquierdo = ratón, stick derecho = rueda, A = clic,
X = clic derecho, R3 = clic central, B = Esc, cruceta = flechas, Vista + Menú = Alt+F4, L3 = teclado en pantalla.

## Preguntas frecuentes y problemas comunes

**Windows o el antivirus no me deja abrirlo.** El exe no está firmado. SmartScreen: *Más información > Ejecutar de todas formas*.
Si el antivirus lo pone en cuarentena, restáuralo y añade la carpeta a sus exclusiones; o compílalo tú (ver *Para desarrolladores*).

**La descarga de mpv / Magpie falla.** Hace falta internet una vez. Con proxy o sin conexión: baja los dos zip que enlaza la ventana
(también en `licencias\VERSIONES.txt`) en otro PC y ponlos en una carpeta `descargas\` junto a `ConsoleEnhancer.exe`; se comprueban
igual. Si dice que no puede escribir, la carpeta es de solo lectura (Archivos de programa): muévela a una tuya.

**Pantalla negra o "No Signal".** Comprueba que la imagen se ve en el *Probar imagen* del asistente. Cierra cualquier programa que use
la capturadora (OBS, el software del fabricante, Discord con la cámara...). Si la consola cifra la salida (**HDCP**), la imagen sale
negra: apágalo en la consola (PS5: Ajustes > Sistema > HDMI > *Activar HDCP*). ConsoleEnhancer no quita protecciones anticopia.
El aviso de HDCP solo sale con la imagen negra de verdad y la consola en silencio 20 s seguidos (`hdcp_aviso_s`; `hdcp_aviso=0` lo
quita): una escena oscura o un fundido ya no lo hacen saltar.

**No hay sonido.** En el asistente, paso del sonido: prueba otra entrada con *Probar sonido* (algunas capturadoras dan el audio por el
propio dispositivo de vídeo y otras como un micrófono aparte).

**No me sale DLSS o la generación de fotogramas.** DLSS solo existe en NVIDIA GeForce RTX; Smooth Motion en RTX 40/50; la generación
necesita una pantalla de 100 Hz o más. Con otras gráficas va FSR 1 y la generación que tenga tu gráfica. Actualiza el driver.

**Va a tirones o con retraso.** Abre el asistente otra vez (con la pantalla y la capturadora a su resolución buena), cierra programas que
pinten de fondo y, si la tele lo tiene, pon el modo juego. El log dice qué cadena se eligió.

**Sale "Recuperando la imagen".** La imagen se paró con la consola encendida (casi siempre la gráfica: driver, mucha carga) y la app
la vuelve a abrir sola; no hace falta tocar nada. Si pasa a menudo, actualiza el driver y quita el *overclock* de la gráfica. El log
dice el motivo ("NO es la consola apagada: ...").

**Se me ha quedado el escritorio en negro.** `ConsoleEnhancer.exe --restaurar-escritorio`.

**¿Dónde están los ajustes y el log?** En `datos\` junto al exe (portable) o en `%LOCALAPPDATA%\ConsoleEnhancer`:
`consoleenhancer.ini` y `logs\consoleenhancer.log` / `engine.log`. Si abres un *issue*, adjunta el log (revisa antes que no lleve nada
tuyo que no quieras enseñar, como nombres de carpetas).

**¿Y "DLSS 5"?** Es una DLL de NVIDIA modificada por la comunidad: **no viene, no se baja y no se recomienda**. La app funciona
entera sin ella; si ya la tienes, mira [DLSS 5 (opcional)](#dlss-5-opcional).

**¿Se ha estropeado mpv o Magpie?** Icono de la bandeja > **Componentes: comprobar / volver a descargar**: dice qué falta y los vuelve a
bajar de sus páginas oficiales.

**"Apagar el PC al apagar la consola" me ha cerrado cosas.** Apaga Windows de verdad tras la cuenta atrás: guarda tu trabajo, o
desactívalo en el menú de la bandeja. Cerrar la app a mano nunca apaga nada.

## Avisos legales

- **Licencia**: ConsoleEnhancer es software libre bajo la **GPL-3.0** (`LICENSE`), **sin ninguna garantía**, con un permiso adicional de
  la sección 7 para usarlo con las librerías oficiales de NVIDIA, AMD, Intel y Microsoft (`LICENSE-EXCEPTION.txt`). Código de terceros,
  versiones y licencias: [`THIRD-PARTY.md`](THIRD-PARTY.md) y `licencias\` en el zip.
- **Lo que va en el zip**: la app y las DLL de NVIDIA Streamline / DLSS / Reflex, sin cambios y firmadas por NVIDIA, que NVIDIA deja
  repartir dentro de una aplicación; sus condiciones (no venderlas sueltas, no hacer ingeniería inversa...) están en
  `licencias\NVIDIA-AVISO.txt` y valen también para ti.
- **mpv y Magpie no los reparte ConsoleEnhancer**: los baja la app de sus releases oficiales, con sus licencias (GPL) y su código fuente
  en sus páginas.
- **"DLSS 5" no se incluye.** `nvngx_dlssnr.dll` es una DLL de NVIDIA modificada por la comunidad, sin firma ni licencia, y
  `runtimes\dlssg_sm86` un runtime de la comunidad con DLL autofirmadas. No están en el repositorio ni en el zip, la app no los baja, y
  su uso puede ir contra las condiciones de NVIDIA. Si ya los tienes, puedes añadirlos tú (bandeja > *Añadir DLSS 5*, que los copia a
  `extras\`) bajo tu responsabilidad: la app avisa de que no son oficiales.
- **Autor**: ConsoleEnhancer lo crea y mantiene **IanRog29** (con las aportaciones de sus colaboradores). Copyright (C) 2026 IanRog29 y
  colaboradores de ConsoleEnhancer.
- **Donaciones**: voluntarias (Ko-fi); no dan acceso a nada extra ni compran DLL de terceros.
- **Marcas**: NVIDIA, GeForce RTX, DLSS, Reflex, AMD, FSR, FidelityFX, Intel, XeSS, Nintendo, PlayStation, Xbox, Windows y demás son de
  sus dueños; se nombran solo para describir compatibilidad. ConsoleEnhancer no está afiliado, patrocinado ni respaldado por ninguno.
  Usa NVIDIA DLSS, NVIDIA Reflex y NVIDIA Streamline.
- **Privacidad**: la app no manda nada a ningún sitio. Solo se conecta a internet para bajar mpv y Magpie de GitHub la primera vez.
- **Contenido**: muestra en tu pantalla la imagen de tu consola; no graba, no emite y no quita el HDCP.

## Para desarrolladores

**Un clic:** con el código bajado (`git clone` o el zip de "Code"), doble clic en **`preparar.bat`**. Baja las dependencias a
`terceros\` (versiones fijas, con su SHA-256), compila el lanzador y el motor, pasa las pruebas y deja `bin\ConsoleEnhancer.exe` listo
para abrir. `preparar.bat paquete` además monta el zip de la release en `dist\`. Solo hace falta Windows 10/11: se compila con el
`csc.exe` de .NET Framework 4.8 que trae Windows (C# 5), sin Visual Studio.

Paso a paso:

```bat
powershell -ExecutionPolicy Bypass -File scripts\descargar_dependencias.ps1   :: terceros\mpv, terceros\magpie, terceros\streamline
build.bat                       :: bin\ConsoleEnhancer.exe (src\Lanzador + src\Deteccion) y bin\ listo (mpv y magpie enlazados a terceros\)
build_motor.bat                 :: bin\motor\ConsoleEnhancerEngine.exe (src\Motor) + DLL de Streamline
tests\build_deteccion.bat       :: tests\bin\DeteccionTest.exe
tests\bin\DeteccionTest.exe --simula=todo --sin-d3d12 --salida=deteccion.txt   :: 23 PCs simulados
powershell -ExecutionPolicy Bypass -File scripts\empaquetar.ps1 -Zip -Version v1.0.0   :: dist\ConsoleEnhancer\ y dist\ConsoleEnhancer-v1.0.0.zip
```

- **Dependencias**: la tabla `scripts\componentes.json` (versión, URL, SHA-256 y qué se coge de cada zip) la usan
  `descargar_dependencias.ps1` y la propia app. Para cambiar de versión, se cambia ahí.
- **Paquete** (`scripts\empaquetar.ps1`): lista blanca. Lleva la app, el motor con Streamline / DLSS, scripts, idiomas, recursos y
  licencias; **no** lleva mpv ni Magpie (los baja la app) ni nada no redistribuible, y comprueba que no hay datos del PC donde se monta
  (tu usuario, el nombre del equipo y lo que pongas en `scripts\privado.txt`, que no va a git).
- **GitHub Actions** (`.github/workflows/compilar.yml`): compila y pasa las pruebas en cada push; con una etiqueta `v*` monta el zip y
  crea la Release con él y su `.sha256`.
- **Pruebas sin consola**:

| Argumento | Qué hace |
|---|---|
| `--datos=<carpeta>` | Otra carpeta de datos (ini, log, juegos): no toca los de verdad |
| `--asistente-auto` | Escribe el ini con el plan de la detección (escalas estimadas, imagen, mando), sin preguntar |
| `--simula=<caso>` | Con `--asistente-auto`: el plan de otro PC (`amd-rx7800xt`, `intel-arc-b580`, `rtx3060`, `sin-nvidia`...) |
| `--dry-run` | Prepara la copia de Magpie y apunta en el log la cadena y la línea de mpv, sin lanzar nada |
| `--componentes=<carpeta>` | Baja e instala mpv y Magpie en esa carpeta sin preguntar (o con los zip de `<carpeta>\descargas`) e informa en `componentes.log` |
| `--componentes-reparar` | Vuelve a bajar mpv y Magpie aunque estén bien (lo que hace bandeja > *Componentes: comprobar / volver a descargar*) |
| `--ui-preview=asistente;<carpeta>` | Imágenes de los 9 pasos del asistente (es / en), sin abrir nada |
| `--ui-preview=apagado;<carpeta>` | Imágenes del aviso de apagado |
| `--prueba-apagado[=<s>[;fuera]]` | La cuenta atrás de verdad; al acabar solo apunta "SIMULADO" en el log |
| ini: `test_source=barras`, `apagar_pc=0` y en `motor_extra` `power_test=1 prueba_cuelgue_ms=4000` o `prueba_apagado_s=35 prueba_apagado_modo=corta` (o `nosignal`) | El motor con la imagen de prueba simula la gráfica parada 4 s (la app no debe cerrarse) o la consola apagada (sí) |
| `--iniciar-con-windows=1` / `=0` | Pone o quita el inicio con Windows y sale |
| `--test-consolas=<informe>` | Perfiles de consola y su detección con un reloj falso |
| `--restaurar-escritorio` / `--escritorio-negro` | El escritorio normal o el negro a mano |

**Estructura**

```
src\Lanzador\   la app de bandeja (C# 5, WinForms): mpv, Magpie, el motor, mandos, telón, asistente, componentes, inicio y apagado,
                extras del usuario (Añadir / Quitar DLSS 5) y el enlace de apoyo (Extras.cs)
src\Deteccion\  detección de gráfica, pantallas, capturadoras, audio y mandos; el plan de imagen; PCs simulados
src\Motor\      el motor de imagen D3D12 (captura, DLSS / FSR 1, generación, Reflex, OSD)
scripts\        lua y glsl de mpv, componentes.json, descargar_dependencias.ps1, empaquetar.ps1
recursos\       base.ini (ajustes de partida), mpv.conf (portable_config de mpv), icono
idiomas\        en.txt (textos de pantalla en inglés)
parches\        cambios de código fuente para Magpie (GPL-3.0), opcionales
tests\          DeteccionTest
DISENO.md       diseño y decisiones; LEEME.txt: estado, lo probado y lo que falta
```

Lee [`CONTRIBUTING.md`](CONTRIBUTING.md) antes de mandar cambios (C# 5, nada de binarios ni datos personales en git). Fallos de
seguridad: [`SECURITY.md`](SECURITY.md). Cambios por versión: [`CHANGELOG.md`](CHANGELOG.md).

---

<a id="english"></a>
# English

**Your console on the TV, better than ever, without touching a keyboard.** ConsoleEnhancer turns the living-room PC into the "box"
between your console and your TV: plug the console into a capture card, the PC into the TV, and that's it. Turn the console on and the
app opens by itself; the picture is **upscaled to 4K** (DLSS, FSR, XeSS, RTX Video), with **frame generation** when your GPU supports it
(DLSS Frame Generation, Smooth Motion, FSR 3 / XeSS FG), **HDR** and **VRR**, and **as little latency as possible**. Everything is driven
**with the controller** from the couch, no mouse or keyboard; when you turn the console off, the app closes and can **shut down the PC**.

- **Sets itself up**: a wizard detects the GPU, TV, capture card, sound and controller, tests picture and sound and picks the best chain
  for your PC. Scaling **adapts to your GPU** while you play (and remembers it).
- **Any console**: Nintendo Switch / Switch 2, PlayStation, Xbox... Console menus are recognised and shown at native 4K.
- **Recognises each game and keeps its profile**: on Switch / Switch 2 it reads the game name on the HOME screen and stores its icon,
  so whatever you change with the controller (capture, DLSS 5 model and passes, upscaler, 30/60 mode and frame-gen type) is remembered
  per game and restored next time. On **PS5** it reads the game name in the control center or game switcher (PS button) and keeps a
  profile per game too; on **Xbox Series**, in the Guide and Home tiles (*preliminary: not yet tested with a real Xbox*). If the game is
  not recognised, the single console-wide profile is used as before (`ps5_reconocer_juegos` / `xbox_reconocer_juegos`, 0 = one profile).
  **Important on PS5/Xbox:** the game name only shows in the control center / switcher (PS button) or the Guide, not during gameplay. So the
  app remembers each game, **press the PS button once after entering a game** (the control center opens, the app reads the name and saves
  it); it is re-applied automatically afterwards. The console menu is shown at native 4K whenever this needs it, regardless of
  `home_4k_nativo` (that option only affects the Switch 2 HOME).
- **Made for the living room**: starts with Windows on a black screen until the picture shows; switch modes (30 / 60 fps, frame
  generation on / off, capture resolution) with the controller buttons.
- **Free, open source (GPL-3.0), no ads, no telemetry.** The interface is in Spanish and English; code comments and logs are in Spanish.

Created and maintained by **IanRog29** · [Source and downloads](https://github.com/IanRog29/ConsoleEnhancer)

## Quick start

1. Download the latest **`ConsoleEnhancer-vX.Y.Z.zip`** from **[Releases](https://github.com/IanRog29/ConsoleEnhancer/releases)** (not the
   green "Code" button: that is the source code).
2. **Unzip** it into one of your own folders (Documents, Desktop, `D:\Games`...), not *Program Files*.
3. Open **`ConsoleEnhancer.exe`** and follow the wizard. Done: turn the console on.

If Windows says *"Windows protected your PC"*: **More info > Run anyway** (the exe is not signed with a paid certificate). The first time
it downloads **mpv** and **Magpie** (free programs used for upscaling and Magpie's frame generation) from their official pages: about
670 MB, once, SHA-256 checked. If they break later: tray icon > **Components: check / download again**. Run the wizard again from the
tray icon. To uninstall: untick "start with Windows" in the tray menu, close the app and delete its folder (and
`%LOCALAPPDATA%\ConsoleEnhancer` if it was not portable).

## Support the project

ConsoleEnhancer is **free and complete**: no paid version, no locked features, no ads. If you find it useful and want to buy me a
coffee so I can keep improving it, you can do so on Ko-fi. It is **entirely optional**.

[![Support me on Ko-fi](https://img.shields.io/badge/Ko--fi-buy%20me%20a%20coffee-FF5E5B?style=for-the-badge&logo=ko-fi&logoColor=white)](https://ko-fi.com/ianrog29)

Also from the app: tray icon > **Support the project (Ko-fi)**. A GitHub star, bug reports and telling which PC and console you use help
a lot too.

## DLSS 5 (optional)

"DLSS 5" is the community name for an NVIDIA neural filter (DLSSNR, as used by Magpie) that redraws the console picture with more detail
before upscaling. **It is not an official NVIDIA feature and does not ship with ConsoleEnhancer**: it needs `nvngx_dlssnr.dll`, a
community-modified NVIDIA DLL with no signature or license that cannot be redistributed. So the app **does not include it, download it
or say where to get it**. Everything else works the same without it.

If you already have your own file: tray icon > **Add DLSS 5 (your own file)…** (or the **Add DLSS 5** button on the wizard's last step;
GeForce RTX with 6 GB+ VRAM). Read the notice (unofficial, unsupported by NVIDIA, your own responsibility), pick your
`nvngx_dlssnr.dll`; the app checks it is a 64-bit Windows DLL with that name and copies it to `extras\` (optionally also your
`dlssg_sm86` folder for RTX 30). Then choose whether to turn it on; in game the **A** button toggles it. **Remove DLSS 5** in the tray
deletes the copy.

## Requirements

Windows 10 2004 or later (64-bit) with .NET Framework 4.8 (built into Windows 10/11), a Direct3D 12 GPU (NVIDIA, AMD or Intel; GeForce
RTX for DLSS, RTX 40/50 for Smooth Motion / DLSS FG), a DirectShow capture device (USB/UVC or PCIe), about 1.2 GB of disk. Frame
generation needs a 100 Hz+ display.

## Controller cheat sheet

With the console picture in front (the console also receives the press):

| Controller | 30 → 60 → 120 fps mode | Frame generation on / off |
|---|---|---|
| Xbox / XInput (GameSir, 8BitDo in X mode) | Menu (≡) | View (⧉) |
| PlayStation 5 (DualSense / Edge) | Options | Create |
| PlayStation 4 (DualShock 4) | Options | Share |
| Nintendo Switch / Switch 2 (Pro, Joy-Con, 8BitDo in Switch mode) | + | − |
| Generic HID | Start | Select |

The 120 fps (esports) mode joins the cycle only when the console outputs 120, the TV runs at **120 Hz or more** and the current capture is one your capture card lists at 120 (usually **1080p or 1440p**; read from the device by the wizard, `capturas_120`); at 4K it stays 30 ↔ 60. It captures at 120 with no frame generation for the lowest latency. Frame generation also works with Capture, the touchpad or the GameSir capture button. **D-pad** = capture at 720p / 1080p / 1440p /
4K · **L3** = frame generation type (hold 1 s: dither filter) · **R3** = main / alternative upscaler · **A X Y B, L1 L2 R2 R1** = only
with the unofficial "DLSS 5" extra · keyboard **Ctrl+G** = like L3. Outside the console picture the controller drives the PC (left
stick = mouse, A = click, B = Esc, L3 = on-screen keyboard).

## FAQ

- **Windows / antivirus blocks it**: the exe is unsigned; SmartScreen: *More info > Run anyway*, or build it yourself.
- **The mpv / Magpie download fails**: internet is needed once. Offline or behind a proxy: download the two zips shown in that window
  (also in `licencias\VERSIONES.txt`) elsewhere and put them in a `descargas\` folder next to the exe.
- **Black screen / "No Signal"**: check *Test picture* in the wizard, close other programs using the capture card (OBS, vendor
  software...). If the console uses **HDCP** the picture is black: turn it off in the console (PS5: Settings > System > HDMI > *Enable
  HDCP*). ConsoleEnhancer does not remove copy protection. The HDCP warning only shows after 20 s of truly black picture with a silent
  console (`hdcp_aviso_s`; `hdcp_aviso=0` turns it off), so dark scenes and fades no longer trigger it.
- **No sound**: try another input with *Test sound* in the wizard.
- **No DLSS / frame generation**: DLSS needs a GeForce RTX, Smooth Motion an RTX 40/50, frame generation a 100 Hz+ display.
- **"Recovering the picture"**: the picture froze with the console on (usually the GPU) and the app reopened it by itself. If it
  happens often, update the driver and remove any GPU overclock; the log says why.
- **mpv / Magpie broken**: tray icon > *Components: check / download again*.
- **Desktop stuck black**: `ConsoleEnhancer.exe --restaurar-escritorio`.
- **Settings and logs**: `datos\` next to the exe (portable) or `%LOCALAPPDATA%\ConsoleEnhancer` (`consoleenhancer.ini`,
  `logs\consoleenhancer.log`, `engine.log`). Attach the log to bug reports (check it first for anything private).

## Legal

- **License**: free software under the **GPL-3.0** (`LICENSE`), **without any warranty**, plus a section 7 additional permission for
  use with the official NVIDIA, AMD, Intel and Microsoft libraries (`LICENSE-EXCEPTION.txt`). Third-party code and licenses:
  [`THIRD-PARTY.md`](THIRD-PARTY.md).
- **In the zip**: the app plus the unmodified, NVIDIA-signed Streamline / DLSS / Reflex DLLs, which NVIDIA allows applications to
  redistribute; their terms (`licencias\NVIDIA-AVISO.txt`) also apply to you.
- **mpv and Magpie are not redistributed by ConsoleEnhancer**: the app downloads them from their official releases; their licenses and
  source code are on their pages.
- **"DLSS 5" is not included**: the community-modified `nvngx_dlssnr.dll` and the `dlssg_sm86` runtime are never in the repository or
  the zip and are not downloaded; using them may breach NVIDIA's terms. If you already have them you may add them yourself (tray >
  *Add DLSS 5*, which copies them to `extras\`) at your own risk.
- **Author**: created and maintained by **IanRog29** (with its contributors). Copyright (C) 2026 IanRog29 and ConsoleEnhancer contributors.
  Donations (Ko-fi) are voluntary and unlock nothing.
- **Trademarks** belong to their owners and are used only to describe compatibility. Not affiliated with, sponsored or endorsed by
  NVIDIA, AMD, Intel, Nintendo, Sony or Microsoft. Uses NVIDIA DLSS, NVIDIA Reflex and NVIDIA Streamline.
- **Privacy**: no telemetry; the only connection is the first-run download of mpv and Magpie from GitHub.

## Building

Double-click **`preparar.bat`** (downloads pinned dependencies with SHA-256 checks, builds, runs the tests and leaves
`bin\ConsoleEnhancer.exe` ready); `preparar.bat paquete` also builds the release zip. Only Windows is needed: the code is C# 5, built
with the `csc.exe` of .NET Framework 4.8 that ships with Windows. Step by step and test switches: see *Para desarrolladores* above.
Contributions: [`CONTRIBUTING.md`](CONTRIBUTING.md). Security: [`SECURITY.md`](SECURITY.md).
