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

        bool CeParseReescalado(string key, string val)
        {
            switch (key)
            {
                case "magpie_nitidez_transformer": MagpieSharpenTransformer = Math.Max(0, Math.Min(1, ParseNum(val))); return true;
            }
            return false;
        }
    }
}
