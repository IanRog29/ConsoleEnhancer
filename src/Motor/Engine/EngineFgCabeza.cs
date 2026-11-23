using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgCabeza.cs (1.2.4, sexta ronda, agente CABEZA). El usuario: "intenta tambien evitar que desaparezca la cabeza al girar la
    // camara con todos los fgs". Lo que el motor le da a XeSS FG y a FSR 3 FG (y a la FSR FG x3 / x4 de FGMULTI: los mismos mvecFg y
    // depthFg) en un juego en tercera persona: el personaje casi quieto en el centro mientras el mundo gira detras.
    //
    // CAUSA (leida en el codigo y medida con test=real / reala: capturas reales de la PS5 con un personaje de espaldas, ver el informe):
    // con fg_vectores=3 (fg_global, de serie) la generacion recibe UN vector, el de la camara, en toda la imagen, y 0 solo en lo QUIETO CON
    // TEXTURA (fg_quieto: el 3x3 casa sin mover y tiene >= 6 niveles de textura). (1) Lo liso del personaje (piel, pelo, ropa sin dibujo)
    // no pasa la regla de la textura y se lleva el vector de la camara: en la inventada esa parte de la cabeza se desplaza media camara y se
    // mezcla con el fondo ("desaparece"). (2) Un personaje que respira o se balancea 1-3 px no "casa sin mover" en ningun punto: TODO el
    // personaje se lleva el vector de la camara. (3) La profundidad: a XeSS FG la de los bordes (fg_profundidad: "cerca" a <= 4 px de
    // cualquier borde fuerte, tambien los del fondo; lo liso, lejos) y a FSR 3 FG la textura 'depth' del motor, toda a 0: ninguna dice que
    // el personaje va DELANTE del fondo, y las dos generaciones usan la profundidad para decidir quien tapa a quien donde el fondo que se
    // mueve y el personaje quieto caen en el mismo punto de la inventada (y para ensanchar los vectores del de delante en su borde): sin ella
    // el fondo pasa por encima de la cabeza.
    //
    // ARREGLO (fg_cabeza=1, de serie; solo con XeSS FG / FSR 3 FG del motor, que son las que quedan; fg_cabeza=0 = exactamente lo de antes):
    //   A) por bloque de 8x8 del nivel 1 de la piramide de luma (16x16 de la entrada): lo que queda tras mover con el vector de la camara
    //      frente a lo mejor de una busqueda local de +-fg_cabeza_radio px del nivel 1 alrededor de 0 (sin los vectores pegados al de la
    //      camara): "va con la camara", "casi quieto" (el personaje, la interfaz; con su pequeño movimiento, con medio punto) o "no se sabe"
    //      (liso: los dos casan). Si la camara no se mueve (< fg_cabeza_giro px), no se sabe nada nuevo: se queda lo de la imagen anterior.
    //   B) por bloque: lo liso que no se sabe es "casi quieto" si lo rodea lo casi quieto (en 3 de 4 direcciones a <= fg_cabeza_rodeo
    //      bloques) o si ya lo era en la imagen anterior y no lo rodea lo que va con la camara (3 de 4); si no, va con la camara. Un bloque "casi quieto" suelto en medio de lo que va
    //      con la camara (un dibujo del fondo que casa por casualidad) vuelve a ir con la camara.
    //   C) por punto de la entrada: donde los 3x3 bloques de alrededor dicen lo mismo, eso; en la frontera, cada punto elige entre el vector
    //      del personaje y el de la camara por su 3x3 (empate: la clase de los bloques, bilineal). Lo quieto con textura de lo que va con la
    //      camara (letras pequeñas de la interfaz) sigue a 0 como con fg_quieto. Vector de lo casi quieto = el suyo (el balanceo); de lo que
    //      va con la camara = el de la camara. Profundidad: lo casi quieto CERCA (fg_cabeza_cerca) y lo demas LEJOS (fg_cabeza_lejos), en la
    //      textura de la generacion (depthFg) que reciben XeSS FG y FSR 3 FG. DLSS SR y DLSS 5 no la ven (siguen con mvec y su profundidad).
    partial class Settings
    {
        public int FgCab = 1;                       // fg_cabeza=0|1
        public int FgCabR = 3;                      // fg_cabeza_radio: busqueda local, px del nivel 1 (1/2): +-3 = +-6 px de la entrada
        public double FgCabRatio = 0.6;             // fg_cabeza_ratio: "casi quieto" si lo local deja menos de esto x lo de la camara
        public double FgCabNoise = 2;               // fg_cabeza_ruido: niveles de luma (0-255) por punto que no cuentan
        public int FgCabFill = 8;                   // fg_cabeza_rodeo: bloques (16 px de la entrada) que se miran en cada direccion
        public double FgCabMinG = 4;                // fg_cabeza_giro: px de la entrada que tiene que moverse la camara para clasificar
        public double FgCabNear = 0.5, FgCabFar = 0.99;   // fg_cabeza_cerca / _lejos (profundidad normal: 0 cerca, 1 lejos)
        public bool FgCabModel = true;              // fg_cabeza_modelo=1: el fondo con traslacion + acercamiento + giro (0 = solo el vector de la camara)
        public int FgCabEdge = 18;                  // fg_cabeza_borde: en la frontera (5x5 bloques), casi quieto solo si su 3x3 casa (< esto x ruido, suma de 9); 0 = 3x3 sin limite

        bool ApplyFgCabeza(string k, string val)
        {
            switch (k)
            {
                case "fg_cabeza": FgCab = Math.Max(0, Math.Min(1, Iv(val))); return true;
                case "fg_cabeza_radio": FgCabR = Math.Max(1, Math.Min(6, Iv(val))); return true;
                case "fg_cabeza_ratio": FgCabRatio = Math.Max(0.1, Math.Min(1, Dv(val))); return true;
                case "fg_cabeza_ruido": FgCabNoise = Math.Max(0, Math.Min(32, Dv(val))); return true;
                case "fg_cabeza_rodeo": FgCabFill = Math.Max(1, Math.Min(32, Iv(val))); return true;
                case "fg_cabeza_giro": FgCabMinG = Math.Max(0.5, Math.Min(64, Dv(val))); return true;
                case "fg_cabeza_cerca": FgCabNear = Math.Max(0, Math.Min(1, Dv(val))); return true;
                case "fg_cabeza_lejos": FgCabFar = Math.Max(0, Math.Min(1, Dv(val))); return true;
                case "fg_cabeza_modelo": FgCabModel = Bv(val); return true;
                case "fg_cabeza_borde": FgCabEdge = Math.Max(0, Math.Min(200, Iv(val))); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        // Tablas (EngineRejas.cs TRCount): A y C por juego de luma, M una, B por juego y memoria (ida y vuelta)
        const int TRCabA = TRBase + 31 /* + juego */, TRCabM = TRBase + 33, TRCabB = TRBase + 34 /* + 2 x juego + memoria */, TRCabC = TRBase + 38 /* + juego */;
        ID3D12PipelineState psoCabA, psoCabM, psoCabB, psoCabC;
        ID3D12Resource cabBlk, cabBg, cabCls, cabModel;
        readonly ID3D12Resource[] cabMem = new ID3D12Resource[2];
        int cabBx, cabBy, cabMemCur;
        bool cabSaid;

        // XeSS FG / FSR 3 FG del motor (FfxFgVecNow) con el vector de la camara (fg_global, fg_vectores=3)
        bool FgCabezaNow { get { return s.FgCab > 0 && FgGlobalNow && FfxFgVecNow && cabBlk != null && cabCls != null && cabModel != null && depthFg != null && mvecFg != null && gvec != null; } }

        void EnsureCabPsos()
        {
            if (psoCabA != null) return;
            long t = Clock.Now;
            psoCabA = Pso(CompileCs("fgcabezaa", CabShaders.A));
            psoCabM = Pso(CompileCs("fgcabezam", CabShaders.M));
            psoCabB = Pso(CompileCs("fgcabezab", CabShaders.B));
            psoCabC = Pso(CompileCs("fgcabezac", CabShaders.C));
            Log.Write("Generacion: shaders de la cabeza (" + Clock.SinceMs(t).ToString("0") + " ms)");
        }

        // RejasViews (al cambiar el tamaño de la entrada; luma, mvecFg, depthFg y gvec ya creadas)
        void FgCabezaViews()
        {
            if (cabBlk != null) Com.Release(ref cabBlk);
            if (cabBg != null) Com.Release(ref cabBg);
            if (cabCls != null) Com.Release(ref cabCls);
            if (cabModel != null) Com.Release(ref cabModel);
            for (int m = 0; m < 2; m++) if (cabMem[m] != null) Com.Release(ref cabMem[m]);
            cabBx = Math.Max(1, (lw[1] + 7) / 8); cabBy = Math.Max(1, (lh[1] + 7) / 8);
            cabBlk = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, cabBx, cabBy, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "cabeza: bloques");
            cabBg = Tex(DXGI_FORMAT.R32G32B32A32_FLOAT, cabBx, cabBy, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "cabeza: fondo por bloque");
            cabCls = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, cabBx, cabBy, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "cabeza: clases");
            cabModel = Tex(DXGI_FORMAT.R32G32B32A32_FLOAT, 1, 1, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "cabeza: camara (traslacion, acercamiento, giro)");
            for (int m = 0; m < 2; m++) cabMem[m] = Tex(DXGI_FORMAT.R16G16_FLOAT, cabBx, cabBy, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "cabeza: memoria " + m);
            cabMemCur = 0;
            for (int st = 0; st < 2; st++)
            {
                NullViews(TRCabA + st);
                SrvTex(TRCabA + st, 0, luma[st, 1], DXGI_FORMAT.R8_UNORM);
                SrvTex(TRCabA + st, 1, luma[1 - st, 1], DXGI_FORMAT.R8_UNORM);
                if (gvec != null) SrvTex(TRCabA + st, 2, gvec, DXGI_FORMAT.R32G32_FLOAT);
                SrvTex(TRCabA + st, 3, cabModel, DXGI_FORMAT.R32G32B32A32_FLOAT);
                UavTex(TRCabA + st, U0, cabBlk, DXGI_FORMAT.R16G16B16A16_FLOAT);
                UavTex(TRCabA + st, U0 + 1, cabBg, DXGI_FORMAT.R32G32B32A32_FLOAT);
                NullViews(TRCabC + st);
                SrvTex(TRCabC + st, 0, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(TRCabC + st, 1, luma[1 - st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(TRCabC + st, 3, cabCls, DXGI_FORMAT.R16G16B16A16_FLOAT);
                SrvTex(TRCabC + st, 4, cabModel, DXGI_FORMAT.R32G32B32A32_FLOAT);
                if (mvecFg != null) UavTex(TRCabC + st, U0, mvecFg, DXGI_FORMAT.R16G16_FLOAT);
                if (depthFg != null) UavTex(TRCabC + st, U0 + 1, depthFg, DXGI_FORMAT.R32_FLOAT);
                for (int m = 0; m < 2; m++)
                {
                    int tb = TRCabB + 2 * st + m;
                    NullViews(tb);
                    SrvTex(tb, 0, cabBlk, DXGI_FORMAT.R16G16B16A16_FLOAT);
                    SrvTex(tb, 1, cabMem[m], DXGI_FORMAT.R16G16_FLOAT);
                    SrvTex(tb, 2, luma[st, 1], DXGI_FORMAT.R8_UNORM);
                    SrvTex(tb, 3, luma[1 - st, 1], DXGI_FORMAT.R8_UNORM);
                    SrvTex(tb, 4, cabModel, DXGI_FORMAT.R32G32B32A32_FLOAT);
                    UavTex(tb, U0, cabCls, DXGI_FORMAT.R16G16B16A16_FLOAT);
                    UavTex(tb, U0 + 1, cabMem[1 - m], DXGI_FORMAT.R16G16_FLOAT);
                }
            }
            NullViews(TRCabM);
            SrvTex(TRCabM, 0, cabBg, DXGI_FORMAT.R32G32B32A32_FLOAT);
            if (gvec != null) SrvTex(TRCabM, 2, gvec, DXGI_FORMAT.R32G32_FLOAT);
            UavTex(TRCabM, U0, cabModel, DXGI_FORMAT.R32G32B32A32_FLOAT);
        }

        // RecordFgVec (EngineRejas.cs), imagen nueva con la anterior valida, tras RecordFgGlobal (gvec): mvecFg y depthFg
        void RecordFgCabeza(int cur)
        {
            EnsureCabPsos();
            uint* c = stackalloc uint[16];
            float sx = (float)lw[1] / Math.Max(1, lw[0]), sy = (float)lh[1] / Math.Max(1, lh[0]);
            float noise = (float)(s.FgCabNoise / 255.0);
            float cx = 0.5f * inW, cy = 0.5f * inH, half = 0.5f * (float)Math.Sqrt((double)inW * inW + (double)inH * inH);
            // A) por bloque del nivel 1: casi quieto / con la camara, y el fondo alrededor de la camara prevista
            Barrier(cabBlk, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            Barrier(cabBg, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)lw[1]; c[1] = (uint)lh[1]; c[2] = (uint)Math.Max(1, Math.Min(6, s.FgCabR)); c[3] = 2u;
            c[4] = F(sx); c[5] = F(sy); c[6] = F(noise); c[7] = F((float)s.FgCabRatio);
            c[8] = F((float)s.FgCabMinG); c[9] = F(1.25f); c[10] = F(cx); c[11] = F(cy);
            c[12] = F(half); c[13] = s.FgCabModel ? 1u : 0u;
            BindCompute(psoCabA, TRCabA + cur, c, 16);
            list.Dispatch((uint)cabBx, (uint)cabBy, 1);
            Barrier(cabBlk, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(cabBg, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            // M) la camara de esta imagen: traslacion + acercamiento + giro con los bloques del fondo fiables (dos vueltas)
            Barrier(cabModel, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)cabBx; c[1] = (uint)cabBy; c[2] = s.FgCabModel ? 1u : 0u;
            c[4] = F(cx); c[5] = F(cy); c[6] = F(8f / sx); c[7] = F(8f / sy);
            c[8] = F(1.5f * 1.5f);
            BindCompute(psoCabM, TRCabM, c, 12);
            list.Dispatch(1, 1, 1);
            Barrier(cabModel, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            // B) relleno de lo liso, lo que la camara explica igual y memoria
            int m = cabMemCur;
            Barrier(cabCls, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            Barrier(cabMem[1 - m], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)cabBx; c[1] = (uint)cabBy; c[2] = (uint)Math.Max(1, Math.Min(32, s.FgCabFill));
            c[4] = F(cx); c[5] = F(cy); c[6] = F(8f / sx); c[7] = F(8f / sy);
            c[8] = F(sx); c[9] = F(sy); c[10] = F(noise); c[11] = F(lw[1]); c[12] = F(lh[1]);
            BindCompute(psoCabB, TRCabB + 2 * cur + m, c, 16);
            list.Dispatch(Groups(cabBx, 8), Groups(cabBy, 8), 1);
            Barrier(cabCls, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(cabMem[1 - m], D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            cabMemCur = 1 - m;
            // C) por punto: vectores y profundidad de la generacion
            Barrier(mvecFg, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            Barrier(depthFg, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)cabBx; c[3] = (uint)cabBy;
            c[4] = F(sx / 8f); c[5] = F(sy / 8f); c[6] = F(noise); c[7] = F((float)s.FgCabMinG);
            c[8] = F((float)s.FgCabNear); c[9] = F((float)s.FgCabFar); c[10] = F((float)(s.FgQuietTex / 255.0)); c[11] = F((float)(s.FgQuietStill / 255.0));
            c[12] = (s.FgQuiet ? 1u : 0u) | ((uint)Math.Max(0, Math.Min(200, s.FgCabEdge)) << 8); c[13] = F((float)s.FgQuietRel); c[14] = F(cx); c[15] = F(cy);
            BindCompute(psoCabC, TRCabC + cur, c, 16);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(mvecFg, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(depthFg, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            if (!cabSaid)
            {
                cabSaid = true;
                Log.Write(string.Format(CultureInfo.InvariantCulture, "Generacion ({0}): el personaje separado del fondo (fg_cabeza=1: bloques {1}x{2}, busqueda local +-{3} px, " +
                    "lo casi quieto cerca {4:0.##} y lo que va con la camara lejos {5:0.##}; camara con acercamiento y giro {6}; fg_cabeza=0 = como antes)",
                    ffxFgOn ? "FSR 3 FG" : "XeSS FG", cabBx, cabBy, 2 * Math.Max(1, Math.Min(6, s.FgCabR)), s.FgCabNear, s.FgCabFar, s.FgCabModel ? "si" : "no (fg_cabeza_modelo=0)"));
            }
        }
    }

    static class CabShaders
    {
        // A) un grupo = un bloque de 8x8 del nivel 1. c0: ancho y alto del nivel 1, R (busqueda local), RB (busqueda del fondo) | c1 (float):
        // escala de la entrada al nivel 1 (x, y), ruido, ratio | c2: giro minimo (px de la entrada), radio excluido alrededor de la camara (px
        // del nivel 1), centro de la entrada | c3: media diagonal de la entrada (float), modelo (0/1).
        // La camara prevista en el bloque = el vector de la camara de ahora + el acercamiento / giro de la imagen anterior (Mdl).
        // Blk = (vector local en px de la entrada, puntuacion: +1 casi quieto, -1 con la camara, 0 no se sabe o la camara quieta, lo que deja)
        // Bg = (el fondo afinado alrededor de la camara prevista en px de la entrada, fiable para el ajuste 0/1, lo que deja)
        public const string A = @"
cbuffer C : register(b0) { uint LW, LH, R, RB; float SX, SY, Noise, Ratio; float MinG, ExR, CX, CY; float HalfDiag; uint Modelo, P2, P3; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float2> G : register(t2);
Texture2D<float4> Mdl : register(t3);
RWTexture2D<float4> Blk : register(u0);
RWTexture2D<float4> Bg : register(u1);
SamplerState Lin : register(s0);
groupshared float tc[64];
groupshared float tp[400];
groupshared float tb[196];
groupshared float ss[176];
groupshared float sb2[32];
groupshared float sg[64];

[numthreads(8, 8, 1)]
void main(uint3 gid : SV_GroupID, uint3 gt : SV_GroupThreadID, uint gi : SV_GroupIndex)
{
    int Ri = (int)R, Rb = (int)RB;
    int T = 8 + 2 * Ri, TB = 8 + 2 * (Rb + 1);
    int2 b0 = int2(gid.xy) * 8;
    int2 mx = int2((int)LW - 1, (int)LH - 1);
    float2 g = G.Load(int3(0, 0, 0));
    float4 md = Modelo != 0 ? Mdl.Load(int3(0, 0, 0)) : float4(0, 0, 0, 0);
    float2 dc = (float2(b0) + 4.0) / float2(SX, SY) - float2(CX, CY);
    float2 gb = g + md.z * dc + md.w * float2(-dc.y, dc.x);
    float2 g1 = gb * float2(SX, SY);
    bool haveG = length(g) + length(md.zw) * HalfDiag >= MinG;
    int2 pc = clamp(b0 + int2(gt.xy), int2(0, 0), mx);
    float cv = Cur.Load(int3(pc, 0));
    tc[gi] = cv;
    sg[gi] = abs(cv - Prev.SampleLevel(Lin, (float2(pc) + 0.5 + g1) / float2(LW, LH), 0));
    for (int k = (int)gi; k < T * T; k += 64)
    {
        int2 q = clamp(b0 - int2(Ri, Ri) + int2((uint)k % (uint)T, (uint)k / (uint)T), int2(0, 0), mx);
        tp[k] = Prev.Load(int3(q, 0));
    }
    int2 gi1 = int2(floor(g1));
    float2 gf = g1 - float2(gi1);
    for (int k2 = (int)gi; k2 < TB * TB; k2 += 64)
    {
        int2 q = clamp(b0 + gi1 - int2(Rb + 1, Rb + 1) + int2((uint)k2 % (uint)TB, (uint)k2 / (uint)TB), int2(0, 0), mx);
        tb[k2] = Prev.Load(int3(q, 0));
    }
    GroupMemoryBarrierWithGroupSync();
    int D = 2 * Ri + 1, DB = 2 * Rb + 1;
    for (int cI = (int)gi; cI < D * D; cI += 64)
    {
        int2 d = int2((uint)cI % (uint)D, (uint)cI / (uint)D) - int2(Ri, Ri);
        float s = 0;
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 8; i++)
                s += abs(tc[j * 8 + i] - tp[(j + d.y + Ri) * T + i + d.x + Ri]);
        ss[cI] = s * (1.0 / 64.0);
    }
    if ((int)gi < DB * DB)
    {
        int2 d = int2(gi % (uint)DB, gi / (uint)DB) - int2(Rb, Rb);
        float s = 0;
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 8; i++)
            {
                int x0 = i + d.x + Rb + 1, y0 = j + d.y + Rb + 1;
                float a = lerp(tb[y0 * TB + x0], tb[y0 * TB + x0 + 1], gf.x), b = lerp(tb[(y0 + 1) * TB + x0], tb[(y0 + 1) * TB + x0 + 1], gf.x);
                s += abs(tc[j * 8 + i] - lerp(a, b, gf.y));
            }
        sb2[gi] = s * (1.0 / 64.0);
    }
    GroupMemoryBarrierWithGroupSync();
    if (gi != 0) return;
    float sadG = 0;
    for (int t = 0; t < 64; t++) sadG += sg[t];
    sadG *= 1.0 / 64.0;
    // con la camara moviendose, sin los vectores pegados a la camara (eso ya lo mide sadG): para la clase, a < ExR px del nivel 1; para
    // el vector del bloque, solo el punto de la camara (a < 0,75: el balanceo de 1-2 px junto a un giro lento no se pierde)
    int best = Ri * D + Ri; float sb = 1e9, sv = 1e9;
    for (int c2 = 0; c2 < D * D; c2++)
    {
        float dist = length(float2((int)((uint)c2 % (uint)D) - Ri, (int)((uint)c2 / (uint)D) - Ri) - g1);
        float e = ss[c2];
        if ((!haveG || dist >= ExR) && e < sb) sb = e;
        if ((!haveG || dist >= 0.75) && e < sv) { sv = e; best = c2; }
    }
    int bx = (int)((uint)best % (uint)D), by = (int)((uint)best / (uint)D);
    float2 v = float2(bx - Ri, by - Ri);
    if (bx > 0 && bx < D - 1)
    {
        float l = ss[best - 1], r = ss[best + 1], a = l - 2 * sv + r;
        if (a > 1e-6) v.x += clamp(0.5 * (l - r) / a, -0.5, 0.5);
    }
    if (by > 0 && by < D - 1)
    {
        float u = ss[best - D], dn = ss[best + D], a = u - 2 * sv + dn;
        if (a > 1e-6) v.y += clamp(0.5 * (u - dn) / a, -0.5, 0.5);
    }
    float score = 0;
    if (haveG && sb < 1e8)
    {
        if (max(sadG, sb) < 1.5 * Noise) score = 0;                                     // liso: los dos casan
        else if (sb < Ratio * sadG && sadG - sb > Noise) score = 1;                    // casi quieto (personaje, interfaz)
        else if (sadG < Ratio * sb && sb - sadG > Noise) score = -1;                   // con la camara
        else if (sadG <= sb) score = -0.5;                                              // mas bien con la camara
    }
    Blk[int2(gid.xy)] = float4(v / float2(SX, SY), score, sb);
    // el fondo: lo mejor de +-RB alrededor de la camara prevista, con medio punto; fiable si va con la camara, el minimo esta dentro, hay
    // textura (lo peor deja bastante mas) y casa
    int bb = Rb * DB + Rb; float sbb = sb2[bb], worst = 0;
    for (int c3 = 0; c3 < DB * DB; c3++) { worst = max(worst, sb2[c3]); if (sb2[c3] < sbb) { sbb = sb2[c3]; bb = c3; } }
    int ibx = (int)((uint)bb % (uint)DB), iby = (int)((uint)bb / (uint)DB);
    float2 db = float2(ibx - Rb, iby - Rb);
    bool inside = ibx > 0 && ibx < DB - 1 && iby > 0 && iby < DB - 1;
    if (inside)
    {
        float l = sb2[bb - 1], r = sb2[bb + 1], a = l - 2 * sbb + r;
        if (a > 1e-6) db.x += clamp(0.5 * (l - r) / a, -0.5, 0.5);
        float u = sb2[bb - DB], dn = sb2[bb + DB], a2 = u - 2 * sbb + dn;
        if (a2 > 1e-6) db.y += clamp(0.5 * (u - dn) / a2, -0.5, 0.5);
    }
    float w = score <= -0.5 && inside && worst - sbb > 2 * Noise && sbb < 3 * Noise ? 1 : 0;
    Bg[int2(gid.xy)] = float4((g1 + db) / float2(SX, SY), w, sbb);
}";

        // M) un grupo de 1024: la camara de esta imagen, v = t + s (p - c) + r (p - c) girado 90 grados, por minimos cuadrados con los bloques
        // del fondo fiables (Bg.z), y otra vuelta sin los que se separan mas de sqrt(Out2) px. c0: bloques x, y, modelo (0/1) | c1 (float):
        // centro, tamaño del bloque en px de la entrada | c2.x: Out2. Mdl = (t, s, r) en px de la entrada; si no hay bastantes, (camara, 0, 0)
        public const string M = @"
cbuffer C : register(b0) { uint BX, BY, Modelo, P0; float CX, CY, BSX, BSY; float Out2, Q0, Q1, Q2; uint4 Pad3; }
Texture2D<float4> Bg : register(t0);
Texture2D<float2> G : register(t2);
RWTexture2D<float4> Mdl : register(u0);
groupshared float red[1024];

float Sum(uint t, float v)
{
    red[t] = v;
    GroupMemoryBarrierWithGroupSync();
    for (uint s = 512; s > 0; s >>= 1)
    {
        if (t < s) red[t] += red[t + s];
        GroupMemoryBarrierWithGroupSync();
    }
    float r = red[0];
    GroupMemoryBarrierWithGroupSync();
    return r;
}

[numthreads(1024, 1, 1)]
void main(uint3 gt : SV_GroupThreadID)
{
    uint t = gt.x;
    float2 g = G.Load(int3(0, 0, 0));
    float4 m = float4(g, 0, 0);
    bool ok = false, go = Modelo != 0;
    // (las sumas, con su sincronizacion, siempre en los dos pasos: sin saltos que dependan de los datos alrededor de ellas)
    [unroll] for (int it = 0; it < 2; it++)
    {
        float n = 0, sX = 0, sY = 0, sR2 = 0, svx = 0, svy = 0, sa = 0, sb = 0;
        for (uint k = t; k < BX * BY; k += 1024)
        {
            float4 b = Bg.Load(int3(k % BX, k / BX, 0));
            // (en miles de px: sin perder precision en las sumas)
            float2 X = ((float2(k % BX, k / BX) + 0.5) * float2(BSX, BSY) - float2(CX, CY)) * 0.001;
            float2 e = m.xy + m.z * X + m.w * float2(-X.y, X.x) - b.xy;
            if (b.z <= 0 || (it == 1 && dot(e, e) > Out2)) continue;
            n += 1; sX += X.x; sY += X.y; sR2 += dot(X, X);
            svx += b.x; svy += b.y; sa += X.x * b.x + X.y * b.y; sb += X.x * b.y - X.y * b.x;
        }
        n = Sum(t, n); sX = Sum(t, sX); sY = Sum(t, sY); sR2 = Sum(t, sR2);
        svx = Sum(t, svx); svy = Sum(t, svy); sa = Sum(t, sa); sb = Sum(t, sb);
        // ecuaciones normales resueltas a mano: la escala y el giro con lo centrado, luego la traslacion
        float den = sR2 - (sX * sX + sY * sY) / max(n, 1.0);
        go = go && n >= 40 && den >= 0.01 * n;
        if (go)
        {
            float sc = (sa - (sX * svx + sY * svy) / n) / den;
            float rt = (sb - (sX * svy - sY * svx) / n) / den;
            m = float4((svx - sX * sc + sY * rt) / n, (svy - sY * sc - sX * rt) / n, sc, rt);
            ok = true;
        }
    }
    // (escala y giro por px; si el ajuste no sale, el vector de la camara sin mas)
    if (t == 0) Mdl[int2(0, 0)] = ok ? float4(m.xy, m.z * 0.001, m.w * 0.001) : float4(g, 0, 0);
}";

        // B) un hilo = un bloque. c0: bloques x, y, rodeo (bloques) | c1 (float): centro de la entrada, tamaño del bloque en px de la entrada |
        // c2 (float): escala de la entrada al nivel 1 (x, y), ruido, ancho del nivel 1 | c3.x: alto del nivel 1.
        // Cls = (vector de lo casi quieto en px de la entrada, clase 0/1, puntuacion); Mem (x): la clase de la imagen anterior; MemOut, la de esta
        public const string B = @"
cbuffer C : register(b0) { uint BX, BY, RF, P0; float CX, CY, BSX, BSY; float SX, SY, Noise, LW; float LH, Q0, Q1, Q2; }
Texture2D<float4> Blk : register(t0);
Texture2D<float2> Mem : register(t1);
Texture2D<float> Cur : register(t2);
Texture2D<float> Prev : register(t3);
Texture2D<float4> Mdl : register(t4);
RWTexture2D<float4> Cls : register(u0);
RWTexture2D<float2> MemOut : register(u1);
SamplerState Lin : register(s0);

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    int2 p = int2(id.xy);
    int2 mx = int2((int)BX - 1, (int)BY - 1);
    float4 b = Blk.Load(int3(p, 0));
    float mem = Mem.Load(int3(p, 0)).x;
    float s = b.z;
    float cls;
    float2 v = b.xy;
    bool noInfo = true;
    if (s >= 0.5)
    {
        // la camara de esta imagen (con su acercamiento / giro) explica el bloque igual que lo local: no es el personaje (un acercamiento)
        float4 md = Mdl.Load(int3(0, 0, 0));
        float2 dc = (float2(p) + 0.5) * float2(BSX, BSY) - float2(CX, CY);
        float2 mb = md.xy + md.z * dc + md.w * float2(-dc.y, dc.x);
        if (length(mb - b.xy) < 3.0)
        {
            float sm = 0, sl = 0;
            float2 inv = 1.0 / float2(LW, LH);
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 8; i++)
                {
                    float2 q = float2(p * 8 + int2(i, j)) + 0.5;
                    float cv = Cur.Load(int3(min(p * 8 + int2(i, j), int2((int)LW - 1, (int)LH - 1)), 0));
                    sm += abs(cv - Prev.SampleLevel(Lin, (q + mb * float2(SX, SY)) * inv, 0));
                    sl += abs(cv - Prev.SampleLevel(Lin, (q + b.xy * float2(SX, SY)) * inv, 0));
                }
            if (sm <= sl + 32.0 * Noise) s = -1;
        }
    }
    if (s >= 0.5)
    {
        cls = 1; noInfo = false;
        // suelto en medio de lo que va con la camara: un dibujo del fondo que casa por casualidad
        int nc = 0;
        [unroll] for (int j = -1; j <= 1; j++)
            [unroll] for (int i = -1; i <= 1; i++)
                if (i != 0 || j != 0) { float z = Blk.Load(int3(clamp(p + int2(i, j), int2(0, 0), mx), 0)).z; if (z <= -0.5) nc++; }
        if (nc >= 6) cls = 0;
    }
    else if (s <= -0.5) { cls = 0; noInfo = false; }
    else
    {
        // liso: lo que haya en 4 direcciones
        int nL = 0, nC = 0;
        float2 sv = float2(0, 0);
        [unroll] for (int d = 0; d < 4; d++)
        {
            int2 dir = d == 0 ? int2(1, 0) : d == 1 ? int2(-1, 0) : d == 2 ? int2(0, 1) : int2(0, -1);
            for (int k = 1; k <= (int)RF; k++)
            {
                int2 q = p + dir * k;
                if (q.x < 0 || q.y < 0 || q.x > mx.x || q.y > mx.y) break;
                float4 o = Blk.Load(int3(q, 0));
                if (o.z >= 0.5) { nL++; sv += o.xy; break; }
                if (o.z <= -0.5) { nC++; break; }
            }
        }
        if (nL + nC == 0) cls = mem > 0.5 ? 1 : 0;
        else
        {
            // lo que ya era casi quieto lo sigue siendo salvo que lo rodee lo que va con la camara (3 de 4); lo que no, si lo rodea lo
            // casi quieto (3 de 4). Asi el personaje no se pierde en los giros lentos (lo liso casa con los dos)
            noInfo = false;
            cls = mem > 0.5 ? (nC >= 3 ? 0 : 1) : (nL >= 3 ? 1 : 0);
            if (nL > 0) v = sv / (float)nL;
        }
    }
    Cls[p] = float4(v, cls, s);
    MemOut[p] = float2(noInfo ? mem : cls, 0);
}";

        // C) por punto de la entrada. c0: ancho, alto, bloques x, y | c1 (float): de la entrada a la rejilla de bloques (x, y), ruido, giro
        // minimo | c2 (float): cerca, lejos, textura minima y diferencia maxima de lo quieto (fg_quieto) | c3: fg_quieto (0/1), su proporcion,
        // centro de la entrada. El fondo con la camara de esta imagen (Mdl: traslacion + acercamiento + giro)
        public const string C = @"
cbuffer C : register(b0) { uint W, H, BX, BY; float KX, KY, Noise, MinG; float Near, Far, TexT, StillT; uint Quiet; float QRatio, CX, CY; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float4> Cls : register(t3);
Texture2D<float4> Mdl : register(t4);
RWTexture2D<float2> Out : register(u0);
RWTexture2D<float> Dep : register(u1);
SamplerState Lin : register(s0);
groupshared float tc[180];
groupshared float tp[180];
groupshared float tg[180];

float4 CB(int2 b) { return Cls.Load(int3(clamp(b, int2(0, 0), int2((int)BX - 1, (int)BY - 1)), 0)); }

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID, uint3 gt : SV_GroupThreadID, uint3 gid : SV_GroupID, uint gi : SV_GroupIndex)
{
    float4 md = Mdl.Load(int3(0, 0, 0));
    int2 o = int2(gid.xy) * int2(16, 8) - 1;
    float2 inv = 1.0 / float2(W, H);
    for (uint k = gi; k < 180; k += 128)
    {
        int2 q = clamp(o + int2(k % 18, k / 18), int2(0, 0), int2((int)W - 1, (int)H - 1));
        float2 dq = float2(q) + 0.5 - float2(CX, CY);
        float2 mq = md.xy + md.z * dq + md.w * float2(-dq.y, dq.x);
        tc[k] = Cur.Load(int3(q, 0));
        tp[k] = Prev.Load(int3(q, 0));
        tg[k] = Prev.SampleLevel(Lin, (float2(q) + 0.5 + mq) * inv, 0);
    }
    GroupMemoryBarrierWithGroupSync();
    if (id.x >= W || id.y >= H) return;
    float2 dp = float2(id.xy) + 0.5 - float2(CX, CY);
    float2 g = md.xy + md.z * dp + md.w * float2(-dp.y, dp.x);
    bool haveG = dot(g, g) >= MinG * MinG;
    float2 bp = (float2(id.xy) + 0.5) * float2(KX, KY);
    int2 b = int2(floor(bp));
    float4 own = CB(b);
    float mn = own.z, mxc = own.z;
    float2 sL = float2(0, 0);
    float nL = 0;
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            float4 n = CB(b + int2(i, j));
            mn = min(mn, n.z); mxc = max(mxc, n.z);
            if (n.z > 0.5) { sL += n.xy; nL += 1; }
        }
    // el vector de lo casi quieto: la media de los bloques casi quietos de alrededor (sin saltos de bloque en el personaje)
    float2 vL = nL > 0 ? sL / nL : own.xy;
    float edgeMax = (float)(Quiet >> 8) * Noise;
    if (edgeMax > 0)
    {
        // frontera mas ancha (5x5 bloques): el fondo que destapa el personaje (y el de los rincones entre cabeza y hombros) cae en bloques
        // casi quietos; ahi cada punto se mira por su cuenta
        [unroll] for (int j5 = -2; j5 <= 2; j5++)
            [unroll] for (int i5 = -2; i5 <= 2; i5 += 4) { float z5 = CB(b + int2(i5, j5)).z; mn = min(mn, z5); mxc = max(mxc, z5); }
        [unroll] for (int i6 = -1; i6 <= 1; i6++)
            [unroll] for (int j6 = -2; j6 <= 2; j6 += 4) { float z6 = CB(b + int2(i6, j6)).z; mn = min(mn, z6); mxc = max(mxc, z6); }
    }
    bool near;
    if (mxc - mn < 0.5) near = own.z > 0.5;
    else
    {
        // frontera: cada punto elige por su 3x3 (empate: la clase de los bloques, bilineal entre sus centros)
        float eL = 0, eC = 0;
        [unroll] for (int j2 = 0; j2 < 3; j2++)
            [unroll] for (int i2 = 0; i2 < 3; i2++)
            {
                uint k2 = (gt.y + j2) * 18 + gt.x + i2;
                int2 q = clamp(int2(id.xy) + int2(i2 - 1, j2 - 1), int2(0, 0), int2((int)W - 1, (int)H - 1));
                eL += abs(tc[k2] - Prev.SampleLevel(Lin, (float2(q) + 0.5 + vL) * inv, 0));
                eC += abs(tc[k2] - tg[k2]);
            }
        float2 c0 = bp - 0.5;
        int2 f0 = int2(floor(c0));
        float2 fr = c0 - float2(f0);
        float cs = lerp(lerp(CB(f0).z, CB(f0 + int2(1, 0)).z, fr.x), lerp(CB(f0 + int2(0, 1)).z, CB(f0 + int2(1, 1)).z, fr.x), fr.y);
        // (con fg_cabeza_borde, ademas tiene que casar de verdad: el fondo recien destapado no casa con ninguno de los dos y va lejos, con
        // el vector de la camara: la generacion ve por la profundidad que antes lo tapaba el personaje)
        bool fits = edgeMax <= 0 || eL < edgeMax;
        if (eL + 9.0 * Noise < eC) near = fits;
        else if (eC + 9.0 * Noise < eL) near = false;
        else near = cs > 0.5 && fits;
    }
    float2 v = near ? vL : g;
    // lo casi quieto que se mueve mucho (mas de 2 px y de media camara: un acercamiento, no el personaje) lleva su vector pero va LEJOS
    bool lejos = near && dot(vL, vL) > max(4.0, 0.25 * dot(g, g));
    if (lejos) near = false;
    if (!near && !lejos && (Quiet & 1) != 0 && haveG)
    {
        // lo quieto con textura dentro de lo que va con la camara (letras de la interfaz): a 0 y delante, como fg_quieto
        float s0 = 0, sgs = 0, lo = 1, hi = 0;
        [unroll] for (int j3 = 0; j3 < 3; j3++)
            [unroll] for (int i3 = 0; i3 < 3; i3++)
            {
                uint k3 = (gt.y + j3) * 18 + gt.x + i3;
                float cv = tc[k3];
                lo = min(lo, cv); hi = max(hi, cv);
                s0 += abs(cv - tp[k3]); sgs += abs(cv - tg[k3]);
            }
        if (hi - lo >= TexT && s0 <= 9.0 * StillT && s0 < QRatio * sgs) { v = float2(0, 0); near = true; }
    }
    Out[id.xy] = v;
    Dep[id.xy] = near ? Near : Far;
}";
    }
}
