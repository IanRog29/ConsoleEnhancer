using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 17-11 (REJAS): pruebas de la reja de Ocarina (rastro y temblor de los barrotes con DLSS 4.5 al mover la camara).
    //   volcado=<carpeta>: la salida final (recorte de volcado_rect a 4K), los vectores y la entrada (el mismo recorte a la resolucion
    //     de entrada) de volcado_n imagenes nuevas seguidas desde la volcado_desde, a ficheros crudos o_/m_/i_<seq>.raw + info.txt,
    //     para medirlos contra la verdad del paralaje (tests\rejas_mide). Sin tocar la pantalla: vale con la tele apagada
    //   mvverdad=suma de 1 (vectores de la verdad), 2 (profundidad por capas: prof_cerca la reja, prof_lejos el cielo), 4 (la reja
    //     ensanchada lo que se separa del cielo + 1 px, como hace un juego con su profundidad); con test=paralaje. La capa se
    //     decide por la luma (la reja es oscura). Para separar "vectores malos" de "lo que hace DLSS con vectores buenos"
    unsafe partial class Engine
    {
        // Tablas propias detras de TDown (Engine.cs: TablesCount)
        const int TRBase = TQBase + TQCount + 1, TRTruth = TRBase /* + juego de luma */, TRCons = TRBase + 2 /* + juego */, TRCapas = TRBase + 4 /* + juego */, TRApert = TRBase + 6, TREdge = TRBase + 7 /* + juego */, TREns = TRBase + 9 /* + juego */, TRFg = TRBase + 11, TRFgD = TRBase + 12, TRCount = 31;   // (21-11 FG21: + TRFillA/B/C, EngineFgRelleno.cs) (21-11 1.2.1: + TRGlob*, EngineFgGlobal.cs)
        ID3D12PipelineState psoMvTruth, psoMvCons, psoMvCapas, psoMvApert, psoEdge, psoEns, psoFgSmooth, psoFgGlobal, psoFgDepth;
        ID3D12Resource edgeMap, mvecW;   // 17-11 13:20 (mvensancha): fuerza del borde por punto (R8) y los vectores ensanchados
        ID3D12Resource mvCons;      // el campo de bloques del nivel fino tras el consenso (se copia encima de mvLevel[0])
        ID3D12Resource mvRel;       // por bloque: fiabilidad de x e y (mvapertura)
        long curSeq = -1, prevMotionSeq = -1;

        void EnsureRejasPsos()
        {
            if (psoMvTruth != null) return;
            long t = Clock.Now;
            psoMvTruth = Pso(CompileCs("mvverdad", RShaders.MvTruth));
            psoMvCons = Pso(CompileCs("mvconsenso", RShaders.MvCons));
            psoMvCapas = Pso(CompileCs("mvcapas", RShaders.MvCapas));
            psoMvApert = Pso(CompileCs("mvapertura", RShaders.MvApert));
            psoEdge = Pso(CompileCs("mvborde", RShaders.Edge));
            psoEns = Pso(CompileCs("mvensancha", RShaders.Ens));
            psoFgSmooth = Pso(CompileCs("fgsuave", RShaders.FgSmooth));
            psoFgGlobal = Pso(CompileCs("fgglobal", RShaders.FgGlobal));
            psoFgDepth = Pso(CompileCs("fgprof", RShaders.FgDepth));   // 17-11 15:20 (fg_profundidad)
            Log.Write("Rejas: shaders (" + Clock.SinceMs(t).ToString("0") + " ms)");
        }

        void RejasViews()
        {
            if (mvCons != null) Com.Release(ref mvCons);
            if (mvRel != null) Com.Release(ref mvRel);
            mvCons = Tex(DXGI_FORMAT.R16G16_FLOAT, bx[0], by[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "bloques consenso");
            mvRel = Tex(DXGI_FORMAT.R16G16_FLOAT, bx[0], by[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "bloques fiabilidad");
            if (edgeMap != null) Com.Release(ref edgeMap);
            if (mvecW != null) Com.Release(ref mvecW);
            edgeMap = Tex(DXGI_FORMAT.R8_UNORM, lw[0], lh[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "borde por punto");
            mvecW = Tex(DXGI_FORMAT.R16G16_FLOAT, lw[0], lh[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "movimiento ensanchado");
            for (int st = 0; st < 2; st++)
            {
                NullViews(TREdge + st);
                SrvTex(TREdge + st, 0, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                UavTex(TREdge + st, U0, edgeMap, DXGI_FORMAT.R8_UNORM);
                NullViews(TREns + st);
                SrvTex(TREns + st, 0, mvec, DXGI_FORMAT.R16G16_FLOAT);
                SrvTex(TREns + st, 1, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(TREns + st, 2, luma[1 - st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(TREns + st, 3, edgeMap, DXGI_FORMAT.R8_UNORM);
                UavTex(TREns + st, U0, mvecW, DXGI_FORMAT.R16G16_FLOAT);
            }
            NullViews(TRFg);   // 17-11 13:40 (fg_vectores=2|3)
            SrvTex(TRFg, 0, mvec, DXGI_FORMAT.R16G16_FLOAT);
            SrvTex(TRFg, 1, mvLevel[0], DXGI_FORMAT.R16G16_FLOAT);
            if (gvec != null) { SrvTex(TRFg, 2, gvec, DXGI_FORMAT.R32G32_FLOAT); UavTex(TRFg, U0 + 1, gvec, DXGI_FORMAT.R32G32_FLOAT); }
            if (mvecFg != null) UavTex(TRFg, U0, mvecFg, DXGI_FORMAT.R16G16_FLOAT);
            NullViews(TRFgD);   // 17-11 15:20 (fg_profundidad): borde por punto -> profundidad para DLSS-G
            SrvTex(TRFgD, 0, edgeMap, DXGI_FORMAT.R8_UNORM);
            if (depthFg != null) UavTex(TRFgD, U0, depthFg, DXGI_FORMAT.R32_FLOAT);
            NullViews(TRApert);
            SrvTex(TRApert, 0, mvCons, DXGI_FORMAT.R16G16_FLOAT);
            SrvTex(TRApert, 1, mvRel, DXGI_FORMAT.R16G16_FLOAT);
            SrvTex(TRApert, 2, mvPrev[0], DXGI_FORMAT.R16G16_FLOAT);   // (mvtemporal_mezcla)
            UavTex(TRApert, U0, mvLevel[0], DXGI_FORMAT.R16G16_FLOAT);
            for (int st = 0; st < 2; st++)
            {
                int tt = TRTruth + st;
                NullViews(tt);
                SrvTex(tt, 0, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                UavTex(tt, U0, mvec, DXGI_FORMAT.R16G16_FLOAT);
                UavTex(tt, U0 + 1, depth, DXGI_FORMAT.R32_FLOAT);
                int tc = TRCons + st;
                NullViews(tc);
                SrvTex(tc, 0, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(tc, 1, luma[1 - st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(tc, 2, mvLevel[0], DXGI_FORMAT.R16G16_FLOAT);
                UavTex(tc, U0, mvCons, DXGI_FORMAT.R16G16_FLOAT);
                UavTex(tc, U0 + 1, mvRel, DXGI_FORMAT.R16G16_FLOAT);
                int tk = TRCapas + st;
                NullViews(tk);
                SrvTex(tk, 0, mvLevel[0], DXGI_FORMAT.R16G16_FLOAT);
                SrvTex(tk, 1, blockInfo, DXGI_FORMAT.R16G16B16A16_FLOAT);
                SrvTex(tk, 2, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(tk, 3, luma[1 - st, 0], DXGI_FORMAT.R8_UNORM);
                UavTex(tk, U0, mvec, DXGI_FORMAT.R16G16_FLOAT);
            }
            FgFillViews();   // 21-11 (FG21): fg_relleno, EngineFgRelleno.cs
            FgGuardViews();   // 21-11 (BORDE): fg_borde, EngineFgBorde.cs
            FgGlobalViews();   // 21-11 (1.2.1): fg_global / fg_quieto, EngineFgGlobal.cs
        }

        // RecordMotion, tras la busqueda (mvLevel[0] en NON_PIXEL_SHADER_RESOURCE): mvconsenso=N. Cada bloque elige entre su vector y los
        // de sus 5x5 vecinos el que menos cuesta: lo que queda tras moverlo (media por punto) menos N niveles x la fraccion de vecinos que
        // llevan ese mismo movimiento (+-1 px). Un barrote vertical no dice nada de su movimiento vertical (el bloque casa igual 3 px mas
        // arriba o mas abajo): solo, cada bloque cogia una altura distinta y DLSS traia la historia de otro trozo del barrote (ondulado) ->
        // el barrote temblaba. Con el consenso gana el movimiento que llevan los vecinos si casa casi igual
        void RecordConsensus(int cur, uint* c)
        {
            if ((s.MvCons <= 0 && s.MvApert <= 0) || MvLightNow) return;   // (17-11 17:35: mv_ligero)
            EnsureRejasPsos();
            Barrier(mvCons, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            Barrier(mvRel, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)lw[0]; c[1] = (uint)lh[0]; c[2] = (uint)bx[0]; c[3] = (uint)by[0];
            c[4] = 2;   // (5x5 fijo)
            c[5] = s.MvCons > 0 ? 1u : 0u;   // sin mvconsenso: solo la fiabilidad (para mvapertura)
            c[6] = s.MvConsStill ? 1u : 0u; c[7] = s.MvConsAxis ? 1u : 0u;
            c[8] = F((float)(s.MvCons / 255.0)); c[9] = F((float)s.MvConsTol); c[10] = F(0.004f);
            BindCompute(psoMvCons, TRCons + cur, c, 12);
            list.Dispatch(Groups(bx[0], 8), Groups(by[0], 8), 1);
            Barrier(mvRel, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            if (s.MvApert > 0)
            {
                // 17-11 (mvapertura): mvCons + fiabilidad -> mvLevel[0]
                Barrier(mvCons, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                Barrier(mvLevel[0], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                for (int i = 0; i < 16; i++) c[i] = 0;
                c[0] = (uint)bx[0]; c[1] = (uint)by[0]; c[2] = (uint)Math.Max(1, Math.Min(16, s.MvApert)); c[3] = s.MvApertOutl ? 1u : 0u;
                c[4] = F((float)s.MvApertGate); c[5] = F(4f); c[6] = F(1e-5f); c[7] = F(s.MvTemporal && mvPrevValid ? (float)s.MvTempBlend : 0f);
                c[8] = F((float)s.MvApertAmb);   // 17-11 12:50 (borrosidad): mvapertura_ambigua
                BindCompute(psoMvApert, TRApert, c, 12);
                list.Dispatch(Groups(bx[0], 8), Groups(by[0], 8), 1);
                Barrier(mvLevel[0], D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                return;
            }
            Barrier(mvCons, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
            Barrier(mvLevel[0], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
            list.CopyResource(mvLevel[0], mvCons);
            Barrier(mvLevel[0], D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(mvCons, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        // RecordMotion, con mvec en UNORDERED_ACCESS y blockInfo escrito: mvcapas=1, vectores por punto (en vez de MvExpand/MvExpandQ)
        void RecordCapas(int cur, uint* c)
        {
            EnsureRejasPsos();
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)bx[0]; c[3] = (uint)by[0];
            c[8] = F((float)(s.MvPxMargin / 255.0)); c[9] = F((float)(s.MvCapasTie / 255.0)); c[10] = F((float)s.MvSmooth);
            BindCompute(psoMvCapas, TRCapas + cur, c, 12);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
        }


        // 17-11 13:20 (mvensancha=R): el barrote "ensanchado" punto a punto (sin decisiones por bloques de 8x8, que dejaban escalones en
        // el cielo con mvcapas). Un punto liso (cielo) junto a un borde fuerte cuyo vector es distinto del suyo (> 0,5 px) coge el vector
        // del borde si esta a <= |diferencia| + 1 px (como mucho R) y si explica su 3x3 igual (+ mvcapas_empate): es el cielo que el barrote
        // destapa o tapa en esta imagen; con su vector propio (0 o el del cielo) DLSS traia de la anterior el barrote: el rastro
        void RecordEnsancha(int cur, uint* c)
        {
            if (s.MvEns <= 0) return;
            EnsureRejasPsos();
            // 1) fuerza del borde por punto
            Barrier(edgeMap, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH;
            BindCompute(psoEdge, TREdge + cur, c, 4);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(edgeMap, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            // 2) mvec -> mvecW ensanchado -> mvec
            Barrier(mvec, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(mvecW, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)Math.Max(1, Math.Min(16, s.MvEns));
            c[4] = F((float)(s.MvEnsEdge / 255.0)); c[5] = F((float)(s.MvEnsFlat / 255.0)); c[6] = F((float)(s.MvCapasTie / 255.0));
            BindCompute(psoEns, TREns + cur, c, 8);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(mvecW, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
            Barrier(mvec, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
            list.CopyResource(mvec, mvecW);
            Barrier(mvec, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            Barrier(mvecW, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        // 17-11 13:40 (REJAS, fg_vectores=2|3): vectores solo para la generacion (DLSS-G), en su propia textura; DLSS SR sigue con los
        // del motor (EvaluateDlss, etiqueta local). 2 = el campo suavizado (media de (2R+1)^2 muestras cada 8 px alrededor, sin puerta: la
        // generacion interpola el fondo y la reja con un movimiento suave en vez de saltos de bloque), 3 = un solo vector para toda la
        // imagen (la moda de los vectores de bloque en casillas de 1 px, afinada con la media de los que caen en +-0,75; en la grafica)
        // 21-11 (1.2.1): con fg_global=1 (de serie) el global sale de su propia busqueda y lo quieto va a 0 (EngineFgGlobal.cs): la moda de
        // los bloques en -32..32 dejaba 32 px en los giros de mas de 32 px por imagen y 0 en los paneos de 5-64 px a 4K
        void RecordFgVec(bool on, int cur)
        {
            if (!(fgApplied && s.FgVec >= 2) || mvecFg == null || gvec == null || FgVecFullNow) return;   // (20-11 FGFALLOS: fg_vectores_completos, DLSS-G con mvec)
            EnsureRejasPsos();
            uint* c = stackalloc uint[16];
            if (on && FgGlobalNow) { RecordFgGlobal(cur); RecordFgGlobalOut(cur); return; }   // 21-11 (1.2.1)
            if (on && s.FgVec == 3)
            {
                Barrier(gvec, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                for (int i = 0; i < 16; i++) c[i] = 0;
                c[0] = (uint)bx[0]; c[1] = (uint)by[0];
                BindCompute(psoFgGlobal, TRFg, c, 4);
                list.Dispatch(1, 1, 1);
                Barrier(gvec, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            }
            Barrier(mvecFg, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)Math.Max(1, Math.Min(8, s.FgVecRad)); c[3] = on ? (uint)s.FgVec : 0u;
            c[4] = (uint)bx[0]; c[5] = (uint)by[0];
            c[6] = s.FgVecSmooth ? 1u : 0u; c[7] = F((float)s.FgVecGate);   // 17-11 15:10: fg_vectores_suave, fg_vectores_puerta
            BindCompute(psoFgSmooth, TRFg, c, 8);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(mvecFg, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }
        // 17-11 15:20 (fg_profundidad=R): profundidad aproximada solo para la generacion. Con la de siempre (todo a 0) DLSS-G no sabe en
        // el borde de un barrote que va delante (el barrote o el cielo) y mezcla los dos: el rastro. Aqui, a <= R px de un borde fuerte
        // (fg_profundidad_borde, niveles de luma) = cerca; lo liso = lejos. DLSS SR sigue con la de siempre (EvaluateDlss, etiqueta local)
        void RecordFgDepth(int cur)
        {
            if (!(fgApplied && s.FgDepth > 0) || depthFg == null || edgeMap == null) return;
            if (FgLayerDepthNow) return;   // 21-11 (1.2.1): la profundidad por capas ya la ha escrito RecordFgGlobalOut (EngineFgGlobal.cs)
            EnsureRejasPsos();
            uint* c = stackalloc uint[16];
            Barrier(edgeMap, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH;
            BindCompute(psoEdge, TREdge + cur, c, 4);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(edgeMap, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(depthFg, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)Math.Max(1, Math.Min(8, s.FgDepth));
            c[4] = F((float)(s.FgDepthEdge / 255.0)); c[5] = F((float)s.FgDepthNear); c[6] = F((float)s.FgDepthFar);
            BindCompute(psoFgDepth, TRFgD, c, 8);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(depthFg, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        // RecordMotion, con mvec en UNORDERED_ACCESS: vectores y/o profundidad de la verdad del paralaje
        void RecordTruth(int cur, uint* c)
        {
            if (s.MvTruth <= 0 || s.Test != "paralaje" || curSeq < 0) return;
            EnsureRejasPsos();
            long prev = prevMotionSeq >= 0 && prevMotionSeq < curSeq ? prevMotionSeq : curSeq - 1;
            int fx, fy, bx, by, px, py, qx, qy;
            Parallax.OffsetsOf(s.TestPar, s.TestVel, curSeq, out fx, out fy, out bx, out by);
            Parallax.OffsetsOf(s.TestPar, s.TestVel, prev, out px, out py, out qx, out qy);
            float k = (float)inW / Parallax.W4;
            UavBarrier(mvec);
            Barrier(depth, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH;
            c[2] = (uint)s.MvTruth;   // suma de 1 (vectores), 2 (profundidad), 4 (la reja ensanchada lo que se separa del cielo + 1 px)
            c[4] = F((fx - px) * k); c[5] = F((fy - py) * k); c[6] = F((bx - qx) * k); c[7] = F((by - qy) * k);
            c[8] = F(30f / 255f); c[9] = F((float)s.RejasNear); c[10] = F((float)s.RejasFar);
            BindCompute(psoMvTruth, TRTruth + cur, c, 12);
            prevMotionSeq = curSeq;
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(depth, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        // =============================================================================================== volcado
        const int DRing = 6;
        readonly ID3D12Resource[] dRb = new ID3D12Resource[DRing];
        readonly IntPtr[] dRbPtr = new IntPtr[DRing];
        readonly ulong[] dRbFence = new ulong[DRing];
        readonly long[] dRbSeq = new long[DRing];
        readonly bool[] dRbUsed = new bool[DRing];
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT dFpOut, dFpMv, dFpIn;
        int dX, dY, dW, dH, dMx, dMy, dMw, dMh;
        int dumpSeen, dumpTaken;
        bool dumpReady, dumpOff;
        readonly Queue<KeyValuePair<string, byte[]>> dQueue = new Queue<KeyValuePair<string, byte[]>>();
        Thread dThread;
        volatile bool dStop;
        readonly List<string> dInfo = new List<string>();

        static D3D12_PLACED_SUBRESOURCE_FOOTPRINT Foot(ulong off, DXGI_FORMAT f, int w, int h, int bpp)
        {
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT fp = new D3D12_PLACED_SUBRESOURCE_FOOTPRINT();
            fp.Offset = off;
            fp.Footprint.Format = f; fp.Footprint.Width = (uint)w; fp.Footprint.Height = (uint)h; fp.Footprint.Depth = 1;
            fp.Footprint.RowPitch = (uint)((w * bpp + 255) & ~255);
            return fp;
        }
        static ulong Align512(ulong v) { return (v + 511) & ~511UL; }

        // RenderFrame, tras el CAS ('final' en COPY_SOURCE), solo con imagen nueva
        void RejasDump(int slot)
        {
            if (s.DumpDir.Length == 0 || dumpOff || slot < 0) return;
            DumpCollect(false);
            dumpSeen++;
            if (dumpSeen <= s.DumpFrom || dumpTaken >= s.DumpN) return;
            if (!dumpReady)
            {
                dX = Math.Max(0, Math.Min(outW - 16, s.DumpX)); dY = Math.Max(0, Math.Min(outH - 16, s.DumpY));
                dW = Math.Min(s.DumpW, outW - dX); dH = Math.Min(s.DumpH, outH - dY);
                dMx = dX * inW / outW; dMy = dY * inH / outH; dMw = Math.Max(1, dW * inW / outW); dMh = Math.Max(1, dH * inH / outH);
                dFpOut = Foot(0, ColorFmt, dW, dH, 4);
                dFpMv = Foot(Align512(dFpOut.Offset + (ulong)dFpOut.Footprint.RowPitch * (ulong)dH), DXGI_FORMAT.R16G16_FLOAT, dMw, dMh, 4);
                dFpIn = Foot(Align512(dFpMv.Offset + (ulong)dFpMv.Footprint.RowPitch * (ulong)dMh), ColorFmt, dMw, dMh, 4);
                long total = (long)(dFpIn.Offset + (ulong)dFpIn.Footprint.RowPitch * (ulong)dMh);
                for (int i = 0; i < DRing; i++)
                {
                    dRb[i] = MakeBuffer(D3D12_HEAP_TYPE.READBACK, total, D3D12_RESOURCE_STATES.COPY_DEST, "volcado " + i);
                    Hr.Check(dRb[i].Map(0, null, out dRbPtr[i]), "Map volcado");
                }
                try { Directory.CreateDirectory(s.DumpDir); } catch { }
                dInfo.Add(string.Format(CultureInfo.InvariantCulture, "salida {0} {1} {2} {3} entrada {4} {5} {6} {7} tam {8} {9} {10} {11} | {12}",
                    dX, dY, dW, dH, dMx, dMy, dMw, dMh, inW, inH, outW, outH, s.Describe()));
                dInfo.Add(string.Format(CultureInfo.InvariantCulture, "test_par {0} test_vel {1} preset {2} fg {3} fino {4}", s.TestPar, s.TestVel, s.Preset, fgApplied ? 1 : 0, s.TestFine ? 1 : 0));
                dInfo.Add("escena " + (s.Test == "zoom" ? "zoom " + s.TestZoomKind + " " + s.TestTexture : s.Test));   // 17-11 (borrosidad)
                dThread = new Thread(DumpWriter);
                dThread.IsBackground = true; dThread.Name = "volcado";
                dThread.Start();
                dumpReady = true;
            }
            int e = -1;
            for (int i = 0; i < DRing && e < 0; i++) if (!dRbUsed[i]) e = i;
            if (e < 0)
            {
                // Todas en vuelo: se espera la mas vieja (el banco no mide el ritmo mientras se vuelca)
                ulong oldest = ulong.MaxValue;
                for (int i = 0; i < DRing; i++) if (dRbFence[i] < oldest) oldest = dRbFence[i];
                WaitFence(oldest);
                DumpCollect(false);
                for (int i = 0; i < DRing && e < 0; i++) if (!dRbUsed[i]) e = i;
                if (e < 0) return;
            }
            D3D12_BOX box = new D3D12_BOX((uint)dX, (uint)dY, (uint)(dX + dW), (uint)(dY + dH));
            D3D12_TEXTURE_COPY_LOCATION dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(dRb[e], dFpOut);
            D3D12_TEXTURE_COPY_LOCATION src = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(final, 0);
            list.CopyTextureRegion(ref dst, 0, 0, 0, ref src, &box);
            D3D12_BOX mbox = new D3D12_BOX((uint)dMx, (uint)dMy, (uint)(dMx + dMw), (uint)(dMy + dMh));
            // 21-11 (FG21, prueba): con la variable S2_VOLCADO_FG=1 los vectores que recibe la generacion (FgMvec) en vez de mvec
            ID3D12Resource dmv = Environment.GetEnvironmentVariable("S2_VOLCADO_FG") == "1" ? FgMvec : mvec;
            Barrier(dmv, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
            dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(dRb[e], dFpMv);
            src = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(dmv, 0);
            list.CopyTextureRegion(ref dst, 0, 0, 0, ref src, &mbox);
            Barrier(dmv, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(inColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
            dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(dRb[e], dFpIn);
            src = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(inColor, 0);
            list.CopyTextureRegion(ref dst, 0, 0, 0, ref src, &mbox);
            Barrier(inColor, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            dRbUsed[e] = true;
            dRbFence[e] = fenceValue + 1;   // la valla de esta imagen (se senala al enviarla)
            dRbSeq[e] = slotSeq[slot];
            dumpTaken++;
        }

        void DumpCollect(bool all)
        {
            if (!dumpReady) return;
            ulong done = fence.GetCompletedValue();
            for (int i = 0; i < DRing; i++)
            {
                if (!dRbUsed[i]) continue;
                if (done < dRbFence[i]) { if (!all) continue; WaitFence(dRbFence[i]); }
                string tag = dRbSeq[i].ToString("D6");
                Take(i, dFpOut, dW, dH, 4, "o_" + tag);
                Take(i, dFpMv, dMw, dMh, 4, "m_" + tag);
                Take(i, dFpIn, dMw, dMh, 4, "i_" + tag);
                lock (dInfo) dInfo.Add("imagen " + dRbSeq[i]);
                dRbUsed[i] = false;
            }
        }

        void Take(int e, D3D12_PLACED_SUBRESOURCE_FOOTPRINT fp, int w, int h, int bpp, string name)
        {
            byte[] o = new byte[(long)w * h * bpp];
            for (int y = 0; y < h; y++)
                Marshal.Copy(new IntPtr(dRbPtr[e].ToInt64() + (long)fp.Offset + (long)y * fp.Footprint.RowPitch), o, y * w * bpp, w * bpp);
            lock (dQueue) { dQueue.Enqueue(new KeyValuePair<string, byte[]>(Path.Combine(s.DumpDir, name + ".raw"), o)); Monitor.Pulse(dQueue); }
        }

        void DumpWriter()
        {
            while (true)
            {
                KeyValuePair<string, byte[]> it;
                lock (dQueue)
                {
                    while (dQueue.Count == 0 && !dStop) Monitor.Wait(dQueue, 200);
                    if (dQueue.Count == 0) return;
                    it = dQueue.Dequeue();
                }
                try { File.WriteAllBytes(it.Key, it.Value); } catch (Exception ex) { Log.Error("Volcado", ex); }
            }
        }

        // Al salir del bucle (Run): lo que quede en vuelo, a disco
        void RejasDumpFinish()
        {
            if (!dumpReady) return;
            try
            {
                DumpCollect(true);
                dStop = true;
                lock (dQueue) Monitor.Pulse(dQueue);
                if (dThread != null) dThread.Join(30000);
                lock (dInfo) File.WriteAllLines(Path.Combine(s.DumpDir, "info.txt"), dInfo.ToArray());
                Log.Write("Volcado: " + dumpTaken + " imagenes en " + s.DumpDir);
            }
            catch (Exception ex) { Log.Error("Volcado (final)", ex); }
            dumpOff = true;
        }
    }

    static class RShaders
    {

        // fg_vectores=2: media de (2R+1)^2 muestras de los vectores de bloque cada 8 px alrededor del punto (sin puerta); 3: el global;
        // 0 (Mode): todo a 0. c0: ancho, alto, R, modo | c1: bloques x/y
        public const string FgSmooth = @"
cbuffer C : register(b0) { uint W, H, R, Mode; uint BX, BY, P0, P1; uint4 Pad2; uint4 Pad3; }
Texture2D<float2> Mv : register(t0);
Texture2D<float2> Blocks : register(t1);
Texture2D<float2> G : register(t2);
RWTexture2D<float2> Out : register(u0);

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    if (Mode == 3) { Out[id.xy] = G.Load(int3(0, 0, 0)); return; }
    if (Mode != 2) { Out[id.xy] = float2(0, 0); return; }
    int2 p = int2(id.xy);
    int Rr = (int)R;
    // 17-11 15:10: P0 = fg_vectores_suave (cada muestra con interpolacion bilineal entre centros de bloque: sin escalones de 8 px);
    // P1 = fg_vectores_puerta (px, asfloat): las muestras pesan menos cuanto mas se separan del vector del punto (la reja y el cielo
    // no se mezclan); 0 = sin puerta
    float gate = asfloat(P1);
    float2 vp = Mv.Load(int3(p, 0));
    int2 mx = int2(BX - 1, BY - 1);
    float2 sum = 0;
    float n = 0;
    for (int j = -Rr; j <= Rr; j++)
        for (int i = -Rr; i <= Rr; i++)
        {
            float2 v;
            if (P0)
            {
                float2 b = (float2(p) + 0.5 + float2(i, j) * 8.0) / 8.0 - 0.5;
                int2 b0 = (int2)floor(b);
                float2 f = b - float2(b0);
                float2 v00 = Blocks.Load(int3(clamp(b0, int2(0, 0), mx), 0));
                float2 v10 = Blocks.Load(int3(clamp(b0 + int2(1, 0), int2(0, 0), mx), 0));
                float2 v01 = Blocks.Load(int3(clamp(b0 + int2(0, 1), int2(0, 0), mx), 0));
                float2 v11 = Blocks.Load(int3(clamp(b0 + int2(1, 1), int2(0, 0), mx), 0));
                v = lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
            }
            else
            {
                int2 q = clamp((p + int2(i, j) * 8) / 8, int2(0, 0), mx);
                v = Blocks.Load(int3(q, 0));
            }
            float w = 1.0;
            if (gate > 0) { float2 d = v - vp; w = exp(-dot(d, d) / (gate * gate)); }
            if (i == 0 && j == 0) w = max(w, 0.05);
            sum += w * v; n += w;
        }
    Out[id.xy] = sum / max(n, 1e-4);
}";

        // fg_vectores=3: el vector global (un grupo de 1024 hilos): moda de los vectores de bloque en casillas de 1 px (-32..32), afinada
        // con la media de los que caen a +-0,75 de ella. c0: bloques x/y
        public const string FgGlobal = @"
cbuffer C : register(b0) { uint BX, BY, P0, P1; uint4 Pad1; uint4 Pad2; uint4 Pad3; }
Texture2D<float2> Blocks : register(t1);
RWTexture2D<float2> Gout : register(u1);
groupshared uint hist[4225];
groupshared uint bestC[1024];
groupshared uint bestI[1024];
groupshared int sx;
groupshared int sy;
groupshared uint cnt;

[numthreads(1024, 1, 1)]
void main(uint3 gt : SV_GroupThreadID)
{
    uint t = gt.x;
    for (uint i = t; i < 4225; i += 1024) hist[i] = 0;
    if (t == 0) { sx = 0; sy = 0; cnt = 0; }
    GroupMemoryBarrierWithGroupSync();
    uint N = BX * BY;
    for (uint k = t; k < N; k += 1024)
    {
        float2 v = Blocks.Load(int3(k % BX, k / BX, 0));
        int bxv = clamp((int)round(v.x) + 32, 0, 64), byv = clamp((int)round(v.y) + 32, 0, 64);
        InterlockedAdd(hist[byv * 65 + bxv], 1);
    }
    GroupMemoryBarrierWithGroupSync();
    uint bc = 0, bi = 0;
    for (uint i2 = t; i2 < 4225; i2 += 1024) if (hist[i2] > bc) { bc = hist[i2]; bi = i2; }
    bestC[t] = bc; bestI[t] = bi;
    GroupMemoryBarrierWithGroupSync();
    for (uint s = 512; s > 0; s >>= 1)
    {
        if (t < s && bestC[t + s] > bestC[t]) { bestC[t] = bestC[t + s]; bestI[t] = bestI[t + s]; }
        GroupMemoryBarrierWithGroupSync();
    }
    float2 c = float2((float)((int)(bestI[0] % 65) - 32), (float)((int)(bestI[0] / 65) - 32));
    for (uint k2 = t; k2 < N; k2 += 1024)
    {
        float2 v = Blocks.Load(int3(k2 % BX, k2 / BX, 0));
        if (all(abs(v - c) <= 0.75)) { InterlockedAdd(sx, (int)round(v.x * 256)); InterlockedAdd(sy, (int)round(v.y * 256)); InterlockedAdd(cnt, 1); }
    }
    GroupMemoryBarrierWithGroupSync();
    if (t == 0) Gout[int2(0, 0)] = cnt > 0 ? float2((float)sx, (float)sy) / (256.0 * (float)cnt) : c;
}";

        // mvensancha (1): fuerza del borde por punto = la mayor diferencia central de luma (horizontal o vertical). c0: ancho, alto
        // fg_profundidad: a <= R px de un borde fuerte (>= Tedge) = Near; si no, Far. c0: ancho, alto, R | c1 (float): Tedge, Near, Far
        public const string FgDepth = @"
cbuffer C : register(b0) { uint W, H, R, P0; float Tedge, Near, Far, P1; uint4 Pad2; uint4 Pad3; }
Texture2D<float> Edge : register(t0);
RWTexture2D<float> Out : register(u0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    int Rr = (int)R, st = Rr > 2 ? 2 : 1;
    int2 mx = int2((int)W - 1, (int)H - 1);
    float m = 0;
    for (int j = -Rr; j <= Rr; j += st)
        for (int i = -Rr; i <= Rr; i += st)
            m = max(m, Edge.Load(int3(clamp(p + int2(i, j), int2(0, 0), mx), 0)));
    Out[id.xy] = m >= Tedge ? Near : Far;
}";

        public const string Edge = @"
cbuffer C : register(b0) { uint W, H, P0, P1; uint4 Pad1; uint4 Pad2; uint4 Pad3; }
Texture2D<float> Cur : register(t0);
RWTexture2D<unorm float> Out : register(u0);
float L(int2 q, int W1, int H1) { return Cur.Load(int3(clamp(q, int2(0, 0), int2(W1, H1)), 0)); }
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    int W1 = (int)W - 1, H1 = (int)H - 1;
    float gx = abs(L(p + int2(1, 0), W1, H1) - L(p - int2(1, 0), W1, H1)), gy = abs(L(p + int2(0, 1), W1, H1) - L(p - int2(0, 1), W1, H1));
    Out[id.xy] = saturate(max(gx, gy));
}";

        // mvensancha (2): un punto liso (borde < Tflat) busca en 8 direcciones (hasta R px) el primer punto de borde fuerte (>= Tedge); de
        // los que llevan un vector distinto del suyo (> 0,5 px) y estan a <= |diferencia| + 1 px, el mas cercano; lo coge si explica su 3x3
        // igual (+Tie). c0: ancho, alto, R | c1 (float): Tedge, Tflat, Tie
        public const string Ens = @"
cbuffer C : register(b0) { uint W, H, R, P0; float Tedge, Tflat, Tie, P1; uint4 Pad2; uint4 Pad3; }
Texture2D<float2> Mv : register(t0);
Texture2D<float> Cur : register(t1);
Texture2D<float> Prev : register(t2);
Texture2D<float> Edge : register(t3);
RWTexture2D<float2> Out : register(u0);
SamplerState Lin : register(s0);

static float cc[9];
void LoadCur(int2 p)
{
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
            cc[(j + 1) * 3 + i + 1] = Cur.Load(int3(clamp(p + int2(i, j), int2(0, 0), int2(W - 1, H - 1)), 0));
}
float Err(int2 p, float2 v)
{
    float s = 0;
    float2 inv = 1.0 / float2(W, H);
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            int2 q = clamp(p + int2(i, j), int2(0, 0), int2(W - 1, H - 1));
            s += abs(cc[(j + 1) * 3 + i + 1] - Prev.SampleLevel(Lin, (float2(q) + 0.5 + v) * inv, 0));
        }
    return s * (1.0 / 9.0);
}

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    float2 v = Mv.Load(int3(p, 0));
    if (Edge.Load(int3(p, 0)) >= Tflat) { Out[id.xy] = v; return; }
    float bestD = 1e9;
    float2 best = v;
    bool found = false;
    [unroll] for (int d = 0; d < 8; d++)
    {
        int2 dir = d == 0 ? int2(1, 0) : d == 1 ? int2(-1, 0) : d == 2 ? int2(0, 1) : d == 3 ? int2(0, -1) : d == 4 ? int2(1, 1) : d == 5 ? int2(1, -1) : d == 6 ? int2(-1, 1) : int2(-1, -1);
        float step = (d < 4) ? 1.0 : 1.41421356;
        for (int k = 1; k <= (int)R; k++)
        {
            int2 q = p + dir * k;
            if (q.x < 0 || q.y < 0 || q.x >= (int)W || q.y >= (int)H) break;
            if (Edge.Load(int3(q, 0)) >= Tedge)
            {
                float2 w = Mv.Load(int3(q, 0));
                float dv = max(abs(w.x - v.x), abs(w.y - v.y));
                float dist = step * k;
                if (dv > 0.5 && dist <= dv + 1.0 && dist < bestD) { bestD = dist; best = w; found = true; }
                break;
            }
        }
    }
    if (found)
    {
        LoadCur(p);
        if (Err(p, best) <= Err(p, v) + Tie) v = best;
    }
    Out[id.xy] = v;
}";

        // mvconsenso: por bloque del nivel fino, el vector (el suyo o el de un vecino de 5x5, afinado con la media de los que lo llevan
        // +-Tol) que menos cuesta: lo que queda tras moverlo (media por punto, la anterior con filtro bilineal) - Mu x fraccion de vecinos
        // que lo llevan. Los bloques lisos se quedan como estan (mvcapas los rellena por punto)
        // c0: ancho, alto, bloques x/y | c1: radio | c2 (float): Mu, Tol, liso
        public const string MvCons = @"
cbuffer C : register(b0) { uint LW, LH, BX, BY; uint Rad, P0, P1, P2; float Mu, Tol, Flat, P3; uint4 Pad3; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float2> Mv : register(t2);
RWTexture2D<float2> Out : register(u0);
RWTexture2D<float2> Rel : register(u1);   // 17-11 (mvapertura): fiabilidad de x e y
SamplerState Lin : register(s0);

static float c[64];
float Sad(int2 b, float2 d)
{
    float s = 0;
    float2 inv = 1.0 / float2(LW, LH);
    [unroll] for (int yy = 0; yy < 8; yy++)
        [unroll] for (int x = 0; x < 8; x++)
            s += abs(c[yy * 8 + x] - Prev.SampleLevel(Lin, (float2(b + int2(x, yy)) + 0.5 + d) * inv, 0));
    return s * (1.0 / 64.0);
}

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    int2 bk = int2(id.xy), b = bk * 8;
    int2 mx = int2(LW - 1, LH - 1);
    float mean = 0;
    [unroll] for (int yy = 0; yy < 8; yy++)
        [unroll] for (int x = 0; x < 8; x++) { float v = Cur.Load(int3(clamp(b + int2(x, yy), int2(0, 0), mx), 0)); c[yy * 8 + x] = v; mean += v; }
    mean /= 64;
    float var = 0;
    [unroll] for (int q = 0; q < 64; q++) var += abs(c[q] - mean);
    var /= 64;
    float2 v0 = Mv.Load(int3(bk, 0));
    if (var < Flat) { Out[bk] = v0; Rel[bk] = float2(0, 0); return; }
    // mvconsenso_quieto=1: lo que la busqueda dejo quieto (0, con su preferencia frente al ruido: texto, HUD, marcos) no se toca. Con el
    // consenso los bloques del panel junto al fondo en movimiento cogian algo de ese movimiento (texto del menu con M 0,232 -> 0,263; con
    // esto 0,250; L, K y E igual), pero la reja empeora (temblor L 1,26 -> 1,39, M 1,24 -> 1,38): de serie no
    bool still = P1 && all(v0 == 0);   // (mvconsenso_quieto=1)
    float2 nb[25];
    [unroll] for (int jj = 0; jj < 5; jj++)
        [unroll] for (int ii = 0; ii < 5; ii++)
            nb[jj * 5 + ii] = Mv.Load(int3(clamp(bk + int2(ii - 2, jj - 2), int2(0, 0), int2(BX - 1, BY - 1)), 0));
    // el suyo
    int sup0 = 0;
    [unroll] for (int m0 = 0; m0 < 25; m0++) if (all(abs(nb[m0] - v0) <= Tol)) sup0++;
    float best = Sad(b, v0) - Mu * (float)sup0 / 25.0;
    float2 bv = v0;
    [loop] for (int k = 0; k < (P0 && !still ? 25 : 0); k++)   // (P0 = 0: solo la fiabilidad, para mvapertura sin mvconsenso)
    {
        float2 w = nb[k];
        if (all(abs(w - v0) < 0.25)) continue;
        bool dup = false;
        for (int m = 0; m < k; m++) if (all(abs(nb[m] - w) < 0.25)) dup = true;
        if (dup) continue;
        int sup = 0;
        float2 sum = 0;
        [unroll] for (int m2 = 0; m2 < 25; m2++) if (all(abs(nb[m2] - w) <= Tol)) { sup++; sum += nb[m2]; }
        float2 wm = sum / (float)max(sup, 1);
        float cost = Sad(b, wm) - Mu * (float)sup / 25.0;
        if (cost < best) { best = cost; bv = wm; }
    }
    // mvconsenso_eje=1: la vertical de la capa (media recortada de la de los vecinos con la misma horizontal, +-Tol) con la horizontal
    // propia y +-0,5: con un movimiento horizontal de media punto, el barrote ondulado casaba mejor con 3-5 px de vertical (el ondulado
    // desplazado imita la fraccion) que con la buena a un punto entero; con +-0,5 (bilineal) la buena gana
    if (P2 && !still)
    {
        float sy = 0, ny = 0;
        [unroll] for (int m3 = 0; m3 < 25; m3++) if (!all(nb[m3] == 0) && abs(nb[m3].x - v0.x) <= Tol) { sy += nb[m3].y; ny += 1; }
        if (ny >= 3)
        {
            float m1 = sy / ny, sy2 = 0, ny2 = 0;
            [unroll] for (int m4 = 0; m4 < 25; m4++) if (!all(nb[m4] == 0) && abs(nb[m4].x - v0.x) <= Tol && abs(nb[m4].y - m1) <= 1.5) { sy2 += nb[m4].y; ny2 += 1; }
            float my = ny2 > 0 ? sy2 / ny2 : m1;
            [unroll] for (int t = -1; t <= 1; t++)
            {
                float2 cnd = float2(v0.x + 0.5 * t, my);
                float cost = Sad(b, cnd) - Mu * ny / 25.0;
                if (cost < best) { best = cost; bv = cnd; }
            }
        }
    }
    Out[bk] = bv;
    // 17-11 (mvapertura): fiabilidad de cada componente = curvatura de lo que queda al mover 1 px en x / en y (un barrote vertical: x
    // fiable, y no)
    float s0 = Sad(b, bv);
    float rx = max(0.0, Sad(b, bv + float2(1, 0)) + Sad(b, bv - float2(1, 0)) - 2 * s0);
    float ry = max(0.0, Sad(b, bv + float2(0, 1)) + Sad(b, bv - float2(0, 1)) - 2 * s0);
    Rel[bk] = float2(rx, ry);
}";


        // mvapertura=R: el problema de la apertura. Un barrote vertical casa igual 1-3 px mas arriba o mas abajo: su movimiento vertical no
        // se ve en el bloque (ni el horizontal en un travesaño). Cada componente de cada bloque pasa a ser la media de esa componente en
        // los bloques de su capa a <= R bloques (vector a <= 2,5 px del suyo), pesada por lo fiable que es en cada uno (la curvatura de
        // mvconsenso); el propio pesa x4. Los fiables (un travesaño para la vertical) mandan; los bloques quietos (0) no se tocan ni cuentan.
        // Medido con la reja (17-11): mejor que la media por componente con su propia puerta (1,71 frente a 1,48 de temblor) y que la moda
        // en casillas de 0,5 px (1,89): las dos dejan fracciones distintas en cada bloque
        // c0: bloques x/y, radio | c1 (float): puerta (px), peso propio, minimo
        public const string MvApert = @"
cbuffer C : register(b0) { uint BX, BY, Rad, Outl; float Gate, Own, Eps, Blend; uint4 Pad2; uint4 Pad3; }
Texture2D<float2> Mv : register(t0);
Texture2D<float2> Rel : register(t1);
Texture2D<float2> PrevF : register(t2);   // (mvtemporal_mezcla) el campo final de la imagen anterior
RWTexture2D<float2> Out : register(u0);

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    int2 bk = int2(id.xy);
    float2 v = Mv.Load(int3(bk, 0));
    float2 r = Rel.Load(int3(bk, 0));
    if (all(v == 0) || r.x + r.y <= 0) { Out[bk] = v; return; }
    float2 sw = r * Own, sv = r * Own * v;
    float2 aw = 0, av = 0;   // (Outl) por componente, con la puerta solo en la otra: para los bloques con 5 px de error en la que no se ve
    int R = (int)Rad, st = R > 8 ? 2 : 1;   // (radio > 8: un bloque de cada 2 en cada eje, el coste de radio 8)
    for (int j = -R; j <= R; j += st)
        for (int i = -R; i <= R; i += st)
        {
            if (i == 0 && j == 0) continue;
            int2 q = clamp(bk + int2(i, j), int2(0, 0), int2(BX - 1, BY - 1));
            float2 w = Mv.Load(int3(q, 0));
            if (all(w == 0)) continue;
            float2 rw = Rel.Load(int3(q, 0));
            if (Outl)
            {
                if (abs(w.x - v.x) <= Gate) { aw.y += rw.y; av.y += rw.y * w.y; }
                if (abs(w.y - v.y) <= Gate) { aw.x += rw.x; av.x += rw.x * w.x; }
            }
            if (any(abs(w - v) > Gate)) continue;
            sw += rw; sv += rw * w;
        }
    float2 o = v;
    if (sw.x > Eps) o.x = sv.x / sw.x;
    if (sw.y > Eps) o.y = sv.y / sw.y;
    if (Outl)
    {
        // un bloque que se sale de su capa en la componente que no se ve (menos fiable que sus vecinos juntos): la media de ellos
        if (aw.y > Eps && aw.y > r.y * Own && abs(v.y - av.y / aw.y) > Gate) o.y = av.y / aw.y;
        if (aw.x > Eps && aw.x > r.x * Own && abs(v.x - av.x / aw.x) > Gate) o.x = av.x / aw.x;
    }
    // 17-11 12:50 (borrosidad, mvapertura_ambigua=A): solo la componente que de verdad no se ve: la que en este bloque es A veces menos
    // fiable que la otra (la vertical de un barrote vertical). Un bloque con detalle en las dos (madera, piedra) se queda el suyo: en una
    // escena 3D (el techo al girar la camara) la media de 16 bloques lo corria 1-2 px y DLSS mezclaba historia desalineada: borroso
    float amb = asfloat(Pad2.x);
    if (amb > 0)
    {
        float rm = max(r.x, r.y);
        if (r.x >= amb * rm) o.x = v.x;
        if (r.y >= amb * rm) o.y = v.y;
    }
    // mvtemporal_mezcla=a: la camara se mueve suave: lo que cambia de una imagen a otra en el mismo bloque (si es la misma capa, < 1 px) es
    // sobre todo error de la estimacion, y el temblor es justo eso
    if (Blend > 0)
    {
        float2 p = PrevF.Load(int3(bk, 0));
        if (!all(p == 0) && all(abs(o - p) < 1.0)) o = lerp(o, p, Blend);
    }
    Out[bk] = o;
}";

        // mvcapas=1: vectores por punto (en vez de MvExpand / MvExpandQ). Cada punto, el vector de su bloque (con mvsuave) salvo que el de
        // un bloque vecino distinto (o 0) explique su 3x3 Margin niveles mejor (como mvpx con mvpx_margen) o, si su bloque es liso (sin
        // nada que buscar: cielo, pared lisa) y el vecino tiene textura, que lo explique igual (+Tie): la reja "ensanchada" sobre el cielo
        // liso, como hace un juego con su profundidad. Asi el borde del barrote y el cielo que destapa traen la historia con el movimiento
        // del barrote (cielo en la anterior), no con 0 o el del cielo (que en la anterior era barrote: estela, borde que tiembla)
        // c0: ancho, alto, bloques x/y | c2 (float): Margin, Tie, Smooth (mvsuave)
        public const string MvCapas = @"
cbuffer C : register(b0) { uint W, H, BX, BY; uint P0, P1, P2, P3; float Margin, Tie, Smooth, P4; uint4 Pad3; }
Texture2D<float2> Blocks : register(t0);
Texture2D<float4> Inf : register(t1);
Texture2D<float> Cur : register(t2);
Texture2D<float> Prev : register(t3);
RWTexture2D<float2> Out : register(u0);
SamplerState Lin : register(s0);

int2 Cb(int2 p) { return clamp(p, int2(0, 0), int2(BX - 1, BY - 1)); }
float2 B(int2 p) { return Blocks.Load(int3(Cb(p), 0)); }
float2 Sm(int2 bk, float R)
{
    float2 v = B(bk);
    if (R <= 0) return v;
    float2 sum = 0;
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            float2 w = B(bk + int2(i, j));
            if (any(abs(w - v) > R)) return v;
            sum += w;
        }
    return sum * (1.0 / 9.0);
}

static float cc[9];
void LoadCur(int2 p)
{
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
            cc[(j + 1) * 3 + i + 1] = Cur.Load(int3(clamp(p + int2(i, j), int2(0, 0), int2(W - 1, H - 1)), 0));
}
float Err(int2 p, float2 v)
{
    float s = 0;
    float2 inv = 1.0 / float2(W, H);
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            int2 q = clamp(p + int2(i, j), int2(0, 0), int2(W - 1, H - 1));
            s += abs(cc[(j + 1) * 3 + i + 1] - Prev.SampleLevel(Lin, (float2(q) + 0.5 + v) * inv, 0));
        }
    return s * (1.0 / 9.0);
}

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy), bk = p / 8;
    float2 v = Sm(bk, Smooth);
    bool ownFlat = Inf.Load(int3(Cb(bk), 0)).w > 0.5;
    bool border = false;
    float eOwn = 0, bestE = 1e9;
    float2 best = v;
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            if (i == 0 && j == 0) continue;
            float2 w = B(bk + int2(i, j));
            if (all(abs(w - v) <= 1.0)) continue;
            if (!border) { border = true; LoadCur(p); eOwn = Err(p, v); }
            float e = Err(p, w);
            bool nbTex = Inf.Load(int3(Cb(bk + int2(i, j)), 0)).w < 0.5;
            bool ok = e < eOwn - Margin || (ownFlat && nbTex && e <= eOwn + Tie);
            if (ok && e < bestE) { bestE = e; best = w; }
        }
    if (border && any(v != 0))
    {
        float e0 = Err(p, float2(0, 0));
        if (e0 < eOwn - Margin && e0 < bestE) { bestE = e0; best = float2(0, 0); }
    }
    Out[id.xy] = best;
}";
        // mvverdad: vectores y/o profundidad de la verdad del paralaje (la capa por la luma: la reja es oscura)
        // c0: ancho, alto, modo (1 vectores, 2 profundidad, 4 la reja ensanchada |Vf-Vb|+1 px) | c1: vector de la reja, del cielo |
        // c2: umbral de luma, cerca, lejos
        public const string MvTruth = @"
cbuffer C : register(b0) { uint W, H, Mode, P0; float2 Vf; float2 Vb; float T, Dn, Df, P1; uint4 Pad3; }
Texture2D<float> Cur : register(t0);
RWTexture2D<float2> Out : register(u0);
RWTexture2D<float> Dep : register(u1);

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    bool fg = Cur.Load(int3(p, 0)) < T;
    if ((Mode & 12) && !fg)
    {
        int R = (Mode & 8) ? 2 : min(10, (int)ceil(max(abs(Vf.x - Vb.x), abs(Vf.y - Vb.y))) + 1);   // 8: solo 2 px (el borde)
        for (int j = -R; j <= R && !fg; j++)
            for (int i = -R; i <= R; i++)
                if (Cur.Load(int3(clamp(p + int2(i, j), int2(0, 0), int2(W - 1, H - 1)), 0)) < T) { fg = true; break; }
    }
    if (Mode & 1) Out[id.xy] = fg ? Vf : ((Mode & 16) ? float2(0, 0) : Vb);   // 16: el cielo (lejos de la reja) con 0
    if (Mode & 2) Dep[id.xy] = fg ? Dn : Df;
}";
    }
}
