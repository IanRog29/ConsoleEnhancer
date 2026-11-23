using System;
using System.Globalization;

namespace ConsoleEnhancer.Motor
{
    // EngineX4D5.cs - 23-11-2026 (1.2.4 ronda 7, agente EFGFIX). El usuario: "no se porque con dlss 5 activado no dejas activar el x4 de smooth
    // y enhance". x4 CON DLSS 5 (Smooth Motion x4 = sm=3, XeSS FG x2 del motor + Smooth Motion del driver; Enhanced FG x3 / x4 = efg=3|4): el
    // lanzador ya lo deja elegir y aqui se hace que quepa. Lo de siempre (EngineAdapta) mira el p95 de la grafica del MOTOR, que no ve lo que
    // hacen XeSS FG ni el driver en sus colas: con Smooth Motion x4 + DLSS 5 al 45 % el motor iba a 13,8 ms (cabia de sobra) y se perdian
    // reales (27,6 de 29,97; medido con el video real de GTA V a 4K, DLSS 5 nivel 4). Este vigilante mira lo que se ve: las reales presentadas y
    // (Enhanced FG) las inventadas saltadas, y si x4 no llega baja el % de DLSS 5 de 10 en 10 hasta el 25 % (y, en el 25 %, una pasada menos):
    //   - "mal" un segundo = menos de x4d5_reales (29,4) reales, 2 o mas tarde, o (Enhanced FG) mas del 3 % de las inventadas saltadas;
    //   - 2 de los ultimos 3 s mal -> DLSS 5 -10 puntos (o una pasada menos) y 8 s sin juzgar (se rehace DLSS 5); el % que no cupo queda como techo
    //     de lo aprendido (EngineAdapta: no se vuelve a subir ahi con esta clave);
    //   - ya en lo minimo (25 %, 1 pasada) y 4 de 6 s mal -> "switch2-x4d5" "no": el lanzador pasa a x2 esta sesion y lo dice;
    //   - 12 s seguidos bien con DLSS 5 bajado -> "switch2-x4d5" "ok" <%>: el lanzador avisa "x4 con DLSS 5: DLSS 5 al N % para que quepa".
    // Primera vez con Smooth Motion x4 + DLSS 5 (sin nada aprendido): se empieza en x4d5_inicio (35 %; medido: 45 % no llega) en vez de lo pedido.
    // Y con sm=3 y DLSS 5 al abrir el motor la cola del motor va en prioridad ALTA (xefg_prioridad_x4, de serie 1; Engine.cs InitDevice): medido
    // con DLSS 5 al 45 %, 27,6 -> 29,6 reales y 107 -> 120 imagenes/s; en la app a 4K con DLSS 5 al 35 %: 30 reales, 119 vistas/s, el 92 % a un
    // refresco. Sin DLSS 5 al abrir, la normal de siempre (INTEGRA7: 99,3 % a un refresco; con la alta y DLSS 5 quitado en marcha salio 76,5 %).
    // Claves: x4d5=0 (sin el vigilante), x4d5_reales=29.4, x4d5_inicio=35 (0 = lo pedido), xefg_prioridad_x4=0 (la cola en normal, como antes).
    partial class Settings
    {
        public bool X4D5 = true;
        public double X4D5Reales = 29.4;
        public int X4D5Inicio = 35;
        public bool XeHighPrioSm4 = true;

        bool ApplyX4D5(string key, string val)
        {
            double d; int n;
            switch (key.Trim().ToLowerInvariant())
            {
                case "x4d5": X4D5 = val.Trim() != "0"; return true;
                case "x4d5_reales": if (double.TryParse(val.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) X4D5Reales = Math.Max(20, Math.Min(29.97, d)); return true;
                case "x4d5_inicio": if (int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) X4D5Inicio = n <= 0 ? 0 : Math.Max(25, Math.Min(100, n)); return true;
                case "xefg_prioridad_x4": XeHighPrioSm4 = val.Trim() != "0"; return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        int x4Hist, x4Hist6, x4OkN;
        long x4CalmaDesde, x4Sk0, x4Gn0, x4NoAt;
        string x4Clave = "";
        int x4OkDicho = -1, x4PctPedido = -1;
        bool x4Inicio;

        bool X4D5Activo { get { return s.X4D5 && s.Mode == 30 && AdNrActivo && (s.SmoothMode == 3 || (s.Efg >= 3 && EfgWanted())); } }

        // SecondTick, tras AdaptaTick
        void X4D5Tick()
        {
            try
            {
                if (!X4D5Activo) { x4Clave = ""; x4Hist = x4Hist6 = x4OkN = 0; return; }
                if (s.Escalera) { X4D5Empezar(); return; }   // (1.3.1, FPS: lo baja EngineEscalera.cs, tambien el reescalado DLSS; esto solo el inicio)
                string clave = s.SmoothMode + "|" + s.Efg + "|" + inW + "x" + inH + "|" + outW + "x" + outH;
                if (clave != x4Clave) { x4Clave = clave; x4CalmaDesde = Clock.Now; x4Hist = x4Hist6 = x4OkN = 0; x4OkDicho = -1; x4PctPedido = adPedidoPct > 0 ? adPedidoPct : s.NrPct; }
                X4D5Empezar();
                long sk = efgSkipped, gn = efgGens;
                long dSk = sk - x4Sk0, dGn = gn - x4Gn0;
                x4Sk0 = sk; x4Gn0 = gn;
                // (sin el foco tampoco: Smooth Motion del driver no genera sin el y las reales salen peor sin que sea la grafica -app entera a
                // 4K del 23-11 19:03: otra ventana cogio el primer plano y x4 bajo DLSS 5 al 25 % por eso-)
                if (inStandby || noSignalScreen || lastCapture == 0 || Clock.SinceMs(lastCapture) > 1500 || Program.FocusHold || !Program.HasFocus()) { x4Hist = x4Hist6 = x4OkN = 0; x4CalmaDesde = Clock.Now; return; }
                if (Clock.SinceMs(x4CalmaDesde) < 8000 || Clock.SinceMs(adCambioAt) < 6000) { x4Hist = x4Hist6 = x4OkN = 0; return; }
                double rate = stats.LastRate;
                int late = stats.LastLate;
                bool efgMal = s.Efg >= 3 && dSk > Math.Max(2, (dGn + dSk) * 0.03);
                bool mal = rate < s.X4D5Reales || late >= 2 || efgMal;
                x4Hist = ((x4Hist << 1) | (mal ? 1 : 0)) & 0x7;
                x4Hist6 = ((x4Hist6 << 1) | (mal ? 1 : 0)) & 0x3F;
                x4OkN = mal ? 0 : x4OkN + 1;
                int pct = Math.Max(25, Math.Min(100, s.NrPct));
                if (Bits(x4Hist) >= 2 && (pct > 25 || s.NrPasses > 1))
                {
                    string por = string.Format(CultureInfo.InvariantCulture, "reales {0:0.0}{1}{2}", rate, late > 0 ? ", tarde " + late : "", s.Efg >= 3 ? ", inventadas saltadas " + dSk + " de " + (dGn + dSk) : "");
                    if (pct > 25)
                    {
                        int sig = Math.Max(25, pct - 10);
                        if (s.Adapta && adTabla != null && adClave.Length > 0) AdaptaTecho("pct", pct);
                        s.NrPct = sig;
                        Log.Write("x4 con DLSS 5: no cabe (" + por + "; grafica del motor p95 " + stats.LastGpuP95.ToString("0.0", IC) + " ms): DLSS 5 al " + pct + " % -> " + sig + " %");
                    }
                    else
                    {
                        s.NrPasses--;
                        Log.Write("x4 con DLSS 5: no cabe ni al 25 % (" + por + "): DLSS 5 con " + s.NrPasses + " pasada(s)");
                    }
                    if (s.Adapta && adTabla != null && adClave.Length > 0) { adCambioAt = Clock.Now; adSubida = ""; AdaptaGuardarFila(); }
                    x4CalmaDesde = Clock.Now; x4Hist = x4Hist6 = x4OkN = 0;
                    return;
                }
                if (pct <= 25 && s.NrPasses <= 1 && Bits(x4Hist6) >= 4 && (x4NoAt == 0 || Clock.SinceMs(x4NoAt) > 30000))
                {
                    x4NoAt = Clock.Now; x4Hist = x4Hist6 = 0;
                    Log.Write("x4 con DLSS 5: no llega ni con DLSS 5 al 25 % (reales " + rate.ToString("0.0", IC) + "): aviso al lanzador (x2)");
                    Ev("switch2-x4d5", "no", "25", rate.ToString("0.0", IC));
                    return;
                }
                // (el aviso solo si se ha bajado de lo pedido: con lo pedido cabiendo no hay nada que decir)
                if (x4OkN >= 12 && x4OkDicho != pct && x4PctPedido > 0 && pct < x4PctPedido)
                {
                    x4OkDicho = pct;
                    Log.Write("x4 con DLSS 5: cabe con DLSS 5 al " + pct + " % (" + s.NrPasses + " pasada(s); reales " + rate.ToString("0.0", IC) + ")");
                    Ev("switch2-x4d5", "ok", pct.ToString(CultureInfo.InvariantCulture), rate.ToString("0.0", IC));
                }
            }
            catch (Exception ex) { Log.Error("x4 con DLSS 5", ex); }
        }

        static int Bits(int v) { int n = 0; while (v != 0) { n += v & 1; v >>= 1; } return n; }

        // (EngineAdapta, clave nueva sin nada aprendido) antes de crear DLSS 5: Smooth Motion x4 + DLSS 5 empieza en x4d5_inicio
        void X4D5ClaveNueva()
        {
            if (!s.X4D5 || s.Mode != 30 || !s.Nr || s.SmoothMode != 3 || s.X4D5Inicio <= 0 || s.NrPct <= s.X4D5Inicio) return;
            Log.Write("x4 con DLSS 5: Smooth Motion x4 sin nada aprendido: DLSS 5 empieza al " + s.X4D5Inicio + " % (pedido " + s.NrPct + " %; x4d5_inicio)");
            s.NrPct = s.X4D5Inicio;
            x4Inicio = true;
            if (s.Escalera && s.Adapta && adTabla != null && adClave.Length > 0) AdaptaGuardarFila();   // (1.3.1, FPS: aprendido; el que se despierta de la espera empieza aqui)
        }

        // La primera vez con Smooth Motion x4 + DLSS 5 (sin fila aprendida y sin adapta): de x4d5_inicio, no de lo pedido (que no llega)
        void X4D5Empezar()
        {
            if (x4Inicio) return;
            x4Inicio = true;
            if (s.SmoothMode != 3 || s.X4D5Inicio <= 0 || s.NrPct <= s.X4D5Inicio) return;
            if (s.Adapta) return;   // (con adapta lo hace X4D5ClaveNueva al ver la clave; lo aprendido manda)
            Log.Write("x4 con DLSS 5: Smooth Motion x4 sin nada aprendido: DLSS 5 empieza al " + s.X4D5Inicio + " % (pedido " + s.NrPct + " %; x4d5_inicio)");
            s.NrPct = s.X4D5Inicio;
            x4CalmaDesde = Clock.Now;
            if (s.Adapta && adTabla != null && adClave.Length > 0) { adCambioAt = Clock.Now; AdaptaGuardarFila(); }
        }
    }
}
