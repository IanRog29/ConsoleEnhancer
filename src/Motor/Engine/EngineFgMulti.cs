using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgMulti.cs - 22-11-2026 (1.2.4, agente MOTORFG). El usuario: "y no se podria pasar todo al motor para que funcione mejor?".
    // DLSS FG x3 / x4 en el motor propio por la via oficial de Streamline: DLSSGOptions.numFramesToGenerate = 2 / 3 (generacion de varias
    // imagenes de NVIDIA). Solo donde el runtime de NVIDIA lo admite (DLSSGState.numFramesToGenerateMax >= 2: RTX 50). En RTX 40 / 30 / 20
    // el runtime da x2 (numFramesToGenerateMax = 1): el motor se queda en x2 y lo dice (switch2-fgmult no <pedido> <da>) para que el
    // lanzador lleve ese multiplicador por Magpie. Regla del usuario (22-11): en el codigo del motor nada que se salte comprobaciones de
    // NVIDIA (ni de Streamline, ni de NGX, ni DLL parcheadas o redirigidas): por eso el motor NO carga el paquete comunitario dlssg_sm86.
    partial class Settings
    {
        public int FgMult = 2;            // fg_mult=2|3|4: imagenes en la tele por cada real con la generacion (DLSS-G, XeSS FG)

        bool ApplyFgMulti(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "fg_mult":
                case "fg_multiplicador":
                    {
                        int n;
                        string v = val.Trim().ToLowerInvariant().TrimStart('x').TrimEnd('x');
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) FgMult = Math.Max(2, Math.Min(4, n));
                        return true;
                    }
                // (EngineFgXe.cs)
                case "fg_dlss_sr": SlSoloDlss = val.Trim() != "0"; return true;
                case "xefg_modo": { int n; if (int.TryParse(val.Trim(), out n)) XeTagMode = n; return true; }
                case "xefg_log": { int n; if (int.TryParse(val.Trim(), out n)) XeLogLevel = n; return true; }
                case "xefg_cola": { int n; if (int.TryParse(val.Trim(), out n)) XeQueue = Math.Max(0, Math.Min(3, n)); return true; }
                case "xefg_prioridad": XeHighPrio = val.Trim() == "1"; return true;
                case "xefg_directo": XeRaw = val.Trim() != "0"; return true;
                case "xefg_xell": XeLowLatency = val.Trim() == "1"; XeNoXell = val.Trim() == "2"; return true;
                case "xefg_hudless": XeHudless = val.Trim() == "1"; return true;
                case "xefg_corte": { double d; if (double.TryParse(val.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) XeSceneThr = d; return true; }
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        uint fgGenMax = 1;                    // numFramesToGenerateMax que dice Streamline (1 = solo x2)
        bool fgGenMaxRead;
        uint fgGenApplied = 1;
        string fgMultSaid = "";

        // Inventadas por cada real que se piden ahora a DLSS-G (1 = x2)
        uint FgGenWanted() { return (uint)Math.Max(1, Math.Min(3, s.FgMult - 1)); }
        // Imagenes en la tele por cada real con la generacion puesta (para el ritmo, VRR y el margen)
        int FgFactor { get { return fgApplied ? (int)fgGenApplied + 1 : ffxFgOn ? 2 : xeFgOn ? XeFgMultNow : 1; } }

        // Lo que admite DLSS-G (numFramesToGenerateMax): con la generacion ya puesta (antes Streamline no lo sabe). Una vez por sesion
        void FgGenMaxRead()
        {
            if (fgGenMaxRead || !fgOk) return;
            SlDLSSGState st;
            SlResult r = Sl.DLSSGGetState(vp, out st);
            if (r != SlResult.eOk) return;
            fgGenMaxRead = true;
            fgGenMax = Math.Max(1, st.numFramesToGenerateMax);
            Log.Write("Generacion: el runtime de NVIDIA admite hasta x" + (fgGenMax + 1) + " (numFramesToGenerateMax " + st.numFramesToGenerateMax + ")");
        }

        // Las inventadas que se piden en el SetOptions de ahora: lo pedido, como mucho lo que admite (antes de saberlo: x2, y en cuanto se
        // sabe, lo pedido si cabe)
        uint FgGenFor(bool fg)
        {
            if (!fg) return 1;
            uint want = FgGenWanted();
            uint n = fgGenMaxRead ? Math.Min(want, fgGenMax) : 1u;
            string said = want + "/" + n;
            if (fgGenMaxRead && want != n && said != fgMultSaid)
            {
                Log.Write("Generacion: x" + (want + 1) + " pedido, el runtime de NVIDIA de esta grafica da x" + (n + 1) + ": se queda en x" + (n + 1));
                try { Ev("switch2-fgmult", "no", (want + 1).ToString(CultureInfo.InvariantCulture), (n + 1).ToString(CultureInfo.InvariantCulture)); } catch { }
            }
            if (fgGenMaxRead) fgMultSaid = said;
            return n;
        }
    }
}
