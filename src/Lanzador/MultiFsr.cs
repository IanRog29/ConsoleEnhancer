// MultiFsr.cs - 23-11-2026 (1.2.4 ronda 6, agente FGMULTI). C# 5
//
// El usuario: "no se si seria legal pero podrias intentar crear un modo smooth motion 3x y 4x, con fsr y xess tambien, y haciendo que vayan
// muy bien y sin latencia". Lo LEGAL que hay para x3 / x4 en una RTX 40 (ver notas\FGMULTI.md): la generacion de FSR 3.1 de AMD (codigo
// MIT) compilada por nosotros para inventar en varios instantes, engine\CeFsrFg.dll (src\CeFsrFg, build_fsrfg.bat; el motor:
// src\Motor\Engine\EngineFsrMulti.cs, fsrfg_multi=N). Smooth Motion x3 / x4 (parchear el driver) y XeSS FG x3 / x4 fuera de Intel (parchear
// libxess_fg, lo prohibe su licencia) no son legales; DLSS FG ya no esta.
// En el lanzador:
//  - con el tipo "FSR 3 FG" elegido, una fila "Multiplicador" (x2 / x3 / x4) en el menu de ajustes. x2 = la FSR 3 FG de siempre (la DLL
//    firmada de AMD, EngineFgFfx.cs del motor); x3 / x4 = la nuestra, solo en el modo 30 (en el 60, 3 x 59,94 no cabe en 120 Hz) y solo
//    donde se ha medido bien (MultiFsrPuede: la DLL junto al motor y, de serie, sin DLSS 5: con DLSS 5 la grafica no da para las 30 reales,
//    ver el informe; motor_fsr_multi_dlss5=1 lo deja probar igual);
//  - el motor se abre con su "cadena" propia (FsrMulChainId: la cola de presentacion se crea al abrirlo) y con fg=0 fsrfg_multi=N
//    (EngineLive, cambia en marcha entre x3 y x4);
//  - si el motor se cae con ella (grafica perdida / parado), el resto de la sesion va x2.
// Lo elegido se guarda en data\games\fsr_mult.txt. ini: motor_fsr_multi=1|0 (0 = sin x3 / x4), motor_fsr_multi_dlss5=0|1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public const int FsrMulChainId = 60;
        public static bool FsrMulChain(int chain) { return chain == FsrMulChainId; }
        public static volatile bool MotorFsrMulti = true;        // motor_fsr_multi=1|0
        // (1.2.4 ronda 6, INTEGRA6: de serie 1. Medido en la tele -4K, modo 30, DLSS 5 1 pasada, VRR, capturas reales-: x4 las 30 reales, 118,2/s,
        // el 99 % a un refresco, la 1a imagen nueva a 33,5 ms y la real a 58,7 -Smooth Motion x2 con DLSS 5: 33,1 / 49,8-; x3 30 reales, 88,7/s)
        public static volatile bool MotorFsrMultiDlss5 = true;   // motor_fsr_multi_dlss5=1|0
        static int fsrMult = -1;
        static readonly object fsrMultSync = new object();
        static string FsrMultFile { get { return Path.Combine(Paths.Datos, @"games\fsr_mult.txt"); } }

        public bool CeParseMultiFsr(string key, string val)
        {
            string v = (val ?? "").Trim();
            switch (key)
            {
                case "motor_fsr_multi": MotorFsrMulti = v != "0"; return true;
                case "motor_fsr_multi_dlss5": MotorFsrMultiDlss5 = v != "0"; return true;
            }
            return false;
        }

        static void FsrMultLoad()
        {
            if (fsrMult > 0) return;
            fsrMult = 2;
            try
            {
                if (!File.Exists(FsrMultFile)) return;
                int m;
                if (int.TryParse(File.ReadAllText(FsrMultFile).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) fsrMult = Math.Max(2, Math.Min(4, m));
            }
            catch { }
        }
        // El elegido (x2 / x3 / x4)
        public static int FsrMult { get { lock (fsrMultSync) { FsrMultLoad(); return fsrMult; } } }
        public static void FsrMultSet(int mult)
        {
            lock (fsrMultSync)
            {
                FsrMultLoad();
                fsrMult = Math.Max(2, Math.Min(4, mult));
                try { Directory.CreateDirectory(Path.GetDirectoryName(FsrMultFile)); File.WriteAllText(FsrMultFile, fsrMult.ToString(CultureInfo.InvariantCulture)); }
                catch (Exception ex) { Log.Error("FSR FG: guardando el multiplicador", ex); }
            }
        }

        public static bool FsrMulCaido { get { return motorGenCaida.Contains(" fsrmul"); } }
        static bool FsrMulDll { get { return File.Exists(Path.Combine(Paths.AppDir, @"engine\CeFsrFg.dll")); } }

        // ¿Se pueden ofrecer x3 / x4 ahora? (la DLL nuestra, sin caida en esta sesion, el modo 30 y, de serie, sin DLSS 5)
        public string FsrMulNoPorQue(bool cap30)
        {
            if (!MotorFsrMulti) return Tx.T("Apagado en el ini (motor_fsr_multi=0)", "Off in the ini (motor_fsr_multi=0)");
            if (!FsrMulDll) return Tx.T("Falta engine\\CeFsrFg.dll", "engine\\CeFsrFg.dll is missing");
            // (1.2.4 ronda 7, TEXTOS: tambien si la DLL no carga -switch2-fsrmulti rota-, no solo con la grafica perdida)
            if (FsrMulCaido) return Tx.T("x3 / x4 han fallado en esta sesión", "x3 / x4 failed this session");
            if (!cap30) return Tx.T("x2 en el modo 60", "x2 in 60 fps mode");
            // (1.2.4 ronda 6, INTEGRA6: 90 / 120 imagenes/s solo tienen sentido con la pantalla a 100 Hz o mas; a 60 Hz el hilo de presentacion
            // se medio roto -9,8 reales de 29,97-, como el de Enhanced FG)
            { double hz = Pantalla.Hz(); if (hz > 0 && hz < 100) return Tx.T("x2 con la pantalla a menos de 100 Hz", "x2 with the screen below 100 Hz"); }
            if (MotorD5Simple && !MotorFsrMultiDlss5) return Tx.T("x2 con DLSS 5 (la gráfica no da para x3 / x4)", "x2 with DLSS 5 (not enough GPU for x3 / x4)");
            return "";
        }
        public bool MultiFsrPuede { get { return FsrMulNoPorQue(CapMode && !Mode120).Length == 0; } }

        // El multiplicador que va de verdad con FSR 3 FG
        public int FsrMultNow { get { return MultiFsrPuede ? FsrMult : 2; } }

        // Lo que va en EngineLive con la cadena de FSR FG x3 / x4 (fg=0 al motor: no es la cadena de FSR 3 FG de AMD)
        public string FsrMulLiveArgs(bool on)
        {
            return " fsrfg_multi=" + (on ? FsrMultNow : 0).ToString(CultureInfo.InvariantCulture);
        }

        public static string FsrMulLabel(int mult) { return "FSR 3 FG" + (mult > 2 ? " x" + mult.ToString(CultureInfo.InvariantCulture) : ""); }
    }

    partial class TrayApp
    {
        // El menu de ajustes: la fila "Multiplicador" de FSR 3 FG, detras de "Tipo de generacion" (solo se ve con FSR 3 FG elegido)
        FilaAjuste FsrMulFila()
        {
            FilaAjuste mul = new FilaAjuste { Id = "fsrmul", Nombre = Tx.T("Multiplicador", "Multiplier") };
            string m = Config.FsrMult.ToString(CultureInfo.InvariantCulture);
            mul.Poner(new List<string> { "2", "3", "4" }, new List<string> { "x2", "x3", "x4" }, m, m);
            mul.Visible = false;
            return mul;
        }

        // (AjustesDepende, tras el tipo) visible con FSR 3 FG elegido; x3 / x4 solo donde se pueden (Config.FsrMulNoPorQue)
        void FsrMulDepende(string modoSel)
        {
            FilaAjuste tipo = AjFila("tipo"), fg = AjFila("fg"), mul = AjFila("fsrmul");
            if (mul == null) return;
            bool on = tipo != null && tipo.Visible && tipo.Val == "fsr3" && modoSel != "120";
            bool fgSi = fg == null || fg.Val == "1";
            string no = cfg.FsrMulNoPorQue(modoSel == "30");
            mul.Visible = on;
            mul.Activa = on && fgSi && no.Length == 0;
            mul.Nota = !on ? "" : !fgSi ? Tx.T("Con la generación encendida", "With frame generation on") : no;
        }

        // (AjustesPonerTodo) lo elegido; entre x3 y x4 el motor lo recibe en marcha (EngineLive), x2 <-> x3 / x4 reabre el motor (otra cadena)
        void FsrMulAplicar(Dictionary<string, string> e)
        {
            string a = AjVal(e, "fsrmul");
            if (a == null) return;
            int n;
            if (!int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n == Config.FsrMult) return;
            Config.FsrMultSet(n);
            Log.Write("FSR 3 FG: multiplicador x" + Config.FsrMult.ToString(CultureInfo.InvariantCulture) + (Config.FsrMult > 2 ? " (la de varios instantes, engine\\CeFsrFg.dll)" : " (la de AMD)"));
        }

        // RunMpv, el motor se ha cerrado solo con la grafica perdida (3) o parado (5) con FSR FG x3 / x4: el resto de la sesion x2
        bool FsrMulCaida(Process p, int code)
        {
            if ((code != 3 && code != 5) || !Config.FsrMulChain(Config.EngineFfxOf(p))) return false;
            Config.MotorGenCaidaAnota("fsrmul");
            Log.Write("Motor: la grafica se ha " + (code == 3 ? "perdido" : "parado") + " con FSR 3 FG x3 / x4 (codigo " + code.ToString(CultureInfo.InvariantCulture) +
                      "): el resto de la sesion x2 (la de AMD)");
            return true;
        }

        // MpvReader: {"event":"client-message","args":["switch2-fsrmulti","rota","N"]} (EngineFsrMulti.cs: la DLL no carga o falla): x2
        bool FsrMulEvento(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-fsrmulti\"")) return false;
            Config.MotorGenCaidaAnota("fsrmul");
            Log.Write("Motor: su FSR FG x3 / x4 no va en esta grafica: el resto de la sesion x2");
            PrepareCfg();
            if (!cfg.FgOff && cfg.FgTypeNow == "fsr3")
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    // (1.2.4 ronda 7, TEXTOS: lo que va, "FSR 3 FG x2 · 30 → 60 fps · x4 no: <por que>", y con la hora del boton para el relevo)
                    try { System.Threading.Thread.Sleep(300); lastPadAction = DateTime.Now; RestartLs(Tx.T("Generación", "Frame generation"), AvisoConfig().AvisoGen(true)); }
                    catch (Exception ex) { Log.Error("FSR FG x3 / x4: a x2", ex); }
                });
            return true;
        }
    }
}
