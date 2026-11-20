// NvApi.cs - lo que la deteccion le pregunta al driver de NVIDIA (nvapi64.dll), sin escribir nada (ConsoleEnhancer)
//
// nvapi64.dll solo existe con un driver de NVIDIA: se carga con LoadLibrary y, si no esta (PC sin NVIDIA, o simulado), todo devuelve
// "no se sabe" sin errores. Ids de nvapi_interface.h (los mismos que usa el lanzador en NvProfiles, src\Lanzador\Switch2Magpie.cs, y el motor).
// Nada de perfiles aqui: los perfiles (Smooth Motion, sincronizacion) los pone la app al lanzar, como NvProfiles del lanzador.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ConsoleEnhancer.Deteccion
{
    public sealed class NvGpu
    {
        public IntPtr Handle;
        public int VendorId, DeviceId, SubSysId, Revision;
        public string Nombre = "";
        public int Arq;                 // NV_GPU_ARCHITECTURE_ID (0x190 = Ada); 0 = no se sabe
        public int Implementacion;      // NV_GPU_ARCH_IMPLEMENTATION_ID (0x07 = AD107...)
        public ulong MemoriaKb;
        public int Nucleos;
        public int TipoSistema;         // 1 portatil, 2 sobremesa, 0 no se sabe
    }

    public static class NvApi
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr QueryFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnVoid();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnEnumGpus([Out] IntPtr[] handles, out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGpuBytes(IntPtr gpu, [In, Out] byte[] data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnDriver(out uint ver, [In, Out] byte[] branch);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnPci(IntPtr gpu, out uint dev, out uint sub, out uint rev, out uint ext);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGpuU32(IntPtr gpu, out uint v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnEnumSettings([In, Out] uint[] ids, ref uint max);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnDispId([In] byte[] name, out uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnVrr(uint displayId, [In, Out] byte[] info);

        const uint IdInitialize = 0x0150E828, IdEnumPhysicalGPUs = 0xE5AC921F, IdGetFullName = 0xCEEE8E9F,
                   IdGetDriverAndBranch = 0x2926AAAD, IdGetArchInfo = 0xD8265D24, IdGetPciIds = 0x2DDFB66E,
                   IdGetFrameBuffer = 0x46FBEB03, IdGetCoreCount = 0xC7026A87, IdGetSystemType = 0xBAAABFCC,
                   IdEnumAvailableSettingIds = 0xF020614A, IdGetDisplayIdByName = 0xAE457190, IdGetVrrInfo = 0xDF8FDA57;

        // Ajuste oculto de Smooth Motion en el perfil del driver (ver NvProfiles en src\Lanzador\Switch2Magpie.cs: 0xB0D384C0)
        public const uint SmoothMotionSetting = 0xB0D384C0;

        static IntPtr mod;
        static QueryFn query;
        static bool tried, ok;
        public static string Error = "";

        // permitir=false: como si no hubiera NVIDIA (simulacion)
        public static bool Cargar(bool permitir)
        {
            if (!permitir) { Error = "desactivado (simulación)"; return false; }
            if (tried) return ok;
            tried = true;
            try
            {
                mod = Nat.LoadLibraryW("nvapi64.dll");
                if (mod == IntPtr.Zero) { Error = "nvapi64.dll no está (sin driver de NVIDIA)"; return false; }
                query = Nat.Fn<QueryFn>(mod, "nvapi_QueryInterface");
                if (query == null) { Error = "nvapi64.dll sin nvapi_QueryInterface"; return false; }
                FnVoid init = F<FnVoid>(IdInitialize);
                int r = init != null ? init() : -1;
                if (r != 0) { Error = "NvAPI_Initialize " + r; return false; }
                ok = true;
            }
            catch (Exception ex) { Error = ex.GetType().Name + ": " + ex.Message; ok = false; }
            return ok;
        }

        static T F<T>(uint id) where T : class
        {
            if (query == null) return null;
            IntPtr p = query(id);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        public static List<NvGpu> Gpus()
        {
            List<NvGpu> l = new List<NvGpu>();
            if (!ok) return l;
            try
            {
                FnEnumGpus en = F<FnEnumGpus>(IdEnumPhysicalGPUs);
                if (en == null) return l;
                IntPtr[] h = new IntPtr[64];
                int n;
                if (en(h, out n) != 0) return l;
                FnPci pci = F<FnPci>(IdGetPciIds);
                FnGpuBytes name = F<FnGpuBytes>(IdGetFullName), arch = F<FnGpuBytes>(IdGetArchInfo);
                FnGpuU32 fb = F<FnGpuU32>(IdGetFrameBuffer), cores = F<FnGpuU32>(IdGetCoreCount), sys = F<FnGpuU32>(IdGetSystemType);
                for (int i = 0; i < n && i < 64; i++)
                {
                    NvGpu g = new NvGpu();
                    g.Handle = h[i];
                    uint dev, sub, rev, ext, v;
                    if (pci != null && pci(h[i], out dev, out sub, out rev, out ext) == 0)
                    {
                        g.VendorId = (int)(dev & 0xFFFF); g.DeviceId = (int)(dev >> 16); g.SubSysId = (int)sub; g.Revision = (int)rev;
                    }
                    byte[] s = new byte[64];
                    if (name != null && name(h[i], s) == 0) g.Nombre = Nat.Ansi(s, 0, 64);
                    // NV_GPU_ARCH_INFO_V2: version, architecture, implementation, revision
                    byte[] ai = new byte[16];
                    BitConverter.GetBytes(16u | (2u << 16)).CopyTo(ai, 0);
                    if (arch != null && arch(h[i], ai) == 0) { g.Arq = BitConverter.ToInt32(ai, 4); g.Implementacion = BitConverter.ToInt32(ai, 8); }
                    if (fb != null && fb(h[i], out v) == 0) g.MemoriaKb = v;
                    if (cores != null && cores(h[i], out v) == 0) g.Nucleos = (int)v;
                    if (sys != null && sys(h[i], out v) == 0) g.TipoSistema = (int)v;
                    l.Add(g);
                }
            }
            catch (Exception ex) { Error = "Gpus: " + ex.Message; }
            return l;
        }

        // Version del driver: 61692 = 616.92 (0 = no se sabe) y la rama ("r615_00")
        public static int Driver(out string rama)
        {
            rama = "";
            if (!ok) return 0;
            try
            {
                FnDriver f = F<FnDriver>(IdGetDriverAndBranch);
                uint v; byte[] b = new byte[64];
                if (f != null && f(out v, b) == 0) { rama = Nat.Ansi(b, 0, 64); return (int)v; }
            }
            catch { }
            return 0;
        }

        // ¿Conoce el driver este ajuste de perfil? (lista de ajustes disponibles; no se escribe nada). -1 no se sabe, 0 no, 1 si
        public static int ConoceAjuste(uint id)
        {
            if (!ok) return -1;
            try
            {
                FnEnumSettings f = F<FnEnumSettings>(IdEnumAvailableSettingIds);
                if (f == null) return -1;
                uint[] ids = new uint[8192]; uint max = (uint)ids.Length;
                if (f(ids, ref max) != 0) return -1;
                for (int i = 0; i < max && i < ids.Length; i++) if (ids[i] == id) return 1;
                return 0;
            }
            catch { return -1; }
        }

        // VRR (G-SYNC) de una pantalla por su nombre de GDI (\\.\DISPLAY1). bits: 1 activo, 2 posible, 4 pedido, 16 en modo VRR. -1 = no se sabe
        public static int Vrr(string gdiName)
        {
            if (!ok || string.IsNullOrEmpty(gdiName)) return -1;
            try
            {
                FnDispId fid = F<FnDispId>(IdGetDisplayIdByName);
                FnVrr fv = F<FnVrr>(IdGetVrrInfo);
                if (fid == null || fv == null) return -1;
                byte[] nm = new byte[gdiName.Length + 1];
                System.Text.Encoding.ASCII.GetBytes(gdiName).CopyTo(nm, 0);
                uint did;
                if (fid(nm, out did) != 0) return -1;
                byte[] info = new byte[24];
                BitConverter.GetBytes(24u | (1u << 16)).CopyTo(info, 0);
                if (fv(did, info) != 0) return -1;
                return BitConverter.ToInt32(info, 4) & 0x1F;
            }
            catch { return -1; }
        }

        // Nombre de la arquitectura (NV_GPU_ARCHITECTURE_ID)
        public static string ArqNombre(int a)
        {
            if (a >= 0x1C0) return "posterior a Blackwell (0x" + a.ToString("X") + ")";
            if (a >= 0x1A0) return "Blackwell";
            if (a >= 0x190) return "Ada Lovelace";
            if (a >= 0x180) return "Hopper";
            if (a >= 0x170) return "Ampere";
            if (a >= 0x160) return "Turing";
            if (a >= 0x140) return "Volta";
            if (a >= 0x130) return "Pascal";
            if (a >= 0x110) return "Maxwell";
            if (a > 0) return "anterior a Maxwell (0x" + a.ToString("X") + ")";
            return "";
        }
    }
}
