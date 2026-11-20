// HLSL de los pasos propios (compute, cs_5_1). Se compilan al arrancar con D3DCompile y se guardan en cache\.
//
// ConsoleEnhancer, GPL-3.0 (LICENSE, LICENSE-EXCEPTION.txt). Codigo de terceros en este fichero (ver THIRD-PARTY.md):
//  - Easu y Rcas (en Cas): port a HLSL de FsrEasuF / FsrRcasF de ffx_fsr1.h (AMD FidelityFX Super Resolution 1,
//    https://github.com/GPUOpen-Effects/FidelityFX-FSR). Copyright (c) 2021 Advanced Micro Devices, Inc. All rights reserved.
//  - Cas: AMD FidelityFX Contrast Adaptive Sharpening (ffx_cas.h, https://github.com/GPUOpen-Effects/FidelityFX-CAS).
//    Copyright (c) 2020 Advanced Micro Devices, Inc. All rights reserved.
//    Las dos con esta licencia (MIT):
//    Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files
//    (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify,
//    merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished
//    to do so, subject to the following conditions:
//    The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
//    FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
//    CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//  - Aa: la parte FXAA sigue NVIDIA FXAA 3.11 (Timothy Lottes). Copyright (c) 2014-2015, NVIDIA CORPORATION. All rights reserved.
//    BSD-3-Clause (https://github.com/NVIDIAGameWorks/GraphicsSamples): texto completo en scripts\s2_aa.glsl y THIRD-PARTY.md.
namespace ConsoleEnhancer.Motor
{
    static class Shaders
    {
        // Formato del buffer de captura subido (ya de arriba abajo, filas de 'pitch' bytes, multiplo de 4)
        public const int FmtBgr24 = 0, FmtBgrx32 = 1, FmtYuy2 = 2, FmtNv12 = 3;

        // Root signature comun a todos: b0 = 16 constantes de 32 bits, t0..t7 = SRV, u0..u1 = UAV, s0 = lineal clamp
        // (tablas de 10 descriptores: SRV en 0-7, UAV en 8-9; 20-11, DLSS 5 EN EL MOTOR: antes 4 SRV, el antiparpadeo de DLSS 5 lee 6)
        public const int TableSize = 10, SlotU0 = 8;
        public const string RootSig =
            "RootFlags(0), RootConstants(num32BitConstants=16, b0), " +
            "DescriptorTable(SRV(t0, numDescriptors=8), UAV(u0, numDescriptors=2)), " +
            "StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_LINEAR, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP)";

        // ---- Captura -> RGBA. Un hilo = 4 pixeles en horizontal (12 bytes en BGR24: siempre alineado a 4) ----
        // c0: ancho, alto, pitch (bytes), formato | c1: rango limitado (yuv), matriz (0 = 709, 1 = 601), offsetUV (nv12), dedupe
        public const string Unpack = @"
cbuffer C : register(b0) { uint W, H, Pitch, Fmt; uint Limited, Mat601, UvOffset, Pad0; uint4 Pad1; uint4 Pad2; }
ByteAddressBuffer Src : register(t0);
RWTexture2D<unorm float4> Dst : register(u0);

float3 Yuv(float y, float u, float v)
{
    if (Limited) { y = (y - 16.0 / 255.0) * (255.0 / 219.0); u = (u - 128.0 / 255.0) * (255.0 / 224.0); v = (v - 128.0 / 255.0) * (255.0 / 224.0); }
    else { u -= 128.0 / 255.0; v -= 128.0 / 255.0; }
    float3 c = Mat601 ? float3(y + 1.402 * v, y - 0.344136 * u - 0.714136 * v, y + 1.772 * u)
                      : float3(y + 1.5748 * v, y - 0.187324 * u - 0.468124 * v, y + 1.8556 * u);
    return saturate(c);
}
float B(uint w, uint i) { return ((w >> (8 * i)) & 0xFF) / 255.0; }

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint x0 = id.x * 4, y = id.y + Pad0;   // (20-11 LATENCIA7: Pad0 = primera fila, por bandas; H = la ultima + 1)
    if (x0 >= W || y >= H) return;
    uint row = y * Pitch;
    if (Fmt == 0)
    {
        uint3 d = Src.Load3(row + id.x * 12);
        // bytes: B0 G0 R0 B1 | G1 R1 B2 G2 | R2 B3 G3 R3
        Dst[uint2(x0 + 0, y)] = float4(B(d.x, 2), B(d.x, 1), B(d.x, 0), 1);
        if (x0 + 1 < W) Dst[uint2(x0 + 1, y)] = float4(B(d.y, 1), B(d.y, 0), B(d.x, 3), 1);
        if (x0 + 2 < W) Dst[uint2(x0 + 2, y)] = float4(B(d.z, 0), B(d.y, 3), B(d.y, 2), 1);
        if (x0 + 3 < W) Dst[uint2(x0 + 3, y)] = float4(B(d.z, 3), B(d.z, 2), B(d.z, 1), 1);
    }
    else if (Fmt == 1)
    {
        uint4 d = Src.Load4(row + id.x * 16);
        [unroll] for (uint i = 0; i < 4; i++)
        {
            uint p = i == 0 ? d.x : i == 1 ? d.y : i == 2 ? d.z : d.w;
            if (x0 + i < W) Dst[uint2(x0 + i, y)] = float4(B(p, 2), B(p, 1), B(p, 0), 1);
        }
    }
    else if (Fmt == 2)
    {
        uint2 d = Src.Load2(row + id.x * 8); // Y0 U Y1 V | Y2 U Y3 V
        Dst[uint2(x0 + 0, y)] = float4(Yuv(B(d.x, 0), B(d.x, 1), B(d.x, 3)), 1);
        if (x0 + 1 < W) Dst[uint2(x0 + 1, y)] = float4(Yuv(B(d.x, 2), B(d.x, 1), B(d.x, 3)), 1);
        if (x0 + 2 < W) Dst[uint2(x0 + 2, y)] = float4(Yuv(B(d.y, 0), B(d.y, 1), B(d.y, 3)), 1);
        if (x0 + 3 < W) Dst[uint2(x0 + 3, y)] = float4(Yuv(B(d.y, 2), B(d.y, 1), B(d.y, 3)), 1);
    }
    else
    {
        uint yw = Src.Load(row + x0);                          // Y0..Y3
        uint uv = Src.Load(UvOffset + (y / 2) * Pitch + x0);   // U0 V0 U1 V1
        Dst[uint2(x0 + 0, y)] = float4(Yuv(B(yw, 0), B(uv, 0), B(uv, 1)), 1);
        if (x0 + 1 < W) Dst[uint2(x0 + 1, y)] = float4(Yuv(B(yw, 1), B(uv, 0), B(uv, 1)), 1);
        if (x0 + 2 < W) Dst[uint2(x0 + 2, y)] = float4(Yuv(B(yw, 2), B(uv, 2), B(uv, 3)), 1);
        if (x0 + 3 < W) Dst[uint2(x0 + 3, y)] = float4(Yuv(B(yw, 3), B(uv, 2), B(uv, 3)), 1);
    }
}";

        // ---- Escalado sin DLSS (captura 4K, fallo de DLSS, o 'ninguno'): Catmull-Rom con 9 lecturas bilineales ----
        // c0: ancho/alto destino, c1 (float): 1/ancho y 1/alto de la fuente, ancho/alto fuente
        public const string Scale = @"
cbuffer C : register(b0) { uint DW, DH, P0, P1; float4 Src; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> In : register(t0);
RWTexture2D<unorm float4> Out : register(u0);
SamplerState Lin : register(s0);

float4 CatRom(float2 uv)
{
    float2 texSize = Src.zw, inv = Src.xy;
    float2 samplePos = uv * texSize;
    float2 tc1 = floor(samplePos - 0.5) + 0.5;
    float2 f = samplePos - tc1;
    float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    float2 w3 = f * f * (-0.5 + 0.5 * f);
    float2 w12 = w1 + w2;
    float2 off12 = w2 / w12;
    float2 tc0 = (tc1 - 1) * inv, tc3 = (tc1 + 2) * inv, tc12 = (tc1 + off12) * inv;
    float4 r = 0;
    r += In.SampleLevel(Lin, float2(tc0.x, tc0.y), 0) * w0.x * w0.y;
    r += In.SampleLevel(Lin, float2(tc12.x, tc0.y), 0) * w12.x * w0.y;
    r += In.SampleLevel(Lin, float2(tc3.x, tc0.y), 0) * w3.x * w0.y;
    r += In.SampleLevel(Lin, float2(tc0.x, tc12.y), 0) * w0.x * w12.y;
    r += In.SampleLevel(Lin, float2(tc12.x, tc12.y), 0) * w12.x * w12.y;
    r += In.SampleLevel(Lin, float2(tc3.x, tc12.y), 0) * w3.x * w12.y;
    r += In.SampleLevel(Lin, float2(tc0.x, tc3.y), 0) * w0.x * w3.y;
    r += In.SampleLevel(Lin, float2(tc12.x, tc3.y), 0) * w12.x * w3.y;
    r += In.SampleLevel(Lin, float2(tc3.x, tc3.y), 0) * w3.x * w3.y;
    return saturate(r);
}

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= DW || id.y >= DH) return;
    float2 uv = (float2(id.xy) + 0.5) / float2(DW, DH);
    Out[id.xy] = float4(CatRom(uv).rgb, 1);
}";

        // ---- 18-11 (MOTOR-CE): FSR 1 EASU (AMD FidelityFX Super Resolution 1, "Edge Adaptive Spatial Upsampling") ----
        // Copyright (c) 2021 Advanced Micro Devices, Inc. Licencia MIT (se puede usar, copiar y cambiar dejando este aviso; sin
        // garantia). Es FsrEasuF de ffx_fsr1.h (version de 32 bits) en compute: las 12 lecturas (b c / e f g h / i j k l / n o) con
        // Load en vez de Gather (mismo resultado: bordes con clamp, como el muestreador de FSR). Para cualquier tamaño de entrada y
        // salida (ampliando; el motor usa Catmull-Rom si la entrada es mayor). La entrada es la captura en gamma (como pide FSR 1).
        // c0: ancho/alto destino, ancho/alto fuente | c1 (float): escala (fuente/destino) y desplazamiento (0,5 escala - 0,5)
        public const string Easu = @"
cbuffer C : register(b0) { uint DW, DH, SW, SH; float4 Con0; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> In : register(t0);
RWTexture2D<unorm float4> Out : register(u0);

float3 Px(int2 p) { return In.Load(int3(clamp(p, int2(0, 0), int2(SW - 1, SH - 1)), 0)).rgb; }
// Luma x2 aproximada (2 FMA), como FSR
float Lum2(float3 c) { return c.b * 0.5 + (c.r * 0.5 + c.g); }
// APrxLoRcpF1 / APrxLoRsqF1 de ffx_a.h
float RcpLo(float a) { return asfloat(0x7ef07ebbu - asuint(a)); }
float RsqLo(float a) { return asfloat(0x5f347d74u - (asuint(a) >> 1)); }

// Un punto del filtro: lanczos 2 aproximado, girado segun el borde y estirado (anisotropo)
void EasuTap(inout float3 aC, inout float aW, float2 off, float2 dir, float2 len, float lob, float clp, float3 c)
{
    float2 v;
    v.x = (off.x * dir.x) + (off.y * dir.y);
    v.y = (off.x * (-dir.y)) + (off.y * dir.x);
    v *= len;
    float d2 = v.x * v.x + v.y * v.y;
    d2 = min(d2, clp);
    float wB = (2.0 / 5.0) * d2 - 1.0;
    float wA = lob * d2 - 1.0;
    wB *= wB;
    wA *= wA;
    wB = (25.0 / 16.0) * wB - (25.0 / 16.0 - 1.0);
    float w = wB * wA;
    aC += c * w;
    aW += w;
}

// Direccion y longitud del borde en una cruz (a arriba, b izquierda, c centro, d derecha, e abajo), con su peso bilineal
void EasuSet(inout float2 dir, inout float len, float w, float lA, float lB, float lC, float lD, float lE)
{
    float dc = lD - lC, cb = lC - lB;
    float lenX = max(abs(dc), abs(cb));
    lenX = RcpLo(lenX);
    float dirX = lD - lB;
    dir.x += dirX * w;
    lenX = saturate(abs(dirX) * lenX);
    lenX *= lenX;
    len += lenX * w;
    float ec = lE - lC, ca = lC - lA;
    float lenY = max(abs(ec), abs(ca));
    lenY = RcpLo(lenY);
    float dirY = lE - lA;
    dir.y += dirY * w;
    lenY = saturate(abs(dirY) * lenY);
    lenY *= lenY;
    len += lenY * w;
}

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= DW || id.y >= DH) return;
    float2 pp = float2(id.xy) * Con0.xy + Con0.zw;
    float2 fp = floor(pp);
    pp -= fp;
    int2 q = int2(fp);
    //    b c
    //  e f g h
    //  i j k l
    //    n o
    float3 b = Px(q + int2(0, -1)), c = Px(q + int2(1, -1));
    float3 e = Px(q + int2(-1, 0)), f = Px(q), g = Px(q + int2(1, 0)), h = Px(q + int2(2, 0));
    float3 i = Px(q + int2(-1, 1)), j = Px(q + int2(0, 1)), k = Px(q + int2(1, 1)), l = Px(q + int2(2, 1));
    float3 n = Px(q + int2(0, 2)), o = Px(q + int2(1, 2));
    float bL = Lum2(b), cL = Lum2(c), eL = Lum2(e), fL = Lum2(f), gL = Lum2(g), hL = Lum2(h);
    float iL = Lum2(i), jL = Lum2(j), kL = Lum2(k), lL = Lum2(l), nL = Lum2(n), oL = Lum2(o);
    float2 dir = float2(0, 0);
    float len = 0;
    EasuSet(dir, len, (1.0 - pp.x) * (1.0 - pp.y), bL, eL, fL, gL, jL);
    EasuSet(dir, len, pp.x * (1.0 - pp.y), cL, fL, gL, hL, kL);
    EasuSet(dir, len, (1.0 - pp.x) * pp.y, fL, iL, jL, kL, nL);
    EasuSet(dir, len, pp.x * pp.y, gL, jL, kL, lL, oL);
    // Normalizar la direccion (aproximado) y limpiar cerca de 0
    float2 dir2 = dir * dir;
    float dirR = dir2.x + dir2.y;
    bool zro = dirR < (1.0 / 32768.0);
    dirR = RsqLo(dirR);
    dirR = zro ? 1.0 : dirR;
    dir.x = zro ? 1.0 : dir.x;
    dir *= dirR;
    // {0..2} -> {0..1} y al cuadrado
    len = len * 0.5;
    len *= len;
    // Estirar el nucleo: 1 en horizontal/vertical, raiz de 2 en diagonal
    float stretch = (dir.x * dir.x + dir.y * dir.y) * RcpLo(max(abs(dir.x), abs(dir.y)));
    float2 len2 = float2(1.0 + (stretch - 1.0) * len, 1.0 - 0.5 * len);
    float lob = 0.5 + ((1.0 / 4.0 - 0.04) - 0.5) * len;
    float clp = RcpLo(lob);
    // Sin anillos: dentro del minimo y maximo de los 4 mas cercanos
    float3 min4 = min(min(f, g), min(j, k));
    float3 max4 = max(max(f, g), max(j, k));
    float3 aC = float3(0, 0, 0);
    float aW = 0;
    EasuTap(aC, aW, float2( 0.0, -1.0) - pp, dir, len2, lob, clp, b);
    EasuTap(aC, aW, float2( 1.0, -1.0) - pp, dir, len2, lob, clp, c);
    EasuTap(aC, aW, float2(-1.0,  1.0) - pp, dir, len2, lob, clp, i);
    EasuTap(aC, aW, float2( 0.0,  1.0) - pp, dir, len2, lob, clp, j);
    EasuTap(aC, aW, float2( 0.0,  0.0) - pp, dir, len2, lob, clp, f);
    EasuTap(aC, aW, float2(-1.0,  0.0) - pp, dir, len2, lob, clp, e);
    EasuTap(aC, aW, float2( 1.0,  1.0) - pp, dir, len2, lob, clp, k);
    EasuTap(aC, aW, float2( 2.0,  1.0) - pp, dir, len2, lob, clp, l);
    EasuTap(aC, aW, float2( 2.0,  0.0) - pp, dir, len2, lob, clp, h);
    EasuTap(aC, aW, float2( 1.0,  0.0) - pp, dir, len2, lob, clp, g);
    EasuTap(aC, aW, float2( 1.0,  2.0) - pp, dir, len2, lob, clp, o);
    EasuTap(aC, aW, float2( 0.0,  2.0) - pp, dir, len2, lob, clp, n);
    float3 pix = min(max4, max(min4, aC / aW));
    Out[id.xy] = float4(pix, 1);
}";

        // ---- CAS (AMD FidelityFX Contrast Adaptive Sharpening, MIT) a la resolucion de salida ----
        // c0: ancho, alto, -, - | c1.x (float): nitidez 0..1 (18-11: c1.w = 1, RCAS de FSR 1 en vez de CAS)
        public const string Cas = @"
cbuffer C : register(b0) { uint W, H, P0, P1; float4 S; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> In : register(t0);
RWTexture2D<unorm float4> Out : register(u0);

float3 L(int2 p) { return In.Load(int3(clamp(p, int2(0, 0), int2(W - 1, H - 1)), 0)).rgb; }

// 18-11 (MOTOR-CE): RCAS de FSR 1 (AMD FidelityFX, Copyright (c) 2021 Advanced Micro Devices, Inc., licencia MIT), FsrRcasF de
// ffx_fsr1.h sin el quitar ruido opcional: la cruz de 5 puntos con el lobulo negativo que no se sale de lo que permite el anillo.
// S.w = 1. Fuerza: la del CAS al mismo sharp= (el mismo lobulo maximo, 1/lerp(8,5,sharp), frente al 0,1875 de RCAS; tope 1 = 0 stops,
// el maximo de FSR). Medido el 18-11 (Ocarina 1440p -> 4K, gradiente medio de la salida): EASU sin enfoque 2,44, EASU + RCAS lineal 0,3
// 2,52 (casi nada), EASU + CAS 0,3 3,88, Catmull-Rom + CAS 0,3 3,83, EASU + RCAS 0,87 4,52, EASU + RCAS 1 5,86
float RcpMed(float a) { float b = asfloat(0x7ef19fffu - asuint(a)); return b * (-b * a + 2.0); }
float3 Rcas(int2 p, float con)
{
    float3 b = L(p + int2(0, -1)), d = L(p + int2(-1, 0)), e = L(p), f = L(p + int2(1, 0)), h = L(p + int2(0, 1));
    float3 mn4 = min(min(b, d), min(f, h));
    float3 mx4 = max(max(b, d), max(f, h));
    float3 hitMin = min(mn4, e) / max(4.0 * mx4, 1e-5);
    float3 hitMax = (1.0 - max(mx4, e)) / min(4.0 * mn4 - 4.0, -1e-5);
    float3 lobeC = max(-hitMin, hitMax);
    float lobe = max(-(0.25 - 1.0 / 16.0), min(max(lobeC.r, max(lobeC.g, lobeC.b)), 0.0)) * con;
    float rcpL = RcpMed(4.0 * lobe + 1.0);
    return saturate((lobe * (b + d + f + h) + e) * rcpL);
}

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    if (S.z > 0.5) { Out[id.xy] = float4(0.012, 0.014, 0.022, 1); return; }   // pantalla Enciende la Switch 2
    if (S.y > 0.5) { Out[id.xy] = float4(L(p), 1); return; }   // nitidez 0: copia
    if (S.w > 0.5) { Out[id.xy] = float4(Rcas(p, min(1.0, (1.0 / lerp(8.0, 5.0, saturate(S.x))) / 0.1875)), 1); return; }   // 18-11 (MOTOR-CE): RCAS (tras FSR 1)
    float3 a = L(p + int2(-1, -1)), b = L(p + int2(0, -1)), c = L(p + int2(1, -1));
    float3 d = L(p + int2(-1, 0)),  e = L(p),               f = L(p + int2(1, 0));
    float3 g = L(p + int2(-1, 1)),  h = L(p + int2(0, 1)),  i = L(p + int2(1, 1));
    float3 mn = min(min(min(d, e), min(f, b)), h);
    float3 mn2 = min(mn, min(min(a, c), min(g, i)));
    mn += mn2;
    float3 mx = max(max(max(d, e), max(f, b)), h);
    float3 mx2 = max(mx, max(max(a, c), max(g, i)));
    mx += mx2;
    float3 amp = saturate(min(mn, 2.0 - mx) / max(mx, 1e-5));
    amp = sqrt(amp);
    float peak = -1.0 / lerp(8.0, 5.0, saturate(S.x));
    float3 w = amp * peak;
    float3 rw = 1.0 / (1.0 + 4.0 * w);
    float3 o = saturate((b * w + d * w + f * w + h * w + e) * rw);
    Out[id.xy] = float4(o, 1);
}";

        // ---- Bordes y tramado: scripts\s2_aa.glsl de Switch2Magpie (FXAA 3.11 + quitar el tramado de 5 bits) en HLSL ----
        // c0: ancho, alto, tramado (1 = quitarlo), aa (0 = copia tal cual)
        public const string Aa = @"
cbuffer C : register(b0) { uint W, H, Tramado, On; uint4 Pad1; uint4 Pad2; uint4 Pad3; }
Texture2D<float4> In : register(t0);
RWTexture2D<unorm float4> Out : register(u0);
SamplerState Lin : register(s0);

#define EDGE_THRESHOLD 0.125
#define EDGE_THRESHOLD_MIN 0.047
#define SUBPIX 0.25
static const float3 LW = float3(0.299, 0.587, 0.114);
static float2 pt;
float S2L(float2 p) { return dot(In.SampleLevel(Lin, p, 0).rgb, LW); }
float3 T(int2 q) { return In.Load(int3(clamp(q, int2(0, 0), int2(W - 1, H - 1)), 0)).rgb; }
float S2Paso(float d)
{
    float ad = abs(d) * 255.0;
    if (ad < 2.0) return 0.0;
    float e = abs(ad - 8.25 * floor(ad / 8.25 + 0.5));
    return (e < 2.0 && ad < 20.0) ? 1.0 : -1.0;
}

float4 Run(int2 q)
{
    float4 color = float4(T(q), 1);
    float2 pos = (float2(q) + 0.5) * pt;
    float lumaM = dot(color.rgb, LW);
    float lumaS = dot(T(q + int2(0, 1)), LW), lumaE = dot(T(q + int2(1, 0)), LW), lumaN = dot(T(q + int2(0, -1)), LW), lumaW = dot(T(q + int2(-1, 0)), LW);
    float rangeMax = max(max(max(lumaS, lumaE), max(lumaN, lumaW)), lumaM);
    float rangeMin = min(min(min(lumaS, lumaE), min(lumaN, lumaW)), lumaM);
    float range = rangeMax - rangeMin;
    if (Tramado > 0 && range < 13.0 / 255.0)
    {
        float3 c0 = color.rgb, sum = 0, lo = c0, hi = c0;
        float3 t9[9];
        [unroll] for (int j = -1; j <= 1; j++) [unroll] for (int i = -1; i <= 1; i++)
        {
            float3 t = T(q + int2(i, j));
            t9[(j + 1) * 3 + i + 1] = t;
            sum += t; lo = min(lo, t); hi = max(hi, t);
        }
        float3 rg = hi - lo;
        float dw = 1.0 - smoothstep(9.0 / 255.0, 13.0 / 255.0, max(rg.r, max(rg.g, rg.b)));
        float steps = 0.0, off = 0.0;
        [unroll] for (int j2 = 0; j2 < 3; j2++) [unroll] for (int i2 = 0; i2 < 2; i2++)
        {
            float3 dh = t9[j2 * 3 + i2 + 1] - t9[j2 * 3 + i2], dv = t9[(i2 + 1) * 3 + j2] - t9[i2 * 3 + j2];
            [unroll] for (int c = 0; c < 3; c++)
            {
                float a = S2Paso(dh[c]), b = S2Paso(dv[c]);
                steps += max(a, 0.0) + max(b, 0.0); off += max(-a, 0.0) + max(-b, 0.0);
            }
        }
        if (dw > 0.0 && off < 0.5 && steps > 0.5)
        {
            float3 m3 = sum / 9.0, s5 = 0; float n5 = 0.0;
            float3 t5[25];
            [unroll] for (int j3 = -2; j3 <= 2; j3++) [unroll] for (int i3 = -2; i3 <= 2; i3++) t5[(j3 + 2) * 5 + i3 + 2] = T(q + int2(i3, j3));
            steps = 0.0; off = 0.0;
            [unroll] for (int j4 = 0; j4 < 5; j4++) [unroll] for (int i4 = 0; i4 < 4; i4++)
            {
                float3 dh = t5[j4 * 5 + i4 + 1] - t5[j4 * 5 + i4], dv = t5[(i4 + 1) * 5 + j4] - t5[i4 * 5 + j4];
                [unroll] for (int c = 0; c < 3; c++)
                {
                    float a = S2Paso(dh[c]), b = S2Paso(dv[c]);
                    steps += max(a, 0.0) + max(b, 0.0); off += max(-a, 0.0) + max(-b, 0.0);
                }
            }
            if (off < 0.5 && steps > 1.5)
            {
                [unroll] for (int k = 0; k < 25; k++)
                {
                    float3 dd = abs(t5[k] - m3);
                    float w = 1.0 - smoothstep(10.0 / 255.0, 14.0 / 255.0, max(dd.r, max(dd.g, dd.b)));
                    s5 += t5[k] * w; n5 += w;
                }
                float3 m5 = n5 > 0.5 ? s5 / n5 : m3;
                return float4(lerp(c0, m5, dw), 1);
            }
        }
    }
    // 16-11 noche (liso=N, decimas de nivel; 0 = nada): el ruido de +-1 nivel de la captura en lo plano (oscuro, gris) lo toman los
    // transformer por detalle y al mover la camara lo arrastran como grano. Donde el 3x3 entero (cada canal) no pasa de N/10 niveles
    // es solo ese ruido: la media del 3x3 (lo que tenga textura de verdad pasa de ahi y no se toca)
    if (Pad1.x > 0 && range < 8.0 / 255.0)
    {
        float3 s9 = 0, lo9 = color.rgb, hi9 = color.rgb;
        [unroll] for (int jl = -1; jl <= 1; jl++) [unroll] for (int il = -1; il <= 1; il++)
        {
            float3 t = T(q + int2(il, jl));
            s9 += t; lo9 = min(lo9, t); hi9 = max(hi9, t);
        }
        float3 rg9 = hi9 - lo9;
        float lim = Pad1.x / 2550.0;
        float wl = 1.0 - smoothstep(lim, lim + 1.5 / 255.0, max(rg9.r, max(rg9.g, rg9.b)));
        if (wl > 0.0) return float4(lerp(color.rgb, s9 / 9.0, wl), 1);
    }
    if (range < max(EDGE_THRESHOLD_MIN, rangeMax * EDGE_THRESHOLD)) return color;

    float lumaNW = dot(T(q + int2(-1, -1)), LW), lumaSE = dot(T(q + int2(1, 1)), LW), lumaNE = dot(T(q + int2(1, -1)), LW), lumaSW = dot(T(q + int2(-1, 1)), LW);
    float lumaNS = lumaN + lumaS, lumaWE = lumaW + lumaE;
    float subpixNSWE = lumaNS + lumaWE;
    float edgeHorz1 = -2.0 * lumaM + lumaNS;
    float edgeVert1 = -2.0 * lumaM + lumaWE;
    float lumaNESE = lumaNE + lumaSE;
    float lumaNWNE = lumaNW + lumaNE;
    float edgeHorz2 = -2.0 * lumaE + lumaNESE;
    float edgeVert2 = -2.0 * lumaN + lumaNWNE;
    float lumaNWSW = lumaNW + lumaSW;
    float lumaSWSE = lumaSW + lumaSE;
    float edgeHorz3 = -2.0 * lumaW + lumaNWSW;
    float edgeVert3 = -2.0 * lumaS + lumaSWSE;
    float edgeHorz = abs(edgeHorz3) + abs(edgeHorz1) * 2.0 + abs(edgeHorz2);
    float edgeVert = abs(edgeVert3) + abs(edgeVert1) * 2.0 + abs(edgeVert2);
    float subpixNWSWNESE = lumaNWSW + lumaNESE;
    float lengthSign = pt.x;
    bool horzSpan = edgeHorz >= edgeVert;
    float subpixA = subpixNSWE * 2.0 + subpixNWSWNESE;
    if (!horzSpan) { lumaN = lumaW; lumaS = lumaE; } else { lengthSign = pt.y; }
    float subpixB = subpixA / 12.0 - lumaM;
    float gradientN = lumaN - lumaM, gradientS = lumaS - lumaM;
    float lumaNN = lumaN + lumaM, lumaSS = lumaS + lumaM;
    bool pairN = abs(gradientN) >= abs(gradientS);
    float gradient = max(abs(gradientN), abs(gradientS));
    if (pairN) lengthSign = -lengthSign;
    float subpixC = saturate(abs(subpixB) / range);
    float2 posB = pos;
    float2 offNP = horzSpan ? float2(pt.x, 0.0) : float2(0.0, pt.y);
    if (!horzSpan) posB.x += lengthSign * 0.5; else posB.y += lengthSign * 0.5;
    float2 posN = posB - offNP;
    float2 posP = posB + offNP;
    float subpixD = -2.0 * subpixC + 3.0;
    float subpixE = subpixC * subpixC;
    if (!pairN) lumaNN = lumaSS;
    float gradientScaled = gradient * 0.25;
    float lumaMM = lumaM - lumaNN * 0.5;
    float subpixF = subpixD * subpixE;
    bool lumaMLTZero = lumaMM < 0.0;
    float lumaEndN = S2L(posN) - lumaNN * 0.5;
    float lumaEndP = S2L(posP) - lumaNN * 0.5;
    bool doneN = abs(lumaEndN) >= gradientScaled;
    bool doneP = abs(lumaEndP) >= gradientScaled;
    static const float stepLen[8] = { 1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0 };
    [loop] for (int s = 0; s < 8 && !(doneN && doneP); s++)
    {
        if (!doneN) { posN -= offNP * stepLen[s]; lumaEndN = S2L(posN) - lumaNN * 0.5; doneN = abs(lumaEndN) >= gradientScaled; }
        if (!doneP) { posP += offNP * stepLen[s]; lumaEndP = S2L(posP) - lumaNN * 0.5; doneP = abs(lumaEndP) >= gradientScaled; }
    }
    float dstN = horzSpan ? pos.x - posN.x : pos.y - posN.y;
    float dstP = horzSpan ? posP.x - pos.x : posP.y - pos.y;
    bool directionN = dstN < dstP;
    float dst = min(dstN, dstP);
    bool goodSpan = directionN ? ((lumaEndN < 0.0) != lumaMLTZero) : ((lumaEndP < 0.0) != lumaMLTZero);
    float pixelOffset = 0.5 - dst / (dstP + dstN);
    float pixelOffsetGood = goodSpan ? pixelOffset : 0.0;
    float subpixH = subpixF * subpixF * SUBPIX;
    float offs = max(pixelOffsetGood, subpixH);
    float2 posM = pos;
    if (!horzSpan) posM.x += offs * lengthSign; else posM.y += offs * lengthSign;
    return float4(In.SampleLevel(Lin, posM, 0).rgb, 1);
}

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint2 p = uint2(id.x, id.y + Pad1.y);   // (20-11 LATENCIA7: Pad1.y = primera fila, Pad1.z = la ultima + 1; 0 = la imagen entera)
    if (p.x >= W || p.y >= H || (Pad1.z > 0 && p.y >= Pad1.z)) return;
    pt = 1.0 / float2(W, H);
    Out[p] = On ? Run(int2(p)) : float4(T(int2(p)), 1);
}";

        // ---- Movimiento (vectores para DLSS SR y la generacion): piramide de luma y busqueda por bloques de 8x8 ----
        // Luma: nivel 0 desde la imagen RGBA (c0.x = 0) o la mitad del nivel anterior (c0.x = 1). c0.yz = tamaño destino
        public const string Luma = @"
cbuffer C : register(b0) { uint FromLuma, DW, DH, P0; uint4 Pad1; uint4 Pad2; uint4 Pad3; }
Texture2D<float4> InRgb : register(t0);
Texture2D<float> InL : register(t1);
RWTexture2D<unorm float> Out : register(u0);

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= DW || id.y >= DH) return;
    if (FromLuma == 0)
    {
        float3 c = InRgb.Load(int3(id.xy, 0)).rgb;
        Out[id.xy] = dot(c, float3(0.2126, 0.7152, 0.0722));
    }
    else
    {
        int2 p = int2(id.xy) * 2;
        Out[id.xy] = 0.25 * (InL.Load(int3(p, 0)) + InL.Load(int3(p + int2(1, 0), 0)) + InL.Load(int3(p + int2(0, 1), 0)) + InL.Load(int3(p + int2(1, 1), 0)));
    }
}";

        // Busqueda: un hilo = un bloque de 8x8 del nivel. Candidatos: 0 y (si hay nivel mas grueso) el vector de ese nivel x2 en el
        // bloque y sus 4 vecinos; alrededor de cada uno, +-Radio. Al final medio pixel por parabola (Sub = 1) y si el 0 es casi
        // igual de bueno, 0 (que la imagen quieta no tiemble). Vector = donde estaba el bloque en la imagen anterior (anterior - actual)
        // 16-11 (calidad, E2): FlatMode = 1 (mvflat=1, solo nivel fino): los bloques lisos eligen entre los candidatos (0, el del nivel
        // grueso y sus vecinos) y +-1 alrededor del mejor, comparando una ventana de 16x16 alrededor del bloque (antes: siempre 0).
        // Info = 1 (nivel fino, con las mascaras): u1 = por bloque (lo que queda tras mover por punto, textura, sin mover por punto, liso)
        public const string Search = @"
cbuffer C : register(b0) { uint LW, LH; float GX, GY; uint Radius, HasCoarse, CBX, CBY; uint Sub, Neighbors, FlatMode, Info; float ZeroBias, Flat, Noise, Exact; }
// 20-11 (GIRO): GX, GY = el giro de la camara en px de este nivel (Neighbors & 16; antes ahi iban BX, BY, que salen de LW, LH)
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float2> Coarse : register(t2);
Texture2D<float2> PrevMv : register(t3);   // 17-11 (REJAS, mvtemporal): el campo de este nivel en la imagen anterior
RWTexture2D<float2> Mv : register(u0);
RWTexture2D<float4> Inf : register(u1);

static float c[64];
float SadAt(int2 b, int2 d)
{
    float s = 0;
    int2 mx = int2(LW - 1, LH - 1);
    [unroll] for (int y = 0; y < 8; y++)
        [unroll] for (int x = 0; x < 8; x++)
            s += abs(c[y * 8 + x] - Prev.Load(int3(clamp(b + int2(x, y) + d, int2(0, 0), mx), 0)));
    return s;
}

// Ventana de 16x16 centrada en el bloque (b-4 .. b+11) en damero: 128 puntos (para los lisos, que en 8x8 no tienen con que comparar)
float SadWide(int2 b, int2 d)
{
    float s = 0;
    int2 mx = int2(LW - 1, LH - 1);
    [loop] for (int y = -4; y < 12; y++)
    {
        int x0 = -4 + (y & 1);
        [unroll] for (int i = 0; i < 8; i++)
        {
            int2 q = b + int2(x0 + 2 * i, y);
            s += abs(Cur.Load(int3(clamp(q, int2(0, 0), mx), 0)) - Prev.Load(int3(clamp(q + d, int2(0, 0), mx), 0)));
        }
    }
    return s;
}

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint BX = (LW + 7) / 8, BY = (LH + 7) / 8;   // (20-11 GIRO: como bx/by de EngineMotion.cs)
    if (id.x >= BX || id.y >= BY) return;
    int2 b = int2(id.xy) * 8;
    int2 mx = int2(LW - 1, LH - 1);
    float mean = 0;
    [unroll] for (int y = 0; y < 8; y++)
        [unroll] for (int x = 0; x < 8; x++) { float v = Cur.Load(int3(clamp(b + int2(x, y), int2(0, 0), mx), 0)); c[y * 8 + x] = v; mean += v; }
    mean /= 64;
    float var = 0;
    [unroll] for (int i = 0; i < 64; i++) var += abs(c[i] - mean);
    var /= 64;

    int2 cand[8];
    int nc = 1;
    cand[0] = int2(0, 0);
    if (HasCoarse)
    {
        int2 cb = int2(id.xy) / 2;
        int2 cmx = int2(CBX - 1, CBY - 1);
        cand[nc++] = int2(round(Coarse.Load(int3(clamp(cb, int2(0, 0), cmx), 0)) * 2));
    }
    if (HasCoarse && (Neighbors & 1))
    {
        int2 cb = int2(id.xy) / 2;
        int2 cmx = int2(CBX - 1, CBY - 1);
        cand[nc++] = int2(round(Coarse.Load(int3(clamp(cb + int2(-1, 0), int2(0, 0), cmx), 0)) * 2));
        cand[nc++] = int2(round(Coarse.Load(int3(clamp(cb + int2(1, 0), int2(0, 0), cmx), 0)) * 2));
        cand[nc++] = int2(round(Coarse.Load(int3(clamp(cb + int2(0, -1), int2(0, 0), cmx), 0)) * 2));
        cand[nc++] = int2(round(Coarse.Load(int3(clamp(cb + int2(0, 1), int2(0, 0), cmx), 0)) * 2));
    }
    // 17-11 (REJAS, mvtemporal=1): candidato temporal, el vector de este bloque en la imagen anterior (la camara se mueve suave): entre
    // dos sitios que casan igual (el barrote de al lado) gana el que sigue el movimiento de antes, y el vector no salta de una imagen a otra
    int kt = 99;   // el candidato temporal: con +-2 como mucho (con el radio del nivel grueso, +-12, costaba +0,55 ms)
    if (Neighbors & 2) { kt = nc; cand[nc++] = int2(round(PrevMv.Load(int3(id.xy, 0)))); }
    // 20-11 (GIRO, mvglobal=1, Neighbors & 32): el giro de la camara (CutGrid.cs) como candidato, +-(Radius >> 8) alrededor: con +-12
    // alrededor de 0 el nivel grueso no llegaba a mas de 96 px de la entrada (un giro rapido a 4K: 150-400 px por imagen real)
    int kg = 99;
    if (Neighbors & 32) { kg = nc; cand[nc++] = int2(round(float2(GX, GY))); }
    // Zona lisa: no hay nada que buscar; el vector del nivel grueso (o 0)
    // (en el nivel fino, 0: el texto y la interfaz quietos no heredan el movimiento de alrededor)
    if (var < Flat)
    {
        float2 r0 = (HasCoarse && !Sub) ? float2(cand[1]) : float2(0, 0);
        // 20-11 (GIRO): con la camara girando (Neighbors & 16), lo liso sigue al giro (en el nivel grueso el giro; en el fino, sin mvflat,
        // el del nivel grueso): a 4K y con el desenfoque de movimiento del juego casi la mitad de los bloques de 8x8 son lisos y se
        // quedaban a 0 en pleno giro (DLSS-G con fg_vectores=2 los mezcla con los de al lado: vectores cortos, la intermedia a medias)
        if ((Neighbors & 16) && !(Sub && (FlatMode & 1))) r0 = HasCoarse ? float2(cand[1]) : float2(round(float2(GX, GY)));
        float res0 = 0, z0 = 0;
        if (Sub && (FlatMode & 1))
        {
            // 16-11: el candidato que mejor explica la ventana de 16x16 (ve el borde o la textura mas cercana: junto al texto quieto
            // gana 0; en un paneo, el del paneo) y +-1 alrededor; si 0 es casi igual de bueno, 0
            float sw0 = SadWide(b, int2(0, 0));
            float bw = sw0;
            int2 bdw = int2(0, 0);
            [loop] for (int k = 1; k < nc; k++) { float s = SadWide(b, cand[k]); if (s < bw) { bw = s; bdw = cand[k]; } }
            int2 cw = bdw;
            [loop] for (int q = 0; q < 9; q++)
            {
                if (q == 4) continue;
                int2 d = cw + int2(q % 3 - 1, q / 3 - 1);
                float s = SadWide(b, d);
                if (s < bw) { bw = s; bdw = d; }
            }
            bool z = sw0 <= bw * (1.0 + ZeroBias) + 0.04 + 128.0 * Noise;
            r0 = z ? float2(0, 0) : float2(bdw);
            res0 = (z ? sw0 : bw) / 128.0; z0 = sw0 / 128.0;
        }
        else if (Info) { z0 = SadAt(b, int2(0, 0)) / 64.0; res0 = z0; }
        Mv[id.xy] = r0;
        if (Info) Inf[id.xy] = float4(res0, var, z0, 1);
        return;
    }

    // 17-11 (rejas, mvlargo): castigo por pixel de desplazamiento (FlatMode >> 1, en millonesimas por punto). Entre dos sitios que
    // casan casi igual (el barrote de al lado en una reja sobre cielo liso) gana el mas cercano; 0 = como antes
    float Pen = (float)(FlatMode >> 1) * 1e-6 * 64.0;
    // 17-11 15:40 (mvlargo_rel=1, Neighbors & 4): el castigo cuenta desde el vector de este bloque en la imagen anterior, no desde 0: un
    // paneo rapido no se castiga (los vectores largos de verdad no se acortan: borroso con textura en 3D) y saltar al barrote de al lado si
    float2 pred = (Neighbors & 4) ? PrevMv.Load(int3(id.xy, 0)) : float2(0, 0);
    float best = 1e9;
    int2 bd = int2(0, 0);
    int r = (int)(Radius & 255);
    bool giro = (Neighbors & 16) != 0;   // 20-11 (GIRO): mvlargo tampoco castiga lo que sigue al giro de la camara
    for (int k = 0; k < nc; k++)
    {
        int2 c0 = cand[k];
        int rk = k == kt ? min(r, 2) : k == kg ? (int)(Radius >> 8) : r;
        for (int dy = -rk; dy <= rk; dy++)
            for (int dx = -rk; dx <= rk; dx++)
            {
                int2 d = c0 + int2(dx, dy);
                float dl = length(float2(d) - pred);
                if (Neighbors & 64) dl = min(dl, length(float2(d - cand[1])));   // 20-11 (GIRO, mvlargo_grueso=1): ni lo que sigue al nivel grueso
                if (giro) dl = min(dl, length(float2(d) - float2(GX, GY)));
                float s = SadAt(b, d) + Pen * dl;
                if (s < best) { best = s; bd = d; }
            }
    }
    float s0 = SadAt(b, int2(0, 0));
    float2 res = float2(bd);
    // (16-11: Noise = margen frente al ruido de la captura por punto, mvruido; 0 = como antes)
    // (20-11 GIRO: con la camara girando, sin la preferencia por 0 frente al ruido: en un giro rapido de algo poco texturado el 0 ganaba
    // por el margen del ruido; lo quieto de verdad, como la interfaz, sigue ganando con 0 por si solo)
    bool zero = (Neighbors & 16) ? s0 <= best + 0.02 : s0 <= best * (1.0 + ZeroBias) + 0.02 + 64.0 * Noise;
    if (Info) Inf[id.xy] = float4((zero ? s0 : best) / 64.0, var, s0 / 64.0, 0);
    if (zero) res = float2(0, 0);
    else if (Sub)
    {
        float sl = SadAt(b, bd + int2(-1, 0)), sr = SadAt(b, bd + int2(1, 0));
        float su = SadAt(b, bd + int2(0, -1)), sd = SadAt(b, bd + int2(0, 1));
        float ax = sl - 2 * best + sr, ay = su - 2 * best + sd;
        // 16-11 (mvexacto): si el entero ya casa casi del todo (frente al mejor de sus 4 vecinos), sin medio pixel: la parabola
        // sobre un SAD en V con pendientes distintas se inventa una fraccion (un desplazamiento entero exacto, como una lista
        // que se desplaza a pixeles enteros, salia con 0,1-0,3 px de error y DLSS cogia la historia corrida)
        bool exact = Exact > 0 && best < Exact * min(min(sl, sr), min(su, sd));
        // 17-11 15:50 (mvsub=1, Neighbors & 8): el SAD es en V, no una parabola: la parabola se queda corta con fracciones pequenas (0,25 px
        // de verdad -> 0,17). Con dos rectas de la misma pendiente (equiangular) la fraccion sale exacta en una V
        if (Neighbors & 8)
        {
            float mxs = max(sl, sr), mys = max(su, sd);
            if (!exact && mxs - best > 1e-4) res.x += clamp(0.5 * (sl - sr) / (mxs - best), -0.5, 0.5);
            if (!exact && mys - best > 1e-4) res.y += clamp(0.5 * (su - sd) / (mys - best), -0.5, 0.5);
        }
        else
        {
            if (!exact && ax > 1e-4) res.x += clamp(0.5 * (sl - sr) / ax, -0.5, 0.5);
            if (!exact && ay > 1e-4) res.y += clamp(0.5 * (su - sd) / ay, -0.5, 0.5);
        }
    }
    Mv[id.xy] = res;
}";

        // Vectores por pixel (a la resolucion de la captura) desde los bloques del nivel 0, interpolados. c1.x = 1: todo a 0
        public const string MvExpand = @"
cbuffer C : register(b0) { uint W, H, BX, BY; uint Zero, P0, P1, P2; float Smooth, Q0, Q1, Q2; uint4 Pad3; }
Texture2D<float2> Blocks : register(t0);
RWTexture2D<float2> Out : register(u0);

float2 B(int2 p) { return Blocks.Load(int3(clamp(p, int2(0, 0), int2(BX - 1, BY - 1)), 0)); }
// 17-11 (rejas, mvsuave=R): si el bloque y sus 8 vecinos llevan casi el mismo movimiento (todos a <= R px del suyo), la media
// de los 9 (antes mediana: 2,9 s de compilar): un barrote que cruza varios bloques no sale ondulado por fracciones de pixel distintas en cada uno.
// Donde hay un borde de verdad (HUD quieto sobre el fondo, objeto contra fondo) algun vecino pasa de R y se queda el suyo
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

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    if (Zero) { Out[id.xy] = float2(0, 0); return; }
    if (P0) { Out[id.xy] = float2(asfloat(P1), asfloat(P2)); return; }   // 17-11 (rejas, prueba): mvfijo
    // El vector del propio bloque (sin mezclar con los vecinos: el movimiento no se corre sobre el texto quieto de los menus)
    Out[id.xy] = Sm(int2(id.xy) / 8, Smooth);
}";

        // ---- Avisos: textura BGRA premultiplicada (pintada con GDI+) sobre la imagen final, con opacidad ----
        // c0: x, y, ancho, alto del aviso | c1.x (float): opacidad
        public const string Osd = @"
cbuffer C : register(b0) { uint X, Y, OW, OH; float4 A; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> Ov : register(t0);
RWTexture2D<unorm float4> Out : register(u0);

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= OW || id.y >= OH) return;
    float4 o = Ov.Load(int3(id.xy, 0)) * A.x;     // premultiplicado
    uint2 p = uint2(X, Y) + id.xy;
    float4 c = Out[p];
    Out[p] = float4(o.rgb + c.rgb * (1.0 - o.a), 1);
}";
    }
}
