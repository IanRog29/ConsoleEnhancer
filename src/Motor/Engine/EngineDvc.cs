using System;
using System.IO;
using System.Runtime.InteropServices;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 22-11-2026 (1.2.4, DLSS5LEGAL): lo OFICIAL y legal de NVIDIA para "mejorar la imagen con IA" en el motor, para cualquier RTX.
    //  1. RTX Dynamic Vibrance ("DeepDVC", Streamline sl.deepdvc + nvngx_deepdvc.dll, publicos en el SDK de Streamline 2.14.16 y
    //     repartibles con la app): filtro de IA de NVIDIA que ajusta la viveza y la saturacion de la imagen segun lo que sale, en RTX 20,
    //     30, 40 y 50 (el plugin pide Turing o mas nueva). NO es DLSS 5 (DLSS 5 cambia luz y materiales; esto, color y claridad), pero
    //     es lo unico de ese tipo que NVIDIA deja usar hoy a cualquier aplicacion en todas las RTX. Va en el mismo sitio que DLSS 5
    //     (sobre la captura, en las imagenes nuevas, antes de DLSS SR / el escalado y la generacion). Claves: vibrancia=0|1 (de serie 0; el
    //     plugin solo se carga si va a 1 al arrancar el motor; luego 'set vibrancia=0|1' la apaga y la enciende),
    //     vibrancia_intensidad=0..1 (0,25), vibrancia_saturacion=0..1 (0,10). Por el lanzador: motor_extra=vibrancia=1 ...
    //     Medido (22-11, RTX 4060 Ti, 4K): +0,4-0,75 ms de grafica por imagen real y 4 MB (DLSS 5 de 1 pasada al 65 %: ~11-13 ms y GB).
    //     Si DLSS 5 (dlss5=1) va en esta imagen, la vibrancia no (son alternativas; con las dos, la de NVIDIA de DLSS 5 manda).
    //  2. DLSS 5 oficial por Streamline (kFeatureDLSS_NR = 1004, ya en sl_core_types.h de 2.14.16, pero NVIDIA no ha publicado ni el plugin
    //     sl.dlss_nr.dll ni su cabecera): si algun dia esta junto al motor (firmado por NVIDIA: el sl.interposer de produccion no carga
    //     plugins sin su firma), se pide a Streamline y el log dice si esta GPU lo tiene. Solo eso: sin su cabecera publica no se usa.
    // Nada de esto carga DLL a mano ni se salta ninguna comprobacion: todo va por Streamline (slIsFeatureSupported / slEvaluateFeature).

    // sl_deepdvc.h (Streamline 2.14.16)
    [StructLayout(LayoutKind.Sequential)]
    public struct SlDeepDVCOptions
    {
        // {23288AAD-7E7E-BE2A-916F-27DA30A3046B}
        public static readonly Guid TypeGuid = SlGuid.Make(0x23288aad, 0x7e7e, 0xbe2a, 0x91, 0x67, 0x27, 0xda, 0x30, 0xa3, 0x04, 0x6b);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public uint mode;                           // DeepDVCMode: 0 eOff, 1 eOn
        public float intensity;                     // 0..1 (0,5)
        public float saturationBoost;               // 0..1 (0,25)

        public static SlDeepDVCOptions Create()
        {
            SlDeepDVCOptions o = new SlDeepDVCOptions();
            o.Base = new SlBaseStructure(TypeGuid, Version);
            o.intensity = 0.5f;
            o.saturationBoost = 0.25f;
            return o;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SlDeepDVCState
    {
        // {934FD3D3-B34C-70A7-A139-F19FE04D91D3}
        public static readonly Guid TypeGuid = SlGuid.Make(0x934fd3d3, 0xb34c, 0x70a7, 0xa1, 0x39, 0xf1, 0x9f, 0xe0, 0x4d, 0x91, 0xd3);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public ulong estimatedVRAMUsageInBytes;

        public static SlDeepDVCState Create()
        {
            SlDeepDVCState s = new SlDeepDVCState();
            s.Base = new SlBaseStructure(TypeGuid, Version);
            return s;
        }
    }

    partial class Settings
    {
        public bool Dvc = false;                    // vibrancia=1: RTX Dynamic Vibrance (oficial de NVIDIA) sobre la captura
        // (de serie mas suave que los 0,5 / 0,25 de NVIDIA: medido el 22-11 con capturas de la PS5 a 4K, con los de NVIDIA las sombras
        // se aclaran mucho -GTA VI: luma media +35 de 255, la camiseta oscura sale gris; Rocket League +25-; con 0,25 / 0,10, la mitad)
        public float DvcIntensity = 0.25f;          // vibrancia_intensidad
        public float DvcSaturation = 0.10f;         // vibrancia_saturacion

        static float F01(string v) { return (float)Math.Max(0, Math.Min(1, double.Parse(v.Trim().Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture))); }

        // (desde el default de Apply) false = no es de aqui
        bool ApplyDvc(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "vibrancia": Dvc = B(val); return true;
                case "vibrancia_intensidad": DvcIntensity = F01(val); return true;
                case "vibrancia_saturacion": DvcSaturation = F01(val); return true;
            }
            return false;   // (1.2.4 ronda 6, QUITAFG: fuera la generacion propia, EngineFgPropia.cs; sus claves en EngineFgXe.cs ApplyFgXe)
        }
    }

    unsafe partial class Engine
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate SlResult PFun_slDeepDVCSetOptions(ref SlViewportHandle viewport, ref SlDeepDVCOptions options);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate SlResult PFun_slDeepDVCGetState(ref SlViewportHandle viewport, ref SlDeepDVCState state);

        // Su propio viewport (las etiquetas de DLSS SR y de la generacion van en vp: asi no se pisan)
        static readonly SlViewportHandle vpDvc = SlViewportHandle.Create(7);
        bool dvcPlugin;                    // sl.deepdvc.dll y nvngx_deepdvc.dll junto al motor: se pide a Streamline
        bool dvcOk, dvcBroken, dvcEverOn;  // Streamline la tiene en esta GPU / ha fallado en esta sesion / alguna vez encendida
        bool nrOfficialAsked;              // sl.dlss_nr.dll (DLSS 5 oficial) estaba y se pidio a Streamline
        string dvcApplied = "";            // las opciones dadas (para no repetirlas cada imagen)
        int dvcFailsRow;
        PFun_slDeepDVCSetOptions dvcSetOptions;
        SlTagSet dvcTags;

        // En InitStreamline, antes de slInit: lo oficial que este junto al motor, a la lista de Streamline
        void DvcFeatures(SlInitSettings si)
        {
            try
            {
                string dir = Program.Dir;
                // (solo si se pide al arrancar: sin vibrancia=1 el motor carga lo mismo que antes. El plugin de produccion no engancha
                // nada de la cadena de presentacion: deepdvc.json sin "hooks"; el de los enganches es solo de pruebas de NVIDIA)
                dvcPlugin = s.Dvc && File.Exists(Path.Combine(dir, "sl.deepdvc.dll")) && File.Exists(Path.Combine(dir, "nvngx_deepdvc.dll"));
                nrOfficialAsked = File.Exists(Path.Combine(dir, "sl.dlss_nr.dll"));
                if (!dvcPlugin && !nrOfficialAsked) return;
                SlFeature[] f = new SlFeature[si.Features.Length + (dvcPlugin ? 1 : 0) + (nrOfficialAsked ? 1 : 0)];
                Array.Copy(si.Features, f, si.Features.Length);
                int n = si.Features.Length;
                if (dvcPlugin) f[n++] = SlFeature.DeepDVC;
                if (nrOfficialAsked) f[n++] = SlFeature.DLSS_NR;
                si.Features = f;
            }
            catch (Exception ex) { Log.Write("Vibrancia RTX / DLSS 5 oficial: no se pudo mirar junto al motor (" + ex.Message + ")"); }
        }

        // En InitDevice, tras slSetD3DDevice
        void DvcCheck(long luid)
        {
            bool l;
            if (dvcPlugin)
            {
                SlResult sup = Sl.IsFeatureSupported(SlFeature.DeepDVC, luid);
                dvcOk = sup == SlResult.eOk && Sl.IsFeatureLoaded(SlFeature.DeepDVC, out l) == SlResult.eOk && l;
                Log.Write("Vibrancia RTX (RTX Dynamic Vibrance, Streamline sl.deepdvc, oficial de NVIDIA): " +
                          (dvcOk ? "disponible en esta grafica" : "no disponible (" + sup + ")") + (s.Dvc ? "; pedida (vibrancia=1)" : ""));
            }
            else if (s.Dvc) Log.Write("Vibrancia RTX pedida pero no estan sl.deepdvc.dll y nvngx_deepdvc.dll junto al motor: sin ella");
            // (con vibrancia=1 dada luego por la tuberia, sin el plugin cargado al arrancar: no se puede; el lanzador reabre el motor)
            if (nrOfficialAsked)
            {
                SlResult sup = Sl.IsFeatureSupported(SlFeature.DLSS_NR, luid);
                bool ok = sup == SlResult.eOk && Sl.IsFeatureLoaded(SlFeature.DLSS_NR, out l) == SlResult.eOk && l;
                Log.Write("DLSS 5 oficial (Streamline sl.dlss_nr): " + (ok ? "Streamline lo tiene en esta grafica (el motor aun no lo usa: falta la cabecera publica de NVIDIA)"
                                                                            : "no disponible aqui (" + sup + ")"));
                // (lo que se puede preparar sin su cabecera: que etiquetas pide, que driver y que version; para escribir el camino cuando NVIDIA
                // publique sl_dlss_nr.h. OJO: el plugin tiene que ser de la MISMA version de Streamline que el resto de sl.*.dll del motor)
                try
                {
                    SlFeatureRequirements req;
                    if (Sl.GetFeatureRequirements(SlFeature.DLSS_NR, out req) == SlResult.eOk)
                    {
                        SlBufferType[] tags = req.GetRequiredTags();
                        string[] names = new string[tags.Length];
                        for (int i = 0; i < tags.Length; i++) names[i] = tags[i].ToString();
                        Log.Write("DLSS 5 oficial: pide " + req.flags + ", etiquetas [" + string.Join(", ", names) + "], driver " + req.driverVersionDetected +
                                  " (pide " + req.driverVersionRequired + ")");
                    }
                    SlFeatureVersion ver;
                    if (Sl.GetFeatureVersion(SlFeature.DLSS_NR, out ver) == SlResult.eOk)
                        Log.Write("DLSS 5 oficial: plugin " + ver.versionSL + ", NGX " + ver.versionNGX);
                }
                catch (Exception ex) { Log.Write("DLSS 5 oficial: sin sus requisitos (" + ex.Message + ")"); }
            }
            else Log.Write("DLSS 5 oficial: no esta sl.dlss_nr.dll de NVIDIA junto al motor (Streamline 2.14.16 no lo trae): hueco preparado, sin DLSS 5");
        }

        // En RenderFrame tras RecordDlss5 (inColor en NON_PIXEL_SHADER_RESOURCE; solo imagenes nuevas, como DLSS 5)
        void RecordDvc(bool newFrame, IntPtr token)
        {
            if (!dvcOk || dvcBroken || !newFrame || inColor == null || token == IntPtr.Zero) return;
            bool on = s.Dvc && !(s.Nr && nrW != 0);   // (con DLSS 5 puesto en esta imagen, DLSS 5)
            if (!on && !dvcEverOn) return;
            IntPtr cl = Com.BorrowPtr(list, typeof(ID3D12GraphicsCommandList));
            string want = on ? string.Format(IC, "on {0:0.00} {1:0.00}", s.DvcIntensity, s.DvcSaturation) : "off";
            if (want != dvcApplied)
            {
                if (dvcSetOptions == null)
                {
                    IntPtr p;
                    SlResult rf = SlApi.slGetFeatureFunction(SlFeature.DeepDVC, "slDeepDVCSetOptions", out p);
                    if (rf != SlResult.eOk || p == IntPtr.Zero) { DvcFail("slGetFeatureFunction(slDeepDVCSetOptions): " + rf); return; }
                    dvcSetOptions = (PFun_slDeepDVCSetOptions)Marshal.GetDelegateForFunctionPointer(p, typeof(PFun_slDeepDVCSetOptions));
                }
                SlDeepDVCOptions o = SlDeepDVCOptions.Create();
                o.mode = on ? 1u : 0u;
                o.intensity = s.DvcIntensity;
                o.saturationBoost = s.DvcSaturation;
                SlViewportHandle v = vpDvc;
                SlResult r = Sl.Check(dvcSetOptions(ref v, ref o), "slDeepDVCSetOptions");
                if (r != SlResult.eOk) { DvcFail("slDeepDVCSetOptions: " + r); return; }
                Log.Write("Vibrancia RTX " + (on ? string.Format(IC, "encendida (intensidad {0:0.00}, saturacion {1:0.00}) sobre {2}x{3}", s.DvcIntensity, s.DvcSaturation, inW, inH) : "apagada"));
                dvcApplied = want;
            }
            if (!on) return;
            if (dvcTags == null) dvcTags = new SlTagSet(1);
            dvcTags.Clear();
            SlExtent ext = new SlExtent(); ext.width = (uint)inW; ext.height = (uint)inH;
            dvcTags.Add(SlBufferType.ScalingOutputColor, Com.BorrowPtr(inColor, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, SlResourceLifecycle.eValidUntilEvaluate, ext);
            SlResult re = Sl.EvaluateFeature(SlFeature.DeepDVC, token, vpDvc, cl, dvcTags);
            if (re != SlResult.eOk) { if (++dvcFailsRow >= 8) DvcFail("slEvaluateFeature(DeepDVC): " + re); return; }
            dvcFailsRow = 0;
            if (!dvcEverOn)
            {
                dvcEverOn = true;
                SlDeepDVCState st = SlDeepDVCState.Create();
                try
                {
                    IntPtr p;
                    if (SlApi.slGetFeatureFunction(SlFeature.DeepDVC, "slDeepDVCGetState", out p) == SlResult.eOk && p != IntPtr.Zero)
                    {
                        PFun_slDeepDVCGetState gs = (PFun_slDeepDVCGetState)Marshal.GetDelegateForFunctionPointer(p, typeof(PFun_slDeepDVCGetState));
                        SlViewportHandle v = vpDvc;
                        if (gs(ref v, ref st) == SlResult.eOk) Log.Write("Vibrancia RTX: memoria de la grafica estimada " + (st.estimatedVRAMUsageInBytes >> 20) + " MB");
                    }
                }
                catch { }
            }
            // (el estado de la lista lo deja el motor en cada paso: los siguientes pasos ponen sus tablas y su PSO)
        }

        void DvcFail(string why)
        {
            dvcBroken = true;
            Log.Write("Vibrancia RTX no disponible: " + why + " (se sigue sin ella en esta sesion)");
        }

        // En espera / al cerrar: sus recursos fuera (Streamline los vuelve a crear con la primera imagen)
        void ReleaseDvc(string why)
        {
            if (!dvcOk || !dvcEverOn) return;
            try
            {
                SlResult r = Sl.FreeResources(SlFeature.DeepDVC, vpDvc);
                dvcEverOn = false; dvcApplied = "";
                Log.Write("Vibrancia RTX: recursos soltados (" + why + "; " + r + ")");
            }
            catch (Exception ex) { Log.Write("Vibrancia RTX: soltar (" + ex.Message + ")"); }
        }
    }
}
