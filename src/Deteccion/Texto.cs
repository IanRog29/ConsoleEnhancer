// Texto.cs - textos de la deteccion en espanol o ingles (ConsoleEnhancer)
//
// Tx.T("texto en espanol", "english text"): el de la lengua de Windows (espanol si la interfaz esta en espanol; si no, ingles).
// La app de verdad (fase 2) tendra su tabla de textos; esto es solo para los informes de la deteccion y del plan.
using System;
using System.Globalization;

namespace ConsoleEnhancer.Deteccion
{
    public static class Tx
    {
        static int ingles = -1;   // -1 = sin decidir (la lengua de Windows)

        public static bool Ingles
        {
            get
            {
                if (ingles < 0) ingles = 1;   // (1.2.06) de serie en ingles; la app pone el elegido (Idioma.Poner)
                return ingles == 1;
            }
            set { ingles = value ? 1 : 0; }
        }

        public static string Idioma { get { return Ingles ? "en" : "es"; } }

        public static string T(string es, string en) { return Ingles ? en : es; }

        public static string SiNo(bool v) { return Ingles ? (v ? "yes" : "no") : (v ? "sí" : "no"); }

        // Numero con coma o punto segun la lengua
        public static string N(double v, string fmt)
        {
            return v.ToString(fmt, Ingles ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("es-ES"));
        }

        public static string Mb(ulong bytes)
        {
            double gb = bytes / (1024.0 * 1024 * 1024);
            if (gb >= 1) return N(gb, "0.#") + " GB";
            return N(bytes / (1024.0 * 1024), "0") + " MB";
        }
    }
}
