using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleEnhancer.Motor
{
    // Perfil de NVIDIA (DRS) solo para Switch2Engine.exe: sincronizacion vertical la que pida la aplicacion (el perfil
    // global de este PC la tiene forzada a "desactivada", y con eso hay desgarro y la generacion no puede sincronizar),
    // maximo rendimiento y 1 imagen en cola. No toca el perfil global ni los de otros programas.
    // (Copiado de NvProfiles de Switch2Magpie)
    static class NvDrs
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnVoid();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnOutHandle(out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnHandle(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnFindApp(IntPtr session, IntPtr appName, out IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnFindProfile(IntPtr session, IntPtr name, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnCreateProfile(IntPtr session, IntPtr info, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnCreateApp(IntPtr session, IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetSetting(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnSetSetting(IntPtr session, IntPtr profile, IntPtr setting);

        const uint IdInitialize = 0x0150E828, IdCreateSession = 0x0694D52E, IdDestroySession = 0xDAD9CFF8, IdLoadSettings = 0x375DBD6B,
                   IdSaveSettings = 0xFCBC7E14, IdFindApplicationByName = 0xEEE566B2, IdFindProfileByName = 0x7E4A9A0B,
                   IdCreateProfile = 0xCC176068, IdCreateApplication = 0x4347A9DE, IdGetSetting = 0x73BF8338, IdSetSetting = 0x577DD202;

        public const uint VsyncMode = 0x00A879CF, VsyncAppControlled = 0x60925292;
        const uint PowerManagementMode = 0x1057EB71, PreRenderLimit = 0x007BA09E;
        static readonly uint[][] Wanted = { new[] { VsyncMode, VsyncAppControlled }, new[] { PowerManagementMode, 1u }, new[] { PreRenderLimit, 1u } };

        const int StringBytes = 4096, SettingSize = 12320, SettingIdOffset = 4100, SettingLocationOffset = 4108, SettingCurrentOffset = 8220,
                  ProfileSize = 4116, ProfileGpuSupportOffset = 4100, AppSize = 12296, AppNameOffset = 8;
        const int NvOk = 0, NvProfileNotFound = -163, NvExecutableNotFound = -166;

        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = QueryInterface(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("NVAPI sin la funcion 0x" + id.ToString("X8"));
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
        static void Check(int rc, string what) { if (rc != NvOk) throw new InvalidOperationException(what + " -> NVAPI " + rc); }

        // 0 = ya estaba, 1 = cambiado (vale para los procesos que se abran despues), -1 = fallo, -2 = no se pudo guardar (administrador)
        public static int Ensure(string exe)
        {
            List<IntPtr> mem = new List<IntPtr>();
            IntPtr session = IntPtr.Zero;
            try
            {
                Check(Fn<FnVoid>(IdInitialize)(), "Initialize");
                Check(Fn<FnOutHandle>(IdCreateSession)(out session), "CreateSession");
                Check(Fn<FnHandle>(IdLoadSettings)(session), "LoadSettings");
                List<string> changes = new List<string>();
                IntPtr profile;
                int rc = Fn<FnFindApp>(IdFindApplicationByName)(session, Str(mem, exe), out profile, Alloc(mem, AppSize));
                if (rc == NvExecutableNotFound)
                {
                    string name = "ConsoleEnhancer - " + exe;   // (18-11, MOTOR-CE: antes "Switch 2 - "; el perfil se busca por el exe)
                    rc = Fn<FnFindProfile>(IdFindProfileByName)(session, Str(mem, name), out profile);
                    if (rc == NvProfileNotFound)
                    {
                        IntPtr info = Alloc(mem, ProfileSize);
                        WriteString(info, 4, name);
                        Marshal.WriteInt32(info, ProfileGpuSupportOffset, 1);
                        Check(Fn<FnCreateProfile>(IdCreateProfile)(session, info, out profile), "CreateProfile");
                    }
                    else Check(rc, "FindProfileByName");
                    IntPtr app = Alloc(mem, AppSize);
                    WriteString(app, AppNameOffset, exe);
                    Check(Fn<FnCreateApp>(IdCreateApplication)(session, profile, app), "CreateApplication");
                    changes.Add("perfil creado");
                }
                else Check(rc, "FindApplicationByName");
                foreach (uint[] want in Wanted)
                {
                    IntPtr s = Alloc(mem, SettingSize);
                    rc = Fn<FnGetSetting>(IdGetSetting)(session, profile, want[0], s);
                    bool own = rc == NvOk && Marshal.ReadInt32(s, SettingLocationOffset) == 0;
                    if (own && (uint)Marshal.ReadInt32(s, SettingCurrentOffset) == want[1]) continue;
                    IntPtr w = Alloc(mem, SettingSize);
                    Marshal.WriteInt32(w, SettingIdOffset, (int)want[0]);
                    Marshal.WriteInt32(w, SettingCurrentOffset, (int)want[1]);
                    int src = Fn<FnSetSetting>(IdSetSetting)(session, profile, w);
                    if (src != NvOk) { Log.Write("NVIDIA: ajuste 0x" + want[0].ToString("X8") + " no aceptado (NVAPI " + src + ")"); continue; }
                    changes.Add("0x" + want[0].ToString("X8") + " " + (own ? "0x" + ((uint)Marshal.ReadInt32(s, SettingCurrentOffset)).ToString("X") : "(heredado)") + " -> 0x" + want[1].ToString("X"));
                }
                if (changes.Count == 0) return 0;
                int save = Fn<FnHandle>(IdSaveSettings)(session);
                if (save != NvOk) { Log.Write("NVIDIA: no se pudo guardar el perfil de " + exe + " (NVAPI " + save + ")"); return -2; }
                Log.Write("NVIDIA " + exe + ": " + string.Join(", ", changes.ToArray()));
                return 1;
            }
            catch (Exception ex) { Log.Error("NVIDIA: perfil", ex); return -1; }
            finally
            {
                if (session != IntPtr.Zero) try { Fn<FnHandle>(IdDestroySession)(session); } catch { }
                foreach (IntPtr p in mem) Marshal.FreeHGlobal(p);
            }
        }

        static IntPtr Alloc(List<IntPtr> mem, int size)
        {
            IntPtr p = Marshal.AllocHGlobal(size);
            mem.Add(p);
            Marshal.Copy(new byte[size], 0, p, size);
            Marshal.WriteInt32(p, 0, size | (1 << 16));
            return p;
        }
        static IntPtr Str(List<IntPtr> mem, string s)
        {
            IntPtr p = Marshal.AllocHGlobal(StringBytes);
            mem.Add(p);
            Marshal.Copy(new byte[StringBytes], 0, p, StringBytes);
            WriteString(p, 0, s);
            return p;
        }
        static void WriteString(IntPtr block, int offset, string s)
        {
            byte[] b = Encoding.Unicode.GetBytes(s);
            Marshal.Copy(b, 0, new IntPtr(block.ToInt64() + offset), b.Length);
        }
    }
}
