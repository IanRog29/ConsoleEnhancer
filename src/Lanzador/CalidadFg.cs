// CalidadFg.cs - 22-11-2026 (1.2.2, agente CALIDAD; el usuario: "arregla fallos de barrotes y cosas muy finas con frame generation" y
// "el xefg tiene problemas visuales en la cabeza de los personajes al girar la camara"). Los parametros de los efectos de generacion de
// Magpie (DLSS FG, XeSS FG) que dicen de donde salen los vectores de movimiento: el flujo optico (ninguno, AMD o NVIDIA) y su calidad.
//
// Como lo usa Magpie 0.6.12 (FrameGuidanceService / NvidiaOpticalFlowProvider, leido en su codigo): UN solo estimador de flujo para toda la
// sesion, compartido por todos los efectos que lo piden (DLSS 5, DLSS SR / XeSS SR, DLSS FG, XeSS FG): NVIDIA antes que AMD y la calidad
// MAS ALTA que pida cualquiera. NVOF: 1 = rejilla 4x4 rapida, 2 = 4x4 media, 3 = 4x4 lenta, 4 = rejilla 2x2 media, 5 = 2x2 lenta (con
// comprobacion de ida y vuelta); el campo se lleva a cada punto con interpolacion bilineal. Un efecto con "ninguno" recibe vectores a 0.
// XeSS FG recibe ese campo como vectores de movimiento con la profundidad plana (todo a la misma distancia); DLSS FG igual (profundidad 0).
// Por eso pedir el flujo a la generacion cuando DLSS 5 ya lo calcula (4K: DLSS 5 con NVIDIA calidad 2) no cuesta otro estimador: es el mismo.
//
// Medido (22-11, escenas de prueba Finas.cs por la cadena de verdad: mpv con el video de la escena + Magpie con esta config, cada imagen
// presentada grabada por Desktop Duplication y medida contra la intermedia ideal de las dos reales vistas): ver MEDIDAS abajo.
using System;
using System.Globalization;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // ---- ini (vacio / -1 = el de antes) ----
        // fg_xess_flujo=ninguno|amd|nvidia: los vectores de XeSS FG (antes: dlss_flujo, el de DLSS SR)
        // fg_xess_flujo_calidad=1-5: su calidad NVOF (antes: flujo_calidad_nvidia, 1 en los modos ligeros)
        // fg_dlss_flujo_4k=ninguno|amd|nvidia: los vectores de DLSS FG con la captura de 4K (antes: ninguno)
        // fg_dlss_flujo_calidad_4k=1-5: su calidad NVOF (con nvidia)
        public int XessFgFlow = -1, XessFgFlowQ = -1, DlssFgFlow4k = -1, DlssFgFlowQ4k = -1;

        // Lo que va en la cadena (MagpieConfig.Chain)
        public int XessFgFlowNow { get { return XessFgFlow >= 0 ? XessFgFlow : DlssFlow; } }
        public int XessFgFlowQNow { get { return XessFgFlowQ >= 1 ? XessFgFlowQ : FlowQualityNvNow; } }
        public int DlssFgFlowNow { get { return Win4k ? (DlssFgFlow4k >= 0 ? DlssFgFlow4k : 0) : CapMode ? FgFlow30 : FgFlow60; } }
        public int DlssFgFlowQNow
        {
            get
            {
                int f = DlssFgFlowNow;
                if (f != 2) return FlowQualityNvNow;
                if (Win4k) return DlssFgFlowQ4k >= 1 ? DlssFgFlowQ4k : FlowQualityNvNow;
                return CapMode ? FgFlowQualityNv : FgFlowQualityNv60;
            }
        }

        bool ParseCalidadFg(string key, string val)
        {
            string v = val.Trim();
            switch (key)
            {
                case "fg_xess_flujo": XessFgFlow = v.Length == 0 ? -1 : ParseFlow(v); return true;
                case "fg_xess_flujo_calidad": XessFgFlowQ = v.Length == 0 ? -1 : Math.Max(1, Math.Min(5, ParseInt(v))); return true;
                case "fg_dlss_flujo_4k": DlssFgFlow4k = v.Length == 0 ? -1 : ParseFlow(v); return true;
                case "fg_dlss_flujo_calidad_4k": DlssFgFlowQ4k = v.Length == 0 ? -1 : Math.Max(1, Math.Min(5, ParseInt(v))); return true;
            }
            return false;
        }
    }
}
