// CatalogoCapturas.cs - 22-11-2026 (1.2.2, agente UNIVERSAL): lo que se sabe de las capturadoras de verdad, como TABLA DE DATOS (ConsoleEnhancer)
//
// El usuario: "aprendete como son todas las elgato, avermedia, etc". Una fila por modelo; la deteccion (Capturadoras.cs) la empareja por
// VID:PID (exacto) o por el nombre de DirectShow (Patrones, el primero que coincide: los mas concretos antes) y el plan (Plan.cs) la usa:
//  - SIN abrirla: sus formatos previstos (Modos) para elegir captura y formato; el asistente los confirma al abrirla.
//  - Abierta: lo que lista manda, corregido con lo que el catalogo sabe que no da (Malos) y sus rarezas (al informe).
// Sintaxis:
//   Modos  = "<alto>p<fps>:<FORMATO>/<FORMATO> ..." lo mejor de cada alto (2160 / 1440 / 1080 / 720) SIN que haga falta su programa.
//            Formatos: YUY2 (4:2:2), NV12 (4:2:0), P010 (10 bits, HDR), RGB24, UYVY; comprimidos: MJPG, H264, HEVC.
//   Malos  = "<alto>:<FORMATO>=<fps reales>" (la GC573 a 4K en RGB24 lista 60 y da 53, medido el 18-11)
//   Datos(driver, modos, malos, latencia ms de la vista previa (0 = no se sabe), captura HDR, passthrough, rarezas)
// Cuentas de ancho de banda (por que las USB 3.0 de 5 Gbit/s no dan 4K60 sin comprimir): 4K60 NV12 = 6,0 Gbit/s, 4K30 NV12 = 3,0,
// 1440p60 NV12 = 2,7, 1080p60 YUY2 = 2,0, 1080p120 NV12 = 3,0. USB 2.0 (480 Mbit/s): ni 720p60 sin comprimir (0,9) -> MJPEG.
// Fuentes: ver docs del informe del agente UNIVERSAL (C:\CE_wt2\notas\universal.md): paginas de especificaciones de Elgato (help.elgato.com),
// AVerMedia (avermedia.com, "Specifications"), Magewell, Razer, Genki, j5create; listas de VID:PID (linux-hardware.org, usb-ids) y salidas
// reales de "ffmpeg -list_options true -f dshow" en foros. Lo que no esta confirmado va sin Modos (se elige al abrirla) o con "?".
using System;
using System.Collections.Generic;

namespace ConsoleEnhancer.Deteccion
{
    public static class CatalogoCapturas
    {
        static string[] P(params string[] p) { return p; }

        public static readonly List<ModeloCaptura> Modelos = new List<ModeloCaptura>
        {
            // ================================================================ AVerMedia (USB: VID 07CA; PCIe: 1461)
            new ModeloCaptura("AVerMedia", "Live Gamer 4K (GC573)", 0x1461, 0x0054, "PCIe", P("GC573", "Live Gamer 4K"), 3840, 2160, 60, false,
                              "4K60 / 1440p144 / 1080p240; YUY2, NV12, P010, RGB24", "audio en el mismo filtro DirectShow (pines «Audio» y «Audio Mixer»); Media Foundation no la abre (0xC00D36B4); a 4K en RGB24 llega a ~53 fps (medido)").Con(true, true)
                .Datos("propio: AVXGC573Cap", "2160p60:YUY2/NV12/P010/RGB24 1440p144:YUY2/NV12/P010/RGB24 1080p240:YUY2/NV12/P010/RGB24 720p60:YUY2/NV12/RGB24", "2160:RGB24=53",
                       0, true, "4K60 HDR, 1440p144, 1080p240", "").Id("avermedia-gc573"),
            new ModeloCaptura("AVerMedia", "Live Gamer 4K 2.1 (GC575)", 0, 0, "PCIe", P("GC575", "Live Gamer 4K 2.1"), 3840, 2160, 60, false, "4K60 HDR / 1440p144 / 1080p240", "").Con(true, true)
                .Datos("propio", "2160p60:YUY2/NV12/P010 1440p144:YUY2/NV12/P010 1080p240:YUY2/NV12/P010 720p60:YUY2/NV12", "", 0, true, "4K144 HDR, VRR", "").Id("avermedia-gc575"),
            new ModeloCaptura("AVerMedia", "Live Gamer Ultra 2.1 (GC553G2)", 0, 0, "USB3", P("GC553G2", "Ultra 2.1"), 3840, 2160, 60, false, "4K60 / 1440p120 / 1080p240", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p120:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "4K144 HDR, VRR", "USB 3.2 Gen 2 (10 Gbit/s) para lo mas alto; en un puerto de 5 Gbit/s da menos").Id("avermedia-gc553g2"),
            new ModeloCaptura("AVerMedia", "Live Gamer Ultra S (GC553Pro)", 0, 0, "USB3", P("GC553Pro", "Ultra S"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p60", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60 HDR", "").Id("avermedia-gc553pro"),
            new ModeloCaptura("AVerMedia", "Live Gamer Ultra (GC553)", 0, 0, "USB3", P("GC553", "Live Gamer Ultra"), 3840, 2160, 30, false, "4K30 / 1080p60 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p30:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60 HDR", "").Id("avermedia-gc553"),
            new ModeloCaptura("AVerMedia", "Live Gamer Bolt (GC555)", 0, 0, "TB3", P("GC555", "Live Gamer Bolt"), 3840, 2160, 60, false, "4K60 HDR / 1440p144 / 1080p240 (Thunderbolt 3)", "")
                .Datos("propio", "2160p60:NV12/YUY2/P010 1440p144:NV12/YUY2 1080p240:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "4K60 HDR, 1440p144, 1080p240", "pide un puerto Thunderbolt 3/4").Id("avermedia-gc555"),
            new ModeloCaptura("AVerMedia", "Live Gamer Extreme 3 (GC551G2)", 0, 0, "USB3", P("GC551G2", "Extreme 3"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p60", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60 HDR", "").Id("avermedia-gc551g2"),
            new ModeloCaptura("AVerMedia", "Live Gamer Extreme (GC550/GC551)", 0, 0, "USB3", P("GC550", "GC551", "Live Gamer Extreme"), 1920, 1080, 60, false, "1080p60", "")
                .Datos("uvc", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "4K60 (GC551)", "").Id("avermedia-gc551"),
            new ModeloCaptura("AVerMedia", "Live Gamer Duo (GC570D)", 0, 0, "PCIe", P("GC570D", "Live Gamer Duo"), 1920, 1080, 60, false, "1080p60 HDR (x2 entradas)", "").Con(true, true)
                .Datos("propio", "1080p60:YUY2/NV12/P010 720p60:YUY2/NV12", "", 0, true, "4K60 HDR", "").Id("avermedia-gc570d"),
            new ModeloCaptura("AVerMedia", "Live Gamer HD 2 (GC570)", 0, 0, "PCIe", P("GC570", "Live Gamer HD 2"), 1920, 1080, 60, false, "1080p60", "").Con(true, true)
                .Datos("propio", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "1080p60", "").Id("avermedia-gc570"),
            new ModeloCaptura("AVerMedia", "Live Gamer Portable 2 Plus (GC513)", 0, 0, "USB2", P("GC513", "Live Gamer Portable 2"), 1920, 1080, 60, true, "1080p60 comprimida (USB 2.0)", "USB 2.0: comprimida")
                .Datos("uvc", "1080p60:MJPG 720p60:MJPG", "", 0, false, "4K60", "USB 2.0: la imagen llega comprimida (mas CPU y latencia)").Id("avermedia-gc513"),
            new ModeloCaptura("AVerMedia", "Live Gamer Mini (GC311)", 0, 0, "USB2", P("GC311", "Live Gamer Mini"), 1920, 1080, 60, true, "1080p60 comprimida (USB 2.0)", "USB 2.0: puede ir comprimida")
                .Datos("uvc", "1080p60:MJPG 720p60:MJPG", "", 0, false, "1080p60", "USB 2.0: la imagen llega comprimida (mas CPU y latencia)").Id("avermedia-gc311"),
            new ModeloCaptura("AVerMedia", "Live Gamer (genérica)", 0x07CA, 0, "USB", P("Live Gamer", "AVerMedia"), 1920, 1080, 60, false, "", ""),

            // ================================================================ Elgato (VID 0FD9)
            new ModeloCaptura("Elgato", "4K X", 0, 0, "USB3", P("4K X"), 3840, 2160, 144, false, "4K144 / 1440p240 / 1080p240 HDR10 (UVC)", "")
                .Datos("uvc", "2160p60:NV12/P010 1440p120:NV12/P010 1080p240:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "4K144 HDR10, VRR",
                       "lo mas alto pide USB 3.2 Gen 2 (10 Gbit/s) directo a la placa; en un puerto de 5 Gbit/s ofrece menos").Id("elgato-4kx"),
            new ModeloCaptura("Elgato", "4K S", 0, 0, "USB3", P("4K S"), 3840, 2160, 60, false, "4K60 HDR10 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p120:NV12 720p60:NV12/YUY2", "", 0, true, "4K60 HDR10", "").Id("elgato-4ks"),
            new ModeloCaptura("Elgato", "HD60 X", 0x0FD9, 0x0082, "USB3", P("HD60 X"), 3840, 2160, 30, false, "1080p60 / 1440p60 / 4K30; NV12, YUY2 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "4K60 HDR10, 1440p120, 1080p240, VRR", "").Id("elgato-hd60x"),
            new ModeloCaptura("Elgato", "HD60 S+", 0, 0, "USB3", P("HD60 S+"), 1920, 1080, 60, false, "1080p60 HDR10 (driver de Elgato)", "")
                .Datos("propio: Elgato", "1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "4K60 HDR10",
                       "driver de Elgato (no UVC): instala 4K Capture Utility para que aparezca en DirectShow").Id("elgato-hd60s+"),
            new ModeloCaptura("Elgato", "HD60 S", 0, 0, "USB3", P("HD60 S"), 1920, 1080, 60, false, "1080p60 (driver de Elgato)", "")
                .Datos("propio: Elgato", "1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "1080p60", "driver de Elgato (no UVC)").Id("elgato-hd60s"),
            new ModeloCaptura("Elgato", "Game Capture Neo", 0, 0, "USB3", P("Neo"), 1920, 1080, 60, false, "1080p60 (UVC)", "")
                .Datos("uvc", "1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60 HDR", "").Id("elgato-neo"),
            new ModeloCaptura("Elgato", "4K60 Pro MK.2", 0, 0, "PCIe", P("4K60 Pro MK.2", "4K60 Pro MK2"), 3840, 2160, 60, false, "4K60 HDR10 / 1440p144 / 1080p240 (PCIe)", "")
                .Datos("propio: Elgato", "2160p60:NV12/YUY2/P010 1440p120:NV12/YUY2 1080p240:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "4K60 HDR10", "").Id("elgato-4k60promk2"),
            new ModeloCaptura("Elgato", "4K Pro", 0, 0, "PCIe", P("Game Capture 4K Pro", "Elgato 4K Pro"), 3840, 2160, 60, false, "4K60 HDR10 / 1440p120 / 1080p240 (PCIe)", "")
                .Datos("propio: Elgato", "2160p60:NV12/YUY2/P010 1440p120:NV12/YUY2/P010 1080p240:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "8K60, 4K240 HDR10, VRR", "").Id("elgato-4kpro"),
            new ModeloCaptura("Elgato", "4K60 Pro", 0, 0, "PCIe", P("4K60 Pro"), 3840, 2160, 60, false, "4K60 HDR10 (PCIe)", "")
                .Datos("propio: Elgato", "2160p60:NV12/YUY2 1440p60:NV12/YUY2 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "4K60 HDR10", "").Id("elgato-4k60pro"),
            new ModeloCaptura("Elgato", "HD60 Pro", 0, 0, "PCIe", P("HD60 Pro"), 1920, 1080, 60, false, "1080p60 (PCIe)", "")
                .Datos("propio: Elgato", "1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "1080p60", "").Id("elgato-hd60pro"),
            new ModeloCaptura("Elgato", "4K60 S+", 0, 0, "USB3", P("4K60 S+"), 3840, 2160, 60, false, "4K60 HDR10 (comprimida, HEVC)", "la captura llega comprimida")
                .Datos("propio: Elgato", "", "", 0, true, "4K60 HDR10",
                       "la captura al PC va comprimida en la propia tarjeta (mas latencia que las demas): para jugar mirando el PC, mejor otra").Id("elgato-4k60s+"),
            new ModeloCaptura("Elgato", "Cam Link 4K", 0x0FD9, 0x0066, "USB3", P("Cam Link 4K", "Cam Link"), 3840, 2160, 30, false, "4K30 / 1080p60; NV12, YUY2 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p30:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "-", "sin passthrough (la tele va por otra salida de la consola o un divisor HDMI)").Id("elgato-camlink4k"),
            new ModeloCaptura("Elgato", "Game Capture (genérica)", 0x0FD9, 0, "USB", P("Elgato", "Game Capture"), 1920, 1080, 60, false, "", ""),

            // ================================================================ otras
            new ModeloCaptura("Magewell", "USB Capture HDMI 4K Plus", 0x2935, 0, "USB3", P("USB Capture HDMI 4K"), 3840, 2160, 30, false, "4K30 / 1080p60 (UVC)", "")
                .Datos("uvc", "2160p30:NV12/YUY2 1440p60:NV12 1080p60:NV12/YUY2/RGB24 720p60:NV12/YUY2", "", 0, false, "4K60 (bucle)", "").Id("magewell-4kplus"),
            new ModeloCaptura("Magewell", "USB Capture HDMI", 0x2935, 0, "USB3", P("USB Capture HDMI", "Magewell"), 1920, 1080, 60, false, "1080p60; YUY2, NV12, RGB24 (UVC; 4K Plus: 4K30)", "")
                .Datos("uvc", "1080p60:YUY2/NV12/RGB24 720p60:YUY2/NV12/RGB24", "", 0, false, "1080p60 (bucle)", "").Id("magewell-usbhdmi"),
            new ModeloCaptura("Magewell", "Pro Capture (PCIe)", 0x1CD7, 0, "PCIe", P("Pro Capture"), 3840, 2160, 60, false, "", "")
                .Datos("propio: Magewell", "2160p60:YUY2/NV12 1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "", "").Id("magewell-pro"),
            new ModeloCaptura("j5create", "JVA04 (4K HDMI capture)", 0, 0, "USB3", P("JVA04"), 1920, 1080, 60, false, "1080p60 (UVC)", "")
                .Datos("uvc", "1080p60:YUY2/MJPG 720p60:YUY2/MJPG", "", 0, false, "4K60", "").Id("j5create-jva04"),
            new ModeloCaptura("j5create", "JVA02 (HDMI capture)", 0, 0, "USB2", P("JVA02"), 1920, 1080, 30, true, "1080p30 MJPEG (USB 2.0)", "")
                .Datos("uvc", "1080p30:MJPG 720p60:MJPG", "", 0, false, "-", "USB 2.0: la imagen llega comprimida").Id("j5create-jva02"),
            new ModeloCaptura("Genki", "ShadowCast 2 / 2 Pro", 0, 0, "USB3", P("ShadowCast 2"), 1920, 1080, 60, false, "1080p60 (UVC)", "")
                .Datos("uvc", "1080p60:YUY2/MJPG 720p60:YUY2/MJPG", "", 0, false, "-", "").Id("genki-shadowcast2"),
            new ModeloCaptura("Genki", "ShadowCast", 0, 0, "USB", P("ShadowCast"), 1920, 1080, 30, true, "", "")
                .Datos("uvc", "1080p30:MJPG 720p60:MJPG", "", 0, false, "-", "USB 2.0: la imagen llega comprimida").Id("genki-shadowcast"),
            new ModeloCaptura("Razer", "Ripsaw X", 0, 0, "USB3", P("Ripsaw X"), 3840, 2160, 30, false, "4K30 / 1080p60 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p30:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60", "").Id("razer-ripsawx"),
            new ModeloCaptura("Razer", "Ripsaw HD", 0, 0, "USB3", P("Ripsaw"), 1920, 1080, 60, false, "1080p60", "")
                .Datos("uvc", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "4K60", "").Id("razer-ripsawhd"),
            new ModeloCaptura("Hauppauge", "HD PVR / Colossus", 0x2040, 0, "", P("Hauppauge", "HD PVR", "Colossus"), 1920, 1080, 60, true, "", "suele llegar comprimida (H.264)")
                .Datos("propio: Hauppauge", "1080p60:H264", "", 0, false, "1080p60", "codificador H.264 por hardware: un segundo o mas de retraso, no sirve para jugar mirando el PC").Id("hauppauge"),
            new ModeloCaptura("Blackmagic", "DeckLink / UltraStudio", 0, 0, "", P("Decklink", "DeckLink", "Blackmagic", "UltraStudio"), 3840, 2160, 60, false, "UYVY / v210", "")
                .Datos("propio: Blackmagic", "", "", 0, false, "", "el modo de entrada tiene que coincidir con lo que manda la consola (1080p59,94, 2160p59,94...); UYVY: va por mpv"),
            new ModeloCaptura("EVGA", "XR1 / XR1 Lite / XR1 Pro", 0, 0, "USB3", P("XR1"), 1920, 1080, 60, false, "", "")
                .Datos("uvc", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "4K60", "").Id("evga-xr1"),
            new ModeloCaptura("NZXT", "Signal", 0, 0, "USB3", P("NZXT Signal"), 3840, 2160, 30, false, "", "")
                .Datos("uvc", "2160p30:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60", "").Id("nzxt-signal"),
            // MacroSilicon: las genericas ("USB Video", "USB3 Video", UGREEN, Hagibis, ezcap...): solo por VID:PID (sus nombres no dicen nada)
            new ModeloCaptura("MacroSilicon", "MS2109 (USB 2.0, genérica)", 0x534D, 0x2109, "USB2", new string[0], 1920, 1080, 30, true,
                              "MJPEG 1080p30 / 720p60; YUY2 solo a poca resolución", "USB 2.0: a 1080p solo MJPEG a 30 fps (más latencia y CPU)")
                .Datos("uvc", "1080p30:MJPG 720p60:MJPG 480p30:YUY2", "", 0, false, "-", "USB 2.0: MJPEG; su YUY2 a 1080p / 720p va a 5-10 fps").Id("ms2109"),
            new ModeloCaptura("MacroSilicon", "MS2130 (USB 3.0, genérica)", 0x345F, 0x2130, "USB3", new string[0], 1920, 1080, 60, false, "YUY2 1080p60; MJPEG hasta 4K30", "")
                .Datos("uvc", "1080p60:YUY2/MJPG 720p60:YUY2/MJPG 2160p30:MJPG", "", 0, false, "4K30-4K60 segun el aparato", "a 4K solo MJPEG (y la entrada 4K se reduce por dentro)").Id("ms2130"),
            new ModeloCaptura("MacroSilicon", "MS2130 (USB 3.0, genérica)", 0x534D, 0x2130, "USB3", new string[0], 1920, 1080, 60, false, "YUY2 1080p60; MJPEG hasta 4K30", "")
                .Datos("uvc", "1080p60:YUY2/MJPG 720p60:YUY2/MJPG 2160p30:MJPG", "", 0, false, "", "a 4K solo MJPEG"),
        };
    }
}
