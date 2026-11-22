// PrismaFg.dll - 22-11-2026 (ConsoleEnhancer 1.2.4). Pieza propia del motor, como PrismaNr: se publica desde la 1.2.4 por decision del
// usuario (va en el repositorio y en el zip). Solo hace algo si el usuario ha puesto el paquete dlssg_sm86, que la app no reparte ni baja.
//
// Que hace: DLSS FG x3 / x4 en el motor propio en una RTX 40 (o x2 en RTX 30) con el paquete comunitario dlssg_sm86 (0.4.0, el
// mismo que usa Magpie 0.6.12). Igual que Magpie (Magpie.Core\DlssgSm86Runtime.cpp): copia el paquete a una cache propia con su
// dlssg_sm86.ini (forward.ini en SM 8.9, native.ini en SM 8.6), carga su version.dll (el proxy) y pide nvngx_dlssg.dll desde una carpeta
// vacia para que el proxy cargue el runtime del paquete. A partir de ahi, cuando NGX (por Streamline) carga nvngx_dlssg.dll, el proxy le
// da el del paquete, y DLSS-G dice numFramesToGenerateMax = 3. Esto SE SALTA las comprobaciones de NVIDIA (la licencia de los SDK RTX no
// lo permite). El motor (src\Motor\Engine\EngineFgMulti.cs) solo llama aqui si esta la DLL y con fg_mult=3|4.
//
// API (stdcall):
//   int  PFG_Prepare(const wchar_t* packageDir, const wchar_t* cacheDir, unsigned luidLow, int luidHigh)  1 = listo, 0 = no (PFG_Info)
//        packageDir vacio = el primero que este entero de <app>\extras\runtimes\dlssg_sm86, <app>\magpie\runtimes\dlssg_sm86 y
//        %LOCALAPPDATA%\ConsoleEnhancer\extras\runtimes\dlssg_sm86 (<app> = la carpeta de encima de engine\); cacheDir vacio =
//        %LOCALAPPDATA%\ConsoleEnhancer\PrismaFg
//        luid 0 = la primera NVIDIA. Hay que llamarla ANTES de slInit / slSetD3DDevice (antes de que NGX cargue nvngx_dlssg.dll).
//   int  PFG_Check()       tras slSetD3DDevice: cuantas nvngx_dlssg.dll hay cargadas (1 = la del paquete y solo esa; PFG_Info lo dice)
//   const wchar_t* PFG_Info()   el ultimo mensaje (para el log del motor)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <bcrypt.h>
#include <string>
#include <vector>
#include <cwchar>
#pragma comment(lib, "bcrypt.lib")

extern "C" __declspec(dllexport) const wchar_t* __stdcall PFG_Info();

namespace {

std::wstring info;
HMODULE proxy = nullptr, runtime = nullptr;
std::wstring cacheRuntime;
std::vector<HANDLE> held;   // el paquete de la cache abierto (solo lectura compartida) hasta que se cierra el motor

struct PkgFile { const wchar_t* name; unsigned long long size; const char* sha256; };
// dlssg_sm86 0.4.0 (dlssg-release-0.4.0-x64.zip; los de Magpie 0.6.12, src\Shared\DlssgSm86Package.h)
const PkgFile Files[] = {
    { L"version.dll", 853280, "7b3f90435be4f94348bf8d128c84871c7f904c704f7690601a263277c327a7b2" },
    { L"dlssg_sm86\\backend\\sm86_backend.dll", 2109728, "30ffc6e09a347ccde87ecc6fa54fd7818f5ed8a9353f5c31c2a90835c7f0ca9b" },
    { L"dlssg_sm86\\kernels\\fg_kernels.dll", 18318624, "1394c2d5d9cc8938ba3e52b043048d20d6d4c13a019c161b11a3dfd16343ec24" },
    { L"dlssg_sm86\\runtime\\nvngx_dlssg.dll", 7506544, "cadee1aa6db3907babdc3285da2aebc4553d92b89c38ba3eae8abd8bba1bea72" },
};
// forward.ini / native.ini de Magpie 0.6.12 (mismos bytes); solo cambia KernelImage
const char IniHead[] =
    "; Managed by Magpie. Feature presets are submitted through NGX, not this INI.\n[General]\nEnabled=1\n\n[FrameGeneration]\nEngine=DLSS\n"
    "MaxGeneratedFrames=3\nForceGeneratedFrames=0\nForcePluginFrames=0\nImageApprox=0\n\n[DLSSG]\nOptimized=1\nPreset=Auto\n\n[Compatibility]\n"
    "Router=Auto\nKernelImage=";
const char IniTail[] =
    "\nAccelerateNewerGpus=1\nSpoofArchToGame=0\nForceSM86Route=0\nSimulateAmpere=0\nLaunchChains=0\nChainBlock0=0\n\n[Runtime]\nMode=Bundled\n\n"
    "[SuperResolution]\nMode=Off\n\n[Logging]\nLevel=2\nDirectory=dlssg_sm86\\logs\n";

int Fail(const std::wstring& m) { info = m; return 0; }

std::wstring Env(const wchar_t* n)
{
    wchar_t b[32768]; DWORD l = GetEnvironmentVariableW(n, b, 32768);
    return l && l < 32768 ? std::wstring(b, l) : std::wstring();
}

std::wstring ModPath(HMODULE m)
{
    std::wstring p(32768, L'\0');
    DWORD l = GetModuleFileNameW(m, &p[0], (DWORD)p.size());
    if (!l || l >= p.size()) return L"";
    p.resize(l);
    return p;
}

bool SamePath(const std::wstring& a, const std::wstring& b)
{
    wchar_t fa[MAX_PATH * 4], fb[MAX_PATH * 4];
    if (!GetFullPathNameW(a.c_str(), MAX_PATH * 4, fa, nullptr) || !GetFullPathNameW(b.c_str(), MAX_PATH * 4, fb, nullptr)) return false;
    return _wcsicmp(fa, fb) == 0;
}

std::string Sha256(const std::wstring& path, unsigned long long& size)
{
    size = 0;
    HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
    if (f == INVALID_HANDLE_VALUE) return "";
    LARGE_INTEGER li; if (GetFileSizeEx(f, &li)) size = (unsigned long long)li.QuadPart;
    BCRYPT_ALG_HANDLE alg = nullptr; BCRYPT_HASH_HANDLE h = nullptr;
    std::string out;
    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0 && BCryptCreateHash(alg, &h, nullptr, 0, nullptr, 0, 0) == 0)
    {
        std::vector<unsigned char> buf(1 << 20);
        DWORD rd = 0; bool ok = true;
        while (ReadFile(f, buf.data(), (DWORD)buf.size(), &rd, nullptr) && rd > 0)
            if (BCryptHashData(h, buf.data(), rd, 0) != 0) { ok = false; break; }
        unsigned char d[32];
        if (ok && BCryptFinishHash(h, d, 32, 0) == 0)
        {
            static const char* hx = "0123456789abcdef";
            for (int i = 0; i < 32; i++) { out += hx[d[i] >> 4]; out += hx[d[i] & 15]; }
        }
    }
    if (h) BCryptDestroyHash(h);
    if (alg) BCryptCloseAlgorithmProvider(alg, 0);
    CloseHandle(f);
    return out;
}

bool Same(const std::wstring& path, const PkgFile& e)
{
    unsigned long long sz;
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &a)) return false;
    if ((((unsigned long long)a.nFileSizeHigh << 32) | a.nFileSizeLow) != e.size) return false;
    return Sha256(path, sz) == e.sha256 && sz == e.size;
}

bool MakeDirs(const std::wstring& d)
{
    if (d.empty()) return false;
    DWORD a = GetFileAttributesW(d.c_str());
    if (a != INVALID_FILE_ATTRIBUTES) return (a & FILE_ATTRIBUTE_DIRECTORY) != 0;
    size_t p = d.find_last_of(L"\\/");
    if (p != std::wstring::npos && p > 2) MakeDirs(d.substr(0, p));
    return CreateDirectoryW(d.c_str(), nullptr) || GetLastError() == ERROR_ALREADY_EXISTS;
}

std::wstring Dir(const std::wstring& f) { size_t p = f.find_last_of(L"\\/"); return p == std::wstring::npos ? L"" : f.substr(0, p); }

// SM de la grafica por la API del driver de CUDA (nvcuda.dll del sistema; sin contexto de CUDA), como Magpie
bool Arch(unsigned luidLow, int luidHigh, int& major, int& minor)
{
    HMODULE cuda = LoadLibraryExW(L"nvcuda.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!cuda) return false;
    typedef int (WINAPI *Init)(unsigned); typedef int (WINAPI *Count)(int*); typedef int (WINAPI *Get)(int*, int);
    typedef int (WINAPI *Luid)(char*, unsigned*, int); typedef int (WINAPI *Attr)(int*, int, int);
    Init init = (Init)GetProcAddress(cuda, "cuInit"); Count count = (Count)GetProcAddress(cuda, "cuDeviceGetCount");
    Get get = (Get)GetProcAddress(cuda, "cuDeviceGet"); Luid luid = (Luid)GetProcAddress(cuda, "cuDeviceGetLuid");
    Attr attr = (Attr)GetProcAddress(cuda, "cuDeviceGetAttribute");
    bool ok = false;
    int n = 0;
    if (init && count && get && luid && attr && init(0) == 0 && count(&n) == 0)
        for (int i = 0; i < n && !ok; i++)
        {
            int dev = 0; LUID l{}; unsigned mask = 0;
            if (get(&dev, i) != 0 || luid((char*)&l, &mask, dev) != 0) continue;
            if ((luidLow || luidHigh) && (l.LowPart != luidLow || l.HighPart != luidHigh)) continue;
            ok = attr(&major, 75, dev) == 0 && attr(&minor, 76, dev) == 0;
        }
    FreeLibrary(cuda);   // (Magpie tambien la suelta; la del motor la carga quien la necesite)
    return ok;
}

}  // namespace

extern "C" __declspec(dllexport) const wchar_t* __stdcall PFG_Info() { return info.c_str(); }

extern "C" __declspec(dllexport) int __stdcall PFG_Prepare(const wchar_t* packageDir, const wchar_t* cacheDir, unsigned luidLow, int luidHigh)
{
    try
    {
        if (runtime) { info = L"ya preparado (" + cacheRuntime + L")"; return 1; }
        std::wstring la = Env(L"LOCALAPPDATA");
        // El paquete: el que diga el motor (fg_privado_dir=) o, por orden, extras y magpie de la instalacion y la copia de %LOCALAPPDATA%
        std::vector<std::wstring> cands;
        if (packageDir && *packageDir) cands.push_back(packageDir);
        else
        {
            HMODULE self = nullptr;
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (LPCWSTR)&PFG_Info, &self);
            std::wstring app = Dir(Dir(ModPath(self)));   // <app>\engine\PrismaFg.dll -> <app>
            if (!app.empty()) { cands.push_back(app + L"\\extras\\runtimes\\dlssg_sm86"); cands.push_back(app + L"\\magpie\\runtimes\\dlssg_sm86"); }
            cands.push_back(la + L"\\ConsoleEnhancer\\extras\\runtimes\\dlssg_sm86");
        }
        std::wstring pkg, why;
        for (const std::wstring& c : cands)
        {
            bool ok = true;
            for (const PkgFile& f : Files) if (!Same(c + L"\\" + f.name, f)) { ok = false; why += (why.empty() ? L"" : L"; ") + c + L" (" + f.name + L")"; break; }
            if (ok) { pkg = c; break; }
        }
        if (pkg.empty()) return Fail(L"no esta el paquete dlssg_sm86 0.4.0 completo: " + why);
        std::wstring root = cacheDir && *cacheDir ? cacheDir : la + L"\\ConsoleEnhancer\\PrismaFg";
        int major = 0, minor = 0;
        if (!Arch(luidLow, luidHigh, major, minor)) return Fail(L"no se sabe el SM de la grafica (CUDA del driver): sin x3/x4");
        const wchar_t* route = (major == 8 && minor == 6) ? L"native" : (major == 8 && minor == 9) ? L"forward" : nullptr;
        if (!route)
            return Fail(L"SM " + std::to_wstring(major) + L"." + std::to_wstring(minor) + (major >= 10 ? L": esta grafica ya tiene x3/x4 de NVIDIA, no hace falta" : L": sin ruta del paquete para este SM"));
        // Nada de generacion cargado antes (si NGX ya cargo la oficial, el proxy no la cambia)
        HMODULE mods[1024]; DWORD need = 0;
        if (!K32EnumProcessModules(GetCurrentProcess(), mods, sizeof(mods), &need)) return Fail(L"no se pueden ver los modulos del proceso");
        for (DWORD i = 0; i < need / sizeof(HMODULE) && i < 1024; i++)
        {
            std::wstring p = ModPath(mods[i]); size_t s = p.find_last_of(L"\\/"); std::wstring nm = s == std::wstring::npos ? p : p.substr(s + 1);
            if (_wcsnicmp(nm.c_str(), L"nvngx_dlssg", 11) == 0 || _wcsnicmp(nm.c_str(), L"sm86_backend", 12) == 0 || GetProcAddress(mods[i], "DlssgProxy_Name"))
                return Fail(L"ya hay una generacion cargada (" + p + L"): hay que llamar antes de slInit");
        }
        // Cache propia (una por ruta): el paquete y su dlssg_sm86.ini; solo se copia lo que no esta igual
        std::wstring cache = root + L"\\" + route;
        for (const PkgFile& f : Files)
        {
            std::wstring dst = cache + L"\\" + f.name;
            if (Same(dst, f)) continue;
            MakeDirs(Dir(dst));
            std::wstring tmp = dst + L"." + std::to_wstring(GetCurrentProcessId()) + L".tmp";
            if (!CopyFileW((pkg + L"\\" + f.name).c_str(), tmp.c_str(), FALSE)) return Fail(L"no se pudo copiar " + std::wstring(f.name) + L" a " + cache);
            bool ok = Same(tmp, f) && MoveFileExW(tmp.c_str(), dst.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
            DeleteFileW(tmp.c_str());
            if (!ok && !Same(dst, f)) return Fail(L"no se pudo dejar " + std::wstring(f.name) + L" en " + cache + L" (en uso por otro motor?)");
        }
        std::string ini = std::string(IniHead) + (wcscmp(route, L"forward") == 0 ? "PTX" : "Auto") + IniTail;
        std::wstring iniPath = cache + L"\\dlssg_sm86.ini";
        {
            bool same = false;
            HANDLE f = CreateFileW(iniPath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
            if (f != INVALID_HANDLE_VALUE)
            {
                std::string cur(4096, '\0'); DWORD rd = 0;
                if (ReadFile(f, &cur[0], (DWORD)cur.size(), &rd, nullptr)) { cur.resize(rd); same = cur == ini; }
                CloseHandle(f);
            }
            if (!same)
            {
                HANDLE w = CreateFileW(iniPath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
                DWORD wr = 0;
                if (w == INVALID_HANDLE_VALUE || !WriteFile(w, ini.data(), (DWORD)ini.size(), &wr, nullptr) || wr != ini.size()) { if (w != INVALID_HANDLE_VALUE) CloseHandle(w); return Fail(L"no se pudo escribir " + iniPath); }
                CloseHandle(w);
            }
        }
        MakeDirs(cache + L"\\dlssg_sm86\\logs");
        // La cache abierta (sin escritura) mientras viva el motor: nadie la cambia con el proxy cargado
        for (const PkgFile& f : Files) { HANDLE h = CreateFileW((cache + L"\\" + f.name).c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr); if (h != INVALID_HANDLE_VALUE) held.push_back(h); }
        { HANDLE h = CreateFileW(iniPath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr); if (h != INVALID_HANDLE_VALUE) held.push_back(h); }
        for (const PkgFile& f : Files) if (!Same(cache + L"\\" + f.name, f)) return Fail(L"la cache cambio antes de cargarla");
        proxy = LoadLibraryExW((cache + L"\\version.dll").c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!proxy) return Fail(L"no se pudo cargar el proxy version.dll (" + std::to_wstring(GetLastError()) + L")");
        if (!SamePath(ModPath(proxy), cache + L"\\version.dll") || !GetProcAddress(proxy, "DlssgProxy_Name")) return Fail(L"el proxy cargado no es el de la cache");
        // El proxy es perezoso: la peticion de nvngx_dlssg.dll desde una carpeta vacia propia le hace cargar el runtime del paquete
        std::wstring req = cache + L"\\request-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64());
        if (!CreateDirectoryW(req.c_str(), nullptr)) return Fail(L"no se pudo crear " + req);
        runtime = LoadLibraryExW((req + L"\\nvngx_dlssg.dll").c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
        DWORD le = GetLastError();
        RemoveDirectoryW(req.c_str());
        if (!runtime) return Fail(L"el proxy no cargo el runtime del paquete (" + std::to_wstring(le) + L")");
        cacheRuntime = ModPath(runtime);
        if (!SamePath(cacheRuntime, cache + L"\\dlssg_sm86\\runtime\\nvngx_dlssg.dll")) { std::wstring got = cacheRuntime; runtime = nullptr; return Fail(L"el runtime no salio del paquete: " + got); }
        HMODULE pin = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, (LPCWSTR)proxy, &pin);
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, (LPCWSTR)runtime, &pin);
        info = L"dlssg_sm86 0.4.0 de " + pkg + L" listo: SM " + std::to_wstring(major) + L"." + std::to_wstring(minor) + L", ruta " + route + L", cache " + cache;
        return 1;
    }
    catch (...) { return Fail(L"excepcion preparando dlssg_sm86"); }
}

extern "C" __declspec(dllexport) int __stdcall PFG_Check()
{
    try
    {
        HMODULE mods[1024]; DWORD need = 0;
        if (!K32EnumProcessModules(GetCurrentProcess(), mods, sizeof(mods), &need)) { info = L"no se pueden ver los modulos"; return -1; }
        int n = 0; bool backend = false; std::wstring l;
        for (DWORD i = 0; i < need / sizeof(HMODULE) && i < 1024; i++)
        {
            std::wstring p = ModPath(mods[i]); size_t s = p.find_last_of(L"\\/"); std::wstring nm = s == std::wstring::npos ? p : p.substr(s + 1);
            if (_wcsnicmp(nm.c_str(), L"nvngx_dlssg", 11) == 0) { n++; l += (l.empty() ? L"" : L"; ") + p; }
            if (_wcsnicmp(nm.c_str(), L"sm86_backend", 12) == 0) backend = true;
        }
        bool ours = n == 1 && runtime && SamePath(l, cacheRuntime);
        info = std::to_wstring(n) + L" nvngx_dlssg cargada(s): " + (l.empty() ? L"ninguna" : l) + (backend ? L"; backend del paquete cargado" : L"; SIN el backend del paquete") + (ours ? L" (bien: solo la del paquete)" : L" (OJO: no es solo la del paquete)");
        return ours ? 1 : 0;
    }
    catch (...) { info = L"excepcion mirando los modulos"; return -1; }
}
