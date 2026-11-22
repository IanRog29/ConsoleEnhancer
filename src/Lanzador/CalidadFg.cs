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
// MEDIDO (22-11, RTX 4060 Ti, sin pantalla: la tele estaba en reposo y ni Desktop Duplication ni la captura de Magpie iban; el flujo de NVIDIA
// por la API del driver, nvofapi64 por CUDA, con los mismos perfiles que Magpie, sobre las escenas de prueba src\Motor\Engine\Finas.cs:
// barrotes finos / cables / malla / hierba en paneo y un personaje quieto con la ciudad corriendo detras; 36 pares de imagenes por caso).
// Coste del estimador por imagen real (ida + vuelta, como lo pide Magpie), ms:
//                 1 (4x4 rapida)   2 (4x4 media)   3 (4x4 lenta)   4 (2x2 media)   5 (2x2 lenta)
//      1080p          2,5              3,0             5,4-5,9          9,7-10,0        15,5-16,0
//      1440p          5,4              5,2-6,4        10,4-11,6        16,7-17,8        26,6-28,1
//      4K             6,3              9,2-10,2       22,6-23,1        36,4-37,0        58,8-59,4
// (la 3 a 4K: los 23 ms que bajaban el modo 30 a 23 imagenes reales el 16-11; 4 y 5 a 4K no caben en los 33 ms de una imagen a 30 fps).
// Error de los vectores contra el movimiento de verdad (px de la captura, media; entre parentesis 1080p / 1440p / 4K):
//   barrotes finos: calidad 2 la mejor (1,16 / 1,63 / 2,03); 3 peor (1,49 / 2,18 / 3,19); 4 (1,39 / 1,57 / 2,69); 1 (1,75 / 2,51 / 3,40)
//   fondo con detalle que se repite (ventanas): 2 (10 / 13 / 18) frente a 3 (51 / 57 / 54) y 4 (20 / 33 / 49): las lentas se van de periodo
//   borde de la cabeza por dentro: 2 y 3 ~0,5, 4 y 5 ~0,25; el fondo que se tapa y destapa junto a la cabeza: igual con todas (6-13 px)
// Error fotometrico (luma 0-255: la anterior movida con los vectores contra la actual, lo que la generacion tiene que acertar; 1080p /
// 1440p / 4K): barrotes finos 2 = 2,85 / 4,11 / 2,34; 3 = 3,22 / 5,55 / 3,37; 4 = 2,22 / 3,41 / 1,75; SIN vectores 14,8 / 15,5 / 17,4.
// El cielo junto a lo fino: 2 = 4,1 / 5,3 / 3,3; 3 = 4,9 / 7,4 / 4,7; sin vectores ~18. Borde de la cabeza por dentro: 2 = 2,9 / 2,2 /
// 2,0; 3 = 3,1 / 2,4 / 2,1; 4 = 1,9 / 1,1 / 0,8 (la rejilla 2x2 lo deja en la mitad, pero no lo de fuera de la cabeza, y cuesta ~11 ms
// mas de flujo a 1440p: retraso; a 4K no cabe). La interfaz quieta sale con vectores del fondo con cualquier calidad (4-10 niveles).
// Conclusion: la 2 es la mejor y de las baratas en todas las capturas; la rejilla 2x2 no arregla lo de junto a la cabeza (es la oclusion,
// que ningun flujo resuelve sin profundidad) y no cabe a 4K. XeSS FG se queda en la 2 (como antes; fg_xess_flujo_calidad=4 para probar la
// rejilla fina a 1440p: borde de la cabeza mas limpio a cambio de ~11 ms mas de retraso); DLSS FG del modo 30 pasa de la 3 a la 2
// (fg_flujo_calidad, Switch2Magpie.cs) y a 4K, que iba sin vectores (16-11: la 3 a 4K costaba demasiado), los comparte con DLSS 5 (la 2: el
// mismo estimador, sin coste de mas). Con flujo "ninguno" DLSS FG cree la escena quieta y en lo fino que se repite mezcla las dos imagenes
// (16-11, cesped de FC 26 a 1080p: por eso el modo 30 ya llevaba vectores fuera de 4K).
// SIN MEDIR en la tele (la imagen inventada de verdad): las herramientas quedan en el informe del agente (fgcal, mp_corre.ps1).
using System;
using System.Globalization;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // ---- ini (vacio / -1 = de serie) ----
        // fg_xess_flujo=ninguno|amd|nvidia: los vectores de XeSS FG (de serie: dlss_flujo, como antes)
        // fg_xess_flujo_calidad=1-5: su calidad NVOF (de serie: flujo_calidad_nvidia, 1 en los modos ligeros, como antes)
        // fg_dlss_flujo_4k=ninguno|amd|nvidia|auto: los vectores de DLSS FG con la captura de 4K. De serie (auto): en el modo 30, los de NVIDIA SOLO si DLSS 5 ya
        //   los calcula (el mismo estimador para los dos: no cuesta otro); si no, ninguno (como antes). ninguno = como en la 1.2.1
        // fg_dlss_flujo_calidad_4k=1-5: su calidad NVOF (de serie la de DLSS 5: el mismo estimador)
        public int XessFgFlow = -1, XessFgFlowQ = -1, DlssFgFlow4k = -1, DlssFgFlowQ4k = -1;

        // DLSS 5 (el filtro DLSSNR de Magpie) va con el flujo de NVIDIA: el estimador ya esta en marcha a la calidad FlowQualityNvNow
        public bool Dlss5NvofNow { get { return D5Tuned && Dlss5FlowNow == 2; } }

        // Lo que va en la cadena (MagpieConfig.Chain)
        public int XessFgFlowNow { get { return XessFgFlow >= 0 ? XessFgFlow : DlssFlow; } }
        public int XessFgFlowQNow { get { return XessFgFlowQ >= 1 ? XessFgFlowQ : FlowQualityNvNow; } }
        public int DlssFgFlowNow
        {
            get
            {
                if (!Win4k) return CapMode ? FgFlow30 : FgFlow60;
                if (DlssFgFlow4k >= 0) return DlssFgFlow4k;
                return CapMode && Dlss5NvofNow ? 2 : 0;   // (el 60 a 4K como antes: sin vectores)
            }
        }
        public int DlssFgFlowQNow
        {
            get
            {
                int f = DlssFgFlowNow;
                if (f != 2) return FlowQualityNvNow;
                if (Win4k) return DlssFgFlowQ4k >= 1 ? DlssFgFlowQ4k : FlowQualityNvNow;   // (la de DLSS 5: el mismo estimador, sin coste de mas)
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
                case "fg_dlss_flujo_4k": DlssFgFlow4k = v.Length == 0 || v.ToLowerInvariant() == "auto" ? -1 : ParseFlow(v); return true;
                case "fg_dlss_flujo_calidad_4k": DlssFgFlowQ4k = v.Length == 0 ? -1 : Math.Max(1, Math.Min(5, ParseInt(v))); return true;
            }
            return false;
        }
    }
}
