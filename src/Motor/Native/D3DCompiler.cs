// D3DCompiler.cs - D3DCompile (System32\d3dcompiler_47.dll, shader models up to 5.1) and ID3DBlob, C# 5 / .NET 4.8.
using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace ConsoleEnhancer.Motor.Native
{
    // ID3D10Blob / ID3DBlob (d3dcommon.h)
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public interface ID3DBlob
    {
        /* 3*/ [PreserveSig] IntPtr GetBufferPointer();
        /* 4*/ [PreserveSig] UIntPtr GetBufferSize();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D_SHADER_MACRO                         // 16
    {
        public IntPtr Name;                                // LPCSTR
        public IntPtr Definition;                          // LPCSTR
    }

    public sealed class ShaderCompileException : Exception
    {
        public readonly string Messages;                   // compiler output (errors + warnings)
        public ShaderCompileException(int hr, string messages)
            : base("D3DCompile failed: " + Hr.Format(hr) + "\n" + messages)
        {
            Messages = messages; HResult = hr;
        }
    }

    public static class D3DCompilerApi
    {
        public const uint D3DCOMPILE_DEBUG = 1u << 0;
        public const uint D3DCOMPILE_SKIP_VALIDATION = 1u << 1;
        public const uint D3DCOMPILE_SKIP_OPTIMIZATION = 1u << 2;
        public const uint D3DCOMPILE_PACK_MATRIX_ROW_MAJOR = 1u << 3;
        public const uint D3DCOMPILE_PACK_MATRIX_COLUMN_MAJOR = 1u << 4;
        public const uint D3DCOMPILE_PARTIAL_PRECISION = 1u << 5;
        public const uint D3DCOMPILE_NO_PRESHADER = 1u << 8;
        public const uint D3DCOMPILE_AVOID_FLOW_CONTROL = 1u << 9;
        public const uint D3DCOMPILE_PREFER_FLOW_CONTROL = 1u << 10;
        public const uint D3DCOMPILE_ENABLE_STRICTNESS = 1u << 11;
        public const uint D3DCOMPILE_ENABLE_BACKWARDS_COMPATIBILITY = 1u << 12;
        public const uint D3DCOMPILE_IEEE_STRICTNESS = 1u << 13;
        public const uint D3DCOMPILE_OPTIMIZATION_LEVEL0 = 1u << 14;
        public const uint D3DCOMPILE_OPTIMIZATION_LEVEL1 = 0;
        public const uint D3DCOMPILE_OPTIMIZATION_LEVEL2 = (1u << 14) | (1u << 15);
        public const uint D3DCOMPILE_OPTIMIZATION_LEVEL3 = 1u << 15;
        public const uint D3DCOMPILE_WARNINGS_ARE_ERRORS = 1u << 18;
        public const uint D3DCOMPILE_RESOURCES_MAY_ALIAS = 1u << 19;
        public const uint D3DCOMPILE_ENABLE_UNBOUNDED_DESCRIPTOR_TABLES = 1u << 20;
        public const uint D3DCOMPILE_ALL_RESOURCES_BOUND = 1u << 21;
        public const uint D3DCOMPILE_DEBUG_NAME_FOR_SOURCE = 1u << 22;
        public const uint D3DCOMPILE_DEBUG_NAME_FOR_BINARY = 1u << 23;

        /// <summary>pInclude value that enables #include "file" relative to pSourceName.</summary>
        public static readonly IntPtr D3D_COMPILE_STANDARD_FILE_INCLUDE = new IntPtr(1);

        /// <summary>Raw export. pDefines = NULL-terminated D3D_SHADER_MACRO array or IntPtr.Zero; pInclude = IntPtr.Zero
        /// or D3D_COMPILE_STANDARD_FILE_INCLUDE. ppErrorMsgs gets warnings too (may be non-null on success).</summary>
        [DllImport("d3dcompiler_47.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int D3DCompile(byte[] pSrcData, UIntPtr SrcDataSize,
            [MarshalAs(UnmanagedType.LPStr)] string pSourceName, IntPtr pDefines, IntPtr pInclude,
            [MarshalAs(UnmanagedType.LPStr)] string pEntrypoint, [MarshalAs(UnmanagedType.LPStr)] string pTarget,
            uint Flags1, uint Flags2, out ID3DBlob ppCode, out ID3DBlob ppErrorMsgs);

        public static byte[] BlobToBytes(ID3DBlob blob)
        {
            if (blob == null) return null;
            int n = checked((int)blob.GetBufferSize().ToUInt64());
            byte[] b = new byte[n];
            if (n > 0) Marshal.Copy(blob.GetBufferPointer(), b, 0, n);
            return b;
        }

        public static string BlobToString(ID3DBlob blob)
        {
            if (blob == null) return "";
            int n = checked((int)blob.GetBufferSize().ToUInt64());
            if (n == 0) return "";
            string s = Marshal.PtrToStringAnsi(blob.GetBufferPointer(), n);
            return s.TrimEnd('\0', '\r', '\n');
        }

        /// <summary>Compiles HLSL (UTF-8) and returns the bytecode. Throws ShaderCompileException with the compiler
        /// output on failure. defines = { "NAME", "VALUE", "NAME2", "VALUE2", ... } or null.</summary>
        public static byte[] Compile(string source, string entryPoint, string target, uint flags, out string messages,
            string sourceName = null, string[] defines = null)
        {
            byte[] src = Encoding.UTF8.GetBytes(source);
            IntPtr pDefines = IntPtr.Zero;
            IntPtr[] strings = null;
            int count = defines == null ? 0 : defines.Length / 2;
            try
            {
                if (count > 0)
                {
                    int sz = Marshal.SizeOf(typeof(D3D_SHADER_MACRO));
                    pDefines = Marshal.AllocHGlobal(sz * (count + 1));
                    strings = new IntPtr[count * 2];
                    for (int i = 0; i < count; i++)
                    {
                        D3D_SHADER_MACRO m;
                        m.Name = strings[2 * i] = Marshal.StringToHGlobalAnsi(defines[2 * i]);
                        m.Definition = strings[2 * i + 1] = Marshal.StringToHGlobalAnsi(defines[2 * i + 1] ?? "1");
                        Marshal.StructureToPtr(m, pDefines + i * sz, false);
                    }
                    D3D_SHADER_MACRO end = new D3D_SHADER_MACRO();
                    Marshal.StructureToPtr(end, pDefines + count * sz, false);
                }
                ID3DBlob code, errors;
                int hr = D3DCompile(src, new UIntPtr((uint)src.Length), sourceName, pDefines, IntPtr.Zero,
                    entryPoint, target, flags, 0, out code, out errors);
                messages = BlobToString(errors);
                if (errors != null) Marshal.ReleaseComObject(errors);
                if (hr < 0 || code == null)
                {
                    if (code != null) Marshal.ReleaseComObject(code);
                    throw new ShaderCompileException(hr, messages);
                }
                byte[] result = BlobToBytes(code);
                Marshal.ReleaseComObject(code);
                return result;
            }
            finally
            {
                if (strings != null) foreach (IntPtr p in strings) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
                if (pDefines != IntPtr.Zero) Marshal.FreeHGlobal(pDefines);
            }
        }

        public static byte[] Compile(string source, string entryPoint, string target, uint flags = D3DCOMPILE_OPTIMIZATION_LEVEL3)
        {
            string messages;
            return Compile(source, entryPoint, target, flags, out messages);
        }
    }
}
