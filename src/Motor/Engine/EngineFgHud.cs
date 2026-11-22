using System;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 22-11 (1.2.4, agente DLSSFG; el usuario: "Dlss fg x2 x3 y x4 tiene muchos fallos visuales y en los mapas y asi muchisimos").
    // fgui=4 / fgui=5: la interfaz quieta (minimapa, textos, marcadores, barras) fuera de lo que interpola DLSS-G. DLSS-G sabe tratar la
    // interfaz si recibe la imagen SIN ella (HUDLess: con la diferencia entre la imagen y la sin interfaz ve donde esta) y, con la
    // recomposicion, la interfaz aparte (UIColorAndAlpha). Antes el motor le daba como "sin interfaz" la misma imagen (con interfaz), asi que
    // DLSS-G no veia ninguna: la interpolaba con el fondo que corre detras (el texto y el marco del minimapa arrastraban fondo y dejaban
    // estela).
    // Mascara de zonas fijas: la de fgui (QShaders.UiMask: puntos quietos fgui_quieto imagenes seguidas con borde mientras la escena se
    // mueve) crecida fgui_crecer puntos (de la entrada) por lo que tambien esta quieto (el interior liso de las letras y los marcos).
    // La imagen sin interfaz: lo marcado se rellena con el fondo de los lados (en horizontal, el primer punto no marcado a cada lado hasta
    // 96 px de la salida, mezclado por distancia; si no hay, en vertical).
    //   fgui=4  HUDLess = la imagen rellena (sin etiqueta de interfaz)
    //   fgui=5  HUDLess = la imagen rellena + UIColorAndAlpha (la interfaz, color premultiplicado y alfa) con la recomposicion
    partial class Settings
    {
        public int FgUiGrow = 3;   // fgui_crecer: puntos de la entrada que crece la mascara por lo quieto (0-6)
        public bool FgUiShow;      // fgui_ver=1 (solo pruebas): en pantalla la imagen sin interfaz que recibe DLSS-G
        public bool FgUiSticky = true;   // fgui_pegada=1 (1.2.4 ronda 5, FGCAL): la mascara no se apaga cuando la camara se para (QShaders.UiMask)
    }

    unsafe partial class Engine
    {
        ID3D12PipelineState psoHudFill;
        ID3D12Resource hudless;              // la imagen sin la interfaz quieta (salida), para DLSS-G
        bool hudValid;
        object hudViewsFor;                  // los recursos con los que se escribieron las vistas de TQUiOut
        object hudViewsFor2, hudViewsFor3;

        // (1.2.4, integracion: y solo si la mascara es de la imagen que se pinta -uiValid, como RecordUiOut-: en un corte o sin movimiento
        // no se rehace y la imagen sin interfaz seria la de otra escena; entonces 'final', como antes)
        bool HudlessNow { get { return fgApplied && s.FgUi >= 4 && hudValid && uiValid && hudless != null; } }

        // La etiqueta HUDLess (Engine.cs): la imagen rellena con fgui=4/5; si no, 'final' como siempre
        void TagHudless(SlExtent outExt)
        {
            if (HudlessNow)
                tags.Add(SlBufferType.HUDLessColor, Com.BorrowPtr(hudless, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, SlResourceLifecycle.eValidUntilPresent, outExt);
            else
                tags.Add(SlBufferType.HUDLessColor, Com.BorrowPtr(final, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.COPY_SOURCE, SlResourceLifecycle.eValidUntilPresent, outExt);
        }

        // Despues del CAS ('final' en COPY_SOURCE), desde RecordUiOut con fgui=4/5
        void RecordHudless()
        {
            if (psoHudFill == null) psoHudFill = Pso(CompileCs("hudfill", HudShaders.Fill));
            bool color = s.FgUi == 5;
            if (hudless == null) hudless = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "imagen sin interfaz");
            if (color && uiColor == null) uiColor = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "interfaz (color y alfa)");
            if (!ReferenceEquals(hudViewsFor, uiMaskIn) || !ReferenceEquals(hudViewsFor2, final) || !ReferenceEquals(hudViewsFor3, uiColor))
            {
                NullViews(TQUiOut);
                SrvTex(TQUiOut, 0, uiMaskIn, DXGI_FORMAT.R8_UNORM);
                SrvTex(TQUiOut, 1, final, ColorFmt);
                SrvTex(TQUiOut, 2, stillCount, DXGI_FORMAT.R32_UINT);
                if (uiColor != null) UavTex(TQUiOut, U0, uiColor, ColorFmt);
                UavTex(TQUiOut, U0 + 1, hudless, ColorFmt);
                hudViewsFor = uiMaskIn; hudViewsFor2 = final; hudViewsFor3 = uiColor;
            }
            uint* c = stackalloc uint[16];
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)outW; c[1] = (uint)outH; c[2] = color ? 1u : 0u; c[3] = (uint)Math.Max(1, s.FgUiFrames);
            c[4] = (uint)inW; c[5] = (uint)inH; c[6] = (uint)Math.Max(0, Math.Min(6, s.FgUiGrow)); c[7] = 0;
            c[8] = F((float)inW / outW); c[9] = F((float)inH / outH);
            QStamp(2);
            Barrier(final, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(hudless, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            if (color) Barrier(uiColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            BindCompute(psoHudFill, TQUiOut, c, 12);
            list.Dispatch(Groups(outW, 16), Groups(outH, 8), 1);
            if (color) Barrier(uiColor, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(hudless, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(final, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
            QStamp(3);
            QResolve(2);
            qMaskQ[qCur] |= 2;
            hudValid = true;
        }

        // A la pantalla (Engine.cs): 'show', o con fgui_ver=1 la imagen sin interfaz
        void CopyToBack(ID3D12Resource bb, ID3D12Resource show)
        {
            if (!(s.FgUiShow && HudlessNow)) { list.CopyResource(bb, show); return; }
            Barrier(hudless, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
            list.CopyResource(bb, hudless);
            Barrier(hudless, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        void ReleaseHudless()
        {
            if (hudless != null) Com.Release(ref hudless);
            hudValid = false; hudViewsFor = hudViewsFor2 = hudViewsFor3 = null;
        }
    }

    static class HudShaders
    {
        // c0: salida ancho, alto, color (UIColorAndAlpha), NStill | c1: entrada ancho, alto, crecer | c2 (float): entrada / salida en x, y
        // t0 = mascara de interfaz (entrada), t1 = final (salida), t2 = imagenes quieto (entrada); u0 = interfaz color+alfa, u1 = sin interfaz
        public const string Fill = @"
cbuffer C : register(b0) { uint W, H, Color, NStill; uint IW, IH, Grow, P0; float2 K; float2 P1; uint4 Pad3; }
Texture2D<float> Ui : register(t0);
Texture2D<float4> Final : register(t1);
Texture2D<uint> Still : register(t2);
RWTexture2D<unorm float4> OutC : register(u0);
RWTexture2D<unorm float4> Hud : register(u1);

int2 In(int2 o) { return clamp(int2((float2(o) + 0.5) * K), int2(0, 0), int2(IW - 1, IH - 1)); }
bool Raw(int2 q) { return Ui.Load(int3(clamp(q, int2(0, 0), int2(IW - 1, IH - 1)), 0)) > 0.5; }
bool StillAt(int2 q) { return (Still.Load(int3(clamp(q, int2(0, 0), int2(IW - 1, IH - 1)), 0)) & 0xFFFFu) >= NStill; }
// interfaz: marcada, o quieta con algo marcado a <= Grow puntos (de la entrada)
bool Masked(int2 q)
{
    if (Raw(q)) return true;
    if (Grow == 0 || !StillAt(q)) return false;
    int g = (int)Grow;
    [loop] for (int j = -g; j <= g; j += max(1, g / 2))
        [loop] for (int i = -g; i <= g; i += max(1, g / 2))
            if (Raw(q + int2(i, j))) return true;
    return false;
}

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    float4 f = Final.Load(int3(p, 0));
    bool m = Masked(In(p));
    float4 h = f;
    if (m)
    {
        // relleno: el primer punto sin interfaz a cada lado (horizontal; si no, vertical), mezclado por distancia
        float4 a = 0, b = 0; float da = 0, db = 0;
        [loop] for (int s = 3; s <= 96; s += 3) { int2 q = p + int2(-s, 0); if (q.x < 0) break; if (!Masked(In(q))) { a = Final.Load(int3(q, 0)); da = s; break; } }
        [loop] for (int s = 3; s <= 96; s += 3) { int2 q = p + int2(s, 0); if (q.x >= (int)W) break; if (!Masked(In(q))) { b = Final.Load(int3(q, 0)); db = s; break; } }
        if (da == 0 && db == 0)
        {
            [loop] for (int s = 3; s <= 96; s += 3) { int2 q = p + int2(0, -s); if (q.y < 0) break; if (!Masked(In(q))) { a = Final.Load(int3(q, 0)); da = s; break; } }
            [loop] for (int s = 3; s <= 96; s += 3) { int2 q = p + int2(0, s); if (q.y >= (int)H) break; if (!Masked(In(q))) { b = Final.Load(int3(q, 0)); db = s; break; } }
        }
        if (da > 0 && db > 0) h = (a * db + b * da) / (da + db);
        else if (da > 0) h = a;
        else if (db > 0) h = b;
    }
    Hud[p] = float4(h.rgb, 1);
    if (Color) OutC[p] = m ? float4(f.rgb, 1) : float4(0, 0, 0, 0);
}";
    }
}
