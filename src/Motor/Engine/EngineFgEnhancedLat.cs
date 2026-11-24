using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedLat.cs - 23-11-2026 (1.3.1, octava ronda, agente EFGLAT). El modo BAJA LATENCIA de Enhanced FG (efg_modo=latencia:
    // la real se enseña al llegar y las inventadas van HACIA DELANTE desde ella con el movimiento de la pareja anterior) con su propio camino de
    // grafica: el usuario, tras jugar con la 1.3 (GTA VI, PS5, 4K 120 Hz VRR): "sobre todo tienes que reducir muchisimo los errores del modo
    // baja latencia del enhanced fg que al girar la camara tiene muchisimos y mas en 3x y 4x". Ver notas\EFGLAT.md.
    // Lo que cambia frente al de la 1.3 (que extrapolaba con la eleccion y la inventada del modo calidad con Extrap=1). Medido con el simulador
    // de EFG2 (C:\CE_wt8\_scratch_efglat\sim) y video real con giros de camara (GTA V conduciendo, Fallout 4, CS2 a 120/s = la verdad de x3 /
    // x4), mirando la imagen entera (bordes incluidos) y los mosaicos, no solo el error medio:
    //  - un MODELO GLOBAL del movimiento por cada real (EfgLatShaders.Fit, grado 2, robusto): al girar la camara casi todo va con el;
    //  - LISO (efg_lat_liso): donde lo que llega no tiene textura (una pared, el cielo, el asfalto) todos los vectores "casan" y la eleccion
    //    hacia un remiendo de vectores distintos: manchas claras / oscuras que cambian en cada inventada (lo peor que se veia en x3 / x4). Ahi se
    //    tira hacia el modelo global: la pared sale entera;
    //  - DELANTE / DETRAS (efg_lat_delante=1): entre lo que casa, lo que se aparta del global (el arma, las manos, el personaje) gana un poco
    //    (lo de delante tapa); en lo que se destapa se coge de donde hay fondo (antes se estiraba el borde oscuro del arma sobre el hueco);
    //  - BORDES (efg_lat_borde, efg_lat_estira): lo que entra por el borde no existe en la real; antes los candidatos que salian "fuera"
    //    perdian y ganaba el 0 (una franja quieta pegada al borde: lo de al lado repetido); ahora sigue el movimiento de alrededor y lo que se
    //    sale se estira en una franja de 3 veces lo que falta en vez de repetir la ultima fila;
    //  - la inventada (efg_lat_fin=1) elige entre las 4 elecciones de alrededor por su coste (a un cuarto de resolucion) y no punto a punto
    //    por lo que casa con la ANTERIOR (en lo animado nunca casa): sin el granulado de puntos sueltos.
    // Claves (motor) y su "volver atras" (todo junto: efg_lat=0 = la extrapolacion de la 1.3 tal cual):
    //  efg_lat=1, efg_lat_orden=2 (1 = afin), efg_lat_liso=0 (0.006 = donde no hay textura, hacia el vector grueso de ese sitio -efg_lat_liso_local=1- o el
    //  global -0-: paredes enteras sin manchas, pero el error medio de CS2 x4 sube un 2 % / 6 %), efg_lat_delante=1 (0 = no; 2 = "lo que llega", en prueba: peor en
    //  el error medio), efg_lat_delante_peso=0.01, efg_lat_hueco=0.005, efg_lat_anterior=0.25 (solo con delante=2), efg_lat_borde=1 (0 = no),
    //  efg_lat_estira=3 (0 o 1 = repetir el borde), efg_lat_fin=1 (0 = punto a punto como antes), efg_lat_global=0 (1 = el global tambien
    //  como candidato: sin efecto medible).
    // (1.3.3, decima ronda, EFGLAT) Dos cambios en la ELECCION hacia delante, medidos con video real mirando los fallos LOCALES (la imagen
    // llevada a la verdad con un modelo global antes de medir: lo que queda son remiendos, bordes rotos, manchas; el resto es adivinar el
    // giro, que no se arregla aqui):
    //  - efg_lat_destapado=1: lo que la actual tiene y la anterior no (recien destapado en la actual, lo animado) no tiene vector fiable;
    //    antes contaba como un vector cualquiera y ganaba un remiendo del fondo liso de al lado; ahora se supone que va con la camara;
    //  - efg_lat_revisa=2: dos vueltas de la eleccion REVISADA con los 9 vecinos (el mismo coste mas lo que se aparta de los de al lado): sin
    //    los vectores sueltos que salen en lo liso y en el borde de lo de delante (manchas que cambian en cada inventada).
    //  Volver a la 1.3.2: efg_lat_destapado=0 efg_lat_revisa=0.
    partial class Settings
    {
        public bool EfgLat = true;
        public int EfgLatOrder = 2;
        public bool EfgLatGlobal = false, EfgLatBorder = true;
        public int EfgLatZ = 1;                 // 0 = sin delante / detras, 1 = sobre el coste de antes (de serie), 2 = "lo que llega" (Flags 32, prueba)
        public float EfgLatStretch = 3f, EfgLatZLam = 0.01f, EfgLatZHole = 0.005f, EfgLatZD = 0.25f, EfgLatZP = 0f;
        public int EfgLatFin = 1;
        public bool EfgLatSmoothLocal = true;   // efg_lat_liso_local=1: lo liso tira hacia el vector grueso de L3 de ese sitio (0 = hacia el global)
        // (1.3.2, ronda 9, EFGLAT) efg_lat_ganancia: lo lejos que se extrapola cada inventada (t - 1 por este factor; 1 = lo que toca, de
        // serie). Solo para pruebas: medido con video real (simulador) 0,75 / 1,15 / 1,3 salen PEOR que 1 en Fallout 4 (14,75 / 12,99 / 15,05
        // frente a 11,75) y 1,25 apenas mejor en GTA V (11,29 frente a 11,43): el movimiento de la pareja anterior tal cual es lo mejor que se
        // puede adivinar (notas\EFGLAT.md, ronda 9)
        public float EfgLatGain = 1f;
        // (1.3.3, decima ronda, EFGLAT) efg_lat_revisa: vueltas de la eleccion revisada con sus vecinos (EfgLatShaders.Ref; 0 = no);
        // efg_lat_revisa_peso = lo que paga un vector por apartarse de los de al lado
        public int EfgLatRef = 2;
        public float EfgLatRefLam = 0.01f;
        // (1.3.3, EFGLAT r10) efg_lat_destapado: lo que la actual tiene y la anterior no (recien destapado, animado) se mueve con la camara
        public bool EfgLatRev = true;
        // (1.3.3, EFGLAT r10) efg_lat_plano: en un paneo plano (mapa / menu; lo detecta efg_plano de EFGMAPA en la textura de efg_giro) el global
        // preciso es candidato y lo demas paga 0,01 por punto de diferencia (salvo la interfaz quieta)
        // efg_lat_frena: en un paneo plano, si va mas lento que en la pareja anterior se supone que sigue frenando (necesita efg_lat_plano)
        public bool EfgLatFrena = false;   // mapa sintetico: el global tiembla menos pero las baldosas no (0,77 -> 0,68 / 0,87 px segun la variante): no de serie
        public bool EfgLatPlano = false;   // medido en el mapa sintetico: temblor -1 a -4 %, HUD algo peor: no de serie

        bool ApplyEfgLat(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "efg_lat": EfgLat = B(val); return true;
                case "efg_lat_orden": EfgLatOrder = Math.Max(1, Math.Min(2, I(val))); return true;
                case "efg_lat_global": EfgLatGlobal = B(val); return true;
                case "efg_lat_borde": EfgLatBorder = B(val); return true;
                case "efg_lat_estira": EfgLatStretch = (float)Math.Max(0, Math.Min(16, D(val))); return true;
                case "efg_lat_delante": EfgLatZ = Math.Max(0, Math.Min(2, I(val))); return true;
                case "efg_lat_anterior": EfgLatZD = (float)Math.Max(0, Math.Min(1, D(val))); return true;
                case "efg_lat_liso": EfgLatZP = (float)Math.Max(0, Math.Min(0.1, D(val))); return true;
                case "efg_lat_delante_peso": EfgLatZLam = (float)Math.Max(0, Math.Min(0.2, D(val))); return true;
                case "efg_lat_hueco": EfgLatZHole = (float)Math.Max(0, Math.Min(0.1, D(val))); return true;
                case "efg_lat_fin": EfgLatFin = Math.Max(0, Math.Min(1, I(val))); return true;
                case "efg_lat_liso_local": EfgLatSmoothLocal = B(val); return true;
                case "efg_lat_revisa": EfgLatRef = Math.Max(0, Math.Min(4, I(val))); return true;   // (1.3.3, EFGLAT r10)
                case "efg_lat_plano": EfgLatPlano = B(val); return true;
                case "efg_lat_frena": EfgLatFrena = B(val); return true;
                case "efg_lat_destapado": EfgLatRev = B(val); return true;
                case "efg_lat_revisa_peso": EfgLatRefLam = (float)Math.Max(0, Math.Min(0.1, D(val))); return true;
                case "efg_lat_ganancia": EfgLatGain = (float)Math.Max(0, Math.Min(2, D(val))); return true;   // (1.3.2, EFGLAT r9)
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        const int EfgLatTabFit = 0, EfgLatTabSel = 1, EfgLatTabFin = 2, EfgLatTabRef1 = 3, EfgLatTabRef2 = 4, EfgLatTabs = 5;
        ID3D12PipelineState psoEfgLatFit, psoEfgLatSel, psoEfgLatFin, psoEfgLatRef;
        ID3D12Resource efgLatSel2;            // (1.3.3, EFGLAT r10) la otra eleccion de la revision (efg_lat_revisa: Sel -> Sel2 -> Sel)
        ID3D12Resource efgGm;                 // el modelo global (4x2 float4): fila 0 = el de la ultima pareja, fila 1 = el de la de antes
        ID3D12DescriptorHeap efgLatHeap;
        IntPtr efgLatHeapPtr;
        D3D12_CPU_DESCRIPTOR_HANDLE efgLatCpu0;
        D3D12_GPU_DESCRIPTOR_HANDLE efgLatGpu0;
        bool efgLatReady, efgLatBroken, efgLatPlanoBound;

        static readonly string[] EfgLatNames = { "efg_lat_ajuste", "efg_lat_eleccion", "efg_lat_inventada", "efg_lat_revisa" };
        static string[] EfgLatSources() { return new string[] { EfgLatShaders.Fit, EfgLatShaders.Sel, EfgLatShaders.Fin, EfgLatShaders.Ref }; }

        // ¿el camino nuevo? (solo para las inventadas hacia delante)
        bool EfgLatOn() { return s.EfgLat && efgLatReady && !efgLatBroken; }

        // EfgPsos: los 3 sombreadores (en paralelo con los demas no hace falta: son pequeños)
        void EfgLatPsos()
        {
            if (psoEfgLatFin != null || !s.EfgLat) return;
            try
            {
                long t0 = Clock.Now;
                string[] src = EfgLatSources();
                byte[][] cs = new byte[4][];
                System.Threading.Tasks.Parallel.For(0, 4, delegate (int i) { cs[i] = CompileCs(EfgLatNames[i], src[i]); });
                psoEfgLatFit = Pso(cs[0]); psoEfgLatSel = Pso(cs[1]); psoEfgLatFin = Pso(cs[2]); psoEfgLatRef = Pso(cs[3]);
                Log.Write("Enhanced FG baja latencia: shaders (" + Clock.SinceMs(t0).ToString("0", CultureInfo.InvariantCulture) + " ms)");
            }
            catch (Exception ex) { efgLatBroken = true; Log.Write("Enhanced FG baja latencia: sin el camino nuevo (" + ex.Message + "): la extrapolacion de antes"); }
        }

        // EfgPrecompilar: los mios tambien a la cache de fondo
        int EfgLatPrecompile()
        {
            int n = 0;
            string[] src = EfgLatSources();
            for (int i = 0; i < src.Length && !Program.Quit; i++)
                if (!CsCached(EfgLatNames[i], src[i])) { CompileCs(EfgLatNames[i], src[i]); n++; }
            return n;
        }

        // EfgEnsure, con lo demas ya creado
        void EfgLatEnsure()
        {
            if (efgLatReady || efgLatBroken || !s.EfgLat) return;
            try
            {
                EfgLatPsos();
                if (efgLatBroken) return;
                efgGm = Tex(DXGI_FORMAT.R32G32B32A32_FLOAT, 4, 2, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "Enhanced FG: modelo global");
                efgLatSel2 = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, efgQw, efgQh, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "Enhanced FG: eleccion revisada");
                D3D12_DESCRIPTOR_HEAP_DESC hd = new D3D12_DESCRIPTOR_HEAP_DESC();
                hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE.CBV_SRV_UAV;
                hd.NumDescriptors = (uint)(EfgSets * EfgLatTabs * Shaders.TableSize);
                hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.SHADER_VISIBLE;
                object o;
                Guid iid = typeof(ID3D12DescriptorHeap).GUID;
                Hr.Check(dev.CreateDescriptorHeap(ref hd, ref iid, out o), "CreateDescriptorHeap (Enhanced FG baja latencia)");
                efgLatHeap = (ID3D12DescriptorHeap)o;
                efgLatHeapPtr = Com.BorrowPtr(efgLatHeap, typeof(ID3D12DescriptorHeap));
                efgLatHeap.GetCPUDescriptorHandleForHeapStart(out efgLatCpu0);
                efgLatHeap.GetGPUDescriptorHandleForHeapStart(out efgLatGpu0);
                D3D12_SHADER_RESOURCE_VIEW_DESC nsd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
                D3D12_UNORDERED_ACCESS_VIEW_DESC nud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
                for (int t = 0; t < EfgSets * EfgLatTabs; t++)
                {
                    for (int i = 0; i < U0; i++) dev.CreateShaderResourceView(null, &nsd, efgLatCpu0.Offset(t * Shaders.TableSize + i, inc));
                    for (int i = U0; i < Shaders.TableSize; i++) dev.CreateUnorderedAccessView(null, null, &nud, efgLatCpu0.Offset(t * Shaders.TableSize + i, inc));
                }
                const DXGI_FORMAT F4 = DXGI_FORMAT.R16G16B16A16_FLOAT;
                for (int c = 0; c < EfgSets; c++)
                {
                    int p = (c + EfgSets - 1) % EfgSets;
                    EfgLatSrv(c, EfgLatTabFit, 0, efgF[c, 0, 0], F4); EfgLatUav(c, EfgLatTabFit, 0, efgGm, DXGI_FORMAT.R32G32B32A32_FLOAT);
                    EfgLatSrv(c, EfgLatTabSel, 0, efgL[p, 0], DXGI_FORMAT.R16_FLOAT); EfgLatSrv(c, EfgLatTabSel, 1, efgL[c, 0], DXGI_FORMAT.R16_FLOAT);
                    EfgLatSrv(c, EfgLatTabSel, 2, efgF[c, 1, 0], F4); EfgLatSrv(c, EfgLatTabSel, 3, efgF[c, 0, 0], F4);
                    EfgLatSrv(c, EfgLatTabSel, 4, efgSt[c], DXGI_FORMAT.R16G16_FLOAT);
                    EfgLatSrv(c, EfgLatTabSel, 5, efgF[c, 1, 1], F4); EfgLatSrv(c, EfgLatTabSel, 6, efgF[c, 0, 1], F4);
                    EfgLatSrv(c, EfgLatTabSel, 7, efgF[c, 0, 3], F4);
                    EfgLatUav(c, EfgLatTabSel, 0, efgSel, F4); EfgLatUav(c, EfgLatTabSel, 1, efgGm, DXGI_FORMAT.R32G32B32A32_FLOAT);
                    EfgLatSrv(c, EfgLatTabFin, 0, efgRing[p], ColorFmt); EfgLatSrv(c, EfgLatTabFin, 1, efgRing[c], ColorFmt);
                    EfgLatSrv(c, EfgLatTabFin, 2, efgSel, F4); EfgLatSrv(c, EfgLatTabFin, 3, efgSt[c], DXGI_FORMAT.R16G16_FLOAT);
                    EfgLatSrv(c, EfgLatTabFin, 4, efgCut[c], DXGI_FORMAT.R32_FLOAT); EfgLatUav(c, EfgLatTabFin, 0, efgGen, ColorFmt);
                    // (1.3.3, EFGLAT r10) la revision: las tablas de Sel con la eleccion de entrada en t5 y la de salida en u0 (ida y vuelta)
                    for (int r = 0; r < 2; r++)
                    {
                        int tb = r == 0 ? EfgLatTabRef1 : EfgLatTabRef2;
                        EfgLatSrv(c, tb, 0, efgL[p, 0], DXGI_FORMAT.R16_FLOAT); EfgLatSrv(c, tb, 1, efgL[c, 0], DXGI_FORMAT.R16_FLOAT);
                        EfgLatSrv(c, tb, 2, efgF[c, 1, 0], F4); EfgLatSrv(c, tb, 3, efgF[c, 0, 0], F4);
                        EfgLatSrv(c, tb, 4, efgSt[c], DXGI_FORMAT.R16G16_FLOAT); EfgLatSrv(c, tb, 5, r == 0 ? efgSel : efgLatSel2, F4);
                        EfgLatSrv(c, tb, 7, efgF[c, 0, 3], F4);
                        EfgLatUav(c, tb, 0, r == 0 ? efgLatSel2 : efgSel, F4); EfgLatUav(c, tb, 1, efgGm, DXGI_FORMAT.R32G32B32A32_FLOAT);
                    }
                }
                efgLatReady = true;
                Log.Write("Enhanced FG baja latencia: camino propio (modelo global de grado " + s.EfgLatOrder + (s.EfgLatGlobal ? ", candidato global" : "") +
                          (s.EfgLatBorder ? ", bordes" : "") + ", estira " + s.EfgLatStretch.ToString("0.#", CultureInfo.InvariantCulture) +
                          ", delante " + s.EfgLatZ + " (" + s.EfgLatZLam.ToString("0.###", CultureInfo.InvariantCulture) + ", hueco " + s.EfgLatZHole.ToString("0.###", CultureInfo.InvariantCulture) +
                          ", anterior " + s.EfgLatZD.ToString("0.##", CultureInfo.InvariantCulture) + ", liso " + s.EfgLatZP.ToString("0.###", CultureInfo.InvariantCulture) + "), inventada " + s.EfgLatFin + ", destapado " + (s.EfgLatRev ? 1 : 0) + ", revisa " + s.EfgLatRef + " (" + s.EfgLatRefLam.ToString("0.###", CultureInfo.InvariantCulture) + "))");
            }
            catch (Exception ex) { efgLatBroken = true; Log.Write("Enhanced FG baja latencia: sin el camino nuevo (" + ex.Message + "): la extrapolacion de antes"); }
        }

        D3D12_CPU_DESCRIPTOR_HANDLE EfgLatCpu(int set, int tab, int slot) { return efgLatCpu0.Offset((set * EfgLatTabs + tab) * Shaders.TableSize + slot, inc); }
        D3D12_GPU_DESCRIPTOR_HANDLE EfgLatGpu(int set, int tab) { return efgLatGpu0.Offset((set * EfgLatTabs + tab) * Shaders.TableSize, inc); }
        void EfgLatSrv(int set, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC d = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(f);
            dev.CreateShaderResourceView(r, &d, EfgLatCpu(set, tab, slot));
        }
        void EfgLatUav(int set, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_UNORDERED_ACCESS_VIEW_DESC d = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(f);
            dev.CreateUnorderedAccessView(r, null, &d, EfgLatCpu(set, tab, U0 + slot));
        }

        void EfgLatRun(ID3D12PipelineState pso, int set, int tab, uint* c, uint gx, uint gy)
        {
            efgList.SetPipelineState(pso);
            efgList.SetComputeRootDescriptorTable(1, EfgLatGpu(set, tab));
            efgList.SetComputeRoot32BitConstants(0, 16, c, 0);
            efgList.Dispatch(gx, gy, 1);
        }

        void EfgLatUavBarrier(ID3D12Resource r)
        {
            D3D12_RESOURCE_BARRIER u = D3D12_RESOURCE_BARRIER.CreateUav(r);
            efgList.ResourceBarrier(1, ref u);
        }

        // EfgFlowList (con la lista abierta, tras el flujo): el modelo global de esta pareja (4 vueltas; sin flujo, "no valido"). Solo hace falta
        // con inventadas hacia delante
        void EfgLatFit(int c, bool flow)
        {
            if (!EfgLatOn() || !EfgLatWanted()) return;
            uint* k = stackalloc uint[16];
            if (!efgLatPlanoBound && efgGiroG[0] != null)
            {
                // (1.3.3, EFGLAT r10) la textura de efg_giro se crea despues que lo mio (EfgGiroEnsure va detras): su vista, la primera vez
                for (int c2 = 0; c2 < EfgSets; c2++) if (efgGiroG[c2] != null) EfgLatUav(c2, EfgLatTabFit, 1, efgGiroG[c2], DXGI_FORMAT.R32_FLOAT);
                efgLatPlanoBound = true;
            }
            efgList.SetDescriptorHeaps(1, ref efgLatHeapPtr);
            EfgLatUavBarrier(efgGm);
            int iters = flow ? 4 : 1;
            for (int it = 0; it < iters; it++)
            {
                EfgZero(k);
                k[0] = (uint)efgBx[0]; k[1] = (uint)efgBy[0]; k[2] = (uint)efgLw[0]; k[3] = (uint)efgLh[0];
                k[4] = (uint)it; k[5] = (uint)s.EfgLatOrder; k[6] = flow ? 1u : 0u; k[7] = (s.EfgLatPlano || s.EfgLatFrena) && efgGiroG[c] != null ? 1u : 0u;
                k[8] = F(1.5f); k[9] = F(0.05f); k[10] = F(8f);
                EfgLatRun(psoEfgLatFit, c, EfgLatTabFit, k, 1, 1);
                EfgLatUavBarrier(efgGm);
            }
            efgList.SetDescriptorHeaps(1, ref efgHeapPtr);
        }

        // ¿hay inventadas hacia delante? (baja latencia, o un retraso menor que el de calidad)
        bool EfgLatWanted()
        {
            int k = Math.Max(2, Math.Min(4, s.Efg));
            return EfgPhaseFor(k) < (k - 1) / (double)k - 1e-6;
        }

        // EfgGenList con t > 1 (hacia delante): la eleccion y la inventada propias (marcas 2-3)
        ulong EfgLatGenList(int c, float t, bool dup, bool hasPrev, bool cutCpu)
        {
            uint* k = stackalloc uint[16];
            int p = (c + EfgSets - 1) % EfgSets;
            if (s.EfgLatGain != 1f) t = 1f + s.EfgLatGain * (t - 1f);   // (1.3.2, EFGLAT r9: efg_lat_ganancia; 1 = como la 1.3.1)
            EfgBegin();
            efgList.SetDescriptorHeaps(1, ref efgLatHeapPtr);
            EfgStamp(2);
            EB(efgRing[p], D3D12_RESOURCE_STATES.COMMON, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            EB(efgRing[c], D3D12_RESOURCE_STATES.COMMON, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            if (!dup)
            {
                EfgZero(k);
                k[0] = (uint)efgQw; k[1] = (uint)efgQh; k[2] = (uint)efgBx[0]; k[3] = (uint)efgBy[0];
                k[4] = F(t); k[5] = F(s.EfgKappa); k[6] = F(s.EfgTau); k[7] = F(s.EfgStillN);
                k[8] = (uint)efgLw[0]; k[9] = (uint)efgLh[0];
                k[10] = (s.EfgLatGlobal ? 1u : 0u) | (s.EfgLatBorder ? 2u : 0u) | (s.EfgLatZ == 1 ? 16u : 0u) | (s.EfgLatZ == 2 ? 32u : 0u) | (s.EfgLatSmoothLocal ? 64u : 0u) | (s.EfgLatRev ? 128u : 0u) | (s.EfgLatFrena ? 256u : 0u) | (s.EfgLatPlano ? 512u : 0u);
                k[11] = F(s.EfgLatZD);
                k[12] = F(s.EfgLatZLam); k[13] = F(s.EfgLatZHole); k[14] = F(8f * efgLw[0] / 1920f); k[15] = F(s.EfgLatZP);
                EB(efgSel, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                EfgLatUavBarrier(efgGm);
                EfgLatRun(psoEfgLatSel, c, EfgLatTabSel, k, Groups(efgQw, 16), Groups(efgQh, 8));
                EB(efgSel, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                // (1.3.3, EFGLAT r10) la eleccion revisada con sus vecinos: Sel -> Sel2 -> Sel (ida y vuelta: las vueltas se redondean a par
                // para que la inventada lea siempre Sel)
                int nref = (s.EfgLatRef + 1) / 2 * 2;
                if (nref > 0)
                {
                    k[10] &= ~32u; k[11] = F(s.EfgLatRefLam);
                    for (int r = 0; r < nref; r++)
                    {
                        ID3D12Resource dst = (r & 1) == 0 ? efgLatSel2 : efgSel, src = (r & 1) == 0 ? efgSel : efgLatSel2;
                        EB(dst, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                        EfgLatRun(psoEfgLatRef, c, (r & 1) == 0 ? EfgLatTabRef1 : EfgLatTabRef2, k, Groups(efgQw, 16), Groups(efgQh, 8));
                        EB(dst, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                    }
                }
            }
            EfgZero(k);
            k[0] = (uint)outW; k[1] = (uint)outH; k[2] = (uint)efgQw; k[3] = (uint)efgQh;
            k[4] = F(hasPrev ? t : 1f); k[5] = F(s.EfgZeroPen); k[6] = F(s.EfgStillN); k[7] = F(s.EfgSigma);
            k[8] = s.EfgLatBorder ? 1u : 0u; k[9] = dup || cutCpu || !hasPrev ? 1u : 0u; k[10] = (uint)efgLw[0]; k[11] = (uint)efgLh[0];
            k[12] = F(s.EfgCutThr); k[13] = F(s.EfgTau); k[14] = F(s.EfgLatStretch); k[15] = (uint)s.EfgLatFin;
            EfgLatRun(psoEfgLatFin, c, EfgLatTabFin, k, Groups(outW, 16), Groups(outH, 8));
            EB(efgRing[p], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COMMON);
            EB(efgRing[c], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COMMON);
            EfgLatUavBarrier(efgGen);
            efgList.SetDescriptorHeaps(1, ref efgHeapPtr);
            EfgStamp(3);
            EfgStampsResolve();
            ulong v = EfgSubmit();
            SetSlotDone(c, v); SetSlotDone(p, v);
            return v;
        }
    }
}
