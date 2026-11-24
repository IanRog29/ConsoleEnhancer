using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedGiro.cs - 24-11-2026 (1.3.2, novena ronda, agente EFGGIRO). El MOVIMIENTO GLOBAL de la camara para el flujo optico de
    // Enhanced FG (EngineFgEnhanced.cs). El usuario tras jugar con la 1.3.1 (GTA VI, PS5, 4K 120 Hz VRR): "Enhanced fg calidad fallos al girar
    // camara ... y en los mapas y cosas asi falla tambien", "hay fallos en modo 60 con el enhanced pero sobre todo al usar 30 de base son una
    // locura los fallos". Ver notas\EFGGIRO.md.
    // Por cada pareja de reales, antes del flujo por bloques: la traslacion de TODA la imagen (la camara) con una busqueda entera de +-25 %
    // del ancho en el nivel mas grueso de la piramide (EfgGiroShaders.Cost, un grupo de 64 hilos por traslacion) y su refinado +-1 en el
    // nivel de debajo (EfgGiroShaders.Global, un grupo; efg_giro_fino=1 tambien en el siguiente). Sirve:
    //  - al flujo (efg_giro bit 1, DE SERIE): candidato en todos los niveles, con +-2 alrededor en el mas grueso. La busqueda por bloques
    //    del nivel mas grueso llegaba a +-8 puntos de ese nivel (con 5 niveles +-128 de L0 = +-256 de una salida de 4K = 6,7 % del ancho por
    //    imagen real): con base 30 un giro rapido mueve el doble por imagen real que con base 60 y se salia: la inventada salia como la
    //    anterior en el sitio equivocado con remiendos. Con video real (simulador, Fallout 4 girando con el arma al doble de velocidad con
    //    base 30), junto con los 6 niveles de serie (efg_niveles): error 22,4 -> 18,1, las peores 42-46 -> 20-27, bloques rotos 39 -> 32 %;
    //    donde el giro cabe (GTA V, Fallout 4, CS2 con base 30; base 60) igual o -1 / -2 %. En el motor, sin coste de grafica medible.
    //  - (en prueba, apagados: sin mejora clara medida) bit 2: candidato en la eleccion de cada punto; bit 4 ("liso"): donde el bloque no
    //    tiene textura la eleccion tira hacia el global; bit 8 ("suave"): donde ningun candidato casa ni por un lado, el global por los dos.
    // Claves (motor): efg_giro=1 (bits, ver arriba; 0 = como la 1.3.1), efg_giro_radio=0.25 (alcance en fraccion del ancho), efg_giro_fino=0,
    // efg_giro_liso=0.003, efg_giro_liso_ref=0.02, efg_giro_suave=0.1. Volver a la 1.3.1 entera: efg_giro=0 efg_niveles=5.
    partial class Settings
    {
        public int EfgGiro = 1;
        public float EfgGiroRadio = 0.25f, EfgGiroLiso = 0.003f, EfgGiroLisoRef = 0.02f, EfgGiroSuave = 0.1f;
        public bool EfgGiroFino = false;

        bool ApplyEfgGiro(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "efg_giro": EfgGiro = Math.Max(0, Math.Min(15, I(val))); return true;
                case "efg_giro_radio": EfgGiroRadio = (float)Math.Max(0.05, Math.Min(0.45, D(val))); return true;
                case "efg_giro_liso": EfgGiroLiso = (float)Math.Max(0, Math.Min(0.1, D(val))); return true;
                case "efg_giro_liso_ref": EfgGiroLisoRef = (float)Math.Max(0.001, Math.Min(0.2, D(val))); return true;
                case "efg_giro_suave": EfgGiroSuave = (float)Math.Max(0, Math.Min(1, D(val))); return true;
                case "efg_giro_fino": EfgGiroFino = B(val); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        const int EfgGiroTabCost = 0, EfgGiroTabGlobal = 1, EfgGiroTabPlanoFit = 2, EfgGiroTabPlanoCost = 3, EfgGiroTabPlanoFin = 4, EfgGiroTabs = 5;   // (1.3.3, EFGMAPA: las del paneo plano)
        ID3D12PipelineState psoEfgGiroCost, psoEfgGiroGlobal;
        ID3D12Resource efgGiroCost;
        readonly ID3D12Resource[] efgGiroG = new ID3D12Resource[EfgSets];
        ID3D12DescriptorHeap efgGiroHeap;
        IntPtr efgGiroHeapPtr;
        D3D12_CPU_DESCRIPTOR_HANDLE efgGiroCpu0;
        D3D12_GPU_DESCRIPTOR_HANDLE efgGiroGpu0;
        bool efgGiroReady, efgGiroBroken;
        int efgGiroR, efgGiroN, efgGiroLv;
        // el registro: el global de la ultima pareja (lectura de la grafica, con una lista de retraso) y cuantas se salian del alcance del flujo
        ID3D12Resource efgGiroRead;
        IntPtr efgGiroReadPtr;
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT efgGiroFoot;
        long efgGiroLogAt, efgGiroPairs, efgGiroLejos;
        double efgGiroMax;

        static readonly string[] EfgGiroNames = { "efg_giro_coste", "efg_giro_global" };
        static string[] EfgGiroSources() { return new string[] { EfgGiroShaders.Cost, EfgGiroShaders.Global }; }

        bool EfgGiroOn() { return s.EfgGiro != 0 && efgGiroReady && !efgGiroBroken; }
        // lo que va al flujo (GFlags) y a la eleccion (bits 9-11 de Opts)
        uint EfgGiroFlowFlags() { return EfgGiroOn() && (s.EfgGiro & 1) != 0 ? 1u : 0u; }
        uint EfgGiroSelOpts() { return EfgGiroOn() ? (uint)(((s.EfgGiro & 2) != 0 ? 512 : 0) | ((s.EfgGiro & 4) != 0 ? 1024 : 0) | ((s.EfgGiro & 8) != 0 ? 2048 : 0)) : 0u; }

        void EfgGiroPsos()
        {
            if (psoEfgGiroGlobal != null || s.EfgGiro == 0) return;
            try
            {
                long t0 = Clock.Now;
                string[] src = EfgGiroSources();
                byte[][] cs = new byte[2][];
                System.Threading.Tasks.Parallel.For(0, 2, delegate (int i) { cs[i] = CompileCs(EfgGiroNames[i], src[i]); });
                psoEfgGiroCost = Pso(cs[0]); psoEfgGiroGlobal = Pso(cs[1]);
                EfgPlanoPsos();   // (1.3.3, EFGMAPA: el paneo plano, EngineFgEnhancedPlano.cs)
                Log.Write("Enhanced FG giro: shaders (" + Clock.SinceMs(t0).ToString("0", CultureInfo.InvariantCulture) + " ms)");
            }
            catch (Exception ex) { efgGiroBroken = true; Log.Write("Enhanced FG giro: sin el movimiento global (" + ex.Message + ")"); }
        }

        int EfgGiroPrecompile()
        {
            int n = 0;
            string[] src = EfgGiroSources();
            for (int i = 0; i < src.Length && !Program.Quit; i++)
                if (!CsCached(EfgGiroNames[i], src[i])) { CompileCs(EfgGiroNames[i], src[i]); n++; }
            if (s.EfgPlano != 0 && !Program.Quit) n += EfgPlanoPrecompile();   // (1.3.3, EFGMAPA)
            return n;
        }

        // EfgEnsure, con lo demas ya creado (tablas de efgHeap incluidas): sus texturas, su monton y el u1 de las tablas del flujo y de la eleccion
        void EfgGiroEnsure()
        {
            if (efgGiroReady || efgGiroBroken || s.EfgGiro == 0) return;
            try
            {
                EfgGiroPsos();
                if (efgGiroBroken) return;
                efgGiroLv = efgLv - 1;
                efgGiroR = Math.Max(4, Math.Min(32, (int)Math.Ceiling(s.EfgGiroRadio * efgLw[efgGiroLv])));
                efgGiroN = 2 * efgGiroR + 1;
                efgGiroCost = Tex(DXGI_FORMAT.R32_FLOAT, efgGiroN, efgGiroN, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "Enhanced FG: giro, coste");
                for (int c = 0; c < EfgSets; c++) efgGiroG[c] = Tex(DXGI_FORMAT.R32_FLOAT, 16, 1, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "Enhanced FG: giro " + c);   // (1.3.3, EFGMAPA: 8 -> 16, [8]-[14] del paneo plano)
                D3D12_DESCRIPTOR_HEAP_DESC hd = new D3D12_DESCRIPTOR_HEAP_DESC();
                hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE.CBV_SRV_UAV;
                hd.NumDescriptors = (uint)(EfgSets * EfgGiroTabs * Shaders.TableSize);
                hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.SHADER_VISIBLE;
                object o;
                Guid iid = typeof(ID3D12DescriptorHeap).GUID;
                Hr.Check(dev.CreateDescriptorHeap(ref hd, ref iid, out o), "CreateDescriptorHeap (Enhanced FG giro)");
                efgGiroHeap = (ID3D12DescriptorHeap)o;
                efgGiroHeapPtr = Com.BorrowPtr(efgGiroHeap, typeof(ID3D12DescriptorHeap));
                efgGiroHeap.GetCPUDescriptorHandleForHeapStart(out efgGiroCpu0);
                efgGiroHeap.GetGPUDescriptorHandleForHeapStart(out efgGiroGpu0);
                D3D12_SHADER_RESOURCE_VIEW_DESC nsd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
                D3D12_UNORDERED_ACCESS_VIEW_DESC nud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
                for (int t = 0; t < EfgSets * EfgGiroTabs; t++)
                {
                    for (int i = 0; i < U0; i++) dev.CreateShaderResourceView(null, &nsd, efgGiroCpu0.Offset(t * Shaders.TableSize + i, inc));
                    for (int i = U0; i < Shaders.TableSize; i++) dev.CreateUnorderedAccessView(null, null, &nud, efgGiroCpu0.Offset(t * Shaders.TableSize + i, inc));
                }
                const DXGI_FORMAT L = DXGI_FORMAT.R16_FLOAT, R = DXGI_FORMAT.R32_FLOAT;
                int lg = efgGiroLv;
                for (int c = 0; c < EfgSets; c++)
                {
                    int p = (c + EfgSets - 1) % EfgSets;
                    EfgGiroSrv(c, EfgGiroTabCost, 0, efgL[c, lg], L); EfgGiroSrv(c, EfgGiroTabCost, 1, efgL[p, lg], L);
                    EfgGiroUav(c, EfgGiroTabCost, 0, efgGiroCost, R);
                    EfgGiroSrv(c, EfgGiroTabGlobal, 0, efgL[c, lg - 1], L); EfgGiroSrv(c, EfgGiroTabGlobal, 1, efgL[p, lg - 1], L);
                    EfgGiroSrv(c, EfgGiroTabGlobal, 2, efgL[c, lg - 2], L); EfgGiroSrv(c, EfgGiroTabGlobal, 3, efgL[p, lg - 2], L);
                    EfgGiroUav(c, EfgGiroTabGlobal, 0, efgGiroG[c], R); EfgGiroUav(c, EfgGiroTabGlobal, 1, efgGiroCost, R);
                    // el u1 (libre) de las tablas del flujo de cada nivel y de la eleccion de efgHeap: el global de esta pareja
                    for (int d = 0; d < 2; d++)
                        for (int l = 0; l < efgLv; l++) EfgUav(c, EfgTabFlow(d, l), 1, efgGiroG[c], R);
                    EfgUav(c, EfgTabSel, 1, efgGiroG[c], R);
                    EfgPlanoTables(c, p);   // (1.3.3, EFGMAPA)
                }
                {
                    D3D12_RESOURCE_DESC rd; efgGiroG[0].GetDesc(out rd);
                    D3D12_PLACED_SUBRESOURCE_FOOTPRINT fp; uint rows; ulong rowSize, total;
                    dev.GetCopyableFootprints(ref rd, 0, 1, 0, &fp, &rows, &rowSize, &total);
                    efgGiroFoot = fp;
                    efgGiroRead = MakeBuffer(D3D12_HEAP_TYPE.READBACK, (long)Math.Max(256, total), D3D12_RESOURCE_STATES.COPY_DEST, "Enhanced FG: giro (registro)");
                    Hr.Check(efgGiroRead.Map(0, null, out efgGiroReadPtr), "Map (Enhanced FG giro)");
                }
                efgGiroReady = true;
                Log.Write("Enhanced FG giro: movimiento global de la camara (efg_giro=" + s.EfgGiro + ": " + ((s.EfgGiro & 1) != 0 ? "flujo " : "") +
                          ((s.EfgGiro & 2) != 0 ? "eleccion " : "") + ((s.EfgGiro & 4) != 0 ? "liso " : "") + ((s.EfgGiro & 8) != 0 ? "suave " : "") +
                          "; busqueda +-" + efgGiroR + " del nivel " + lg + " = +-" + (efgGiroR << lg) + " de L0)");
            }
            catch (Exception ex) { efgGiroBroken = true; Log.Write("Enhanced FG giro: sin el movimiento global (" + ex.Message + ")"); }
        }

        D3D12_CPU_DESCRIPTOR_HANDLE EfgGiroCpu(int set, int tab, int slot) { return efgGiroCpu0.Offset((set * EfgGiroTabs + tab) * Shaders.TableSize + slot, inc); }
        D3D12_GPU_DESCRIPTOR_HANDLE EfgGiroGpu(int set, int tab) { return efgGiroGpu0.Offset((set * EfgGiroTabs + tab) * Shaders.TableSize, inc); }
        void EfgGiroSrv(int set, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC d = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(f);
            dev.CreateShaderResourceView(r, &d, EfgGiroCpu(set, tab, slot));
        }
        void EfgGiroUav(int set, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_UNORDERED_ACCESS_VIEW_DESC d = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(f);
            dev.CreateUnorderedAccessView(r, null, &d, EfgGiroCpu(set, tab, U0 + slot));
        }

        void EfgGiroUavBarrier(ID3D12Resource r)
        {
            D3D12_RESOURCE_BARRIER u = D3D12_RESOURCE_BARRIER.CreateUav(r);
            efgList.ResourceBarrier(1, ref u);
        }

        // EfgFlowList, con la piramide de la actual hecha y antes del flujo por bloques: el global de esta pareja (sin flujo, nada)
        void EfgGiroFit(int c, bool flow)
        {
            if (!EfgGiroOn() || !flow) return;
            uint* k = stackalloc uint[16];
            int lg = efgGiroLv;
            efgList.SetDescriptorHeaps(1, ref efgGiroHeapPtr);
            EfgGiroUavBarrier(efgGiroCost);
            EfgZero(k);
            k[0] = (uint)efgLw[lg]; k[1] = (uint)efgLh[lg]; k[2] = (uint)efgGiroR; k[3] = (uint)efgGiroN;
            k[4] = F(0.15f); k[5] = F(0.0005f); k[6] = F(0.5f); k[8] = efgLw[lg] > 64 ? 2u : 1u;   // (rejilla de paso 2 en el nivel grueso de 4K / 1440p)
            efgList.SetPipelineState(psoEfgGiroCost);
            efgList.SetComputeRootDescriptorTable(1, EfgGiroGpu(c, EfgGiroTabCost));
            efgList.SetComputeRoot32BitConstants(0, 16, k, 0);
            efgList.Dispatch((uint)efgGiroN, (uint)efgGiroN, 1);   // (un grupo de 64 por traslacion)
            EfgGiroUavBarrier(efgGiroCost);
            EfgZero(k);
            k[0] = (uint)efgGiroN; k[1] = (uint)efgGiroR; k[2] = (uint)efgLw[lg - 1]; k[3] = (uint)efgLh[lg - 1];
            bool dos = s.EfgGiroFino;   // (efg_giro_fino=1: refinado tambien en el segundo nivel de debajo; +~0,3 ms de grafica, sin diferencia en el flujo)
            k[4] = (uint)efgLw[lg - 2]; k[5] = (uint)efgLh[lg - 2]; k[6] = (uint)(1 << (dos ? lg - 2 : lg - 1)); k[7] = 1u;
            k[8] = F(0.15f); k[9] = F(s.EfgGiroLiso); k[10] = F(s.EfgGiroLisoRef); k[11] = F(s.EfgGiroSuave); k[12] = dos ? 1u : 0u;
            efgList.SetPipelineState(psoEfgGiroGlobal);
            efgList.SetComputeRootDescriptorTable(1, EfgGiroGpu(c, EfgGiroTabGlobal));
            efgList.SetComputeRoot32BitConstants(0, 16, k, 0);
            efgList.Dispatch(1, 1, 1);
            EfgGiroUavBarrier(efgGiroG[c]);
            // el registro (cada 10 s): lo de la lectura anterior y la copia de esta
            float* rb = (float*)efgGiroReadPtr;
            if (efgGiroPairs > 0 && rb[7] > 0.5f)
            {
                double m = Math.Sqrt(rb[0] * rb[0] + rb[1] * rb[1]);
                if (m > efgGiroMax) efgGiroMax = m;
                if (m > (8 << (efgLv - 1)) * 0.75) efgGiroLejos++;   // (cerca o fuera del alcance de la busqueda por bloques: +-8 del nivel mas grueso)
                EfgPlanoNote(rb);   // (1.3.3, EFGMAPA)
            }
            efgGiroPairs++;
            if (efgGiroLogAt == 0 || Clock.SinceMs(efgGiroLogAt) > 10000)
            {
                efgGiroLogAt = Clock.Now;
                Log.Write("Enhanced FG giro: global de la ultima pareja (" + rb[0].ToString("0", CultureInfo.InvariantCulture) + ", " + rb[1].ToString("0", CultureInfo.InvariantCulture) +
                          ") puntos de L0, coste " + rb[2].ToString("0.000", CultureInfo.InvariantCulture) + " (0: " + rb[3].ToString("0.000", CultureInfo.InvariantCulture) + "); mayor " +
                          efgGiroMax.ToString("0", CultureInfo.InvariantCulture) + ", " + efgGiroLejos + " de " + efgGiroPairs + " parejas lejos (> 3/4 del alcance por bloques)" + EfgPlanoLog(rb));
                efgGiroMax = 0;
            }
            EB(efgGiroG[c], D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
            D3D12_TEXTURE_COPY_LOCATION dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(efgGiroRead, efgGiroFoot);
            D3D12_TEXTURE_COPY_LOCATION src = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(efgGiroG[c], 0);
            efgList.CopyTextureRegion(ref dst, 0, 0, 0, ref src, null);
            EB(efgGiroG[c], D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            efgList.SetDescriptorHeaps(1, ref efgHeapPtr);
        }
    }
}
