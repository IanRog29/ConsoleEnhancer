using System;
using System.Globalization;
using System.IO;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 24-11 (1.3.2, SWITCH2: "a veces al aplicar perfiles hay un pantallazo negro 1 segundo"). Al entrar en un juego desde el HOME (o volver),
    // el lanzador manda en una tanda los ajustes del perfil ("engine set ... dlss5=1 ...") y el cambio de captura ("loadfile" a 1080p / 1440p).
    // Los ajustes se aplicaban YA y la imagen de antes (el HOME a 4K, repintada cada 250 ms o alguna que aun llegaba) se pintaba con ellos:
    // DLSS 5 se creaba al tamaño de la captura de ANTES (log del usuario: 01:24:55 "creado a 1152x648 (30 % de 3840x2160)" 303 ms, 01:29:25
    // "25 % de 3840x2160" 340 ms, 01:46:44 "35 % de 3840x2160" 184 ms) y, al llegar la primera imagen de la captura nueva, otra vez a su
    // tamaño (130-330 ms): la imagen parada 0,8-0,9 s tras llegar la del juego (01:24:55.490 llega, 56.26 se pinta; 01:40:28.44 / 29.27).
    // Ahora, con un cambio de captura pedido (y como mucho cambio_captura_espera_ms, de serie 1500):
    //  - las imagenes que llegan aun con el tamaño de antes no se pintan y la ultima no se repinta (lo de la tele: la ultima imagen tal cual);
    //  - mientras se espera la primera de la captura nueva, si el perfil lleva DLSS 5 y aun no se inicio en este motor, se inicia (PNR_Init:
    //    50-200 ms que antes iban despues de llegar la imagen).
    // motor_extra cambio_captura_espera=0 = como antes.
    partial class Settings
    {
        public bool CambioEspera = true;        // cambio_captura_espera
        public double CambioEsperaMs = 1500;    // cambio_captura_espera_ms

        bool ApplyCambioCaptura(string key, string val)
        {
            switch (key)
            {
                case "cambio_captura_espera": { string v = val.Trim().ToLowerInvariant(); CambioEspera = !(v == "0" || v == "no" || v == "false" || v == "off"); return true; }
                case "cambio_captura_espera_ms": { double d; if (double.TryParse(val.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) CambioEsperaMs = Math.Max(100, Math.Min(5000, d)); return true; }
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        long awaitSwitchAt;      // cuando llego el loadfile con el cambio de captura (Clock)
        int cambioTiradas;       // imagenes de la captura de antes no pintadas en este cambio (para el log)
        bool cambioLogged;

        // EngineMpv, loadfile: el cambio de captura pedido
        void CambioPedido()
        {
            awaitSwitchAt = Clock.Now;
            cambioTiradas = 0;
            cambioLogged = false;
        }

        // EngineMpv.FrameHook: ya llego la primera imagen de la captura nueva
        void CambioLlego(string aw)
        {
            if (cambioTiradas > 0 || cambioLogged)
                Log.Write("Cambio de captura a " + aw + ": " + cambioTiradas + " imagen(es) de la captura de antes sin pintar con los ajustes nuevos y sin repintar la ultima durante " +
                          Clock.SinceMs(awaitSwitchAt).ToString("0", CultureInfo.InvariantCulture) + " ms (cambio_captura_espera)");
        }

        bool CambioEsperando
        {
            get { return s.CambioEspera && awaitSwitch != null && awaitSwitchAt != 0 && Clock.SinceMs(awaitSwitchAt) < s.CambioEsperaMs; }
        }

        // Run, con una imagen recogida: true = es de la captura de antes y hay un cambio pedido: no se pinta (se suelta la ranura)
        bool CambioTira(int w, int h)
        {
            if (!CambioEsperando) return false;
            string aw = awaitSwitch;
            if (aw == null || aw == w + "x" + h || Capture.DsCapture.LastSizeSwap == aw + ">" + w + "x" + h) return false;
            cambioTiradas++;
            return true;
        }

        // Run, sin imagen nueva: true = no se repinta la ultima (se espera la de la captura nueva). De paso, DLSS 5 iniciado ya si hara falta
        bool CambioSinRepintar()
        {
            if (!CambioEsperando) return false;
            if (!cambioLogged) { cambioLogged = true; CambioIniciaDlss5(); }
            return true;
        }

        // PNR_Init mientras llega la captura nueva (lo mismo que RecordDlss5 haria con la primera imagen, antes)
        void CambioIniciaDlss5()
        {
            if (!s.Nr || nrInited || nrBroken || dev == null) return;
            try
            {
                if (!File.Exists(Path.Combine(s.NrDir, "nvngx_dlssnr.dll"))) return;   // (RecordDlss5 lo dira)
                long t = Clock.Now;
                if (PrismaNr.PNR_Init(Com.BorrowPtr(dev, typeof(ID3D12Device)), s.NrDir) == 0) { NrFail("PNR_Init: " + PrismaNr.LastError()); return; }
                nrInited = true;
                Log.Write("DLSS 5 (DLSSNR) iniciado desde " + s.NrDir + " mientras llega la captura nueva (" + Clock.SinceMs(t).ToString("0", CultureInfo.InvariantCulture) + " ms)");
            }
            catch (Exception ex) { NrFail("PrismaNr.dll: " + ex.Message); }
        }
    }
}
