-- switch2_diag.lua
-- Diagnostico de tirones: cada 20 s dice al lanzador como han salido las imagenes de mpv (se apunta en switch2.log). Va en un
-- script aparte porque las capturas de switch2_power.lua retrasan a los observadores de su propio script (medido el 02-11).
--   hora: cuanto paso de verdad entre dos imagenes. marca: su marca de tiempo (la de la capturadora).
--   Si la marca salta, la imagen ya llego tarde o falto en la captura (consola, capturadora o el filtro de 30 fps).
--   Si salta la hora y la marca no, se retraso mpv. Si no salta ninguna y LS ve un tiron, el tiron es de LS o de la grafica.
local mp = require "mp"

local last_w, last_p
local n, over_w, over_p, late, max_w, max_p, sum_w = 0, 0, 0, 0, 0, 0, 0

-- 21-11 (1.2.1): la cola de mpv entre la capturadora y la pantalla: lo que el demuxer ya ha leido y aun no ha pasado a la imagen
-- (demuxer-cache-state fw-bytes, en imagenes de la captura). Con --untimed y sin quitar imagenes, si mpv se retrasa (la grafica ocupada
-- con Magpie) esas imagenes salen tarde y por orden: cada una en cola es una imagen mas de retraso. Se mira 4 veces por segundo y cada
-- 20 s va el maximo, la media y cuantas veces habia 2 o mas
local q_max, q_sum, q_n, q_over = 0, 0, 0, 0
local q_frame, q_frame_at = nil, -10

local function frame_bytes()
    local now = mp.get_time()
    if q_frame and now - q_frame_at < 5 then return q_frame end
    q_frame_at = now
    local vp = mp.get_property_native("video-params")
    if not vp or not vp.w or not vp.h or vp.w <= 0 or vp.h <= 0 then q_frame = nil; return nil end
    local f = vp.pixelformat or ""
    local bpp = 2
    if f == "rgb24" or f == "bgr24" then bpp = 3
    elseif f == "bgr0" or f == "rgb0" or f == "bgra" or f == "rgba" or f == "0rgb" then bpp = 4
    elseif f == "nv12" or f == "yuv420p" or f == "nv21" then bpp = 1.5 end
    q_frame = vp.w * vp.h * bpp
    return q_frame
end

mp.add_periodic_timer(0.25, function()
    local st = mp.get_property_native("demuxer-cache-state")
    if not st or not st["fw-bytes"] then return end
    local fb = frame_bytes()
    if not fb then return end
    local q = st["fw-bytes"] / fb
    q_n = q_n + 1
    q_sum = q_sum + q
    if q > q_max then q_max = q end
    if q >= 2 then q_over = q_over + 1 end
end)

local function reset()
    n, over_w, over_p, late, max_w, max_p, sum_w = 0, 0, 0, 0, 0, 0, 0
    q_max, q_sum, q_n, q_over = 0, 0, 0, 0
end

mp.observe_property("time-pos", "number", function(_, v)
    local t = mp.get_time()
    if not v then last_w, last_p = nil, nil; return end
    if last_w and v > last_p then
        local dw, dp = (t - last_w) * 1000, (v - last_p) * 1000
        if dw < 1000 and dp < 1000 then
            n = n + 1
            sum_w = sum_w + dw
            if dw > max_w then max_w = dw end
            if dp > max_p then max_p = dp end
            local limit = 1.4 * sum_w / n
            if dw > limit and dw - sum_w / n >= 10 then
                over_w = over_w + 1
                if dp <= limit then late = late + 1 end
            end
            if dp > limit and dp - sum_w / n >= 10 then over_p = over_p + 1 end
        end
    end
    last_w, last_p = t, v
end)

mp.add_periodic_timer(20, function()
    if n > 0 then
        local cola = ""
        if q_n > 0 then
            cola = string.format(" | cola de mpv max %.1f imagenes (media %.2f, con 2 o mas %d de %d)", q_max, q_sum / q_n, q_over, q_n)
        end
        mp.commandv("script-message", "switch2-diag", string.format(
            "%d imagenes, media %.1f ms | tarde por la hora %d (peor %.0f ms), de ellas solo mpv %d | saltos en la marca de la capturadora %d (peor %.0f ms)",
            n, sum_w / n, over_w, max_w, late, over_p, max_p) .. cola)
    end
    reset()
end)
