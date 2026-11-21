// (1.2.04) El corrector ortografico de Windows (ISpellChecker, Windows 8+), solo para limpiar nombres de juegos leidos por OCR: el ® de
// "Rocket League®" lo leia como una "n" pegada ("Rocket Leaguen"). Si falla (sin diccionarios, otra version de Windows) no hace nada
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ConsoleEnhancer
{
    static class Ortografia
    {
        [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISpellCheckerFactory
        {
            [PreserveSig] int GetSupportedLanguages(out IntPtr value);
            [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, [MarshalAs(UnmanagedType.Bool)] out bool value);
            [PreserveSig] int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
        }

        [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISpellChecker
        {
            [PreserveSig] int GetLanguageTag(out IntPtr value);
            [PreserveSig] int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
        }

        [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IEnumSpellingError
        {
            [PreserveSig] int Next(out IntPtr value);   // S_OK = hay un error; S_FALSE = no hay mas
        }

        [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
        class SpellCheckerFactory { }

        static readonly object sync = new object();
        static List<ISpellChecker> checkers;
        static readonly Dictionary<string, bool> cache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        static List<ISpellChecker> Checkers()
        {
            if (checkers != null) return checkers;
            checkers = new List<ISpellChecker>();
            try
            {
                ISpellCheckerFactory f = (ISpellCheckerFactory)new SpellCheckerFactory();
                foreach (string lang in new[] { "en-US", "en-GB", "es-ES" })
                {
                    bool ok;
                    ISpellChecker sc;
                    if (f.IsSupported(lang, out ok) == 0 && ok && f.CreateSpellChecker(lang, out sc) == 0 && sc != null) checkers.Add(sc);
                }
            }
            catch (Exception ex) { Log.Write("Corrector de Windows no disponible para limpiar nombres: " + ex.Message); }
            return checkers;
        }

        // ¿Palabra valida en algun diccionario? null = no se sabe (sin corrector)
        // 21-11 (1.2.1): el corrector de Windows da por buena cualquier palabra EN MAYUSCULAS (no las mira: "PLAYÑOOM", "XYZQW"): esas se
        // miran en minusculas y con la primera en mayuscula ("ROCKET LEAGUEN" -> "LEAGUE", como "Rocket Leaguen"). Ojo: en el PC del
        // usuario solo hay el diccionario de español (en-US / en-GB no instalados): "Rocket" o "Theft" no estan; solo sirve de desempate
        public static bool? Valida(string palabra)
        {
            if (string.IsNullOrEmpty(palabra)) return null;
            if (palabra.Length >= 2 && palabra.ToUpperInvariant() == palabra && palabra.ToLowerInvariant() != palabra)
            {
                string min = palabra.ToLowerInvariant(), cap = palabra.Substring(0, 1) + palabra.Substring(1).ToLowerInvariant();
                bool? a = ValidaTal(min);
                if (a == true) return true;
                bool? b = ValidaTal(cap);
                return b == true ? true : (a == null && b == null ? (bool?)null : false);
            }
            return ValidaTal(palabra);
        }

        static bool? ValidaTal(string palabra)
        {
            lock (sync)
            {
                bool v;
                if (cache.TryGetValue(palabra, out v)) return v;
                List<ISpellChecker> cs = Checkers();
                if (cs.Count == 0) return null;
                v = false;
                foreach (ISpellChecker sc in cs)
                {
                    try
                    {
                        IEnumSpellingError en;
                        if (sc.Check(palabra, out en) != 0 || en == null) continue;
                        IntPtr err;
                        int hr = en.Next(out err);
                        if (err != IntPtr.Zero) Marshal.Release(err);
                        Marshal.ReleaseComObject(en);
                        if (hr != 0) { v = true; break; }   // sin errores
                    }
                    catch { }
                }
                if (cache.Count > 2000) cache.Clear();
                cache[palabra] = v;
                return v;
            }
        }

        // La marca ® / ™ final leida como letra pegada a la ultima palabra ("Leaguen", "LeagueR"): se quita si la palabra no existe y sin esa
        // letra si. "Batman" se queda (existe, o "Batma" tampoco)
        public static string QuitaMarcaPegada(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return nombre;
            try
            {
                Match m = Regex.Match(nombre, @"(\p{L}{3,})([nRr®])$");
                if (!m.Success) return nombre;
                string w = m.Groups[1].Value + m.Groups[2].Value, sin = m.Groups[1].Value;
                bool? vw = Valida(w), vs = Valida(sin);
                if (vw == false && vs == true) return nombre.Substring(0, nombre.Length - 1);
            }
            catch { }
            return nombre;
        }
    }
}
