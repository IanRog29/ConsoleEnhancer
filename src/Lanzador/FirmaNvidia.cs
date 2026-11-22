// FirmaNvidia.cs - (22-11-2026, 1.2.4, DLSS5LEGAL) de quien es la nvngx_dlssnr.dll que pone el usuario. C# 5.
// Hay dos que circulan con la misma version (310.9.16.0): la ORIGINAL de NVIDIA (firma Authenticode valida de NVIDIA Corporation;
// NVIDIA solo la hace funcionar en RTX 50) y una MODIFICADA por la comunidad para RTX 20/30/40 (sin firma: al cambiarla se pierde).
// Solo se mira y se dice: la app no reparte ninguna, no cambia ninguna y no se salta nada para saberlo (WinVerifyTrust de Windows).
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    static class FirmaNvidia
    {
        public const int Nvidia = 1, SinFirma = 0, Otra = -1;

        // Rapida (sin calcular el hash del fichero: solo lee el certificado que lleva): para los textos de la deteccion
        public static int Rapida(string f, out string firmante)
        {
            firmante = "";
            try
            {
                X509Certificate c = X509Certificate.CreateFromSignedFile(f);
                firmante = c.Subject ?? "";
                return firmante.IndexOf("O=NVIDIA Corporation", StringComparison.OrdinalIgnoreCase) >= 0 ? Nvidia : Otra;
            }
            catch { return SinFirma; }   // (sin tabla de certificados = sin firma)
        }

        // Completa: Windows comprueba la firma (WinVerifyTrust, el hash de todo el fichero: ~1 s con los 276 MB) y de quien es
        public static int Comprobar(string f, out string firmante)
        {
            int r = Rapida(f, out firmante);
            if (r == SinFirma) return SinFirma;
            uint hr = Verificar(f);
            if (hr != 0) { firmante += " (firma NO valida: 0x" + hr.ToString("X8") + ")"; return Otra; }
            return r;
        }

        public static string Texto(int r)
        {
            return r == Nvidia ? Tx.T("la original de NVIDIA (firma válida de NVIDIA Corporation)", "NVIDIA's original (valid NVIDIA Corporation signature)")
                 : r == SinFirma ? Tx.T("sin firma: modificada, no es la de NVIDIA tal cual", "unsigned: modified, not NVIDIA's file as released")
                 : Tx.T("firmada, pero no por NVIDIA o con la firma rota", "signed, but not by NVIDIA or with a broken signature");
        }

        // ---- WinVerifyTrust (WINTRUST_ACTION_GENERIC_VERIFY_V2), sin interfaz y sin ir a internet (solo lo que ya tiene Windows)
        static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        static uint Verificar(string f)
        {
            IntPtr path = Marshal.StringToCoTaskMemUni(f), pfi = IntPtr.Zero;
            try
            {
                WINTRUST_FILE_INFO fi = new WINTRUST_FILE_INFO();
                fi.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
                fi.pcwszFilePath = path;
                pfi = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fi, pfi, false);
                WINTRUST_DATA d = new WINTRUST_DATA();
                d.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
                d.dwUIChoice = 2;               // WTD_UI_NONE
                d.fdwRevocationChecks = 0;      // WTD_REVOKE_NONE
                d.dwUnionChoice = 1;            // WTD_CHOICE_FILE
                d.pFile = pfi;
                d.dwStateAction = 0;            // WTD_STATEACTION_IGNORE
                d.dwProvFlags = 0x1000;         // WTD_CACHE_ONLY_URL_RETRIEVAL (sin red)
                Guid g = GenericVerifyV2;
                return WinVerifyTrust(new IntPtr(-1), ref g, ref d);   // (INVALID_HANDLE_VALUE: sin ventana)
            }
            catch { return 0xFFFFFFFF; }
            finally
            {
                if (pfi != IntPtr.Zero) Marshal.FreeCoTaskMem(pfi);
                Marshal.FreeCoTaskMem(path);
            }
        }
    }
}
