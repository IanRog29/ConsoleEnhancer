// EscalaAuto.cs - 21-11-2026 (1.2.1, agente ESCALA): la escala de DLSS 5 (el % de entrada de DLSSNR en Magpie) elegida por la cadena
// entera y vigilada jugando.
//
// El usuario: "Si estoy usando a 1440p 30 imaginate reescalado a 4k y 60, dependiendo del reescalado dlss y tal que use la escala del
// dlss 5 crecera o bajara, eso la aplicacion lo detectara, y ten en cuenta que la aplicacion tambien tendra que detectar si se llegan a
// los fps que se han seleccionado, si no se llega, cambiara la escala del dlss 5 o cambiara algun otro ajuste y con ello avisara de lo
// que ha pasado la app".
//
// Antes (tabla_dlss5_*): un % por modo + tipo de generacion + captura, estimado una vez por la gama de la grafica (Escalas.cs). No
// miraba el modelo de DLSS SR (M cuesta ~5 ms mas que E a 4K en la Ti: con M o con E salia el mismo 40 %), ni la salida, ni el modo 120
// (usaba la tabla del 60 con generacion: 35 % a 120 fps = ~100 reales de 120 en Rocket League, log del 21-11 19:12), y solo se
// corregia "para la proxima vez" (Escalas.Muestra) o quitando pasadas / DLSS 5 (FpsBajo.cs).
//
// Ahora (dlss5_escala_auto=1, de serie):
//  1) MODELO DE COSTE (ms de grafica por imagen REAL, en la RTX 4060 Ti del usuario = indice 1,2; Coste*): lo fijo (captura de la
//     ventana de mpv, mpv, CAS, presentar: 1 + 0,6 ms por megapixel de la ventana; "ligero" x0,55; + el flujo optico de DLSS SR), el
//     escalado a la salida (DLSS E 4,8 / K 6,2 / M 10,1 / L 12,1 ms a 4K, medidos en el motor el 15-11; FSR 1 0,5...), la generacion por
//     imagen real (DLSS FG 5 ms a 4K por cada inventada + sus vectores NVOF, Smooth Motion 2,5, FSR 3 7, XeSS 8, VFG 10) y DLSS 5 =
//     pasadas x 4 ms por megapixel de la ventana x (% de entrada)^2 (x1,15 con la ventana de 4K). Se escala por la grafica (1,2 / indice
//     de Gpu.cs; lo neuronal x1,3 en RTX 20 y x1,1 en RTX 30) y por k (lo medido en esta grafica). El % elegido es el mayor (de 5 en 5,
//     25-100) que cabe en el 75 % del tiempo de una imagen real del modo (dlss5_escala_uso; el minimo "a prueba" si le falta <= 15 %).
//     Calibrado con lo medido en Prisma (LEEME / switch2.ini 14-19/11, imagen de Ocarina en movimiento, 1440p -> 4K, E): 30 + DLSS FG
//     85/60/50 (el modelo 75/55/45), 30 sin FG 100/80/65 (100/70/60), 60 + DLSS FG ligero 40 (35), XeSS FG 30 70/45/35 (70/50/40); con
//     el 75 % y no el 80-85 % de entonces porque la capturadora de verdad cuesta 7-10 puntos mas. Con el modelo de DLSS SR cambia: 1440p
//     30 + FG -> 4K en la Ti E 75 %, K 70 %, M 50 %, L 30 % (antes 40 % con cualquiera); a 120 fps no cabe (DLSS 5 fuera: el motor propio
//     da los 120; al 35 % Magpie recibia ~100 de 120, log del 21-11 19:12).
//  2) VIGILANCIA (cada 5 s, CheckLsPacing; Decidir es una funcion pura): las imagenes reales que recibe Magpie (TODOS sus resumenes
//     "Output cadence": nuevas / tiempo; antes solo el primero), las que da mpv (su script o su recuento de 20 s: a 120 el script se
//     queda corto), las de salida con la generacion de Magpie ((nuevas + inventadas) / tiempo) y el % de grafica (NVAPI,
//     GetDynamicPstatesInfoEx, cada 0,5 s). Si mpv no da el modo entero (el juego va a menos), nada. No llega (menos de lo del modo -
//     max(1, 3 %)) 3 ventanas (15 s), o menos del 85 % 2 ventanas (10 s): se baja YA, en una sola reapertura de Magpie (con el puente:
//     el motor encima, sin telon), lo que falta + un 3 % del tiempo de grafica, en proporcion (lo que quita cada paso sale del modelo
//     en fraccion; DLSS 5 cuenta como un 25 % del tiempo como poco): el % de DLSS 5 (hasta 25; 30 con la ventana de 1080p; como mucho
//     -15 si no falta mucho), una pasada menos, el modelo de DLSS SR (M / L -> K -> E), una generacion mas barata de la lista del modo.
//     Hasta 2 pasos juntos (3 si falta mucho); sin la grafica llena (por debajo del 85 %), solo -5. Si ya no queda nada de eso, la
//     escalera de siempre (FpsBajo: DLSS 5 fuera, FSR 1, 120 -> 60, captura, 60 -> 30). 20 s tras cada cambio sin juzgar (y 25 s de
//     Magpie abierto). El nivel de DLSS 5 no se toca: no cambia el coste. En pantalla: "No se llega a 60 fps (52) · DLSS 5 al 50 % →
//     40 %" (inglés: "60 fps not reached (52): DLSS 5 scale 50% -> 40%").
//  3) SUBIR, con histeresis: 3 min seguidos de sobra (todas las imagenes, sin huecos, la grafica por debajo del 82 %; sin NVAPI, 10 min
//     y +5) -> el % que deja la grafica al 85 % como mucho, +15 como mucho y nunca hasta el % que ya no llego en ese juego y esa cadena
//     (no_pct, 7 dias). Magpie 0.6.12 NO cambia el % de entrada en caliente desde fuera (es un ajuste "estructural": "Restart scaling
//     after changing ... input size", RELEASE-NOTES; sin IPC: solo WM_MAGPIE_QUIT / WM_MAGPIE_SHOWME y atajos; no vigila su
//     config.json; su modo "adaptativo" no tiene topes ni sabe de la generacion): la subida queda pendiente y vale en la siguiente
//     apertura de Magpie (al entrar en un juego desde el menu, o cualquier otra reapertura), nunca reabriendo por ella. El % de cada
//     cadena se congela mientras Magpie siga abierto. Un aviso corto: "Sobra gráfica · DLSS 5 al 50 % → 65 % al volver a entrar".
//  4) APRENDIDO (data\scales_auto.txt, por grafica + driver): k de la grafica (coste medido con el % de grafica / coste del modelo,
//     media movil: para las cadenas sin nada aprendido) y, por juego + cadena (modo, generacion, captura, ventana, salida, escalador; no
//     el modelo de DLSS SR ni las pasadas), el "extra" de ms y el k con los que lo decidido es justo lo mayor que cabe (al bajar, al
//     subir y tras 60 s llegando: lo que llega se queda); ademas el % que no llego (por modelo de DLSS SR y pasadas) y el tope del
//     modelo de DLSS SR. Cambiar luego el modelo (E <-> M) o las pasadas recalcula el % con lo aprendido. Un juego nuevo empieza por lo
//     ultimo aprendido en cualquier juego ("*") con esa cadena. dlss5_escala_auto=0 = como antes (tablas tabla_dlss5_* + Escalas.Muestra).
// Prueba sin consola: ConsoleEnhancer.exe --test-escala[=<informe>] (src\Lanzador\EscalaPrueba.cs).
// C# 5
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    // Lo que cuesta grafica por cada imagen real: la cadena de ahora (Config.CadenaEscalaPara) o la de una prueba
    sealed class CadenaEscala
    {
        public int Modo = 60;               // 30 | 60 | 120
        public double Objetivo = 59.94;     // imagenes reales por segundo que pide el modo
        public int CapW = 2560, CapH = 1440; // la captura
        public int VenW = 2560, VenH = 1440; // la ventana de mpv (lo que recibe Magpie: DLSS 5 trabaja a esto)
        public int OutW = 3840, OutH = 2160; // la pantalla (salida de Magpie)
        public string Gen = "";             // "" sin generacion; dlss | smooth | xess | fsr3 | vfg
        public int Mult = 2;
        public string Esc = "dlss";         // el escalador de Magpie (Config.ScalerNow); "" o ninguno = sin escalar
        public bool Escala = true;          // Config.UpscaleOn
        public int Modelo = 5;              // DLSS SR: 5 E, 10 J, 11 K, 12 L, 13 M
        public bool Ligero;                 // Config.LightNow (60: antiparpadeo estatico, flujo en rendimiento, DLSS FG preset C...)
        public bool FlujoSr = true;         // DLSS SR con flujo optico (dlss_flujo)
        public bool AfUna;                  // (1.2.2) DLSS 5 con 1 pasada lleva antiparpadeo (modo 30: Config.Af30OnePass; antes no)
        public bool VectoresFg;             // DLSS FG de Magpie con sus vectores (fg_flujo_30 / _60, no a 4K)
        public bool Hdr, HdrAntes;          // RTX HDR (a la salida o a la resolucion de la captura)
        public double Indice = 1.2;         // la grafica frente a la RTX 4060 (1,0)
        public string Familia = "rtx40";    // rtx20 | rtx30 | rtx40 | rtx50 | otra
        public bool Intel;                  // (1.2.3, MULTI2) grafica Intel: XeSS FG x3 / x4 por su camino nativo (EscalaAuto.XessCompat)
        public double FlujoD5 = 1;          // (1.2.3) el flujo optico de DLSS 5: 1 = NVIDIA calidad 2, 0,8 = calidad 1 (ligero), 0 = sin flujo
        public bool Vrr = true;             // (1.2.3) Magpie presenta por VRR (Config.MagpieVrrNow); false = refresco fijo (DXGI cada 2 refrescos)
        public double VramGb = 8;
        public string Juego = "";           // el perfil del juego ("" = sin saber)

        public CadenaEscala Copia() { return (CadenaEscala)MemberwiseClone(); }

        // La clave de lo aprendido: todo lo que cambia el coste menos el modelo de DLSS SR, las pasadas y el %
        public string Clave
        {
            get
            {
                return Modo + "|" + (Gen.Length == 0 || Modo >= 100 ? "sin" : Gen + Mult) + "|" + CapW + "x" + CapH + "|v" + VenW + "x" + VenH + "|o" + OutW + "x" + OutH +
                       "|" + (Escala ? (Esc.Length > 0 ? Esc : "ninguno") : "sin_escalar") + (Ligero ? "|ligero" : "") + (Hdr ? "|hdr" : "");
            }
        }
        public string ClaveCompleta(int pasadas) { return Clave + "|m" + (Escala && Esc == "dlss" ? Modelo : 0) + "|p" + pasadas; }
    }

    static class EscalaAuto
    {
        public const double IndiceRef = 1.2;      // RTX 4060 Ti (Gpu.cs), la grafica de las medidas
        // dlss5_escala_uso: parte del tiempo de una imagen real que puede llevarse el hilo de la imagen de Magpie (Prevision). (1.2.3, MULTI2:
        // 95; antes 75 con un modelo que solo miraba la grafica y no lo fijo de DLSS 5 ni el hilo en serie: elegia 45-50 % a 4K con DLSS FG
        // x2 y se quedaba en 28 reales. Con el modelo del hilo, lo medido llega hasta 31,8 ms previstos -XeSS FG x2 al 50 % a 4K, DLSS FG x2
        // al 45 % con refresco fijo de RITMO2- y no llega desde 33,8: el 95 % -31,7 ms- deja ~1,7 ms de margen)
        public static double Uso = 0.95;
        // dlss5_escala_grafica (1.2.3, MULTI2): lo que puede ocupar la grafica (Carga x k) del tiempo de una real. 88: antes, con el 75 % del
        // modelo de solo la grafica, era ~el 75 %; medido llegando con 77-82 % (XeSS FG x2 a 4K al 50 %, DLSS FG x4 a 1440p)
        public static double UsoGrafica = 0.88;
        public static volatile string Juego;      // el juego de ahora (TrayApp.PrepareCfg)

        static double Mp(int w, int h) { return Math.Max(0, w) * (double)Math.Max(0, h) / 1e6; }
        static double Mp4k(int w, int h) { return Mp(w, h) / 8.2944; }

        // ------------------------------------------------------------------ el modelo de coste (ms en la referencia)
        public static double CosteBase(CadenaEscala c)
        {
            double mp = Mp(c.VenW, c.VenH);
            // (a 4K, mas: 18-11, 60 + Smooth Motion a 4K con todo puesto al 25 % = 83 % de grafica; y "ligero" a 4K casi no quita nada: la
            // mascara, el suavizado de mpv y el flujo solo se quitan con claves _4k)
            double b = 1.0 + 0.6 * mp + 0.5 * Math.Max(0, mp - 4);
            if (c.Ligero) b *= c.VenH >= 2000 ? 0.9 : 0.55;
            if (c.FlujoSr && c.Escala && !c.Ligero) b += 0.27 * mp;   // (flujo optico de DLSS SR a la resolucion de la ventana: ~1 ms a 1440p)
            if (c.Hdr) b += 15.0 * (c.HdrAntes ? mp / 8.2944 : Mp4k(c.OutW, c.OutH));   // (RTX HDR: ~15 ms a 4K, medido el 15-11)
            return b;
        }

        public static double CosteEscalado(CadenaEscala c)
        {
            if (!c.Escala || (c.VenW >= c.OutW && c.VenH >= c.OutH)) return 0;
            double f = Mp4k(c.OutW, c.OutH);
            switch (c.Esc)
            {
                case "dlss": return f * (c.Modelo == 13 ? 10.1 : c.Modelo == 12 ? 12.1 : c.Modelo == 11 || c.Modelo == 10 ? 6.2 : 4.8);
                case "vsr": return f * 4.0;
                case "xess": return f * 4.5;
                case "fsr2": case "fsr3": case "fsr4": return f * 3.5;
                case "nvvfx": return f * 4.0;
                case "fsr": return f * 0.5;
                case "sgsr": return f * 0.4;
                default: return f * 0.3;   // lanczos, bicubico, CAS, sin reescalado
            }
        }

        // (1.2.3, MULTI2) DOS cuentas de la generacion (MEDIDO el 22-11, ver Prevision): lo que ocupa la grafica (Carga: lo que ve su %, para
        // aprender k) y lo que alarga el hilo de la imagen de Magpie (lo que decide si se llega a las reales del modo). ms con salida 4K en la
        // RTX 4060 Ti (indice 1,2): DLSS FG ocupa ~5 por inventada (x4 sin DLSS 5 a 4K: nvidia-smi 69 %, 23 ms por real) pero en el hilo
        // la primera ~5 y cada una de mas ~1,5 (DLSS-G reutiliza el flujo del par); XeSS FG ocupa ~8 por inventada pero las genera en su
        // propia cola (x2 con DLSS 5 al 50 % a 4K llega: 29,9): en el hilo ~1. ini: escala_coste_dlss_fg / _mas, escala_coste_xess_fg / _hilo
        public static double CosteDlssFg = 5.0, CosteDlssFgMas = 1.5, CosteXessFg = 8.0, CosteXessFgHilo = 1.0, CreditoFijoDlss = 2.0;

        // lo que ocupa la grafica (el % de grafica lo ve: NuevoK)
        public static double CosteGeneracion(CadenaEscala c) { return CosteGeneracion(c, false); }
        // hilo = true: lo que alarga el hilo de la imagen (Prevision: llegar a las reales)
        public static double CosteGeneracion(CadenaEscala c, bool hilo)
        {
            if (c.Gen.Length == 0 || c.Modo >= 100) return 0;
            double f = Mp4k(c.OutW, c.OutH), g = Math.Max(1, c.Mult - 1), v;
            switch (c.Gen)
            {
                case "smooth": v = 2.5 * f; break;                       // (x2 siempre; ~la mitad que DLSS FG)
                case "fsr3": v = 7.0 * f; break;                         // (x2 fijo)
                case "xess": v = (hilo ? CosteXessFgHilo : CosteXessFg) * g * f + (c.VectoresFg ? 0.4 * Mp(c.VenW, c.VenH) : 0); break;
                case "vfg": v = 10.0 * g * f; break;
                default:   // DLSS FG (+ los vectores NVOF de Magpie fuera de 4K)
                    v = (hilo ? CosteDlssFg + CosteDlssFgMas * (g - 1) : CosteDlssFg * g) * f + (c.VectoresFg ? 0.55 * Mp(c.VenW, c.VenH) : 0);
                    // (x2 con refresco fijo -DXGI cada 2 refrescos, sin la cola por VRR de Magpie-: el hilo espera menos a la cola de
                    // presentacion. RITMO2, 22-11, 4K al 45 %: por VRR 28,6 reales, fijo 29,68 de 29,73; por VRR Magpie anota
                    // "excludedRingWait" ~6 ms con x2 y ~0 con x4. escala_credito_fijo_dlss)
                    if (hilo && g == 1 && !c.Vrr) v = Math.Max(0, v - CreditoFijoDlss * f);
                    break;
            }
            if (c.Ligero) v *= 0.85;
            return v;
        }

        public static double CosteDlss5(CadenaEscala c, int pct, int pasadas)
        {
            if (pasadas <= 0 || pct <= 0) return 0;
            double p = pct / 100.0;
            double d = 4.0 * Mp(c.VenW, c.VenH) * (c.VenH >= 2000 ? 1.15 : 1.0);
            if (c.Ligero) d *= 0.9;
            // (22-11, 1.2.2 REESCALADO: 1 pasada en el modo 30 ahora con antiparpadeo: su pasada, src\Lanzador\Reescalado.cs)
            return pasadas * d * p * p + (pasadas == 1 && c.AfUna ? Config.CosteAntiParpadeoMs(c.VenW, c.VenH) : 0);
        }

        // (1.2.3, MULTI2) Lo fijo de DLSS 5 en el hilo de la imagen que el % de grafica NO ve: el flujo optico de NVIDIA a la resolucion de la
        // ventana (el acelerador de flujo, en serie: DLSSNR lo espera) y lo que DLSSNR hace a esa resolucion antes y despues del modelo
        // (bajar el color, subir el residuo). ms por megapixel de la ventana (escala_coste_dlss5_fijo); lo que ya cuentan los vectores de
        // DLSS FG / el flujo de DLSS SR (el mismo estimador) se descuenta
        // (a 4K, ajustado a lo medido: ~11,6 ms = el flujo de calidad 2 ~9,5 -CALIDAD, 1.2.2: 9,2-10,2- y lo de DLSSNR ~2)
        public static double CosteDlss5FlujoMp = 1.15, CosteDlss5FijoMp = 0.25;
        public static double CosteDlss5Fijo(CadenaEscala c, int pct, int pasadas)
        {
            if (pasadas <= 0 || pct <= 0) return 0;
            double mp = Mp(c.VenW, c.VenH);
            double flujo = CosteDlss5FlujoMp * c.FlujoD5 * mp;
            if (c.FlujoD5 > 0)   // (el mismo estimador que ya cuentan los vectores de DLSS FG / el flujo de DLSS SR)
                flujo -= (c.VectoresFg && c.Gen == "dlss" ? 0.55 * mp : c.VectoresFg && c.Gen == "xess" ? 0.4 * mp : 0) + (c.FlujoSr && c.Escala && !c.Ligero ? 0.27 * mp : 0);
            return Math.Max(0, flujo) + CosteDlss5FijoMp * mp;
        }

        static double Neuronal(CadenaEscala c) { return c.Familia == "rtx20" ? 1.3 : c.Familia == "rtx30" ? 1.1 : 1.0; }
        static double PorGrafica(CadenaEscala c) { return IndiceRef / Math.Max(0.05, c.Indice); }

        // ms del hilo de la imagen de Magpie por imagen real con este % y pasadas (k: lo medido en esta grafica; extra: lo aprendido de esta
        // cadena). (1.2.3, MULTI2) MEDIDO el 22-11 (tele 4K a 120 con VRR, fuente de 30 en 59,94, captura 4K sin escalar, DLSS 5 nivel 3 de 1
        // pasada, PresentMon + log de Magpie): con DLSS 5 lo que no deja llegar a las 29,97 reales NO es la grafica llena (nvidia-smi 50-75 %)
        // sino el hilo de Magpie, que va en serie (captura -> flujo -> DLSSNR -> DLSS FG, esperando cada inventada -> publicar). DLSS FG x2 al
        // 50 %: 28,3 reales; x3 al 45 %: 28,6; x3 al 40 %: 29,5 (min 28,2); x4 al 45 %: 27,2; x4 sin DLSS 5: 29,96 / 119,9. Antes el modelo
        // contaba 5 ms por inventada de DLSS FG en el hilo (x4 = 15: DLSS 5 "no cabe" o al 25 %) y nada de lo fijo de DLSS 5 (~11 ms a 4K)
        public static double Prevision(CadenaEscala c, int pct, int pasadas, double k, double extra)
        {
            double s = PorGrafica(c), nn = Neuronal(c);
            double fijo = CosteBase(c) * s + (CosteEscalado(c) * (c.Esc == "dlss" ? nn : 1) + CosteGeneracion(c, true) * nn) * s;
            return (fijo + CosteDlss5(c, pct, pasadas) * s * nn) * k + CosteDlss5Fijo(c, pct, pasadas) * s + extra;
        }

        // (1.2.3, MULTI2) Lo que ocupa la grafica (lo que ve su %, NVAPI): para aprender k (NuevoK). Sin lo fijo de DLSS 5 (el acelerador de
        // flujo no cuenta en el %) y con la generacion entera
        public static double Carga(CadenaEscala c, int pct, int pasadas)
        {
            double s = PorGrafica(c), nn = Neuronal(c);
            double fijo = CosteBase(c) * s + (CosteEscalado(c) * (c.Esc == "dlss" ? nn : 1) + CosteGeneracion(c, false) * nn) * s;
            return fijo + CosteDlss5(c, pct, pasadas) * s * nn;
        }

        // (1.2.3, MULTI2) XeSS FG x3 / x4 en una grafica que no es Intel: Magpie 0.6.12 pone su ritmo de compatibilidad (XESSFG.md: "3x/4x
        // en graficas no Intel"; src\Magpie.Core\XeSSFGPacing.h), que reparte las inventadas esperando DENTRO de la presentacion: cada una
        // a T/m de la anterior (T = el periodo de las reales que estima Magpie). El hilo de la imagen solo tiene T/m para todo lo demas (la
        // cadena entera y la propia XeSS FG); si no le basta, el periodo se estira (lo estimado ya lleva la espera) y las reales bajan a
        // 1 / (m x W), W = lo de fuera de esas esperas. Log del usuario (22-11 10:28, 4K sin DLSS 5, x4): "PresentMs=28.4 extraWaitMs=24.2
        // sourceMs=37.6" -> 26,4 reales/s y la salida a 105 con huecos de 2 refrescos (P95 18 ms). Asi que el presupuesto es T/m
        public static double UsoXess = 0.95;      // xess_fg_multi_uso: parte de T/m que puede usar la cadena con XeSS FG x3 / x4 (compatibilidad)
        public static bool XessCompat(CadenaEscala c) { return c.Gen == "xess" && c.Mult >= 3 && !c.Intel && c.Modo < 100; }

        public static double Presupuesto(CadenaEscala c)
        {
            double t = 1000.0 / Math.Max(1, c.Objetivo);
            if (XessCompat(c)) return t / c.Mult * UsoXess;
            return t * Uso;
        }

        // El minimo de calidad: ~324 lineas de entrada (25 % de 1440p son 360; a la ventana de 1080p, 30 %)
        public static int Suelo(CadenaEscala c)
        {
            int s = (int)Math.Ceiling(32400.0 / Math.Max(1, c.VenH) / 5.0) * 5;
            // (1.2.3, MULTI2) con x3 / x4, como poco generacion_dlss5_minimo (30 %): con 3 de cada 4 imagenes inventadas lo de DLSS 5 a muy poca
            // entrada (al 25 % de 4K, 960x540; log del usuario, 22-11 10:28-10:30, "fallos visuales" con DLSS FG x4 llegando a 30 / 120) se ve
            // en todas; si no cabe ni asi, DLSS 5 fuera (el menu lo dice: "x4: DLSS 5 no cabe (se quita)")
            if (c.Mult >= 3 && c.Gen.Length > 0 && c.Modo < 100 && SueloMulti > 0) s = Math.Max(s, SueloMulti);
            return Math.Max(25, Math.Min(100, s));
        }
        public static int SueloMulti = 30;        // generacion_dlss5_minimo (0 = el suelo de siempre)

        // El mayor % que cabe con estas pasadas (de 5 en 5); -1 = ni el minimo. Al minimo "a prueba" si le falta poco (<= 15 %): si luego
        // no llega, la vigilancia lo quita
        public static int Mejor(CadenaEscala c, int pasadas, double k, double extra)
        {
            if (pasadas >= 2 && c.VramGb > 0 && c.VramGb < 7.5) return -1;   // (con menos de 8 GB, una pasada: Plan.Dlss5PasadasMax)
            double b = Presupuesto(c);
            // (1.2.3, MULTI2) y la grafica sin llenarse: lo que ocupa (Carga, con el k aprendido) hasta dlss5_escala_grafica (88 %) del tiempo de una
            // real. Con XeSS FG, que genera en su propia cola, es lo que limita (4K x2 al 50 %: 77 % de grafica y llega)
            double bg = 1000.0 / Math.Max(1, c.Objetivo) * UsoGrafica;
            int suelo = Suelo(c);
            for (int pct = 100; pct >= suelo; pct -= 5)
                if (Prevision(c, pct, pasadas, k, extra) <= b && Carga(c, pct, pasadas) * k <= bg) return pct;
            // (a 4K no: 60 + FG a 4K no cabia ni al 25 %, medido; asi sigue el motor propio sin reabrir Magpie al volver del menu, 1.2.04)
            // (1.2.3, MULTI2: el modelo ya es el del hilo -con el uso del 92 %-: "a prueba" solo si no pasa del 97 % del tiempo de una real)
            if (c.VenH < 2000 && Prevision(c, suelo, pasadas, k, extra) <= Math.Min(b * 1.15, 1000.0 / Math.Max(1, c.Objetivo) * (XessCompat(c) ? UsoXess / c.Mult : 0.97)) &&
                Carga(c, suelo, pasadas) * k <= bg * 1.08) return suelo;
            return -1;
        }

        public static string Letra(int modelo) { return modelo == 13 ? "M" : modelo == 12 ? "L" : modelo == 11 ? "K" : modelo == 10 ? "J" : modelo == 5 ? "E" : "?"; }
        public static int Numero(string l) { return l == "M" ? 13 : l == "L" ? 12 : l == "K" ? 11 : l == "J" ? 10 : l == "E" ? 5 : 0; }
        // El siguiente mas ligero: M / L -> K, K / J -> E (E es el minimo: 16-11, el usuario)
        public static int MasLigero(int modelo) { return modelo == 13 || modelo == 12 ? 11 : modelo == 11 || modelo == 10 ? 5 : 0; }
        // De barata a cara (para la escalera): Smooth Motion, DLSS FG, FSR 3 FG, XeSS FG, NVIDIA VFG
        public static int RangoGen(string g) { return g == "smooth" ? 1 : g == "dlss" ? 2 : g == "fsr3" ? 3 : g == "xess" ? 4 : g == "vfg" ? 5 : 0; }

        // ------------------------------------------------------------------ la decision (funcion pura: medidas -> que hacer)
        public sealed class Medida
        {
            public double Real;                 // imagenes reales/s que recibe Magpie (todas las de los resumenes de la ventana)
            public double RealMin;              // el peor resumen
            public double Fuente;               // las que da mpv (tras el filtro de repetidas); 0 = no se sabe
            public double Objetivo;             // las del modo
            public double Salida = -1, SalidaObjetivo = -1;   // con la generacion de Magpie (-1 = sin ella o no se sabe)
            public double Uso = -1;             // % de grafica (NVAPI); -1 = no se sabe
            public int Huecos;                  // resumenes con una imagen real tarde (> 1,6 veces lo normal)
            public double Segundos = 5;         // lo que dura la ventana
        }

        public sealed class Seguimiento
        {
            public string Clave = "";
            public int Mal, Grave;
            public double SegBien, SegLlega;
            public bool Fijado;
        }

        public sealed class Opciones
        {
            public int Pct, Pasadas = 1;        // lo de ahora
            public double K = 1, Extra;         // lo aprendido
            public int NoPct;                   // el % que ya no llego con esto (0 = ninguno)
            public bool ModeloBajable = true;   // se puede bajar el modelo de DLSS SR
            public string GenBarata = "";       // la generacion mas barata de la lista del modo ("" = ninguna)
            public double SegSubir = -1;        // segundos llegando de sobra para subir (-1 = 180 con el % de grafica, 600 sin el)
        }

        public sealed class Decision
        {
            public string Tipo = "nada";        // nada | bajar | escalera (lo demas: FpsBajo) | subir (pendiente, a la proxima apertura)
            public int Pct, Pasadas, Modelo;    // lo nuevo
            public string Gen = "";
            public double Extra, K = 1;         // el "extra" nuevo de la cadena y el k con el que vale
            public List<string> Pasos = new List<string>();   // pct, pasadas, modelo, gen
            public string Motivo = "";
            public double Objetivo, Real, Salida = -1, SalidaObjetivo = -1;
            public bool PorSalida;              // no llegaba la salida (con la generacion), no las reales
            public bool Fijar;                  // 60 s seguidos llegando: lo de ahora vale (se aprende tal cual: un k nuevo no lo baja luego)
            public double Previsto;             // ms previstos con lo nuevo
        }

        static string F1(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        public static Decision Decidir(CadenaEscala c, Medida m, Seguimiento s, Opciones o)
        {
            Decision d = new Decision();
            d.Pct = o.Pct; d.Pasadas = o.Pasadas; d.Modelo = c.Modelo; d.Gen = c.Gen; d.Extra = o.Extra;
            d.Objetivo = m.Objetivo; d.Real = m.Real; d.Salida = m.Salida; d.SalidaObjetivo = m.SalidaObjetivo;
            string clave = c.ClaveCompleta(o.Pasadas) + "|" + o.Pct;
            if (clave != s.Clave) { s.Clave = clave; s.Mal = s.Grave = 0; s.SegBien = 0; s.SegLlega = 0; s.Fijado = false; }
            // Solo si mpv le da a Magpie el modo entero (si el juego va a menos por si mismo, no es cosa del PC)
            if (m.Real <= 0 || m.Objetivo <= 0 || m.Fuente < m.Objetivo * 0.92) { s.Mal = s.Grave = 0; s.SegBien = 0; s.SegLlega = 0; d.Motivo = "sin juzgar"; return d; }
            double t = Math.Min(m.Objetivo, m.Fuente);
            bool falta = m.Real < t - Math.Max(1.0, 0.03 * t);
            bool grave = m.Real < 0.85 * t;
            // Con la generacion de Magpie: la salida, si la grafica va llena (sin el % de grafica no se sabe: solo al log)
            bool faltaSalida = !falta && m.Salida > 0 && m.SalidaObjetivo > 0 && m.Salida < 0.85 * m.SalidaObjetivo && m.Uso >= 90;
            bool sobra = !falta && !faltaSalida && m.Real >= t - 0.35 && m.Huecos == 0 && (m.Uso < 0 || m.Uso <= 82) &&
                         (m.Salida < 0 || m.SalidaObjetivo < 0 || m.Salida >= 0.95 * m.SalidaObjetivo);
            s.Mal = falta || faltaSalida ? s.Mal + 1 : 0;
            s.Grave = grave ? s.Grave + 1 : 0;
            s.SegBien = sobra ? s.SegBien + m.Segundos : 0;
            s.SegLlega = falta || faltaSalida ? 0 : s.SegLlega + m.Segundos;
            if (!s.Fijado && s.SegLlega >= 60) { s.Fijado = true; d.Fijar = true; }
            if (s.Mal >= 3 || s.Grave >= 2)
            {
                s.Mal = s.Grave = 0; s.SegBien = 0;
                d.PorSalida = faltaSalida && !falta;
                Bajar(c, m, o, d, t, grave);
                return d;
            }
            double espera = o.SegSubir > 0 ? o.SegSubir : m.Uso >= 0 ? 180 : 600;
            if (s.SegBien >= espera)
            {
                s.SegBien = 0;
                Subir(c, m, o, d);
                return d;
            }
            d.Motivo = falta || faltaSalida ? "no llega (" + s.Mal + ")" : sobra ? "de sobra " + Math.Round(s.SegBien) + " s" : "justo";
            return d;
        }

        // ms de DLSS 5 (con este k) y el extra que deja "pct" como el mayor que cabe en el modelo (lo aprendido = lo decidido)
        // (1.2.3: sin lo fijo de DLSS 5 -flujo y DLSSNR a la resolucion de la ventana-, que no baja con el %: lo que cambia con el %)
        static double MsDlss5(CadenaEscala c, int pct, int pasadas, double k) { return Prevision(c, pct, pasadas, k, 0) - Prevision(c, 0, 0, k, 0) - CosteDlss5Fijo(c, pct, pasadas) * PorGrafica(c); }
        public static double ExtraPara(CadenaEscala c, int pct, int pasadas, double k)
        {
            double b = Presupuesto(c);
            return Math.Max(-0.5 * b, Math.Min(b, b - Prevision(c, pct, pasadas, k, 0) - 0.01));
        }

        // No llega: lo que hay que quitar es lo que falta (t / reales - 1) y un 3 % de margen, en fraccion del tiempo de grafica de ahora. Lo
        // que quita cada paso sale del modelo en proporcion (DLSS 5 cuenta como un 25 % como poco: el modelo lo infravalora en los de 60), no
        // de su valor absoluto: asi 57,5 de 59,94 al 40 % baja al 35 %, y 100 de 120 al 35 %, al minimo. Sin la grafica llena (% conocido y por
        // debajo del 85 %) no es solo cosa de la grafica: un paso de 5 y ya
        static void Bajar(CadenaEscala c, Medida m, Opciones o, Decision d, double t, bool grave)
        {
            double b = Presupuesto(c);
            double r = RealEfectiva(m, d.PorSalida, t);
            double queda = Math.Max(0, t / Math.Max(1, r) - 1) + 0.03, pedido = queda;
            // (1.2.3, MULTI2: con XeSS FG x3 / x4 de compatibilidad lo que falta es tiempo de T/m, no grafica: como llena)
            bool llena = m.Uso < 0 || m.Uso >= 85 || XessCompat(c);
            double k = o.K;
            CadenaEscala x = c.Copia();
            double t0 = Math.Max(0.1, Prevision(c, o.Pct, o.Pasadas, k, 0));
            int pct = o.Pct, pas = o.Pasadas, max = grave ? 3 : 2;
            int suelo = Suelo(c);
            // 1) el % de DLSS 5
            if (pct > suelo)
            {
                double f = Math.Max(0.25, MsDlss5(x, pct, pas, k) / t0);
                int p;
                if (!llena) p = pct - 5;
                else
                {
                    double q = Math.Sqrt(Math.Max(0, 1 - queda / f));
                    p = (int)Math.Floor(pct * q / 5.0 + 0.5) * 5;
                    p = Math.Min(pct - 5, p);
                    if (!grave) p = Math.Max(p, pct - 15);   // (sin que falte mucho, como mucho 15 puntos de una vez)
                }
                p = Math.Max(suelo, p);
                queda -= f * (1 - (p / (double)pct) * (p / (double)pct));
                pct = p;
                d.Pasos.Add("pct");
            }
            // 2) una pasada menos (el coste de DLSS 5 va con las pasadas; el % se queda)
            if (queda > 0.005 && llena && pas > 1 && d.Pasos.Count < max)
            {
                double f = Math.Max(0.25, MsDlss5(x, pct, pas, k) / t0);
                queda -= f / pas;
                pas--;
                d.Pasos.Add("pasadas");
            }
            // 3) el modelo de DLSS SR, uno mas ligero (M / L -> K -> E)
            if (queda > 0.005 && llena && o.ModeloBajable && x.Escala && x.Esc == "dlss" && MasLigero(x.Modelo) > 0 && d.Pasos.Count < max)
            {
                double antes = Prevision(x, pct, pas, k, 0);
                x.Modelo = MasLigero(x.Modelo);
                queda -= (antes - Prevision(x, pct, pas, k, 0)) / t0;
                d.Pasos.Add("modelo");
            }
            // 4) una generacion mas barata de la lista del modo
            if (queda > 0.005 && llena && o.GenBarata.Length > 0 && x.Gen.Length > 0 && RangoGen(o.GenBarata) < RangoGen(x.Gen) && d.Pasos.Count < max)
            {
                double antes = Prevision(x, pct, pas, k, 0);
                x.Gen = o.GenBarata;
                if (x.Gen == "smooth") x.Mult = 2;
                queda -= (antes - Prevision(x, pct, pas, k, 0)) / t0;
                d.Pasos.Add("gen");
            }
            d.Pct = pct; d.Pasadas = pas; d.Modelo = x.Modelo; d.Gen = x.Gen;
            d.K = k;
            d.Extra = ExtraPara(x, pct, pas, k);
            d.Previsto = Prevision(x, pct, pas, k, d.Extra);
            if (d.Pasos.Count == 0)
            {
                d.Tipo = "escalera";
                // (ni al minimo: la proxima vez con DLSS 5 en esta cadena, el minimo "a prueba" -un 10 % por encima de lo que cabe- y no mas)
                d.Extra = ExtraPara(c, suelo, Math.Max(1, o.Pasadas), k) + 0.10 * b;
                d.Motivo = "DLSS 5 ya al minimo (" + o.Pct + " %, " + o.Pasadas + " pasada(s)" + (c.Esc == "dlss" && c.Escala ? ", DLSS " + Letra(c.Modelo) : "") +
                           ") y no llega (" + F1(m.Real) + " de " + F1(t) + "): lo siguiente de la escalera";
                return;
            }
            d.Tipo = "bajar";
            d.Motivo = "no llega (" + F1(m.Real) + " de " + F1(t) + (d.PorSalida ? ", salida " + F1(m.Salida) + " de " + F1(m.SalidaObjetivo) : "") +
                       (m.Uso >= 0 ? ", grafica " + Math.Round(m.Uso) + " %" : "") + "): hay que quitar un " + Math.Round(pedido * 100) + " % del tiempo de grafica" +
                       (queda > 0.005 ? " (quedaria un " + Math.Round(queda * 100) + " %: lo siguiente, si sigue sin llegar)" : "") + "; aprendido: extra " + F1(o.Extra) + " -> " + F1(d.Extra) + " ms";
        }

        static double RealEfectiva(Medida m, bool porSalida, double t)
        {
            // (sin llegar la salida, como si las reales fueran la salida partida por el multiplicador)
            if (porSalida && m.SalidaObjetivo > 0) return t * m.Salida / m.SalidaObjetivo;
            return m.Real;
        }

        // Sobra: con el % de grafica, el % que deja la grafica al 85 % como mucho (DLSS 5 cuenta como un 25 % del tiempo como poco: prudente);
        // sin el, +5. Como mucho +15 y nunca hasta el que ya no llego (no_pct). Pendiente hasta la proxima apertura de Magpie
        static void Subir(CadenaEscala c, Medida m, Opciones o, Decision d)
        {
            double k = o.K;
            int p;
            // (1.2.3, MULTI2: con XeSS FG x3 / x4 de compatibilidad la grafica espera mucho -las esperas del ritmo- y su % no dice lo que
            // queda de T/m: +5 y ya)
            if (m.Uso >= 0 && !XessCompat(c))
            {
                double u = m.Uso / 100.0;
                double f = Math.Max(0.25, MsDlss5(c, o.Pct, o.Pasadas, k) / Math.Max(0.1, Prevision(c, o.Pct, o.Pasadas, k, 0)));
                double q2 = 1 + (0.85 - u) / Math.Max(0.05, f * u);
                p = q2 <= 1 ? o.Pct : (int)Math.Floor(o.Pct * Math.Sqrt(q2) / 5.0) * 5;
                p = Math.Min(p, o.Pct + 15);
            }
            else p = o.Pct + 5;
            if (o.NoPct > 0) p = Math.Min(p, o.NoPct - 5);
            p = Math.Min(100, p);
            // (1.2.3, MULTI2) con DLSS 5 el % de grafica se queda en el 50-75 % aunque el hilo de Magpie ya no llegue (medido): no se propone un % con
            // el que el hilo, con lo aprendido de esta cadena, pasaria del 97 % del tiempo de una real
            double tope = 1000.0 / Math.Max(1, c.Objetivo) * (XessCompat(c) ? UsoXess / c.Mult : 0.97);
            while (p > o.Pct && Prevision(c, p, o.Pasadas, k, o.Extra) > tope) p -= 5;
            if (p < o.Pct + 5) { d.Tipo = "nada"; d.Motivo = "de sobra, pero no cabe mas" + (o.NoPct > 0 ? " (al " + o.NoPct + " % ya no llego)" : ""); return; }
            d.Tipo = "subir";
            d.Pct = p;
            d.K = k;
            d.Extra = ExtraPara(c, p, o.Pasadas, k);
            d.Previsto = Prevision(c, p, o.Pasadas, k, d.Extra);
            d.Motivo = "de sobra (" + F1(m.Real) + " de " + F1(m.Objetivo) + (m.Uso >= 0 ? ", grafica " + Math.Round(m.Uso) + " %" : "") + "): la proxima vez al " + p + " %" +
                       " (aprendido: extra " + F1(o.Extra) + " -> " + F1(d.Extra) + " ms)";
        }

        // k: lo que cuesta de verdad en esta grafica frente al modelo, con el % de grafica de una ventana que llega o casi (media movil)
        public static double NuevoK(CadenaEscala c, Medida m, int pct, int pasadas, double k)
        {
            if (m.Uso < 30 || m.Uso > 98 || m.Real < 0.6 * m.Objetivo) return k;
            double tObs = m.Uso / 100.0 * 1000.0 / Math.Max(1, m.Real);
            // (1.2.3, MULTI2: contra lo que ocupa la grafica -Carga-, no contra el hilo: el % de grafica no ve el flujo ni las esperas)
            double t1 = Carga(c, pct, pasadas);
            if (t1 <= 0.5) return k;
            double obs = Math.Max(0.5, Math.Min(2.5, tObs / t1));
            return Math.Max(0.5, Math.Min(2.5, 0.85 * k + 0.15 * obs));
        }

        // ------------------------------------------------------------------ la grafica de este PC (para el modelo)
        static double gpuIndice = -1, gpuVram = 8;
        static string gpuFamilia = "rtx40";
        public static double IndiceIni = 0, VramIni = 0;   // escala_gpu_indice / escala_gpu_vram del ini (los escribe el plan)
        public static string FamiliaIni = "";               // escala_gpu_familia

        public static void GpuPara(CadenaEscala c)
        {
            if (gpuIndice < 0) LeerGpu();
            c.Indice = IndiceIni > 0 ? IndiceIni : gpuIndice;
            c.Familia = FamiliaIni.Length > 0 ? FamiliaIni : gpuFamilia;
            c.VramGb = VramIni > 0 ? VramIni : gpuVram;
        }

        // (prueba) otra grafica
        public static void GpuFija(double indice, string familia, double vram) { gpuIndice = indice; gpuFamilia = familia; gpuVram = vram; }

        // De data\hardware.txt (el informe de la deteccion: la grafica de la huella, su "indice ~1,20", su familia y su VRAM)
        static void LeerGpu()
        {
            double ind = 1.0, vram = 8;
            string fam = "rtx40";
            try
            {
                if (File.Exists(Ce.HardwareFile))
                {
                    string[] ls = File.ReadAllLines(Ce.HardwareFile);
                    string vd = "";
                    foreach (string l in ls) if (l.StartsWith("huella=")) { string h = l.Substring(7); string[] p = h.Split(':'); if (p.Length >= 2) vd = p[0] + ":" + p[1]; break; }
                    int ini = -1;
                    for (int i = 0; i < ls.Length; i++)
                        if (vd.Length > 0 && ls[i].Contains(" " + vd + ",")) { ini = i; break; }
                    if (ini < 0) for (int i = 0; i < ls.Length; i++) if (Regex.IsMatch(ls[i], @"familia\s+Nvidia|family\s+Nvidia")) { ini = i; break; }
                    if (ini >= 0)
                    {
                        Match mf = Regex.Match(ls[ini], @"(?:familia|family)\s+(\w+)");
                        if (mf.Success)
                        {
                            string f = mf.Groups[1].Value.ToLowerInvariant();
                            fam = f.Contains("rtx20") ? "rtx20" : f.Contains("rtx30") ? "rtx30" : f.Contains("rtx40") ? "rtx40" : f.Contains("rtx50") ? "rtx50" : "otra";
                        }
                        for (int i = ini; i < ls.Length && i < ini + 6; i++)
                        {
                            Match mi = Regex.Match(ls[i], @"(?:ndice|index) ~([0-9]+[.,]?[0-9]*)");
                            if (mi.Success) ind = double.Parse(mi.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                            Match mv = Regex.Match(ls[i], @"VRAM ([0-9]+[.,]?[0-9]*) (GB|MB)");
                            if (mv.Success) { double v = double.Parse(mv.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture); vram = mv.Groups[2].Value == "MB" ? v / 1024 : v; }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Error("Escala de DLSS 5: leyendo la grafica de " + Ce.HardwareFile, ex); }
            gpuIndice = ind > 0.05 ? ind : 1.0; gpuFamilia = fam; gpuVram = vram;
            Log.Write("Escala de DLSS 5 automatica: grafica de indice " + gpuIndice.ToString("0.##", CultureInfo.InvariantCulture) + ", " + gpuFamilia + ", " +
                      gpuVram.ToString("0.#", CultureInfo.InvariantCulture) + " GB (" + Ce.HardwareFile + ")");
        }

        // ------------------------------------------------------------------ lo aprendido (data\scales_auto.txt)
        sealed class Fila { public double Extra, Pend = double.NaN, K = double.NaN, PendK = double.NaN; public int NoPct; public string Modelo = "", Elegido = "", Fecha = ""; }
        static readonly object sync = new object();
        static Dictionary<string, Fila> filas;          // "x|<gpu>|<juego>|<clave>" y "no|<gpu>|<juego>|<clave completa>"
        static Dictionary<string, double> ks;           // por grafica + driver
        static readonly Dictionary<string, int> congelado = new Dictionary<string, int>();   // % por clave completa mientras Magpie siga abierto
        static readonly Dictionary<string, int> topeSesion = new Dictionary<string, int>(); // lo bajado en esta sesion (no se pasa de ahi)
        static string fichero;
        public static string Fichero { get { return fichero ?? Path.Combine(Paths.Datos, "scales_auto.txt"); } set { fichero = value; } }
        static DateTime guardarAt = DateTime.MinValue;
        static bool sucio;

        static string Gpu { get { string g = Escalas.GpuClave; return g.Length > 0 ? g : "-"; } }
        static string JuegoClave(string j) { return string.IsNullOrEmpty(j) ? "*" : j.Replace("|", " ").Replace("\t", " "); }

        static void Cargar()
        {
            if (filas != null) return;
            filas = new Dictionary<string, Fila>();
            ks = new Dictionary<string, double>();
            try
            {
                if (!File.Exists(Fichero)) return;
                foreach (string raw in File.ReadAllLines(Fichero, Encoding.UTF8))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    string[] c = raw.Split('\t');
                    if (c.Length < 2) continue;
                    if (c[0].StartsWith("k|"))
                    {
                        double kv;
                        foreach (string f in c) if (f.StartsWith("k=") && double.TryParse(f.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out kv)) ks[c[0].Substring(2)] = kv;
                        continue;
                    }
                    Fila r = new Fila();
                    bool v2 = false;
                    for (int i = 1; i < c.Length; i++)
                    {
                        int eq = c[i].IndexOf('=');
                        if (eq <= 0) continue;
                        string k = c[i].Substring(0, eq), v = c[i].Substring(eq + 1);
                        double dv; int iv;
                        if (k == "v") v2 = v == "2";
                        else if (k == "extra" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) r.Extra = dv;
                        else if (k == "pend" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) r.Pend = dv;
                        else if (k == "k" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) r.K = dv;
                        else if (k == "pend_k" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) r.PendK = dv;
                        else if (k == "no_pct" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) r.NoPct = iv;
                        else if (k == "modelo") r.Modelo = v;
                        else if (k == "elegido") r.Elegido = v;
                        else if (k == "fecha") r.Fecha = v;
                    }
                    // (1.2.3, MULTI2) el "extra" y el k de una cadena aprendidos con el modelo de antes (sin lo fijo de DLSS 5 en el hilo, 5 ms por
                    // inventada de DLSS FG) no valen con el de ahora: fuera (se aprende otra vez). Lo que no llego (no_pct) si vale: fue medido
                    if (c[0].StartsWith("x|") && !v2) continue;
                    filas[c[0]] = r;
                }
            }
            catch (Exception ex) { Log.Error("Escala de DLSS 5: leyendo " + Fichero, ex); }
        }

        static void Guardar(bool ya)
        {
            if (!sucio) return;
            if (!ya && (DateTime.Now - guardarAt).TotalSeconds < 60) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ConsoleEnhancer (1.2.1): la escala de DLSS 5 automatica aprendida en este PC (la ajusta la app sola; borrar = volver a la estimacion)");
                sb.AppendLine("# k|grafica+driver <TAB> k=coste medido / coste previsto");
                sb.AppendLine("# x|grafica+driver|juego|modo|generacion|captura|ventana|salida|escalador <TAB> extra=ms que el modelo no ve <TAB> pend=el de la proxima apertura <TAB> modelo=tope del modelo de DLSS SR (elegido=lo elegido entonces)");
                sb.AppendLine("# no|...|m<modelo de DLSS SR>|p<pasadas> <TAB> no_pct=% que no llego a los fps del modo");
                lock (sync)
                {
                    foreach (KeyValuePair<string, double> kv in ks) sb.Append("k|").Append(kv.Key).Append("\tk=").Append(kv.Value.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine();
                    foreach (KeyValuePair<string, Fila> kv in filas)
                    {
                        Fila f = kv.Value;
                        sb.Append(kv.Key);
                        if (kv.Key.StartsWith("x|"))
                        {
                            sb.Append("\tv=2");   // (1.2.3: modelo del hilo de Magpie; las de antes se ignoran)
                            sb.Append("\textra=").Append(f.Extra.ToString("0.##", CultureInfo.InvariantCulture));
                            if (!double.IsNaN(f.K)) sb.Append("\tk=").Append(f.K.ToString("0.###", CultureInfo.InvariantCulture));
                            if (!double.IsNaN(f.Pend)) sb.Append("\tpend=").Append(f.Pend.ToString("0.##", CultureInfo.InvariantCulture));
                            if (!double.IsNaN(f.PendK)) sb.Append("\tpend_k=").Append(f.PendK.ToString("0.###", CultureInfo.InvariantCulture));
                            if (f.Modelo.Length > 0) sb.Append("\tmodelo=").Append(f.Modelo).Append("\telegido=").Append(f.Elegido);
                        }
                        else sb.Append("\tno_pct=").Append(f.NoPct.ToString(CultureInfo.InvariantCulture));
                        sb.Append("\tfecha=").Append(f.Fecha).AppendLine();
                    }
                    sucio = false;
                }
                guardarAt = DateTime.Now;
                Directory.CreateDirectory(Path.GetDirectoryName(Fichero));
                string tmp = Fichero + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(Fichero)) File.Replace(tmp, Fichero, null); else File.Move(tmp, Fichero);
            }
            catch (Exception ex) { Log.Error("Escala de DLSS 5: guardando " + Fichero, ex); }
        }
        public static void GuardarYa() { Guardar(true); }

        // (prueba) todo lo aprendido fuera, con otro fichero
        public static void Reiniciar(string otroFichero)
        {
            lock (sync)
            {
                fichero = otroFichero;
                filas = null; ks = null;
                congelado.Clear(); topeSesion.Clear();
                sucio = false;
            }
        }

        static string Ahora { get { return DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); } }

        public static double K
        {
            get { lock (sync) { Cargar(); double k; return ks.TryGetValue(Gpu, out k) ? k : 1.0; } }
            set { lock (sync) { Cargar(); double old; if (!ks.TryGetValue(Gpu, out old) || Math.Abs(old - value) > 0.005) { ks[Gpu] = value; sucio = true; } } Guardar(false); }
        }

        // La fila de la cadena: la del juego o, si no hay, la de cualquier juego ("*")
        static Fila FilaDe(string tipo, string juego, string clave)
        {
            Fila f;
            if (filas.TryGetValue(tipo + "|" + Gpu + "|" + JuegoClave(juego) + "|" + clave, out f)) return f;
            if (!string.IsNullOrEmpty(juego) && filas.TryGetValue(tipo + "|" + Gpu + "|*|" + clave, out f)) return f;
            return null;
        }

        static Fila FilaPara(string tipo, string juego, string clave)
        {
            string k = tipo + "|" + Gpu + "|" + JuegoClave(juego) + "|" + clave;
            Fila f;
            if (!filas.TryGetValue(k, out f)) { f = new Fila(); filas[k] = f; }
            return f;
        }

        public static double Extra(string juego, string clave) { lock (sync) { Cargar(); Fila f = FilaDe("x", juego, clave); return f != null ? f.Extra : 0; } }
        // El k con el que vale lo aprendido de esta cadena (el de cuando se decidio); sin nada aprendido, el de la grafica
        public static double KDe(string juego, string clave)
        {
            lock (sync)
            {
                Cargar();
                Fila f = FilaDe("x", juego, clave);
                if (f != null && !double.IsNaN(f.K)) return f.K;
                double k; return ks.TryGetValue(Gpu, out k) ? k : 1.0;
            }
        }
        // ¿Hay algo aprendido de esta cadena en este juego (no el de cualquier juego)?
        public static bool Aprendida(string juego, string clave) { lock (sync) { Cargar(); return filas.ContainsKey("x|" + Gpu + "|" + JuegoClave(juego) + "|" + clave); } }
        public static int NoPct(string juego, string claveCompleta) { lock (sync) { Cargar(); Fila f = FilaDe("no", juego, claveCompleta); return f != null && Reciente(f) ? f.NoPct : 0; } }

        // Un "no llego" vale 7 dias (una mala racha no cierra ese % para siempre; con otro driver ya es otra grafica)
        static bool Reciente(Fila f)
        {
            DateTime t;
            return f.NoPct > 0 && DateTime.TryParseExact(f.Fecha, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && (DateTime.Now - t).TotalDays <= 7;
        }

        // El tope del modelo de DLSS SR que puso la vigilancia en esta cadena (0 = ninguno); vale mientras lo elegido en el menu sea lo de entonces
        public static int TopeModelo(string juego, string clave, string elegidoAhora)
        {
            lock (sync)
            {
                Cargar();
                Fila f = FilaDe("x", juego, clave);
                if (f == null || f.Modelo.Length == 0 || f.Elegido != elegidoAhora) return 0;
                return Numero(f.Modelo);
            }
        }

        // Lo bajado jugando, ya: en el juego y en "*" (lo ultimo aprendido en cualquier juego con esta cadena) el extra y su k, el tope del
        // modelo de DLSS SR y el % que no llego (en la clave completa de antes); en esta sesion no se pasa del % nuevo
        public static void AnotarBajada(string juego, string clave, string claveCompletaAntes, int pctAntes, double extra, double k, int modeloTope, string elegido,
                                        string claveCompletaNueva, int pctNuevo)
        {
            lock (sync)
            {
                Cargar();
                foreach (string j in string.IsNullOrEmpty(juego) ? new[] { "" } : new[] { juego, "" })
                {
                    Fila x = FilaPara("x", j, clave);
                    x.Extra = extra; x.K = k;
                    x.Pend = double.NaN; x.PendK = double.NaN;
                    if (modeloTope > 0) { x.Modelo = Letra(modeloTope); x.Elegido = elegido ?? ""; }
                    x.Fecha = Ahora;
                    if (pctAntes > 0)
                    {
                        Fila n = FilaPara("no", j, claveCompletaAntes);
                        n.NoPct = Reciente(n) ? Math.Min(n.NoPct, pctAntes) : pctAntes;
                        n.Fecha = Ahora;
                    }
                }
                if (claveCompletaNueva != null) { topeSesion[claveCompletaNueva] = pctNuevo; congelado[claveCompletaNueva] = pctNuevo; }
                sucio = true;
            }
            Guardar(true);
        }

        // Ni al minimo (lo siguiente lo baja FpsBajo): solo el extra, en el juego y en "*"
        public static void AnotarExtra(string juego, string clave, double extra, double k) { AnotarBajada(juego, clave, null, 0, extra, k, 0, null, null, 0); }

        // Sobra: el extra nuevo (y su k), para la proxima apertura de Magpie
        public static void AnotarSubida(string juego, string clave, double extra, double k)
        {
            lock (sync)
            {
                Cargar();
                Fila x = FilaPara("x", juego, clave);
                x.Pend = extra; x.PendK = k;
                x.Fecha = Ahora;
                sucio = true;
            }
            Guardar(true);
        }

        // MagpieConfig.Patch con escritura (Magpie se abre o se reabre): las subidas pendientes pasan a valer y se recalcula todo
        public static void AlAbrirMagpie()
        {
            List<string> hechas = new List<string>();
            lock (sync)
            {
                Cargar();
                foreach (KeyValuePair<string, Fila> kv in filas)
                    if (kv.Key.StartsWith("x|") && !double.IsNaN(kv.Value.Pend))
                    {
                        hechas.Add(kv.Key.Substring(2) + ": extra " + kv.Value.Extra.ToString("0.##", CultureInfo.InvariantCulture) + " -> " + kv.Value.Pend.ToString("0.##", CultureInfo.InvariantCulture));
                        kv.Value.Extra = kv.Value.Pend; kv.Value.Pend = double.NaN;
                        if (!double.IsNaN(kv.Value.PendK)) { kv.Value.K = kv.Value.PendK; kv.Value.PendK = double.NaN; }
                        sucio = true;
                    }
                congelado.Clear();
                if (hechas.Count > 0) topeSesion.Clear();   // (la subida pedida manda sobre lo bajado antes en la sesion)
            }
            if (hechas.Count > 0) { Log.Write("Escala de DLSS 5: subida(s) pendiente(s) puesta(s) al abrir Magpie: " + string.Join("; ", hechas.ToArray())); Guardar(true); }
        }

        // El % de DLSS 5 para esta cadena y pasadas (congelado mientras Magpie siga abierto): el modelo con lo aprendido de la cadena (su
        // extra y su k) o, sin nada aprendido, con el k de la grafica; nunca el % que ya no llego (7 dias) ni mas de lo bajado en la sesion
        public static int PctPara(CadenaEscala c, int pasadas)
        {
            string cc = c.ClaveCompleta(pasadas);
            lock (sync)
            {
                Cargar();
                int v;
                if (congelado.TryGetValue(cc, out v)) return v;
                double k; if (!ks.TryGetValue(Gpu, out k)) k = 1.0;
                Fila fx = FilaDe("x", c.Juego, c.Clave);
                if (fx != null && !double.IsNaN(fx.K)) k = fx.K;
                int p = Mejor(c, pasadas, k, fx != null ? fx.Extra : 0);
                Fila fn = FilaDe("no", c.Juego, cc);
                if (p > 0 && fn != null && Reciente(fn) && p >= fn.NoPct) p = fn.NoPct - 5 >= Suelo(c) ? fn.NoPct - 5 : -1;
                int tope;
                if (p > 0 && topeSesion.TryGetValue(cc, out tope) && p > tope) p = tope;
                congelado[cc] = p;
                return p;
            }
        }

        // ------------------------------------------------------------------ % de grafica (NVAPI: NvAPI_GPU_GetDynamicPstatesInfoEx)
        static class Nv
        {
            [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr Query(uint id);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnVoid();
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnEnum([Out] IntPtr[] h, out int n);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnBytes(IntPtr gpu, [In, Out] byte[] d);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnPci(IntPtr gpu, out uint dev, out uint sub, out uint rev, out uint ext);
            static FnBytes pstates;
            static IntPtr gpu;
            static int estado;   // 0 sin mirar, 1 listo, -1 no hay
            public static string Error = "";

            static T Fn<T>(uint id) where T : class
            {
                IntPtr p = Query(id);
                return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
            }

            static bool Abrir()
            {
                if (estado != 0) return estado > 0;
                estado = -1;
                try
                {
                    FnVoid init = Fn<FnVoid>(0x0150E828);
                    if (init == null || init() != 0) { Error = "NvAPI_Initialize"; return false; }
                    FnEnum en = Fn<FnEnum>(0xE5AC921F);
                    IntPtr[] hs = new IntPtr[64]; int n;
                    if (en == null || en(hs, out n) != 0 || n <= 0) { Error = "sin graficas de NVIDIA"; return false; }
                    gpu = hs[0];
                    // (la de la huella, VID:DID: el id de NVAPI es DID << 16 | VID)
                    string clave = Escalas.GpuClave;
                    string[] p = clave.Split(':');
                    uint want = 0;
                    if (p.Length >= 2) { uint ven, dev; if (uint.TryParse(p[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ven) && uint.TryParse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out dev)) want = (dev << 16) | ven; }
                    FnPci pci = Fn<FnPci>(0x2DDFB66E);
                    if (want != 0 && pci != null)
                        for (int i = 0; i < n; i++) { uint d, s, r, e; if (pci(hs[i], out d, out s, out r, out e) == 0 && d == want) { gpu = hs[i]; break; } }
                    pstates = Fn<FnBytes>(0x60DED2ED);
                    if (pstates == null) { Error = "sin GetDynamicPstatesInfoEx"; return false; }
                    estado = 1;
                    return true;
                }
                catch (Exception ex) { Error = ex.GetType().Name + ": " + ex.Message; return false; }
            }

            // % de uso del motor de graficos (-1 = no se puede)
            public static int Uso()
            {
                if (!Abrir()) return -1;
                try
                {
                    byte[] b = new byte[72];   // NV_GPU_DYNAMIC_PSTATES_INFO_EX: version, flags, 8 x { bIsPresent, percentage }
                    BitConverter.GetBytes((uint)(72 | (1 << 16))).CopyTo(b, 0);
                    if (pstates(gpu, b) != 0) return -1;
                    if ((BitConverter.ToUInt32(b, 8) & 1) == 0) return -1;
                    return (int)Math.Min(100, BitConverter.ToUInt32(b, 12));
                }
                catch { return -1; }
            }
        }

        static Thread muestreo;
        static volatile bool muestrear;
        static double usoSuma; static int usoN;
        static readonly object usoSync = new object();
        public static string UsoError { get { return Nv.Error; } }

        // Cada 0,5 s mientras haga falta (Activo); la media desde la ultima vez (-1 = sin dato)
        public static void Muestreo(bool activo)
        {
            muestrear = activo;
            if (!activo || muestreo != null || Ce.NoNvidia) return;
            muestreo = new Thread(delegate ()
            {
                while (true)
                {
                    try
                    {
                        Thread.Sleep(500);
                        if (!muestrear) continue;
                        int u = Nv.Uso();
                        if (u < 0) { if (Nv.Error.Length > 0) { Log.Write("Escala de DLSS 5: sin el % de grafica de NVAPI (" + Nv.Error + "): se sube solo con el tiempo"); return; } continue; }
                        lock (usoSync) { usoSuma += u; usoN++; }
                    }
                    catch (Exception ex) { Log.Error("Escala de DLSS 5: % de grafica", ex); return; }
                }
            });
            muestreo.IsBackground = true;
            muestreo.Priority = ThreadPriority.BelowNormal;
            muestreo.Name = "Escala de DLSS 5 (% de grafica)";
            muestreo.Start();
        }

        public static double UsoMedio()
        {
            lock (usoSync)
            {
                double r = usoN >= 3 ? usoSuma / usoN : -1;
                usoSuma = 0; usoN = 0;
                return r;
            }
        }

        public static int UsoAhora() { return Nv.Uso(); }   // (prueba)

        // La pantalla (lo que saca Magpie), mirada como mucho cada 5 s; en las pruebas, la del escenario
        static int salW, salH;
        static DateTime salAt = DateTime.MinValue;
        public static bool SalidaFija;
        public static void Salida(out int w, out int h)
        {
            if (!SalidaFija && (DateTime.UtcNow - salAt).TotalSeconds > 5)
            {
                try { System.Drawing.Rectangle b = System.Windows.Forms.Screen.PrimaryScreen.Bounds; if (b.Width > 0 && b.Height > 0) { salW = b.Width; salH = b.Height; } } catch { }
                salAt = DateTime.UtcNow;
            }
            w = salW > 0 ? salW : 3840; h = salH > 0 ? salH : 2160;
        }
        public static void SalidaPrueba(int w, int h) { SalidaFija = true; salW = w; salH = h; }

        // "60000/1001" -> 59.94, "120" -> 120
        public static double Fps(string f, double def)
        {
            if (string.IsNullOrEmpty(f)) return def;
            string[] p = f.Split('/');
            double a, b;
            if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a) && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b) && b > 0) return a / b;
            if (double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out a) && a > 0) return a;
            return def;
        }
    }

    // ================================================================== la cadena de ahora y el % (Config)
    partial class Config
    {
        public bool Dlss5AutoOn = true;     // dlss5_escala_auto (0 = las tablas tabla_dlss5_* como antes)

        // (desde CeParseEscalas, Escalas.cs)
        bool CeParseEscalaAuto(string key, string val)
        {
            string v = val.Trim().ToLowerInvariant();
            double d;
            switch (key)
            {
                case "dlss5_escala_auto": Dlss5AutoOn = !(v == "0" || v == "no" || v == "false" || v == "off"); return true;
                case "dlss5_escala_uso":
                    if (double.TryParse(v.TrimEnd('%', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) EscalaAuto.Uso = Math.Max(0.4, Math.Min(0.95, d > 1 ? d / 100.0 : d));
                    return true;
                case "dlss5_escala_grafica":
                    if (double.TryParse(v.TrimEnd('%', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) EscalaAuto.UsoGrafica = Math.Max(0.4, Math.Min(1.0, d > 1 ? d / 100.0 : d));
                    return true;
                case "escala_coste_dlss5_flujo": if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) EscalaAuto.CosteDlss5FlujoMp = Math.Min(10, d); return true;
                // (1.2.3, MULTI2) el coste de cada imagen inventada (ms con salida 4K en la RTX 4060 Ti) del modelo
                case "escala_coste_dlss_fg": if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) EscalaAuto.CosteDlssFg = Math.Min(30, d); return true;
                case "escala_coste_xess_fg": if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) EscalaAuto.CosteXessFg = Math.Min(30, d); return true;
                case "escala_coste_dlss_fg_mas": if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) EscalaAuto.CosteDlssFgMas = Math.Min(30, d); return true;
                case "escala_coste_xess_fg_hilo": if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) EscalaAuto.CosteXessFgHilo = Math.Min(30, d); return true;
                case "escala_coste_dlss5_fijo": if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) EscalaAuto.CosteDlss5FijoMp = Math.Min(10, d); return true;
                case "escala_credito_fijo_dlss": if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) EscalaAuto.CreditoFijoDlss = Math.Min(10, d); return true;
                case "generacion_dlss5_minimo": { int iv; if (int.TryParse(v.TrimEnd('%', ' '), out iv)) EscalaAuto.SueloMulti = Math.Max(0, Math.Min(100, iv)); } return true;   // (1.2.3)
                // (1.2.3, MULTI2) XeSS FG x3 / x4 de compatibilidad (no Intel): parte de T/m para la cadena (95 de serie)
                case "xess_fg_multi_uso":
                    if (double.TryParse(v.TrimEnd('%', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) EscalaAuto.UsoXess = Math.Max(0.5, Math.Min(1.5, d > 2 ? d / 100.0 : d));
                    return true;
                case "escala_gpu_indice": if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) EscalaAuto.IndiceIni = d; return true;
                case "escala_gpu_vram": if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) EscalaAuto.VramIni = d; return true;
                case "escala_gpu_familia": EscalaAuto.FamiliaIni = v; return true;
            }
            return false;
        }

        // (Dlss5PassesNow / Dlss5PctNow / DlssModelNow, dentro de la cuenta de la cadena: lo que pasa otra vez por ellos ve las pasadas pedidas)
        [ThreadStatic] static int autoDentro;

        // (no con DLSS 5 en el motor propio, motor_dlss5=1: su coste es otro y lo ajusta el motor, EngineAdapta.cs)
        public bool Dlss5AutoAhora { get { return Dlss5AutoOn && Dlss5InputPct == 0 && !(EngineDlss5 && Engine == "propio"); } }

        // El modelo de DLSS SR de Magpie con DLSS 5 y estas pasadas (lo de DlssModelNow sin pasar por Dlss5PassesNow), con el tope de la vigilancia
        int ModeloPara(int pasadas, string clave)
        {
            int m0;
            string pk = DlssPick;
            if (pk.Length > 0) m0 = DlssPickNum(pk);
            else if (CapMode && DlssModel30 > 0) m0 = DlssModel30;
            else if (DlssModel > 0) m0 = DlssModel;
            else { int[] m; m0 = ModelGet(out m) ? (m.Length >= 4 ? m[Math.Max(1, Math.Min(3, pasadas))] : m.Length > 1 ? m[1] : m[0]) : 5; }
            int tope = EscalaAuto.TopeModelo(EscalaAuto.Juego ?? "", clave, pk);
            return tope > 0 && CosteModelo(m0) > CosteModelo(tope) ? tope : m0;
        }
        static double CosteModelo(int m) { return m == 12 ? 4 : m == 13 ? 3 : m == 11 || m == 10 ? 2 : m == 5 ? 1 : 2.5; }

        // La cadena de imagen de ahora con estas pasadas (lo que cuesta grafica en Magpie)
        public CadenaEscala CadenaEscalaPara(int pasadas)
        {
            CadenaEscala c = new CadenaEscala();
            autoDentro++;
            try
            {
                c.Modo = Mode120 ? 120 : CapMode ? 30 : 60;
                c.Objetivo = Mode120 ? EscalaAuto.Fps(FramerateNow, 120) : CapMode ? 29.97 : 59.94;
                string[] cs = CaptureNow.Split('x'), ws = WindowFor(CaptureNow).Split('x');
                int.TryParse(cs[0], out c.CapW); if (cs.Length > 1) int.TryParse(cs[1], out c.CapH);
                int.TryParse(ws[0], out c.VenW); if (ws.Length > 1) int.TryParse(ws[1], out c.VenH);
                EscalaAuto.Salida(out c.OutW, out c.OutH);
                c.Gen = FgOff || Mode120 ? "" : FgTypeNow;
                c.Mult = FgMultNow;
                c.Escala = UpscaleOn;
                c.Esc = ScalerNow;
                c.Ligero = LightNow;
                c.FlujoSr = DlssFlowNow > 0;
                c.AfUna = c.Modo == 30 && Af30OnePass != 0;   // (1.2.2: el antiparpadeo de 1 pasada en el modo 30, src\Lanzador\Reescalado.cs)
                c.VectoresFg = c.Gen == "dlss" && !Win4k && (CapMode ? FgFlow30 : FgFlow60) > 0;
                c.Hdr = RtxHdr; c.HdrAntes = RtxHdrBefore;
                c.Intel = GpuVendor == "intel";
                int f5 = Dlss5FlowNow;   // (1.2.3: el flujo de DLSS 5 -0 ninguno, 1 AMD, 2 NVIDIA- y su calidad: lo fijo de DLSS 5 en el hilo)
                c.FlujoD5 = f5 <= 0 ? 0 : f5 == 2 && FlowQualityNvNow <= 1 ? 0.8 : 1.0;
                c.Vrr = MagpieVrrNow;    // (1.2.3: DLSS FG x2 con refresco fijo espera menos a la cola de presentacion)
                EscalaAuto.GpuPara(c);
                c.Juego = EscalaAuto.Juego ?? "";
                c.Modelo = ModeloPara(pasadas, c.Clave);
            }
            finally { autoDentro--; }
            return c;
        }

        // Dlss5PassesNow con la escala automatica: las pedidas o, si no caben ni al minimo, menos (0 = ni una)
        int AutoPasadas(int want)
        {
            if (autoDentro > 0) return want;
            autoDentro++;
            try
            {
                for (int n = want; n >= 1; n--)
                    if (EscalaAuto.PctPara(CadenaEscalaPara(n), n) > 0) return n;
                return 0;
            }
            catch (Exception ex) { Log.Error("Escala de DLSS 5 (pasadas)", ex); return want; }
            finally { autoDentro--; }
        }

        // Dlss5PctNow con la escala automatica
        int AutoPctAhora()
        {
            if (autoDentro > 0) return 100;   // (no deberia pasar: nada de la cadena mira el %)
            int n = Math.Max(1, Dlss5PassesNow);
            autoDentro++;
            try { return Math.Max(0, EscalaAuto.PctPara(CadenaEscalaPara(n), n)); }
            catch (Exception ex) { Log.Error("Escala de DLSS 5 (%)", ex); return 50; }
            finally { autoDentro--; }
        }

        // PassesFitWithoutFg con la escala automatica: ¿caben n pasadas sin la generacion? (la misma cadena sin ella; sin tocar FgOff, que
        // leen otros hilos)
        bool AutoCabeSinFg(int n)
        {
            if (n < 1 || n > 3) return false;
            CadenaEscala c = CadenaEscalaPara(n);
            c.Gen = "";
            return EscalaAuto.Mejor(c, n, EscalaAuto.K, EscalaAuto.Extra(c.Juego, c.Clave)) > 0;
        }

        // DlssModelNow con DLSS 5 por Magpie: el tope que puso la vigilancia (M/L -> K -> E) en esta cadena
        int AutoModelo(int m)
        {
            if (!Dlss5AutoAhora || autoDentro > 0 || Dlss5Off || !UpscaleOn || ScalerNow != "dlss" || EngineDlss5Now || !Dlss5FitsNow) return m;
            autoDentro++;
            try
            {
                CadenaEscala c = CadenaEscalaPara(Math.Max(1, Passes30));
                int tope = EscalaAuto.TopeModelo(c.Juego, c.Clave, DlssPick);
                return tope > 0 && CosteModelo(m) > CosteModelo(tope) ? tope : m;
            }
            catch { return m; }
            finally { autoDentro--; }
        }
    }

    // ================================================================== la vigilancia (TrayApp)
    partial class TrayApp
    {
        readonly EscalaAuto.Seguimiento escSeg = new EscalaAuto.Seguimiento();
        DateTime escCambioAt = DateTime.MinValue, escVistaAt = DateTime.MinValue, escNotaAt = DateTime.MinValue;
        int escBusy;
        double mpvDiagFps;
        DateTime mpvDiagAt = DateTime.MinValue;

        // (enlace de mpv, switch2-diag cada 20 s: "580 imagenes, media 33.2 ms | ...") las que da mpv de verdad (a 120 el script de 0,5 s
        // se queda corto)
        void EscalaNotaDiag(string texto)
        {
            try
            {
                Match m = Regex.Match(texto ?? "", @"^(\d+) imagenes, media ([0-9.]+) ms");
                if (!m.Success) return;
                double ms = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (ms > 1) { mpvDiagFps = 1000.0 / ms; mpvDiagAt = DateTime.Now; }
            }
            catch { }
        }

        // Las imagenes que da mpv (tras el filtro de repetidas): su script cada 0,5 s o, si es mas, su recuento de 20 s (reciente)
        double FuenteFps() { return Math.Max(vfFps, (DateTime.Now - mpvDiagAt).TotalSeconds < 30 ? mpvDiagFps : 0); }

        // CheckLsPacing (cada 5 s con Magpie): true = DLSS 5 por Magpie con la escala automatica (lo vigila esto; FpsMagpieVigila no)
        bool EscalaVigila(LsStats st)
        {
            DateTime now = DateTime.Now;
            double dt = escVistaAt == DateTime.MinValue ? 5 : Math.Max(1, Math.Min(15, (now - escVistaAt).TotalSeconds));
            escVistaAt = now;
            PrepareCfg();
            bool nuestro = cfg.Dlss5AutoAhora && cfg.D5Tuned && !cfg.EngineDlss5Now && !Config.EngineActive && !cfg.HomeNative && MagpieLive.Pid() > 0;
            EscalaAuto.Muestreo(nuestro);
            if (!nuestro) return false;
            try
            {
                if (st.CapN == 0) return true;   // (sin resumen nuevo de Magpie en estos 5 s: en el modo 30 sale uno cada ~4 s; se sigue la cuenta)
                double uso = EscalaAuto.UsoMedio();
                bool juzgar = !InHome && !homeBusy && !passesBusy && relaunchSize == null && !relaunchEngine && !userExit && lsReady && !applying &&
                              MagpieLive.RunningFor() > 25 && (now - escCambioAt).TotalSeconds >= 20 && Interlocked.CompareExchange(ref escBusy, 0, 0) == 0;
                if (!juzgar) { escSeg.Mal = escSeg.Grave = 0; escSeg.SegBien = 0; return true; }
                int n = Math.Max(1, cfg.Dlss5PassesNow), pct = cfg.Dlss5PctNow;
                CadenaEscala c = cfg.CadenaEscalaPara(n);
                EscalaAuto.Medida m = new EscalaAuto.Medida();
                m.Real = st.CapTime > 0 ? st.NewSum / st.CapTime : st.Captured;
                m.RealMin = st.CapMin;
                m.Fuente = FuenteFps();
                m.Objetivo = c.Objetivo;
                bool fgMagpie = c.Gen.Length > 0 && c.Gen != "smooth";
                if (fgMagpie && st.CapTime > 0 && st.GenSum > 0)
                {
                    m.Salida = (st.NewSum + st.GenSum) / st.CapTime;
                    int hz = Pantalla.Hz();
                    m.SalidaObjetivo = Math.Min(c.Objetivo * c.Mult, hz >= 60 ? hz - 0.2 : c.Objetivo * c.Mult);
                }
                m.Uso = uso;
                m.Huecos = st.Gaps;
                m.Segundos = dt;
                double k = EscalaAuto.K, kn = EscalaAuto.NuevoK(c, m, pct, n, k);
                if (Math.Abs(kn - k) > 0.001) EscalaAuto.K = kn;
                EscalaAuto.Opciones o = new EscalaAuto.Opciones();
                o.Pct = pct; o.Pasadas = n; o.K = EscalaAuto.KDe(c.Juego, c.Clave);   // (el de lo aprendido de esta cadena; sin nada, el de la grafica)
                o.Extra = EscalaAuto.Extra(c.Juego, c.Clave);
                o.NoPct = EscalaAuto.NoPct(c.Juego, c.ClaveCompleta(n));
                o.GenBarata = EscalaGenBarata(c.Gen);
                if (escSubida == c.ClaveCompleta(n) + "|" + pct) o.SegSubir = 1e9;   // (ya pedida una subida de esto: hasta la proxima apertura, nada mas)
                EscalaAuto.Decision d = EscalaAuto.Decidir(c, m, escSeg, o);
                // 60 s llegando con lo de ahora y sin nada aprendido de esta cadena en este juego: lo de ahora queda aprendido (un k de la
                // grafica nuevo, medido en otra cadena, ya no lo cambia en la proxima apertura)
                if (d.Fijar && d.Tipo == "nada" && !EscalaAuto.Aprendida(c.Juego, c.Clave))
                {
                    EscalaAuto.AnotarExtra(c.Juego, c.Clave, EscalaAuto.ExtraPara(c, pct, n, o.K), o.K);
                    Log.Write("Escala de DLSS 5: " + n + " pasada(s) al " + pct + " % llega a los fps del modo (60 s): aprendido para " +
                              (string.IsNullOrEmpty(c.Juego) ? "cualquier juego" : "\"" + c.Juego + "\"") + " (" + c.Clave + ")");
                }
                // (al log, como mucho cada 60 s: lo que se ve)
                if ((now - escNotaAt).TotalSeconds >= 60)
                {
                    escNotaAt = now;
                    Log.Write("Escala de DLSS 5: " + n + " pasada(s) al " + pct + " %" + (c.Esc == "dlss" && c.Escala ? ", DLSS " + EscalaAuto.Letra(c.Modelo) : "") + ", " +
                              F1(m.Real) + " de " + F1(m.Objetivo) + " reales (mpv " + F1(m.Fuente) + ")" + (m.Salida > 0 ? ", salida " + F1(m.Salida) + " de " + F1(m.SalidaObjetivo) : "") +
                              (uso >= 0 ? ", grafica " + Math.Round(uso) + " %" : "") + ", k " + kn.ToString("0.00", CultureInfo.InvariantCulture) + ", previsto " +
                              F1(EscalaAuto.Prevision(c, pct, n, kn, o.Extra)) + " de " + F1(EscalaAuto.Presupuesto(c)) + " ms: " + d.Motivo + " (" + c.Clave + ")");
                }
                if (d.Tipo == "bajar") EscalaBajar(c, d, pct, n, m);
                else if (d.Tipo == "escalera")
                {
                    escCambioAt = now;
                    EscalaAuto.AnotarExtra(c.Juego, c.Clave, d.Extra, d.K);   // (la proxima vez con DLSS 5 en esta cadena, mas prudente)
                    Log.Write("Escala de DLSS 5: " + d.Motivo);
                    FpsLanza(m.Real, Math.Min(m.Objetivo, m.Fuente), uso >= 0 ? uso / 100.0 * 1000.0 / Math.Max(1, m.Real) : 0, "Magpie");
                }
                else if (d.Tipo == "subir") EscalaSubir(c, d, pct);
            }
            catch (Exception ex) { Log.Error("Escala de DLSS 5 (vigilancia)", ex); }
            return true;
        }

        // La generacion de la lista del modo mas barata que la de ahora (la mas cercana); "" = ninguna
        string EscalaGenBarata(string g)
        {
            if (g.Length == 0) return "";
            try
            {
                string best = "";
                foreach (string t in GenOptions())
                    if (EscalaAuto.RangoGen(t) > 0 && EscalaAuto.RangoGen(t) < EscalaAuto.RangoGen(g) && (best.Length == 0 || EscalaAuto.RangoGen(t) > EscalaAuto.RangoGen(best))) best = t;
                return best;
            }
            catch { return ""; }
        }

        static string Fps0(double v) { return Math.Round(v).ToString(CultureInfo.InvariantCulture); }

        // No llega: lo nuevo ya (aprendido para la proxima vez y, de una vez, en Magpie con el puente) y el aviso
        void EscalaBajar(CadenaEscala c, EscalaAuto.Decision d, int pctAntes, int pasAntes, EscalaAuto.Medida m)
        {
            if (Interlocked.CompareExchange(ref escBusy, 1, 0) != 0) return;
            escCambioAt = DateTime.Now;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    CadenaEscala nueva = c.Copia();
                    nueva.Modelo = d.Modelo; nueva.Gen = d.Gen;
                    if (d.Gen == "smooth") nueva.Mult = 2;
                    EscalaAuto.AnotarBajada(c.Juego, nueva.Clave, c.ClaveCompleta(pasAntes), pctAntes, d.Extra, d.K, d.Pasos.Contains("modelo") ? d.Modelo : 0, Config.DlssPick,
                                            nueva.ClaveCompleta(d.Pasadas), d.Pct);
                    List<string> es = new List<string>(), en = new List<string>();
                    if (d.Pct != pctAntes) { es.Add("DLSS 5 al " + pctAntes + " % → " + d.Pct + " %"); en.Add("DLSS 5 scale " + pctAntes + "% -> " + d.Pct + "%"); }
                    if (d.Pasos.Contains("pasadas"))
                    {
                        es.Add(Pasadas(pasAntes) + " → " + Pasadas(d.Pasadas)); en.Add(pasAntes + " -> " + d.Pasadas + (d.Pasadas > 1 ? " passes" : " pass"));
                        PasadasOnPasses(pasAntes, d.Pasadas);
                        dlss5Passes = d.Pasadas;
                        SaveText(Paths.Dlss5PassesFile, d.Pasadas.ToString(CultureInfo.InvariantCulture));
                    }
                    if (d.Pasos.Contains("modelo")) { es.Add("DLSS " + EscalaAuto.Letra(c.Modelo) + " → " + EscalaAuto.Letra(d.Modelo)); en.Add("DLSS " + EscalaAuto.Letra(c.Modelo) + " -> " + EscalaAuto.Letra(d.Modelo)); }
                    if (d.Pasos.Contains("gen"))
                    {
                        es.Add(GenLabel(c.Gen, false) + " → " + GenLabel(d.Gen, false)); en.Add(GenLabel(c.Gen, false) + " -> " + GenLabel(d.Gen, false));
                        if (cfg.CapMode) SetPicks(cfg.FgPick60, d.Gen); else SetPicks(d.Gen, cfg.FgPick30);
                    }
                    string obj = Fps0(d.PorSalida ? d.SalidaObjetivo : d.Objetivo), real = Fps0(d.PorSalida ? d.Salida : d.Real);
                    string texto = Tx.T("No se llega a " + obj + " fps (" + real + ") · " + string.Join(" · ", es.ToArray()),
                                        obj + " fps not reached (" + real + "): " + string.Join(", ", en.ToArray()));
                    Log.Write("Escala de DLSS 5: " + d.Motivo + ": " + string.Join(", ", es.ToArray()) + " (ya, reabriendo Magpie; guardado para la proxima vez en " + EscalaAuto.Fichero + ")");
                    SaveCurProfile();
                    lastPadAction = DateTime.Now;   // (el puente / telon de la reapertura lleva el aviso, como con un boton)
                    RestartLs(Tx.T("No se llega a " + obj + " fps", obj + " fps not reached"), Tx.T(string.Join(" · ", es.ToArray()), string.Join(", ", en.ToArray())));
                    // el aviso cuando Magpie vuelve a escalar (la reapertura dura unos segundos)
                    for (int i = 0; i < 80 && (passesBusy || !lsReady) && !userExit; i++) Thread.Sleep(250);
                    OnUi(delegate { OsdWarn(texto, "7000"); });
                }
                catch (Exception ex) { Log.Error("Escala de DLSS 5 (bajando)", ex); }
                finally { escCambioAt = DateTime.Now; Interlocked.Exchange(ref escBusy, 0); }
            });
        }

        // Sobra: la subida queda para la proxima apertura de Magpie (no se reabre por ella)
        string escSubida = "";
        void EscalaSubir(CadenaEscala c, EscalaAuto.Decision d, int pctAntes)
        {
            escSubida = c.ClaveCompleta(d.Pasadas) + "|" + pctAntes;
            EscalaAuto.AnotarSubida(c.Juego, c.Clave, d.Extra, d.K);
            Log.Write("Escala de DLSS 5: " + d.Motivo + ": la proxima vez que se abra Magpie (al volver a entrar en el juego) DLSS 5 al " + d.Pct + " % (ahora " + pctAntes + " %)");
            Osd(Tx.T("Sobra gráfica · DLSS 5 al " + pctAntes + " % → " + d.Pct + " % al volver a entrar en el juego",
                     "GPU headroom: DLSS 5 scale " + pctAntes + "% -> " + d.Pct + "% next time you start the game"));
        }
    }
}
