// EngineSlProxy.cs - 23-11-2026 (1.2.4, ronda de arreglos, INTEGRA5). C# 5
//
// Con XeSS FG / FSR 3 FG y DLSS de escalador el motor usa Streamline solo para DLSS SR, con el enganche manual (eUseManualHooking,
// Engine.cs InitStreamline): la cadena es la de XeSS FG / FidelityFX y Streamline no ve ningun Present. Visto en la 1.2.4 del usuario
// (21:33-21:34, XeSS FG + DLSS SR a 1440p): sl.log "Streamline presentCommon() was not observed ... Without presentCommon(), internal
// bookkeeping and garbage collection will not run" y al cerrar el motor una excepcion dentro de slShutdown (al apagar sl.common) con un
// minivolcado de 2,6 MB en C:\ProgramData\NVIDIA\Streamline\ConsoleEnhancerEngine\ cada vez.
// La guia de Streamline para el enganche manual: la cadena se "mejora" con slUpgradeInterface nada mas crearla y el Present va por ese
// proxy (asi corre presentCommon cada imagen); slShutdown antes de soltar el dispositivo. Aqui: el proxy de Streamline envuelve la cadena
// de XeSS FG (la de GetSwapChainPtr) o la de FidelityFX, y ScPresent presenta por el; se suelta antes de slShutdown.
// sl_proxy_present=0 = como antes (Present directo a la cadena de XeSS FG / FidelityFX).
using System;
using System.Runtime.InteropServices;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    partial class Settings
    {
        public bool SlProxyPresent = true;   // sl_proxy_present=0|1
    }

    unsafe partial class Engine
    {
        IntPtr slProxySc;                    // el proxy de Streamline sobre la cadena (solo con el enganche manual)
        ScPresentFn slProxyPresent;

        // InitDevice, con la cadena ya creada (y los buffers pedidos)
        void SlProxyCreate()
        {
            if (!slOn || !slSoloDlss || !s.SlProxyPresent || swap == null || slProxySc != IntPtr.Zero) return;
            IntPtr baseSc = IntPtr.Zero;
            try
            {
                if (xeProxySc != IntPtr.Zero && s.XeRaw) { baseSc = xeProxySc; Marshal.AddRef(baseSc); }
                else baseSc = Marshal.GetComInterfaceForObject(swap, typeof(IDXGISwapChain3));   // (AddRef: la referencia pasa al proxy)
                IntPtr p = baseSc;
                SlResult r = Sl.UpgradeInterface(ref p);
                if (r != SlResult.eOk || p == IntPtr.Zero || p == baseSc)
                {
                    Log.Write("Streamline (solo DLSS SR): la cadena sin su proxy (" + r + "): Present directo, como antes");
                    Marshal.Release(baseSc);
                    return;
                }
                slProxySc = p;
                slProxyPresent = (ScPresentFn)Slot(p, 8, typeof(ScPresentFn));
                Log.Write("Streamline (solo DLSS SR, enganche manual): el Present va por su proxy de la cadena (" + (xeFgOn ? "XeSS FG" : ffxFgOn ? "FSR 3 FG" : "DXGI") + "), para su presentCommon");
            }
            catch (Exception ex)
            {
                Log.Write("Streamline: el proxy de la cadena no se pudo crear: " + ex.Message + " (Present directo)");
                slProxySc = IntPtr.Zero; slProxyPresent = null;
            }
        }

        // Dispose, ANTES de slShutdown (el proxy es codigo de Streamline)
        void SlProxyRelease()
        {
            if (slProxySc == IntPtr.Zero) return;
            try { Marshal.Release(slProxySc); } catch { }
            slProxySc = IntPtr.Zero; slProxyPresent = null;
        }
    }
}
