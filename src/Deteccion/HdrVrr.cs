// HdrVrr.cs - HDR, RTX HDR y VRR en el plan (20-11-2026, agente CE-FUNCIONES, ConsoleEnhancer)
//
// Lo detectado ya estaba (Pantallas.cs: HDR de Windows, nits de DXGI_OUTPUT_DESC1, VRR del EDID / FreeSync / G-SYNC por NVAPI, tearing de
// DXGI). Aqui se decide lo que se OFRECE y lo de serie (prudente). Lo que se hace con ello: src\Lanzador\ImagenCe.cs.
//  - RTX HDR (la imagen SDR de la consola en HDR, el del driver de NVIDIA): RTX 20 o posterior, driver 551.52+, Windows 11 22H2+ (lo pide
//    el driver: WDDM 3.1) y pantalla HDR. APAGADO de serie (cambia el aspecto y cuesta grafica). Brillo maximo = el de la pantalla
//    (400-2000 nits), gris medio sensato (RtxHdrValores), contraste y saturacion neutros.
//  - Auto HDR de Windows: no se ofrece (solo para los juegos de la lista de Microsoft; para un programa cualquiera haria falta un truco).
//  - VRR: auto = encendido solo si la pantalla lo tiene, DXGI deja presentar sin sincronizar (tearing) y, en NVIDIA, G-SYNC se ha VISTO
//    funcionando en esa pantalla (NvAPI_Disp_GetVRRInfo: activo / pedido / en modo VRR ahora, o recordado de otra vez: esos bits cambian
//    con lo que este abierto) y no esta apagado en el perfil global (0x1094F1F7 = 0). AMD / Intel: auto = apagado (no se sabe si
//    FreeSync esta encendido): vrr=1 en el asistente.
using System;
using System.Text;

namespace ConsoleEnhancer.Deteccion
{
    public sealed partial class PlanImagen
    {
        public bool HdrPantalla, HdrWindows;          // la pantalla admite HDR / el HDR de Windows esta encendido ahora
        public double NitsPantalla;                    // brillo maximo que da la pantalla (DXGI; 0 = no se sabe)
        public bool RtxHdrPosible; public string RtxHdrMotivo = "";
        public int RtxHdrNits = 1000, RtxHdrGris = 50, RtxHdrContraste = 100, RtxHdrSaturacion = 100;
        public bool VrrPosible; public bool VrrDeSerie; public string VrrMotivo = "";

        string InformeHdrVrr()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("  HDR: " + (HdrPantalla ? Tx.T("pantalla HDR", "HDR display") + (NitsPantalla > 0 ? " " + Tx.N(NitsPantalla, "0") + " nits" : "") +
                                                     (HdrWindows ? Tx.T(" (HDR de Windows encendido)", " (Windows HDR on)") : Tx.T(" (HDR de Windows apagado)", " (Windows HDR off)")) : Tx.T("la pantalla no tiene HDR", "no HDR display")) +
                          " | RTX HDR: " + (RtxHdrPosible ? Tx.T("posible (apagado de serie; ", "available (off by default; ") + RtxHdrNits + " nits)" : "no - " + RtxHdrMotivo));
            sb.AppendLine("  VRR: " + (VrrPosible ? (VrrDeSerie ? Tx.T("encendido de serie", "on by default") : Tx.T("posible, apagado de serie", "available, off by default")) : "no") +
                          (VrrMotivo.Length > 0 ? " - " + VrrMotivo : ""));
            return sb.ToString();
        }
    }

    public static class PlanHdrVrr
    {
        // RTX HDR del driver (NVIDIA App): desde el 551.52 (febrero de 2024), RTX 20 o posterior, Windows 11 22H2 (22621) o posterior
        public const int RtxHdrDriverMin = 55152, RtxHdrWindowsMin = 22621;

        public static void Elegir(Hardware hw, PlanImagen p, PreferenciasPlan pref)
        {
            PantallaInfo pa = p.Pantalla;
            GpuInfo g = p.Gpu;
            p.HdrPantalla = pa != null && (pa.HdrPosible || pa.HdrActivo);
            p.HdrWindows = pa != null && pa.HdrActivo;
            p.NitsPantalla = pa != null && pa.NitsMax > 1 ? pa.NitsMax : 0;
            int[] v = RtxHdrValores(p.NitsPantalla, 0);
            p.RtxHdrNits = v[0]; p.RtxHdrGris = v[1]; p.RtxHdrContraste = v[2]; p.RtxHdrSaturacion = v[3];

            // ---- RTX HDR
            int build = hw.Sistema.Build > 0 ? hw.Sistema.Build : Nat.OsBuild();
            if (g == null || g.Fab != Fabricante.Nvidia) p.RtxHdrMotivo = Tx.T("solo NVIDIA RTX", "NVIDIA RTX only");
            else if (!g.EsRtx) p.RtxHdrMotivo = Tx.T("pide una RTX (20 o posterior)", "needs an RTX GPU");
            else if (g.DriverNv > 0 && g.DriverNv < RtxHdrDriverMin) p.RtxHdrMotivo = Tx.T("driver anterior a 551.52: actualízalo", "driver older than 551.52");
            else if (build > 0 && build < RtxHdrWindowsMin) p.RtxHdrMotivo = Tx.T("pide Windows 11 22H2 o posterior", "needs Windows 11 22H2 or later");
            else if (!p.HdrPantalla) p.RtxHdrMotivo = Tx.T("la pantalla no tiene HDR", "the display has no HDR");
            else p.RtxHdrPosible = true;
            if (p.RtxHdrPosible)
                p.Motivos.Add(Tx.T("RTX HDR posible (imagen SDR de la consola a HDR): apagado de serie; asistente o bandeja", "RTX HDR available: off by default (wizard or tray)") +
                              (p.HdrWindows ? "" : Tx.T(" (la app enciende el HDR de Windows mientras está abierta)", " (the app turns Windows HDR on while open)")));

            // ---- VRR
            string motivo;
            p.VrrDeSerie = VrrAuto(pa, g != null && g.Fab == Fabricante.Nvidia, pref != null && pref.VrrVisto, pref != null ? pref.GsyncGlobal : -1, out motivo);
            p.VrrPosible = pa != null && pa.VrrPosible && pa.Tearing;
            p.VrrMotivo = motivo;
            p.UsarVrr = p.VrrPosible && p.VrrDeSerie;
            if (p.VrrPosible) p.Motivos.Add("VRR: " + (p.VrrDeSerie ? Tx.T("encendido (", "on (") : Tx.T("apagado de serie (", "off by default (")) + p.VrrMotivo + ")");
        }

        // vrr=auto: ¿encendido? (tambien lo usa el lanzador al arrancar, src\Lanzador\ImagenCe.cs). visto = G-SYNC visto funcionando en esta
        // pantalla otra vez; global = VRRREQUESTSTATE del perfil global de NVIDIA (0 apagado, 1 pantalla completa, 2 tambien en ventana; -1 no se sabe)
        public static bool VrrAuto(PantallaInfo pa, bool nvidia, bool visto, int global, out string motivo)
        {
            if (pa == null || !pa.VrrPosible) { motivo = Tx.T("la pantalla no lo dice (EDID / driver)", "the display does not report it"); return false; }
            if (!pa.Tearing) { motivo = Tx.T("DXGI no deja presentar sin sincronizar (driver o Windows antiguos)", "DXGI does not allow tearing"); return false; }
            if (nvidia && pa.VrrNvidia >= 0)
            {
                if (global == 0) { motivo = Tx.T("G-SYNC apagado en el panel de NVIDIA: de serie no", "G-SYNC is off in the NVIDIA panel"); return false; }
                bool ahora = (pa.VrrNvidia & (1 | 4 | 16)) != 0;
                if (ahora || visto) { motivo = ahora ? Tx.T("G-SYNC activo ahora en esta pantalla", "G-SYNC active on this display now") : Tx.T("G-SYNC visto funcionando en esta pantalla", "G-SYNC seen working on this display"); return true; }
                motivo = Tx.T("G-SYNC posible pero aún no visto funcionando en esta pantalla: de serie no (vrr=1 lo enciende)", "G-SYNC capable but not seen working yet: off by default (vrr=1 turns it on)");
                return false;
            }
            motivo = Tx.T("no se sabe si FreeSync / VRR está encendido en el driver: de serie no (vrr=1 lo enciende)", "can't tell if FreeSync/VRR is on in the driver: off by default (vrr=1)");
            return false;
        }

        // Valores sensatos de RTX HDR: { pico (nits), gris medio (nits), contraste, saturacion } (contraste / saturacion: 100 = neutro, la
        // codificacion del perfil = lo de la NVIDIA App + 100). Pico = el de la pantalla (lo de serie de la NVIDIA App), 400-2000. Gris medio:
        // con el blanco SDR de Windows conocido (sdrBlanco, nits), el gris del 50 % con gamma 2,2 (blanco x 0,5^2,2: 203 -> 44); si no, 50 (lo de
        // serie de NVIDIA); entre 35 y 60
        public static int[] RtxHdrValores(double nits, double sdrBlanco)
        {
            int pico = nits >= 300 ? (int)Math.Round(Math.Max(400, Math.Min(2000, nits)) / 10) * 10 : 1000;
            int gris = sdrBlanco >= 120 ? (int)Math.Round(Math.Max(35, Math.Min(60, sdrBlanco * Math.Pow(0.5, 2.2)))) : 50;
            return new[] { pico, gris, 100, 100 };
        }
    }
}
