-- switch2_diag.lua
-- Diagnostico de tirones: cada 20 s dice al lanzador como han salido las imagenes de mpv (se apunta en switch2.log). Va en un
-- script aparte porque las capturas de switch2_power.lua retrasan a los observadores de su propio script (medido el 02-11).
--   hora: cuanto paso de verdad entre dos imagenes. marca: su marca de tiempo (la de la capturadora).
--   Si la marca salta, la imagen ya llego tarde o falto en la captura (consola, capturadora o el filtro de 30 fps).
--   Si salta la hora y la marca no, se retraso mpv. Si no salta ninguna y LS ve un tiron, el tiron es de LS o de la grafica.
local mp = require "mp"

local last_w, last_p
local n, over_w, over_p, late, max_w, max_p, sum_w = 0, 0, 0, 0, 0, 0, 0

local function reset()
    n, over_w, over_p, late, max_w, max_p, sum_w = 0, 0, 0, 0, 0, 0, 0
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
        mp.commandv("script-message", "switch2-diag", string.format(
            "%d imagenes, media %.1f ms | tarde por la hora %d (peor %.0f ms), de ellas solo mpv %d | saltos en la marca de la capturadora %d (peor %.0f ms)",
            n, sum_w / n, over_w, max_w, late, over_p, max_p))
    end
    reset()
end)
