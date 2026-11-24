// EnhancedRt.cs - 24-11-2026 (1.3.3, decima ronda, agente RTX). C# 5
//
// El usuario: "Crea un shader para la imagen que haga la funcion que hace complete rt de añadir raytracing y haz que funcione bien creando
// vectores para ello, esta opcion aparecera como experimental y se podra activar o desactivar desde el menu de ajustes del mando."
// Enhanced RT (experimental): el trazado de rayos sobre la imagen del motor propio (src\Motor\Engine\EngineRt.cs y EngineRtShaders.cs:
// oclusion de contacto y luz rebotada trazadas sobre un relieve estimado de la captura, con acumulacion temporal y vectores propios).
// En el lanzador:
//  - clave del ini enhanced_rt=0|1 (de serie 0): lo de partida;
//  - la fila "Enhanced RT (experimental)" del menu de ajustes del mando (Si / No): lo elegido alli se guarda en data\games\enhanced_rt.txt
//    y manda sobre el ini; aplicar SOLO esa fila no reabre la imagen (el motor lo enciende / apaga en marcha: 'set rt=0|1');
//  - el motor recibe rt=1 con EngineLive (nunca en el HOME a 4K nativo: menus de la consola = todo HUD).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public bool EnhancedRtIni = false;   // enhanced_rt= del ini
        static int rtPick = -2;              // -2 sin leer, -1 sin elegir en el menu, 0 / 1 lo elegido
        static readonly object rtSync = new object();
        static string RtFile { get { return Path.Combine(Paths.Datos, @"games\enhanced_rt.txt"); } }

        static int RtPick
        {
            get
            {
                lock (rtSync)
                {
                    if (rtPick == -2)
                    {
                        rtPick = -1;
                        try { if (File.Exists(RtFile)) { string v = File.ReadAllText(RtFile).Trim(); if (v == "1") rtPick = 1; else if (v == "0") rtPick = 0; } } catch { }
                    }
                    return rtPick;
                }
            }
        }
        public bool EnhancedRtOn { get { int p = RtPick; return p >= 0 ? p == 1 : EnhancedRtIni; } }
        public static void EnhancedRtSet(bool on)
        {
            lock (rtSync)
            {
                rtPick = on ? 1 : 0;
                try { Directory.CreateDirectory(Path.GetDirectoryName(RtFile)); File.WriteAllText(RtFile, on ? "1" : "0"); }
                catch (Exception ex) { Log.Error("Enhanced RT: guardando lo elegido", ex); }
            }
        }
        // (ini) false = no es de aqui
        public bool ParseEnhancedRt(string key, string val)
        {
            if (key != "enhanced_rt") return false;
            string v = (val ?? "").Trim().ToLowerInvariant();
            EnhancedRtIni = v == "1" || v == "true" || v == "si" || v == "on" || v == "yes";
            return true;
        }
        // Lo que va en EngineLive
        public string EnhancedRtLiveArgs() { return " rt=" + (EnhancedRtOn && !HomeNative ? "1" : "0"); }
    }

    partial class TrayApp
    {
        public static string RtNombre { get { return Tx.T("Enhanced RT (experimental)", "Enhanced RT (Experimental)"); } }

        FilaAjuste RtFila()
        {
            FilaAjuste f = new FilaAjuste { Id = "rt", Nombre = RtNombre };
            string v = cfg.EnhancedRtOn ? "1" : "0";
            f.Poner(SiNo(), SiNoTxt(), v, v);
            f.Nota = Tx.T("Trazado de rayos sobre la imagen", "Ray tracing on the image");
            return f;
        }

        // (AjPonerEstado) lo elegido en su fila
        void RtAplicar(Dictionary<string, string> e)
        {
            string v = AjVal(e, "rt");
            if (v == null || (v == "1") == cfg.EnhancedRtOn) return;
            Config.EnhancedRtSet(v == "1");
            Log.Write("Enhanced RT (experimental): " + (v == "1" ? "encendido" : "apagado") + " (menu de ajustes)");
        }

        // (AjustesAplicar) solo ha cambiado esta fila: sin reabrir la imagen, el motor lo cambia en marcha
        bool RtSoloEsto(Dictionary<string, string> e, HashSet<string> cambiadas)
        {
            if (cambiadas.Count != 1 || !cambiadas.Contains("rt")) return false;
            RtAplicar(e);
            ThreadPool.QueueUserWorkItem(delegate { try { SyncEngine(); } catch (Exception ex) { Log.Error("Enhanced RT: al motor", ex); } });
            return true;
        }
    }
}
