// BotonesCe.cs - los botones de ConsoleEnhancer en cualquier mando (20-11-2026, agente CE-FUNCIONES)
//
// El usuario: "el boton de 60 fps y 30 que sean los dos el boton options y el otro boton que sea el de frame generation, tiene que
// estar adaptado a los mandos en general". Con la imagen de la consola delante (mando_botones=ce, de serie):
//   Options (PlayStation) / Menu (Xbox, ≡) / + (Nintendo) / Start (genericos)          = modo 30 <-> 60 fps (alterna)
//   Share (DS4) / Crear (DualSense) / Vista (Xbox, ⧉) / - (Nintendo) / Select (genericos) = generacion de fotogramas si / no
// Antes (Prisma, mando_botones=prisma): Share/-/Vista = modo 30, Options/+/Menu = modo 60, y la generacion solo con Captura (Switch,
// 8BitDo, GameSir) o el panel tactil (PlayStation). No se pierde nada: el modo 30/60 queda en un boton (alterna), y Captura / panel
// tactil / el boton de captura del GameSir siguen siendo la generacion (como antes). Lo demas igual: cruceta = captura, X/Y/B = pasadas
// de DLSS 5, A = DLSS 5 si/no, L1/L2/R2/R1 = nivel, L3 = tipo de generacion (mantenido: filtro de tramado), R3 = escalador.
//
// El mando se comparte con la consola: la app lo lee sin quedarselo (XInput y Raw Input con INPUTSINK, sin HidHide ni nada exclusivo;
// src\Mandos.cs) y solo actua con la imagen de la consola delante. La pulsacion llega tambien a la consola (Options abre el menu del
// juego en la PS5, + el de la Switch...): es lo de siempre con cualquier boton; aqui no se cambia nada de eso.
//
// Nombres segun el mando (PadHub.MainKind) y el idioma (Idioma.Ingles): la ayuda del log (PadNames.HelpLine, si mando_botones=ce), el
// paso "Mando" del asistente y los textos en pantalla que decian "boton de captura" (Idioma.T -> BotonesCe.EnTexto)
using System;

namespace ConsoleEnhancer
{
    static class BotonesCe
    {
        // mando_botones=ce (de serie) | prisma (Vista = modo 30, Menu = modo 60, como Prisma)
        public static volatile bool Prisma;

        public static bool Parse(string key, string val)
        {
            if (key != "mando_botones") return false;
            string v = val.Trim().ToLowerInvariant();
            Prisma = v == "prisma" || v == "clasico" || v == "antes";
            return true;
        }

        public static bool Activo { get { return !Prisma; } }

        // (ingles solo en pantalla: el log siempre en espanol)
        static bool En { get { return Idioma.Ingles; } }

        static PadKind Kind
        {
            get { try { return PadHub.Shared.MainKind; } catch { return PadKind.XInput; } }
        }

        // El tipo de mando por VID/PID (como PadHub con Raw Input; para el asistente, con lo que ve la deteccion)
        public static PadKind KindDe(int vid, int pid, bool xinput)
        {
            if (vid == 0x054C) return pid == 0x0CE6 ? PadKind.DualSense : pid == 0x0DF2 ? PadKind.DualSenseEdge : PadKind.DualShock4;
            if (vid == 0x057E) return pid == 0x2006 ? PadKind.JoyConL : pid == 0x2007 ? PadKind.JoyConR : (pid == 0x2069 || pid == 0x2066 || pid == 0x2067 || pid == 0x2073) ? PadKind.Switch2Pro : PadKind.SwitchPro;
            if (vid == 0x045E) return PadKind.Xbox;
            if (vid == 0x3537) return xinput ? PadKind.XInput : PadKind.GameSir;
            if (vid == 0x2DC8) return xinput ? PadKind.XInput : PadKind.EightBitDo;
            return xinput ? PadKind.XInput : PadKind.Generic;
        }

        // ---- nombres (screen = con tildes y simbolos para la pantalla; sin, para el log) ----
        // El de Vista / Share / Crear / - : la generacion de fotogramas
        public static string NombreVista(PadKind k, bool screen)
        {
            if (k == PadKind.DualShock4) return "Share";
            if (PadNames.IsPlayStation(k)) return screen && En ? "Create" : "Crear";
            if (PadNames.IsNintendo(k)) return screen ? "−" : "-";
            if (k == PadKind.Generic) return "Select";
            return screen && En ? "View" : "Vista";
        }

        // El de Options / Menu / + : el modo 30 <-> 60
        public static string NombreMenu(PadKind k, bool screen)
        {
            if (PadNames.IsPlayStation(k)) return screen && En ? "Options" : (screen ? "Opciones" : "Options");
            if (PadNames.IsNintendo(k)) return "+";
            if (k == PadKind.Generic) return "Start";
            return screen && En ? "Menu" : (screen ? "Menú" : "Menu");
        }

        // Con el simbolo del mando de Xbox (asistente: la fuente de Windows lo tiene)
        public static string NombreVistaLargo(PadKind k) { return NombreVista(k, true); }   // (⧉ no esta en Consolas: se descuadra la tabla)
        public static string NombreMenuLargo(PadKind k) { string n = NombreMenu(k, true); return k == PadKind.Xbox || k == PadKind.XInput || k == PadKind.GameSir ? n + " (≡)" : n; }

        // El otro boton que tambien hace la generacion (Captura, panel tactil, el de captura del GameSir); "" si no tiene
        public static string NombreCaptura(PadKind k, bool screen)
        {
            bool en = screen && En;
            if (PadNames.IsPlayStation(k)) return en ? "touchpad" : screen ? "panel táctil" : "panel tactil";
            if (PadNames.IsNintendo(k)) return en ? "Capture" : "Captura";
            if (k == PadKind.GameSir) return en ? "capture button" : screen ? "botón de captura" : "boton de captura";
            return "";
        }

        // ---- ayuda (log al conectar un mando y asistente) ----
        public static string Ayuda(PadKind k, bool screen)
        {
            bool en = screen && En;
            string lt = PadNames.TriggerName(k, true), rt = PadNames.TriggerName(k, false);
            string cap = NombreCaptura(k, screen);
            if (en)
                return "Menu button " + NombreMenu(k, screen) + " = 30 / 60 fps mode (toggles), " + NombreVista(k, screen) + " = frame generation on/off" +
                       (cap.Length > 0 ? " (also " + cap + ")" : "") + ", d-pad = capture size, " +
                       PadNames.Name(k, PadBtn.X, false) + "/" + PadNames.Name(k, PadBtn.Y, false) + "/" + PadNames.Name(k, PadBtn.B, false) + " = 1/2/3 DLSS 5 passes, " +
                       PadNames.Name(k, PadBtn.A, false) + " = DLSS 5 on/off, " + PadNames.Name(k, PadBtn.LB, false) + "/" + lt + "/" + rt + "/" + PadNames.Name(k, PadBtn.RB, false) +
                       " = level 1-4, L3 = frame generation type (hold 1 s = dither filter), R3 = scaler";
            return NombreMenu(k, screen) + " = modo 30 / 60 fps (alterna), " + NombreVista(k, screen) + " = generacion de fotogramas si/no" +
                   (cap.Length > 0 ? " (tambien " + cap + ")" : "") + ", cruceta = captura, " +
                   PadNames.Name(k, PadBtn.X, false) + "/" + PadNames.Name(k, PadBtn.Y, false) + "/" + PadNames.Name(k, PadBtn.B, false) + " = 1/2/3 pasadas de DLSS 5, " +
                   PadNames.Name(k, PadBtn.A, false) + " = DLSS 5 si/no, " + PadNames.Name(k, PadBtn.LB, false) + "/" + lt + "/" + rt + "/" + PadNames.Name(k, PadBtn.RB, false) +
                   " = nivel 1-4, L3 = tipo de generacion (mantenido 1 s = filtro de tramado), R3 = escalador";
        }

        // Filas para el asistente: { boton, que hace }
        public static string[][] Tabla(PadKind k)
        {
            string lt = PadNames.TriggerName(k, true), rt = PadNames.TriggerName(k, false);
            string cap = NombreCaptura(k, true);
            Func<uint, string> n = delegate(uint b) { return NombreBoton(k, b); };
            return new string[][]
            {
                new[] { NombreMenuLargo(k), En ? "30 / 60 fps mode (each press toggles)" : "Modo 30 / 60 fps (cada pulsación alterna)" },
                new[] { NombreVistaLargo(k) + (cap.Length > 0 ? (En ? " or " : " o ") + cap : ""), En ? "Frame generation on / off" : "Generación de fotogramas sí / no" },
                new[] { "L3", En ? "Frame generation type (hold 1 s: dither filter)" : "Tipo de generación (mantenido 1 s: filtro de tramado)" },
                new[] { En ? "D-pad ← ↑ → ↓" : "Cruceta ← ↑ → ↓", En ? "Capture 720p / 1080p / 1440p / 4K" : "Captura 720p / 1080p / 1440p / 4K" },
                new[] { n(PadBtn.LB) + " / " + lt + " / " + rt + " / " + n(PadBtn.RB), En ? "DLSS 5 level 1-4" : "Nivel de DLSS 5 (1-4)" },
                new[] { n(PadBtn.X) + " / " + n(PadBtn.Y) + " / " + n(PadBtn.B), En ? "DLSS 5 with 1 / 2 / 3 passes" : "DLSS 5 con 1 / 2 / 3 pasadas" },
                new[] { n(PadBtn.A), En ? "DLSS 5 on / off (if you have the DLL)" : "DLSS 5 sí / no (si tienes la DLL)" },
                new[] { "R3", En ? "Main / alternative scaler" : "Escalador normal / alternativo" },
            };
        }

        // Un boton con el idioma de la pantalla (PadNames.Name esta en espanol)
        public static string NombreBoton(PadKind k, uint b)
        {
            if (b == PadBtn.View) return NombreVista(k, true);
            if (b == PadBtn.Start) return NombreMenu(k, true);
            string s = PadNames.Name(k, b, true);
            if (!En) return s;
            switch (s)
            {
                case "Cruz": return "Cross";
                case "Círculo": return "Circle";
                case "Cuadrado": return "Square";
                case "Triángulo": return "Triangle";
                case "Guía": return "Guide";
                case "Captura": return "Capture";
                case "Compartir": return "Share";
                case "panel táctil": return "touchpad";
                case "silencio": return "mute";
                default: return s;
            }
        }

        // ---- textos en pantalla: "boton de captura" (la generacion en Prisma) -> el boton de la generacion de este mando ----
        public static string EnTexto(string s, bool ingles)
        {
            if (Prisma || string.IsNullOrEmpty(s)) return s;
            if (!ingles)
            {
                if (s.IndexOf("botón de captura", StringComparison.Ordinal) < 0) return s;
                return s.Replace("botón de captura", "botón " + NombreVista(Kind, true));
            }
            if (s.IndexOf("capture button", StringComparison.Ordinal) < 0) return s;
            return s.Replace("capture button", NombreVista(Kind, true) + " button");
        }
    }

    partial class TrayApp
    {
        // (GamepadLoop, con la imagen delante y fuera del HOME) el boton Vista / Share / Crear / -
        void CeBotonVista()
        {
            if (BotonesCe.Prisma) { CycleMode(-1); return; }   // (fusion I, Prisma HZ120: Share / - baja 120 -> 60 -> 30)
            Log.Write("Mando: " + BotonesCe.NombreVista(PadHub.Shared.MainKind, false) + " = generacion de fotogramas " + (fgWanted ? "no" : "si"));
            PadCaptureButton(false);   // (lo mismo que Captura / panel tactil: la generacion si / no, con su espera de 0,6 s)
        }

        // El boton Options / Menu / + : modo 30 -> 60 -> 120 -> 30 (fusion I, modo 120 de Prisma HZ120: el 120 solo si cabe, Puede120;
        // si no, 30 <-> 60 como antes y, al pasar del 60, el aviso de por que no hay 120 la primera vez)
        void CeBotonMenu()
        {
            if (BotonesCe.Prisma) { CycleMode(+1); return; }   // (Options / + sube 30 -> 60 -> 120)
            int cur = cfg.ModoFps;
            int next = Modo40Siguiente(cur);   // (1.3.2, MODO40: 30 -> 40 -> 60 -> 120 si cabe -> 30)
            // (1.2.4 ronda 7, TEXTOS: el por que de no haber 120 va en el aviso del modo; antes eran dos avisos seguidos y el del modo pisaba al otro)
            string nota = null;
            if (cur == 60 && next == 30 && !avisado120) { avisado120 = true; nota = Aviso120NoCabe(); }
            Log.Write("Mando: " + BotonesCe.NombreMenu(PadHub.Shared.MainKind, false) + " = modo " + next + " fps");
            SetModeN(next, null, null, nota);
        }
        bool avisado120;
    }
}
