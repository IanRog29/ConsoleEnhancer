// PrismaNr.dll - "DLSS 5" (NVIDIA DLSSNR, Feature 18) para el motor propio de ConsoleEnhancer (D3D12), 20-11-2026.
// Carga la nvngx_dlssnr.dll que pone el usuario (la misma que usa Magpie) y llama a sus exportaciones directamente (Init_Ext con el
// AppId del "signed snippet", CreateFeature 18, EvaluateFeature), como hace Magpie (DLSSNRFilter.cpp).
// OJO (1.2.4, se publica por decision del usuario): esto carga la DLL sin pasar por NGX, es decir, saltandose la comprobacion de NVIDIA;
// los terminos de los SDK RTX de NVIDIA no lo permiten. ConsoleEnhancer no reparte ni baja ninguna nvngx_dlssnr.dll: la pone cada usuario.
// Se compila con las cabeceras publicas de NGX (github.com/NVIDIA/DLSS, carpeta include), que no van en el repositorio
// (scripts\descargar_cabeceras_ngx.ps1).
// Sin el nucleo de NGX: el bloque de parametros es propio (NVSDK_NGX_Parameter es una interfaz Set/Get por nombre), asi no choca con
// el NGX que ya arranca Streamline en el motor. La DLL de NVIDIA no se redistribuye: se carga de la carpeta que se le diga.
// API en C (P/Invoke desde C#): PNR_Init, PNR_Create, PNR_Evaluate, PNR_Release, PNR_Shutdown, PNR_LastError.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <nvsdk_ngx_defs.h>
#include <nvsdk_ngx_params.h>
#include <string>
#include <map>
#include <cstring>
#include <cstdio>

namespace {

// ---------------------------------------------------------------- parametros propios
class Params final : public NVSDK_NGX_Parameter {
	enum class T { U64, F32, F64, U32, I32, R11, R12, Ptr };
	struct V { T t; union { unsigned long long u64; float f; double d; unsigned int u; int i; void* p; }; };
	std::map<std::string, V> m;
	void Put(const char* n, V v) { if (n) m[n] = v; }
	const V* Find(const char* n) const { if (!n) return nullptr; auto it = m.find(n); return it == m.end() ? nullptr : &it->second; }
	template<class O> NVSDK_NGX_Result GetNum(const char* n, O* out) const {
		const V* v = Find(n); if (!v || !out) return NVSDK_NGX_Result_Fail;
		switch (v->t) {
		case T::U64: *out = (O)v->u64; break; case T::F32: *out = (O)v->f; break; case T::F64: *out = (O)v->d; break;
		case T::U32: *out = (O)v->u; break; case T::I32: *out = (O)v->i; break; default: return NVSDK_NGX_Result_Fail;
		}
		return NVSDK_NGX_Result_Success;
	}
	NVSDK_NGX_Result GetPtr(const char* n, void** out) const {
		const V* v = Find(n); if (!v || !out) return NVSDK_NGX_Result_Fail;
		if (v->t == T::R11 || v->t == T::R12 || v->t == T::Ptr) { *out = v->p; return NVSDK_NGX_Result_Success; }
		if (v->t == T::U64) { *out = (void*)v->u64; return NVSDK_NGX_Result_Success; }
		return NVSDK_NGX_Result_Fail;
	}
public:
	void Set(const char* n, unsigned long long x) override { V v; v.t = T::U64; v.u64 = x; Put(n, v); }
	void Set(const char* n, float x) override { V v; v.t = T::F32; v.f = x; Put(n, v); }
	void Set(const char* n, double x) override { V v; v.t = T::F64; v.d = x; Put(n, v); }
	void Set(const char* n, unsigned int x) override { V v; v.t = T::U32; v.u = x; Put(n, v); }
	void Set(const char* n, int x) override { V v; v.t = T::I32; v.i = x; Put(n, v); }
	void Set(const char* n, ID3D11Resource* x) override { V v; v.t = T::R11; v.p = x; Put(n, v); }
	void Set(const char* n, ID3D12Resource* x) override { V v; v.t = T::R12; v.p = x; Put(n, v); }
	void Set(const char* n, void* x) override { V v; v.t = T::Ptr; v.p = x; Put(n, v); }
	NVSDK_NGX_Result Get(const char* n, unsigned long long* o) const override { return GetNum(n, o); }
	NVSDK_NGX_Result Get(const char* n, float* o) const override { return GetNum(n, o); }
	NVSDK_NGX_Result Get(const char* n, double* o) const override { return GetNum(n, o); }
	NVSDK_NGX_Result Get(const char* n, unsigned int* o) const override { return GetNum(n, o); }
	NVSDK_NGX_Result Get(const char* n, int* o) const override { return GetNum(n, o); }
	NVSDK_NGX_Result Get(const char* n, ID3D11Resource** o) const override { return GetPtr(n, (void**)o); }
	NVSDK_NGX_Result Get(const char* n, ID3D12Resource** o) const override { return GetPtr(n, (void**)o); }
	NVSDK_NGX_Result Get(const char* n, void** o) const override { return GetPtr(n, o); }
	void Reset() override { m.clear(); }
};

// ---------------------------------------------------------------- exportaciones de la DLL de NVIDIA
using InitExtFn = NVSDK_NGX_Result(NVSDK_CONV*)(unsigned long long, const wchar_t*, ID3D12Device*, NVSDK_NGX_Version, const NVSDK_NGX_Parameter*);
using CreateFn = NVSDK_NGX_Result(NVSDK_CONV*)(ID3D12GraphicsCommandList*, NVSDK_NGX_Feature, NVSDK_NGX_Parameter*, NVSDK_NGX_Handle**);
using EvalFn = NVSDK_NGX_Result(NVSDK_CONV*)(ID3D12GraphicsCommandList*, const NVSDK_NGX_Handle*, const NVSDK_NGX_Parameter*, PFN_NVSDK_NGX_ProgressCallback);
using ReleaseFn = NVSDK_NGX_Result(NVSDK_CONV*)(NVSDK_NGX_Handle*);
using ShutdownFn = NVSDK_NGX_Result(NVSDK_CONV*)(ID3D12Device*);

constexpr unsigned long long kAppId = 0x0876232Cull;              // "signed snippet" (el mismo que usa Magpie)
constexpr NVSDK_NGX_Feature kFeature = static_cast<NVSDK_NGX_Feature>(18);

HMODULE g_mod = nullptr;
InitExtFn g_init = nullptr; CreateFn g_create = nullptr; EvalFn g_eval = nullptr; ReleaseFn g_release = nullptr; ShutdownFn g_shutdown = nullptr;
ID3D12Device* g_device = nullptr;
bool g_inited = false;
struct Inst { Params p; NVSDK_NGX_Handle* h = nullptr; unsigned w = 0, hh = 0; };   // 20-11: una por pasada (2-3 pasadas)
constexpr int kMaxInst = 3;
Inst g_inst[kMaxInst];
char g_err[512] = "";

void Err(const char* fmt, unsigned long long a = 0, unsigned long long b = 0) { _snprintf_s(g_err, sizeof(g_err), _TRUNCATE, fmt, a, b); }

// ---------------------------------------------------------------- GetModuleFileNameW de la DLL: para nuestro modulo, "nvngx.dll"
using GmfnFn = DWORD(WINAPI*)(HMODULE, LPWSTR, DWORD);
GmfnFn g_realGmfn = nullptr; HMODULE g_self = nullptr; void** g_slot = nullptr; void* g_slotOld = nullptr;

DWORD WINAPI ShimGmfn(HMODULE module, LPWSTR name, DWORD size) {
	static const wchar_t kName[] = L"nvngx.dll"; const DWORD n = 9;
	if (module == g_self) {
		if (size == 0) return 0;
		if (size <= n) { for (DWORD i = 0; i + 1 < size; ++i) name[i] = kName[i]; name[size - 1] = 0; SetLastError(ERROR_INSUFFICIENT_BUFFER); return size; }
		for (DWORD i = 0; i <= n; ++i) name[i] = kName[i];
		return n;
	}
	return g_realGmfn ? g_realGmfn(module, name, size) : 0;
}

void** FindImport(HMODULE module, const char* fn) {
	auto* base = reinterpret_cast<BYTE*>(module);
	auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base); if (dos->e_magic != IMAGE_DOS_SIGNATURE) return nullptr;
	auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew); if (nt->Signature != IMAGE_NT_SIGNATURE) return nullptr;
	auto& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT]; if (!dir.VirtualAddress) return nullptr;
	for (auto* d = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + dir.VirtualAddress); d->Name; ++d) {
		const char* dll = reinterpret_cast<const char*>(base + d->Name);
		if (_stricmp(dll, "kernel32.dll") && _strnicmp(dll, "api-ms-", 7) && _strnicmp(dll, "ext-ms-", 7)) continue;
		ULONGLONG look = d->OriginalFirstThunk ? d->OriginalFirstThunk : d->FirstThunk; if (!look) continue;
		auto* lk = reinterpret_cast<ULONGLONG*>(base + look); auto* iat = reinterpret_cast<IMAGE_THUNK_DATA*>(base + d->FirstThunk);
		for (size_t i = 0; lk[i]; ++i) {
			if (IMAGE_SNAP_BY_ORDINAL64(lk[i])) continue;
			auto* byName = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + (size_t)lk[i]);
			if (!strcmp(reinterpret_cast<const char*>(byName->Name), fn)) return reinterpret_cast<void**>(&iat[i].u1.Function);
		}
	}
	return nullptr;
}

bool SwapSlot(void** slot, void* v, void** old) {
	DWORD prot = 0; if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &prot)) return false;
	void* prev = InterlockedExchangePointer(slot, v); if (old) *old = prev;
	DWORD ig = 0; VirtualProtect(slot, sizeof(void*), prot, &ig); FlushInstructionCache(GetCurrentProcess(), slot, sizeof(void*));
	return true;
}

// ---------------------------------------------------------------- llamadas protegidas (SEH: sin objetos con destructor dentro)
__declspec(noinline) NVSDK_NGX_Result SehInit(const wchar_t* dir, ID3D12Device* dev, DWORD* seh) {
	*seh = 0; __try { return g_init(kAppId, dir, dev, NVSDK_NGX_Version_API, nullptr); } __except (EXCEPTION_EXECUTE_HANDLER) { *seh = GetExceptionCode(); return NVSDK_NGX_Result_FAIL_PlatformError; }
}
__declspec(noinline) NVSDK_NGX_Result SehCreate(ID3D12GraphicsCommandList* cl, NVSDK_NGX_Parameter* p, NVSDK_NGX_Handle** h, DWORD* seh) {
	*seh = 0; __try { return g_create(cl, kFeature, p, h); } __except (EXCEPTION_EXECUTE_HANDLER) { *seh = GetExceptionCode(); return NVSDK_NGX_Result_FAIL_PlatformError; }
}
__declspec(noinline) NVSDK_NGX_Result SehEval(ID3D12GraphicsCommandList* cl, const NVSDK_NGX_Handle* h, const NVSDK_NGX_Parameter* p, DWORD* seh) {
	*seh = 0; __try { return g_eval(cl, h, p, nullptr); } __except (EXCEPTION_EXECUTE_HANDLER) { *seh = GetExceptionCode(); return NVSDK_NGX_Result_FAIL_PlatformError; }
}
__declspec(noinline) NVSDK_NGX_Result SehRelease(NVSDK_NGX_Handle* h, DWORD* seh) {
	*seh = 0; __try { return g_release(h); } __except (EXCEPTION_EXECUTE_HANDLER) { *seh = GetExceptionCode(); return NVSDK_NGX_Result_FAIL_PlatformError; }
}
__declspec(noinline) NVSDK_NGX_Result SehShutdown(ID3D12Device* d, DWORD* seh) {
	*seh = 0; __try { return g_shutdown(d); } __except (EXCEPTION_EXECUTE_HANDLER) { *seh = GetExceptionCode(); return NVSDK_NGX_Result_FAIL_PlatformError; }
}

NVSDK_NGX_Result NVSDK_CONV ScalingRatioCallback(NVSDK_NGX_Parameter* p) { if (p) p->Set("DLSSNR.ScalingRatio", 1.0f); return NVSDK_NGX_Result_Success; }

bool Ok(NVSDK_NGX_Result r) { return (r & 0xFFF00000) != 0xBAD00000; }

void Subrect(Params& p, const char* pre, unsigned w, unsigned h) {
	char k[64];
	_snprintf_s(k, _TRUNCATE, "DLSSNR.%sSubrectBaseX", pre); p.Set(k, 0u);
	_snprintf_s(k, _TRUNCATE, "DLSSNR.%sSubrectBaseY", pre); p.Set(k, 0u);
	_snprintf_s(k, _TRUNCATE, "DLSSNR.%sSubrectWidth", pre); p.Set(k, w);
	_snprintf_s(k, _TRUNCATE, "DLSSNR.%sSubrectHeight", pre); p.Set(k, h);
}

Inst* Get(int i) { return i >= 0 && i < kMaxInst ? &g_inst[i] : nullptr; }

} // namespace

extern "C" {

__declspec(dllexport) const char* __stdcall PNR_LastError() { return g_err; }

// runtimeDir: la carpeta con nvngx_dlssnr.dll (p. ej. <app>\magpie). 1 = bien
__declspec(dllexport) int __stdcall PNR_Init(ID3D12Device* device, const wchar_t* runtimeDir) {
	if (g_inited) return 1;
	if (!device || !runtimeDir) { Err("PNR_Init: argumentos"); return 0; }
	std::wstring dll = std::wstring(runtimeDir) + L"\\nvngx_dlssnr.dll";
	if (GetFileAttributesW(dll.c_str()) == INVALID_FILE_ATTRIBUTES) { Err("no esta nvngx_dlssnr.dll en la carpeta"); return 0; }
	if (!g_mod) {
		g_mod = LoadLibraryExW(dll.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
		if (!g_mod) { Err("LoadLibraryExW(nvngx_dlssnr.dll) Win32=%llu", GetLastError()); return 0; }
		g_init = (InitExtFn)GetProcAddress(g_mod, "NVSDK_NGX_D3D12_Init_Ext");
		g_create = (CreateFn)GetProcAddress(g_mod, "NVSDK_NGX_D3D12_CreateFeature");
		g_eval = (EvalFn)GetProcAddress(g_mod, "NVSDK_NGX_D3D12_EvaluateFeature");
		g_release = (ReleaseFn)GetProcAddress(g_mod, "NVSDK_NGX_D3D12_ReleaseFeature");
		g_shutdown = (ShutdownFn)GetProcAddress(g_mod, "NVSDK_NGX_D3D12_Shutdown1");
		if (!g_init || !g_create || !g_eval || !g_release || !g_shutdown) { Err("faltan exportaciones en nvngx_dlssnr.dll"); return 0; }
		GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, reinterpret_cast<LPCWSTR>(&ShimGmfn), &g_self);
		g_slot = FindImport(g_mod, "GetModuleFileNameW");
		if (!g_slot || !*g_slot) { Err("nvngx_dlssnr.dll no importa GetModuleFileNameW"); return 0; }
		g_realGmfn = reinterpret_cast<GmfnFn>(*g_slot);
		if (!SwapSlot(g_slot, reinterpret_cast<void*>(&ShimGmfn), &g_slotOld)) { Err("no se pudo cambiar GetModuleFileNameW"); return 0; }
	}
	DWORD seh = 0;
	NVSDK_NGX_Result r = SehInit(runtimeDir, device, &seh);
	if (seh) { Err("Init_Ext SEH 0x%llx", seh); return 0; }
	if (!Ok(r)) { Err("Init_Ext fallo 0x%llx", (unsigned)r); return 0; }
	g_device = device; g_inited = true; g_err[0] = 0;
	return 1;
}

__declspec(dllexport) void __stdcall PNR_ReleaseN(int idx) {
	Inst* in = Get(idx); if (!in) return;
	if (in->h) { DWORD s; SehRelease(in->h, &s); in->h = nullptr; }
}

// Crea la instancia idx (0-2: una por pasada) de DLSSNR a w x h (entrada = salida). preset: DLSSNR.Hint.Render.Preset (dlss5_modelo de
// Prisma). La lista se ejecuta despues por quien llama (y hay que dejar una imagen antes del primer Evaluate). 1 = bien
__declspec(dllexport) int __stdcall PNR_CreateN(int idx, ID3D12GraphicsCommandList* cl, unsigned w, unsigned h, int preset) {
	if (!g_inited) { Err("PNR_Create sin PNR_Init"); return 0; }
	Inst* in = Get(idx); if (!in) { Err("PNR_Create: instancia %llu fuera de rango", (unsigned)idx); return 0; }
	PNR_ReleaseN(idx);
	Params& p = in->p;
	p.Reset();
	p.Set("DLSSNR.Width", w); p.Set("DLSSNR.Height", h);
	p.Set("DLSSNR.InputWidth", w); p.Set("DLSSNR.InputHeight", h);
	p.Set("DLSSNR.OutputWidth", w); p.Set("DLSSNR.OutputHeight", h);
	p.Set("DLSSNR.Output.Width", w); p.Set("DLSSNR.Output.Height", h);
	p.Set("DLSSNR.Upscaling", 0u); p.Set("DLSSNR.Scale", 1.0f); p.Set("DLSSNR.ScalingRatio", 1.0f);
	p.Set("DLSSNRComputeScalingRatioCallback", reinterpret_cast<void*>(&ScalingRatioCallback));
	p.Set("DLSSNR.Hint.Render.Preset", preset);
	p.Set("Width", w); p.Set("Height", h);
	p.Set("PerfQualityValue", 1);   // Balanced
	p.Set("CreationNodeMask", 1u); p.Set("VisibilityNodeMask", 1u);
	DWORD seh = 0; NVSDK_NGX_Handle* h0 = nullptr;
	NVSDK_NGX_Result r = SehCreate(cl, &p, &h0, &seh);
	if (seh) { Err("CreateFeature SEH 0x%llx", seh); return 0; }
	if (!Ok(r) || !h0) { Err("CreateFeature fallo 0x%llx", (unsigned)r); return 0; }
	in->h = h0; in->w = w; in->hh = h; g_err[0] = 0;
	return 1;
}

// color (RGBA8, NON_PIXEL_SHADER_RESOURCE) -> output (RGBA8 o RGBA16F, UNORDERED_ACCESS), del tamaño de la instancia. mvec (RG16F,
// pixeles, de la imagen actual a la anterior) y depth (R32F), de mvW x mvH. Niveles = intensidad, tono local, estructura local, piel
// (dlss5_nivelN de Prisma). 1 = bien
__declspec(dllexport) int __stdcall PNR_EvaluateN(int idx, ID3D12GraphicsCommandList* cl, ID3D12Resource* color, ID3D12Resource* output,
	ID3D12Resource* mvec, ID3D12Resource* depth, unsigned mvW, unsigned mvH, float mvScaleX, float mvScaleY,
	int style, float intensity, float tone, float structure, float skin, int autoMask, int uiCorrection, int reset) {
	Inst* in = Get(idx);
	if (!in || !in->h) { Err("PNR_Evaluate sin PNR_Create (instancia %llu)", (unsigned)idx); return 0; }
	Params& p = in->p;
	p.Set("DLSSNR.Color", color); p.Set("DLSSNR.Output", output);
	p.Set("DLSSNR.MVec", mvec); p.Set("DLSSNR.Depth", depth);
	Subrect(p, "Color", in->w, in->hh); Subrect(p, "Output", in->w, in->hh); Subrect(p, "MVec", mvW, mvH); Subrect(p, "Depth", mvW, mvH);
	p.Set("DLSSNR.MVecScaleX", mvScaleX); p.Set("DLSSNR.MVecScaleY", mvScaleY);
	p.Set("DLSSNR.DepthInverted", 1);
	p.Set("DLSS.Indicator.Invert.X.Axis", 0); p.Set("DLSS.Indicator.Invert.Y.Axis", 0);
	p.Set("DLSSNR.Enabled", 1); p.Set("DLSSNR.Reset", reset ? 1 : 0);
	p.Set("DLSSNR.Style", style);
	p.Set("DLSSNR.Intensity", intensity); p.Set("DLSSNR.LocalToneStrength", tone);
	p.Set("DLSSNR.LocalStructureStrength", structure); p.Set("DLSSNR.SkinStructureStrength", skin);
	p.Set("DLSSNR.UseAutoMask", autoMask ? 1 : 0); p.Set("DLSSNR.UICorrection", uiCorrection ? 1 : 0);
	DWORD seh = 0;
	NVSDK_NGX_Result r = SehEval(cl, in->h, &p, &seh);
	if (seh) { Err("EvaluateFeature SEH 0x%llx", seh); return 0; }
	if (!Ok(r)) { Err("EvaluateFeature fallo 0x%llx", (unsigned)r); return 0; }
	return 1;
}

// La API de la primera version (una sola instancia = la 0)
__declspec(dllexport) int __stdcall PNR_Create(ID3D12GraphicsCommandList* cl, unsigned w, unsigned h, int preset) { return PNR_CreateN(0, cl, w, h, preset); }
__declspec(dllexport) int __stdcall PNR_Evaluate(ID3D12GraphicsCommandList* cl, ID3D12Resource* color, ID3D12Resource* output,
	ID3D12Resource* mvec, ID3D12Resource* depth, unsigned mvW, unsigned mvH, float mvScaleX, float mvScaleY,
	int style, float intensity, float tone, float structure, float skin, int autoMask, int uiCorrection, int reset) {
	return PNR_EvaluateN(0, cl, color, output, mvec, depth, mvW, mvH, mvScaleX, mvScaleY, style, intensity, tone, structure, skin, autoMask, uiCorrection, reset);
}
__declspec(dllexport) void __stdcall PNR_Release() { for (int i = 0; i < kMaxInst; i++) PNR_ReleaseN(i); }

__declspec(dllexport) void __stdcall PNR_Shutdown() {
	PNR_Release();
	if (g_inited && g_device) { DWORD s; SehShutdown(g_device, &s); }
	g_inited = false; g_device = nullptr;
	// (la DLL y el cambio de GetModuleFileNameW se quedan hasta que el proceso sale: soltarla con trabajo de GPU pendiente es peor)
}

} // extern "C"
