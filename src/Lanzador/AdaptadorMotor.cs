// AdaptadorMotor.cs - 22-11-2026 (1.2.2, agente ERRORES): motor_adaptador del ini, con el LUID que tiene AHORA esa grafica.
// El asistente guarda motor_adaptador=<LUID en hex> de la grafica elegida, pero un LUID solo vale hasta que se reinicia Windows: el 22-11
// el motor decia en cada arranque "adapter_luid=0xF9CF: no hay ninguna grafica con ese LUID; se usa la de alto rendimiento" (la del
// usuario, la misma RTX 4060 Ti, era 0x10B5C desde el ultimo reinicio). Con una sola grafica da igual; con dos (un portatil con la
// integrada, dos tarjetas, una eGPU) el motor podia acabar en otra que la elegida. Ahora, si el LUID ya no esta, se mira en
// data\hardware.txt (lo escribe el asistente con ese mismo LUID) de que grafica era (fabricante:modelo) y al motor va el LUID de ahora de
// esa; si no se sabe, ninguno (la de alto rendimiento, como pasaba) y sin el aviso del motor. Una vez por sesion (DXGI, ~1 ms).
// C# 5
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    static class AdaptadorMotor
    {
        [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapters1Fn(IntPtr self, uint i, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDesc1Fn(IntPtr self, [Out] byte[] desc);
        static readonly Guid IidFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        const int SlotEnumAdapters1 = 12, SlotGetDesc1 = 10;   // (como DetectorGpu, src\Deteccion\Gpu.cs)

        static readonly object sync = new object();
        static string pedido, resuelto;

        // Lo que va en adapter_luid= ("" = nada: el motor coge la de alto rendimiento)
        public static string Luid(string ini)
        {
            string v = (ini ?? "").Trim();
            if (v.Length == 0) return "";
            lock (sync)
            {
                if (resuelto != null && pedido == v) return resuelto;
                pedido = v;
                resuelto = Resolver(v);
                return resuelto;
            }
        }

        sealed class Ad { public long Luid; public int Ven, Dev; public bool Soft; public string Nombre = ""; }

        static List<Ad> Ahora()
        {
            List<Ad> l = new List<Ad>();
            IntPtr fac = IntPtr.Zero;
            try
            {
                Guid iid = IidFactory1;
                if (CreateDXGIFactory1(ref iid, out fac) < 0 || fac == IntPtr.Zero) return l;
                EnumAdapters1Fn en = Nat.Vt<EnumAdapters1Fn>(fac, SlotEnumAdapters1);
                for (uint i = 0; i < 16; i++)
                {
                    IntPtr ad;
                    if (en(fac, i, out ad) < 0 || ad == IntPtr.Zero) break;
                    try
                    {
                        byte[] d = new byte[320];   // DXGI_ADAPTER_DESC1
                        if (Nat.Vt<GetDesc1Fn>(ad, SlotGetDesc1)(ad, d) < 0) continue;
                        Ad a = new Ad();
                        a.Nombre = Nat.Uni(d, 0, 128);
                        a.Ven = BitConverter.ToInt32(d, 256); a.Dev = BitConverter.ToInt32(d, 260);
                        a.Luid = ((long)BitConverter.ToInt32(d, 300) << 32) | BitConverter.ToUInt32(d, 296);
                        a.Soft = (BitConverter.ToUInt32(d, 304) & 2) != 0 || (a.Ven == 0x1414 && a.Dev == 0x8C);
                        l.Add(a);
                    }
                    finally { Nat.Release(ad); }
                }
            }
            finally { Nat.Release(fac); }
            return l;
        }

        static string Resolver(string v)
        {
            string h = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? v.Substring(2) : v;
            ulong u;
            if (!ulong.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out u) || u == 0)
            {
                // (antes iba al motor tal cual y su ulong.Parse fallaba con lo que no es hexadecimal)
                Log.Write("motor_adaptador=" + v + ": no es un LUID (hexadecimal): el motor coge la grafica de alto rendimiento");
                return "";
            }
            long want = (long)u;
            List<Ad> ads;
            try { ads = Ahora(); }
            catch (Exception ex) { Log.Error("motor_adaptador: mirando las graficas", ex); return v; }
            if (ads.Count == 0) return v;
            foreach (Ad a in ads) if (a.Luid == want) return v;   // (el mismo de cuando se guardo: sin reiniciar Windows desde el asistente)
            int ven, dev;
            string era = DeHardware(want, out ven, out dev);
            if (era != null)
                foreach (Ad a in ads)
                    if (!a.Soft && a.Ven == ven && a.Dev == dev)
                    {
                        string n = a.Luid.ToString("X");
                        Log.Write("motor_adaptador=" + v + ": ese LUID ya no existe (cambia al reiniciar Windows); era " + era + ": la misma grafica ahora es 0x" + n + " (" + a.Nombre + ")");
                        return n;
                    }
            Log.Write("motor_adaptador=" + v + ": ese LUID ya no existe (cambia al reiniciar Windows)" + (era != null ? " y no esta la grafica que era (" + era + ")" : "") +
                      ": el motor coge la de alto rendimiento");
            return "";
        }

        // En hardware.txt (el informe de la deteccion del asistente): "LUID 0x<hex>" en la linea de la memoria de cada grafica y, una o dos
        // lineas antes, "<fabricante> VVVV:DDDD, ..." (en los dos idiomas igual). Devuelve el nombre de la grafica (la linea de su bloque)
        static string DeHardware(long luid, out int ven, out int dev)
        {
            ven = dev = 0;
            try
            {
                string f = Ce.HardwareFile;
                if (!File.Exists(f)) return null;
                string[] ls = File.ReadAllLines(f);
                string marca = "LUID 0x" + luid.ToString("X");
                for (int i = 0; i < ls.Length; i++)
                {
                    int k = ls[i].IndexOf(marca, StringComparison.OrdinalIgnoreCase);
                    if (k < 0) continue;
                    string fin = ls[i].Substring(k + marca.Length);
                    if (fin.Length > 0 && Uri.IsHexDigit(fin[0])) continue;   // (0xF9CF no es 0xF9CF0)
                    for (int j = i - 1; j >= Math.Max(0, i - 3); j--)
                    {
                        Match m = Regex.Match(ls[j], @"\b([0-9A-Fa-f]{4}):([0-9A-Fa-f]{4})\b");
                        if (!m.Success) continue;
                        ven = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        dev = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        string nombre = j > 0 ? ls[j - 1].Trim() : "";
                        return (nombre.Length > 0 ? nombre + " " : "") + m.Groups[1].Value.ToUpperInvariant() + ":" + m.Groups[2].Value.ToUpperInvariant();
                    }
                    return null;
                }
            }
            catch (Exception ex) { Log.Error("motor_adaptador: leyendo " + Ce.HardwareFile, ex); }
            return null;
        }
    }
}
