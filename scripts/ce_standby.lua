-- ce_standby.lua - la sonda de la espera ligera (1.0.14, src\Lanzador\Suspension.cs): mpv sin ventana (--vo=null) mirando la
-- capturadora a poca resolucion. Cada `cada_s` segundos una mirada (screenshot-raw): si NO es la pantalla "No Signal" de la
-- capturadora ni negro del todo en `seguidas` miradas seguidas, la consola se ha encendido -> sale con el codigo 55.
-- Mismo criterio de "No Signal" que ce_power.lua (franjas negras arriba y abajo y un recuadro gris en el centro).
local mp = require 'mp'
local msg = require 'mp.msg'
local options = require 'mp.options'

-- una_vez=yes: una sola mirada y fuera (codigo 55 = imagen, 0 = "No Signal" o negro): la app la lanza cada pocos segundos
local o = { cada_s = 1.0, seguidas = 2, codigo = 55, grid_w = 48, grid_h = 27, negro = 12, una_vez = false }
options.read_options(o, 'ce_standby')

local function muestras()
    local r = mp.command_native({name = "screenshot-raw", flags = "video"})
    if not r then msg.verbose("espera: sin imagen"); return nil end
    if not r.data or not r.w or r.w < 8 then msg.verbose("espera: imagen rara w=" .. tostring(r.w)); return nil end
    local w, h, stride, data = r.w, r.h, r.stride, r.data
    local t = {}
    for j = 0, o.grid_h - 1 do
        local y = math.floor((j + 0.5) / o.grid_h * h)
        for i = 0, o.grid_w - 1 do
            local x = math.floor((i + 0.5) / o.grid_w * w)
            local p = y * stride + x * 4
            local B, G, R = data:byte(p + 1, p + 3)
            t[#t + 1] = {R or 0, G or 0, B or 0}
        end
    end
    return t
end

-- "nosig", "negro" o "imagen"
local function que_es(t)
    local blk_n, blk_ok, box_n, box_ok, maxl = 0, 0, 0, 0, 0
    for j = 0, o.grid_h - 1 do
        local fy = (j + 0.5) / o.grid_h
        for i = 0, o.grid_w - 1 do
            local fx = (i + 0.5) / o.grid_w
            local c = t[j * o.grid_w + i + 1]
            local R, G, B = c[1], c[2], c[3]
            local l = (54 * R + 183 * G + 19 * B) / 256
            if l > maxl then maxl = l end
            if fy < 0.27 or fy > 0.67 then
                blk_n = blk_n + 1
                if l < 20 then blk_ok = blk_ok + 1 end
            elseif fy > 0.33 and fy < 0.63 and fx > 0.10 and fx < 0.90 then
                box_n = box_n + 1
                if l > 110 and l < 235 and math.abs(R - G) < 25 and math.abs(G - B) < 25 then box_ok = box_ok + 1 end
            end
        end
    end
    if blk_n > 0 and box_n > 0 and blk_ok >= blk_n * 0.95 and box_ok >= box_n * 0.75 then return "nosig" end
    if maxl < o.negro then return "negro" end
    return "imagen"
end

local seguidas, ultimo = 0, nil
mp.add_periodic_timer(o.cada_s, function()
    local t = muestras()
    if not t then return end
    local q = que_es(t)
    if q ~= ultimo then msg.info("espera: " .. q); ultimo = q end
    if o.una_vez then mp.command("quit " .. (q == "imagen" and o.codigo or 0)); return end
    if q == "imagen" then seguidas = seguidas + 1 else seguidas = 0 end
    if seguidas >= o.seguidas then
        msg.info("espera: la consola se ha encendido")
        mp.command("quit " .. o.codigo)
    end
end)
