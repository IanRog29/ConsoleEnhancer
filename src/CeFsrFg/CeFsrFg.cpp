// CeFsrFg.cpp - ConsoleEnhancer (1.2.4 ronda 6, agente FGMULTI, 23-11-2026)
//
// FSR FRAME GENERATION x2 / x3 / x4 para el motor propio (engine\CeFsrFg.dll), hecha por nosotros con el codigo MIT de AMD:
// GPUOpen FidelityFX SDK 1.1.4 (https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK, etiqueta v1.1.4): la interpolacion de
// imagenes de FSR 3.1 (frameinterpolation) y su flujo optico (opticalflow) con el backend DX12. Cambiado (parches\fidelityfx-fi-multi.patch)
// para inventar en CUALQUIER instante t entre la anterior (0) y la actual (1) y no solo en 0.5: con x3 en 1/3 y 2/3, con x4 en 1/4, 2/4
// y 3/4, todas con el MISMO flujo optico y los mismos vectores preparados de ese par (solo se repiten las pasadas que dependen de t).
// La API de FSR FG de AMD (la DLL firmada que carga el motor para FSR 3 FG) solo da una inventada a mitad: por eso esta pieza propia.
// Licencia: el codigo de AMD es MIT (Copyright (C) 2024 Advanced Micro Devices, Inc.; texto en THIRD-PARTY.md); esta pieza tambien.
//
// API (C, la llama el motor de C# por P/Invoke; EngineFsrMulti.cs):
//   CeFg_Version()                         = 1
//   CeFg_Create(dev, dispW, dispH, maxRenderW, maxRenderH, dxgiFormat, flags, &ctx, err, errLen)    0 = bien
//   CeFg_Prepare(ctx, lista, depth, mvec, renderW, renderH, mvScaleX, mvScaleY, frameTimeMs, frameId) cada imagen real, con la
//        profundidad y los vectores ("anterior - actual" en px de la entrada si mvScale = 1) en NON_PIXEL_SHADER_RESOURCE
//   CeFg_Generate(ctx, lista, color, colorState, n, outputs, outState, times, reset, frameId, frameTimeMs, renderW, renderH)
//        el flujo optico de (anterior, color) y n inventadas en los instantes times[i] (0..1) a outputs[i] (UAV, R8G8B8A8); la
//        actual queda como "anterior" para la siguiente. reset = corte (sin inventar: las salidas llevan la actual)
//   CeFg_Destroy(ctx)                      (con la grafica parada)
//   CeFg_LastError(buf, len)
// flags: 1 = profundidad invertida (1 cerca, 0 lejos), 2 = imagen HDR (no se usa en el motor).

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#include <FidelityFX/host/ffx_interface.h>
#include <FidelityFX/host/ffx_frameinterpolation.h>
#include <FidelityFX/host/ffx_opticalflow.h>
#include <FidelityFX/host/backends/dx12/ffx_dx12.h>

#define CEFG_API extern "C" __declspec(dllexport)

static wchar_t g_err[512];

static void SetErr(const wchar_t* what, FfxErrorCode e)
{
    swprintf(g_err, 512, L"%ls: 0x%08X", what, (unsigned)e);
}

struct CeFgCtx
{
    FfxInterface iface;
    void* scratch;
    FfxUInt32 sharedId;
    bool sharedOk;
    FfxOpticalflowContext of;
    bool ofOk;
    FfxFrameInterpolationContext fi;
    bool fiOk;
    FfxResourceInternal ofVector, ofScd, dilDepth[2], dilMv[2], recDepth[2];   // (lo preparado, doble: la lista del motor prepara N+1 mientras la de
                                                                                // calculo asincrono aun puede leer N; por frameId & 1, como FSR 3)
    bool resOk;
    int dispW, dispH, maxRW, maxRH;
    DXGI_FORMAT fmt;
    uint32_t flags;
    float camNear, camFar, fov;
    uint64_t lastPrepared;
};

static FfxResource Res(ID3D12Resource* r, FfxResourceStates st, FfxResourceUsage usage, const wchar_t* name)
{
    FfxResourceDescription d = ffxGetResourceDescriptionDX12(r, usage);
    return ffxGetResourceDX12(r, d, name, st);
}

static FfxResourceStates StateOf(int d3d)
{
    switch (d3d)
    {
    case D3D12_RESOURCE_STATE_UNORDERED_ACCESS: return FFX_RESOURCE_STATE_UNORDERED_ACCESS;
    case D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE: return FFX_RESOURCE_STATE_COMPUTE_READ;
    case D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE: return FFX_RESOURCE_STATE_PIXEL_READ;
    case D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE: return FFX_RESOURCE_STATE_PIXEL_COMPUTE_READ;
    case D3D12_RESOURCE_STATE_COPY_SOURCE: return FFX_RESOURCE_STATE_COPY_SRC;
    case D3D12_RESOURCE_STATE_COPY_DEST: return FFX_RESOURCE_STATE_COPY_DEST;
    case D3D12_RESOURCE_STATE_RENDER_TARGET: return FFX_RESOURCE_STATE_RENDER_TARGET;
    default: return FFX_RESOURCE_STATE_COMMON;
    }
}

static void Release(CeFgCtx* c)
{
    if (!c) return;
    if (c->fiOk) ffxFrameInterpolationContextDestroy(&c->fi);
    if (c->ofOk) ffxOpticalflowContextDestroy(&c->of);
    if (c->resOk)
    {
        FfxResourceInternal* rs[] = { &c->ofVector, &c->ofScd, &c->dilDepth[0], &c->dilMv[0], &c->recDepth[0], &c->dilDepth[1], &c->dilMv[1], &c->recDepth[1] };
        for (FfxResourceInternal* r : rs) if (r->internalIndex != 0) c->iface.fpDestroyResource(&c->iface, *r, c->sharedId);
    }
    if (c->sharedOk) c->iface.fpDestroyBackendContext(&c->iface, c->sharedId);
    free(c->scratch);
    delete c;
}

CEFG_API int CeFg_Version() { return 1; }

CEFG_API int CeFg_LastError(wchar_t* buf, int len)
{
    if (buf && len > 0) { wcsncpy_s(buf, len, g_err, _TRUNCATE); }
    return (int)wcslen(g_err);
}

CEFG_API int CeFg_Create(ID3D12Device* dev, int dispW, int dispH, int maxRenderW, int maxRenderH, int dxgiFormat, int flags, void** outCtx)
{
    *outCtx = nullptr;
    g_err[0] = 0;
    CeFgCtx* c = new CeFgCtx();
    memset(c, 0, sizeof(CeFgCtx));
    c->dispW = dispW; c->dispH = dispH; c->maxRW = maxRenderW; c->maxRH = maxRenderH;
    c->fmt = (DXGI_FORMAT)dxgiFormat; c->flags = (uint32_t)flags;
    c->camNear = 0.1f; c->camFar = 1000.0f; c->fov = 1.0471976f;
    const size_t maxContexts = 4;   // recursos compartidos + flujo optico + interpolacion (+1 de holgura)
    size_t sz = ffxGetScratchMemorySizeDX12(maxContexts);
    c->scratch = calloc(1, sz);
    if (!c->scratch) { SetErr(L"sin memoria", 0); Release(c); return -1; }
    FfxErrorCode e = ffxGetInterfaceDX12(&c->iface, ffxGetDeviceDX12(dev), c->scratch, sz, maxContexts);
    if (e != FFX_OK) { SetErr(L"ffxGetInterfaceDX12", e); Release(c); return (int)e; }
    e = c->iface.fpCreateBackendContext(&c->iface, FFX_EFFECT_SHAREDRESOURCES, nullptr, &c->sharedId);
    if (e != FFX_OK) { SetErr(L"contexto de recursos compartidos", e); Release(c); return (int)e; }
    c->sharedOk = true;

    FfxOpticalflowContextDescription od = {};
    od.backendInterface = c->iface;
    od.flags = 0;   // (HDR: el permutado de AMD lo elige la propia OF por el formato)
    od.resolution.width = (uint32_t)dispW; od.resolution.height = (uint32_t)dispH;
    e = ffxOpticalflowContextCreate(&c->of, &od);
    if (e != FFX_OK) { SetErr(L"ffxOpticalflowContextCreate", e); Release(c); return (int)e; }
    c->ofOk = true;

    FfxFrameInterpolationContextDescription fd = {};
    fd.backendInterface = c->iface;
    fd.flags = ((flags & 1) ? FFX_FRAMEINTERPOLATION_ENABLE_DEPTH_INVERTED : 0) | ((flags & 2) ? FFX_FRAMEINTERPOLATION_ENABLE_HDR_COLOR_INPUT : 0);
    fd.maxRenderSize.width = (uint32_t)maxRenderW; fd.maxRenderSize.height = (uint32_t)maxRenderH;
    fd.displaySize.width = (uint32_t)dispW; fd.displaySize.height = (uint32_t)dispH;
    fd.backBufferFormat = ffxGetSurfaceFormatDX12(c->fmt);
    fd.previousInterpolationSourceFormat = fd.backBufferFormat;
    e = ffxFrameInterpolationContextCreate(&c->fi, &fd);
    if (e != FFX_OK) { SetErr(L"ffxFrameInterpolationContextCreate", e); Release(c); return (int)e; }
    c->fiOk = true;

    FfxOpticalflowSharedResourceDescriptions ofr = {};
    e = ffxOpticalflowGetSharedResourceDescriptions(&c->of, &ofr);
    if (e != FFX_OK) { SetErr(L"ffxOpticalflowGetSharedResourceDescriptions", e); Release(c); return (int)e; }
    FfxFrameInterpolationSharedResourceDescriptions fir = {};
    e = ffxFrameInterpolationGetSharedResourceDescriptions(&c->fi, &fir);
    if (e != FFX_OK) { SetErr(L"ffxFrameInterpolationGetSharedResourceDescriptions", e); Release(c); return (int)e; }
    c->resOk = true;
    struct { const FfxCreateResourceDescription* d; FfxResourceInternal* r; const wchar_t* n; } todo[] = {
        { &ofr.opticalFlowVector, &c->ofVector, L"flujo optico" }, { &ofr.opticalFlowSCD, &c->ofScd, L"cambio de escena" },
        { &fir.dilatedDepth, &c->dilDepth[0], L"profundidad dilatada 0" }, { &fir.dilatedMotionVectors, &c->dilMv[0], L"vectores dilatados 0" },
        { &fir.reconstructedPrevNearestDepth, &c->recDepth[0], L"profundidad anterior reconstruida 0" },
        { &fir.dilatedDepth, &c->dilDepth[1], L"profundidad dilatada 1" }, { &fir.dilatedMotionVectors, &c->dilMv[1], L"vectores dilatados 1" },
        { &fir.reconstructedPrevNearestDepth, &c->recDepth[1], L"profundidad anterior reconstruida 1" } };
    for (auto& t : todo)
    {
        e = c->iface.fpCreateResource(&c->iface, t.d, c->sharedId, t.r);
        if (e != FFX_OK) { SetErr(t.n, e); Release(c); return (int)e; }
    }
    *outCtx = c;
    return 0;
}

CEFG_API int CeFg_Prepare(void* ctx, ID3D12GraphicsCommandList* cl, ID3D12Resource* depth, ID3D12Resource* mvec, int renderW, int renderH,
                          float mvScaleX, float mvScaleY, float frameTimeMs, unsigned long long frameId)
{
    CeFgCtx* c = (CeFgCtx*)ctx;
    if (!c || !cl || !depth || !mvec) return -1;
    FfxFrameInterpolationPrepareDescription p = {};
    p.commandList = ffxGetCommandListDX12(cl);
    p.renderSize.width = (uint32_t)renderW; p.renderSize.height = (uint32_t)renderH;
    p.jitterOffset.x = 0; p.jitterOffset.y = 0;
    p.motionVectorScale.x = mvScaleX; p.motionVectorScale.y = mvScaleY;
    p.frameTimeDelta = frameTimeMs;
    p.cameraNear = c->camNear; p.cameraFar = c->camFar; p.viewSpaceToMetersFactor = 1.0f; p.cameraFovAngleVertical = c->fov;
    p.depth = Res(depth, FFX_RESOURCE_STATE_COMPUTE_READ, FFX_RESOURCE_USAGE_READ_ONLY, L"profundidad del motor");
    p.motionVectors = Res(mvec, FFX_RESOURCE_STATE_COMPUTE_READ, FFX_RESOURCE_USAGE_READ_ONLY, L"vectores del motor");
    p.frameID = frameId;
    const int b = (int)(frameId & 1);
    p.dilatedDepth = c->iface.fpGetResource(&c->iface, c->dilDepth[b]);
    p.dilatedMotionVectors = c->iface.fpGetResource(&c->iface, c->dilMv[b]);
    p.reconstructedPrevDepth = c->iface.fpGetResource(&c->iface, c->recDepth[b]);
    FfxErrorCode e = ffxFrameInterpolationPrepare(&c->fi, &p);
    if (e != FFX_OK) { SetErr(L"ffxFrameInterpolationPrepare", e); return (int)e; }
    c->lastPrepared = frameId;
    return 0;
}

CEFG_API int CeFg_Generate(void* ctx, ID3D12GraphicsCommandList* cl, ID3D12Resource* color, int colorState, int n, ID3D12Resource** outputs,
                           int outState, const float* times, int reset, unsigned long long frameId, float frameTimeMs, int renderW, int renderH)
{
    CeFgCtx* c = (CeFgCtx*)ctx;
    if (!c || !cl || !color || n < 1 || !outputs || !times) return -1;
    FfxCommandList l = ffxGetCommandListDX12(cl);
    FfxResource col = Res(color, StateOf(colorState), FFX_RESOURCE_USAGE_READ_ONLY, L"imagen real");
    // 1. el flujo optico del par (anterior, actual): una vez
    FfxOpticalflowDispatchDescription od = {};
    od.commandList = l;
    od.color = col;
    od.opticalFlowVector = c->iface.fpGetResource(&c->iface, c->ofVector);
    od.opticalFlowSCD = c->iface.fpGetResource(&c->iface, c->ofScd);
    od.reset = reset != 0;
    od.backbufferTransferFunction = (c->flags & 2) ? FFX_BACKBUFFER_TRANSFER_FUNCTION_PQ : FFX_BACKBUFFER_TRANSFER_FUNCTION_SRGB;
    od.minMaxLuminance.x = 0.0f; od.minMaxLuminance.y = 1000.0f;
    FfxErrorCode e = ffxOpticalflowContextDispatch(&c->of, &od);
    if (e != FFX_OK) { SetErr(L"ffxOpticalflowContextDispatch", e); return (int)e; }
    // 2. las n inventadas, cada una en su instante, del mismo flujo y de lo preparado; la actual pasa a "anterior" tras la ultima
    for (int i = 0; i < n; i++)
    {
        FfxFrameInterpolationDispatchDescription d = {};
        d.flags = 0;
        d.commandList = l;
        d.displaySize.width = (uint32_t)c->dispW; d.displaySize.height = (uint32_t)c->dispH;
        d.renderSize.width = (uint32_t)renderW; d.renderSize.height = (uint32_t)renderH;
        d.currentBackBuffer = col;
        d.output = Res(outputs[i], StateOf(outState), FFX_RESOURCE_USAGE_UAV, L"inventada");
        d.interpolationRect.left = 0; d.interpolationRect.top = 0; d.interpolationRect.width = c->dispW; d.interpolationRect.height = c->dispH;
        d.opticalFlowVector = od.opticalFlowVector;
        d.opticalFlowSceneChangeDetection = od.opticalFlowSCD;
        d.opticalFlowBlockSize = 8;
        d.opticalFlowScale.x = 1.0f / c->dispW; d.opticalFlowScale.y = 1.0f / c->dispH;
        d.cameraNear = c->camNear; d.cameraFar = c->camFar; d.cameraFovAngleVertical = c->fov; d.viewSpaceToMetersFactor = 1.0f;
        d.frameTimeDelta = frameTimeMs;
        d.reset = reset != 0;
        d.backBufferTransferFunction = od.backbufferTransferFunction == FFX_BACKBUFFER_TRANSFER_FUNCTION_PQ ? FFX_BACKBUFFER_TRANSFER_FUNCTION_PQ : FFX_BACKBUFFER_TRANSFER_FUNCTION_SRGB;
        d.minMaxLuminance[0] = 0.0f; d.minMaxLuminance[1] = 1000.0f;
        d.frameID = frameId;
        const int b = (int)(frameId & 1);
        d.dilatedDepth = c->iface.fpGetResource(&c->iface, c->dilDepth[b]);
        d.dilatedMotionVectors = c->iface.fpGetResource(&c->iface, c->dilMv[b]);
        d.reconstructedPrevDepth = c->iface.fpGetResource(&c->iface, c->recDepth[b]);
        d.interpolationT = times[i];
        d.keepPreviousSource = i < n - 1;
        e = ffxFrameInterpolationDispatch(&c->fi, &d);
        if (e != FFX_OK) { SetErr(L"ffxFrameInterpolationDispatch", e); return (int)e; }
    }
    return 0;
}

CEFG_API void CeFg_Destroy(void* ctx)
{
    Release((CeFgCtx*)ctx);
}

// El backend DX12 de AMD apunta a la cadena de presentacion de FSR 3 (FrameInterpolationSwapchainDX12) desde ffxGetInterfaceDX12; aqui
// no se usa (presenta el motor): sin ella
FfxErrorCode ffxSetFrameGenerationConfigToSwapchainDX12(FfxFrameGenerationConfig const*) { return FFX_ERROR_INVALID_ARGUMENT; }

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
