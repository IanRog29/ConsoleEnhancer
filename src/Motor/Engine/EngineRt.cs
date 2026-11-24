// EngineRt.cs - 24-11-2026 (1.3.3, decima ronda, agente RTX). Enhanced RT (experimental). C# 5
//
// El usuario: "Crea un shader para la imagen que haga la funcion que hace complete rt de añadir raytracing y haz que funcione bien creando
// vectores para ello, esta opcion aparecera como experimental y se podra activar o desactivar desde el menu de ajustes del mando."
// Enhanced RT: trazado de rayos en espacio de pantalla sobre la captura (oclusion de contacto + luz rebotada de un rebote), escrito desde
// cero (los sombreadores y lo que hacen: EngineRtShaders.cs). Va en las imagenes NUEVAS, sobre inColor (la captura con bordes, tras DLSS 5 /
// la vibrancia y despues de los vectores del motor), antes del reescalado y de la generacion: DLSS SR / XeSS / FSR y Enhanced FG / Smooth
// Motion / FSR FG reciben ya la imagen con el efecto (estable en el tiempo: la generacion no ve parpadeo que inventar). Trabaja a la mitad
// de la entrada por lado (rt_escala=2: 4K -> 1920x1080, 1440p -> 1280x720) con sus propios vectores (dos niveles de busqueda por bloques
// sobre su luma reducida: no depende de que el motor calcule los suyos, que sin DLSS SR / generacion no se calculan).
// Claves (motor; el lanzador manda rt=0|1 con enhanced_rt / el menu del mando):
//   rt=0|1 (de serie 0; tambien enhanced_rt=), rt_fuerza=0..2 (1), rt_ao=0..1.5 (0,75), rt_gi=0..1.5 (0,45), rt_escala=1..4 (2),
//   rt_rayos=1..8 (4 direcciones), rt_pasos=2..16 (6), rt_radio (0,035 del alto de trabajo), rt_altura (1 = el radio por unidad de relieve),
//   rt_relieve (ganancia luma -> relieve, 2,5), rt_historia (largo maximo de la acumulacion, 12), rt_hud=0|1 (1), rt_depuracion=0..3
//   (1 oclusion, 2 luz rebotada, 3 la mascara de HUD / texto en rojo).
// El log dice cada ~10 s lo que cuesta en la grafica (sus marcas de tiempo propias).
using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    partial class Settings
    {
        public bool Rt = false;
        public float RtStrength = 1f, RtAo = 0.75f, RtGi = 0.45f, RtRadius = 0.035f, RtHeight = 1f, RtRelief = 2.5f;
        public int RtScale = 2, RtDirs = 4, RtSteps = 6, RtMaxLen = 12, RtDebug = 0;
        public bool RtHud = true;
        static float Fr(string v, float lo, float hi) { return (float)Math.Max(lo, Math.Min(hi, double.Parse(v.Trim().Replace(',', '.'), CultureInfo.InvariantCulture))); }
        bool ApplyRt(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "rt": case "enhanced_rt": Rt = B(val); return true;
                case "rt_fuerza": RtStrength = Fr(val, 0, 2); return true;
                case "rt_ao": RtAo = Fr(val, 0, 1.5f); return true;
                case "rt_gi": RtGi = Fr(val, 0, 1.5f); return true;
                case "rt_radio": RtRadius = Fr(val, 0.005f, 0.15f); return true;
                case "rt_altura": RtHeight = Fr(val, 0.1f, 4); return true;
                case "rt_relieve": RtRelief = Fr(val, 0.2f, 10); return true;
                case "rt_escala": RtScale = Math.Max(1, Math.Min(4, I(val))); return true;
                case "rt_rayos": RtDirs = Math.Max(1, Math.Min(8, I(val))); return true;
                case "rt_pasos": RtSteps = Math.Max(2, Math.Min(16, I(val))); return true;
                case "rt_historia": RtMaxLen = Math.Max(1, Math.Min(32, I(val))); return true;
                case "rt_hud": RtHud = B(val); return true;
                case "rt_depuracion": RtDebug = Math.Max(0, Math.Min(3, I(val))); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        const int RtTabs = 9, RtMarks = 10;   // por paridad: prep, down, height, mvc, mvf, trace, accum, filter, comp
        bool rtReady, rtBroken, rtValid, rtOnSaid;
        ID3D12Resource rtInRef;
        int rtW, rtH, rtCW, rtCH, rtCBW, rtCBH, rtFBW, rtFBH, rtFW, rtFH, rtS, rtCur;
        uint rtFrame;
        ID3D12Resource rtColor, rtHE, rtFMv, rtNoisy, rtFilt;
        readonly ID3D12Resource[] rtLum = new ID3D12Resource[2], rtCLum = new ID3D12Resource[2], rtCMv = new ID3D12Resource[2], rtHist = new ID3D12Resource[2], rtHud = new ID3D12Resource[2];
        ID3D12PipelineState psoRtPrep, psoRtDown, psoRtHeight, psoRtMvC, psoRtMvF, psoRtTrace, psoRtAccum, psoRtFilter, psoRtComp;
        ID3D12DescriptorHeap rtHeap;
        IntPtr rtHeapPtr;
        D3D12_CPU_DESCRIPTOR_HANDLE rtCpu0;
        D3D12_GPU_DESCRIPTOR_HANDLE rtGpu0;
        ID3D12QueryHeap rtQ;
        ID3D12Resource rtTs;
        IntPtr rtTsPtr;
        readonly bool[] rtTsPend = new bool[FramesInFlight];
        double rtMsSum, rtMsMax; int rtMsN; long rtMsAt;
        readonly double[] rtPassSum = new double[RtMarks];

        void RtPsos()
        {
            if (psoRtPrep != null) return;
            string[] nm = { "rt_prep", "rt_down", "rt_height", "rt_mvc", "rt_mvf", "rt_trace", "rt_accum", "rt_filter", "rt_comp" };
            string[] src = { RtShaders.Prep, RtShaders.Down, RtShaders.Height, RtShaders.MvCoarse, RtShaders.MvFine, RtShaders.Trace, RtShaders.Accum, RtShaders.Filter, RtShaders.Comp };
            byte[][] cs = new byte[nm.Length][];
            long t = Clock.Now;
            System.Threading.Tasks.Parallel.For(0, nm.Length, delegate (int i) { cs[i] = CompileCs(nm[i], src[i]); });
            psoRtPrep = Pso(cs[0]); psoRtDown = Pso(cs[1]); psoRtHeight = Pso(cs[2]); psoRtMvC = Pso(cs[3]); psoRtMvF = Pso(cs[4]);
            psoRtTrace = Pso(cs[5]); psoRtAccum = Pso(cs[6]); psoRtFilter = Pso(cs[7]); psoRtComp = Pso(cs[8]);
            Log.Write("Enhanced RT: sombreadores listos (" + Clock.SinceMs(t).ToString("0", CultureInfo.InvariantCulture) + " ms)");
        }

        D3D12_CPU_DESCRIPTOR_HANDLE RtCpu(int par, int tab, int slot) { return rtCpu0.Offset((par * RtTabs + tab) * Shaders.TableSize + slot, inc); }
        void RtSrv(int par, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC d = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(f);
            dev.CreateShaderResourceView(r, &d, RtCpu(par, tab, slot));
        }
        void RtUav(int par, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_UNORDERED_ACCESS_VIEW_DESC d = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(f);
            dev.CreateUnorderedAccessView(r, null, &d, RtCpu(par, tab, U0 + slot));
        }

        void RtFree()
        {
            Bury(rtColor); Bury(rtHE); Bury(rtFMv); Bury(rtNoisy); Bury(rtFilt);
            rtColor = rtHE = rtFMv = rtNoisy = rtFilt = null;
            for (int i = 0; i < 2; i++) { Bury(rtLum[i]); Bury(rtCLum[i]); Bury(rtCMv[i]); Bury(rtHist[i]); Bury(rtHud[i]); rtLum[i] = rtCLum[i] = rtCMv[i] = rtHist[i] = rtHud[i] = null; }
            rtReady = false; rtValid = false; rtInRef = null;
        }

        bool RtEnsure()
        {
            int sc = Math.Max(1, Math.Min(4, s.RtScale));
            if (rtReady && rtInRef == inColor && rtFW == inW && rtFH == inH && rtS == sc) return true;
            if (rtReady) RtFree();
            RtPsos();
            const D3D12_RESOURCE_STATES NP = D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE;
            const DXGI_FORMAT H4 = DXGI_FORMAT.R16G16B16A16_FLOAT, H2 = DXGI_FORMAT.R16G16_FLOAT, H1 = DXGI_FORMAT.R16_FLOAT;
            rtS = sc; rtFW = inW; rtFH = inH;
            rtW = Math.Max(16, (inW + sc - 1) / sc); rtH = Math.Max(16, (inH + sc - 1) / sc);
            rtCW = (rtW + 3) / 4; rtCH = (rtH + 3) / 4;
            rtCBW = (rtCW + 3) / 4; rtCBH = (rtCH + 3) / 4;
            rtFBW = (rtW + 7) / 8; rtFBH = (rtH + 7) / 8;
            rtColor = Tex(H4, rtW, rtH, true, NP, "Enhanced RT: color");
            rtHE = Tex(H2, rtW, rtH, true, NP, "Enhanced RT: relieve");
            rtFMv = Tex(H4, rtFBW, rtFBH, true, NP, "Enhanced RT: vectores finos");
            rtNoisy = Tex(H4, rtW, rtH, true, NP, "Enhanced RT: trazado");
            rtFilt = Tex(H4, rtW, rtH, true, NP, "Enhanced RT: filtrado");
            for (int i = 0; i < 2; i++)
            {
                rtLum[i] = Tex(H1, rtW, rtH, true, NP, "Enhanced RT: luma " + i);
                rtCLum[i] = Tex(H1, rtCW, rtCH, true, NP, "Enhanced RT: luma gruesa " + i);
                rtCMv[i] = Tex(H4, rtCBW, rtCBH, true, NP, "Enhanced RT: vectores gruesos " + i);
                rtHist[i] = Tex(H4, rtW, rtH, true, NP, "Enhanced RT: historia " + i);
                rtHud[i] = Tex(H2, rtW, rtH, true, NP, "Enhanced RT: HUD " + i);
            }
            object o;
            Guid iid;
            if (rtHeap == null)
            {
                D3D12_DESCRIPTOR_HEAP_DESC hd = new D3D12_DESCRIPTOR_HEAP_DESC();
                hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE.CBV_SRV_UAV;
                hd.NumDescriptors = (uint)(2 * RtTabs * Shaders.TableSize);
                hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.SHADER_VISIBLE;
                iid = typeof(ID3D12DescriptorHeap).GUID;
                Hr.Check(dev.CreateDescriptorHeap(ref hd, ref iid, out o), "CreateDescriptorHeap (Enhanced RT)");
                rtHeap = (ID3D12DescriptorHeap)o;
                rtHeapPtr = Com.BorrowPtr(rtHeap, typeof(ID3D12DescriptorHeap));
                rtHeap.GetCPUDescriptorHandleForHeapStart(out rtCpu0);
                rtHeap.GetGPUDescriptorHandleForHeapStart(out rtGpu0);
                D3D12_QUERY_HEAP_DESC qhd = new D3D12_QUERY_HEAP_DESC();
                qhd.Type = D3D12_QUERY_HEAP_TYPE.TIMESTAMP;
                qhd.Count = FramesInFlight * RtMarks;
                iid = typeof(ID3D12QueryHeap).GUID;
                Hr.Check(dev.CreateQueryHeap(ref qhd, ref iid, out o), "CreateQueryHeap (Enhanced RT)");
                rtQ = (ID3D12QueryHeap)o;
                rtTs = MakeBuffer(D3D12_HEAP_TYPE.READBACK, 8 * RtMarks * FramesInFlight, D3D12_RESOURCE_STATES.COPY_DEST, "Enhanced RT: tiempos");
                Hr.Check(rtTs.Map(0, null, out rtTsPtr), "Map Enhanced RT tiempos");
            }
            D3D12_SHADER_RESOURCE_VIEW_DESC nsd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
            D3D12_UNORDERED_ACCESS_VIEW_DESC nud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
            for (int t = 0; t < 2 * RtTabs; t++)
            {
                for (int i = 0; i < U0; i++) dev.CreateShaderResourceView(null, &nsd, rtCpu0.Offset(t * Shaders.TableSize + i, inc));
                for (int i = U0; i < Shaders.TableSize; i++) dev.CreateUnorderedAccessView(null, null, &nud, rtCpu0.Offset(t * Shaders.TableSize + i, inc));
            }
            for (int p = 0; p < 2; p++)
            {
                int q = 1 - p;
                RtSrv(p, 0, 0, inColor, ColorFmt); RtUav(p, 0, 0, rtColor, H4); RtUav(p, 0, 1, rtLum[p], H1);
                RtSrv(p, 1, 0, rtLum[p], H1); RtUav(p, 1, 0, rtCLum[p], H1);
                RtSrv(p, 2, 0, rtLum[p], H1); RtSrv(p, 2, 1, rtCLum[p], H1); RtUav(p, 2, 0, rtHE, H2);
                RtSrv(p, 3, 0, rtCLum[p], H1); RtSrv(p, 3, 1, rtCLum[q], H1); RtSrv(p, 3, 2, rtCMv[q], H4); RtUav(p, 3, 0, rtCMv[p], H4);
                RtSrv(p, 4, 0, rtLum[p], H1); RtSrv(p, 4, 1, rtLum[q], H1); RtSrv(p, 4, 2, rtCMv[p], H4); RtUav(p, 4, 0, rtFMv, H4);
                RtSrv(p, 5, 0, rtHE, H2); RtSrv(p, 5, 1, rtColor, H4); RtUav(p, 5, 0, rtNoisy, H4);
                RtSrv(p, 6, 0, rtNoisy, H4); RtSrv(p, 6, 1, rtHist[q], H4); RtSrv(p, 6, 2, rtFMv, H4); RtSrv(p, 6, 3, rtLum[p], H1);
                RtSrv(p, 6, 4, rtLum[q], H1); RtSrv(p, 6, 5, rtHud[q], H2); RtUav(p, 6, 0, rtHist[p], H4); RtUav(p, 6, 1, rtHud[p], H2);
                RtSrv(p, 7, 0, rtHist[p], H4); RtSrv(p, 7, 1, rtHE, H2); RtSrv(p, 7, 2, rtColor, H4); RtSrv(p, 7, 3, rtHud[p], H2); RtUav(p, 7, 0, rtFilt, H4);
                RtSrv(p, 8, 0, rtFilt, H4); RtSrv(p, 8, 1, rtHud[p], H2); RtSrv(p, 8, 2, rtHE, H2); RtUav(p, 8, 0, inColor, ColorFmt);
            }
            rtInRef = inColor;
            rtReady = true; rtValid = false;
            long mb = ((long)rtW * rtH * (8 * 5 + 4 * 2 + 2 * 2 + 8 * 2 + 4 * 2)) >> 20;
            Log.Write("Enhanced RT (experimental): " + inW + "x" + inH + " con trabajo a " + rtW + "x" + rtH + " (~" + mb + " MB de grafica)");
            return true;
        }

        void RtBind(ID3D12PipelineState pso, int tab, uint* c)
        {
            list.SetDescriptorHeaps(1, ref rtHeapPtr);
            list.SetComputeRootSignature(rootSig);
            list.SetPipelineState(pso);
            list.SetComputeRootDescriptorTable(1, rtGpu0.Offset((rtCur * RtTabs + tab) * Shaders.TableSize, inc));
            list.SetComputeRoot32BitConstants(0, 16, c, 0);
        }
        void RtMark(int fi, int k) { list.EndQuery(rtQ, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * RtMarks + k)); }
        void RtW(ID3D12Resource r) { Barrier(r, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS); }
        void RtR(ID3D12Resource r) { Barrier(r, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE); }

        // En RenderFrame tras RecordDvc (inColor en NON_PIXEL_SHADER_RESOURCE; solo imagenes nuevas)
        void RecordRt(bool newFrame)
        {
            if (!s.Rt || rtBroken)
            {
                if (rtReady) { RtFree(); Log.Write("Enhanced RT: apagado"); }
                rtOnSaid = false;
                return;
            }
            if (!newFrame || inColor == null || inW < 64 || inH < 64) return;
            try { RtEnsure(); }
            catch (Exception ex) { rtBroken = true; Log.Write("Enhanced RT no disponible: " + ex.Message + " (se sigue sin el en esta sesion)"); try { RtFree(); } catch { } return; }
            if (!rtOnSaid)
            {
                rtOnSaid = true;
                Log.Write(string.Format(CultureInfo.InvariantCulture, "Enhanced RT encendido: fuerza {0:0.00}, oclusion {1:0.00}, luz rebotada {2:0.00}, {3} rayos x {4} pasos, radio {5:0.000}, HUD {6}",
                    s.RtStrength, s.RtAo, s.RtGi, s.RtDirs, s.RtSteps, s.RtRadius, s.RtHud ? "protegido" : "sin proteger"));
            }
            int fi = rfFi;
            if (rtTsPend[fi])
            {
                ulong* ts = (ulong*)((byte*)rtTsPtr + fi * 8 * RtMarks);
                if (ts[RtMarks - 1] > ts[0] && tsFreq > 0)
                {
                    double ms = (ts[RtMarks - 1] - ts[0]) * 1000.0 / tsFreq;
                    if (ms < 200)
                    {
                        rtMsSum += ms; rtMsN++; if (ms > rtMsMax) rtMsMax = ms;
                        for (int k = 1; k < RtMarks; k++) rtPassSum[k] += ts[k] > ts[k - 1] ? (ts[k] - ts[k - 1]) * 1000.0 / tsFreq : 0;
                    }
                }
                rtTsPend[fi] = false;
            }
            if (rtMsN > 0 && Clock.SinceMs(rtMsAt) > 10000)
            {
                string[] pn = { "", "reducir", "gruesa", "relieve", "vec_g", "vec_f", "trazado", "acumular", "filtro", "mezcla" };
                string det = "";
                for (int k = 1; k < RtMarks; k++) { det += (k > 1 ? " " : "") + pn[k] + " " + (rtPassSum[k] / rtMsN).ToString("0.00", CultureInfo.InvariantCulture); rtPassSum[k] = 0; }
                Log.Write(string.Format(CultureInfo.InvariantCulture, "Enhanced RT: {0:0.00} ms de grafica por imagen nueva (peor {1:0.00}, {2} imagenes; {3}x{4} -> trabajo {5}x{6}; {7})",
                    rtMsSum / rtMsN, rtMsMax, rtMsN, inW, inH, rtW, rtH, det));
                rtMsSum = 0; rtMsN = 0; rtMsMax = 0; rtMsAt = Clock.Now;
            }
            list.EndQuery(rtQ, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * RtMarks));
            rtCur ^= 1;
            rtFrame++;
            uint* c = stackalloc uint[16];
            uint valid = rtValid ? 1u : 0u;
            // 1. reducir
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)rtW; c[1] = (uint)rtH; c[2] = (uint)rtS; c[4] = F(1f / inW); c[5] = F(1f / inH);
            RtW(rtColor); RtW(rtLum[rtCur]);
            RtBind(psoRtPrep, 0, c); list.Dispatch(Groups(rtW, 8), Groups(rtH, 8), 1);
            RtR(rtColor); RtR(rtLum[rtCur]); RtMark(fi, 1);
            // 2. luma gruesa
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)rtCW; c[1] = (uint)rtCH; c[4] = F(1f / rtW); c[5] = F(1f / rtH);
            RtW(rtCLum[rtCur]); RtBind(psoRtDown, 1, c); list.Dispatch(Groups(rtCW, 8), Groups(rtCH, 8), 1); RtR(rtCLum[rtCur]); RtMark(fi, 2);
            // 3. relieve
            float rad = Math.Max(3f, s.RtRadius * rtH);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)rtW; c[1] = (uint)rtH; c[4] = F(1f / rtCW); c[5] = F(1f / rtCH); c[6] = F(Math.Max(1.5f, rad * 0.25f)); c[7] = F(s.RtRelief);
            RtW(rtHE); RtBind(psoRtHeight, 2, c); list.Dispatch(Groups(rtW, 8), Groups(rtH, 8), 1); RtR(rtHE); RtMark(fi, 3);
            // 4-5. vectores propios (ahora -> anterior)
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)rtCW; c[1] = (uint)rtCH; c[2] = (uint)rtCBW; c[3] = (uint)rtCBH; c[4] = valid;
            RtW(rtCMv[rtCur]); RtBind(psoRtMvC, 3, c); list.Dispatch(Groups(rtCBW, 8), Groups(rtCBH, 8), 1); RtR(rtCMv[rtCur]); RtMark(fi, 4);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)rtW; c[1] = (uint)rtH; c[2] = (uint)rtFBW; c[3] = (uint)rtFBH; c[4] = (uint)rtCBW; c[5] = (uint)rtCBH; c[6] = valid;
            RtW(rtFMv); RtBind(psoRtMvF, 4, c); list.Dispatch(Groups(rtFBW, 8), Groups(rtFBH, 8), 1); RtR(rtFMv); RtMark(fi, 5);
            // 6. trazado
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)rtW; c[1] = (uint)rtH; c[2] = rtFrame; c[3] = (uint)Math.Max(1, s.RtDirs);
            c[4] = (uint)Math.Max(2, s.RtSteps); c[5] = F(rad); c[6] = F(rad * s.RtHeight); c[7] = F(0.08f);
            RtW(rtNoisy); RtBind(psoRtTrace, 5, c); list.Dispatch(Groups(rtW, 8), Groups(rtH, 8), 1); RtR(rtNoisy); RtMark(fi, 6);
            // 7. acumulacion
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)rtW; c[1] = (uint)rtH; c[2] = (uint)rtFBW; c[3] = (uint)rtFBH; c[4] = valid; c[5] = F(s.RtMaxLen);
            c[8] = F(1f / rtW); c[9] = F(1f / rtH);
            RtW(rtHist[rtCur]); RtW(rtHud[rtCur]);
            RtBind(psoRtAccum, 6, c); list.Dispatch(Groups(rtW, 8), Groups(rtH, 8), 1);
            RtR(rtHist[rtCur]); RtR(rtHud[rtCur]); RtMark(fi, 7);
            // 8. filtro
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)rtW; c[1] = (uint)rtH;
            RtW(rtFilt); RtBind(psoRtFilter, 7, c); list.Dispatch(Groups(rtW, 8), Groups(rtH, 8), 1); RtR(rtFilt); RtMark(fi, 8);
            // 9. a la imagen
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)s.RtDebug;
            c[4] = F(1f / rtW); c[5] = F(1f / rtH); c[6] = F(s.RtAo); c[7] = F(s.RtGi); c[8] = F(s.RtStrength); c[9] = F(s.RtHud ? 1f : 0f);
            RtW(inColor); RtBind(psoRtComp, 8, c); list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1); RtR(inColor);
            RtMark(fi, 9);
            list.ResolveQueryData(rtQ, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * RtMarks), RtMarks, rtTs, (ulong)(fi * 8 * RtMarks));
            rtTsPend[fi] = true;
            rtValid = true;
        }
    }
}
