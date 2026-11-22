using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 22-11 (1.2.4, 5a ronda, DLSS5Q): DLSS 5 en el motor sin cambios de color de golpe.
    //  - dlss5_sosten=1 (de serie): al rehacer la DLSSNR (otro % de entrada, otras pasadas, otro antiparpadeo; lo hacen la escala que se
    //    adapta, el guardian y los cambios del menu) esa imagen iba SIN DLSS 5 (la creacion va en una lista y la primera evaluacion en la
    //    siguiente): un destello con el color y el contraste de la captura entre dos con DLSS 5. Ahora esa imagen lleva la correccion de
    //    DLSS 5 de la imagen anterior (salida - entrada, a su tamaño), llevada a su sitio con los vectores de esta imagen. No en un corte
    //    de escena ni si cambia la captura (ahi no vale la de antes). dlss5_sosten=0 = como antes.
    //  - dlss5_medida=<csv> (solo pruebas): por cada imagen nueva, la media de color de 16x9 casillas de inColor antes y despues de DLSS 5
    //    (en la grafica, ~0,1 ms a 4K) y lo que hizo DLSS 5 en ella (aplicado, creado, reiniciado, corte, sostenido, pasadas, %, peso del
    //    antiparpadeo). Con eso se miden los saltos de color imagen a imagen en el camino entero (tambien con la app: motor_extra=).
    //  - test_escena_s: lo que dura cada escena de test=escenas:<...> (EscenasReales.cs).
    partial class Settings
    {
        public string NrMeasure = "";   // dlss5_medida
        public bool NrHold = true;      // dlss5_sosten
        public bool NrSuave = true;     // dlss5_suave=0: todo lo de DLSS5Q como en la 1.2.4 (sosten, la escala de una vez y en los cortes, la clave nueva)
        public bool NrNow = true;       // dlss5_creacion_inmediata: la imagen en que se crea DLSS 5 ya lo lleva (ver D5qFlush)
        public float NrChroma = 1f;     // dlss5_color: el croma de lo que cambia DLSS 5 con 2-3 pasadas (el "Chroma Strength" del residuo de Magpie; 1 = tal cual)

        bool ApplyD5q(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "dlss5_medida": NrMeasure = val.Trim(); return true;
                case "dlss5_sosten": { string v = val.Trim().ToLowerInvariant(); NrHold = !(v == "0" || v == "no" || v == "false" || v == "off"); return true; }
                case "dlss5_color": { double d; if (double.TryParse(val.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) NrChroma = (float)Math.Max(0, Math.Min(2, d)); return true; }
                case "dlss5_creacion_inmediata": { string v = val.Trim().ToLowerInvariant(); NrNow = !(v == "0" || v == "no" || v == "false" || v == "off"); return true; }
                case "dlss5_suave": { string v = val.Trim().ToLowerInvariant(); NrSuave = !(v == "0" || v == "no" || v == "false" || v == "off"); return true; }
                case "test_escena_s": { double d; if (double.TryParse(val.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) EscenasReales.SceneSecs = Math.Max(1, Math.Min(120, d)); return true; }
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        const int TD5qMed = TNr + 7, TD5qHold = TNr + 8;   // (Engine.cs: TablesCount = ... + 9)
        const int D5qTx = 16, D5qTy = 9, D5qRing = 8;

        // Media de cada casilla (un grupo por casilla, 1 de cada Step puntos en cada eje), a la fila Row0 de la textura de salida
        const string D5qMeanShader = @"
cbuffer C : register(b0) { uint W, H, TX, TY; uint Row0, Step, P2, P3; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> A : register(t0);
RWTexture2D<float4> O : register(u0);
groupshared float3 acc[256];
[numthreads(16, 16, 1)]
void main(uint3 g : SV_GroupID, uint3 t : SV_GroupThreadID, uint gi : SV_GroupIndex)
{
    uint x0 = g.x * W / TX, x1 = (g.x + 1) * W / TX, y0 = g.y * H / TY, y1 = (g.y + 1) * H / TY;
    float3 s = 0;
    for (uint y = y0 + t.y * Step; y < y1; y += 16 * Step)
        for (uint x = x0 + t.x * Step; x < x1; x += 16 * Step)
            s += A[uint2(x, y)].rgb;
    acc[gi] = s;
    GroupMemoryBarrierWithGroupSync();
    for (uint k = 128; k > 0; k >>= 1) { if (gi < k) acc[gi] += acc[gi + k]; GroupMemoryBarrierWithGroupSync(); }
    if (gi == 0)
    {
        float n = (float)(((x1 - x0) + Step - 1) / Step) * (float)(((y1 - y0) + Step - 1) / Step);
        O[uint2(g.x, g.y + Row0)] = float4(acc[0] / max(n, 1.0), 1);
    }
}";
        // dlss5_sosten: la correccion de la imagen anterior (B - Cin, a su tamaño) sobre inColor, desde donde estaba cada punto (vectores)
        const string D5qHoldShader = @"
cbuffer C : register(b0) { uint W, H, P0, P1; float K, Q0, Q1, Q2; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> B : register(t1);
Texture2D<float4> Cin : register(t2);
Texture2D<float2> Mv : register(t3);
RWTexture2D<unorm float4> Io : register(u0);
SamplerState Lin : register(s0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    float2 mv = Mv[id.xy];
    if (!all(isfinite(mv)) || any(abs(mv) > 4096)) mv = 0;
    float2 uv = (id.xy + 0.5 + mv) / float2(W, H);
    float3 a = Io[id.xy].rgb;
    float3 d = B.SampleLevel(Lin, uv, 0).rgb - Cin.SampleLevel(Lin, uv, 0).rgb;
    if (Q0 > 0 && Q0 != 1) { float dy = dot(d, float3(0.2126, 0.7152, 0.0722)); d = dy + (d - dy) * Q0; }
    Io[id.xy] = float4(saturate(a + d * K), 1);
}";
        ID3D12PipelineState psoD5qMean, psoD5qHold;
        // 22-11 (DLSS5Q) dlss5_color: con 2-3 pasadas, el croma de la correccion por esto (como el "Chroma Strength" del residuo que Magpie
        // lleva desde el 19-11 con 2-3 pasadas: dlss5_pasadas_color 0,7 / 0,5 a 4K; sin el, color lavado). Con 1 pasada, tal cual
        float NrChromaNow { get { return nrPasses >= 2 && s.NrChroma > 0 ? s.NrChroma : 1f; } }
        bool d5qHoldBroken;

        int rfFi;   // la imagen (lista y su asignador) que se esta grabando (RenderFrame)

        // ---- dlss5_creacion_inmediata (de serie 1): la creacion de la DLSSNR se envia ya a la grafica y se espera, y la misma imagen se
        // evalua (con la historia de cero). Antes la imagen de la creacion iba SIN DLSS 5 (PrismaNr: "hay que dejar una imagen antes del
        // primer Evaluate"): al rehacerla (otro %, otras pasadas) un destello con el color de la captura, y en un motor recien abierto (el
        // relevo de un motor a otro al cambiar de generacion, el despertar) la primera imagen que se ve, sin DLSS 5, y la siguiente con el.
        // El paron es el de siempre (crear la DLSSNR ya paraba la imagen ~0,15-0,3 s): solo se añade lo que tarda la grafica en hacer la
        // creacion. Si algo falla, la imagen va como antes (sostenida o sin DLSS 5)
        bool d5qFlushBroken;
        bool D5qFlush()
        {
            if (!s.NrNow || !s.NrSuave || d5qFlushBroken) return false;
            try
            {
                long t = Clock.Now;
                Hr.Check(list.Close(), "Close (DLSS 5)");
                IntPtr lp = Com.BorrowPtr(list, typeof(ID3D12CommandList));
                CopyWait();
                queue.ExecuteCommandLists(1, ref lp);
                WaitIdle();
                Hr.Check(list.Reset(alloc[rfFi], null), "List.Reset (DLSS 5)");
                d5qFlushMs = Clock.SinceMs(t);
                return true;
            }
            catch (Exception ex) { d5qFlushBroken = true; Log.Error("DLSS 5: creacion inmediata", ex); throw; }
        }
        double d5qFlushMs;

        // Lo que hizo DLSS 5 en esta imagen (lo pone RecordDlss5; lo lee la medida)
        bool d5qApplied, d5qCreated, d5qReset, d5qHeld;
        float d5qWeight = -1;

        // ---- dlss5_sosten: en la imagen en que se rehace la DLSSNR, la correccion de la anterior (llamado con la grafica ya parada)
        bool D5qHoldRecord()
        {
            if (!s.NrHold || !s.NrSuave || d5qHoldBroken || nrW == 0 || nrFrames == 0 || frameReset) return false;
            if (nrSrcW != inW || nrSrcH != inH || !ReferenceEquals(nrInColor, inColor) || mvec == null) return false;
            ID3D12Resource b = nrAf > 0 ? afOut : nrOuts[Math.Max(0, nrPasses - 1)];
            ID3D12Resource cin = nrIn != null ? nrIn : (nrAf > 0 ? afGuide : null);
            if (b == null || cin == null) return false;   // (al 100 % sin antiparpadeo no queda la entrada de antes)
            try
            {
                if (psoD5qHold == null) psoD5qHold = Pso(CompileCs("d5qsosten", D5qHoldShader));
            }
            catch (Exception ex) { d5qHoldBroken = true; Log.Write("DLSS 5: sosten no disponible (" + ex.Message + ")"); return false; }
            // b esta en UNORDERED_ACCESS (asi la deja RecordDlss5); cin en NON_PIXEL_SHADER_RESOURCE
            SrvTex(TD5qHold, 1, b, ColorFmt); SrvTex(TD5qHold, 2, cin, ColorFmt); SrvTex(TD5qHold, 3, mvec, DXGI_FORMAT.R16G16_FLOAT);
            UavTex(TD5qHold, U0, inColor, ColorFmt);
            uint* c = stackalloc uint[8];
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = 0; c[3] = 0; c[4] = F(s.NrStrength); c[5] = F(NrChromaNow); c[6] = 0; c[7] = 0;
            Barrier(b, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(inColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            BindCompute(psoD5qHold, TD5qHold, c, 8);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(inColor, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(b, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            d5qHeld = true;
            return true;
        }

        // ---- dlss5_medida
        ID3D12Resource d5qTex, d5qIn;
        readonly ID3D12Resource[] d5qRb = new ID3D12Resource[D5qRing];
        readonly IntPtr[] d5qRbPtr = new IntPtr[D5qRing];
        readonly ulong[] d5qRbFence = new ulong[D5qRing];
        readonly bool[] d5qUsed = new bool[D5qRing], d5qSent = new bool[D5qRing];
        readonly string[] d5qMeta = new string[D5qRing];
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT d5qFp;
        StreamWriter d5qOut;
        long d5qT0;
        int d5qLines;
        bool d5qBroken;

        // phase 0: antes de DLSS 5 (y se apunta la imagen), 1: despues (y se copia para leerla)
        void D5qMeasure(bool newFrame, int phase)
        {
            if (s.NrMeasure.Length == 0 || d5qBroken || !newFrame || inColor == null || inW <= 0) return;
            try
            {
                uint* c = stackalloc uint[8];
                if (phase == 0)
                {
                    D5qCollect(false);
                    d5qApplied = d5qCreated = d5qReset = d5qHeld = false; d5qWeight = -1;
                    if (d5qTex == null)
                    {
                        psoD5qMean = Pso(CompileCs("d5qmedia", D5qMeanShader));
                        d5qTex = Tex(DXGI_FORMAT.R32G32B32A32_FLOAT, D5qTx, D5qTy * 2, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "DLSS 5 (medida)");
                        d5qFp = Foot(0, DXGI_FORMAT.R32G32B32A32_FLOAT, D5qTx, D5qTy * 2, 16);
                        long total = (long)d5qFp.Footprint.RowPitch * D5qTy * 2;
                        for (int i = 0; i < D5qRing; i++)
                        {
                            d5qRb[i] = MakeBuffer(D3D12_HEAP_TYPE.READBACK, total, D3D12_RESOURCE_STATES.COPY_DEST, "DLSS 5 (medida) " + i);
                            Hr.Check(d5qRb[i].Map(0, null, out d5qRbPtr[i]), "Map medida");
                        }
                        string path = Path.IsPathRooted(s.NrMeasure) ? s.NrMeasure : Program.DataPath(s.NrMeasure);
                        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); } catch { }
                        d5qOut = new StreamWriter(path, false, new UTF8Encoding(false));
                        StringBuilder hd = new StringBuilder("seq,t_ms,aplicado,creado,reinicio,corte,sosten,pasadas,pct,peso_af,preR,preG,preB,postR,postG,postB");
                        for (int i = 0; i < D5qTx * D5qTy; i++) hd.Append(",a" + i + "R,a" + i + "G,a" + i + "B,d" + i + "R,d" + i + "G,d" + i + "B");
                        d5qOut.WriteLine(hd.ToString());
                        d5qT0 = Clock.Now;
                        Log.Write("DLSS 5: medida de color por casillas (" + D5qTx + "x" + D5qTy + ") a " + path);
                    }
                    if (!ReferenceEquals(d5qIn, inColor))
                    {
                        WaitIdle();
                        SrvTex(TD5qMed, 0, inColor, ColorFmt); UavTex(TD5qMed, U0, d5qTex, DXGI_FORMAT.R32G32B32A32_FLOAT);
                        d5qIn = inColor;
                    }
                }
                c[0] = (uint)inW; c[1] = (uint)inH; c[2] = D5qTx; c[3] = D5qTy; c[4] = (uint)(phase * D5qTy); c[5] = 2; c[6] = 0; c[7] = 0;
                BindCompute(psoD5qMean, TD5qMed, c, 8);
                list.Dispatch(D5qTx, D5qTy, 1);
                UavBarrier(d5qTex);
                if (phase == 0) return;
                int e = -1;
                for (int i = 0; i < D5qRing && e < 0; i++) if (!d5qUsed[i]) e = i;
                if (e < 0) { D5qCollect(true); for (int i = 0; i < D5qRing && e < 0; i++) if (!d5qUsed[i]) e = i; }
                if (e < 0) return;
                Barrier(d5qTex, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
                D3D12_TEXTURE_COPY_LOCATION dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(d5qRb[e], d5qFp);
                D3D12_TEXTURE_COPY_LOCATION src = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(d5qTex, 0);
                list.CopyTextureRegion(ref dst, 0, 0, 0, ref src, null);
                Barrier(d5qTex, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                int pct = nrW <= 0 ? 0 : nrW >= inW ? 100 : (int)Math.Round(100.0 * nrW / Math.Max(1, inW));
                d5qMeta[e] = string.Format(CultureInfo.InvariantCulture, "{0},{1:0.0},{2},{3},{4},{5},{6},{7},{8},{9:0.000}", curSeq, Clock.SinceMs(d5qT0),
                    d5qApplied ? 1 : 0, d5qCreated ? 1 : 0, d5qReset ? 1 : 0, frameReset ? 1 : 0, d5qHeld ? 1 : 0, nrW > 0 ? nrPasses : 0, pct, d5qWeight);
                d5qUsed[e] = true; d5qSent[e] = false; d5qRbFence[e] = ulong.MaxValue;
            }
            catch (Exception ex) { d5qBroken = true; Log.Error("dlss5_medida", ex); }
        }

        // Run, justo despues de enviar la lista de la imagen: la valla que la cierra
        void D5qSubmitted(ulong v)
        {
            if (d5qTex == null) return;
            for (int i = 0; i < D5qRing; i++) if (d5qUsed[i] && !d5qSent[i]) { d5qSent[i] = true; d5qRbFence[i] = v; }
        }

        void D5qCollect(bool all)
        {
            if (d5qTex == null || d5qOut == null) return;
            ulong done = fence.GetCompletedValue();
            // por orden de envio (la valla)
            while (true)
            {
                int e = -1; ulong best = ulong.MaxValue;
                for (int i = 0; i < D5qRing; i++) if (d5qUsed[i] && d5qSent[i] && d5qRbFence[i] < best) { best = d5qRbFence[i]; e = i; }
                if (e < 0) break;
                if (done < best) { if (!all) break; WaitFence(best); done = fence.GetCompletedValue(); }
                float* p = (float*)d5qRbPtr[e];
                int rp = (int)d5qFp.Footprint.RowPitch / 4;
                double[] g = new double[6];
                StringBuilder sb = new StringBuilder(d5qMeta[e], 6000);
                StringBuilder tiles = new StringBuilder(5000);
                for (int ty = 0; ty < D5qTy; ty++)
                    for (int tx = 0; tx < D5qTx; tx++)
                    {
                        float* a = p + ty * rp + tx * 4, d = p + (ty + D5qTy) * rp + tx * 4;
                        for (int k = 0; k < 3; k++) { g[k] += a[k]; g[3 + k] += d[k]; }
                        tiles.AppendFormat(CultureInfo.InvariantCulture, ",{0:0.00},{1:0.00},{2:0.00},{3:0.00},{4:0.00},{5:0.00}", a[0] * 255, a[1] * 255, a[2] * 255, d[0] * 255, d[1] * 255, d[2] * 255);
                    }
                for (int k = 0; k < 6; k++) sb.AppendFormat(CultureInfo.InvariantCulture, ",{0:0.000}", g[k] * 255 / (D5qTx * D5qTy));
                sb.Append(tiles.ToString());
                d5qOut.WriteLine(sb.ToString());
                d5qUsed[e] = false; d5qSent[e] = false;
                if (++d5qLines % 30 == 0) d5qOut.Flush();
            }
        }

        void D5qClose()
        {
            try { if (d5qOut != null) { D5qCollect(true); d5qOut.Flush(); d5qOut.Dispose(); d5qOut = null; } } catch { }
        }
    }
}
