-- ce_power.lua (ConsoleEnhancer; antes switch2_power.lua de Switch2Magpie)
-- Detecta que la consola se ha apagado o esta en reposo (se pierde la señal HDMI de la capturadora; 18-11: cualquier consola)
-- y sale de mpv con código 42 tras escribir un fichero "flag".
--
-- Señales que se vigilan (una vez por segundo):
--   stall  : time-pos deja de avanzar (no llegan frames)
--   black  : imagen negra (muestreo de luma en rejilla vía screenshot-raw)
--   silent : audio en silencio (af-metadata/s2lvl, filtro astats)
--   frozen : imagen idéntica (huella de la rejilla) durante mucho tiempo
--   nosig  : la pantalla "No Signal" de la capturadora (consola en espera o apagada). 09-11: se mira en
--            cada imagen que toma el script (las capturas del lanzador incluidas); vista, se confirma con
--            otra mirada a los 0,2 s y se avisa al lanzador (switch2-poweroff), que cierra mpv sin esperar
--            a su cierre. Negro / congelada / "No Signal" en silencio usan las capturas del lanzador
--            (ya no una imagen propia cada 3 s, que paraba mpv 80-107 ms). 16-11: confirmada, se espera
--            nosig_hold_ms (6 s; arrancando un juego nosig_hold_launch_ms, 25 s; tras el menu de apagado, nada)
--            con el aviso "Sin señal de la consola" encima: un cambio de modo de video vuelve antes y se sigue
--            16-11 tarde: 3 s jugando; en el HOME sin nada arrancando, sin esperar (el lanzador manda switch2-powermenu cada 0,5 s);
--            la espera de cada "No Signal" se decide al verla y solo puede crecer
--
-- Filtro de audio esperado (lo pone el lanzador):
--   --af=@s2lvl:lavfi=[astats=metadata=1:reset=1:measure_perchannel=none:measure_overall=RMS_level]
--
-- Avisos (15-11, Prisma): el lanzador manda "script-message switch2-osd <ms> <texto> [warn]" (antes show-text) y aquí se
-- dibujan como una pastilla oscura arriba en el centro, igual que los del motor propio y los de la app.
--
-- Teclas: Ctrl+p activa/desactiva la detección, Ctrl+Shift+p activa/desactiva debug.

local mp = require "mp"
local msg = require "mp.msg"
local options = require "mp.options"

local o = {
    flag = (os.getenv("TEMP") or ".") .. "\\consoleenhancer_poweroff.flag",
    arm_secs = 10,       -- segundos de señal real (no negra) antes de armar
    stall_secs = 10,     -- sin frames nuevos
    black_secs = 30,     -- negro + silencio (pantallas de carga negras duran menos)
    frozen_secs = 180,   -- imagen congelada + silencio (menús de pausa sin música)
    nosig_secs = 4,      -- "No Signal" tras haber visto la consola, aunque no esté armada
                         -- (apagarla a los pocos segundos de encenderla no cerraba la app)
    black_luma = 18,     -- umbral de luma media (0-255)
    silence_db = -70,    -- umbral RMS en dBFS
    grid_w = 48,         -- puntos de muestreo horizontales
    grid_h = 27,         -- puntos de muestreo verticales
    probe_secs = 3,      -- en silencio, mirar la imagen cada N s
    silence_before_probe = 10, -- segundos de silencio antes de empezar a mirar la imagen
    quit_delay = 0,      -- segundos que se muestra el OSD antes de salir (0 = cerrar ya)
    hard_exit_secs = 1.5, -- si mpv no ha salido tras "quit", forzar os.exit (0 = nunca). Con la consola apagada la
                         -- capturadora suele colgar el cierre: 1,5 s (antes 4)
    exit_code = 42,
    -- 09-11: consola en espera más rápido. Antes "No Signal" solo se miraba en las capturas del lanzador (cada
    -- 1,2 s jugando, 3 seguidas: 2,4-3,6 s) o tras 10 s de silencio
    nosig_fast_secs = 0,      -- con el audio recién en silencio: buscar "No Signal" durante estos segundos (0 = no; de
                              -- serie no: serían imágenes de más mientras se juega; ~0,45 s antes jugando con 3)
    nosig_fast_every = 0.3,   -- cada cuánto, entonces (no si el lanzador ya captura así de a menudo: el HOME)
    nosig_confirm_every = 0.2, -- vista "No Signal": la siguiente mirada a los 0,2 s
    nosig_confirm_silent_ms = 400, -- quieta y con el audio en silencio: apagada si dura esto (2-3 miradas)
    nosig_confirm_ms = 1000,  -- si no (audio desconocido o la imagen cambia): 3 miradas y esto. Con sonido, nunca
    -- 16-11: "No Signal" tambien sale unos segundos cuando la consola cambia de modo de video (arrancando RESIDENT EVIL requiem: la
    -- app se cerraba con la consola encendida, 14-11 23:57 y 16-11 00:46; duro menos de 21 s). Confirmada, se espera esto antes de
    -- darla por apagada (con el aviso "Sin señal de la consola" encima) y si vuelve la imagen se sigue. Lo pone el lanzador
    -- (apagado_espera_s, apagado_espera_arranque_s): switch2-launch = arrancando un juego (la espera larga), switch2-powermenu =
    -- tras el menu de apagado (sin espera, como antes). 16-11 tarde (APAGADO): 3 s (antes 6) jugando; switch2-powermenu tambien cada
    -- 0,5 s en el HOME sin nada arrancando (sin espera), y la espera de cada "No Signal" se decide al verla y solo crece
    nosig_hold_ms = 3000,
    nosig_hold_launch_ms = 25000,
    nosig_hold_every = 0.25,  -- mientras se espera, una mirada cada esto (16-11 tarde: 0,25, antes 0,5; y una justo al acabar la espera)
    -- 15-11 (Prisma): letras de los avisos en pastilla (switch2-osd). Las de siempre: libass con la Segoe UI Variable (una
    -- fuente variable) podria sacar otro peso; se pueden probar con switch2_power-osd_title_font=...
    osd_title_font = "Segoe UI Semibold",
    osd_text_font = "Segoe UI",
    enabled = true,
    debug = false,
}
options.read_options(o, "ce_power")

-- Estado
local armed = false
local good_secs = 0          -- segundos acumulados de señal buena (para armar)
local stall_n, black_n, frozen_n = 0, 0, 0
local last_pos = nil
local last_fp = nil
local triggered = false
local last_db_str, same_db_n = nil, 0
local last_black = nil        -- resultado de la última captura (nil = sin dato)
local last_frozen = false     -- la última captura era idéntica a la anterior
local tick_n = 0
local silent_n = 0            -- segundos seguidos de silencio
local ext_audio, ext_audio_t = nil, 0 -- estado de audio enviado por el lanzador
local silent_since = nil      -- desde cuándo dice el lanzador "silent" (nil = no está en silencio)
local last_shot = 0           -- última imagen mirada por el script (sondeo, tick o captura del lanzador)
local grab_t, grab_gap = 0, 99 -- última captura del lanzador y el tiempo desde la anterior
local grab_sample, grab_used = nil, 0 -- su rejilla (luma media/máxima, huella) y la última que usó tick
local fast_seen               -- definida más abajo: ¿es esta imagen la pantalla "No Signal"?

-- Offsets de bytes R,G,B por formato de píxel (4 bytes/píxel)
local FMT = {
    bgr0 = {2, 1, 0}, bgra = {2, 1, 0},
    rgb0 = {0, 1, 2}, rgba = {0, 1, 2},
    ["0bgr"] = {3, 2, 1}, abgr = {3, 2, 1},
    ["0rgb"] = {1, 2, 3}, argb = {1, 2, 3},
}

local function reset_counters()
    stall_n, black_n, frozen_n = 0, 0, 0
    last_fp = nil
    last_frozen = false
end

-- Muestrea una imagen de screenshot-raw: devuelve luma media, luma máxima y huella (string)
local function sample_raw(r)
    if not r or not r.data or not r.w or r.w <= 0 then return nil end
    local off = FMT[r.format]
    if not off then
        msg.debug("formato no soportado: " .. tostring(r.format))
        return nil
    end
    local data, w, h, stride = r.data, r.w, r.h, r.stride
    local ro, go, bo = off[1] + 1, off[2] + 1, off[3] + 1
    local gw, gh = o.grid_w, o.grid_h
    local sum, maxl, n = 0, 0, 0
    local fp = {}
    for j = 0, gh - 1 do
        local y = math.floor((j + 0.5) * h / gh)
        local row = y * stride
        for i = 0, gw - 1 do
            local x = math.floor((i + 0.5) * w / gw)
            local p = row + x * 4
            local R = data:byte(p + ro)
            local G = data:byte(p + go)
            local B = data:byte(p + bo)
            -- Luma BT.709 aproximada en enteros
            local l = (54 * R + 183 * G + 19 * B) / 256
            sum = sum + l
            if l > maxl then maxl = l end
            n = n + 1
            fp[n] = string.char(R, G, B)
        end
    end
    return sum / n, maxl, table.concat(fp)
end

-- La imagen actual. Toda imagen que mira el script pasa también por fast_seen (la consola apagada se ve antes)
local function sample_frame(src)
    local r = mp.command_native({name = "screenshot-raw", flags = "video"})
    local avg, maxl, fp = sample_raw(r)
    if fp then
        last_shot = mp.get_time()
        fast_seen(fp, src or "tick")
    end
    return avg, maxl, fp
end

-- Nivel de audio: devuelve dB (número, -inf posible) o nil si no hay datos
local function audio_db()
    local md = mp.get_property_native("af-metadata/s2lvl")
    if type(md) ~= "table" then return nil end
    local v = md["lavfi.astats.Overall.RMS_level"]
    if v == nil then return nil end
    if v == "-inf" then return -math.huge end
    -- Si el audio deja de llegar el valor se queda congelado: un RMS idéntico
    -- varios segundos seguidos se considera "desconocido".
    if v == last_db_str then same_db_n = same_db_n + 1 else same_db_n = 0 end
    last_db_str = v
    if same_db_n >= 3 then return nil end
    return tonumber(v)
end

-- Pantalla "No Signal" de AVerMedia (la manda la capturadora sin consola):
-- franjas negras arriba y abajo y un recuadro gris en el centro (con texto y
-- un triángulo amarillo). Se reconoce sobre la huella de la rejilla.
local function is_no_signal(fp)
    if not fp then return false end
    local gw, gh = o.grid_w, o.grid_h
    local blk_n, blk_ok, box_n, box_ok = 0, 0, 0, 0
    for j = 0, gh - 1 do
        local fy = (j + 0.5) / gh
        for i = 0, gw - 1 do
            local fx = (i + 0.5) / gw
            local p = (j * gw + i) * 3
            local R, G, B = fp:byte(p + 1, p + 3)
            local l = (54 * R + 183 * G + 19 * B) / 256
            if fy < 0.27 or fy > 0.67 then
                blk_n = blk_n + 1
                if l < 20 then blk_ok = blk_ok + 1 end
            elseif fy > 0.33 and fy < 0.63 and fx > 0.10 and fx < 0.90 then
                box_n = box_n + 1
                if l > 110 and l < 235 and math.abs(R - G) < 25 and math.abs(G - B) < 25 then
                    box_ok = box_ok + 1
                end
            end
        end
    end
    return blk_n > 0 and box_n > 0 and blk_ok >= blk_n * 0.95 and box_ok >= box_n * 0.75
end

-- Aviso "Enciende la consola" dibujado por encima de la pantalla "No Signal"
-- (fondo negro que la tapa entera + texto). screenshot-raw "video" no incluye
-- el OSD, así que el aviso no afecta a la detección.
local no_signal_shown = false
local console_on = false      -- lo que se ve es la consola (no "No Signal")
local console_seen = false    -- se ha visto la imagen de la consola en esta sesión
local nosig_since = nil       -- desde cuándo está la pantalla "No Signal"
local ns_held = false         -- 16-11: "No Signal" confirmada y esperando por si vuelve la imagen (nosig_hold_ms)
local ns_eval = false         -- 16-11: "No Signal" vista tras haber visto la consola, mirandose (fast_seen): el aviso es el de esperar

-- El lanzador solo vigila el apagado (fotogramas que dejan de llegar) con la
-- consola a la vista; al pasar de "No Signal" a la imagen hay un corte que no
-- es un apagado
local function send_console()
    mp.commandv("script-message", "switch2-console", console_on and "on" or "off")
end

local function set_console(on)
    if on == console_on then return end
    console_on = on
    if on then console_seen = true end
    send_console()
end

local nosig_overlay = mp.create_osd_overlay("ass-events")
nosig_overlay.res_x, nosig_overlay.res_y = 1920, 1080
nosig_overlay.z = 10
-- El de antes, por si el de Prisma (nosig_ass, más abajo) fallase
local NOSIG_PLAIN =
    "{\\rDefault\\an7\\pos(0,0)\\bord0\\shad0\\3a&HFF&\\4a&HFF&\\1c&H080504&\\p1}m 0 0 l 1920 0 l 1920 1080 l 0 1080{\\p0}\n" ..
    "{\\rDefault\\an5\\pos(960,500)\\bord0\\shad0\\3a&HFF&\\4a&HFF&\\fnSegoe UI Semibold\\fs76\\1c&HF8F2F0&}Enciende la consola\n" ..
    "{\\rDefault\\an5\\pos(960,590)\\bord0\\shad0\\3a&HFF&\\4a&HFF&\\fnSegoe UI\\fs34\\1c&HD7CDC8&\\1a&H50&}La imagen aparecerá en cuanto se encienda"

-- ---- Prisma (15-11): avisos en pastilla, como los del motor propio (Osd.cs) y los de la app (telón, pantalla de carga) ----
-- Pastilla oscura translúcida (degradado #1E2129 -> #111319 al 90 %, borde blanco al 18 %, esquinas de media altura), punto
-- cian (rojo: algo va mal), el título en blanco y lo que va tras " · " en gris. Medidas a 1080p, las del aviso del motor;
-- mpv las escala a la ventana (y Magpie con la imagen). Con ASS, como el aviso "Enciende la consola": formas con curvas
-- bezier, el degradado en franjas (\clip), sombra difuminada (\blur). Cada evento empieza por \rDefault: el estilo del OSD
-- lleva la placa opaca de --osd-border-style; \3a y \4a transparentes por si acaso. El texto se mide con un overlay oculto
-- (compute_bounds). Entra en 140 ms subiendo un poco y se va en 260 ms
local PANEL_TOP = {30, 33, 41}       -- #1E2129
local PANEL_BOTTOM = {17, 19, 25}    -- #111319
local ACCENT = {0, 200, 235}         -- cian #00C8EB
local WARN = {255, 60, 85}           -- rojo #FF3C55
local TITLE_C = {246, 248, 252}
local TEXT_C = {168, 176, 190}       -- gris #A8B0BE
local BEZ = 0.5523                   -- (curva bezier que hace un cuarto de círculo)

local function ass_color(c)
    return string.format("&H%02X%02X%02X&", c[3], c[2], c[1])
end

-- Opacidad 0..1 -> alfa de ASS (00 = opaco, FF = transparente)
local function ass_alpha(op)
    op = math.max(0, math.min(1, op))
    return string.format("&H%02X&", math.floor(255 - 255 * op + 0.5))
end

local function ass_escape(s)
    s = s:gsub("\\", "\\\239\187\191") -- (una barra seguida de un espacio sin anchura: no se toma por una orden)
    s = s:gsub("{", "\\{")
    s = s:gsub("}", "\\}")
    s = s:gsub("[\r\n]", " ")
    return s
end

-- Rectángulo redondeado (radio r) como dibujo ASS; rev = en el otro sentido (un agujero dentro de otro: el borde)
local function rrect(x, y, w, h, r, rev)
    r = math.min(r, h / 2, w / 2)
    local k = r * BEZ
    local x1, y1 = x + w, y + h
    if not rev then
        return string.format("m %.2f %.2f l %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f l %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f " ..
                             "l %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f l %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f",
            x + r, y, x1 - r, y, x1 - r + k, y, x1, y + r - k, x1, y + r,
            x1, y1 - r, x1, y1 - r + k, x1 - r + k, y1, x1 - r, y1,
            x + r, y1, x + r - k, y1, x, y1 - r + k, x, y1 - r,
            x, y + r, x, y + r - k, x + r - k, y, x + r, y)
    end
    return string.format("m %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f l %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f " ..
                         "l %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f l %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f",
        x + r, y, x + r - k, y, x, y + r - k, x, y + r,
        x, y1 - r, x, y1 - r + k, x + r - k, y1, x + r, y1,
        x1 - r, y1, x1 - r + k, y1, x1, y1 - r + k, x1, y1 - r,
        x1, y + r, x1, y + r - k, x1 - r + k, y, x1 - r, y)
end

local function circle(cx, cy, r)
    local k = r * BEZ
    return string.format("m %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f " ..
                         "b %.2f %.2f %.2f %.2f %.2f %.2f b %.2f %.2f %.2f %.2f %.2f %.2f",
        cx, cy - r,
        cx + k, cy - r, cx + r, cy - k, cx + r, cy,
        cx + r, cy + k, cx + k, cy + r, cx, cy + r,
        cx - k, cy + r, cx - r, cy + k, cx - r, cy,
        cx - r, cy - k, cx - k, cy - r, cx, cy - r)
end

-- Un dibujo (coordenadas de 1080p tal cual) y un texto (alineado a la izquierda y centrado en vertical en x, y)
local function draw_ev(tags, shape)
    return "{\\rDefault\\an7\\pos(0,0)\\bord0\\shad0\\3a&HFF&\\4a&HFF&" .. tags .. "\\p1}" .. shape .. "{\\p0}"
end

local function text_ev(x, y, font, fs, color, op, text)
    return string.format("{\\rDefault\\an4\\pos(%.2f,%.2f)\\bord0\\shad0\\3a&HFF&\\4a&HFF&\\q2\\fn%s\\fs%.2f\\1c%s\\1a%s}%s",
        x, y, font, fs, color, ass_alpha(op), text)
end

-- Ancho de la tinta de un texto y dónde empieza (para colocarlo justo), midiéndolo con un overlay oculto
local measure_ov = mp.create_osd_overlay("ass-events")
measure_ov.res_x, measure_ov.res_y = 1920, 1080
measure_ov.hidden = true
measure_ov.compute_bounds = true

local function text_box(text, font, fs)
    measure_ov.data = string.format("{\\rDefault\\an7\\pos(0,0)\\bord0\\shad0\\q2\\fn%s\\fs%.2f}%s", font, fs, text)
    local ok, r = pcall(measure_ov.update, measure_ov)
    if ok and type(r) == "table" and r.x0 and r.x1 and r.x1 > r.x0 then
        return r.x1 - r.x0, r.x0
    end
    -- Sin medida (no debería pasar): a ojo, por letras (no se cuentan los bytes de continuación de UTF-8)
    local n = 0
    for _ in text:gmatch("[^\128-\191]") do n = n + 1 end
    return n * fs * 0.37, 0
end

-- Medidas de una pastilla. s = escala (1 = el aviso del motor a 1080p: letra de 25 y 23 px, 62 de alto). \fs de ASS es el alto
-- de la celda (Segoe UI: 1,33 veces el cuerpo): 33,25 y 30,6
local function pill_layout(text, s)
    local title, rest = text, nil
    local cut = text:find(" \194\183 ", 1, true)
    if cut and cut > 1 then
        title = text:sub(1, cut - 1)
        rest = text:sub(cut + 4)
        rest = rest:gsub(" \194\183 ", "   \194\183   ")
    end
    local L = {s = s, h = 62 * s, fs_t = 33.25 * s, fs_r = 30.6 * s}
    L.title = ass_escape(title)
    L.tw, L.tx0 = text_box(L.title, o.osd_title_font, L.fs_t)
    L.w = (30 + 11 + 16 + 30) * s + L.tw
    if rest and rest ~= "" then
        L.rest = ass_escape(rest)
        L.rw, L.rx0 = text_box(L.rest, o.osd_text_font, L.fs_r)
        L.w = L.w + 18 * s + L.rw
    end
    return L
end

-- Los eventos ASS de una pastilla centrada en cx con su borde de arriba en y; op = opacidad (fundidos)
local function pill_events(L, accent, cx, y, op)
    local s, h, w = L.s, L.h, L.w
    local x = cx - w / 2
    local out = {}
    -- Sombra difuminada, algo por debajo
    out[#out + 1] = draw_ev(string.format("\\blur%.2f\\1c&H000000&\\1a%s", 9 * s, ass_alpha(0.34 * op)),
                            rrect(x - 3 * s, y + 6 * s, w + 6 * s, h + 3 * s, (h + 3 * s) / 2))
    -- Panel: degradado de arriba abajo en franjas, con el brillo de arriba (vidrio)
    local n = 10
    for i = 0, n - 1 do
        local t = (i + 0.5) / n
        local hl = 0.078 * math.max(0, 1 - t / 0.48)
        local c = {}
        for j = 1, 3 do
            local v = PANEL_TOP[j] + (PANEL_BOTTOM[j] - PANEL_TOP[j]) * t
            c[j] = math.floor(v + (255 - v) * hl + 0.5)
        end
        local y0 = y + h * i / n - (i == 0 and 2 or 0)
        local y1 = y + h * (i + 1) / n + (i == n - 1 and 2 or 0)
        out[#out + 1] = draw_ev(string.format("\\clip(%.2f,%.2f,%.2f,%.2f)\\1c%s\\1a%s", x - 2, y0, x + w + 2, y1,
                                              ass_color(c), ass_alpha(0.91 * op)),
                                rrect(x, y, w, h, h / 2))
    end
    -- Borde fino, blanco al 18 %: un anillo
    local b = math.max(1.2 * s, 1)
    out[#out + 1] = draw_ev(string.format("\\1c&HFFFFFF&\\1a%s", ass_alpha(0.18 * op)),
                            rrect(x - b / 2, y - b / 2, w + b, h + b, (h + b) / 2) .. " " ..
                            rrect(x + b / 2, y + b / 2, w - b, h - b, (h - b) / 2, true))
    -- Punto de color con su halo
    local dot = 11 * s
    local dx, cy = x + 30 * s + dot / 2, y + h / 2
    out[#out + 1] = draw_ev(string.format("\\blur%.2f\\1c%s\\1a%s", 5 * s, ass_color(accent), ass_alpha(0.5 * op)),
                            circle(dx, cy, dot * 0.95))
    out[#out + 1] = draw_ev(string.format("\\1c%s\\1a%s", ass_color(accent), ass_alpha(op)), circle(dx, cy, dot / 2))
    -- Título en blanco y el resto en gris
    local tx = x + (30 + 11 + 16) * s
    out[#out + 1] = text_ev(tx - L.tx0, cy - s, o.osd_title_font, L.fs_t, ass_color(TITLE_C), op, L.title)
    if L.rest then
        out[#out + 1] = text_ev(tx + L.tw + 18 * s - L.rx0, cy, o.osd_text_font, L.fs_r, ass_color(TEXT_C), op, L.rest)
    end
    return table.concat(out, "\n")
end

-- El aviso de arriba: uno a la vez (uno nuevo sustituye al que hubiera)
local notice_ov = mp.create_osd_overlay("ass-events")
notice_ov.res_x, notice_ov.res_y = 1920, 1080
notice_ov.z = 20
local notice, notice_timer = nil, nil

local function notice_stop()
    notice = nil
    if notice_timer then notice_timer:kill() end
    notice_timer = nil
    notice_ov:remove()
end

local function notice_frame()
    local N = notice
    if not N then return end
    local t = mp.get_time() - N.t0
    local fin, fout = 0.14, 0.26
    local op, slide = 1, 0
    if t < fin then
        local k = t / fin
        op, slide = k, (1 - k) * (1 - k) * 14
    elseif t >= fin + N.hold then
        op = 1 - (t - fin - N.hold) / fout
        if op <= 0 then
            notice_stop()
            return
        end
    end
    if op >= 1 and slide == 0 then
        if N.full then return end -- (quieta: nada que repintar hasta que se vaya)
        N.full = true
    else
        N.full = false
    end
    notice_ov.data = pill_events(N.lay, N.accent, 960, N.top + slide, op)
    notice_ov:update()
end

local function notice_show(text, hold, warn)
    notice = {lay = pill_layout(text, 1), accent = warn and WARN or ACCENT, t0 = mp.get_time(), hold = hold,
              top = 0.035 * 1080 + 18 * 0.7, full = false}
    if not notice_timer then
        notice_timer = mp.add_periodic_timer(1 / 60, function()
            local ok, err = pcall(notice_frame)
            if not ok then
                msg.warn("aviso en pastilla: " .. tostring(err))
                notice_stop()
            end
        end)
    end
    notice_frame()
end

mp.register_script_message("switch2-osd", function(ms, text, kind)
    if not text or text == "" or no_signal_shown then return end -- (con "Enciende la consola" a la vista, nada encima)
    local hold = math.max(0.3, (tonumber(ms) or 2500) / 1000)
    local ok, err = pcall(notice_show, text, hold, kind == "warn")
    if not ok then
        -- Si la pastilla falla, el aviso de mpv de siempre
        msg.warn("aviso en pastilla: " .. tostring(err))
        pcall(notice_stop)
        mp.osd_message(text, hold)
    end
end)

-- "Enciende la consola" con el aspecto de Prisma: fondo oscuro que tapa entera la pantalla "No Signal", con las luces de la
-- pantalla de carga (cian a la izquierda, rojo a la derecha) y la pastilla en el centro, más grande y con el punto rojo
local NOSIG_OFF = "Enciende la consola · La imagen aparecerá en cuanto se encienda"
-- 16-11: esperando por si es un cambio de modo de video (ns_held): lo mismo con otro texto
local NOSIG_WAIT = "Sin señal de la consola · Si vuelve en unos segundos, se sigue jugando"
local nosig_cache = {}
local function nosig_ass(text)
    local out = {}
    -- Fondo opaco y encima el degradado (#0C0E14 arriba, #030407 abajo) en franjas
    out[1] = draw_ev("\\1c&H070403&", "m 0 0 l 1920 0 l 1920 1080 l 0 1080")
    local top, bot, n = {12, 14, 20}, {3, 4, 7}, 8
    for i = 0, n - 1 do
        local t = (i + 0.5) / n
        local c = {}
        for j = 1, 3 do c[j] = math.floor(top[j] + (bot[j] - top[j]) * t + 0.5) end
        local y0, y1 = 1080 * i / n, 1080 * (i + 1) / n
        out[#out + 1] = draw_ev("\\1c" .. ass_color(c), string.format("m 0 %.2f l 1920 %.2f l 1920 %.2f l 0 %.2f", y0, y0, y1, y1))
    end
    -- Luces de ambiente
    out[#out + 1] = draw_ev(string.format("\\blur70\\1c%s\\1a%s", ass_color(ACCENT), ass_alpha(0.10)), circle(380, 470, 420))
    out[#out + 1] = draw_ev(string.format("\\blur70\\1c%s\\1a%s", ass_color(WARN), ass_alpha(0.09)), circle(1540, 470, 420))
    local L = pill_layout(text or NOSIG_OFF, 1.4)
    out[#out + 1] = pill_events(L, WARN, 960, 540 - L.h / 2, 1)
    return table.concat(out, "\n")
end

-- (15-11: el de Prisma, hecho la primera vez; si fallase, el de antes)
local function nosig_data_for(text)
    local d = nosig_cache[text]
    if d then return d end
    local ok, data = pcall(nosig_ass, text)
    if ok and data then
        d = data
    else
        msg.warn("aviso " .. text .. ": " .. tostring(data))
        d = NOSIG_PLAIN
    end
    nosig_cache[text] = d
    return d
end

-- El aviso a pantalla completa: "Sin señal..." mientras se mira o se espera (ns_eval / ns_held, con la consola vista antes), si no
-- "Enciende la consola" (la app abierta con la consola apagada)
-- 21-11 (el usuario: "no pongas los textos de que se va la señal cuando pasa eso"): con la consola vista antes (un cambio de modo de
-- video de la PS5, ~2 s al entrar o salir de un juego a 120 Hz) los primeros NOSIG_QUIET_SECS solo un fondo oscuro, sin texto ni la
-- pantalla No Signal de la capturadora; si dura más (apagada de verdad), el aviso de siempre
local NOSIG_QUIET_SECS = 5
local nosig_quiet_timer = nil
local function nosig_paint()
    pcall(notice_stop) -- (ningún aviso encima)
    if console_seen and nosig_since and (mp.get_time() - nosig_since) < NOSIG_QUIET_SECS then
        nosig_overlay.data = draw_ev("\\1c&H070403&", "m 0 0 l 1920 0 l 1920 1080 l 0 1080")
        nosig_overlay:update()
        if not nosig_quiet_timer then
            nosig_quiet_timer = mp.add_timeout(NOSIG_QUIET_SECS + 0.05, function()
                nosig_quiet_timer = nil
                if no_signal_shown then nosig_paint() end
            end)
        end
        return
    end
    nosig_overlay.data = nosig_data_for(((ns_held or ns_eval) and console_seen) and NOSIG_WAIT or NOSIG_OFF)
    nosig_overlay:update()
end

local trigger -- definida más abajo

local function update_no_signal(fp)
    local ns = is_no_signal(fp)
    if ns == no_signal_shown then return end
    no_signal_shown = ns
    nosig_since = ns and mp.get_time() or nil
    if ns then
        msg.info("pantalla No Signal de la capturadora: aviso para encender la consola")
        nosig_paint()
        -- (09-11: la consola apagada en mitad de la partida ya no se cierra aquí con una sola imagen: lo confirma fast_seen,
        -- 2-3 miradas en 0,4-1 s, y si no, nosig_secs en tick)
    else
        msg.info("la pantalla No Signal ha desaparecido")
        nosig_overlay:remove()
    end
    set_console(not ns)
end

-- "quit" normal; si el core se queda colgado (p.ej. demuxer bloqueado
-- esperando frames), forzar la salida del proceso con el mismo código.
local function do_quit()
    mp.command("quit " .. o.exit_code)
    if o.hard_exit_secs > 0 then
        mp.add_timeout(o.hard_exit_secs, function()
            msg.warn("quit no completado, forzando os.exit(" .. o.exit_code .. ")")
            os.exit(o.exit_code, true)
        end)
    end
end

function trigger(reason, detail) -- (asigna la local declarada arriba)
    if triggered then return end
    triggered = true
    local line = string.format("%s %s\n", reason, os.date("%Y-%m-%d %H:%M:%S"))
    local f, err = io.open(o.flag, "w")
    if f then
        f:write(line)
        f:close()
    else
        msg.error("no se pudo escribir flag: " .. tostring(err))
    end
    msg.warn("Consola apagada (" .. reason .. ") -> salida " .. o.exit_code)
    -- (sin aviso en pantalla: se cierra en ~1 s y el usuario lo quiere lo mas discreto posible)
    local delay = o.quit_delay
    -- 09-11: "No Signal" es la consola apagada seguro (un reinicio de la gráfica congela la imagen, no pone esa pantalla):
    -- cierra el lanzador, que mata mpv al momento (el "quit" de mpv con la capturadora sin señal se puede colgar hasta
    -- hard_exit_secs) y lo demás a la vez. Si el lanzador no contesta, se sale como siempre a los 0,3 s
    if reason:sub(1, 9) == "no-signal" then
        mp.commandv("script-message", "switch2-poweroff", reason, detail or "")
        delay = math.max(delay, 0.3)
    end
    if delay > 0 then
        mp.add_timeout(delay, do_quit)
    else
        do_quit()
    end
end

-- Para el log del lanzador (switch2.log): lo que ve la detección rápida de "No Signal"
local function diag(text)
    mp.commandv("script-message", "switch2-nosig", text)
end

-- Cambio de captura sin cerrar mpv (el lanzador vuelve a abrir la capturadora con otro tamaño): mientras
-- dura, que no se tome por un apagado. Si falla, se avisa al lanzador (lo reabre a la manera de siempre)
local switching_until = 0
-- 15-11: al lanzar un juego (el lanzador manda switch2-launch) la consola puede cambiar de modo de video y la capturadora enseña
-- "No Signal" unos segundos (14-11 23:57: RESIDENT EVIL). 16-11: ya no es una ventana sin mirar nada (la app la mandaba al salir del
-- HOME, tambien con el menu de apagado; y el de 16-11 00:46 llego despues): mientras dura, "No Signal" espera nosig_hold_launch_ms
-- antes de dar la consola por apagada (y no cuentan el negro, la imagen congelada ni el fin de la captura)
local launch_until = 0
local powermenu_until = 0 -- (16-11: declarado aqui para que switch2-launch lo pueda anular)
-- (16-11 tarde: "switch2-launch 0" = ya no se arranca nada: el lanzador la cancela, vuelto al HOME sin entrar en el juego)
mp.register_script_message("switch2-launch", function(secs)
    launch_until = mp.get_time() + (tonumber(secs) or 20)
    powermenu_until = 0 -- (16-11, revision: arrancar un juego anula el menu de apagado abierto y cancelado antes)
end)
local function launching() return mp.get_time() < launch_until end
-- 16-11: tras el menu de apagado (lo ve el lanzador): "No Signal" es la consola apagada, sin esperar. 16-11 tarde: el lanzador tambien lo
-- manda cada 0,5 s ("switch2-powermenu 3") en el HOME sin nada arrancando, y "switch2-powermenu 0" al dejar de estarlo (y luego su
-- switch2-launch si toca)
mp.register_script_message("switch2-powermenu", function(secs)
    powermenu_until = mp.get_time() + (tonumber(secs) or 20)
    launch_until = 0
end)
local function switching() return mp.get_time() < switching_until end
mp.register_script_message("switch2-switching", function(secs)
    switching_until = mp.get_time() + (tonumber(secs) or 8)
    reset_counters()
    last_pos = nil
end)
-- Primera imagen de la captura nueva: el lanzador da el cambio por hecho
mp.register_event("playback-restart", function()
    if not switching() then return end
    switching_until = mp.get_time() + 1
    local w = mp.get_property_number("video-params/w") or 0
    local h = mp.get_property_number("video-params/h") or 0
    mp.commandv("script-message", "switch2-switched", string.format("%dx%d", w, h))
end)

-- ---- Consola en espera o apagada: la pantalla "No Signal" vista enseguida (09-11) ----
-- Antes, 3 capturas seguidas del lanzador con ella (cada 1,2 s jugando: 2,4-3,6 s desde que salía; en el HOME, cada 0,5 s:
-- 1-1,5 s) y luego el cierre. Ahora cuenta cualquier imagen que mire el script (sobre todo las capturas del lanzador) y,
-- vista una vez, se mira otra a los nosig_confirm_every s. Apagada si: la misma imagen quieta, audio en silencio y
-- nosig_confirm_silent_ms desde la primera, o 3 miradas y nosig_confirm_ms (audio desconocido o imagen que cambia). Con
-- sonido de la consola no cuenta (sin señal HDMI no hay sonido), ni sin haber visto antes la consola (al abrir la app con
-- ella apagada). Con nosig_fast_secs > 0, además, en los primeros segundos de cada silencio (lo primero que se nota al
-- ponerla en espera: en el log, 0,2-1,3 s antes de "No Signal") una mirada cada nosig_fast_every s
local ns_n, ns_t0, ns_fp, ns_still, ns_src = 0, nil, nil, true, nil
local probe_at = nil          -- siguiente mirada para confirmar
local ns_sound_said = false

local function audio_state()
    if ext_audio == nil or mp.get_time() - ext_audio_t >= 3 then return "desconocido" end
    return ext_audio
end

-- La misma imagen (la pantalla de la capturadora no se mueve): diferencia media de la rejilla
local function same_fp(a, b)
    if not a or not b or #a ~= #b then return false end
    local d = 0
    for i = 1, #a do d = d + math.abs(a:byte(i) - b:byte(i)) end
    return d / #a < 4
end

local ns_hold, ns_why = nil, nil -- 16-11 tarde: la espera de este "No Signal" (ms; decidida al verla, solo crece: hold_episode)

local function ns_reset()
    ns_n, ns_t0, ns_fp, ns_still, ns_src, probe_at = 0, nil, nil, true, nil, nil
    ns_held, ns_eval = false, false
    ns_hold, ns_why = nil, nil
end

-- 16-11: lo que tiene que durar "No Signal" confirmada para dar la consola por apagada (ms) y por que. 16-11 tarde: en el HOME sin nada
-- arrancando el lanzador manda switch2-powermenu (sin espera); lo de siempre queda para jugando y entrando o saliendo del HOME
local function hold_now()
    if mp.get_time() < powermenu_until then return 0, "tras el menu de apagado" end
    if launching() then return o.nosig_hold_launch_ms, "arrancando un juego" end
    return o.nosig_hold_ms, "jugando o entrando o saliendo del HOME"
end

-- 16-11 tarde (APAGADO): la de este "No Signal": la de cuando se vio y, si luego toca mas (se marca un arranque), esa; nunca menos (al
-- pasar a "en el HOME sin nada arrancando" a mitad, recien vuelto de un juego, se cerraba sin la espera de un cambio de modo al volver)
local function hold_episode()
    local h, why = hold_now()
    if ns_hold == nil or h > ns_hold then ns_hold, ns_why = h, why end
    return ns_hold, ns_why
end

fast_seen = function(fp, src)
    if triggered or not o.enabled or not console_seen or switching() then
        if ns_n > 0 or probe_at then ns_reset() end
        return
    end
    local now = mp.get_time()
    if not is_no_signal(fp) then
        if ns_n > 0 then
            diag(string.format("pantalla No Signal vista %d %s en %.0f ms y ya no se ve: no era un apagado%s",
                ns_n, ns_n == 1 and "vez" or "veces", (now - ns_t0) * 1000,
                ns_held and " (un cambio de modo de video): se sigue" or ""))
            -- (16-11: el aviso fuera ya, sin esperar a tick)
            if no_signal_shown then update_no_signal(fp) end
        end
        ns_reset()
        return
    end
    local audio = audio_state()
    if audio == "sound" then
        -- Si de verdad se ha apagado, el silencio llega en 0,5-1 s y cuenta la siguiente captura
        if not ns_sound_said then
            ns_sound_said = true
            diag("pantalla No Signal vista con sonido de la consola (" .. src .. "): no cuenta")
        end
        ns_reset()
        return
    end
    if ns_n == 0 then
        ns_t0, ns_still, ns_src = now, true, src
        ns_eval = true
        hold_episode() -- (16-11 tarde: la espera de este "No Signal", la de ahora)
        diag(string.format("pantalla No Signal vista (%s, audio %s%s): se confirma", src, audio,
            (audio == "silent" and silent_since) and string.format(" desde hace %.1f s", now - silent_since) or ""))
    else
        ns_still = ns_still and same_fp(fp, ns_fp)
    end
    ns_n = ns_n + 1
    ns_fp = fp
    local span = (now - ns_t0) * 1000
    local quick = ns_still and audio == "silent"
    if (quick and ns_n >= 2 and span >= o.nosig_confirm_silent_ms) or (ns_n >= 3 and span >= o.nosig_confirm_ms) then
        -- 16-11: confirmada; apagada si ademas dura lo de hold_now (un cambio de modo de video vuelve antes). 16-11 tarde: hold_episode
        local hold, why = hold_episode()
        if span >= hold then
            trigger("no-signal-lua", string.format("%d miradas en %.0f ms, %s, audio %s, la primera en %s%s",
                ns_n, span, ns_still and "quieta" or "cambiando", audio, ns_src,
                hold > 0 and string.format(", %.0f s esperados: %s", hold / 1000, why) or ""))
            return
        end
        if not ns_held then
            ns_held = true
            diag(string.format("pantalla No Signal confirmada (%s, audio %s): se espera hasta %.0f s por si vuelve la imagen (%s: puede ser " ..
                "un cambio de modo de video)", ns_still and "quieta" or "cambiando", audio, hold / 1000, why))
            -- El aviso a pantalla completa ya ("Sin señal de la consola"), sin esperar a tick
            if no_signal_shown then nosig_paint() else update_no_signal(fp) end
        end
        -- (16-11 tarde: la siguiente mirada justo al acabar la espera si llega antes que la de cada nosig_hold_every: se cierra a su hora)
        local due = ns_t0 + hold / 1000 + 0.02
        probe_at = (due > now and due < now + o.nosig_hold_every) and due or now + o.nosig_hold_every
        return
    end
    -- En silencio, cada 0,2 s; si no, 3 miradas repartidas en nosig_confirm_ms
    probe_at = now + (quick and o.nosig_confirm_every or math.max(o.nosig_confirm_every, o.nosig_confirm_ms / 2000))
end

-- Las miradas: justo tras salir una imagen (como las capturas del lanzador: sin retrasar la siguiente), como mucho 0,15 s
-- de espera. Una mirada es un screenshot-raw entero (como una captura del lanzador): solo para confirmar (con la consola
-- apagada ya no hay juego que se resienta) o, con nosig_fast_secs > 0, en los primeros segundos de cada silencio (y no si
-- el lanzador ya captura cada <= 0,6 s, como en el HOME)
local probe_req = nil
local function want_probe(now)
    if triggered or not o.enabled or not console_seen or switching() then return false end
    if probe_at then return now >= probe_at end
    if no_signal_shown or o.nosig_fast_secs <= 0 or not silent_since or audio_state() ~= "silent" then return false end
    if now - silent_since > o.nosig_fast_secs then return false end
    if grab_gap <= 0.6 and now - grab_t < 1 then return false end
    return now - last_shot >= o.nosig_fast_every
end
local function do_probe()
    probe_req = nil
    if mp.get_property_bool("pause") then return end
    local now = mp.get_time()
    last_shot = now
    if sample_frame("sondeo") == nil and probe_at then probe_at = now + o.nosig_confirm_every end
end
mp.add_periodic_timer(0.05, function()
    local now = mp.get_time()
    if probe_req then
        if now - probe_req > 0.15 then do_probe() end
    elseif want_probe(now) then
        probe_req = now
    end
end)
mp.observe_property("time-pos", "number", function()
    if probe_req then do_probe() end
end)

local function tick()
    send_console() -- se repite por si el lanzador se conectó tarde
    if switching() then
        reset_counters()
        last_pos = nil
        return
    end
    local idle = not o.enabled or triggered or mp.get_property_bool("pause")
    -- Sin detección (o en pausa) el aviso "Enciende la consola" se sigue quitando
    if idle and no_signal_shown then
        local _, _, fp = sample_frame()
        if fp then update_no_signal(fp) end
    end
    if not o.enabled or triggered then return end
    -- En pausa no se evalúa nada
    if mp.get_property_bool("pause") then
        reset_counters()
        last_pos = nil
        return
    end

    -- stall: time-pos no avanza
    local pos = mp.get_property_number("time-pos")
    local advancing = pos ~= nil and last_pos ~= nil and pos ~= last_pos
    if pos ~= nil and last_pos ~= nil and pos == last_pos then
        stall_n = stall_n + 1
    elseif pos == nil and armed then
        stall_n = stall_n + 1
    else
        stall_n = 0
    end
    last_pos = pos

    -- audio: true=silencio, false=sonido, nil=desconocido.
    -- Fuente preferida: el lanzador (medidor de audio de Streaming Center en Windows).
    -- Si no llega nada reciente, se usa el filtro --af de mpv si existe.
    local silent = nil
    local db = nil
    if ext_audio ~= nil and mp.get_time() - ext_audio_t < 3 then
        silent = ext_audio == "silent"
    else
        db = audio_db()
        if db ~= nil then silent = db <= o.silence_db end
    end
    silent_n = (silent == true) and silent_n + 1 or 0

    -- black / frozen / "No Signal", con el audio en silencio. 09-11: sin imagen propia mientras se juega (cada screenshot-raw
    -- de aquí, sin ir justo tras una imagen, paraba mpv 80-107 ms cada 3 s en las pruebas con el audio en silencio): se usa
    -- la última captura del lanzador (cada 1,2 s jugando, 0,5 s en el HOME, justo tras salir una imagen), ya muestreada en
    -- do_grab. Imagen propia solo con el aviso "Enciende la consola" puesto (no hay juego que se resienta: para quitarlo
    -- enseguida) o si el lanzador lleva 5 s sin capturar (entonces como antes: tras silence_before_probe s de silencio,
    -- cada probe_secs). Con sonido, o sin saber nada del audio, no se mira
    tick_n = tick_n + 1
    local avg, maxl, fp
    local now = mp.get_time()
    local g = grab_sample
    if g and g.t > grab_used and now - g.t < 3 and (no_signal_shown or silent == true) then
        avg, maxl, fp = g.avg, g.maxl, g.fp
        grab_used = g.t
    elseif no_signal_shown or (silent == true and now - grab_t > 5 and silent_n >= o.silence_before_probe
            and (last_black or tick_n % o.probe_secs == 0)) then
        avg, maxl, fp = sample_frame()
    end
    if avg ~= nil then
        -- El aviso "Enciende la consola" (y la vigilancia del lanzador en pausa) solo con "No Signal" vista dos veces
        -- seguidas (fast_seen) o para quitarlo: una imagen de un juego que se le parezca no lo pone
        if no_signal_shown or ns_n >= 2 or not console_seen then update_no_signal(fp) end
        -- la pantalla "No Signal" cuenta como negro (consola apagada)
        last_black = no_signal_shown or ns_n > 0 or (avg < o.black_luma and maxl < o.black_luma * 3)
    elseif silent ~= true then
        last_black = nil
    end
    -- Consola apagada al poco de encenderla (sin armar todavía): "No Signal" que dura (16-11: como poco lo de hold_now; 16-11 tarde: o
    -- lo de este "No Signal" si se esta mirando, hold_episode)
    local hold_s = hold_now() / 1000
    if ns_n > 0 and ns_hold then hold_s = math.max(hold_s, ns_hold / 1000) end
    if no_signal_shown and console_seen and nosig_since and
            mp.get_time() - nosig_since >= math.max(o.nosig_secs, hold_s) then
        trigger("no-signal-unarmed")
        return
    end
    if fp ~= nil then
        last_frozen = last_fp ~= nil and fp == last_fp
        last_fp = fp
    elseif silent ~= true then
        last_frozen = false
    end
    local black = last_black == true
    local frozen = last_frozen

    black_n = (black and silent == true) and black_n + 1 or 0
    frozen_n = (frozen and silent == true) and frozen_n + 1 or 0

    -- Armado: frames avanzando y (sonido, audio desconocido o imagen no negra).
    -- Con la consola apagada y la capturadora mandando negro + silencio no se arma.
    if not armed then
        if advancing and not no_signal_shown and (silent ~= true or last_black == false) then
            good_secs = good_secs + 1
            if good_secs >= o.arm_secs then
                armed = true
                reset_counters()
                msg.info("detección armada")
                if o.debug then mp.osd_message("switch2_power: armado", 2) end
            end
        end
    end

    if o.debug then
        local s = string.format(
            "armed=%s good=%d pos=%s stall=%d | luma avg=%s max=%s black=%s(%d) | frozen=%s(%d) | dB=%s silent=%s",
            tostring(armed), good_secs, pos and string.format("%.2f", pos) or "nil", stall_n,
            avg and string.format("%.1f", avg) or "nil", maxl and string.format("%.0f", maxl) or "nil",
            tostring(black), black_n, tostring(frozen), frozen_n,
            db and string.format("%.1f", db) or tostring(ext_audio), tostring(silent))
        msg.info(s)
        mp.osd_message(s, 1.1)
    end

    if not armed then return end
    -- 16-11: arrancando un juego (switch2-launch) ni el negro largo, ni la imagen congelada ni sin imagenes (como antes, cuando esa
    -- ventana era la de switching: ahora "No Signal" si se mira, con su espera larga)
    if launching() then
        stall_n, black_n, frozen_n = 0, 0, 0
        return
    end
    if stall_n >= o.stall_secs then
        trigger("stall")
    elseif black_n >= o.black_secs then
        trigger("black")
    elseif frozen_n >= o.frozen_secs then
        trigger("frozen")
    end
end

-- 08-11: los tirones que quedan caen ~1 por minuto siempre en la misma fase del segundo: se mide lo que tarda tick
mp.add_periodic_timer(1, function()
    local t0 = mp.get_time(); tick()
    local dt = (mp.get_time() - t0) * 1000
    if dt > 4 then mp.commandv("script-message", "switch2-diag", string.format("tick %.1f ms", dt)) end
end)

-- Aviso al lanzador de que mpv ya muestra imagen (la de la consola o la
-- pantalla "No Signal" de la capturadora): entonces quita su pantalla de carga.
-- Se repite hasta que el lanzador confirma (puede conectarse tarde).
local video_on, video_acked = false, false
local vid_timer
vid_timer = mp.add_periodic_timer(0.5, function()
    if not video_on then
        -- sin frame de video no hay captura (el audio solo no cuenta)
        if mp.get_property_number("time-pos") == nil then return end
        local avg = sample_frame("inicio")
        if avg == nil then return end
        video_on = true
        msg.info("mpv ya muestra imagen")
    end
    mp.commandv("script-message", "switch2-signal", "on")
end)
mp.register_script_message("switch2-signal-ack", function()
    -- 15-11: solo despues de haber avisado. Uno de antes (el lanzador lo mandaba al oir la consola, sin imagen aun) paraba
    -- el aviso para siempre: el lanzador no se enteraba de la imagen (lo mismo pasaba con el motor propio)
    if not video_on then return end
    video_acked = true
    vid_timer:kill()
end)

-- Arranque: mientras no se sepa si hay consola se mira la imagen cada 0,5 s para
-- poner o quitar el aviso enseguida. Después de la primera imagen real lo
-- sigue tick() (sin tirones durante el juego).
local start_timer
start_timer = mp.add_periodic_timer(0.5, function()
    if mp.get_property_number("time-pos") == nil then return end
    local avg, maxl, fp = sample_frame("inicio")
    if avg == nil then return end
    update_no_signal(fp)
    if not no_signal_shown and not (avg < o.black_luma and maxl < o.black_luma * 3) then
        msg.info("señal de la consola detectada")
        set_console(true)
        start_timer:kill()
    end
end)

-- Ritmo real para el lanzador (elige el aspecto de DLSS 5 con el): imagenes que han salido del filtro de
-- repetidas en el ultimo medio segundo y si la ultima era nueva o una copia de una imagen quieta (el filtro
-- deja pasar una de cada dos copias para que el ritmo de LS no tenga huecos)
local shown = 0
mp.observe_property("time-pos", "number", function(_, v)
    if v then shown = shown + 1 end
end)
mp.add_periodic_timer(0.5, function()
    local md = mp.get_property_native("vf-metadata/s2dup")
    local s = type(md) == "table" and tonumber(md["lavfi.scene_score"]) or nil
    local kind = s == nil and "unknown" or (s > 0 and "new" or "copy")
    mp.commandv("script-message", "switch2-rate", tostring(shown * 2), kind)
    shown = 0
end)

-- Imagen actual para el lanzador (lee el nombre del juego seleccionado en el HOME): la de la memoria de mpv
-- (--screenshot-sw, ~11 ms a 1440p), volcada en crudo a un fichero desde el hilo del script.
-- mode "light" (jugando, para ver si se ha ido al HOME): 1 de cada 2-3 pixeles y filas, ~1280x720 (3,7 MB en vez de
-- 15-33; medido el 02-11: ~30 ms en el hilo del script, nada en el de mpv)
local function light_frame(r)
    local step = math.max(1, math.floor(r.w / 1280 + 0.5))
    if step <= 1 then return r.data, r.w, r.h, r.stride end
    local ow, oh = math.floor(r.w / step), math.floor(r.h / step)
    local pat = "(....)" .. string.rep(".", 4 * (step - 1))
    local data, stride, rows = r.data, r.stride, {}
    for y = 0, oh - 1 do
        local s = y * step * stride + 1
        rows[y + 1] = (data:sub(s, s + ow * step * 4 - 1):gsub(pat, "%1"))
    end
    return table.concat(rows), ow, oh, ow * 4
end

local function do_grab(path, mode)
    local gt = mp.get_time()
    grab_gap, grab_t = gt - grab_t, gt
    local r = mp.command_native({name = "screenshot-raw", flags = "video"})
    if not r or not r.data or not r.w or r.w <= 0 then
        mp.commandv("script-message", "switch2-grabbed", "")
        return
    end
    local data, w, h, stride = r.data, r.w, r.h, r.stride
    if mode == "light" then data, w, h, stride = light_frame(r) end
    local f = io.open(path, "wb")
    if not f then
        mp.commandv("script-message", "switch2-grabbed", "")
        return
    end
    f:write(data)
    f:close()
    mp.commandv("script-message", "switch2-grabbed", path, tostring(w), tostring(h), tostring(stride), tostring(r.format))
    -- La misma imagen, para ver la consola apagada sin esperar a 3 capturas del lanzador (rejilla de 48x27: ~1 ms, en el
    -- hilo del script) y para el negro / congelada de tick (que ya no toma imagen propia)
    local avg, maxl, fp = sample_raw(r)
    if fp then
        last_shot = gt
        grab_sample = {t = gt, avg = avg, maxl = maxl, fp = fp}
        fast_seen(fp, "captura")
    end
end

-- 08-11: la captura (~11 ms del hilo de mpv) justo tras salir una imagen, no a mitad del hueco hasta la siguiente (si no,
-- una imagen real salia hasta 11 ms tarde: microtirones con la generacion). Maximo 0,15 s de espera
local grab_req
mp.register_script_message("switch2-grab", function(path, mode) grab_req = {path, mode, mp.get_time()} end)

-- 10-11: diagnostico del tramado: n imagenes SEGUIDAS de la captura tal cual (sin shader), una en cada imagen nueva, a
-- <prefijo>_<i>.raw; al acabar, "switch2-burst-done <prefijo> <n> <w> <h> <stride> <formato>" (el lanzador las pasa a PNG)
local burst
mp.register_script_message("switch2-burst", function(prefix, n)
    burst = {prefix = prefix, n = math.max(1, math.min(4, tonumber(n) or 2)), i = 0, t = mp.get_time()}
end)
local function burst_step()
    local b = burst
    if not b then return false end
    local r = mp.command_native({name = "screenshot-raw", flags = "video"})
    if r and r.data and r.w and r.w > 0 then
        local f = io.open(b.prefix .. "_" .. b.i .. ".raw", "wb")
        if f then f:write(r.data); f:close() end
        b.w, b.h, b.stride, b.fmt = r.w, r.h, r.stride, r.format
        b.i = b.i + 1
    else
        b.n = b.i -- sin imagen: se acaba con las que haya
    end
    if b.i >= b.n then
        burst = nil
        mp.commandv("script-message", "switch2-burst-done", b.prefix, tostring(b.i), tostring(b.w or 0), tostring(b.h or 0),
                    tostring(b.stride or 0), tostring(b.fmt or ""))
    end
    return true
end

mp.observe_property("time-pos", "number", function()
    if burst_step() then return end -- (la captura normal espera a la siguiente imagen)
    local g = grab_req
    if g then grab_req = nil; do_grab(g[1], g[2]) end
end)
mp.add_periodic_timer(0.1, function()
    local b = burst
    if b and mp.get_time() - b.t > 3 then burst_step() end -- (sin imagenes nuevas: que no se quede a medias)
    local g = grab_req
    if g and mp.get_time() - g[3] > 0.15 then grab_req = nil; do_grab(g[1], g[2]) end
end)

-- El lanzador envía "sound" / "silent" cada ~0,5 s por la tubería IPC
mp.register_script_message("switch2-audio", function(state)
    local now = mp.get_time()
    if state ~= "silent" then
        silent_since = nil
    elseif ext_audio ~= "silent" or now - ext_audio_t >= 3 or not silent_since then
        silent_since = now -- (empieza el silencio: nosig_fast_secs s mirando si sale "No Signal")
    end
    ext_audio, ext_audio_t = state, now
end)

-- Fin de stream o error con detección armada => apagada
local pending_end = nil
mp.register_event("end-file", function(ev)
    -- (16-11: arrancando un juego, el cambio de modo de video podria cortar la captura: se reabre, no se cierra todo)
    if (switching() or launching()) and (ev.reason == "eof" or ev.reason == "error") then
        msg.warn("la captura nueva no se pudo abrir (" .. ev.reason .. ")")
        mp.commandv("script-message", "switch2-switch-failed", ev.reason)
        return
    end
    if o.enabled and armed and (ev.reason == "eof" or ev.reason == "error") then
        pending_end = "end-file-" .. ev.reason
        o.quit_delay = 0   -- salir ya, antes de que mpv cierre por su cuenta
        trigger(pending_end)
    end
end)
-- El hook bloquea al core hasta que se procese el quit (garantiza el código 42)
mp.add_hook("on_after_end_file", 50, function()
    if pending_end then mp.command("quit " .. o.exit_code) end
end)

mp.add_key_binding("ctrl+p", "switch2-power-toggle", function()
    o.enabled = not o.enabled
    reset_counters()
    last_pos = nil
    local t = "Detección de apagado · " .. (o.enabled and "activada" or "desactivada")
    msg.info(t)
    mp.osd_message(t, 2)
end)

mp.add_key_binding("ctrl+P", "switch2-power-debug", function()
    o.debug = not o.debug
    mp.osd_message("switch2_power debug: " .. (o.debug and "on" or "off"), 2)
end)
