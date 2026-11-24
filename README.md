# ConsoleEnhancer

[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/IanRog29/ConsoleEnhancer?label=download&sort=semver)](https://github.com/IanRog29/ConsoleEnhancer/releases)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%2F%2011%20(64--bit)-0078D6.svg)](#requirements)
[![Ko-fi](https://img.shields.io/badge/Ko--fi-support%20the%20project-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/ianrog29)

English · [Español](#español)

<a id="english"></a>

**Your console on the TV, better than ever, without touching a keyboard.** ConsoleEnhancer turns the living-room PC into the "box"
between your console and your TV: plug the console into a capture card, the PC into the TV, and that's it. Turn the console on and the
app opens by itself; the picture is **upscaled to 4K** (DLSS, FSR, XeSS), with **frame generation** when your GPU supports it
(Smooth Motion ×2 and the app's own Enhanced FG up to ×4, experimental), **HDR** and **VRR**, and **as little latency as
possible**, all in the app's own image engine (no other player or upscaler window in between). Everything is driven
**with the controller** from the couch, no mouse or keyboard; when you turn the console off, the app closes by itself and, if you want,
the PC goes into a **low-power mode, like a console in rest mode**: it doesn't shut down or sleep, it stays on using as little as
possible, and turning the console back on brings the picture back by itself.

- **Sets itself up**: a wizard detects the GPU, TV, capture card, sound and controller, tests picture and sound and picks the best chain
  for your PC. Scaling **adapts to your GPU** while you play (and remembers it).
- **Any console**: Nintendo Switch / Switch 2, PlayStation, Xbox... Console menus are recognised and shown at native 4K.
- **Made for the living room**: starts with Windows on a black screen until the picture shows, with no windows or pop-ups in the way;
  switch modes (30 / 60 fps, frame generation on / off, capture resolution) with the controller buttons or the on-screen settings menu.
- **Free, open source (GPL-3.0), no ads, no telemetry.** The interface is in Spanish and English; code comments and logs are in Spanish.

Created and maintained by **IanRog29** · [Source and downloads](https://github.com/IanRog29/ConsoleEnhancer)

---

## Quick start

1. Download the latest **`ConsoleEnhancer-v1.3.3.zip`** (or newer) from
   **[Releases](https://github.com/IanRog29/ConsoleEnhancer/releases)** (not the green "Code" button: that is the source code).
2. **Unzip** it into one of your own folders: Documents, the Desktop, `D:\Juegos`... (not in *Program Files*).
3. Open **`ConsoleEnhancer.exe`** and follow the wizard. Done: turn the console on.

First-run details:

- If Windows says *"Windows protected your PC"*: **More info > Run anyway** (the exe is not signed with a paid certificate; the source
  code is here for anyone who wants to review it).
- The first time it downloads **Intel XeSS** and **AMD FSR** (free components its engine uses for XeSS / FSR upscaling and frame
  generation) from their official pages: about 205 MB, once, with their SHA-256 hash checked. Click
  **Download**. If something breaks later: tray icon > **Components: check / download again**.
- The **wizard** asks for the language and shows what was detected, the capture card with **Test picture**, the sound with **Test sound**,
  the console, the display and the plan. At the end you choose whether it starts with Windows and what the PC does when you turn the
  console off (nothing, low-power mode, sleep or shut down; see [Power](#power-what-the-pc-does-when-the-console-turns-off)).

**Settings and uninstall: open `ConsoleEnhancer Settings.exe`** (next to `ConsoleEnhancer.exe`), with the app open or closed. It has
everything in one window: open the app or the setup wizard, how the PC is used (like a console / like a normal PC), start with Windows,
black desktop, what the PC does when the console turns off, DLSS 5 (your own file), download the components again, test the countdown or
the standby, the logs and **Uninstall** (it puts Windows back as it was and deletes only the app's own files; you choose whether to keep
your settings and game profiles). The same options are in the tray icon menu.

## Controls

Everything works **with the controller you already use for the console** (the app reads it without taking it away: the console still
gets every press, e.g. Options also opens the game's menu on PS5) or with a keyboard. The buttons only act **while the console picture
is in front**. They go by position, so the same spot does the same thing on every controller.

### Controller and keyboard

| What it does | Xbox / XInput<br>(GameSir, 8BitDo X-mode) | PlayStation 5 / 4<br>(DualSense, DualShock 4) | Nintendo Switch / Switch 2<br>(Pro, Joy-Con, 8BitDo) | Keyboard |
|---|:---:|:---:|:---:|:---:|
| **Settings menu** on screen (open / close) | Xbox button | PS | HOME | `Ctrl`+`O` |
| **30 → 60 → 120 fps** mode | Menu ≡ | Options | + | `Ctrl`+`M` |
| **Frame generation** on / off | View ⧉ | Create (PS5) / Share (PS4) · touchpad | − · Capture | `Ctrl`+`F` |
| Frame generation **type** (Smooth Motion, Enhanced FG) | L3 | L3 | L3 | `Ctrl`+`G` |
| Dither filter (new / old) | hold L3 1 s | hold L3 1 s | hold L3 1 s | |
| **Capture** at 720p / 1080p / 1440p / 4K | D-pad ← ↑ → ↓ | D-pad ← ↑ → ↓ | D-pad ← ↑ → ↓ | `Ctrl`+`1` … `Ctrl`+`4` |
| Main / alternative **upscaler** (the alternative is FSR 1) | R3 | R3 | R3 | `Ctrl`+`R` |
| **DLSS 5** on / off ¹ | A | ✕ | B | `Ctrl`+`D` |
| DLSS 5 **passes** 1 / 2 / 3 ¹ | X / Y / B | □ / △ / ○ | Y / X / A | |
| DLSS 5 **level** 1 / 2 / 3 / 4 ¹ | LB / LT / RT / RB | L1 / L2 / R2 / R1 | L / ZL / ZR / R | |
| Show the current settings | | | | `Ctrl`+`I` |
| Quit the app | | | | `Alt`+`F4` |

¹ Only with the optional "DLSS 5" file (see [DLSS 5](#dlss-5-optional)).

The 120 fps mode only shows up where your capture card and screen allow it; otherwise the mode button toggles 30 ↔ 60. Generic
controllers: Start = Menu, Select = View, the rest by position. The old layout (View = 30 fps, Menu = 60 fps): `mando_botones=prisma`.

### Inside the settings menu

| | Controller | Keyboard |
|---|:---:|:---:|
| Move between rows | D-pad / left stick ↑ ↓ | ↑ ↓ |
| Change the value | D-pad / left stick ← → | ← → |
| Next value · press the button at the bottom (**Apply**) | A / ✕ / B on Nintendo | `Enter` / `Space` |
| Close without changing anything | B / ○ / A on Nintendo · the centre button | `Esc` / `Backspace` |

Nothing changes until **Apply**: everything you changed (marked with a cyan dot) is applied at once and saved for that game. The menu
also opens while the picture is still being prepared; values that can't be used right now are greyed out with the reason below.

### PC mode (controller outside the console picture)

With `mando_pc=1` (the default) the same controller also drives Windows whenever the console picture is not in front.

| What it does | Xbox / XInput | PlayStation | Nintendo |
|---|:---:|:---:|:---:|
| Mouse pointer | left stick | left stick | left stick |
| Scroll wheel | right stick | right stick | right stick |
| Left click | A | ✕ | B |
| Right click | X | □ | Y |
| Middle click | R3 | R3 | R3 |
| Esc | B | ○ | A |
| Arrow keys | D-pad | D-pad | D-pad |
| Alt+F4 | View + Menu | Create + Options | − and + |
| On-screen keyboard | L3 | L3 | L3 |
| PC mode on / off | hold Xbox button (or LB+RB) 1 s | hold PS (or L1+R1) 1 s | hold HOME (or L+R) 1 s |

## Support the project

ConsoleEnhancer is **free and complete**: no paid version, no locked features, no ads. If you find it useful and want to buy me a
coffee so I can keep improving it, you can do so on Ko-fi. It is **entirely optional**.

[![Support me on Ko-fi](https://img.shields.io/badge/Ko--fi-buy%20me%20a%20coffee-FF5E5B?style=for-the-badge&logo=ko-fi&logoColor=white)](https://ko-fi.com/ianrog29)

Also from the app: tray icon > **Support the project (Ko-fi)**. A GitHub star, bug reports and telling which PC and console you use it
with help a lot too.

## Requirements

| | Minimum | For the best results |
|---|---|---|
| System | Windows 10 2004 or later, 64-bit, .NET Framework 4.8 (built into Windows 10/11) | Windows 11 |
| GPU | Any with Direct3D 12 (NVIDIA, AMD or Intel) | NVIDIA GeForce RTX (DLSS; RTX 40/50: Smooth Motion) |
| Capture card | Any DirectShow device (USB/UVC or PCIe) with the console connected | One that captures at 1440p or 4K with low latency |
| Display | Any | 4K 120 Hz TV or monitor (frame generation needs 100 Hz or more) |
| Disk | About 500 MB | |

## What it does

- **First-run wizard** (and again from the tray), 9 steps: language, what was detected, the capture card with *Test picture*, the sound
  with *Test sound*, the console, the display, the **picture** (HDR and VRR), the **controller** (button table for yours) and the plan.
- **Chain chosen by GPU** (`src\Deteccion\Plan.cs`): RTX 40 / 50, DLSS SR + Reflex + **Smooth Motion** ×2 (Enhanced FG with the TV
  at 119.88 Hz or a driver without Smooth Motion; Enhanced FG in the selector); RTX 20/30, DLSS SR + Reflex
  (Enhanced FG optional); without DLSS, **XeSS** on GTX and Intel Arc and **FSR 4** on AMD RX 6000 / 7000 / 9000 (official on
  RX 9000; on RX 6000 / 7000 through the FSR 4.1.1 unlock, experimental; otherwise FSR 3.1), plus whatever frame generation each one
  has (driver AFMF, XeSS FG). Enhanced FG is never picked by default: it is experimental. **FSR 3 FG is no longer offered** (1.3):
  measured against real gameplay video it was the worst of all, with generated frames that barely move; `generacion_fsr3=1` in the ini
  brings it back as "FSR 3 FG (experimental)". **XeSS FG is not offered either, for now** (1.3.1): not in the settings menu, L3, the
  lists or as an automatic fallback; game profiles and choices that had it switch to the closest one on this PC (Smooth Motion on
  RTX 40 / 50, same ×2) and the app says so once. Smooth Motion ×4, which ran the engine's XeSS FG ×2 underneath, is gone too (1.3.2).
  `generacion_xess=1` in the ini brings XeSS FG back.
- **Everything in the built-in image engine** (D3D12, `src\Motor`; there is nothing to choose): DirectShow capture; upscaling with
  DLSS, XeSS, FSR 3.1 / FSR 4 (with Intel's and AMD's official DLLs, downloaded on first start) or FSR 1 (EASU + RCAS); "DLSS 5" with
  your own file (1 to 3 passes, with any frame generation type); frame generation with **Smooth Motion** (NVIDIA driver, RTX 40 / 50),
  and **Enhanced FG** (experimental; XeSS FG only with `generacion_xess=1`, FSR 3 FG only with `generacion_fsr3=1`); Reflex / XeLL; and, optionally, NVIDIA RTX
  Dynamic Vibrance (`motor_extra=vibrancia=1`). Colour from YUY2 / NV12 capture cards is properly reconstructed, without blocky red
  edges (`croma=2` in `motor_extra`; `croma=0` = as before). Since 1.3 the app no longer uses or downloads Magpie or mpv, and NVIDIA
  DLSS Frame Generation is gone.
- **Frame generation ×3 / ×4, legally** (30 fps mode, settings menu > *Multiplier*; ×3 also in the 40 fps mode):
  - **FSR 3 FG ×3 / ×4** on any GPU (experimental, only with `generacion_fsr3=1`: with real gameplay video its generated frames barely
    move, so FSR 3 FG left the menu): ×2 is AMD's FSR 3 FG as always; ×3 / ×4 use the app's own build of AMD's MIT-licensed FSR 3.1
    frame interpolation (`engine\CeFsrFg.dll`), changed to generate frames at 1/3, 2/3 or 1/4, 2/4, 3/4 between two real frames from one
    optical-flow pass. Measured on an RTX 4060 Ti with a 4K 120 Hz VRR TV and real console captures: ×4 keeps all 30 real frames, 98.6 %
    of the frames last exactly one refresh, the real frame is on screen 41 ms after it arrives and the first new image 17 ms. Still parts
    (HUD, text) are kept from the real frames. Also with DLSS 5 (measured at 4K with DLSS 5: ×4 keeps the 30 real frames, 118 fps, 99 % at one refresh, first new image 34 ms / real frame 59 ms from capture, against 33 / 50 ms with Smooth Motion ×2; `motor_fsr_multi_dlss5=0` = only without DLSS 5).
  - **Smooth Motion ×4 removed** (1.3.2): it ran the engine's XeSS FG ×2 underneath, whose generated frames were the weakest on real
    gameplay video. Smooth Motion is ×2; a saved Smooth Motion ×4 becomes ×2 and the app says so once. For ×3 / ×4, Enhanced FG.
  - **Enhanced FG ×2 / ×3 / ×4** (below).
  - Interpolating always waits for the next real frame: ×4 shows the real frame ~8 ms later than ×2. Smooth Motion ×3 / ×4 on its own and
    XeSS FG ×3 / ×4 outside Intel GPUs are not offered: they would need patching NVIDIA's driver or Intel's library.
- **Enhanced FG** (new, experimental, any GPU with Direct3D 12): the app's own frame generation, written from scratch for the engine (no
  NVIDIA, AMD or Intel SDK or library): its own optical flow in both directions with a forward/backward check to find what gets covered
  and uncovered, a per-pixel choice of motion in every generated frame, the still HUD kept sharp, no blended frame on scene cuts, and
  its own present thread that shows every frame at a fixed time counted from the capture's arrival. ×2 / ×3 / ×4 and two modes:
  quality (interpolation) and **low latency** (the generated frames go forward from the latest real frame, which is shown as soon as it
  is ready: measured at 4K, 30 fps mode, ×2, 60 frames/s with every frame on screen for exactly 2 refreshes and the real frame 9-10 ms
  after it arrives, against ~35 ms with Smooth Motion ×2). An extra choice in *Frame generation type*, never the default; settings menu
  rows *Multiplier* and *Enhanced FG: low latency*. Measured again in this build (4K, 30 fps mode, real PS5 captures): ×2 quality 60 fps evenly, real frame 33 ms / first new 16 ms; ×2 low latency real 9 ms (with DLSS 5: 33 ms, against 50 ms with Smooth Motion ×2); ×4 120 fps evenly but the real frame 62 ms after it arrives. It needs a screen at 100 Hz or more (at 60 Hz it was measured broken, so it is not offered there). Its shaders are compiled in the background by the first engine that starts, so picking it doesn't wait for them. On real gameplay video (GTA V driving) its generated frames are close
  to a plain blend of the two real ones (in a first-person game, close to Smooth Motion), so it is shown as "Enhanced FG (experimental)".
  ×3 / ×4 also with DLSS 5 (measured with real GTA V video at 4K, DLSS 5 level 4: ×4 with all 30 real frames, 120 fps, every frame for one
  refresh, DLSS 5 at 80 %); if ×4 can't keep 30, DLSS 5 is lowered and, as a last resort, ×2 for the session (`efg_multi_dlss5=0` = ×2 with
  DLSS 5). If the GPU fails with it, it stays in the menu with the reason and picking it again retries.
  **1.3.1**: ×2 stays smooth when the console game doesn't hold a steady 30 fps (each frame is timed by when its content arrived and the
  generated frames fill the real gap: on the TV, what is seen strays from its time 8.2 → 1.3 ms, 95th percentile); ×4 **low latency**
  shows the real frame one output frame later, so the first generated frame of each group is interpolated and the others are predicted
  1/4 less far ahead (about 22 % fewer errors when turning the camera, +5 ms; not with DLSS 5, where it wouldn't fit; ×2 and ×3 as
  before; `efg_retraso_lat4=-1` in `motor_extra` = as before); low latency has its own forward pass (fewer loose specks along edges).
  **1.3.2**: it reaches its frame rate when the GPU has room (×4 of the 30 fps mode and ×2 of the 60 fps mode, also at 1440p / 1080p +
  DLSS and with DLSS 5): a generated frame is no longer dropped just because the next real frame is already queued, only if it would
  collide with it (measured on the TV: ×2 low latency of the 60 fps mode 105.6 → 118.2 frames/s, ×4 low latency 120 with no skipped
  frames once running). Fast camera turns at a 30 fps base hold together: each pair of real frames first gets the camera's whole-image
  motion (a search of ±25 % of the width) that seeds the optical flow, which has six pyramid levels (against the true in-between frames
  of real gameplay video turning fast: the worst frames 42-46 → 20-27 of error). In the 40 fps mode, ×3 = 120 fps (the default there)
  or ×2 = 80. `efg_giro=0 efg_niveles=5` / `efg_salto_fino=0` in `motor_extra` = as 1.3.1.
- **Sharper upscaling in motion** (DLSS SR K / L / M and E, XeSS, FSR 3.1-4): each 8×8 tile of the engine's motion vectors is refined
  to a fraction of a pixel for the upscaler, so it no longer blends a misaligned history (measured against the true picture, 1440p → 4K
  in motion: K 1.32 → 0.82, M 0.92 → 0.44, L 0.79 → 0.44, E 1.05 → 0.72; on a video of real console captures K 1.29 → 0.78). Without
  DLSS 5, the capture goes to a temporal upscaler without the edge smoothing / dither removal / flat denoise that smeared fine floor
  textures even with the camera still. The **DLSS model you pick is the one you get** (it is one choice for all games); **XeSS** is also
  in the menu's *Upscaler* row (on NVIDIA too). About +1.3 ms of GPU at 1440p (`sr_vectores=0`, `sr_aa=1` = as before). Measured again
  with real 4K game video scaled down to 1440p and back up, against the original: the refined vectors are only used where they are
  reliable (a dark night scene no longer smears: DLSS M sharpness 0.42 → 0.82, with DLSS 5 0.52 → 0.89); the transformer models (K, L,
  M) get a light CAS only where the picture moves (moving DLSS M 0.75 → 0.84, still scenes unchanged); with DLSS 5 and an upscaler behind
  it, the capture also goes without edge smoothing / dither removal / flat denoise; the model you pick is no longer swapped for K by the
  GPU spike of rebuilding DLSS 5; and with DLSS 5 at 1440p / 1080p / 720p the default model is M, not K (`sr_vec_incert=0`,
  `sr_nitidez_mov=0`, `sr_aa_dlss5=0` = as before). **1.3.2**: where the previous frame moved by its vector doesn't match the current one
  (a floor seen in perspective, a camera turn), DLSS SR starts that pixel fresh instead of blending the wrong history: moving floors with
  DLSS M / L and camera turns with K / J stay sharp (road with DLSS M: floor sharpness 0.56 → 0.83 of the original; turn with K / J 0.76 /
  0.78 → 0.85), still scenes unchanged, about +0.3-0.5 ms at 1440p (`sr_sin_historia=0` = as before).
- **On-screen notices say what really runs** (taken from what the engine and the driver get, not from what was asked): "Apply" in the
  settings menu lists only what changed, with the real multiplier ("Enhanced FG x4 (quality) · 30 → 120 fps") and, if something asked can't
  run, why ("x4 unavailable: no VRR (G-SYNC) on the screen"); one notice per change (on the new engine when it is swapped), and every
  notice is written to the log.
- **The character's head no longer vanishes when the camera turns** (XeSS FG / FSR 3 FG and FSR 3 FG ×3 / ×4): the engine tells the
  character and the HUD apart from the background, gives each its own motion and puts the character in front in the depth (with FSR 3
  FG: head error −84 %, outline −73 %, HUD text −91 %; about 0.6 ms of GPU at 4K; `fg_cabeza=0` = as before).
- **No mpv any more** (1.3): the engine does everything mpv used to do. The console menu at native 4K, the grabs the app reads to
  detect the menu, the game and *No Signal* (now in colour, as mpv's were), the frozen-picture curtain, the low-power probe and the
  wizard's picture / sound tests. The **console sound** is the engine too, in a separate sound-only process
  (`ConsoleEnhancerEngine.exe --audio`): same capture pin, format and 20 ms blocks, Windows audio output (WASAPI), the same 7.1 →
  stereo mix with a limiter, and clock-drift correction inside it. `audio_motor_extra=` passes it `audio_cola_ms=30` (its queue),
  `audio_salida_ms=10`, `audio_deriva=0|1`; its log is `settings\logs\audio.log`. MJPEG capture cards (USB 2.0, e.g. MS2109) are decoded
  in the engine up to 720p; H.264 / HEVC capture is no longer supported (pick an uncompressed format).
- **Scaling that adapts to your PC** (`escalas_adaptables=1`, on by default): the wizard estimates the DLSS model and percentages for
  your GPU and display; while you play, the engine measures GPU time every second and lowers quality if it can't keep up (within 2 s)
  or raises it little by little if there is headroom (after 20 s), and remembers it for next time (`settings\scales_engine.txt`). Tray >
  Picture > *Self-adjusting scales* / *Forget the learned scales*. With the "DLSS 5" extra there is also an **emergency brake**: if
  the GPU is far over budget, it drops a pass or cuts the % at once (so the GPU doesn't choke) and keeps that as the learned value.
- **Checks that your frame rate is reached** (built-in engine): every quarter of a second the engine counts the console frames it had
  to drop (and, with Enhanced FG, the generated frames it skipped); a single hitch, a slow game on the console, waking up, the engine
  hand-over, the settings menu or losing focus don't count. When the PC misses the target it is seen in 1-2 s (1.3.1). **1.3.2**: it
  only lowers anything when the GPU is really full (the whole GPU, NVIDIA's own counter, over 90 %, or with Enhanced FG a real frame that
  takes long to be ready); otherwise it is the pacing, VRR's frame cap or the source, nothing is changed and a notice says so once
  ("Enhanced FG x4 · 113 of 120 fps: not the GPU (41 % busy), nothing changed"). Then the engine lowers, one step at a time and without
  reopening anything: DLSS 5 (big steps first, down to 25 % at 4K, **33 % at 1440p** and 44 % at 1080p, and one pass), then the DLSS
  upscaler (a lighter model, and as a last resort FSR 1 instead of DLSS for that session), and only then the multiplier (Enhanced FG ×4
  → **×3** → ×2) **for that session only**. Without a multiplier to lower (×2 or no frame generation) the app goes on: DLSS 5 off, 120 →
  60, a lower capture, 60 → 30 with frame generation, **all for this session only**: since 1.3.2 nothing the app lowers by itself is
  saved in the game profile. A notice says what was lowered ("Enhanced FG x4 · DLSS 5 at 25 % and DLSS K so it fits"); `escalera=0` in
  `motor_extra` = the 1.3 checks. With GPU to spare for a few minutes, the
  DLSS 5 scale goes back up the next time you enter the game, never to a value that already failed. Remembered per game and GPU
  (`settings\scales_auto.txt`).
- **Backup engine** (`motor_reserva=1`): while you play, an engine with the mode's other frame generation type waits ready in the
  background (XeSS FG while you play with Smooth Motion; Smooth Motion while you play with XeSS FG or FSR 3 FG), so switching between
  them takes ~0.5 s instead of 4-5 s. `motor_reserva=0` removes it (it uses ~0.9 GB of video memory).
- **Doesn't close by itself if the GPU stalls**: if the picture stops moving with the console on (GPU stuck or reset, the engine
  changing settings), the app tells it apart from a console that was turned off (the engine reports it, the capture card keeps sending
  frames or the console keeps making sound), waits and, if it lasts more than 8 s, reopens just the picture (*Recovering the picture*).
  With the console really off (*No Signal* screen, or no picture and no sound) it closes as usual, and it does not shut down the PC if
  the GPU failed in the last minute (`apagado_confirmar`, `cuelgue_relanzar_ms`, `apagar_pc_confirmar`). An error in a background thread
  goes to the log and the app keeps running (`ConsoleEnhancer.exe.config`).
- **No "No signal" on short drops**: when the console briefly changes HDMI mode (e.g. the PS5 switching to 120 Hz for a 120 Hz game),
  the last picture stays on screen; the usual notice only shows if it lasts more than 5 s.
- **Never stuck on a capture format**: if no picture arrives right after the app changes the capture (console menu, a game's profile,
  the settings menu, the 30 / 60 / 120 mode) while the console was on, it switches within seconds to a format your capture card does
  give (same size at 59.94 fps, the last one that worked, 1080p) and says so on screen (`captura_segura=1`).
- **HDR**: RTX HDR (NVIDIA) or Windows Auto HDR to convert the console's SDR picture, in the wizard's *Picture* step (off by default;
  they need Windows HDR turned on for the display).
- **VRR** (`vrr=auto`): turns on by itself only if the display has VRR (G-SYNC / G-SYNC Compatible / FreeSync / HDMI VRR) and Windows and
  the driver allow it; on AMD / Intel, enable it in the wizard (`vrr=1`). Also in tray > Picture > VRR. When on, the built-in engine
  presents each frame as soon as it is done (without waiting for the refresh) and, with frame generation, syncs only where needed
  (`motor_vrr_fg_vsync=-1`); only if the display reports VRR, so as not to cause stutter on one that doesn't have it. In 60 mode with
  frame generation (Enhanced FG, and since 1.3.2 Smooth Motion ×2: 59.94 real frames and 119.9 fps on the TV instead of 58 and 116,
  `motor_60sm_refresco_fijo=auto`) on a 120 Hz G-SYNC display, the engine runs at a **fixed refresh rate** (G-SYNC is turned off only in the NVIDIA profile for its
  exe while you play like this; it is restored on exit, and if the app crashes, the next time it opens): with G-SYNC, Reflex capped the
  output at ~116 frames/s and ~2 console frames per second were lost; this way all of them come through, with less latency
  (`motor_60fg_refresco_fijo=auto`; at 144 Hz or more, at 119.88 Hz, without VRR or without NVIDIA nothing is changed; never in the
  120 fps mode, which always keeps VRR).
- **Frame generation selector**: L3 while playing (or Ctrl+G on the keyboard over the picture, the settings menu or the tray) cycles
  through the types your GPU has (Smooth Motion, and Enhanced FG last; XeSS FG / FSR 3 FG only with `generacion_xess=1` /
  `generacion_fsr3=1`) and remembers the choice per game. Only what can keep pace is offered. Smooth Motion is ×2 (×4 removed in 1.3.2);
  Enhanced FG (and FSR 3 FG, if brought back) has ×2 / ×3 / ×4 in the settings menu's *Multiplier* row. If the real frames can't be held, the multiplier is the last step,
  for that session only (×4 → ×3 → ×2; see *Checks that your frame rate is reached*). Since 1.3 there is no DLSS Frame Generation and no old ×3 / ×4 (they didn't work well); game profiles and settings
  that used them switch by themselves to the closest type (on NVIDIA, Smooth Motion) and keep everything else. If a frame generation
  type loses the GPU in the engine, the rest of the session uses another one (FSR 3 FG ×3 / ×4: ×2), still in the engine.
- **Enhanced RT (Experimental)** (1.3.3): row at the end of the controller settings menu that adds screen-space ray traced lighting
  to the console image: contact shadows (ambient occlusion) and one-bounce light traced over a relief estimated from the image itself
  (consoles don't send depth), with its own motion vectors, temporal accumulation (no noise, no trails) and the HUD / text protected; no
  reflections. Off by default (`enhanced_rt=0`; the menu choice wins); about 0.7 ms of GPU per real frame (~1.1 ms at 4K with Enhanced
  FG ×4, ~1 ms more latency). It switches on / off without reopening the picture. Stronger: `motor_extra=rt_fuerza=1.5`.
- **Flat pans** (1.3.3): when you scroll a map or a menu with the stick, Enhanced FG (quality mode) measures that single movement
  precisely and follows it, also when the pan starts and stops, so roads and text no longer shake; the still HUD is left untouched
  (`efg_plano=0` = as 1.3.2). For maps, quality mode works better than low latency.
- **Add to Steam** (1.3.3): in the settings panel (ConsoleEnhancer Settings.exe, "Steam" section), adds ConsoleEnhancer to your Steam
  library as a non-Steam game with its icon (the same as Steam's "Add a Non-Steam Game"), without duplicating it and with a backup of
  Steam's list first; "Remove from Steam" takes it out. If Steam is open it offers to restart it. If the controller acts oddly when
  opened from Steam, turn Steam Input off for that shortcut.
- **40 fps mode** (1.3.2): for console games' 40 fps modes (the "balanced" modes of PS5 / Xbox Series). Set the console to 120 Hz
  output (PS5: Settings > Screen and Video > Video Output > 120 Hz Output: Automatic; for everything else the advice stays 4K at 60) and
  1440p or 1080p, pick the game's 40 fps / balanced mode and, in ConsoleEnhancer, 40 fps (settings menu, or the Menu button: 30 → 40 →
  60). Capture at 1440p / 1080p runs at 120 Hz (one new frame of every 3); at 4K it runs at 60 and the 2-1 cadence is evened out.
  Output: 40 fps with each frame 3 refreshes at 120 Hz, or Enhanced FG ×3 = 120 fps (default here), ×2 = 80 fps (VRR), Smooth Motion ×2
  = 80 fps. The type of frame generation is the one of the 30 fps mode; the Enhanced FG multiplier of the 40 fps mode is its own (×2 /
  ×3). The console menu still goes to native 4K at 60 as always.
- **120 fps mode (esports)**: for the console's 120 Hz games, it captures at 120 with no frame generation and the lowest latency. The
  mode button goes 30 → 40 → 60 → 120, the 120 only if the console outputs 120, the TV runs at **120 Hz or more** and the current capture is one your
  capture card provides at 120 (usually **1080p or 1440p**; the wizard reads it from the capture card, capturas_120). At 4K it stays
  30 ↔ 40 ↔ 60: the app doesn't lower your resolution by itself. If it doesn't fit, a notice says why. permitir_120=auto (1 / 0 to force it /
  remove it).
- **Console menu at native 4K**: the Switch 2 HOME, the PS5 menu and the Xbox menu are recognised and shown with no upscaling or frame
  generation, and without "DLSS 5" (as is, with ~10 ms less latency; `motor_dlss5_home=0`); switching menu ↔ game takes under 1 s, with the previous picture on screen until the new one (no black curtain since 1.3.2).
- **Console apps are not games**: the Store, Remote Play, the media gallery, PS Plus, Settings and Share Factory (on Xbox, Microsoft
  Store, Game Pass, Settings, Captures...) and video / music apps (Netflix, YouTube, Spotify, Twitch...) are treated like the console
  menu: native 4K, no game profile and no frame generation, until you are back on the menu (or the previous game's picture returns).
  They never create a fake game or get saved as a game's cover art.
- **Recognises each game and keeps its profile** (`games\profiles.txt`): on Switch / Switch 2 it reads the game name on the HOME screen
  and stores its icon, so whatever you change with the controller (capture, DLSS 5 model and passes, upscaler, 30/60 mode and frame
  generation type) is remembered per game and applied again automatically next time you enter it. On **PS5** it recognises the game by
  its **focused icon** on the Home screen (with its name next to it) and by its **cover art** when it starts: just enter the game from the
  Home screen; going back to the one that was already open with the PS button doesn't change the profile. On **Xbox Series**, the same
  with the focused tile on Home (the big one, with the name below) and each game's start-up image (*preliminary: tested with official
  screenshots, not yet with a real Xbox*). If the game is not recognised, it uses the single console-wide profile, as before
  (`ps5_reconocer_juegos=1`, `xbox_reconocer_juegos=1`; 0 = one profile for the whole console). What it learns is stored in
  `games\console_icons.txt` and `games\console_covers.txt`.
- **Controllers**: Xbox / XInput, DualSense / DualShock 4, Switch and Switch 2 controllers, 8BitDo, GameSir and generic HID, with no
  extra software. The app reads the controller without taking it away from the console and, outside the picture, drives the PC (PC mode).
- **On-screen settings menu**: the controller's centre button (Xbox / PS / HOME), Ctrl+O or tray > *On-screen settings menu…*. It
  lists everything you can change (capture, 30 / 60 / 120 mode, frame generation, its type and its *Multiplier*, DLSS 5 with level and
  passes, and the upscaler, including each **DLSS model**: automatic, M, L, K, J, E, saved for all games, plus **XeSS**, **FSR 1** and
  **No upscaling**, saved per game) with the current values marked. Move with
  the D-pad, the stick or the arrow keys; **Apply** sets everything at once with a single reopen of the picture; B or the centre button
  close it without changes. It doesn't take focus away from the picture (frame generation keeps going) and doesn't open over the
  console menu. If the Xbox button also opens the Windows Game Bar, turn that off in Settings > Gaming.
- **Everything also in the tray menu** (right-click the icon), with the current values ticked: frame generation on / off and its
  type, 30 / 60 / 120 mode, capture 720p / 1080p / 1440p / 4K, DLSS 5 (on / off, level, passes), alternative upscaler, dither filter,
  *Show current settings (on screen)* and the settings menu; plus Picture options, start-up and power options, the wizard, components,
  Add / Remove DLSS 5, About and licenses and the Ko-fi link. Useful when there is no controller connected to the PC.
- **Keyboard shortcuts** over the console picture: Ctrl+F, Ctrl+G, Ctrl+M, Ctrl+1…4, Ctrl+R, Ctrl+D, Ctrl+I, Ctrl+O, Alt+F4 (see
  [Controls](#controls)). Shortcuts keep working with Joyxoff and similar tools running.
- **Closing**: tray > Exit, Alt+F4, the window's X or closing it from the taskbar quits the whole app (it isn't taken for a signal
  drop). The Windows key, the Start menu, the taskbar and Alt+Tab work normally while you play.
- **No double sound (echo)**: while it plays the capture card's sound, the app turns off Windows *Listen to this device* on that input,
  closes leftover audio players and warns if another capture app plays it too (see the FAQ).
- **Start with Windows** (a shortcut in your Startup folder, no administrator rights needed) with a black screen until the picture
  shows and, optionally, a black desktop while the app is open (tray > *Black desktop while the app is open*).
- **Low-power mode when the console turns off**: the PC stays on but almost idle (screen off, app closed, a low-power plan) until the
  console comes back, with nothing of Windows on screen (no taskbar, desktop or pointer) from the moment the console turns off until its
  picture is back. How it works and how much it uses is explained in the wizard's last step and in the tray: see
  [Power](#power-what-the-pc-does-when-the-console-turns-off).
- **Console or normal PC** (tray > *How the PC is used*): *Like a console* (opens with Windows, black desktop, low-power mode when the
  console turns off) or *Like a normal PC* (regular Windows: the app only opens when you open it and the PC stays on when the console turns off).
  Each one explains what it does first; switch whenever you like.
- **Uninstall** (tray > *Uninstall ConsoleEnhancer…*): removes the start-with-Windows task, brings back the normal desktop and power plan,
  and deletes only the app's own files (anything else in its folder stays). Your settings and game profiles can be kept for a later install.
- **`ConsoleEnhancer Settings.exe`** next to `ConsoleEnhancer.exe` (replaces `Inicio y apagado.bat`): all of the above plus DLSS 5,
  components, logs, the countdown and standby tests and uninstall, in one window. If the app is open, it offers to restart it to apply
  the changes.
- **Components**: Intel XeSS and AMD FSR are downloaded once from their official pages with their SHA-256 checked (offline, from
  the zips in `downloads\`; if the app folder still has a `magpie\` folder from an older version, the Intel and AMD files are taken
  from there without downloading anything); tray > *Components: check / download again* repairs them.
- **Your settings live on this PC, not in the app folder** (1.3.1): `%LOCALAPPDATA%\ConsoleEnhancer\settings` holds the ini (what you
  pick in the menus), the per-game profiles, the recognised console, the language, the learned scales and the logs (`logs\`, with the
  engine's compiled shaders). It is the same folder for **every** version and is written on every change, so a new version unzipped
  anywhere (Downloads, Desktop, another drive, with no older version next to it) uses your settings as they are from the first start.
  The first time (1.3.1), the app brings them over once from the most recently used data folder (an older version's `data\` next to it
  or the backup in `%LOCALAPPDATA%\ConsoleEnhancer\backup`), noted in `settings\inherited.txt` and said once on screen.
- **Updates keep your choices**: the new version applies its new general settings while keeping what you and the wizard chose (copy in
  `.antes-*`); only what no longer exists is switched to the closest thing (XeSS FG and FSR 3 FG -> Smooth Motion on RTX 40 / 50; DLSS FG,
  the old ×3 / ×4, Magpie and mpv were removed in 1.3), written to the log and said once on screen. An **older** version opened later
  never rewrites the settings of a newer one: it works on a copy (`settings-older\<version>`).
- **Start from scratch**: close the app and delete `%LOCALAPPDATA%\ConsoleEnhancer\settings` (or the whole `ConsoleEnhancer` folder
  there, which also removes the backup and your DLSS 5 file copy); *Uninstall* with "Yes = everything is deleted" does the same.
- **Portable** (optional): create an empty `portable.txt` next to the exe and everything is stored in `data\` there instead (also if the
  PC folder can't be written). The `portable.txt` that zips up to 1.3 shipped is ignored.
- **Interface in English and Spanish**, chosen in the wizard's first step.

## Power: what the PC does when the console turns off

With `apagar_pc=1` (wizard's last step or tray > *Shut down the PC when the console is turned off*), `apagar_pc_modo` picks what
happens. Change it in the wizard, in tray > *When the console is turned off, the PC* (with *How does each one work and how much
power?*) or in `ConsoleEnhancer Settings.exe`.

| Mode | How it works | Power at the wall |
|---|---|---|
| **Low-power mode, like a console in rest mode** (`espera`, default) | See below. | About 30-60 W depending on the PC (a GPU measured 12.5 W idle in P8) |
| **Sleep** (`suspender`) | Windows sleep (S3); ConsoleEnhancer opens when the PC wakes. Asleep, the capture card has no power: turning the console on does **not** wake it, and controllers usually can't either; wake it with the keyboard or the power button. | 2-5 W |
| **Shut down** (`apagar`) | The PC turns off completely; neither the console nor a controller can turn it on: use its power button (with *Start with Windows*, the app opens). | 0.5-2 W |

### The low-power mode

It is **not** sleep and **not** shutdown: the PC stays on, so nothing has to "turn it on" again. When the console turns off:

1. No countdown: a black cover with no pointer goes over every screen at once (the taskbar stays hidden), the screen goes off,
   ConsoleEnhancer closes (the engine with it) and Windows switches to a temporary low-power plan (*Power saver*
   with the processor capped at 50 % and no boost, `espera_cpu_max`; deleted on waking), so the CPU and GPU drop to their lowest
   clocks (the GPU idles in P8; an RTX 4060 Ti measured 8.5-9 W with the screen off).
2. Only a tiny watcher stays, in its own small process (about 0.1 % of one core, low priority, EcoQoS, without raising Windows' timer
   resolution): it looks at a small 720p picture from the capture card and also listens for the controllers and the keyboard. The
   Windows key doesn't open Start, and if another program turns the screen on with nobody touching anything, it is turned off again.
3. When the console turns back on (the capture card gets a picture), a controller is turned on or a key is pressed, the screen comes
   back black (the taskbar stays hidden until the console picture is back), the normal power plan returns and ConsoleEnhancer opens in
   2-3 s. The taskbar always comes back when you close the app, switch to *Like a normal PC* or uninstall, even if the app is closed
   by force (`espera_ocultar_barra=0` leaves it alone).
4. If it was woken by a controller or a key and the console isn't turned on within 90 s, it goes back to low power
   (`espera_volver_sin_consola_s`; 0 = stays awake).

It uses more than sleep or shutdown (the PC is still on: about 30-60 W at the wall depending on the PC, roughly 5-6 EUR a month if
left like that all day), but it's the only mode where turning the console on is enough to get back to playing.

- With sleep and shut down, a countdown shows first (`apagar_pc_aviso_s=15`; 0 = none). Only a real controller button, key or click,
  or moving the mouse more than 40 px, cancels it; input injected by other programs (Joyxoff, controller mappers, Steam Input) doesn't.
- The PC is not shut down if the GPU failed in the last minute (see *Doesn't close by itself if the GPU stalls*).
- Closing the app yourself (Exit, Alt+F4) never shuts down or sleeps the PC.
- `ConsoleEnhancer Settings.exe` > *Test the shutdown notice* / *Test standby* (the low-power mode: the screen goes off; turn the
  console or a controller on, or press a key).

## DLSS 5 (optional)

"DLSS 5" is the community name for an NVIDIA neural filter (DLSSNR) that redraws the console picture with more
detail before upscaling. **It is not an official NVIDIA feature and does not ship with ConsoleEnhancer**: it needs `nvngx_dlssnr.dll`, a
community-modified NVIDIA DLL with no signature or license that cannot be redistributed. So the app **does not include it, download it
or say where to get it**. Everything else (DLSS, frame generation, HDR, VRR...) works the same without it.

If you already have your own file, adding it takes one click:

1. Tray icon > **Add DLSS 5 (your own file)…** (or the **Add DLSS 5** button on the wizard's last step; only with a GeForce RTX and
   6 GB of VRAM or more).
2. Read the notice (it is unofficial, NVIDIA doesn't support it and it is at your own risk). **Add DLSS 5 now finds these files if they
   are already on your PC**: it looks for a few seconds, only on your PC (Downloads, Desktop, other ConsoleEnhancer folders, Magpie
   folders, program and game folders; cancelable), lists what it finds with folder, size, version and whether NVIDIA signed it, puts
   first the one that suits your GPU, and you pick (or *Pick another file…*). Nothing is downloaded. If DLSS 5 is missing when the app
   starts, it looks once and asks first.
3. The app checks that it is a 64-bit Windows DLL with that name, tells you whether it is NVIDIA's original (valid NVIDIA signature) or
   the unsigned community-modified one, and copies it to `extras\` next to the exe.
4. Choose whether to turn it on now. While playing, the controller's **A** button turns it on and off (X / Y / B: passes; L1 / L2 / R2 /
   R1: level).

To remove it: tray icon > **Remove DLSS 5** (deletes the copy in `extras\`; your original file is not touched).

Your DLSS 5 files are also kept in `%LOCALAPPDATA%\ConsoleEnhancer\extras`, so a new version unzipped into a new folder picks them up
by itself. With your file, DLSS 5 always runs inside the app's own engine (`engine\PrismaNr.dll`, which ships with the app since
1.3; 1 to 3 passes, with any frame generation type): much lower latency than through Magpie in 1.2.3 (measured at 4K, 30 fps mode +
frame generation ×2: 43 ms instead of ~72 ms for the real frame; 1080p → 4K with 2 passes: 37 instead of ~52) and a steadier pace.
There is nothing to choose. Without `engine\PrismaNr.dll` the engine runs without DLSS 5. With DLSS 5 on, Enhanced FG ×3 / ×4
lower DLSS 5's scale until they keep the real frames when the GPU is full (then ×3 / ×2 for the session if they can't); FSR 3 FG, if brought back with `generacion_fsr3=1`, offers ×3 / ×4 too (`motor_fsr_multi_dlss5=0` =
only ×2 with DLSS 5).

## DLSS 5 and the law

Short and neutral (this is what the licences say; it is not legal advice):

- **What ConsoleEnhancer ships**: its own code (GPL-3.0), NVIDIA's official Streamline / DLSS SR / Reflex / RTX Dynamic Vibrance
  DLLs, unmodified, which NVIDIA allows apps to redistribute, and `engine\CeFsrFg.dll` (AMD's FidelityFX SDK 1.1.4 frame
  interpolation, MIT-licensed, which allows changes; built and modified by ConsoleEnhancer for FSR 3 FG ×3 / ×4, with AMD's notice). It
  downloads Intel's and AMD's XeSS / FSR files from their official pages.
- **Since 1.3 the repository and the zip also carry `PrismaNr`**, the engine's own native piece (own code). It loads the file you
  add yourself (`nvngx_dlssnr.dll`) without going through NVIDIA's check (NGX), which NVIDIA's terms do not allow. The user decided to
  publish it knowing that. Without your file it does nothing. It is compiled with NVIDIA's public NGX headers, which are only used to
  build it and are not in the repository.
- **What it never ships, downloads or links to**: `nvngx_dlssnr.dll` ("DLSS 5") or any modified NVIDIA file. *Add DLSS 5* only looks
  on your own PC. Each person adds their own file, at their own risk.
- **Legal DLSS 5 does not exist today outside NVIDIA's own games**: NVIDIA has released DLSS 5 only inside the games that ship it, only
  on RTX 50, with no SDK or Streamline plugin for other apps (checked on 22-11-2026: the public NGX route answers "please update display
  driver" with the current driver, and a newer one would only accept NVIDIA's signed original, which only runs on RTX 50). The DLL that
  circulates for RTX 20 / 30 / 40 is a modified NVIDIA binary.
- What every RTX gets officially in the engine: DLSS Super Resolution, Reflex, the driver's Smooth Motion (RTX 40 / 50) and,
  optionally, RTX Dynamic Vibrance (it is not DLSS 5: it adds colour and clarity, it doesn't relight).
- **No NVIDIA DLSS Frame Generation and no `dlssg_sm86`** since 1.3: the app neither uses nor looks for that community package.
  The ×3 / ×4 it offers are legal: FSR 3 FG ×3 / ×4 from AMD's MIT code (only with `generacion_fsr3=1`) and Enhanced FG, which is the
  app's own code. Smooth Motion ×3 / ×4 on its own and XeSS FG
  ×3 / ×4 outside Intel GPUs would need patching NVIDIA's driver or Intel's library (Intel's licence forbids it), so they are not offered.

## FAQ / common problems

**Windows or the antivirus won't let me open it / Microsoft Defender removed ConsoleEnhancer.exe.** The exes are not code-signed yet,
so every new version starts with no reputation. SmartScreen ("Windows protected your PC"): *More info > Run anyway*. Microsoft
Defender: on 21-11-2026 it quarantined versions 1.2.07 and 1.2.09 a few seconds after they were opened, when they added themselves to
the Windows startup (*Behavior:Win32/Persistence.A!ml*, *Trojan:Win32/Bearfoos.A!ml*). It is a false positive: the same files scan
clean. Since **1.2.3** the app never adds itself to the startup on its own (a new copy asks first, and only when you open it
yourself), it runs no PowerShell or `schtasks.exe`, and the exes carry their version and publisher. If Defender still removes it:
Windows Security > Virus & threat protection > **Protection history** > the ConsoleEnhancer entry > **Restore**; if it happens
again, add **only the app folder** as an exclusion (*Manage settings > Exclusions > Add an exclusion > Folder*; the tray item
**Microsoft Defender: exclusion for the app...** opens that page with the folder copied), and please report the false positive at
https://www.microsoft.com/wdsi/filesubmission ("Incorrectly detected as malware/malicious"). Each release lists the SHA-256 of the
zip and of every file in it (`.files.sha256`), and you can build it yourself (see *For developers*).

**The picture stays but there is no sound and the controller does nothing.** Microsoft Defender closed the app while the engine
kept showing the picture: see the previous answer (the app also explains it the next time it opens).

**The XeSS / FSR download fails.** Internet is needed once. Behind a proxy or offline: download the zips linked in that window
(also in `licenses\VERSIONS.txt`) on another PC and put them in a `downloads\` folder next to `ConsoleEnhancer.exe`; they are checked
the same way. If it says it can't write, the folder is read-only (Program Files): move it to one of your own.

**Black screen or "No Signal".** Check that the picture shows in the wizard's *Test picture*. Close any program using the capture card
(OBS, the vendor's software, Discord with the camera...). If the console encrypts its output (**HDCP**), the picture is black: turn it
off in the console (PS5: Settings > System > HDMI > *Enable HDCP*). ConsoleEnhancer does not remove copy protection. The HDCP warning
only shows with a flat black picture across the whole frame (since 1.3.3 a still HUD, logo or text on black doesn't count) and a
silent console for 20 s in a row (`hdcp_aviso_s`; `hdcp_aviso=0` turns it off): a dark
scene or a fade no longer triggers it.

**No sound.** Since 1.3 the console sound goes through the engine (`ConsoleEnhancerEngine.exe --audio`). Look at
`settings\logs\audio.log`: when it works it says which capture device, pin and format it opened (for example *48000 Hz 16 bits 8
channels, 20 ms blocks*); when it can't open it, it lists the pins and formats the capture card offers. In the wizard's sound step, try
another input with *Test sound* (some capture cards provide audio through the video device itself, others as a separate microphone),
or pass the engine other values with `audio_motor_extra=` (e.g. `audio_canales=2`).

**I hear the sound twice (echo).** Something else is playing the capture card at the same time as the app. Since 1.0.6 the app turns off
Windows *Listen to this device* on the capture card input while it plays through it (and restores it on exit), closes a leftover audio
player of its own from a previous session and shows an on-screen warning if RECentral, OBS (with monitoring), Streamlabs or XSplit are
also playing it. What it can't see: the capture card's HDMI pass-through output connected to a TV or monitor with speakers; turn its
volume down or use another output. `sonido_doble=0` in the ini leaves things as before.

**I don't get DLSS or frame generation.** DLSS only exists on NVIDIA GeForce RTX; Smooth Motion on RTX 40/50; frame generation needs a
100 Hz or faster display. Other GPUs get XeSS or FSR 4 / 3.1 and Enhanced FG (any GPU with Direct3D 12; XeSS FG is off for now, see
above). XeSS SR needs the Intel files the app downloads (tray > *Components*). Update the driver.

**Which frame generation should I use?** On RTX 40 / 50, Smooth Motion ×2 (the default). For ×3 / ×4 in the 30 fps mode (or ×3 in the
40 fps mode), with a 120 Hz VRR screen: Enhanced FG (quality mode looks best when the camera turns a lot). Enhanced FG is the app's own one, experimental: its *low latency* mode shows the real frame as soon as
it is ready (less delay, more errors at edges and when the motion changes direction).

**It stutters or lags.** Run the wizard again (with the display and the capture card at their proper resolution), close programs that
draw in the background and, if the TV has one, turn on game mode. The log says which chain was chosen.

**"Recovering the picture" shows up.** The picture froze with the console on (almost always the GPU: driver, heavy load) and the app
reopens it by itself; you don't need to do anything. If it happens often, update the driver and remove the GPU *overclock*. The log says
why ("NO es la consola apagada: ...").

**Black screens of a few seconds, and `DXGI_ERROR_DEVICE_HUNG` / "grafica perdida" in `engine.log`.** That is the graphics card
resetting under load (Windows logs it as `nvlddmkm` event 153 in *Event Viewer > System*). A **video memory overclock** (for example a
memory clock offset in an overclocking tool) can do it with DLSS 5 plus frame generation even when games look fine: set the memory clock back
to stock and test again. Since 1.3 the app keeps the last picture on screen while it recovers, and if it happens with XeSS FG or
Enhanced FG, the rest of the session uses another frame generation type in the engine (with FSR 3 FG ×3 / ×4, ×2).

**My desktop is stuck black.** `ConsoleEnhancer.exe --restaurar-escritorio`.

**Where are the settings and the log?** In `%LOCALAPPDATA%\ConsoleEnhancer\settings` (the same for every version; `data\` next to
the exe only with your own `portable.txt`): `consoleenhancer.ini`, `games\` and `logs\consoleenhancer.log` / `engine.log`. Settings
panel > *Open my settings and profiles* opens it. To start from scratch, close the app and delete that folder. If you open an *issue*, attach the log (check first that it doesn't
contain anything of yours you don't want to show, such as folder names).

**What about "DLSS 5"?** It is a community-modified NVIDIA DLL: **it is not included, not downloaded and not recommended**. The app
works fully without it; if you already have it, see [DLSS 5 (optional)](#dlss-5-optional).

**Is XeSS or FSR broken?** Tray icon > **Components: check / download again**: it says what is missing and downloads them again from
their official pages.

**"Shut down the PC when the console is turned off" closed my stuff.** In *Shut down* mode it really shuts down Windows after the
countdown: save your work, switch to the low-power mode (*Standby*, tray > *When the console is turned off, the PC*), or turn it off in the tray menu.
Closing the app by hand never shuts anything down.

**The countdown is cancelled by itself.** Since 1.0.13 only real input cancels it; if it still happens, the log notes how many
injected inputs it ignored.

**Turning the console on doesn't bring the PC back.** That only works in the low-power mode (*Standby*), because there the PC never
turns off. After *Sleep* or *Shut down* the console and the controllers can't turn the PC on: use a key or the power button. Test the
low-power mode in `ConsoleEnhancer Settings.exe` > *Test standby*.

## Legal notices

- **License**: ConsoleEnhancer is free software under the **GPL-3.0** (`LICENSE`), **without any warranty**, with a section 7 additional
  permission for use with the official NVIDIA, AMD, Intel and Microsoft libraries (`LICENSE-EXCEPTION.txt`). Third-party code, versions
  and licenses: [`THIRD-PARTY.md`](THIRD-PARTY.md) and `licenses\` in the zip.
- **What is in the zip**: the app (with the engine's own `PrismaNr.dll`, see [DLSS 5 and the law](#dlss-5-and-the-law)),
  `engine\CeFsrFg.dll` (AMD FidelityFX SDK 1.1.4 frame interpolation, MIT, modified by ConsoleEnhancer; AMD's notice in
  `licenses\AMD-FidelityFX-SDK-1.1.4-LICENSE.txt`) and the NVIDIA Streamline / DLSS / Reflex / RTX Dynamic Vibrance DLLs, unmodified and
  signed by NVIDIA, which NVIDIA allows to be redistributed inside an application; their terms (no selling them on their own, no
  reverse engineering...) are in `licenses\NVIDIA-NOTICE.txt` and also apply to you.
- **Intel XeSS and AMD FSR are not redistributed by ConsoleEnhancer**: the app downloads them from their official releases, with
  their licenses (XeSS: Intel Simplified Software License; FSR: MIT). Since 1.3 the app neither uses nor downloads Magpie or mpv.
- **"DLSS 5" is not included.** `nvngx_dlssnr.dll` is a community-modified NVIDIA DLL with no signature or license, and
  it is not in the repository or the zip, the app doesn't download it, and using it may go against NVIDIA's terms. If you already have
  it, you can add it yourself (tray > *Add DLSS 5*, which copies it to `extras\`) at your own risk: the app warns that it is not
  official. See [DLSS 5 and the law](#dlss-5-and-the-law).
- **Author**: ConsoleEnhancer is created and maintained by **IanRog29** (with contributions from its contributors). Copyright (C) 2026
  IanRog29 and ConsoleEnhancer contributors.
- **Donations**: voluntary (Ko-fi); they don't give access to anything extra and don't buy third-party DLLs.
- **Trademarks**: NVIDIA, GeForce RTX, DLSS, Reflex, AMD, FSR, FidelityFX, Intel, XeSS, Nintendo, PlayStation, Xbox, Windows and others
  belong to their owners; they are named only to describe compatibility. ConsoleEnhancer is not affiliated with, sponsored or endorsed by
  any of them. Uses NVIDIA DLSS, NVIDIA Reflex and NVIDIA Streamline.
- **Privacy**: the app doesn't send anything anywhere. It only connects to the internet to download XeSS and FSR from GitHub the
  first time.
- **Content**: it shows your console's picture on your screen; it doesn't record, doesn't stream and doesn't remove HDCP.

## For developers

**One click:** with the code downloaded (`git clone` or the "Code" zip), double-click **`preparar.bat`**. It downloads the dependencies
to `terceros\` (pinned versions, with their SHA-256), builds the launcher and the engine, runs the tests and leaves
`bin\ConsoleEnhancer.exe` ready to open. `preparar.bat paquete` also builds the release zip in `dist\`. Only Windows 10/11 is needed:
it is built with the `csc.exe` of .NET Framework 4.8 that ships with Windows (C# 5), no Visual Studio.

Step by step:

```bat
powershell -ExecutionPolicy Bypass -File scripts\descargar_dependencias.ps1   :: terceros\xess, terceros\fsr, terceros\streamline
build.bat                       :: bin\ConsoleEnhancer.exe (src\Lanzador + src\Deteccion) y bin\ listo (xess y fsr enlazados a terceros\)
build_motor.bat                 :: bin\engine\ConsoleEnhancerEngine.exe (src\Motor) + DLL de Streamline
powershell -ExecutionPolicy Bypass -File scripts\descargar_cabeceras_ngx.ps1   :: terceros\dlss_sdk (2 cabeceras de NGX / NGX headers)
build_prismanr.bat              :: bin\engine\PrismaNr.dll (C++, Visual Studio)
build_fsrfg.bat                 :: bin\engine\CeFsrFg.dll (C++, Visual Studio, git; AMD FidelityFX SDK v1.1.4 + parches\fidelityfx-fi-multi.patch)
tests\build_deteccion.bat       :: tests\bin\DeteccionTest.exe
tests\bin\DeteccionTest.exe --simula=todo --sin-d3d12 --salida=deteccion.txt   :: 23 PCs simulados
powershell -ExecutionPolicy Bypass -File scripts\empaquetar.ps1 -Zip -Version v1.3.3   :: dist\ConsoleEnhancer\ y dist\ConsoleEnhancer-v1.3.3.zip
```

- **The engine's own native pieces** (C++; Visual Studio 2022 or its Build Tools with C++). `src\PrismaNr` needs NVIDIA's two public
  NGX headers (`scripts\descargar_cabeceras_ngx.ps1`, pinned version and SHA-256; not in git). `src\CeFsrFg` (FSR 3 FG ×3 / ×4):
  `build_fsrfg.bat` downloads only what it needs of AMD's FidelityFX SDK at tag `v1.1.4` (checking it is commit `c6efa6b`) into `obj\`,
  applies `parches\fidelityfx-fi-multi.patch`, compiles its shaders with the SDK's own shader compiler and links the DLL; the SDK is not in
  git. The app runs without them (no DLSS 5, FSR 3 FG only ×2); the package requires both (`preparar.bat` builds them when Visual
  Studio is there).
- **Dependencies**: the table `scripts\components.json` (version, URL, SHA-256 and what is taken from each zip) is used by
  `descargar_dependencias.ps1` and by the app itself. To change a version, change it there.
- **Package** (`scripts\empaquetar.ps1`): allow-list. It includes the app, the engine with Streamline / DLSS, scripts, languages,
  resources and licenses; it does **not** include XeSS or FSR (the app downloads them) or anything non-redistributable, and it checks
  that there is no data from the PC it is built on (your user name, the computer name and whatever you put in `scripts\privado.txt`,
  which is not in git).
- **GitHub Actions** (`.github/workflows/compilar.yml`): builds the launcher, the engine, `PrismaNr.dll` and `CeFsrFg.dll` and runs the
  tests on every push, and rejects any binary or non-redistributable file in the repository (`nvngx_dlssnr`, `dlssg_sm86`...); with a
  `v*` tag it builds the zip and creates the Release with it and its `.sha256`.
- **Testing without a console**:

| Argument | What it does |
|---|---|
| `--datos=<carpeta>` | A different data folder (ini, log, games): doesn't touch the real ones |
| `--asistente-auto` | Writes the ini with the detection plan (estimated scales, picture, controller), without asking |
| `--simula=<caso>` | With `--asistente-auto`: another PC's plan (`amd-rx7800xt`, `intel-arc-b580`, `rtx3060`, `sin-nvidia`...) |
| `--dry-run` | Writes the chain and the command lines to the log, without launching anything |
| `--componentes=<carpeta>` | Downloads and installs XeSS and FSR in that folder without asking (or from the zips in `<carpeta>\downloads`) and reports in `componentes.log` |
| `--componentes-reparar` | Downloads XeSS and FSR again even if they are fine (what tray > *Components: check / download again* does) |
| `--ui-preview=asistente;<carpeta>` | Images of the wizard's 9 steps (es / en), without opening anything |
| `--ui-preview=apagado;<carpeta>` | Images of the shutdown notice |
| `--prueba-apagado[=<s>[;fuera]]` | The real countdown; when it ends it only writes "SIMULADO" to the log |
| `--prueba-espera` | Goes into the low-power mode now (screen off, the watcher running); the console, a controller or a key brings it back |
| `--prueba-suspender` | Sleeps the PC now and opens the app when it wakes |
| ini: `test_source=barras`, `apagar_pc=0` and in `motor_extra` `power_test=1 prueba_cuelgue_ms=4000` or `prueba_apagado_s=35 prueba_apagado_modo=corta` (or `nosignal`) | The engine, with the test picture, simulates the GPU stalling for 4 s (the app must not close) or the console turning off (it must) |
| `--iniciar-con-windows=1` / `=0` | Adds or removes start with Windows and exits |
| `--test-consolas=<informe>` | Console profiles and their detection with a fake clock |
| `--test-tipos-generacion`, `--test-solo-motor`, `--test-motor-dlss5`, `--test-smooth-x4`, `--test-avisos`, `--test-menu-ajustes`, `--test-perfiles`, `--test-juegos-consola`, `--test-ps5`, `--test-deteccion`, `--test-nombres`, `--test-entrada`, `--test-home`, `--test-extras-guardados`, `--test-buscar-extras`, `--test-juego-en-marcha`, `--test-modo40`, `--test-negro-liso`, `--test-steam` (fake Steam folder only) (`=<informe>`, with `--datos=<carpeta>`) | Tests without the console or the GPU: frame generation types and old-profile migration, what goes to the engine, DLSS 5, Smooth Motion ×4 gone (a saved one becomes ×2), the 40 fps mode, the on-screen notices against what really runs, the settings menu, profiles, console menus and game detection with saved real captures; exit code = failures |
| ini `test_source=<file>` and `test_motor=archivo:<file>` (`.y4m`, or `.mp4` / `.mkv` / `.webm` through Media Foundation) | The engine plays a video file as if it were the capture card (same 59.94 pace, YUY2 at the capture size), to test the whole app without a console; in `motor_extra`, `test_archivo_paso=N` (one of every N file frames reaches the game: with 60 fps footage and N=2 the game runs at 30 and the frame in between is the truth of the generated one), `test_archivo_desde` / `_hasta` (seconds, looped), `_quieto`, `_codigo`, `_filtro`, `_hw`, `_registro` |
| `--restaurar-escritorio` / `--escritorio-negro` | The normal or the black desktop, by hand |

**Layout**

```
src\Lanzador\   the tray app (C# 5, WinForms): the engine (picture and sound), controllers, curtain, wizard, components, start-up and shutdown,
                user extras (Add / Remove DLSS 5) and the support link (Extras.cs)
src\Deteccion\  detection of GPU, displays, capture cards, audio and controllers; the picture plan; simulated PCs
src\Motor\      the D3D12 image engine (capture, DLSS / XeSS / FSR, frame generation, Enhanced FG, Reflex, OSD) and its sound-only mode (Audio\)
src\PrismaNr\   the engine's native piece for "DLSS 5" (C++)
src\CeFsrFg\    FSR 3 FG ×3 / ×4: the C wrapper around AMD's FidelityFX SDK 1.1.4 frame interpolation (C++; the SDK is downloaded to build)
scripts\        components.json, descargar_dependencias.ps1, empaquetar.ps1
resources\       base.ini (starting settings), icon
languages\        en.txt (English on-screen texts)
parches\        source code patches for third-party code (parches\LEEME.txt; fidelityfx-fi-multi.patch for CeFsrFg)
tests\          DeteccionTest
DISENO.md       design and decisions; LEEME.txt: status, what has been tested and what is missing
```

Read [`CONTRIBUTING.md`](CONTRIBUTING.md) before sending changes (C# 5, no binaries or personal data in git). Security issues:
[`SECURITY.md`](SECURITY.md). Changes per version: [`CHANGELOG.md`](CHANGELOG.md).

---

# Español

[English](#english) · Español

**Tu consola en la tele, mejor que nunca, sin tocar el teclado.** ConsoleEnhancer convierte el PC del salón en la "caja" que va entre
tu consola y tu tele: conectas la consola a una capturadora, el PC a la tele, y listo. Enciendes la consola y la app se abre sola; la
imagen llega **reescalada a 4K** (DLSS, FSR, XeSS), con **generación de fotogramas** si tu gráfica puede (Smooth Motion ×2
y la Enhanced FG propia de la app hasta ×4, experimental), **HDR** y **VRR**, y con el **menor retraso posible**, todo en el motor de
imagen propio de la app (sin otro reproductor ni otra ventana de escalado por medio). Todo se maneja **con el mando** desde el
sofá, sin ratón ni teclado; y al apagar la consola, la app se cierra sola y, si quieres, el PC pasa a un **modo de bajo consumo, como
una consola en reposo**: no se apaga ni se suspende, sigue encendido gastando lo mínimo, y al volver a encender la consola vuelve la
imagen sola.

- **Se configura sola**: un asistente detecta la gráfica, la tele, la capturadora, el sonido y el mando, prueba la imagen y el sonido y
  elige la mejor cadena para tu PC. Las escalas **se adaptan a tu gráfica** mientras juegas (y lo recuerda).
- **Cualquier consola**: Nintendo Switch / Switch 2, PlayStation, Xbox... Reconoce el menú de la consola y lo enseña a 4K nativo.
- **Pensada para el salón**: se abre al iniciar Windows con la pantalla en negro hasta que hay imagen, sin ventanas ni avisos por medio;
  cambias de modo (30 / 60 fps, generación sí / no, resolución de captura) con los botones del mando o el menú de ajustes en pantalla.
- **Gratis, libre (GPL-3.0) y sin anuncios ni telemetría.** La interfaz está en español e inglés; los comentarios del código y los logs,
  en español.

Creado y mantenido por **IanRog29** · [Código y descargas](https://github.com/IanRog29/ConsoleEnhancer)

---

## Inicio rápido

1. Baja el último **`ConsoleEnhancer-v1.3.3.zip`** (o uno más nuevo) de
   **[Releases](https://github.com/IanRog29/ConsoleEnhancer/releases)** (no el botón verde "Code": eso es el código fuente).
2. **Descomprímelo** en una carpeta tuya: Documentos, el Escritorio, `D:\Juegos`... (no en *Archivos de programa*).
3. Abre **`ConsoleEnhancer.exe`** y sigue el asistente. Ya está: enciende la consola.

Detalles de la primera vez:

- Si Windows dice *"Windows protegió su PC"*: **Más información > Ejecutar de todas formas** (el exe no está firmado con un certificado
  de pago; el código está aquí para quien quiera revisarlo).
- La primera vez baja **Intel XeSS** y **AMD FSR** (componentes libres que usa su motor para el reescalado y la generación XeSS /
  FSR) de sus páginas oficiales: unos 205 MB, una sola vez, comprobando su huella SHA-256. Pulsa
  **Descargar**. Si algo se estropea más tarde: icono de la bandeja > **Componentes: comprobar / volver a descargar**.
- El **asistente** pregunta el idioma y enseña lo detectado, la capturadora con **Probar imagen**, el sonido con **Probar sonido**, la
  consola, la pantalla y el plan. Al final eliges si se abre al iniciar Windows y qué hace el PC al apagar la consola (nada, bajo
  consumo, suspender o apagar; ver [Energía](#energía-qué-hace-el-pc-al-apagar-la-consola)).

**Ajustes y desinstalar: abre `ConsoleEnhancer Settings.exe`** (junto a `ConsoleEnhancer.exe`), con la app abierta o cerrada. Tiene
todo en una ventana: abrir la app o el asistente, cómo se usa el PC (como una consola / como un PC normal), abrirse con Windows,
escritorio negro, qué hace el PC al apagar la consola, DLSS 5 (tu propio archivo), volver a bajar los componentes, probar el aviso o la
espera, los registros y **Desinstalar** (deja Windows como estaba y borra solo los archivos de la app; tú eliges si guardar tus ajustes y
perfiles). Lo mismo está en el menú del icono de la bandeja.

## Controles

Todo funciona **con el mando que ya usas en la consola** (la app lo lee sin quitárselo: la consola sigue recibiendo cada pulsación;
p. ej. Options también abre el menú del juego en la PS5) o con un teclado. Los botones solo actúan **con la imagen de la consola
delante**. Van por posición: el mismo sitio hace lo mismo en cualquier mando.

### Mando y teclado

| Qué hace | Xbox / XInput<br>(GameSir, 8BitDo modo X) | PlayStation 5 / 4<br>(DualSense, DualShock 4) | Nintendo Switch / Switch 2<br>(Pro, Joy-Con, 8BitDo) | Teclado |
|---|:---:|:---:|:---:|:---:|
| **Menú de ajustes** en pantalla (abrir / cerrar) | botón Xbox | PS | HOME | `Ctrl`+`O` |
| Modo **30 → 60 → 120 fps** | Menú ≡ | Options | + | `Ctrl`+`M` |
| **Generación de fotogramas** sí / no | Vista ⧉ | Crear (PS5) / Share (PS4) · panel táctil | − · Captura | `Ctrl`+`F` |
| **Tipo** de generación (Smooth Motion, Enhanced FG) | L3 | L3 | L3 | `Ctrl`+`G` |
| Filtro de tramado (nuevo / viejo) | mantener L3 1 s | mantener L3 1 s | mantener L3 1 s | |
| **Captura** a 720p / 1080p / 1440p / 4K | cruceta ← ↑ → ↓ | cruceta ← ↑ → ↓ | cruceta ← ↑ → ↓ | `Ctrl`+`1` … `Ctrl`+`4` |
| **Escalador** principal / alternativo (el alternativo es FSR 1) | R3 | R3 | R3 | `Ctrl`+`R` |
| **DLSS 5** sí / no ¹ | A | ✕ | B | `Ctrl`+`D` |
| **Pasadas** de DLSS 5 1 / 2 / 3 ¹ | X / Y / B | □ / △ / ○ | Y / X / A | |
| **Nivel** de DLSS 5 1 / 2 / 3 / 4 ¹ | LB / LT / RT / RB | L1 / L2 / R2 / R1 | L / ZL / ZR / R | |
| Ver los ajustes de ahora | | | | `Ctrl`+`I` |
| Salir de la app | | | | `Alt`+`F4` |

¹ Solo con el archivo opcional "DLSS 5" (ver [DLSS 5](#dlss-5-opcional)).

El modo 120 fps solo aparece donde tu capturadora y tu pantalla lo admiten; si no, el botón del modo alterna 30 ↔ 60. Mandos genéricos:
Start = Menú, Select = Vista, lo demás por posición. La distribución de antes (Vista = 30 fps, Menú = 60 fps): `mando_botones=prisma`.

### Dentro del menú de ajustes

| | Mando | Teclado |
|---|:---:|:---:|
| Cambiar de fila | cruceta / stick izquierdo ↑ ↓ | ↑ ↓ |
| Cambiar el valor | cruceta / stick izquierdo ← → | ← → |
| Siguiente valor · pulsar el botón de abajo (**Aplicar**) | A / ✕ / B en Nintendo | `Intro` / `Espacio` |
| Cerrar sin cambiar nada | B / ○ / A en Nintendo · el botón central | `Esc` / `Retroceso` |

Nada cambia hasta **Aplicar**: todo lo que cambiaste (marcado con un punto cian) se aplica a la vez y se guarda para ese juego. El menú
se abre también mientras la imagen se está preparando; lo que no se puede usar ahora sale en gris con el motivo debajo.

### Modo PC (el mando fuera de la imagen de la consola)

Con `mando_pc=1` (de serie) el mismo mando maneja también Windows cuando la imagen de la consola no está delante.

| Qué hace | Xbox / XInput | PlayStation | Nintendo |
|---|:---:|:---:|:---:|
| Puntero del ratón | stick izquierdo | stick izquierdo | stick izquierdo |
| Rueda del ratón | stick derecho | stick derecho | stick derecho |
| Clic izquierdo | A | ✕ | B |
| Clic derecho | X | □ | Y |
| Clic central | R3 | R3 | R3 |
| Esc | B | ○ | A |
| Flechas | cruceta | cruceta | cruceta |
| Alt+F4 | Vista + Menú | Crear + Options | − y + |
| Teclado en pantalla | L3 | L3 | L3 |
| Modo PC sí / no | mantener el botón Xbox (o LB+RB) 1 s | mantener PS (o L1+R1) 1 s | mantener HOME (o L+R) 1 s |

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
| Gráfica | Cualquiera con Direct3D 12 (NVIDIA, AMD o Intel) | NVIDIA GeForce RTX (DLSS; RTX 40/50: Smooth Motion) |
| Capturadora | Cualquiera DirectShow (USB/UVC o PCIe) con la consola conectada | Una que capture a 1440p o 4K y con poco retraso |
| Pantalla | Cualquiera | Tele o monitor 4K de 120 Hz (la generación de fotogramas necesita 100 Hz o más) |
| Disco | Unos 500 MB | |

## Qué hace

- **Asistente del primer arranque** (y otra vez desde la bandeja), 9 pasos: idioma, lo detectado, la capturadora con *Probar imagen*,
  el sonido con *Probar sonido*, la consola, la pantalla, la **imagen** (HDR y VRR), el **mando** (tabla de botones del tuyo) y el plan.
- **Cadena según la gráfica** (`src\Deteccion\Plan.cs`): RTX 40 / 50, DLSS SR + Reflex + **Smooth Motion** ×2 (Enhanced FG con la tele
  a 119,88 Hz o un driver sin Smooth Motion; Enhanced FG en el selector); RTX 20/30, DLSS SR + Reflex (Enhanced FG
  opcional); sin DLSS, **XeSS** en GTX e Intel Arc y **FSR 4** en AMD RX 6000 / 7000 / 9000 (oficial en las RX 9000; en RX
  6000 / 7000 con el desbloqueo de FSR 4.1.1, experimental; si no, FSR 3.1), y la generación que tenga cada una (AFMF del driver, FSR 3
  FG, XeSS FG). Enhanced FG nunca se pone de serie: es experimental. **FSR 3 FG ya no se ofrece** (1.3): medida
  con vídeo real de juego era la peor de todas, con inventadas que casi no se mueven; `generacion_fsr3=1` en el ini la vuelve a poner
  como «FSR 3 FG (experimental)». **XeSS FG tampoco se ofrece, de momento** (1.3.1): ni en el menú de ajustes, ni en L3, ni en las
  listas, ni como salida automática; los perfiles de juegos y lo elegido con ella pasan a lo más parecido de este PC (Smooth Motion en
  RTX 40 / 50, el mismo ×2) y la app lo dice una vez. Smooth Motion ×4, que usaba por dentro XeSS FG ×2 del motor, también se ha
  quitado (1.3.2). `generacion_xess=1` en el ini vuelve a poner XeSS FG.
- **Todo en el motor de imagen propio** (D3D12, `src\Motor`; no hay nada que elegir): captura DirectShow; escalado con DLSS, XeSS,
  FSR 3.1 / FSR 4 (con las DLL oficiales de Intel y AMD, que se bajan la primera vez) o FSR 1 (EASU + RCAS); "DLSS 5" con tu propio
  archivo (1 a 3 pasadas, con cualquier tipo de generación); generación de fotogramas con **Smooth Motion** (driver de NVIDIA, RTX 40 /
  50) y **Enhanced FG** (experimental; XeSS FG solo con `generacion_xess=1`, FSR 3 FG solo con `generacion_fsr3=1`); Reflex / XeLL; y, si quieres,
  la Vibrancia RTX de NVIDIA (`motor_extra=vibrancia=1`). El color de las capturadoras YUY2 / NV12 se reconstruye bien, sin bordes rojos
  en escalones (`croma=2` en `motor_extra`; `croma=0` = como antes). Desde la 1.3 la app ya no usa ni baja Magpie ni mpv, y DLSS
  Frame Generation de NVIDIA ya no está.
- **Generación ×3 / ×4, de forma legal** (modo 30, menú de ajustes > *Multiplicador*):
  - **FSR 3 FG ×3 / ×4** en cualquier gráfica (experimental, solo con `generacion_fsr3=1`: con vídeo real de juego sus inventadas casi
    no se mueven y FSR 3 FG salió del menú): ×2 es la FSR 3 FG de AMD de siempre; ×3 / ×4 usan una compilación propia de la
    interpolación de FSR 3.1 de AMD (código MIT, `engine\CeFsrFg.dll`) cambiada para inventar en 1/3, 2/3 o 1/4, 2/4, 3/4 entre dos
    reales con un solo flujo óptico. Medido en una RTX 4060 Ti con una tele 4K 120 Hz con VRR y capturas reales de la consola: ×4 mantiene
    las 30 reales, el 98,6 % de las imágenes dura justo un refresco, la real se ve a 41 ms de llegar y la primera imagen nueva a 17 ms. Lo
    quieto (interfaz, texto) sale de las reales. También con DLSS 5 (medido a 4K con DLSS 5: ×4 mantiene las 30 reales, 118 imágenes/s, el 99 % a un refresco, la primera imagen nueva a 34 ms y la real a 59 de la captura, frente a 33 / 50 con Smooth Motion ×2; `motor_fsr_multi_dlss5=0` = solo sin DLSS 5).
  - **Smooth Motion ×4 quitado** (1.3.2): hacía XeSS FG ×2 del motor debajo, cuyas inventadas eran las más flojas con vídeo real de
    juego. Smooth Motion va ×2; un Smooth Motion ×4 guardado pasa a ×2 y la app lo dice una vez. Para ×3 / ×4, Enhanced FG.
  - **Enhanced FG ×2 / ×3 / ×4** (abajo).
  - Interpolar siempre espera a la siguiente real: ×4 enseña la real ~8 ms más tarde que ×2. Smooth Motion ×3 / ×4 por sí solo y XeSS FG
    ×3 / ×4 fuera de las gráficas Intel no se ofrecen: para ellos haría falta parchear el driver de NVIDIA o la librería de Intel.
- **Enhanced FG** (nueva, experimental, cualquier gráfica con Direct3D 12): la generación propia de la app, escrita desde cero para el motor
  (sin SDK ni DLL de NVIDIA, AMD o Intel): flujo óptico propio en los dos sentidos con comprobación de ida y vuelta para saber qué se
  tapa y qué se destapa, el movimiento elegido punto a punto en cada inventada, la interfaz quieta nítida, sin inventadas mezcladas en
  los cortes de escena y su propio hilo de presentación, que enseña cada imagen a una hora fija contada desde la llegada de la captura.
  ×2 / ×3 / ×4 y dos modos: calidad (interpolación) y **baja latencia** (las inventadas van hacia delante desde la última real, que se
  enseña en cuanto está: medido a 4K, modo 30, ×2, 60 imágenes/s, todas 2 refrescos, y la real a 9-10 ms de llegar, frente a ~35 ms con
  Smooth Motion ×2). Opción extra en *Tipo de generación*, nunca la de serie; filas del menú *Multiplicador* y *Enhanced FG: baja
  latencia*. Medido otra vez en esta versión (4K, modo 30, capturas reales de la PS5): ×2 calidad 60 imágenes/s parejas, la real a 33 ms y la primera nueva a 16; ×2 baja latencia, la real a 9 ms (con DLSS 5: 33 ms, frente a 50 con Smooth Motion ×2); ×4, 120 parejas pero la real a 62 ms de llegar. Necesita una pantalla de 100 Hz o más (a 60 Hz se midió rota y ahí no se ofrece). Sus shaders los compila de fondo el primer motor que se abre, así que elegirla no espera a compilarlos. Con vídeo real de juego (GTA V conduciendo) sus inventadas se parecen a
  una mezcla de las dos reales (en uno en primera persona, cerca de Smooth Motion): por eso sale como «Enhanced FG (experimental)». ×3 / ×4
  también con DLSS 5 (medido con vídeo real de GTA V a 4K y DLSS 5 nivel 4: ×4 con las 30 reales, 120 imágenes/s, todas a un refresco y
  DLSS 5 al 80 %); si ×4 no llega a 30, baja DLSS 5 y, si ni así, ×2 esa sesión (`efg_multi_dlss5=0` = ×2 con DLSS 5). Si la gráfica se
  cae con él, sigue en el menú con el motivo y elegirlo otra vez es reintentar.
  **1.3.1**: ×2 va fluido aunque el juego de la consola no mantenga los 30 fps (cada imagen va por la hora en que llegó su contenido y las
  inventadas rellenan el hueco de verdad: en la tele, lo que se ve se aparta de su hora 8,2 → 1,3 ms, percentil 95); ×4 en **baja
  latencia** enseña la real una imagen de salida más tarde, así que la primera inventada de cada grupo se interpola y las demás se
  predicen 1/4 menos lejos (un 22 % menos de errores al girar la cámara, +5 ms; no con DLSS 5, donde no cabría; ×2 y ×3 como antes;
  `efg_retraso_lat4=-1` en `motor_extra` = como antes); la baja latencia tiene su propia pasada hacia delante (menos puntos sueltos en
  los bordes).
  **1.3.2**: llega a sus fps cuando la gráfica tiene margen (×4 del modo 30 y ×2 del modo 60, también a 1440p / 1080p + DLSS y con
  DLSS 5): una inventada ya no se tira solo porque la real siguiente esté en la cola, solo si chocaría con ella (medido en la tele: ×2
  baja latencia del modo 60 105,6 → 118,2 imágenes/s, ×4 baja latencia a 120 sin inventadas saltadas ya en marcha). Los giros rápidos de
  cámara con base 30 aguantan: cada pareja de reales calcula primero el movimiento de toda la imagen (una búsqueda de ±25 % del ancho),
  que sirve de semilla al flujo óptico, y el flujo tiene seis niveles (contra las imágenes de en medio de verdad de vídeo real girando
  rápido: las peores de 42-46 → 20-27 de error). En el modo 40, ×3 = 120 fps (lo de serie ahí) o ×2 = 80. `efg_giro=0 efg_niveles=5` /
  `efg_salto_fino=0` en `motor_extra` = como la 1.3.1.
- **Reescalado más nítido en movimiento** (DLSS SR K / L / M y E, XeSS, FSR 3.1-4): los vectores de movimiento del motor se afinan a
  fracciones de píxel por casilla de 8×8 solo para el escalador, que ya no mezcla una historia desalineada (medido contra la imagen de
  verdad, 1440p → 4K en movimiento: K 1,32 → 0,82, M 0,92 → 0,44, L 0,79 → 0,44, E 1,05 → 0,72; con un vídeo de capturas reales de la
  consola K 1,29 → 0,78). Sin DLSS 5, el escalador temporal recibe la captura sin el suavizado de bordes / quitar tramado / liso que
  emborronaba las texturas finas del suelo incluso con la cámara quieta. **El modelo de DLSS que eliges es el que se pone** (es una sola
  elección para todos los juegos); **XeSS** sale también en la fila *Escalador* del menú (también en NVIDIA). Unos +1,3 ms de gráfica a
  1440p (`sr_vectores=0`, `sr_aa=1` = como antes). Medido otra vez con vídeo real de juego en 4K bajado a 1440p y vuelto a subir, contra
  el original: los vectores afinados solo se usan donde son fiables (una escena de noche ya no se emborrona: DLSS M nitidez 0,42 → 0,82,
  con DLSS 5 0,52 → 0,89); los modelos transformer (K, L, M) llevan un CAS ligero solo donde la imagen se mueve (DLSS M en movimiento
  0,75 → 0,84; lo quieto, igual); con DLSS 5 y un escalador detrás, la captura va también sin suavizado de bordes / quitar tramado / liso;
  el modelo elegido ya no pasa a K por el pico de rehacer DLSS 5; y con DLSS 5 a 1440p / 1080p / 720p el modelo de serie es M y no K
  (`sr_vec_incert=0`, `sr_nitidez_mov=0`, `sr_aa_dlss5=0` = como antes). **1.3.2**: donde la imagen anterior movida con su vector no casa
  con la de ahora (un suelo en perspectiva, un giro de cámara), DLSS SR empieza ese punto de cero en vez de mezclar la historia
  equivocada: el suelo en marcha con DLSS M / L y los giros con K / J salen nítidos (carretera con DLSS M: nitidez del suelo 0,56 → 0,83
  del original; giro con K / J 0,76 / 0,78 → 0,85), lo quieto igual, unos +0,3-0,5 ms a 1440p (`sr_sin_historia=0` = como antes).
- **Los avisos en pantalla dicen lo que va de verdad** (sacado de lo que reciben el motor y el driver, no de lo pedido): el «Aplicar»
  del menú de ajustes dice solo lo que cambia, con el multiplicador de verdad («Enhanced FG x4 (calidad) · 30 → 120 fps») y, si algo de lo
  pedido no puede ir, por qué («x4 no: sin VRR (G-SYNC) en la pantalla»); un aviso por cambio (en el motor nuevo si cambia de motor) y
  todos quedan en el log.
- **La cabeza del personaje ya no desaparece al girar la cámara** (XeSS FG / FSR 3 FG y FSR 3 FG ×3 / ×4): el motor separa el
  personaje y la interfaz del fondo, da a cada uno su movimiento y pone el personaje delante en la profundidad (con FSR 3 FG: error de la
  cabeza −84 %, del contorno −73 %, del texto de la interfaz −91 %; unos 0,6 ms de gráfica a 4K; `fg_cabeza=0` = como antes).
- **Ya sin mpv** (1.3): el motor hace todo lo que hacía mpv. El menú de la consola a 4K nativo, las capturas que lee la app para
  ver el menú, el juego y *No Signal* (ahora en color, como las de mpv), el telón con la imagen congelada, la sonda del modo de bajo
  consumo y las pruebas de imagen y sonido del asistente. El **sonido de la consola** también es del motor, en un proceso aparte solo
  de sonido (`ConsoleEnhancerEngine.exe --audio`): el mismo pin de la capturadora, formato y trozos de 20 ms, la salida de sonido de
  Windows (WASAPI), la misma mezcla de 7.1 a estéreo con limitador y la corrección de la deriva del reloj dentro. `audio_motor_extra=`
  le pasa `audio_cola_ms=30` (su cola), `audio_salida_ms=10`, `audio_deriva=0|1`; su registro es `settings\logs\audio.log`. Las
  capturadoras MJPEG (USB 2.0, p. ej. MS2109) las descomprime el motor hasta 720p; la captura en H.264 / HEVC ya no vale (elige un
  formato sin comprimir).
- **Escalas que se adaptan a tu PC** (`escalas_adaptables=1`, de serie): el asistente estima el modelo de DLSS y los porcentajes para tu
  gráfica y tu pantalla; jugando, el motor mide el tiempo de gráfica cada segundo y baja la calidad si no llega (en 2 s) o la sube poco
  a poco si sobra (tras 20 s), y lo recuerda para la próxima vez (`settings\scales_engine.txt`). Bandeja > Imagen > *Escalas que se
  adaptan solas* / *Olvidar las escalas aprendidas*. Con el extra "DLSS 5" hay además un **freno de emergencia**: si la gráfica va muy
  por encima de lo que cabe, quita una pasada o baja el % de golpe (para no atascar la gráfica) y eso queda como lo aprendido.
- **Comprueba que se llegan los fps** (motor propio): cada cuarto de segundo el motor cuenta las imágenes de la consola que ha tenido que
  tirar (y, con Enhanced FG, las inventadas que se ha saltado); un tirón suelto, un juego lento en la consola, despertar, el relevo entre
  motores, el menú de ajustes o perder el foco no cuentan. Si el PC no llega se ve en 1-2 s (1.3.1). **1.3.2**: solo baja algo si la
  gráfica va llena de verdad (la gráfica entera, el contador de NVIDIA, por encima del 90 %, o con Enhanced FG una real que tarda en
  estar); si no, es el ritmo, el tope de imágenes del VRR o la fuente, no se cambia nada y un aviso lo dice una vez («Enhanced FG x4 · 113
  de 120 fps: no es la gráfica (al 41 %), no se cambia nada»). Entonces el motor baja, de uno en uno y sin reabrir nada: DLSS 5 (primero
  a pasos grandes, hasta el 25 % a 4K, el **33 % a 1440p** y el 44 % a 1080p, y una pasada), luego el reescalado DLSS (un modelo más
  ligero y, como último recurso, FSR 1 en vez de DLSS esa sesión) y solo al final el multiplicador (Enhanced FG ×4 → **×3** → ×2) **solo
  esa sesión**. Sin multiplicador que bajar (×2 o sin generación) la app sigue: DLSS 5 fuera, 120 → 60, captura más baja, 60 → 30 con
  generación, **todo solo esa sesión**: desde la 1.3.2 nada de lo que baja la app sola se guarda en el perfil del juego. Un aviso dice lo
  que se ha bajado
  ("Enhanced FG x4 · DLSS 5 al 25 % y DLSS K para que quepa"); `escalera=0` en `motor_extra` = lo de la 1.3. Si sobra gráfica unos minutos, la escala de
  DLSS 5 vuelve a subir la próxima vez que entres al juego, nunca a un valor que ya falló. Se recuerda por juego y gráfica
  (`settings\scales_auto.txt`).
- **Motor de reserva** (`motor_reserva=1`): jugando, queda preparado en segundo plano un motor con el otro tipo de generación del modo
  (XeSS FG si juegas con Smooth Motion; Smooth Motion si juegas con XeSS FG o FSR 3 FG), y cambiar entre ellos pasa de 4-5 s a ~0,5 s.
  `motor_reserva=0` lo quita (ocupa ~0,9 GB de memoria gráfica).
- **No se cierra sola si la gráfica se para**: si la imagen deja de avanzar con la consola encendida (la gráfica atascada o reiniciada,
  el motor cambiando de ajustes), la app lo distingue de la consola apagada (el motor avisa, la capturadora le sigue mandando imágenes o
  la consola sigue sonando), espera y, si dura más de 8 s, vuelve a abrir solo la imagen (*Recuperando la imagen*). Con la consola
  apagada de verdad (pantalla *No Signal* o sin imagen ni sonido) se cierra como siempre, y no apaga el PC si la gráfica falló en el
  último minuto (`apagado_confirmar`, `cuelgue_relanzar_ms`, `apagar_pc_confirmar`). Un fallo en un hilo secundario va al log y la app
  sigue (`ConsoleEnhancer.exe.config`).
- **Sin "Sin señal" en los cortes cortos**: cuando la consola cambia un momento de modo HDMI (p. ej. la PS5 pasando a 120 Hz con un
  juego a 120), se queda la última imagen; el aviso de siempre solo sale si dura más de 5 s.
- **Nunca atascada en un formato de captura**: si tras un cambio de captura (menú de la consola, el perfil de un juego, el menú de
  ajustes, el modo 30 / 60 / 120) no llega imagen con la consola encendida, pasa en segundos a un formato que tu capturadora sí da (el
  mismo tamaño a 59,94 fps, el último que funcionó, 1080p) y lo dice en pantalla (`captura_segura=1`).
- **HDR**: RTX HDR (NVIDIA) o Auto HDR de Windows para convertir la imagen SDR de la consola, en el paso *Imagen* del asistente
  (apagados de serie; necesitan el HDR de Windows encendido en la pantalla).
- **VRR** (`vrr=auto`): se enciende solo si la pantalla tiene VRR (G-SYNC / G-SYNC Compatible / FreeSync / HDMI VRR) y Windows y el
  driver lo permiten; en AMD / Intel, actívalo en el asistente (`vrr=1`). También en bandeja > Imagen > VRR. Encendido, el motor propio
  presenta cada imagen en cuanto la acaba (sin esperar al refresco) y, con la generación, sincroniza solo donde haría falta
  (`motor_vrr_fg_vsync=-1`); solo si la pantalla dice tener VRR, para no dar cortes en una que no lo tenga. En el modo 60 con generación
  (Enhanced FG y, desde la 1.3.2, Smooth Motion ×2: 59,94 reales y 119,9 en la tele en vez de 58 y 116, `motor_60sm_refresco_fijo=auto`)
  y una pantalla de 120 Hz con G-SYNC, el motor va con **refresco fijo** (G-SYNC apagado solo en el perfil de NVIDIA de su exe mientras
  juegas así; se quita al salir, y si la app se cae, al volver a abrirla): con G-SYNC, Reflex limita la salida a ~116 imágenes/s y se
  perdían ~2 imágenes de la consola por segundo; así salen todas y con menos retraso (`motor_60fg_refresco_fijo=auto`; con 144 Hz o
  más, a 119,88 Hz, sin VRR o sin NVIDIA no se toca nada; nunca en el modo 120, que siempre sigue con VRR).
- **Selector de generación de fotogramas**: L3 jugando (o Ctrl+G con el teclado sobre la imagen, el menú de ajustes o la bandeja)
  recorre los tipos que tiene tu gráfica (Smooth Motion y, la última, Enhanced FG; XeSS FG / FSR 3 FG solo con `generacion_xess=1` /
  `generacion_fsr3=1`) y lo recuerda por juego. Solo se ofrece lo que llega a ritmo. Smooth Motion va ×2 (×4 quitado en la 1.3.2);
  Enhanced FG (y FSR 3 FG, si se vuelve a poner) tiene ×2 / ×3 / ×4 en la fila *Multiplicador* del menú de ajustes. Si no se llega a las reales, el multiplicador es lo último que baja,
  solo esa sesión (×4 → ×3 → ×2; ver *Comprueba que se llegan los fps*). Desde la 1.3 no hay DLSS Frame Generation ni los ×3 / ×4 de antes (no iban bien); los perfiles y ajustes que los
  usaban pasan solos al tipo más parecido (en NVIDIA, Smooth Motion) y conservan todo lo demás. Si un tipo de generación pierde la
  gráfica en el motor, el resto de la sesión va otro (FSR 3 FG ×3 / ×4: ×2), también en el motor.
- **Enhanced RT (experimental)** (1.3.3): fila al final del menú de ajustes del mando que añade a la imagen de la consola luz trazada
  en espacio de pantalla: sombras de contacto (oclusión ambiental) y luz rebotada de un rebote sobre un relieve estimado de la propia
  imagen (la consola no da profundidad), con vectores de movimiento propios, acumulación temporal (sin ruido ni estelas) y el HUD / texto
  protegidos; sin reflejos. Apagada de serie (`enhanced_rt=0`; manda lo elegido en el menú); unos 0,7 ms de gráfica por imagen real
  (~1,1 ms a 4K con Enhanced FG ×4, ~1 ms más de latencia). Se enciende / apaga sin reabrir la imagen. Más fuerte:
  `motor_extra=rt_fuerza=1.5`.
- **Paneos planos** (1.3.3): al desplazar un mapa o un menú con el joystick, Enhanced FG (modo calidad) mide ese movimiento único con
  precisión y lo sigue, también al arrancar y frenar, así que carreteras y textos ya no tiemblan; la interfaz quieta no se toca
  (`efg_plano=0` = como la 1.3.2). Para mapas va mejor el modo calidad que la baja latencia.
- **Añadir a Steam** (1.3.3): en el panel de ajustes (ConsoleEnhancer Settings.exe, sección "Steam"), pone ConsoleEnhancer en tu
  biblioteca de Steam como juego ajeno con su icono (lo mismo que "Añadir un juego ajeno" de Steam), sin duplicarlo y con una copia de
  seguridad antes de la lista de Steam; "Quitar de Steam" lo quita. Si Steam está abierto ofrece reiniciarlo. Si el mando hace cosas
  raras abierto desde Steam, desactiva Steam Input para ese acceso.
- **Modo 40 fps** (1.3.2): para los modos de 40 de los juegos de consola (los "equilibrados" de la PS5 / Xbox Series). Pon la consola
  con salida a 120 Hz (PS5: Ajustes > Pantalla y vídeo > Salida de vídeo > Salida a 120 Hz: Automático; para todo lo demás el consejo
  sigue siendo 4K a 60) y 1440p o 1080p, elige el modo de 40 / equilibrado del juego y, en ConsoleEnhancer, 40 fps (menú de ajustes o el
  botón Menú: 30 → 40 → 60). Con captura de 1440p / 1080p va a 120 Hz (una nueva de cada 3); a 4K va a 60 y la cadencia 2-1 se reparte
  pareja. Salida: 40 fps con cada imagen 3 refrescos a 120 Hz, o Enhanced FG ×3 = 120 fps (lo de serie aquí), ×2 = 80 fps (VRR), Smooth
  Motion ×2 = 80 fps. El tipo de generación es el del modo 30; el multiplicador de Enhanced FG del modo 40 es suyo (×2 / ×3). El menú de
  la consola sigue a 4K nativo a 60 como siempre.
- **Modo 120 fps (esports)**: para los juegos de 120 Hz de la consola, captura a 120 sin generación de fotogramas y con el menor
  retraso. El botón de modo pasa 30 → 40 → 60 → 120, el 120 solo si la consola saca 120, la tele va a **120 Hz o más** y la captura de ahora es una
  que tu capturadora da a 120 (normalmente **1080p o 1440p**; el asistente lo lee de la capturadora, capturas_120). A 4K sigue 30 ↔ 40 ↔ 60:
  la app no te baja la resolución sola. Si no cabe, un aviso dice por qué. permitir_120=auto (1 / 0 para forzarlo / quitarlo).
- **Menú de la consola a 4K nativo**: el HOME de la Switch 2, el menú de la PS5 y el de Xbox se reconocen y van sin escalado ni
  generación, y sin "DLSS 5" (tal cual, con ~10 ms menos de retraso; `motor_dlss5_home=0`); los cambios menú ↔ juego tardan menos de 1 s, con la imagen de antes hasta la nueva (sin telón negro desde la 1.3.2).
- **Las apps de la consola no son juegos**: la Store, Remote Play, la galería, PS Plus, Ajustes y Share Factory (en la Xbox, Microsoft
  Store, Game Pass, Configuración, Capturas...) y las apps de vídeo y música (Netflix, YouTube, Spotify, Twitch...) se tratan como el
  menú de la consola: 4K nativo, sin perfil de juego ni generación, hasta volver al menú (o hasta que vuelve la imagen del juego de
  antes). Nunca crean un juego falso ni se guardan como portada de un juego.
- **Reconoce cada juego y le guarda su perfil** (`games\profiles.txt`): en la Switch / Switch 2 lee el nombre del juego en el HOME
  y guarda su icono, así que lo que cambies con el mando (captura, modelo y pasadas de DLSS 5, escalador, modo 30/60 y tipo de
  generación) se recuerda por juego y se vuelve a poner solo la próxima vez que entras. En la **PS5** reconoce el juego por su
  **icono en foco** en el Inicio (con su nombre al lado) y por su **portada** al arrancarlo: basta con entrar en el juego desde el
  Inicio; volver al que ya estaba abierto con el botón PS no cambia de perfil. En la **Xbox Series**, igual con la tarjeta en foco del
  Inicio (la grande, con el nombre abajo) y la imagen de arranque de cada juego (*preliminar: probado con capturas oficiales, sin
  probar aún con una Xbox de verdad*). Si no reconoce el juego, usa el perfil único de la consola, como antes
  (`ps5_reconocer_juegos=1`, `xbox_reconocer_juegos=1`; 0 = un solo perfil para toda la consola). Lo que aprende está en
  `games\console_icons.txt` y `games\console_covers.txt`.
- **Mandos**: Xbox / XInput, DualSense / DualShock 4, mandos de Switch y Switch 2, 8BitDo, GameSir y genéricos HID, sin programas
  aparte. La app lee el mando sin quitárselo a la consola y, fuera de la imagen, maneja el PC (modo PC).
- **Menú de ajustes en pantalla**: el botón central del mando (Xbox / PS / HOME), Ctrl+O o bandeja > *Menú de ajustes en pantalla…*.
  Muestra todo lo que se puede cambiar (captura, modo 30 / 60 / 120, generación de fotogramas, su tipo y su *Multiplicador*, DLSS 5 con
  nivel y pasadas, y el escalador, con cada **modelo de DLSS**: automático, M, L, K, J, E, guardado para todos los juegos, y **XeSS**,
  **FSR 1** y **Sin reescalado**, guardados por juego) con lo de ahora marcado. Se
  mueve con la cruceta, el stick o las flechas; **Aplicar** lo pone todo a la vez con una sola reapertura de la imagen; B o el botón
  central cierran sin cambiar nada. No le quita el foco a la imagen (la generación sigue) y no se abre encima del menú de la consola.
  Si el botón Xbox abre también la Game Bar de Windows, apágala en Configuración > Juegos.
- **Todo, también en el menú de la bandeja** (clic derecho en el icono), con lo puesto marcado: generación sí / no y su tipo, modo
  30 / 60 / 120, captura 720p / 1080p / 1440p / 4K, DLSS 5 (sí / no, nivel, pasadas), escalador alternativo, filtro de tramado, *Ver lo
  que va puesto (en pantalla)* y el menú de ajustes; además las opciones de Imagen, de inicio y de apagado, el asistente, los
  componentes, Añadir / Quitar DLSS 5, Acerca de y licencias y el enlace de Ko-fi. Para cuando no hay mando conectado al PC.
- **Atajos de teclado** sobre la imagen de la consola: Ctrl+F, Ctrl+G, Ctrl+M, Ctrl+1…4, Ctrl+R, Ctrl+D, Ctrl+I, Ctrl+O, Alt+F4 (ver
  [Controles](#controles)). Siguen funcionando con Joyxoff y programas parecidos abiertos.
- **Cerrar**: bandeja > Salir, Alt+F4, la X de la ventana o cerrarla desde la barra de tareas cierra la app entera (no se toma por un
  corte de la señal). La tecla Windows, el menú Inicio, la barra de tareas y Alt+Tab funcionan normal mientras juegas.
- **Sin sonido doble (eco)**: mientras suena la capturadora por la app, quita *Escuchar este dispositivo* de Windows en esa entrada,
  cierra audios que se hayan quedado abiertos y avisa si otro programa de captura también la reproduce (ver las preguntas frecuentes).
- **Iniciar con Windows** (un acceso directo en tu carpeta Inicio, sin permisos de administrador) con la pantalla en negro hasta que
  hay imagen y, opcional, el escritorio en negro mientras la app está abierta (bandeja > *Escritorio negro con la app abierta*).
- **Modo de bajo consumo al apagar la consola**: el PC sigue encendido pero casi parado (pantalla apagada, app cerrada, un plan de
  bajo consumo) hasta que vuelve la consola, sin nada de Windows en pantalla (ni barra de tareas, ni escritorio, ni cursor) desde que
  se apaga la consola hasta que vuelve su imagen. Cómo funciona y cuánto gasta se explica en el último paso del asistente y en la bandeja:
  ver [Energía](#energía-qué-hace-el-pc-al-apagar-la-consola).
- **Consola o PC normal** (bandeja > *Cómo se usa el PC*): *Como una consola* (se abre con Windows, escritorio negro y modo de bajo
  consumo al apagar la consola) o *Como un PC normal* (el Windows de siempre: la app solo se abre cuando la abres y el PC sigue encendido al apagar
  la consola). Cada uno explica antes lo que hace; se cambia cuando quieras.
- **Desinstalar** (bandeja > *Desinstalar ConsoleEnhancer…*): quita el inicio con Windows, devuelve el escritorio y el plan de energía
  normales y borra solo los archivos de la app (lo demás de su carpeta se queda). Tus ajustes y perfiles se pueden guardar para otra vez.
- **`ConsoleEnhancer Settings.exe`** junto a `ConsoleEnhancer.exe` (sustituye a `Inicio y apagado.bat`): todo lo anterior más DLSS 5,
  componentes, registros, las pruebas del aviso y de la espera y desinstalar, en una ventana. Si la app está abierta, ofrece reiniciarla
  para aplicar los cambios.
- **Componentes**: Intel XeSS y AMD FSR se bajan una vez de sus páginas oficiales comprobando su SHA-256 (sin internet, con los
  zip en `downloads\`; si en la carpeta de la app queda un `magpie\` de una versión anterior, de ahí salen los archivos de Intel y AMD
  sin bajar nada); bandeja > *Componentes: comprobar / volver a descargar* los repara.
- **Tus ajustes viven en este PC, no en la carpeta de la app** (1.3.1): `%LOCALAPPDATA%\ConsoleEnhancer\settings` guarda el ini (lo que
  eliges en los menús), los perfiles de cada juego, la consola reconocida, el idioma, las escalas aprendidas y los registros (`logs\`, con
  los shaders ya compilados del motor). Es la misma carpeta para **todas** las versiones y se escribe en cada cambio: una versión nueva
  descomprimida donde sea (Descargas, Escritorio, otra unidad, sin ninguna anterior al lado) usa tus ajustes tal cual desde el primer
  arranque. La primera vez (1.3.1) la app los trae una vez de la carpeta de datos usada más recientemente (el `data\` de una versión
  anterior de al lado o la copia de `%LOCALAPPDATA%\ConsoleEnhancer\backup`); se apunta en `settings\inherited.txt` y se dice una vez
  en pantalla.
- **Al actualizar no pierdes lo elegido**: la versión nueva pone sus ajustes generales nuevos sin tocar lo que elegiste tú o el asistente
  (copia en `.antes-*`); solo lo que ya no existe pasa a lo más parecido (XeSS FG y FSR 3 FG -> Smooth Motion en RTX 40 / 50; DLSS FG,
  los ×3 / ×4 de antes, Magpie y mpv se quitaron en la 1.3), se apunta en el registro y se dice una vez en pantalla. Una versión **más
  antigua** abierta después nunca reescribe los ajustes de una más nueva: trabaja en una copia (`settings-older\<versión>`).
- **Empezar de cero**: cierra la app y borra `%LOCALAPPDATA%\ConsoleEnhancer\settings` (o la carpeta `ConsoleEnhancer` entera de ahí,
  que quita también la copia de seguridad y la copia de tu archivo de DLSS 5); *Desinstalar* con «Sí = se borra todo» hace lo mismo.
- **Portable** (opcional): crea un `portable.txt` vacío junto al exe y todo se guarda en su `data\` (también si la carpeta del PC no se
  puede escribir). El `portable.txt` que traían los zip hasta la 1.3 no cuenta.
- **Interfaz en español e inglés**, a elegir en el primer paso del asistente.

## Energía: qué hace el PC al apagar la consola

Con `apagar_pc=1` (último paso del asistente o bandeja > *Apagar el PC al apagar la consola*), `apagar_pc_modo` elige qué pasa. Se
cambia en el asistente, en bandeja > *Al apagar la consola, el PC* (con *¿Cómo funciona cada uno y cuánto gasta?*) o en
`ConsoleEnhancer Settings.exe`.

| Modo | Cómo funciona | Consumo en el enchufe |
|---|---|---|
| **Bajo consumo, como una consola en reposo** (`espera`, de serie) | Ver abajo. | Unos 30-60 W según el PC (una gráfica medida: 12,5 W en reposo, en P8) |
| **Suspender** (`suspender`) | La suspensión de Windows (S3); al despertar el PC se abre ConsoleEnhancer. Suspendido, la capturadora no tiene corriente: encender la consola **no** lo despierta, y los mandos casi nunca pueden; se despierta con el teclado o el botón de encendido. | 2-5 W |
| **Apagar** (`apagar`) | El PC se apaga del todo; ni la consola ni el mando lo encienden: con su botón (con *Iniciar con Windows*, se abre la app). | 0,5-2 W |

### El modo de bajo consumo

**No** es suspender ni apagar: el PC sigue encendido, así que no hace falta "encenderlo" otra vez. Al apagar la consola:

1. Sin cuenta atrás: una cubierta negra sin cursor tapa todas las pantallas al momento (la barra de tareas se queda oculta), la
   pantalla se apaga, ConsoleEnhancer se cierra (con el motor) y Windows pasa a un plan de bajo consumo temporal
   (*Economizador* con el procesador al 50 % y sin turbo, `espera_cpu_max`; se borra al despertar), así que la CPU y la gráfica bajan a
   lo mínimo (la gráfica queda en reposo, en P8; una RTX 4060 Ti midió 8,5-9 W con la pantalla apagada).
2. Solo queda un vigilante muy pequeño, en su propio proceso (un 0,1 % de un núcleo, prioridad baja, EcoQoS, sin subir el temporizador
   de Windows): mira una imagen pequeña de 720p de la capturadora y además atiende a los mandos y al teclado. La tecla Windows no abre
   Inicio y, si otro programa enciende la pantalla sin que nadie toque nada, se vuelve a apagar.
3. Al volver a encender la consola (la capturadora recibe imagen), encender un mando o pulsar una tecla, vuelve la pantalla en negro
   (la barra de tareas sigue oculta hasta que vuelve la imagen de la consola), vuelve el plan de energía normal y ConsoleEnhancer se
   abre en 2-3 s. La barra vuelve siempre al cerrar la app, pasar a *Como un PC normal* o desinstalar, también si la app se cierra a
   la fuerza (`espera_ocultar_barra=0` la deja como está).
4. Si lo ha traído un mando o una tecla y no enciendes la consola en 90 s, vuelve al bajo consumo (`espera_volver_sin_consola_s`;
   0 = se queda despierto).

Gasta más que suspender o apagar (el PC sigue encendido: unos 30-60 W en el enchufe según el PC, unos 5-6 EUR al mes si se queda así
todo el día), pero es el único modo en el que basta con encender la consola para volver a jugar.

- Con suspender y apagar, antes sale una cuenta atrás (`apagar_pc_aviso_s=15`; 0 = sin aviso). Solo la cancela un botón del mando, una
  tecla o un clic de verdad, o mover el ratón de verdad (más de 40 px); lo que inyectan otros programas (Joyxoff, mapeadores del mando,
  Steam Input) no cuenta.
- No apaga el PC si la gráfica falló en el último minuto (ver *No se cierra sola si la gráfica se para*).
- Cerrar la app a mano (Salir, Alt+F4) nunca apaga ni duerme el PC.
- `ConsoleEnhancer Settings.exe` > *Probar el aviso de apagado* / *Probar el modo espera* (el bajo consumo: se apaga la pantalla;
  enciende la consola o un mando, o pulsa una tecla).

## DLSS 5 (opcional)

"DLSS 5" es como la comunidad llama a un filtro neuronal de NVIDIA (DLSSNR) que redibuja la imagen de la consola con más
detalle, antes del escalado. **No es una función oficial de NVIDIA y no viene con ConsoleEnhancer**: necesita `nvngx_dlssnr.dll`, una
DLL de NVIDIA modificada por la comunidad, sin firma ni licencia, que no se puede repartir. Por eso la app **no la incluye, no la
descarga ni dice de dónde sacarla**. Todo lo demás (DLSS, generación de fotogramas, HDR, VRR...) funciona igual sin ella.

Si ya tienes tu propio archivo, añadirlo es un clic:

1. Icono de la bandeja > **Añadir DLSS 5 (tu propio archivo)…** (o el botón **Añadir DLSS 5** del último paso del asistente; solo con
   una GeForce RTX y 6 GB de VRAM o más).
2. Lee el aviso (no es oficial, NVIDIA no lo soporta y es bajo tu responsabilidad). **«Añadir DLSS 5» encuentra ahora estos archivos si
   ya están en tu PC**: busca unos segundos, solo en tu PC (Descargas, Escritorio, otras carpetas de ConsoleEnhancer, carpetas de
   Magpie, de programas y de juegos; se puede cancelar), enseña lo que encuentra con carpeta, tamaño, versión y si lo firmó NVIDIA, pone
   primero el que vale para tu gráfica y eliges tú (o *Elegir otro archivo…*). No se descarga nada. Si al abrir la app falta DLSS 5,
   busca una vez y pregunta antes.
3. La app comprueba que es una DLL de Windows de 64 bits con ese nombre, te dice si es la original de NVIDIA (firma válida) o la
   modificada por la comunidad (sin firma) y la copia a `extras\` junto al exe.
4. Dices si lo enciendes ya. Jugando se enciende y se apaga con el botón **A** del mando (X / Y / B: pasadas; L1 / L2 / R2 / R1: nivel).

Para quitarlo: icono de la bandeja > **Quitar DLSS 5** (borra la copia de `extras\`; tu archivo original no se toca).

Tus archivos de DLSS 5 se guardan también en `%LOCALAPPDATA%\ConsoleEnhancer\extras`, así que una versión nueva descomprimida en otra
carpeta los recoge sola. Con tu archivo, DLSS 5 va siempre dentro del motor propio (`engine\PrismaNr.dll`, que viene con la app desde
la 1.3; 1 a 3 pasadas, con cualquier tipo de generación): mucho menos retraso que por Magpie en la 1.2.3 (medido a 4K, modo 30 +
generación ×2: 43 ms en vez de ~72 la imagen real; 1080p → 4K con 2 pasadas: 37 en vez de ~52) y un ritmo más regular. No hay nada que
elegir. Sin `engine\PrismaNr.dll` el motor va sin DLSS 5. Con DLSS 5 encendido, Enhanced FG ×3 / ×4
bajan la escala de DLSS 5 hasta mantener las reales cuando la gráfica va llena (×3 / ×2 esa sesión si no llegan); FSR 3 FG, si se vuelve a poner con `generacion_fsr3=1`, ofrece también ×3 / ×4
(`motor_fsr_multi_dlss5=0` = solo ×2 con DLSS 5).

## DLSS 5 y la ley

En corto y neutro (es lo que dicen las licencias; no es asesoramiento legal):

- **Lo que reparte ConsoleEnhancer**: su propio código (GPL-3.0), las DLL oficiales de NVIDIA de Streamline / DLSS SR / Reflex /
  Vibrancia RTX, sin cambios, que NVIDIA deja repartir dentro de una app, y `engine\CeFsrFg.dll` (la interpolación del FidelityFX SDK
  1.1.4 de AMD, con licencia MIT, que permite cambiarla; compilada y cambiada por ConsoleEnhancer para FSR 3 FG ×3 / ×4, con el aviso de
  AMD). Los archivos de XeSS / FSR de Intel y AMD los baja de sus páginas oficiales.
- **Desde la 1.3 el repositorio y el zip llevan también `PrismaNr`**, la pieza nativa propia del motor (código propio). Carga el
  archivo que pones tú (`nvngx_dlssnr.dll`) sin pasar por la comprobación de NVIDIA (NGX), cosa que los términos de NVIDIA no permiten.
  El usuario decidió publicarla sabiéndolo. Sin tu archivo no hace nada. Se compila con las cabeceras públicas de NGX de NVIDIA, que
  solo se usan para compilar y no van en el repositorio.
- **Lo que no reparte, no baja ni enlaza nunca**: `nvngx_dlssnr.dll` ("DLSS 5") ni ningún archivo de NVIDIA modificado. *Añadir DLSS
  5* solo mira en tu propio PC. Cada uno pone el suyo, bajo su responsabilidad.
- **DLSS 5 legal hoy no existe fuera de los juegos de NVIDIA**: NVIDIA solo lo ha sacado dentro de los juegos que lo traen, solo en
  RTX 50, sin SDK ni plugin de Streamline para otras apps (comprobado el 22-11-2026: la vía pública de NGX responde "please update display
  driver" con el driver de ahora, y uno nuevo solo aceptaría la original firmada de NVIDIA, que solo funciona en RTX 50). La DLL que
  circula para RTX 20 / 30 / 40 es un binario de NVIDIA modificado.
- Lo que tiene cualquier RTX de forma oficial en el motor: DLSS Super Resolution, Reflex, el Smooth Motion del driver (RTX 40 / 50) y,
  si quieres, la Vibrancia RTX (no es DLSS 5: da color y claridad, no cambia la luz).
- **Sin DLSS Frame Generation de NVIDIA ni `dlssg_sm86`** desde la 1.3: la app no usa ni busca ese paquete de la comunidad. Los
  ×3 / ×4 que ofrece son legales: FSR 3 FG ×3 / ×4 con el código MIT de AMD (solo con `generacion_fsr3=1`) y Enhanced FG, que es
  código propio de la app. Para Smooth Motion ×3 / ×4
  por sí solo y XeSS FG ×3 / ×4 fuera de las gráficas Intel haría falta parchear el driver de NVIDIA o la librería de Intel (su licencia
  lo prohíbe), así que no se ofrecen.

## Preguntas frecuentes y problemas comunes

**Windows o el antivirus no me deja abrirlo / Microsoft Defender ha borrado ConsoleEnhancer.exe.** Los exe aún no van firmados, así
que cada versión nueva empieza sin reputación. SmartScreen ("Windows protegió su PC"): *Más información > Ejecutar de todas formas*.
Microsoft Defender: el 21-11-2026 puso en cuarentena las versiones 1.2.07 y 1.2.09 a los pocos segundos de abrirlas, al ponerse en el
inicio de Windows (*Behavior:Win32/Persistence.A!ml*, *Trojan:Win32/Bearfoos.A!ml*). Es un falso positivo: los mismos ficheros salen
limpios al analizarlos. Desde la **1.2.3** la app nunca se pone sola en el inicio (una copia nueva lo pregunta antes, y solo si la
abres tú), no lanza PowerShell ni `schtasks.exe`, y los exe llevan su versión y su editor. Si aun así Defender la quita: Seguridad de
Windows > Protección antivirus y contra amenazas > **Historial de protección** > la entrada de ConsoleEnhancer > **Restaurar**; si
vuelve a pasar, añade **solo la carpeta de la app** a las exclusiones (*Administrar la configuración > Exclusiones > Agregar una
exclusión > Carpeta*; la opción de la bandeja **Microsoft Defender: excepción para la app...** abre esa página con la carpeta
copiada) y, si puedes, avisa a Microsoft del falso positivo en https://www.microsoft.com/wdsi/filesubmission ("Incorrectly detected
as malware/malicious"). Cada release publica el SHA-256 del zip y de cada fichero de dentro (`.files.sha256`), y puedes compilarlo tú
(ver *Para desarrolladores*).

**Se queda la imagen pero sin sonido y el mando no hace nada.** Microsoft Defender cerró la app mientras el motor seguía
enseñando la imagen: mira la respuesta anterior (la app también lo explica la próxima vez que se abre).

**La descarga de XeSS / FSR falla.** Hace falta internet una vez. Con proxy o sin conexión: baja los zip que enlaza la ventana
(también en `licenses\VERSIONS.txt`) en otro PC y ponlos en una carpeta `downloads\` junto a `ConsoleEnhancer.exe`; se comprueban
igual. Si dice que no puede escribir, la carpeta es de solo lectura (Archivos de programa): muévela a una tuya.

**Pantalla negra o "No Signal".** Comprueba que la imagen se ve en el *Probar imagen* del asistente. Cierra cualquier programa que use
la capturadora (OBS, el software del fabricante, Discord con la cámara...). Si la consola cifra la salida (**HDCP**), la imagen sale
negra: apágalo en la consola (PS5: Ajustes > Sistema > HDMI > *Activar HDCP*). ConsoleEnhancer no quita protecciones anticopia.
El aviso de HDCP solo sale con la imagen negra lisa en todo el cuadro (desde la 1.3.3 un HUD, logo o texto quieto sobre negro no
cuenta) y la consola en silencio 20 s seguidos (`hdcp_aviso_s`; `hdcp_aviso=0` lo
quita): una escena oscura o un fundido ya no lo hacen saltar.

**No hay sonido.** Desde la 1.3 el sonido de la consola va por el motor (`ConsoleEnhancerEngine.exe --audio`). Mira
`settings\logs\audio.log`: cuando va, dice qué capturadora, pin y formato ha abierto (por ejemplo *48000 Hz 16 bits 8 canales, trozos de
20 ms*); cuando no puede abrirla, enseña los pines y formatos que da la capturadora. En el asistente, paso del sonido: prueba otra
entrada con *Probar sonido* (algunas capturadoras dan el audio por el propio dispositivo de vídeo y otras como un micrófono aparte), o
pásale al motor otros valores con `audio_motor_extra=` (p. ej. `audio_canales=2`).

**El sonido se oye dos veces (eco).** Algo más reproduce la capturadora a la vez que la app. Desde la 1.0.6 la app quita sola
*Escuchar este dispositivo* de Windows en la entrada de la capturadora mientras suena por ella (y lo devuelve al salir), cierra un audio
suyo que se quedara abierto de una sesión anterior y avisa en pantalla si RECentral, OBS (con monitorización), Streamlabs o XSplit
también suenan. Lo que no puede ver: la salida HDMI de paso de la capturadora conectada a una tele o un monitor con altavoces; baja
su volumen o usa otra salida. `sonido_doble=0` en el ini lo deja como antes.

**No me sale DLSS o la generación de fotogramas.** DLSS solo existe en NVIDIA GeForce RTX; Smooth Motion en RTX 40/50; la generación
necesita una pantalla de 100 Hz o más. Con otras gráficas va XeSS o FSR 4 / 3.1 y Enhanced FG (cualquier gráfica con Direct3D 12;
XeSS FG está quitada de momento, ver arriba). XeSS SR necesita los archivos de Intel que baja la app (bandeja > *Componentes*). Actualiza el driver.

**¿Qué generación uso?** En RTX 40 / 50, Smooth Motion ×2 (la de serie). Para ×3 / ×4 en el modo 30 (o ×3 en el modo 40), con una
pantalla de 120 Hz con VRR: Enhanced FG (con la cámara girando mucho, el modo calidad se ve mejor). Enhanced FG es la propia de la app, experimental: su modo *baja latencia* enseña la real en cuanto está
(menos retraso, más fallos en los bordes y cuando el movimiento cambia de sentido).

**Va a tirones o con retraso.** Abre el asistente otra vez (con la pantalla y la capturadora a su resolución buena), cierra programas que
pinten de fondo y, si la tele lo tiene, pon el modo juego. El log dice qué cadena se eligió.

**Sale "Recuperando la imagen".** La imagen se paró con la consola encendida (casi siempre la gráfica: driver, mucha carga) y la app
la vuelve a abrir sola; no hace falta tocar nada. Si pasa a menudo, actualiza el driver y quita el *overclock* de la gráfica. El log
dice el motivo ("NO es la consola apagada: ...").

**Pantallazos negros de unos segundos y `DXGI_ERROR_DEVICE_HUNG` / "grafica perdida" en `engine.log`.** Es la gráfica reiniciándose con
la carga (Windows lo apunta como el evento 153 de `nvlddmkm` en *Visor de eventos > Sistema*). Un **overclock de la memoria de la
gráfica** (por ejemplo, subir el reloj de la memoria con un programa de overclock) puede provocarlo con DLSS 5 y la generación de fotogramas aunque
los juegos vayan bien: deja la memoria a su velocidad de serie y vuelve a probar. Desde la 1.3 la app deja la última imagen en pantalla
mientras se recupera y, si pasa con XeSS FG o Enhanced FG, el resto de la sesión va con otro tipo de generación en el motor (con FSR 3 FG
×3 / ×4, ×2).

**Se me ha quedado el escritorio en negro.** `ConsoleEnhancer.exe --restaurar-escritorio`.

**¿Dónde están los ajustes y el log?** En `%LOCALAPPDATA%\ConsoleEnhancer\settings` (la misma para todas las versiones; `data\` junto
al exe solo con un `portable.txt` tuyo): `consoleenhancer.ini`, `games\` y `logs\consoleenhancer.log` / `engine.log`. Panel de ajustes >
*Abrir mis ajustes y perfiles* la abre. Para empezar de cero, cierra la app y borra esa carpeta. Si abres un *issue*, adjunta el log (revisa antes que no lleve nada
tuyo que no quieras enseñar, como nombres de carpetas).

**¿Y "DLSS 5"?** Es una DLL de NVIDIA modificada por la comunidad: **no viene, no se baja y no se recomienda**. La app funciona
entera sin ella; si ya la tienes, mira [DLSS 5 (opcional)](#dlss-5-opcional).

**¿Se ha estropeado XeSS o FSR?** Icono de la bandeja > **Componentes: comprobar / volver a descargar**: dice qué falta y los vuelve a
bajar de sus páginas oficiales.

**"Apagar el PC al apagar la consola" me ha cerrado cosas.** En el modo *Apagar* apaga Windows de verdad tras la cuenta atrás: guarda
tu trabajo, pasa al bajo consumo (*En espera*, bandeja > *Al apagar la consola, el PC*) o desactívalo en el menú de la bandeja. Cerrar la app a mano
nunca apaga nada.

**La cuenta atrás se cancela sola.** Desde la 1.0.13 solo la cancela una pulsación de verdad; si aún pasa, el log apunta cuántas
entradas inyectadas no contó.

**Encender la consola no hace volver el PC.** Eso solo va en el modo de bajo consumo (*En espera*), porque ahí el PC nunca se apaga.
Tras *Suspender* o *Apagar*, ni la consola ni los mandos pueden encender el PC: usa una tecla o el botón de encendido. Prueba el bajo
consumo en `ConsoleEnhancer Settings.exe` > *Probar el modo espera*.

## Avisos legales

- **Licencia**: ConsoleEnhancer es software libre bajo la **GPL-3.0** (`LICENSE`), **sin ninguna garantía**, con un permiso adicional de
  la sección 7 para usarlo con las librerías oficiales de NVIDIA, AMD, Intel y Microsoft (`LICENSE-EXCEPTION.txt`). Código de terceros,
  versiones y licencias: [`THIRD-PARTY.md`](THIRD-PARTY.md) y `licenses\` en el zip.
- **Lo que va en el zip**: la app (con `PrismaNr.dll` del motor, ver [DLSS 5 y la ley](#dlss-5-y-la-ley)),
  `engine\CeFsrFg.dll` (la interpolación del FidelityFX SDK 1.1.4 de AMD, MIT, cambiada por ConsoleEnhancer; el aviso de AMD en
  `licenses\AMD-FidelityFX-SDK-1.1.4-LICENSE.txt`) y las DLL de NVIDIA Streamline / DLSS / Reflex / Vibrancia RTX, sin cambios y
  firmadas por NVIDIA, que NVIDIA deja repartir dentro de una aplicación; sus condiciones (no venderlas sueltas, no hacer ingeniería
  inversa...) están en `licenses\NVIDIA-NOTICE.txt` y valen también para ti.
- **Intel XeSS y AMD FSR no los reparte ConsoleEnhancer**: los baja la app de sus releases oficiales, con sus licencias (XeSS: Intel
  Simplified Software License; FSR: MIT). Desde la 1.3 la app ya no usa ni baja Magpie ni mpv.
- **"DLSS 5" no se incluye.** `nvngx_dlssnr.dll` es una DLL de NVIDIA modificada por la comunidad, sin firma ni licencia, y
  no está en el repositorio ni en el zip, la app no la baja, y su uso puede ir contra las condiciones de NVIDIA. Si ya la tienes, puedes
  añadirla tú (bandeja > *Añadir DLSS 5*, que la copia a `extras\`) bajo tu responsabilidad: la app avisa de que no es oficial. Mira
  [DLSS 5 y la ley](#dlss-5-y-la-ley).
- **Autor**: ConsoleEnhancer lo crea y mantiene **IanRog29** (con las aportaciones de sus colaboradores). Copyright (C) 2026 IanRog29 y
  colaboradores de ConsoleEnhancer.
- **Donaciones**: voluntarias (Ko-fi); no dan acceso a nada extra ni compran DLL de terceros.
- **Marcas**: NVIDIA, GeForce RTX, DLSS, Reflex, AMD, FSR, FidelityFX, Intel, XeSS, Nintendo, PlayStation, Xbox, Windows y demás son de
  sus dueños; se nombran solo para describir compatibilidad. ConsoleEnhancer no está afiliado, patrocinado ni respaldado por ninguno.
  Usa NVIDIA DLSS, NVIDIA Reflex y NVIDIA Streamline.
- **Privacidad**: la app no manda nada a ningún sitio. Solo se conecta a internet para bajar XeSS y FSR de GitHub la primera vez.
- **Contenido**: muestra en tu pantalla la imagen de tu consola; no graba, no emite y no quita el HDCP.

## Para desarrolladores

**Un clic:** con el código bajado (`git clone` o el zip de "Code"), doble clic en **`preparar.bat`**. Baja las dependencias a
`terceros\` (versiones fijas, con su SHA-256), compila el lanzador y el motor, pasa las pruebas y deja `bin\ConsoleEnhancer.exe` listo
para abrir. `preparar.bat paquete` además monta el zip de la release en `dist\`. Solo hace falta Windows 10/11: se compila con el
`csc.exe` de .NET Framework 4.8 que trae Windows (C# 5), sin Visual Studio.

Paso a paso:

```bat
powershell -ExecutionPolicy Bypass -File scripts\descargar_dependencias.ps1   :: terceros\xess, terceros\fsr, terceros\streamline
build.bat                       :: bin\ConsoleEnhancer.exe (src\Lanzador + src\Deteccion) y bin\ listo (xess y fsr enlazados a terceros\)
build_motor.bat                 :: bin\engine\ConsoleEnhancerEngine.exe (src\Motor) + DLL de Streamline
powershell -ExecutionPolicy Bypass -File scripts\descargar_cabeceras_ngx.ps1   :: terceros\dlss_sdk (2 cabeceras de NGX / NGX headers)
build_prismanr.bat              :: bin\engine\PrismaNr.dll (C++, Visual Studio)
build_fsrfg.bat                 :: bin\engine\CeFsrFg.dll (C++, Visual Studio, git; AMD FidelityFX SDK v1.1.4 + parches\fidelityfx-fi-multi.patch)
tests\build_deteccion.bat       :: tests\bin\DeteccionTest.exe
tests\bin\DeteccionTest.exe --simula=todo --sin-d3d12 --salida=deteccion.txt   :: 23 PCs simulados
powershell -ExecutionPolicy Bypass -File scripts\empaquetar.ps1 -Zip -Version v1.3.3   :: dist\ConsoleEnhancer\ y dist\ConsoleEnhancer-v1.3.3.zip
```

- **Las piezas nativas propias del motor** (C++; Visual Studio 2022 o sus Build Tools con C++). `src\PrismaNr` necesita las dos
  cabeceras públicas de NGX de NVIDIA (`scripts\descargar_cabeceras_ngx.ps1`, versión fija y SHA-256; no van a git). `src\CeFsrFg`
  (FSR 3 FG ×3 / ×4): `build_fsrfg.bat` baja solo lo necesario del FidelityFX SDK de AMD en su etiqueta `v1.1.4` (comprobando que es el
  commit `c6efa6b`) a `obj\`, aplica `parches\fidelityfx-fi-multi.patch`, compila sus shaders con el compilador de shaders del propio SDK
  y enlaza la DLL; el SDK no va a git. La app va sin ellas (sin DLSS 5, FSR 3 FG solo ×2); el paquete necesita las dos (`preparar.bat`
  las compila si está Visual Studio).
- **Dependencias**: la tabla `scripts\components.json` (versión, URL, SHA-256 y qué se coge de cada zip) la usan
  `descargar_dependencias.ps1` y la propia app. Para cambiar de versión, se cambia ahí.
- **Paquete** (`scripts\empaquetar.ps1`): lista blanca. Lleva la app, el motor con Streamline / DLSS, scripts, languages, resources y
  licencias; **no** lleva XeSS ni FSR (los baja la app) ni nada no redistribuible, y comprueba que no hay datos del PC donde se monta
  (tu usuario, el nombre del equipo y lo que pongas en `scripts\privado.txt`, que no va a git).
- **GitHub Actions** (`.github/workflows/compilar.yml`): compila el lanzador, el motor, `PrismaNr.dll` y `CeFsrFg.dll` y pasa las
  pruebas en cada push, y rechaza cualquier binario o archivo no redistribuible en el repositorio (`nvngx_dlssnr`, `dlssg_sm86`...); con
  una etiqueta `v*` monta el zip y crea la Release con él y su `.sha256`.
- **Pruebas sin consola**:

| Argumento | Qué hace |
|---|---|
| `--datos=<carpeta>` | Otra carpeta de datos (ini, log, juegos): no toca los de verdad |
| `--asistente-auto` | Escribe el ini con el plan de la detección (escalas estimadas, imagen, mando), sin preguntar |
| `--simula=<caso>` | Con `--asistente-auto`: el plan de otro PC (`amd-rx7800xt`, `intel-arc-b580`, `rtx3060`, `sin-nvidia`...) |
| `--dry-run` | Apunta en el log la cadena y las líneas de órdenes, sin lanzar nada |
| `--componentes=<carpeta>` | Baja e instala XeSS y FSR en esa carpeta sin preguntar (o con los zip de `<carpeta>\downloads`) e informa en `componentes.log` |
| `--componentes-reparar` | Vuelve a bajar XeSS y FSR aunque estén bien (lo que hace bandeja > *Componentes: comprobar / volver a descargar*) |
| `--ui-preview=asistente;<carpeta>` | Imágenes de los 9 pasos del asistente (es / en), sin abrir nada |
| `--ui-preview=apagado;<carpeta>` | Imágenes del aviso de apagado |
| `--prueba-apagado[=<s>[;fuera]]` | La cuenta atrás de verdad; al acabar solo apunta "SIMULADO" en el log |
| `--prueba-espera` | Pasa ya al bajo consumo (pantalla apagada, con el vigilante); la consola, un mando o una tecla lo hacen volver |
| `--prueba-suspender` | Suspende ya el PC y abre la app al despertar |
| ini: `test_source=barras`, `apagar_pc=0` y en `motor_extra` `power_test=1 prueba_cuelgue_ms=4000` o `prueba_apagado_s=35 prueba_apagado_modo=corta` (o `nosignal`) | El motor con la imagen de prueba simula la gráfica parada 4 s (la app no debe cerrarse) o la consola apagada (sí) |
| `--iniciar-con-windows=1` / `=0` | Pone o quita el inicio con Windows y sale |
| `--test-consolas=<informe>` | Perfiles de consola y su detección con un reloj falso |
| `--test-tipos-generacion`, `--test-solo-motor`, `--test-motor-dlss5`, `--test-smooth-x4`, `--test-avisos`, `--test-menu-ajustes`, `--test-perfiles`, `--test-juegos-consola`, `--test-ps5`, `--test-deteccion`, `--test-nombres`, `--test-entrada`, `--test-home`, `--test-extras-guardados`, `--test-buscar-extras`, `--test-juego-en-marcha`, `--test-modo40`, `--test-negro-liso`, `--test-steam` (solo con una carpeta de Steam falsa) (`=<informe>`, con `--datos=<carpeta>`) | Pruebas sin la consola ni la gráfica: los tipos de generación y la migración de los perfiles viejos, lo que va al motor, DLSS 5, Smooth Motion ×4 quitado (uno guardado pasa a ×2), el modo 40, los avisos en pantalla contra lo que va de verdad, el menú de ajustes, los perfiles, los menús de las consolas y la detección del juego con capturas reales guardadas; código de salida = fallos |
| ini `test_source=<fichero>` y `test_motor=archivo:<fichero>` (`.y4m`, o `.mp4` / `.mkv` / `.webm` por Media Foundation) | El motor reproduce un vídeo como si fuera la capturadora (al mismo ritmo de 59,94, YUY2 al tamaño de la captura), para probar la app entera sin la consola; en `motor_extra`, `test_archivo_paso=N` (solo 1 de cada N imágenes del fichero llega al juego: con un vídeo de 60 y N=2 el juego va a 30 y la de en medio es la verdad de la inventada), `test_archivo_desde` / `_hasta` (segundos, en bucle), `_quieto`, `_codigo`, `_filtro`, `_hw`, `_registro` |
| `--restaurar-escritorio` / `--escritorio-negro` | El escritorio normal o el negro a mano |

**Estructura**

```
src\Lanzador\   la app de bandeja (C# 5, WinForms): el motor (imagen y sonido), mandos, telón, asistente, componentes, inicio y apagado,
                extras del usuario (Añadir / Quitar DLSS 5) y el enlace de apoyo (Extras.cs)
src\Deteccion\  detección de gráfica, pantallas, capturadoras, audio y mandos; el plan de imagen; PCs simulados
src\Motor\      el motor de imagen D3D12 (captura, DLSS / XeSS / FSR, generación, Enhanced FG, Reflex, OSD) y su modo solo de sonido (Audio\)
src\PrismaNr\   la pieza nativa del motor para "DLSS 5" (C++)
src\CeFsrFg\    FSR 3 FG ×3 / ×4: el envoltorio C de la interpolación del FidelityFX SDK 1.1.4 de AMD (C++; el SDK se baja para compilar)
scripts\        components.json, descargar_dependencias.ps1, empaquetar.ps1
resources\       base.ini (ajustes de partida), icono
languages\        en.txt (textos de pantalla en inglés)
parches\        parches de código fuente para código de terceros (parches\LEEME.txt; fidelityfx-fi-multi.patch para CeFsrFg)
tests\          DeteccionTest
DISENO.md       diseño y decisiones; LEEME.txt: estado, lo probado y lo que falta
```

Lee [`CONTRIBUTING.md`](CONTRIBUTING.md) antes de mandar cambios (C# 5, nada de binarios ni datos personales en git). Fallos de
seguridad: [`SECURITY.md`](SECURITY.md). Cambios por versión: [`CHANGELOG.md`](CHANGELOG.md).
