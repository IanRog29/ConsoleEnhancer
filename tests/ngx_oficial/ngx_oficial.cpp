// ngx_oficial.cpp - (22-11-2026, DLSS5LEGAL) prueba de la VIA OFICIAL de NGX para DLSSNR ("DLSS 5", Feature 18 = Reserved18 en las
// cabeceras publicas): solo la API publica del SDK de DLSS (nvsdk_ngx_s.lib de github.com/NVIDIA/DLSS v310.9.16), sin cargar la DLL a
// mano, sin parchear nada y sin cambiar ninguna importacion. NGX (el _nvngx.dll del driver) decide si carga la DLL de la carpeta dada.
// Uso: ngx_oficial.exe <carpeta con nvngx_dlssnr.dll> <carpeta de datos/logs>   (compilar: tests\ngx_oficial\compilar.bat)
// 22-11 con el driver 610.88 (RTX 4060 Ti): GetFeatureRequirements(18) y CreateFeature(18) = 0xBAD0000C OutOfDate ("required feature is
// not supported by NGX runtime, please update display driver"): NGX ni mira la DLL. Sirve para ver si un driver nuevo lo abre.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <nvsdk_ngx.h>
#include <nvsdk_ngx_helpers.h>
#include <cstdio>
#include <string>
#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")

static const char* kProject = "f0ab2f6e-7610-4001-afe3-ed00e8270716";   // el ProjectId del motor de ConsoleEnhancer (Engine.cs)

static void NVSDK_CONV LogCb(const char* m, NVSDK_NGX_Logging_Level, NVSDK_NGX_Feature c)
{
    if (!m) return;
    std::string s(m);
    if (s.find("SnippetLocationInfo") != std::string::npos || s.find("NGXLoadConfig") != std::string::npos ||
        s.find("IsFeatureDenied") != std::string::npos) return;   // (las mismas lineas de todas las funciones: fuera)
    printf("    [NGX %u] %s%s", (unsigned)c, m, s.empty() || s.back() != '\n' ? "\n" : "");
}

static void Res(const char* what, NVSDK_NGX_Result r)
{
    const wchar_t* t = GetNGXResultAsString(r);
    printf("%s: 0x%08X %ls\n", what, (unsigned)r, t ? t : L"");
}

static void Req(IDXGIAdapter1* ad, NVSDK_NGX_FeatureCommonInfo* fci, const wchar_t* data, int feature, const char* name)
{
    NVSDK_NGX_FeatureDiscoveryInfo di = {};
    di.SDKVersion = NVSDK_NGX_Version_API;
    di.FeatureID = (NVSDK_NGX_Feature)feature;
    di.Identifier.IdentifierType = NVSDK_NGX_Application_Identifier_Type_Project_Id;
    di.Identifier.v.ProjectDesc.ProjectId = kProject;
    di.Identifier.v.ProjectDesc.EngineType = NVSDK_NGX_ENGINE_TYPE_CUSTOM;
    di.Identifier.v.ProjectDesc.EngineVersion = "1.2.4";
    di.ApplicationDataPath = data;
    di.FeatureInfo = fci;
    NVSDK_NGX_FeatureRequirement req = {};
    NVSDK_NGX_Result r = NVSDK_NGX_D3D12_GetFeatureRequirements(ad, &di, &req);
    char what[96]; sprintf_s(what, "GetFeatureRequirements(%d %s)", feature, name);
    Res(what, r);
    if (NVSDK_NGX_SUCCEED(r))
    {
        unsigned b = (unsigned)req.FeatureSupported;
        printf("    FeatureSupported=0x%X%s%s%s%s%s%s MinHWArchitecture=0x%X MinOS=%s\n", b, b == 0 ? " (Supported)" : "",
               b & 1 ? " CheckNotPresent" : "", b & 2 ? " DriverVersionUnsupported" : "", b & 4 ? " AdapterUnsupported" : "",
               b & 8 ? " OSVersionBelowMinimum" : "", b & 16 ? " NotImplemented" : "", req.MinHWArchitecture, req.MinOSVersion);
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 3) { printf("uso: ngx_oficial.exe <carpeta con nvngx_dlssnr.dll> <carpeta de datos>\n"); return 2; }
    const wchar_t* dllDir = argv[1];
    const wchar_t* data = argv[2];
    CreateDirectoryW(data, nullptr);
    IDXGIFactory6* f = nullptr;
    if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&f)))) { printf("sin DXGI\n"); return 1; }
    IDXGIAdapter1* ad = nullptr;
    f->EnumAdapterByGpuPreference(0, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(&ad));
    DXGI_ADAPTER_DESC1 d = {};
    ad->GetDesc1(&d);
    printf("GPU: %ls (vendor 0x%04X device 0x%04X)\n", d.Description, d.VendorId, d.DeviceId);

    const wchar_t* paths[1] = { dllDir };
    NVSDK_NGX_FeatureCommonInfo fci = {};
    fci.PathListInfo.Path = paths;
    fci.PathListInfo.Length = 1;
    fci.LoggingInfo.LoggingCallback = LogCb;
    fci.LoggingInfo.MinimumLoggingLevel = NVSDK_NGX_LOGGING_LEVEL_ON;
    fci.LoggingInfo.DisableOtherLoggingSinks = false;

    printf("\n== 1) Requisitos por la API publica (sin Init) ==\n");
    Req(ad, &fci, data, 1, "SuperSampling / DLSS SR");
    Req(ad, &fci, data, 11, "FrameGeneration / DLSS FG");
    Req(ad, &fci, data, 13, "RayReconstruction");
    Req(ad, &fci, data, 18, "Reserved18 / DLSSNR");

    printf("\n== 2) Init_with_ProjectID ==\n");
    ID3D12Device* dev = nullptr;
    if (FAILED(D3D12CreateDevice(ad, D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&dev)))) { printf("sin D3D12\n"); return 1; }
    NVSDK_NGX_Result r = NVSDK_NGX_D3D12_Init_with_ProjectID(kProject, NVSDK_NGX_ENGINE_TYPE_CUSTOM, "1.2.4", data, dev, &fci, NVSDK_NGX_Version_API);
    Res("NVSDK_NGX_D3D12_Init_with_ProjectID", r);
    if (NVSDK_NGX_FAILED(r)) return 1;

    NVSDK_NGX_Parameter* cap = nullptr;
    r = NVSDK_NGX_D3D12_GetCapabilityParameters(&cap);
    Res("GetCapabilityParameters", r);
    if (cap)
    {
        int ss = -1, ssDrv = -1, fg = -1; unsigned maj = 0, mnr = 0;
        cap->Get(NVSDK_NGX_Parameter_SuperSampling_Available, &ss);
        cap->Get(NVSDK_NGX_Parameter_SuperSampling_NeedsUpdatedDriver, &ssDrv);
        cap->Get(NVSDK_NGX_Parameter_SuperSampling_MinDriverVersionMajor, &maj);
        cap->Get(NVSDK_NGX_Parameter_SuperSampling_MinDriverVersionMinor, &mnr);
        cap->Get("FrameGeneration.Available", &fg);
        printf("    SuperSampling.Available=%d NeedsUpdatedDriver=%d MinDriver=%u.%u; FrameGeneration.Available=%d\n", ss, ssDrv, maj, mnr, fg);
    }

    printf("\n== 3) CreateFeature(18) por NGX (la DLL la tiene que cargar NGX desde %ls) ==\n", dllDir);
    ID3D12CommandQueue* q = nullptr; ID3D12CommandAllocator* al = nullptr; ID3D12GraphicsCommandList* cl = nullptr;
    D3D12_COMMAND_QUEUE_DESC qd = {}; qd.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    dev->CreateCommandQueue(&qd, IID_PPV_ARGS(&q));
    dev->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&al));
    dev->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, al, nullptr, IID_PPV_ARGS(&cl));
    NVSDK_NGX_Parameter* p = nullptr;
    r = NVSDK_NGX_D3D12_AllocateParameters(&p);
    Res("AllocateParameters", r);
    NVSDK_NGX_Handle* h = nullptr;
    if (p)
    {
        const unsigned w = 1280, hh = 720;
        p->Set("DLSSNR.Width", w); p->Set("DLSSNR.Height", hh);
        p->Set("DLSSNR.InputWidth", w); p->Set("DLSSNR.InputHeight", hh);
        p->Set("DLSSNR.OutputWidth", w); p->Set("DLSSNR.OutputHeight", hh);
        p->Set(NVSDK_NGX_Parameter_Width, w); p->Set(NVSDK_NGX_Parameter_Height, hh);
        p->Set(NVSDK_NGX_Parameter_CreationNodeMask, 1u); p->Set(NVSDK_NGX_Parameter_VisibilityNodeMask, 1u);
        r = NVSDK_NGX_D3D12_CreateFeature(cl, (NVSDK_NGX_Feature)18, p, &h);
        Res("NVSDK_NGX_D3D12_CreateFeature(18)", r);
        printf("    handle=%p\n", (void*)h);
    }
    cl->Close();
    ID3D12CommandList* lists[] = { cl };
    q->ExecuteCommandLists(1, lists);
    ID3D12Fence* fe = nullptr; dev->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fe));
    q->Signal(fe, 1);
    HANDLE ev = CreateEventW(nullptr, FALSE, FALSE, nullptr); fe->SetEventOnCompletion(1, ev); WaitForSingleObject(ev, 5000);
    if (h) Res("ReleaseFeature", NVSDK_NGX_D3D12_ReleaseFeature(h));
    if (p) NVSDK_NGX_D3D12_DestroyParameters(p);
    if (cap) NVSDK_NGX_D3D12_DestroyParameters(cap);
    Res("Shutdown1", NVSDK_NGX_D3D12_Shutdown1(dev));
    HMODULE m = GetModuleHandleW(L"nvngx_dlssnr.dll");
    printf("\nnvngx_dlssnr.dll cargada en el proceso al final: %s\n", m ? "SI" : "no");
    return 0;
}
