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
(Smooth Motion, XeSS FG, FSR 3 FG), **HDR** and **VRR**, and **as little latency as possible**. Everything is driven
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

1. Download the latest **`ConsoleEnhancer-v1.2.4.zip`** (or newer) from
   **[Releases](https://github.com/IanRog29/ConsoleEnhancer/releases)** (not the green "Code" button: that is the source code).
2. **Unzip** it into one of your own folders: Documents, the Desktop, `D:\Juegos`... (not in *Program Files*).
3. Open **`ConsoleEnhancer.exe`** and follow the wizard. Done: turn the console on.

First-run details:

- If Windows says *"Windows protected your PC"*: **More info > Run anyway** (the exe is not signed with a paid certificate; the source
  code is here for anyone who wants to review it).
- The first time it downloads **mpv**, **Intel XeSS** and **AMD FSR** (free components the app uses: the player and its engine's XeSS /
  FSR upscaling and frame generation) from their official pages: about 270 MB, once, with their SHA-256 hash checked. Click
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
| Frame generation **type** (XeSS FG, Smooth Motion, FSR 3 FG) | L3 | L3 | L3 | `Ctrl`+`G` |
| Dither filter (new / old) | hold L3 1 s | hold L3 1 s | hold L3 1 s | |
| **Capture** at 720p / 1080p / 1440p / 4K | D-pad ← ↑ → ↓ | D-pad ← ↑ → ↓ | D-pad ← ↑ → ↓ | `Ctrl`+`1` … `Ctrl`+`4` |
| Main / alternative **upscaler** | R3 | R3 | R3 | `Ctrl`+`R` |
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
- **Chain chosen by GPU** (`src\Deteccion\Plan.cs`): RTX 40 / 50, DLSS SR + Reflex + **Smooth Motion** ×2 (XeSS FG ×2 with the TV
  at 119.88 Hz or a driver without Smooth Motion; XeSS FG and FSR 3 FG in the selector); RTX 20/30, DLSS SR + Reflex (XeSS FG / FSR 3
  FG optional); without DLSS, **XeSS** on GTX and Intel Arc and **FSR 4** on AMD RX 6000 / 7000 / 9000 (official on RX 9000; on
  RX 6000 / 7000 through the FSR 4.1.1 unlock, experimental; otherwise FSR 3.1), plus whatever frame generation each one has (driver
  AFMF, FSR 3 FG, XeSS FG).
- **Built-in image engine** (D3D12, `src\Motor`), the path the game takes whenever it can (there is nothing to choose): DirectShow
  capture; upscaling with DLSS, XeSS, FSR 3.1 / FSR 4 (with Intel's and AMD's official DLLs, downloaded on first start) or FSR 1 (EASU +
  RCAS); frame generation ×2 with **Smooth Motion** (NVIDIA driver, RTX 40 / 50), **XeSS FG** and **FSR 3 FG on any GPU** (also with
  DLSS as the upscaler), all of them also with "DLSS 5"; Reflex / XeLL; and, optionally, NVIDIA RTX Dynamic Vibrance
  (`motor_extra=vibrancia=1`). Colour from YUY2 / NV12 capture cards is properly reconstructed, without blocky red edges (`croma=2`
  in `motor_extra`; `croma=0` = as before).
- **Scaling that adapts to your PC** (`escalas_adaptables=1`, on by default): the wizard estimates the DLSS model and percentages for
  your GPU and display; while you play, the engine measures GPU time every second and lowers quality if it can't keep up (within 2 s)
  or raises it little by little if there is headroom (after 20 s), and remembers it for next time (`data\scales_engine.txt`). Tray >
  Picture > *Self-adjusting scales* / *Forget the learned scales*. With the "DLSS 5" extra there is also an **emergency brake**: if
  the GPU is far over budget, it drops a pass or cuts the % at once (so the GPU doesn't choke) and keeps that as the learned value.
- **Checks that your frame rate is reached** (built-in engine): if the mode's frame rate (or the output with frame
  generation) is missed because of the PC (not the console game), it acts within seconds: first the DLSS 5 scale, then one pass less,
  a lighter DLSS model, a cheaper frame generation type, and only then DLSS 5 off, 120 → 60, a lower capture, 60 → 30 with frame
  generation. A notice says what changed ("60 fps not reached (52): DLSS 5 scale 50% -> 40%"). With GPU to spare for a few minutes, the
  DLSS 5 scale goes back up the next time you enter the game, never to a value that already failed. Remembered per game and GPU
  (`data\scales_auto.txt`).
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
  XeSS FG or FSR 3 FG on a 120 Hz G-SYNC display, the engine runs at a **fixed refresh rate** (G-SYNC is turned off only in the NVIDIA profile for its
  exe while you play like this; it is restored on exit, and if the app crashes, the next time it opens): with G-SYNC, Reflex capped the
  output at ~116 frames/s and ~2 console frames per second were lost; this way all of them come through, with less latency
  (`motor_60fg_refresco_fijo=auto`; at 144 Hz or more, at 119.88 Hz, without VRR or without NVIDIA nothing is changed).
- **Frame generation selector**: L3 while playing (or Ctrl+G on the keyboard over the picture, the settings menu or the tray) cycles
  through the types your GPU has (Smooth Motion, XeSS FG, FSR 3 FG) and remembers the choice per game. Only what can keep pace is
  offered. Frame generation is always ×2: since 1.2.4 there is no DLSS Frame Generation and no ×3 / ×4 (they didn't work well); game
  profiles and settings that used them switch by themselves to the closest type (on NVIDIA, Smooth Motion) and keep everything else.
- **120 fps mode (esports)**: for the console's 120 Hz games, it captures at 120 with no frame generation and the lowest latency. The
  mode button goes 30 → 60 → 120 only if the console outputs 120, the TV runs at **120 Hz or more** and the current capture is one your
  capture card provides at 120 (usually **1080p or 1440p**; the wizard reads it from the capture card, capturas_120). At 4K it stays
  30 ↔ 60: the app doesn't lower your resolution by itself. If it doesn't fit, a notice says why. permitir_120=auto (1 / 0 to force it /
  remove it).
- **Console menu at native 4K**: the Switch 2 HOME, the PS5 menu and the Xbox menu are recognised and shown with no upscaling or frame
  generation, and without "DLSS 5" (as is, with ~10 ms less latency; `motor_dlss5_home=0`); switching menu ↔ game takes under 1 s.
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
  lists everything you can change (capture, 30 / 60 / 120 mode, frame generation and its type, DLSS 5 with level and passes, and the
  upscaler, including each **DLSS model**: automatic, M, L, K, J, E, saved for all games, plus **FSR 1** and **No upscaling**, saved per game) with the current values marked. Move with
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
- **Components**: mpv, Intel XeSS and AMD FSR are downloaded once from their official pages with their SHA-256 checked (offline, from
  the zips in `downloads\`; if the app folder still has a `magpie\` folder from an older version, the Intel and AMD files are taken
  from there without downloading anything); tray > *Components: check / download again* repairs them.
- **Updates keep your data**: the first time a new version opens, it copies the settings, per-game profiles and recognised console from
  the previous version's folder (noted in `data\inherited.txt`), reuses the downloaded components and the compiled shaders (first start
  ~11 s instead of ~1 min) and applies the new general settings while keeping what the wizard chose (backup in `.antes-*`).
- **Portable**: with `portable.txt` next to the exe (included in the zip), everything is stored in `data\`; if you delete it, in
  `%LOCALAPPDATA%\ConsoleEnhancer`.
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
   ConsoleEnhancer closes (mpv and the engine with it) and Windows switches to a temporary low-power plan (*Power saver*
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
by itself. With your file, DLSS 5 runs inside the app's own engine (`engine\PrismaNr.dll`, which ships with the app since 1.2.4; 1 to 3
passes, with any frame generation type): much lower latency than through Magpie in 1.2.3 (measured then at 4K, 30 fps mode + frame
generation ×2: 43 ms instead of ~72 ms for the real frame; 1080p → 4K with 2 passes: 37 instead of ~52) and a steadier pace. There is
nothing to choose.

## DLSS 5 and the law

Short and neutral (this is what the licences say; it is not legal advice):

- **What ConsoleEnhancer ships**: its own code (GPL-3.0) and NVIDIA's official Streamline / DLSS SR / Reflex / RTX Dynamic Vibrance
  DLLs, unmodified, which NVIDIA allows apps to redistribute. It downloads mpv and Intel's and AMD's XeSS / FSR files from their
  official pages.
- **Since 1.2.4 the repository and the zip also carry `PrismaNr`**, the engine's own native piece (own code). It loads the file you
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

**The picture stays but there is no sound and the controller does nothing.** Microsoft Defender closed the app while mpv and the engine
kept showing the picture: see the previous answer (the app also explains it the next time it opens).

**The mpv / XeSS / FSR download fails.** Internet is needed once. Behind a proxy or offline: download the zips linked in that window
(also in `licenses\VERSIONS.txt`) on another PC and put them in a `downloads\` folder next to `ConsoleEnhancer.exe`; they are checked
the same way. If it says it can't write, the folder is read-only (Program Files): move it to one of your own.

**Black screen or "No Signal".** Check that the picture shows in the wizard's *Test picture*. Close any program using the capture card
(OBS, the vendor's software, Discord with the camera...). If the console encrypts its output (**HDCP**), the picture is black: turn it
off in the console (PS5: Settings > System > HDMI > *Enable HDCP*). ConsoleEnhancer does not remove copy protection. The HDCP warning
only shows with a truly black picture and a silent console for 20 s in a row (`hdcp_aviso_s`; `hdcp_aviso=0` turns it off): a dark
scene or a fade no longer triggers it.

**No sound.** In the wizard's sound step, try another input with *Test sound* (some capture cards provide audio through the video
device itself, others as a separate microphone).

**I hear the sound twice (echo).** Something else is playing the capture card at the same time as the app. Since 1.0.6 the app turns off
Windows *Listen to this device* on the capture card input while it plays through it (and restores it on exit), closes a leftover audio
player of its own from a previous session and shows an on-screen warning if RECentral, OBS (with monitoring), Streamlabs or XSplit are
also playing it. What it can't see: the capture card's HDMI pass-through output connected to a TV or monitor with speakers; turn its
volume down or use another output. `sonido_doble=0` in the ini leaves things as before.

**I don't get DLSS or frame generation.** DLSS only exists on NVIDIA GeForce RTX; Smooth Motion on RTX 40/50; frame generation needs a
100 Hz or faster display. Other GPUs get XeSS or FSR 4 / 3.1 and whatever frame generation they have. Update the driver.

**It stutters or lags.** Run the wizard again (with the display and the capture card at their proper resolution), close programs that
draw in the background and, if the TV has one, turn on game mode. The log says which chain was chosen.

**"Recovering the picture" shows up.** The picture froze with the console on (almost always the GPU: driver, heavy load) and the app
reopens it by itself; you don't need to do anything. If it happens often, update the driver and remove the GPU *overclock*. The log says
why ("NO es la consola apagada: ...").

**Black screens of a few seconds, and `DXGI_ERROR_DEVICE_HUNG` / "grafica perdida" in `engine.log`.** That is the graphics card
resetting under load (Windows logs it as `nvlddmkm` event 153 in *Event Viewer > System*). A **video memory overclock** (for example a
memory clock offset in MSI Afterburner) can do it with DLSS 5 plus frame generation even when games look fine: set the memory clock back
to stock and test again. Since 1.2.4 the app keeps the last picture on screen while it recovers, and if it happens with XeSS FG, the
rest of the session uses another frame generation type in the engine.

**My desktop is stuck black.** `ConsoleEnhancer.exe --restaurar-escritorio`.

**Where are the settings and the log?** In `data\` next to the exe (portable) or in `%LOCALAPPDATA%\ConsoleEnhancer`:
`consoleenhancer.ini` and `logs\consoleenhancer.log` / `engine.log`. If you open an *issue*, attach the log (check first that it doesn't
contain anything of yours you don't want to show, such as folder names).

**What about "DLSS 5"?** It is a community-modified NVIDIA DLL: **it is not included, not downloaded and not recommended**. The app
works fully without it; if you already have it, see [DLSS 5 (optional)](#dlss-5-optional).

**Is mpv, XeSS or FSR broken?** Tray icon > **Components: check / download again**: it says what is missing and downloads them again from
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
- **What is in the zip**: the app (with the engine's own `PrismaNr.dll`, see [DLSS 5 and the law](#dlss-5-and-the-law))
  and the NVIDIA Streamline / DLSS / Reflex / RTX Dynamic Vibrance DLLs, unmodified and signed by NVIDIA, which NVIDIA allows to be
  redistributed inside an application; their terms (no selling them on their own, no reverse engineering...) are in
  `licenses\NVIDIA-NOTICE.txt` and also apply to you.
- **mpv, Intel XeSS and AMD FSR are not redistributed by ConsoleEnhancer**: the app downloads them from their official releases, with
  their licenses (mpv: GPL, with its source code on its page; XeSS: Intel Simplified Software License; FSR: MIT).
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
- **Privacy**: the app doesn't send anything anywhere. It only connects to the internet to download mpv, XeSS and FSR from GitHub the
  first time.
- **Content**: it shows your console's picture on your screen; it doesn't record, doesn't stream and doesn't remove HDCP.

## For developers

**One click:** with the code downloaded (`git clone` or the "Code" zip), double-click **`preparar.bat`**. It downloads the dependencies
to `terceros\` (pinned versions, with their SHA-256), builds the launcher and the engine, runs the tests and leaves
`bin\ConsoleEnhancer.exe` ready to open. `preparar.bat paquete` also builds the release zip in `dist\`. Only Windows 10/11 is needed:
it is built with the `csc.exe` of .NET Framework 4.8 that ships with Windows (C# 5), no Visual Studio.

Step by step:

```bat
powershell -ExecutionPolicy Bypass -File scripts\descargar_dependencias.ps1   :: terceros\mpv, terceros\xess, terceros\fsr, terceros\streamline
build.bat                       :: bin\ConsoleEnhancer.exe (src\Lanzador + src\Deteccion) y bin\ listo (mpv, xess y fsr enlazados a terceros\)
build_motor.bat                 :: bin\engine\ConsoleEnhancerEngine.exe (src\Motor) + DLL de Streamline
powershell -ExecutionPolicy Bypass -File scripts\descargar_cabeceras_ngx.ps1   :: terceros\dlss_sdk (2 cabeceras de NGX / NGX headers)
build_prismanr.bat              :: bin\engine\PrismaNr.dll (C++, Visual Studio)
tests\build_deteccion.bat       :: tests\bin\DeteccionTest.exe
tests\bin\DeteccionTest.exe --simula=todo --sin-d3d12 --salida=deteccion.txt   :: 23 PCs simulados
powershell -ExecutionPolicy Bypass -File scripts\empaquetar.ps1 -Zip -Version v1.2.4   :: dist\ConsoleEnhancer\ y dist\ConsoleEnhancer-v1.2.4.zip
```

- **The engine's own native piece** (`src\PrismaNr`, C++): it needs Visual Studio 2022 (or its Build Tools) with C++ and NVIDIA's two
  public NGX headers (`scripts\descargar_cabeceras_ngx.ps1`, pinned version and SHA-256; not in git). The app runs without it; the
  package requires it (`preparar.bat` builds it when Visual Studio is there).
- **Dependencies**: the table `scripts\components.json` (version, URL, SHA-256 and what is taken from each zip) is used by
  `descargar_dependencias.ps1` and by the app itself. To change a version, change it there.
- **Package** (`scripts\empaquetar.ps1`): allow-list. It includes the app, the engine with Streamline / DLSS, scripts, languages,
  resources and licenses; it does **not** include mpv, XeSS or FSR (the app downloads them) or anything non-redistributable, and it checks
  that there is no data from the PC it is built on (your user name, the computer name and whatever you put in `scripts\privado.txt`,
  which is not in git).
- **GitHub Actions** (`.github/workflows/compilar.yml`): builds and runs the tests on every push; with a `v*` tag it builds the zip and
  creates the Release with it and its `.sha256`.
- **Testing without a console**:

| Argument | What it does |
|---|---|
| `--datos=<carpeta>` | A different data folder (ini, log, games): doesn't touch the real ones |
| `--asistente-auto` | Writes the ini with the detection plan (estimated scales, picture, controller), without asking |
| `--simula=<caso>` | With `--asistente-auto`: another PC's plan (`amd-rx7800xt`, `intel-arc-b580`, `rtx3060`, `sin-nvidia`...) |
| `--dry-run` | Writes the chain and the command lines to the log, without launching anything |
| `--componentes=<carpeta>` | Downloads and installs mpv, XeSS and FSR in that folder without asking (or from the zips in `<carpeta>\downloads`) and reports in `componentes.log` |
| `--componentes-reparar` | Downloads mpv, XeSS and FSR again even if they are fine (what tray > *Components: check / download again* does) |
| `--ui-preview=asistente;<carpeta>` | Images of the wizard's 9 steps (es / en), without opening anything |
| `--ui-preview=apagado;<carpeta>` | Images of the shutdown notice |
| `--prueba-apagado[=<s>[;fuera]]` | The real countdown; when it ends it only writes "SIMULADO" to the log |
| `--prueba-espera` | Goes into the low-power mode now (screen off, the watcher running); the console, a controller or a key brings it back |
| `--prueba-suspender` | Sleeps the PC now and opens the app when it wakes |
| ini: `test_source=barras`, `apagar_pc=0` and in `motor_extra` `power_test=1 prueba_cuelgue_ms=4000` or `prueba_apagado_s=35 prueba_apagado_modo=corta` (or `nosignal`) | The engine, with the test picture, simulates the GPU stalling for 4 s (the app must not close) or the console turning off (it must) |
| `--iniciar-con-windows=1` / `=0` | Adds or removes start with Windows and exits |
| `--test-consolas=<informe>` | Console profiles and their detection with a fake clock |
| `--restaurar-escritorio` / `--escritorio-negro` | The normal or the black desktop, by hand |

**Layout**

```
src\Lanzador\   the tray app (C# 5, WinForms): mpv, the engine, controllers, curtain, wizard, components, start-up and shutdown,
                user extras (Add / Remove DLSS 5) and the support link (Extras.cs)
src\Deteccion\  detection of GPU, displays, capture cards, audio and controllers; the picture plan; simulated PCs
src\Motor\      the D3D12 image engine (capture, DLSS / XeSS / FSR, frame generation, Reflex, OSD)
scripts\        mpv lua and glsl, components.json, descargar_dependencias.ps1, empaquetar.ps1
resources\       base.ini (starting settings), mpv.conf (mpv portable_config), icon
languages\        en.txt (English on-screen texts)
parches\        source code patches for third-party code, optional (parches\LEEME.txt)
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
imagen llega **reescalada a 4K** (DLSS, FSR, XeSS), con **generación de fotogramas** si tu gráfica puede (Smooth Motion, XeSS FG,
FSR 3 FG), **HDR** y **VRR**, y con el **menor retraso posible**. Todo se maneja **con el mando** desde el
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

1. Baja el último **`ConsoleEnhancer-v1.2.4.zip`** (o uno más nuevo) de
   **[Releases](https://github.com/IanRog29/ConsoleEnhancer/releases)** (no el botón verde "Code": eso es el código fuente).
2. **Descomprímelo** en una carpeta tuya: Documentos, el Escritorio, `D:\Juegos`... (no en *Archivos de programa*).
3. Abre **`ConsoleEnhancer.exe`** y sigue el asistente. Ya está: enciende la consola.

Detalles de la primera vez:

- Si Windows dice *"Windows protegió su PC"*: **Más información > Ejecutar de todas formas** (el exe no está firmado con un certificado
  de pago; el código está aquí para quien quiera revisarlo).
- La primera vez baja **mpv**, **Intel XeSS** y **AMD FSR** (componentes libres que usa la app: el reproductor y el reescalado y la
  generación XeSS / FSR de su motor) de sus páginas oficiales: unos 270 MB, una sola vez, comprobando su huella SHA-256. Pulsa
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
| **Tipo** de generación (XeSS FG, Smooth Motion, FSR 3 FG) | L3 | L3 | L3 | `Ctrl`+`G` |
| Filtro de tramado (nuevo / viejo) | mantener L3 1 s | mantener L3 1 s | mantener L3 1 s | |
| **Captura** a 720p / 1080p / 1440p / 4K | cruceta ← ↑ → ↓ | cruceta ← ↑ → ↓ | cruceta ← ↑ → ↓ | `Ctrl`+`1` … `Ctrl`+`4` |
| **Escalador** principal / alternativo | R3 | R3 | R3 | `Ctrl`+`R` |
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
- **Cadena según la gráfica** (`src\Deteccion\Plan.cs`): RTX 40 / 50, DLSS SR + Reflex + **Smooth Motion** ×2 (XeSS FG ×2 con la tele
  a 119,88 Hz o un driver sin Smooth Motion; XeSS FG y FSR 3 FG en el selector); RTX 20/30, DLSS SR + Reflex (XeSS FG / FSR 3 FG
  opcionales); sin DLSS, **XeSS** en GTX e Intel Arc y **FSR 4** en AMD RX 6000 / 7000 / 9000 (oficial en las RX 9000; en RX 6000 /
  7000 con el desbloqueo de FSR 4.1.1, experimental; si no, FSR 3.1), y la generación que tenga cada una (AFMF del driver, FSR 3 FG,
  XeSS FG).
- **Motor de imagen propio** (D3D12, `src\Motor`), el camino del juego siempre que se pueda (no hay nada que elegir): captura
  DirectShow; escalado con DLSS, XeSS, FSR 3.1 / FSR 4 (con las DLL oficiales de Intel y AMD, que se bajan la primera vez) o FSR 1
  (EASU + RCAS); generación de fotogramas ×2 con **Smooth Motion** (driver de NVIDIA, RTX 40 / 50), **XeSS FG** y **FSR 3 FG en
  cualquier gráfica** (también con DLSS de escalador), todas también con "DLSS 5"; Reflex / XeLL; y, si quieres, la Vibrancia RTX de
  NVIDIA (`motor_extra=vibrancia=1`). El color de las capturadoras YUY2 / NV12 se reconstruye bien, sin bordes rojos en escalones
  (`croma=2` en `motor_extra`; `croma=0` = como antes).
- **Escalas que se adaptan a tu PC** (`escalas_adaptables=1`, de serie): el asistente estima el modelo de DLSS y los porcentajes para tu
  gráfica y tu pantalla; jugando, el motor mide el tiempo de gráfica cada segundo y baja la calidad si no llega (en 2 s) o la sube poco
  a poco si sobra (tras 20 s), y lo recuerda para la próxima vez (`data\scales_engine.txt`). Bandeja > Imagen > *Escalas que se
  adaptan solas* / *Olvidar las escalas aprendidas*. Con el extra "DLSS 5" hay además un **freno de emergencia**: si la gráfica va muy
  por encima de lo que cabe, quita una pasada o baja el % de golpe (para no atascar la gráfica) y eso queda como lo aprendido.
- **Comprueba que se llegan los fps** (motor propio): si no se llega a los fps del modo (o a los de salida con la
  generación) por culpa del PC (no del juego de la consola), actúa en segundos: primero la escala de DLSS 5, luego una pasada menos, un
  modelo de DLSS más ligero, una generación más barata y solo después DLSS 5 fuera, 120 → 60, captura más baja, 60 → 30 con generación.
  Un aviso dice qué ha cambiado ("No se llega a 60 fps (52) · DLSS 5 al 50 % → 40 %"). Si sobra gráfica unos minutos, la escala de
  DLSS 5 vuelve a subir la próxima vez que entres al juego, nunca a un valor que ya falló. Se recuerda por juego y gráfica
  (`data\scales_auto.txt`).
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
  (`motor_vrr_fg_vsync=-1`); solo si la pantalla dice tener VRR, para no dar cortes en una que no lo tenga. En el modo 60 con XeSS FG o
  FSR 3 FG y una pantalla de 120 Hz con G-SYNC, el motor va con **refresco fijo** (G-SYNC apagado solo en el perfil de NVIDIA de su exe mientras
  juegas así; se quita al salir, y si la app se cae, al volver a abrirla): con G-SYNC, Reflex limita la salida a ~116 imágenes/s y se
  perdían ~2 imágenes de la consola por segundo; así salen todas y con menos retraso (`motor_60fg_refresco_fijo=auto`; con 144 Hz o
  más, a 119,88 Hz, sin VRR o sin NVIDIA no se toca nada).
- **Selector de generación de fotogramas**: L3 jugando (o Ctrl+G con el teclado sobre la imagen, el menú de ajustes o la bandeja)
  recorre los tipos que tiene tu gráfica (Smooth Motion, XeSS FG, FSR 3 FG) y lo recuerda por juego. Solo se ofrece lo que llega a
  ritmo. La generación es siempre ×2: desde la 1.2.4 no hay DLSS Frame Generation ni ×3 / ×4 (no iban bien); los perfiles y ajustes que
  los usaban pasan solos al tipo más parecido (en NVIDIA, Smooth Motion) y conservan todo lo demás.
- **Modo 120 fps (esports)**: para los juegos de 120 Hz de la consola, captura a 120 sin generación de fotogramas y con el menor
  retraso. El botón de modo pasa 30 → 60 → 120 solo si la consola saca 120, la tele va a **120 Hz o más** y la captura de ahora es una
  que tu capturadora da a 120 (normalmente **1080p o 1440p**; el asistente lo lee de la capturadora, capturas_120). A 4K sigue 30 ↔ 60:
  la app no te baja la resolución sola. Si no cabe, un aviso dice por qué. permitir_120=auto (1 / 0 para forzarlo / quitarlo).
- **Menú de la consola a 4K nativo**: el HOME de la Switch 2, el menú de la PS5 y el de Xbox se reconocen y van sin escalado ni
  generación, y sin "DLSS 5" (tal cual, con ~10 ms menos de retraso; `motor_dlss5_home=0`); los cambios menú ↔ juego tardan menos de 1 s.
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
  Muestra todo lo que se puede cambiar (captura, modo 30 / 60 / 120, generación de fotogramas y su tipo, DLSS 5 con nivel y pasadas, y
  el escalador, con cada **modelo de DLSS**: automático, M, L, K, J, E, guardado para todos los juegos, y **FSR 1** y **Sin reescalado**, guardados por juego) con lo de ahora marcado. Se
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
- **Componentes**: mpv, Intel XeSS y AMD FSR se bajan una vez de sus páginas oficiales comprobando su SHA-256 (sin internet, con los
  zip en `downloads\`; si en la carpeta de la app queda un `magpie\` de una versión anterior, de ahí salen los archivos de Intel y AMD
  sin bajar nada); bandeja > *Componentes: comprobar / volver a descargar* los repara.
- **Al actualizar no pierdes nada**: la primera vez que se abre una versión nueva, copia los ajustes, los perfiles de cada juego y la
  consola reconocida de la carpeta de la versión anterior (se apunta en `data\inherited.txt`), reutiliza los componentes ya bajados y
  los shaders ya compilados (primer arranque de ~1 min a ~11 s) y pone los ajustes generales nuevos sin tocar lo que eligió el asistente
  (copia en `.antes-*`).
- **Portable**: con `portable.txt` junto al exe (viene en el zip), todo se guarda en `data\`; si lo borras, en
  `%LOCALAPPDATA%\ConsoleEnhancer`.
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
   pantalla se apaga, ConsoleEnhancer se cierra (con mpv y el motor) y Windows pasa a un plan de bajo consumo temporal
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
carpeta los recoge sola. Con tu archivo, DLSS 5 va dentro del motor propio (`engine\PrismaNr.dll`, que viene con la app desde la 1.2.4;
1 a 3 pasadas, con cualquier tipo de generación): mucho menos retraso que por Magpie en la 1.2.3 (medido entonces a 4K, modo 30 +
generación ×2: 43 ms en vez de ~72 la imagen real; 1080p → 4K con 2 pasadas: 37 en vez de ~52) y un ritmo más regular. No hay nada que
elegir.

## DLSS 5 y la ley

En corto y neutro (es lo que dicen las licencias; no es asesoramiento legal):

- **Lo que reparte ConsoleEnhancer**: su propio código (GPL-3.0) y las DLL oficiales de NVIDIA de Streamline / DLSS SR / Reflex /
  Vibrancia RTX, sin cambios, que NVIDIA deja repartir dentro de una app. mpv y los archivos de XeSS / FSR de Intel y AMD los baja de
  sus páginas oficiales.
- **Desde la 1.2.4 el repositorio y el zip llevan también `PrismaNr`**, la pieza nativa propia del motor (código propio). Carga el
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

**Se queda la imagen pero sin sonido y el mando no hace nada.** Microsoft Defender cerró la app mientras mpv y el motor seguían
enseñando la imagen: mira la respuesta anterior (la app también lo explica la próxima vez que se abre).

**La descarga de mpv / XeSS / FSR falla.** Hace falta internet una vez. Con proxy o sin conexión: baja los zip que enlaza la ventana
(también en `licenses\VERSIONS.txt`) en otro PC y ponlos en una carpeta `downloads\` junto a `ConsoleEnhancer.exe`; se comprueban
igual. Si dice que no puede escribir, la carpeta es de solo lectura (Archivos de programa): muévela a una tuya.

**Pantalla negra o "No Signal".** Comprueba que la imagen se ve en el *Probar imagen* del asistente. Cierra cualquier programa que use
la capturadora (OBS, el software del fabricante, Discord con la cámara...). Si la consola cifra la salida (**HDCP**), la imagen sale
negra: apágalo en la consola (PS5: Ajustes > Sistema > HDMI > *Activar HDCP*). ConsoleEnhancer no quita protecciones anticopia.
El aviso de HDCP solo sale con la imagen negra de verdad y la consola en silencio 20 s seguidos (`hdcp_aviso_s`; `hdcp_aviso=0` lo
quita): una escena oscura o un fundido ya no lo hacen saltar.

**No hay sonido.** En el asistente, paso del sonido: prueba otra entrada con *Probar sonido* (algunas capturadoras dan el audio por el
propio dispositivo de vídeo y otras como un micrófono aparte).

**El sonido se oye dos veces (eco).** Algo más reproduce la capturadora a la vez que la app. Desde la 1.0.6 la app quita sola
*Escuchar este dispositivo* de Windows en la entrada de la capturadora mientras suena por ella (y lo devuelve al salir), cierra un audio
suyo que se quedara abierto de una sesión anterior y avisa en pantalla si RECentral, OBS (con monitorización), Streamlabs o XSplit
también suenan. Lo que no puede ver: la salida HDMI de paso de la capturadora conectada a una tele o un monitor con altavoces; baja
su volumen o usa otra salida. `sonido_doble=0` en el ini lo deja como antes.

**No me sale DLSS o la generación de fotogramas.** DLSS solo existe en NVIDIA GeForce RTX; Smooth Motion en RTX 40/50; la generación
necesita una pantalla de 100 Hz o más. Con otras gráficas va XeSS o FSR 4 / 3.1 y la generación que tenga tu gráfica. Actualiza el driver.

**Va a tirones o con retraso.** Abre el asistente otra vez (con la pantalla y la capturadora a su resolución buena), cierra programas que
pinten de fondo y, si la tele lo tiene, pon el modo juego. El log dice qué cadena se eligió.

**Sale "Recuperando la imagen".** La imagen se paró con la consola encendida (casi siempre la gráfica: driver, mucha carga) y la app
la vuelve a abrir sola; no hace falta tocar nada. Si pasa a menudo, actualiza el driver y quita el *overclock* de la gráfica. El log
dice el motivo ("NO es la consola apagada: ...").

**Pantallazos negros de unos segundos y `DXGI_ERROR_DEVICE_HUNG` / "grafica perdida" en `engine.log`.** Es la gráfica reiniciándose con
la carga (Windows lo apunta como el evento 153 de `nvlddmkm` en *Visor de eventos > Sistema*). Un **overclock de la memoria de la
gráfica** (por ejemplo, subir el reloj de la memoria en MSI Afterburner) puede provocarlo con DLSS 5 y la generación de fotogramas aunque
los juegos vayan bien: deja la memoria a su velocidad de serie y vuelve a probar. Desde la 1.2.4 la app deja la última imagen en pantalla
mientras se recupera y, si pasa con XeSS FG, el resto de la sesión va con otro tipo de generación en el motor.

**Se me ha quedado el escritorio en negro.** `ConsoleEnhancer.exe --restaurar-escritorio`.

**¿Dónde están los ajustes y el log?** En `data\` junto al exe (portable) o en `%LOCALAPPDATA%\ConsoleEnhancer`:
`consoleenhancer.ini` y `logs\consoleenhancer.log` / `engine.log`. Si abres un *issue*, adjunta el log (revisa antes que no lleve nada
tuyo que no quieras enseñar, como nombres de carpetas).

**¿Y "DLSS 5"?** Es una DLL de NVIDIA modificada por la comunidad: **no viene, no se baja y no se recomienda**. La app funciona
entera sin ella; si ya la tienes, mira [DLSS 5 (opcional)](#dlss-5-opcional).

**¿Se ha estropeado mpv, XeSS o FSR?** Icono de la bandeja > **Componentes: comprobar / volver a descargar**: dice qué falta y los vuelve a
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
- **Lo que va en el zip**: la app (con `PrismaNr.dll` del motor, ver [DLSS 5 y la ley](#dlss-5-y-la-ley)) y las DLL
  de NVIDIA Streamline / DLSS / Reflex / Vibrancia RTX, sin cambios y firmadas por NVIDIA, que
  NVIDIA deja repartir dentro de una aplicación; sus condiciones (no venderlas sueltas, no hacer ingeniería inversa...) están en
  `licenses\NVIDIA-NOTICE.txt` y valen también para ti.
- **mpv, Intel XeSS y AMD FSR no los reparte ConsoleEnhancer**: los baja la app de sus releases oficiales, con sus licencias (mpv: GPL,
  con su código fuente en su página; XeSS: Intel Simplified Software License; FSR: MIT).
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
- **Privacidad**: la app no manda nada a ningún sitio. Solo se conecta a internet para bajar mpv, XeSS y FSR de GitHub la primera vez.
- **Contenido**: muestra en tu pantalla la imagen de tu consola; no graba, no emite y no quita el HDCP.

## Para desarrolladores

**Un clic:** con el código bajado (`git clone` o el zip de "Code"), doble clic en **`preparar.bat`**. Baja las dependencias a
`terceros\` (versiones fijas, con su SHA-256), compila el lanzador y el motor, pasa las pruebas y deja `bin\ConsoleEnhancer.exe` listo
para abrir. `preparar.bat paquete` además monta el zip de la release en `dist\`. Solo hace falta Windows 10/11: se compila con el
`csc.exe` de .NET Framework 4.8 que trae Windows (C# 5), sin Visual Studio.

Paso a paso:

```bat
powershell -ExecutionPolicy Bypass -File scripts\descargar_dependencias.ps1   :: terceros\mpv, terceros\xess, terceros\fsr, terceros\streamline
build.bat                       :: bin\ConsoleEnhancer.exe (src\Lanzador + src\Deteccion) y bin\ listo (mpv, xess y fsr enlazados a terceros\)
build_motor.bat                 :: bin\engine\ConsoleEnhancerEngine.exe (src\Motor) + DLL de Streamline
powershell -ExecutionPolicy Bypass -File scripts\descargar_cabeceras_ngx.ps1   :: terceros\dlss_sdk (2 cabeceras de NGX / NGX headers)
build_prismanr.bat              :: bin\engine\PrismaNr.dll (C++, Visual Studio)
tests\build_deteccion.bat       :: tests\bin\DeteccionTest.exe
tests\bin\DeteccionTest.exe --simula=todo --sin-d3d12 --salida=deteccion.txt   :: 23 PCs simulados
powershell -ExecutionPolicy Bypass -File scripts\empaquetar.ps1 -Zip -Version v1.2.4   :: dist\ConsoleEnhancer\ y dist\ConsoleEnhancer-v1.2.4.zip
```

- **La pieza nativa propia del motor** (`src\PrismaNr`, C++): necesita Visual Studio 2022 (o sus Build Tools) con C++ y las dos
  cabeceras públicas de NGX de NVIDIA (`scripts\descargar_cabeceras_ngx.ps1`, versión fija y SHA-256; no van a git). La app va sin
  ella; el paquete la necesita (`preparar.bat` la compila si está Visual Studio).
- **Dependencias**: la tabla `scripts\components.json` (versión, URL, SHA-256 y qué se coge de cada zip) la usan
  `descargar_dependencias.ps1` y la propia app. Para cambiar de versión, se cambia ahí.
- **Paquete** (`scripts\empaquetar.ps1`): lista blanca. Lleva la app, el motor con Streamline / DLSS, scripts, languages, resources y
  licencias; **no** lleva mpv, XeSS ni FSR (los baja la app) ni nada no redistribuible, y comprueba que no hay datos del PC donde se monta
  (tu usuario, el nombre del equipo y lo que pongas en `scripts\privado.txt`, que no va a git).
- **GitHub Actions** (`.github/workflows/compilar.yml`): compila y pasa las pruebas en cada push; con una etiqueta `v*` monta el zip y
  crea la Release con él y su `.sha256`.
- **Pruebas sin consola**:

| Argumento | Qué hace |
|---|---|
| `--datos=<carpeta>` | Otra carpeta de datos (ini, log, juegos): no toca los de verdad |
| `--asistente-auto` | Escribe el ini con el plan de la detección (escalas estimadas, imagen, mando), sin preguntar |
| `--simula=<caso>` | Con `--asistente-auto`: el plan de otro PC (`amd-rx7800xt`, `intel-arc-b580`, `rtx3060`, `sin-nvidia`...) |
| `--dry-run` | Apunta en el log la cadena y las líneas de órdenes, sin lanzar nada |
| `--componentes=<carpeta>` | Baja e instala mpv, XeSS y FSR en esa carpeta sin preguntar (o con los zip de `<carpeta>\downloads`) e informa en `componentes.log` |
| `--componentes-reparar` | Vuelve a bajar mpv, XeSS y FSR aunque estén bien (lo que hace bandeja > *Componentes: comprobar / volver a descargar*) |
| `--ui-preview=asistente;<carpeta>` | Imágenes de los 9 pasos del asistente (es / en), sin abrir nada |
| `--ui-preview=apagado;<carpeta>` | Imágenes del aviso de apagado |
| `--prueba-apagado[=<s>[;fuera]]` | La cuenta atrás de verdad; al acabar solo apunta "SIMULADO" en el log |
| `--prueba-espera` | Pasa ya al bajo consumo (pantalla apagada, con el vigilante); la consola, un mando o una tecla lo hacen volver |
| `--prueba-suspender` | Suspende ya el PC y abre la app al despertar |
| ini: `test_source=barras`, `apagar_pc=0` y en `motor_extra` `power_test=1 prueba_cuelgue_ms=4000` o `prueba_apagado_s=35 prueba_apagado_modo=corta` (o `nosignal`) | El motor con la imagen de prueba simula la gráfica parada 4 s (la app no debe cerrarse) o la consola apagada (sí) |
| `--iniciar-con-windows=1` / `=0` | Pone o quita el inicio con Windows y sale |
| `--test-consolas=<informe>` | Perfiles de consola y su detección con un reloj falso |
| `--restaurar-escritorio` / `--escritorio-negro` | El escritorio normal o el negro a mano |

**Estructura**

```
src\Lanzador\   la app de bandeja (C# 5, WinForms): mpv, el motor, mandos, telón, asistente, componentes, inicio y apagado,
                extras del usuario (Añadir / Quitar DLSS 5) y el enlace de apoyo (Extras.cs)
src\Deteccion\  detección de gráfica, pantallas, capturadoras, audio y mandos; el plan de imagen; PCs simulados
src\Motor\      el motor de imagen D3D12 (captura, DLSS / XeSS / FSR, generación, Reflex, OSD)
scripts\        lua y glsl de mpv, components.json, descargar_dependencias.ps1, empaquetar.ps1
resources\       base.ini (ajustes de partida), mpv.conf (portable_config de mpv), icono
languages\        en.txt (textos de pantalla en inglés)
parches\        parches de código fuente para código de terceros, opcionales (parches\LEEME.txt)
tests\          DeteccionTest
DISENO.md       diseño y decisiones; LEEME.txt: estado, lo probado y lo que falta
```

Lee [`CONTRIBUTING.md`](CONTRIBUTING.md) antes de mandar cambios (C# 5, nada de binarios ni datos personales en git). Fallos de
seguridad: [`SECURITY.md`](SECURITY.md). Cambios por versión: [`CHANGELOG.md`](CHANGELOG.md).
