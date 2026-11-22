// Reescalado.cs - 22-11-2026 (1.2.2, agente REESCALADO). El usuario: "Mejora la calidad de los diferentes reescalados, es decir, que no
// tengan fallos visuales, borrosidades y cosas asi al mover camara" y "Reduce errores visuales con dlss 5 y sus modos".
//
// Lo que se sabe del Magpie 0.6.12 (su codigo, v0.6.12-experimental; ver tambien magpie\DLSSNR-MULTIPASS.md):
//  - El antiparpadeo de DLSSNR (antiFlicker 1-4) va UNA vez al final de la cadena, sobre la correccion total (salida - entrada), igual con
//    1, 2 o 3 pasadas, y nunca vuelve a entrar en el modelo. Peso de la historia exp(-dt/antiFlickerTimeMs) por lo que se parece la entrada
//    (3x3) a la anterior; los modos con flujo (2-4) reproyectan la historia con el flujo optico y la tiran donde el flujo salta mas de 2 px.
//    Nunca mezcla imagen vieja (la base es siempre la imagen de ahora): como mucho, correcciones viejas donde la entrada no ha cambiado.
//  - El flujo optico de DLSSNR (opticalFlowMethod != 0, dlss5_flujo=nvidia de serie) se calcula en CADA captura aunque el antiparpadeo
//    este a 0 (va al modelo como sus vectores) y lo comparte toda la cadena (DLSS SR, XeSS, FSR, DLSS FG) a la calidad mas alta que pida
//    alguien. O sea: el antiparpadeo con flujo no paga flujo de mas; cuesta una pasada a la resolucion de la ventana (+ 4-5 texturas
//    RGBA16F: ~300 MB a 4K, ~140 MB a 1440p).
//  - Con la entrada reducida (inputResolutionPercent < 100) la correccion se calcula pequeña y se sube con Catmull-Rom: el "grano" de la
//    correccion salta de una imagen a otra al moverse la camara (lo dice su propia documentacion de diseño). Es el parpadeo que se veia con
//    1 pasada a 4K (45-55 %) en el modo 30, donde desde el 16-11 iba SIN antiparpadeo (para dejar sitio a DLSS 4.5).
//  - DLSS SR de Magpie: vectores = el flujo compartido (de la imagen de ahora a la anterior, en px de la entrada), sin jitter, profundidad 0,
//    BiasCurrentColorMask 0,5 en toda la imagen; sin flujo (opticalFlowMethod 0) DLSS cree la escena quieta.
//
// Cambios (cada uno con su clave para volver a lo de antes):
//  1) Modo 30 con 1 pasada: antiparpadeo 3 (con flujo, el mismo que con 2-3 pasadas) en vez de 0 (Config.Af30OnePass, base.ini
//     dlss5_antiparpadeo_30_1pasada=3; =0 como antes). La escala automatica lo cuenta (EscalaAuto.CosteDlss5: +CosteAntiParpadeo).
//  2) Enfoque CAS de Magpie tras DLSS SR con los modelos transformer (J/K/L/M): x magpie_nitidez_transformer (de serie 0,6; 1 = como
//     antes). El motor ya lo hacia (motor_nitidez_transformer): con K y CAS 0,5 sale un halo oscuro alrededor de las letras; a 0,3 limpio.
//     Magpie seguia dando 0,5 a 1080p (0,25 con 2-3 pasadas) a K/L/M.
//  3) "Sin reescalado" en el motor (Catmull-Rom) sin enfoque CAS, como en Magpie (x enfoque_sin_reescalado, de serie 0; 1 = como antes).
//  4) DLSS SR de Magpie con L / M (transformer 2) con el flujo optico de DLSS 5 aunque el modo diga "sin flujo" (flujo_30_fg_dlss5=ninguno):
//     sin vectores L / M borran la textura al mover la camara (dlss_flujo_transformer=nvidia de serie; vacio = como antes).
//  5) DLSS 5 con 2-3 pasadas y la captura 4K (entrada al 35 / 30 %): menos color lavado y halo (dlss5_pasadas_color_4k=0.5 y en
//     base.ini detalle_30[_fg]_4k_p2/_p3_dlss5=0.5, contener_30[_fg]_4k_p2/_p3_dlss5=0.85).
// C# 5
using System;
using System.Globalization;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // (2) magpie_nitidez_transformer: el enfoque CAS de Magpie con DLSS SR J/K/L/M, por esto (0-1; 1 = como antes)
        public double MagpieSharpenTransformer = 0.6;

        // El enfoque CAS de Magpie de ahora (SharpenNow) con el factor de los modelos transformer de DLSS SR
        public double SharpenMagpieNow
        {
            get
            {
                double s = SharpenNow;
                if (ScalerNow == "dlss" && EsTransformer(DlssModelNow)) s *= MagpieSharpenTransformer;
                return s;
            }
        }

        // Modelos de DLSS SR transformer (DLSS 4 / 4.5): J 10, K 11, L 12, M 13
        public static bool EsTransformer(int preset) { return preset >= 10 && preset <= 13; }

        // (1) Lo que cuesta de grafica el antiparpadeo de DLSS 5 por imagen real (ms en la RTX 4060 Ti de referencia, a la resolucion de la
        // ventana que recibe Magpie): una pasada que lee y escribe 5-6 texturas RGBA16F de ese tamaño (~0,18 ms por megapixel: 4K 1,5 ms,
        // 1440p 0,66, 1080p 0,37). Solo se suma donde es NUEVO en la 1.2.2 (1 pasada en el modo 30): lo demas ya iba con antiparpadeo
        // cuando se midieron las escalas de referencia
        public static double CosteAntiParpadeoMs(int venW, int venH)
        {
            return 0.18 * Math.Max(0, venW) * (double)Math.Max(0, venH) / 1e6;
        }

        // (3) enfoque_sin_reescalado: el enfoque CAS del motor con "sin reescalado" (Catmull-Rom, del menu de ajustes), por esto (0-1). Magpie
        // nunca enfocaba ahi (estirado simple con bicubico); el motor ponia 0,5 a 1080p (log del usuario, 21-11 03:08: "catrom 1920x1080 ->
        // 3840x2160, enfoque CAS 0.50"), halos en los bordes que el estirado no tiene. 1 = como antes
        public double SharpenNoScale = 0;

        // (4) dlss_flujo_transformer=ninguno|amd|nvidia (vacio = el del modo): el flujo optico de DLSS SR de Magpie con los modelos L / M
        // (transformer 2). Con DLSS 5 en el modo 30 con generacion DLSS SR va sin flujo (flujo_30_fg_dlss5=ninguno: con el, el HUD quieto
        // sobre el fondo que se mueve salia con letras partidas) y L / M sin vectores creen la escena quieta: en movimiento borran textura
        // (detalle movido/quieto 0,60 con L; K 0,90, GTA 19-11). Elegidos en el menu de ajustes, ese era el "borroso al mover la camara".
        // MEDIDO 22-11 (cadena de verdad mpv + Magpie con DLSS 5 nivel 3 al 70 %, captura 1080p, M; capturas de Magpie con la tele en
        // reposo): con el flujo, paneo rapido detalle 0,48 -> 0,73 de la verdad y error 12,5 -> 11,3; paneo lento error 8,9 -> 8,2 y la
        // correccion entre capturas 4,4 -> 3,0; el anillo alrededor del HUD casi igual (7,0 -> 7,3 lento, 13,3 -> 12,1 rapido).
        // Solo donde el flujo ya se calcula para DLSS 5 (dlss5_flujo, compartido por toda la cadena de Magpie): no cuesta nada mas.
        // De serie nvidia; vacio = como antes (el del modo, flujo_<modo>_dlss5)
        public int SrFlowTransformer2 = 2;
        public int DlssFlowSrNow
        {
            get
            {
                int f = DlssFlowNow;
                if (f > 0 || SrFlowTransformer2 < 0 || !D5Tuned || Dlss5FlowNow <= 0) return f;
                int m = DlssModelNow;
                // (el mismo proveedor que DLSS 5 -AMD o NVIDIA-: el flujo que ya se calcula)
                return m == 12 || m == 13 ? (SrFlowTransformer2 > 0 ? Dlss5FlowNow : 0) : f;
            }
        }

        // (5) dlss5_pasadas_color_4k: Chroma Strength del residuo de DLSS 5 con 2-3 pasadas y la captura 4K (vacio = dlss5_pasadas_color, 0,7,
        // el de antes). Con la entrada al 35 / 30 % el color salia lavado: MEDIDO 22-11 (cadena de verdad, capturas de Magpie, nivel 3):
        // color de la salida frente a la verdad 2 pasadas 0,82 -> 0,90 y 3 pasadas 0,79 -> 0,88 (1 pasada 0,95), junto con detalle 0,5 y
        // contener 0,85 de base.ini (detalle_30_4k_p2_dlss5...: halo claro 9,5 -> 8,5 % con 2 pasadas, error 9,5 -> 9,2)
        public double Dlss5MultiChroma4k = 0.5;
        public double Dlss5MultiChromaNow { get { return Win4k && Dlss5MultiChroma4k >= 0 ? Dlss5MultiChroma4k : Dlss5MultiChroma; } }

        bool CeParseReescalado(string key, string val)
        {
            switch (key)
            {
                case "magpie_nitidez_transformer": MagpieSharpenTransformer = Math.Max(0, Math.Min(1, ParseNum(val))); return true;
                case "enfoque_sin_reescalado": SharpenNoScale = Math.Max(0, Math.Min(1, ParseNum(val))); return true;
                case "dlss_flujo_transformer": SrFlowTransformer2 = val.Trim().Length == 0 ? -1 : ParseFlow(val); return true;   // (vacio = como antes)
                case "dlss5_pasadas_color_4k": Dlss5MultiChroma4k = val.Trim().Length == 0 ? -1 : Math.Max(0, Math.Min(2, ParseNum(val))); return true;   // (vacio = como antes)
            }
            return false;
        }
    }
}
