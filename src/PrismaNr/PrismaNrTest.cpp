// PrismaNrTest.exe <entrada.png> <salida.png> [carpeta de nvngx_dlssnr.dll] [frames]
// Prueba de PrismaNr.dll sin el motor: D3D12 propio, la imagen por WIC, DLSSNR nivel 2, resultado a PNG. 20-11-2026.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wincodec.h>
#include <cstdio>
#include <vector>
#include <string>
#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "windowscodecs.lib")
#pragma comment(lib, "ole32.lib")

typedef int(__stdcall* InitF)(ID3D12Device*, const wchar_t*);
typedef int(__stdcall* CreateF)(ID3D12GraphicsCommandList*, unsigned, unsigned, int);
typedef int(__stdcall* EvalF)(ID3D12GraphicsCommandList*, ID3D12Resource*, ID3D12Resource*, ID3D12Resource*, ID3D12Resource*,
	unsigned, unsigned, float, float, int, float, float, float, float, int, int, int);
typedef const char*(__stdcall* ErrF)();
typedef void(__stdcall* ShutF)();

#define CHK(x) do { HRESULT _h = (x); if (FAILED(_h)) { printf("FALLO %s = 0x%08lx (linea %d)\n", #x, (unsigned long)_h, __LINE__); return 2; } } while (0)

static ID3D12Device* dev; static ID3D12CommandQueue* q; static ID3D12CommandAllocator* al; static ID3D12GraphicsCommandList* cl;
static ID3D12Fence* fence; static UINT64 fv = 0; static HANDLE ev;

static void Flush() { q->Signal(fence, ++fv); if (fence->GetCompletedValue() < fv) { fence->SetEventOnCompletion(fv, ev); WaitForSingleObject(ev, INFINITE); } }
static void Run() { cl->Close(); ID3D12CommandList* l[] = { cl }; q->ExecuteCommandLists(1, l); Flush(); al->Reset(); cl->Reset(al, nullptr); }

static ID3D12Resource* Tex(DXGI_FORMAT f, UINT w, UINT h, D3D12_RESOURCE_STATES st, bool uav) {
	D3D12_HEAP_PROPERTIES hp{}; hp.Type = D3D12_HEAP_TYPE_DEFAULT;
	D3D12_RESOURCE_DESC d{}; d.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D; d.Width = w; d.Height = h; d.DepthOrArraySize = 1; d.MipLevels = 1;
	d.Format = f; d.SampleDesc.Count = 1; d.Flags = uav ? D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS : D3D12_RESOURCE_FLAG_NONE;
	ID3D12Resource* r = nullptr; dev->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &d, st, nullptr, IID_PPV_ARGS(&r)); return r;
}
static ID3D12Resource* Buf(D3D12_HEAP_TYPE t, UINT64 size, D3D12_RESOURCE_STATES st) {
	D3D12_HEAP_PROPERTIES hp{}; hp.Type = t;
	D3D12_RESOURCE_DESC d{}; d.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER; d.Width = size; d.Height = 1; d.DepthOrArraySize = 1; d.MipLevels = 1;
	d.SampleDesc.Count = 1; d.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
	ID3D12Resource* r = nullptr; dev->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &d, st, nullptr, IID_PPV_ARGS(&r)); return r;
}
static void Barrier(ID3D12Resource* r, D3D12_RESOURCE_STATES a, D3D12_RESOURCE_STATES b) {
	D3D12_RESOURCE_BARRIER x{}; x.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION; x.Transition.pResource = r;
	x.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES; x.Transition.StateBefore = a; x.Transition.StateAfter = b; cl->ResourceBarrier(1, &x);
}
// Sube bytes (filas de 'bpp' bytes por punto) a una textura en COPY_DEST
static void Upload(ID3D12Resource* tex, DXGI_FORMAT f, UINT w, UINT h, UINT bpp, const void* data, ID3D12Resource** keep) {
	UINT pitch = (w * bpp + 255) & ~255u; ID3D12Resource* up = Buf(D3D12_HEAP_TYPE_UPLOAD, (UINT64)pitch * h, D3D12_RESOURCE_STATE_GENERIC_READ);
	BYTE* p = nullptr; up->Map(0, nullptr, (void**)&p);
	for (UINT y = 0; y < h; y++) { if (data) memcpy(p + (size_t)y * pitch, (const BYTE*)data + (size_t)y * w * bpp, w * bpp); else memset(p + (size_t)y * pitch, 0, w * bpp); }
	up->Unmap(0, nullptr);
	D3D12_TEXTURE_COPY_LOCATION dst{}; dst.pResource = tex; dst.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
	D3D12_TEXTURE_COPY_LOCATION src{}; src.pResource = up; src.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
	src.PlacedFootprint.Footprint.Format = f; src.PlacedFootprint.Footprint.Width = w; src.PlacedFootprint.Footprint.Height = h;
	src.PlacedFootprint.Footprint.Depth = 1; src.PlacedFootprint.Footprint.RowPitch = pitch;
	cl->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr); *keep = up;
}

int wmain(int argc, wchar_t** argv) {
	if (argc < 3) { printf("uso: PrismaNrTest entrada.png salida.png [carpeta_dll] [frames]\n"); return 1; }
	const wchar_t* dllDir = argc > 3 ? argv[3] : L"..\\magpie";   // (ConsoleEnhancer: bin\magpie junto a bin\engine)
	int frames = argc > 4 ? _wtoi(argv[4]) : 8;
	CoInitializeEx(nullptr, COINIT_MULTITHREADED);
	IWICImagingFactory* wic = nullptr; CHK(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)));
	IWICBitmapDecoder* dec = nullptr; CHK(wic->CreateDecoderFromFilename(argv[1], nullptr, GENERIC_READ, WICDecodeMetadataCacheOnLoad, &dec));
	IWICBitmapFrameDecode* fr = nullptr; CHK(dec->GetFrame(0, &fr));
	IWICFormatConverter* cv = nullptr; CHK(wic->CreateFormatConverter(&cv));
	CHK(cv->Initialize(fr, GUID_WICPixelFormat32bppRGBA, WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeCustom));
	UINT w = 0, h = 0; cv->GetSize(&w, &h);
	std::vector<BYTE> px((size_t)w * h * 4); CHK(cv->CopyPixels(nullptr, w * 4, (UINT)px.size(), px.data()));
	printf("imagen %ux%u\n", w, h);

	IDXGIFactory6* fac = nullptr; CHK(CreateDXGIFactory2(0, IID_PPV_ARGS(&fac)));
	IDXGIAdapter1* ad = nullptr; CHK(fac->EnumAdapterByGpuPreference(0, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(&ad)));
	CHK(D3D12CreateDevice(ad, D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&dev)));
	D3D12_COMMAND_QUEUE_DESC qd{}; qd.Type = D3D12_COMMAND_LIST_TYPE_DIRECT; CHK(dev->CreateCommandQueue(&qd, IID_PPV_ARGS(&q)));
	CHK(dev->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&al)));
	CHK(dev->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, al, nullptr, IID_PPV_ARGS(&cl)));
	CHK(dev->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence))); ev = CreateEventW(nullptr, FALSE, FALSE, nullptr);

	ID3D12Resource* color = Tex(DXGI_FORMAT_R8G8B8A8_UNORM, w, h, D3D12_RESOURCE_STATE_COPY_DEST, false);
	ID3D12Resource* out = Tex(DXGI_FORMAT_R8G8B8A8_UNORM, w, h, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, true);
	ID3D12Resource* mv = Tex(DXGI_FORMAT_R16G16_FLOAT, w, h, D3D12_RESOURCE_STATE_COPY_DEST, false);
	ID3D12Resource* dp = Tex(DXGI_FORMAT_R32_FLOAT, w, h, D3D12_RESOURCE_STATE_COPY_DEST, false);
	ID3D12Resource *u1, *u2, *u3;
	Upload(color, DXGI_FORMAT_R8G8B8A8_UNORM, w, h, 4, px.data(), &u1);
	Upload(mv, DXGI_FORMAT_R16G16_FLOAT, w, h, 4, nullptr, &u2);
	Upload(dp, DXGI_FORMAT_R32_FLOAT, w, h, 4, nullptr, &u3);
	Barrier(color, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
	Barrier(mv, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
	Barrier(dp, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
	Run();

	wchar_t self[MAX_PATH]; GetModuleFileNameW(nullptr, self, MAX_PATH); std::wstring sd(self); sd = sd.substr(0, sd.find_last_of(L'\\'));
	HMODULE m = LoadLibraryW((sd + L"\\PrismaNr.dll").c_str()); if (!m) { printf("no carga PrismaNr.dll\n"); return 3; }
	InitF pInit = (InitF)GetProcAddress(m, "PNR_Init"); CreateF pCreate = (CreateF)GetProcAddress(m, "PNR_Create");
	EvalF pEval = (EvalF)GetProcAddress(m, "PNR_Evaluate"); ErrF pErr = (ErrF)GetProcAddress(m, "PNR_LastError"); ShutF pShut = (ShutF)GetProcAddress(m, "PNR_Shutdown");
	if (!pInit || !pCreate || !pEval || !pErr || !pShut) { printf("faltan funciones en PrismaNr.dll\n"); return 3; }

	LARGE_INTEGER f0, t0, t1; QueryPerformanceFrequency(&f0);
	QueryPerformanceCounter(&t0);
	if (!pInit(dev, dllDir)) { printf("PNR_Init: %s\n", pErr()); return 4; }
	QueryPerformanceCounter(&t1); printf("PNR_Init bien (%.0f ms)\n", (t1.QuadPart - t0.QuadPart) * 1000.0 / f0.QuadPart);
	// dlss5_modelo=2 -> preset; dlss5_estilo=2; nivel 2 = 1.2,1.2,1.0,0.6
	int preset = 2;
	if (!pCreate(cl, w, h, preset)) { printf("PNR_Create: %s\n", pErr()); return 5; }
	Run(); printf("PNR_Create bien\n");
	double tot = 0;
	for (int i = 0; i < frames; i++) {
		QueryPerformanceCounter(&t0);
		if (!pEval(cl, color, out, mv, dp, w, h, 1.0f, 1.0f, 2, 1.2f, 1.2f, 1.0f, 0.6f, 1, 1, i == 0)) { printf("PNR_Evaluate %d: %s\n", i, pErr()); return 6; }
		D3D12_RESOURCE_BARRIER u{}; u.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV; u.UAV.pResource = out; cl->ResourceBarrier(1, &u);
		Run();
		QueryPerformanceCounter(&t1); double ms = (t1.QuadPart - t0.QuadPart) * 1000.0 / f0.QuadPart; if (i > 0) tot += ms;
		printf("evaluate %d: %.2f ms (CPU+GPU)\n", i, ms);
	}
	if (frames > 1) printf("media sin la primera: %.2f ms\n", tot / (frames - 1));

	// Leer la salida
	UINT pitch = (w * 4 + 255) & ~255u; ID3D12Resource* rb = Buf(D3D12_HEAP_TYPE_READBACK, (UINT64)pitch * h, D3D12_RESOURCE_STATE_COPY_DEST);
	Barrier(out, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COPY_SOURCE);
	D3D12_TEXTURE_COPY_LOCATION src{}; src.pResource = out; src.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
	D3D12_TEXTURE_COPY_LOCATION dst{}; dst.pResource = rb; dst.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
	dst.PlacedFootprint.Footprint.Format = DXGI_FORMAT_R8G8B8A8_UNORM; dst.PlacedFootprint.Footprint.Width = w;
	dst.PlacedFootprint.Footprint.Height = h; dst.PlacedFootprint.Footprint.Depth = 1; dst.PlacedFootprint.Footprint.RowPitch = pitch;
	cl->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr); Run();
	BYTE* p = nullptr; rb->Map(0, nullptr, (void**)&p);
	std::vector<BYTE> res((size_t)w * h * 4); double diff = 0;
	for (UINT y = 0; y < h; y++) { memcpy(&res[(size_t)y * w * 4], p + (size_t)y * pitch, w * 4); }
	rb->Unmap(0, nullptr);
	for (size_t i = 0; i < res.size(); i++) if (i % 4 != 3) diff += abs((int)res[i] - (int)px[i]);
	printf("diferencia media con la entrada: %.3f niveles\n", diff / ((double)w * h * 3));
	for (size_t i = 0; i < res.size(); i += 4) { BYTE t = res[i]; res[i] = res[i + 2]; res[i + 2] = t; res[i + 3] = 255; }   // RGBA -> BGRA (el PNG de WIC)

	IWICStream* st = nullptr; CHK(wic->CreateStream(&st)); CHK(st->InitializeFromFilename(argv[2], GENERIC_WRITE));
	IWICBitmapEncoder* enc = nullptr; CHK(wic->CreateEncoder(GUID_ContainerFormatPng, nullptr, &enc)); CHK(enc->Initialize(st, WICBitmapEncoderNoCache));
	IWICBitmapFrameEncode* fe = nullptr; CHK(enc->CreateNewFrame(&fe, nullptr)); CHK(fe->Initialize(nullptr)); CHK(fe->SetSize(w, h));
	WICPixelFormatGUID pf = GUID_WICPixelFormat32bppBGRA; CHK(fe->SetPixelFormat(&pf));
	CHK(fe->WritePixels(h, w * 4, (UINT)res.size(), res.data())); CHK(fe->Commit()); CHK(enc->Commit());
	printf("guardado\n");
	pShut();
	return 0;
}
