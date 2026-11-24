// SalidaHome.cs - 24-11-2026 (1.3.2, SWITCH2). Dos cosas que pidio el usuario tras jugar con la 1.3.1 (sesion del 24-11 01:24-01:53):
//
// 1. "Al salir de un juego en switch 2 a veces se le olvida volver al modo home". Jugando, la vuelta al HOME se veia SOLO con el nombre
//    cian del juego seleccionado en su fila (y las franjas del HOME) o con su hora arriba a la derecha leida por OCR. Las franjas del HOME
//    (la de arriba con usuarios / hora / bateria y la barra de iconos de abajo, games\home_ref.txt) casi iguales -HomeDiff <= 6, "muy
//    claro"- sin el nombre a la vista (el cursor en la barra de abajo, una notificacion o la marquesina tapando la fila, el nombre que el
//    lector de la 1.3.1 descarta por estar fuera de su fila) no contaban NADA: la hora tampoco se miraba (solo con las franjas distintas) y la
//    app seguia en el juego, con su captura y su generacion, hasta que el nombre se leyera. Ahora, jugando, esas franjas cuentan como el
//    HOME (con otra captura a los 0,15 s, como lo demas; nunca con el HOME atenuado). ini home_por_franjas=0 = como antes.
//    (Comprobado con las capturas reales del HOME a los tamaños de captura del juego -1080p / 1440p / 4K, la captura ligera del motor-: HomeDiff
//    0,9-2,4; las de juegos de la Switch 2 y de la PS5 de test_deteccion: ninguna por debajo de 6. --test-switch2, parte 12)
//
// 2. "A veces al aplicar perfiles hay un pantallazo negro 1 segundo". Al entrar en un juego desde el HOME (y al volver) StartHomeSwitch ponia
//    SIEMPRE el telon negro (heredado de mpv + Magpie: alli el cambio de captura enseñaba basura) y lo quitaba al LLEGAR la primera imagen de
//    la captura nueva ("Telon quitado: 211-315 ms en negro" + 250 ms de fundido), y el motor aun tardaba 0,5-0,9 s en PINTARLA (DLSS 5 creado
//    dos veces, ver src\Motor\Engine\EngineCambioCaptura.cs): tras la carga en negro de la consola (Juego 1 / TOTK: "negro 1.0 s tras elegir
//    usuario") eso era ~1 s mas de negro. Con el motor propio el cambio va en el mismo motor (ajustes y captura) y el motor nunca enseña
//    basura: sin telon, la ultima imagen sigue a la vista hasta la nueva. El telon sigue si hay que reabrir la imagen (otro motor, la captura
//    que no llega). ini telon_home_juego=1 = como antes.
// C# 5
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // ini home_por_franjas=1|0 (de serie 1): jugando, el HOME de la Switch 2 tambien por sus franjas (sin el nombre cian)
        public bool HomePorFranjas = true;
        // ini telon_home_juego=0|1 (de serie 0): el telon negro al pasar del HOME al juego y al reves con el motor propio (1 = como la 1.3.1)
        public bool TelonHomeJuego = false;

        public bool ParseSalidaHome(string key, string val)
        {
            switch (key)
            {
                case "home_por_franjas": HomePorFranjas = ParseBool(val); return true;
                case "telon_home_juego": TelonHomeJuego = ParseBool(val); return true;
            }
            return false;
        }
    }

    static class SalidaHomeReglas
    {
        // HomeDiff por debajo de la cual las franjas son las del HOME sin dudar (la de AnalyzeGrab para entrar con una sola captura)
        public const double FranjasClaras = 6;

        // Jugando, sin el nombre cian: ¿las franjas son las del HOME? (y no el HOME atenuado: un aviso encima, el menu de apagado)
        public static bool PorFranjas(double[] strips, double[] homeRef, double hd)
        {
            if (strips == null || homeRef == null || hd > FranjasClaras) return false;
            double k, corr;
            return !HomeLabel.DimHome(strips, homeRef, out k, out corr);
        }

        // Lo que decide la rama "jugando" de AnalyzeGrab con una captura (para --test-switch2): "nombre" (el nombre cian en su fila y las franjas),
        // "franjas" (solo las franjas: lo nuevo), "" nada. (La hora del HOME por OCR no se prueba aqui: depende de la hora del PC)
        public static string Decide(Bitmap full, double[] homeRef, bool porFranjas, out double hd)
        {
            hd = 999;
            using (Bitmap small = HomeLabel.Small(full))
            {
                Rectangle box;
                bool hint;
                HomeLabel.LabelText label;
                int hintY;
                byte[] lab = HomeLabel.Selected(small, out box, out hint, out label, out hintY);
                double[] strips = HomeLabel.Strips(small);
                if (HomeLabel.IsNoSignal(small)) return "";
                hd = HomeLabel.HomeDiff(strips, homeRef);
                double k, c;
                if (lab != null && homeRef != null && HomeLabel.DimHome(strips, homeRef, out k, out c)) lab = null;
                if (lab != null) return homeRef != null && hd <= HomeLabel.IsHome ? "nombre" : "";
                return porFranjas && PorFranjas(strips, homeRef, hd) ? "franjas" : "";
            }
        }
    }

    partial class TrayApp
    {
        DateTime homeFranjasLog = DateTime.MinValue;

        bool HomePorFranjas(double[] strips, double hd)
        {
            return cfg.HomePorFranjas && SalidaHomeReglas.PorFranjas(strips, homeRef, hd);
        }

        void HomePorFranjasLog(double hd)
        {
            if ((DateTime.Now - homeFranjasLog).TotalSeconds < 30) return;
            homeFranjasLog = DateTime.Now;
            Log.Write("HOME: sin el nombre cian en su fila, pero con sus franjas (diferencia " + hd.ToString("0.0", CultureInfo.InvariantCulture) +
                      " de 6): cuenta como el HOME (home_por_franjas)");
        }

        // StartHomeSwitch: sin telon con el motor propio (el cambio va en el mismo motor)
        bool SinTelonHomeJuego()
        {
            return Config.EngineActive && !cfg.TelonHomeJuego;
        }
    }
}
