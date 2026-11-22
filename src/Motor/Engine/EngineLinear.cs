using System;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 20-11-2026 (DLSS SR en luz lineal; guia de NGX 3.1: "Linear RGBA16F requires IsHDR even for SDR media"): dlss_lineal=1 = la captura (sRGB) pasa a luz lineal en FP16 antes de DLSS SR, que se configura con colorBuffersHDR y
    // auto exposicion, y su salida vuelve a sRGB (8 bits) para lo de despues (CAS, aviso, generacion). Sin el (0): como antes, sRGB de 8
    // bits con colorBuffersHDR=false. Las tablas TLinIn / TLinOut van detras de las de DLSS 5 (Engine.cs: TablesCount)
    unsafe partial class Engine
    {
        const int TLinIn = TNr + 5, TLinOut = TNr + 6;   // a lineal: t0 = inColor, u0 = inLinear; a sRGB: t0 = outLinear, u0 = outColor
        const string ToLinearShader = @"
cbuffer C : register(b0) { uint W, H, P0, P1; float4 Q; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> In : register(t0);
RWTexture2D<float4> Out : register(u0);
float3 ToLin(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    Out[id.xy] = float4(ToLin(saturate(In[id.xy].rgb)), 1);
}";
        const string ToSrgbShader = @"
cbuffer C : register(b0) { uint W, H, P0, P1; float4 Q; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> In : register(t0);
RWTexture2D<unorm float4> Out : register(u0);
float3 ToSrgb(float3 c) { c = saturate(c); return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055; }
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    Out[id.xy] = float4(ToSrgb(In[id.xy].rgb), 1);
}";
        ID3D12PipelineState psoToLinear, psoToSrgb;
        ID3D12Resource inLinear, outLinear, linInColor, linOutColor;   // (los inColor / outColor con que se hicieron las tablas)

        // Las texturas en FP16 y sus tablas, si faltan o cambio la entrada / la salida
        bool EnsureLinear()
        {
            if (inLinear == null || !ReferenceEquals(linInColor, inColor))
            {
                if (inLinear != null) Bury(inLinear);
                inLinear = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, inW, inH, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "DLSS SR: entrada en luz lineal");
                SrvTex(TLinIn, 0, inColor, ColorFmt); UavTex(TLinIn, U0, inLinear, DXGI_FORMAT.R16G16B16A16_FLOAT);
                linInColor = inColor;
            }
            if (outLinear == null || !ReferenceEquals(linOutColor, outColor))
            {
                if (outLinear != null) Bury(outLinear);
                outLinear = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, outW, outH, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "DLSS SR: salida en luz lineal");
                SrvTex(TLinOut, 0, outLinear, DXGI_FORMAT.R16G16B16A16_FLOAT); UavTex(TLinOut, U0, outColor, ColorFmt);
                linOutColor = outColor;
            }
            if (psoToLinear == null) psoToLinear = Pso(CompileCs("tolinear", ToLinearShader));
            if (psoToSrgb == null) psoToSrgb = Pso(CompileCs("tosrgb", ToSrgbShader));
            return true;
        }

        // inColor (sRGB, NON_PIXEL_SHADER_RESOURCE) -> inLinear (queda en NON_PIXEL_SHADER_RESOURCE)
        void RecordToLinear()
        {
            uint* c = stackalloc uint[8];
            Barrier(inLinear, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = 0; c[3] = 0; c[4] = 0; c[5] = 0; c[6] = 0; c[7] = 0;
            BindCompute(psoToLinear, TLinIn, c, 8);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(inLinear, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        // outLinear (lo que escribio DLSS SR, UNORDERED_ACCESS) -> outColor (sRGB, UNORDERED_ACCESS)
        void RecordToSrgb()
        {
            uint* c = stackalloc uint[8];
            UavBarrier(outLinear);
            Barrier(outLinear, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            c[0] = (uint)outW; c[1] = (uint)outH; c[2] = 0; c[3] = 0; c[4] = 0; c[5] = 0; c[6] = 0; c[7] = 0;
            BindCompute(psoToSrgb, TLinOut, c, 8);
            list.Dispatch(Groups(outW, 16), Groups(outH, 8), 1);
            Barrier(outLinear, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
        }
    }
}
