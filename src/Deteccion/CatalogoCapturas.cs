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
            // (las de UVC -GC553, GC553G2, GC553Pro, GC551G2, GC575, GC571, GC311...- son de un solo programa a la vez: con OBS / RECentral
            // abiertos dshow da "device already in use"; la lista de formatos ENCOGE en un puerto de 5 Gbit/s: se lee siempre al abrirla)
            new ModeloCaptura("AVerMedia", "Live Gamer 4K (GC573)", 0x1461, 0x0054, "PCIe", P("GC573"), 3840, 2160, 60, false,
                              "4K60 / 1440p144 / 1080p240; YUY2, NV12, P010, RGB24", "audio en el mismo filtro DirectShow (pines «Audio» y «Audio Mixer»); Media Foundation no la abre (0xC00D36B4); a 4K en RGB24 llega a ~53 fps (medido)").Con(true, true)
                .Datos("propio: AVXGC573Cap", "2160p60:YUY2/NV12/P010/RGB24 1440p144:YUY2/NV12/P010/RGB24 1080p240:YUY2/NV12/P010/RGB24 720p60:YUY2/NV12/RGB24", "2160:RGB24=53",
                       16, true, "4K60 HDR, 1440p144, 1080p240 (HDMI 2.0; sin VRR)",
                       "PCIe x4 (en x1 no va); su driver deja abrirla a varios programas a la vez; de una Xbox a 1080p120 / 1440p120: quita «Allow 4K» y «Allow HDR10» en la consola").Id("avermedia-gc573"),
            // GC575: NO es de driver propio: una USB 3.2 Gen 2 (UVC, 07CA:0575) detras de un ASMedia ASM3142 en la propia tarjeta; el mismo chip
            // (Realtek RTD2801) y la misma tabla que la GC553G2 a 10 Gbit/s. 4K120/144 solo en MJPEG y con el modo puesto en su programa
            new ModeloCaptura("AVerMedia", "Live Gamer 4K 2.1 (GC575)", 0x07CA, 0x0575, "PCIe (USB)", P("GC575", "Live Gamer 4K 2.1"), 3840, 2160, 60, false, "4K60 NV12 / 1440p144 / 1080p240 (UVC); 4K144 MJPEG", "")
                .Datos("uvc", "2160p60:NV12 2160p50:YUY2 2160p30:P010/RGB24 1440p144:NV12 1440p60:YUY2/RGB24/P010 1080p240:NV12 1080p120:YUY2/RGB24/P010 720p60:NV12/YUY2 2160p144:MJPG",
                       "", 0, true, "4K144 / 1440p240 / 1080p360 HDR, VRR (firmware 1.1.6.12 o posterior para 4K144)",
                       "UVC: un solo programa a la vez; 4K120/144 solo en MJPEG y activandolo en su programa (aqui no se usa)").Id("avermedia-gc575"),
            new ModeloCaptura("AVerMedia", "Live Gamer Ultra 2.1 (GC553G2)", 0x07CA, 0x2553, "USB3", P("GC553G2", "Ultra 2.1"), 3840, 2160, 60, false, "4K60 NV12 (10 Gbit/s) / 1440p144 / 1080p240 (UVC)", "")
                .Datos("uvc", "2160p60:NV12 2160p50:YUY2 2160p30:P010/RGB24 1440p144:NV12 1440p60:YUY2/RGB24/P010 1080p240:NV12 1080p120:YUY2/RGB24/P010 720p60:NV12/YUY2 2160p144:MJPG",
                       "", 33, true, "4K144 / 1440p240 / 1080p360 HDR, VRR",
                       "USB 3.2 Gen 2 (10 Gbit/s); en un puerto de 5 Gbit/s: 4K solo a 30 en NV12, 1440p a 60, 1080p a 120 (y sin YUY2 a 4K). UVC: un solo programa a la vez").Id("avermedia-gc553g2"),
            new ModeloCaptura("AVerMedia", "Live Gamer Ultra S (GC553Pro)", 0x07CA, 0x1553, "USB3", P("GC553Pro", "Ultra S"), 3840, 2160, 30, false, "4K30 NV12 / 1440p60 / 1080p120 (UVC); MJPEG hasta 1080p240", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1440p50:YUY2 1440p30:RGB24/P010 1080p120:NV12 1080p60:YUY2/RGB24/P010 720p60:NV12/YUY2 2160p60:MJPG 1440p144:MJPG 1080p240:MJPG",
                       "", 0, true, "4K60 / 1440p144 / 1080p240 HDR, VRR", "USB 3.2 Gen 1 (5 Gbit/s). UVC: un solo programa a la vez").Id("avermedia-gc553pro"),
            new ModeloCaptura("AVerMedia", "Live Gamer Ultra (GC553)", 0x07CA, 0x0553, "USB3", P("GC553", "Live Gamer Ultra"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p120 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1440p50:YUY2 1440p30:RGB24 1080p120:NV12 1080p60:YUY2/RGB24 720p60:NV12/YUY2", "", 55, true, "4K60 HDR / 1440p144 / 1080p240 (VRR con firmware nuevo)",
                       "solo USB 3.1 Gen 1: en puertos Gen 2 o USB 2.0 no va (mejor un controlador de Intel). UVC: un solo programa a la vez").Id("avermedia-gc553"),
            new ModeloCaptura("AVerMedia", "Live Gamer Bolt (GC555)", 0x1461, 0x0054, "TB3", P("GC555", "Live Gamer Bolt", "Live Gamer BOLT"), 3840, 2160, 60, false, "4K60 HDR / 1440p144 / 1080p240 (Thunderbolt 3)", "")
                .Datos("propio", "2160p60:NV12/YUY2/P010 1440p144:NV12/YUY2 1080p240:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "4K60 HDR, 1440p144, 1080p240", "pide un puerto Thunderbolt 3/4; en PCI es 1461:0054 como la GC573 (otro subsistema): se reconoce por el nombre (formatos sin confirmar)").Id("avermedia-gc555"),
            new ModeloCaptura("AVerMedia", "Live Gamer Extreme 3 (GC551G2)", 0x07CA, 0x3551, "USB3", P("GC551G2", "Extreme 3", "EXTREME 3"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p60 (UVC, sin HDR)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60 HDR / 1440p120 / 1080p240, VRR hasta 1440p120",
                       "no captura HDR: con la consola en HDR los colores salen lavados (HDR apagado en la consola). UVC: un solo programa a la vez").Id("avermedia-gc551g2"),
            new ModeloCaptura("AVerMedia", "Live Gamer Extreme 2 (GC551)", 0x07CA, 0x0551, "USB3", P("GC551", "Extreme 2"), 1920, 1080, 60, false, "1080p60 (driver de AVerMedia)", "")
                .Datos("propio: AVerMedia GC551", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "4K60", "driver propio (paquete GC551)").Id("avermedia-gc551"),
            new ModeloCaptura("AVerMedia", "Live Gamer Extreme (GC550)", 0x07CA, 0x4710, "USB3", P("GC550", "Live Gamer Extreme"), 1920, 1080, 60, false, "1080p60 (driver de AVerMedia)", "")
                .Datos("propio: AVerMedia GC550", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "1080p60", "solo con controladores USB 3.0 certificados").Id("avermedia-gc550"),
            new ModeloCaptura("AVerMedia", "Live Gamer Duo (GC570D)", 0, 0, "PCIe", P("GC570D", "Live Gamer DUO", "Live Gamer Duo"), 1920, 1080, 60, false, "1080p60 HDR (entrada 1) + 1080p60 (entrada 2)", "").Con(true, true)
                .Datos("propio", "1080p60:YUY2/NV12/P010 720p60:YUY2/NV12", "", 0, true, "entrada 1: 4K60 HDR / 1440p144 / 1080p240", "lo mismo que la GC573 en PCI (1461:0054, otro subsistema): se reconoce por el nombre").Id("avermedia-gc570d"),
            new ModeloCaptura("AVerMedia", "Live Streamer Ultra HD (GC571)", 0x07CA, 0x0571, "PCIe (USB)", P("GC571", "Live Streamer Ultra HD"), 3840, 2160, 30, false, "4K30 (UVC en PCIe x1)", "")
                .Datos("uvc", "2160p30:NV12/YUY2/P010 1440p60:NV12/YUY2 1080p60:NV12/YUY2/RGB24 720p60:NV12/YUY2", "", 0, true, "4K60 HDR, VRR", "UVC: un solo programa a la vez").Id("avermedia-gc571"),
            new ModeloCaptura("AVerMedia", "Live Gamer HD 2 (GC570)", 0x07CA, 0x1570, "PCIe (USB)", P("GC570", "Live Gamer HD 2"), 1920, 1080, 60, false, "1080p60 YUY2 (UVC en PCIe x1)", "")
                .Datos("uvc", "1080p60:YUY2/MJPG 720p60:YUY2/MJPG", "", 0, false, "1080p60", "UVC: un solo programa a la vez").Id("avermedia-gc570"),
            new ModeloCaptura("AVerMedia", "Live Gamer Portable 2 Plus (GC513)", 0x07CA, 0x1513, "USB2", P("GC513", "Live Gamer Portable 2 Plus"), 1920, 1080, 60, true, "1080p60 comprimida (USB 2.0: H.264 / MJPEG)", "USB 2.0: comprimida")
                .Datos("uvc", "1080p60:MJPG 720p60:MJPG", "", 0, false, "4K60", "USB 2.0: solo MJPEG o H.264 de su codificador, nada sin comprimir (mas CPU y latencia)").Id("avermedia-gc513"),
            new ModeloCaptura("AVerMedia", "Live Gamer Portable 2 (GC510)", 0x07CA, 0x1510, "USB2", P("GC510", "Live Gamer Portable 2"), 1920, 1080, 60, true, "1080p60 comprimida (USB 2.0)", "USB 2.0: comprimida")
                .Datos("uvc", "1080p60:MJPG 720p60:MJPG", "", 0, false, "1080p60", "USB 2.0: la imagen llega comprimida").Id("avermedia-gc510"),
            // GC311: SOLO MJPEG o su H.264 (formato del fabricante): no tiene ni un formato sin comprimir. 1080p a 60/59,94/50/30
            new ModeloCaptura("AVerMedia", "Live Gamer Mini (GC311)", 0x07CA, 0x1311, "USB2", P("GC311", "Live Gamer MINI", "Live Gamer Mini"), 1920, 1080, 60, true, "1080p60 MJPEG (USB 2.0)", "USB 2.0: solo comprimida")
                .Datos("uvc", "1080p60:MJPG 720p60:MJPG", "", 0, false, "1080p60", "USB 2.0: solo MJPEG (o H.264 del fabricante), nada sin comprimir").Id("avermedia-gc311"),
            new ModeloCaptura("AVerMedia", "Live Gamer Mini (GC311)", 0x07CA, 0x3311, "USB2", new string[0], 1920, 1080, 60, true, "1080p60 MJPEG (USB 2.0)", "USB 2.0: solo comprimida")
                .Datos("uvc", "1080p60:MJPG 720p60:MJPG", "", 0, false, "1080p60", "USB 2.0: solo MJPEG"),
            new ModeloCaptura("AVerMedia", "StreamLine Mini+ (GC311G2)", 0, 0, "USB3", P("GC311G2", "Mini+"), 1920, 1080, 60, false, "1080p60 NV12 / YUY2 (UVC)", "")
                .Datos("uvc", "1080p60:NV12/YUY2/MJPG 720p60:NV12/YUY2/MJPG", "", 0, false, "4K60 / 1440p144 / 1080p240 HDR", "no captura HDR (HDR apagado en la consola)").Id("avermedia-gc311g2"),
            new ModeloCaptura("AVerMedia", "Live Streamer CAP 4K (BU113)", 0x07CA, 0x1113, "USB3", P("BU113", "CAP 4K"), 3840, 2160, 30, false, "4K30 / 1080p60 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "-", "sin passthrough; solo da el modo que le llega (a la velocidad de la fuente, 59,94)").Id("avermedia-bu113"),
            new ModeloCaptura("AVerMedia", "Live Gamer (genérica)", 0x07CA, 0, "USB", P("Live Gamer", "AVerMedia"), 1920, 1080, 60, false, "", ""),
            // ================================================================ Elgato (USB: VID 0FD9; PCIe: chip de YUAN, PCI 12AB, subsistema 1CFA)
            // UVC (sin driver): HD60 S+, HD60 X, 4K X, 4K S, Neo, Cam Link 4K -> un solo programa a la vez (cierra Elgato Studio / Camera
            // Hub); audio aparte ("Digital Audio Interface (...)", 48 kHz estereo). Driver de Elgato: HD60 S, HD60 Pro, 4K60 Pro, 4K60 Pro
            // MK.2, 4K Pro (varios programas a la vez; audio en un filtro "(Audio)" aparte). ffmpeg (mpv) no lee el P010 de ninguna ni el
            // RGB de las PCIe ("unknown compression 0xE436EB7D"): NV12 / YUY2 / UYVY. Las de escalador (HD60 X, 4K S, Neo, Cam Link nueva)
            // listan velocidades ENTERAS (60/50/30/25 y las altas): se pide 60; las sin escalador (HD60 S+, Cam Link original) solo el modo
            // que les llega, a la velocidad de la fuente (59,94)
            new ModeloCaptura("Elgato", "4K X", 0x0FD9, 0x009B, "USB3", P("4K X"), 3840, 2160, 60, false, "4K60 NV12 / 1440p144 / 1080p240 (UVC, 10 Gbit/s); 4K144 MJPEG", "")
                .Datos("uvc", "2160p60:NV12 2160p50:YUY2 1440p144:NV12 1440p60:YUY2/RGB24 1080p240:NV12 1080p144:YUY2 1080p120:RGB24 720p120:NV12 2160p144:MJPG", "",
                       60, true, "4K144 HDR10, 1440p240, 1080p240, VRR (sin DSC)",
                       "USB 3.2 Gen 2 (10 Gbit/s) directo a la placa, sin hubs; en 5 Gbit/s se presenta como 0FD9:009C y da menos (4K a 30). UVC: un solo programa a la vez").Id("elgato-4kx"),
            new ModeloCaptura("Elgato", "4K X (5 Gbit/s)", 0x0FD9, 0x009C, "USB3", new string[0], 3840, 2160, 30, false, "4K30 NV12 / 1440p60 / 1080p120 (UVC, 5 Gbit/s)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1440p50:YUY2 1440p30:RGB24 1080p120:NV12 1080p60:YUY2/RGB24 720p120:NV12 2160p144:MJPG", "",
                       60, true, "4K144 HDR10, VRR", "en un puerto de 5 Gbit/s: con uno de 10 Gbit/s (USB 3.2 Gen 2) da 4K60 y 1440p144 sin comprimir"),
            new ModeloCaptura("Elgato", "4K S", 0x0FD9, 0x00AF, "USB3", P("4K S"), 3840, 2160, 30, false, "4K30 NV12 / 1440p60 / 1080p120 (UVC); 4K60 solo MJPEG", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p120:NV12 1080p60:YUY2 720p60:NV12/YUY2 2160p60:MJPG 1440p144:MJPG 1080p240:MJPG", "",
                       43, true, "4K60 HDR, 1440p120, 1080p240, VRR", "4K60, 1440p de mas de 60 y 1080p de mas de 120 solo en MJPEG. UVC: un solo programa a la vez").Id("elgato-4ks"),
            new ModeloCaptura("Elgato", "HD60 X", 0x0FD9, 0x0082, "USB3", P("HD60 X"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p120 NV12; YUY2 hasta 1080p60 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p120:NV12 1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, true, "4K60 HDR10, 1440p120, 1080p240, VRR",
                       "solo velocidades enteras (60/50/30/25 y 120: se le pide 60). UVC: un solo programa a la vez").Id("elgato-hd60x"),
            new ModeloCaptura("Elgato", "HD60 X (rev. 2)", 0x0FD9, 0x008A, "USB3", P("Elgato HD60 X"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p120 NV12; YUY2 hasta 1080p60 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p120:NV12 1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, true, "4K60 HDR10, 1440p120, 1080p240, VRR", "solo velocidades enteras (se le pide 60)"),
            new ModeloCaptura("Elgato", "HD60 S+", 0x0FD9, 0x006A, "USB3", P("HD60 S+", "HD60S+"), 1920, 1080, 60, false, "1080p60 (4K30 de una fuente 4K30); UVC", "")
                .Datos("uvc", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, true, "4K60 HDR10 (sin VRR)",
                       "sin escalador: solo da el modo que le llega (la lista cambia con la consola) y nada por encima de 60; HDR: la entrada en YUV 4:2:0 (PS5: «Velocidad de transferencia 4K» -2)").Id("elgato-hd60s+"),
            new ModeloCaptura("Elgato", "HD60 S", 0x0FD9, 0x004F, "USB3", P("HD60 S"), 1920, 1080, 60, false, "1080p60 sin comprimir (driver de Elgato)", "")
                .Datos("propio: Elgato", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "*:RGB24=0 *:RGB32=0", 0, false, "1080p60",
                       "driver de Elgato (no UVC; varios programas a la vez); a veces se conecta como USB 2.0: puerto USB 3 directo").Id("elgato-hd60s"),
            new ModeloCaptura("Elgato", "Game Capture Neo", 0x0FD9, 0x008C, "USB3", P("Game Capture Neo", "Capture Neo"), 1920, 1080, 60, false, "1080p60 YUY2 / NV12 (UVC)", "")
                .Datos("uvc", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "4K60 HDR, 1440p60, 1080p60 (sin VRR)", "convierte siempre el HDR a SDR en la propia tarjeta. UVC: un solo programa a la vez").Id("elgato-neo"),
            new ModeloCaptura("Elgato", "4K60 Pro MK.2", 0x12AB, 0x0710, "PCIe", P("4K60 Pro MK.2", "4K60 Pro MK2"), 3840, 2160, 60, false, "4K60 HDR10 / 1440p144 / 1080p240 (PCIe, driver de Elgato)", "")
                .Datos("propio: Elgato", "2160p60:NV12/YUY2 1440p144:NV12/YUY2 1080p240:NV12/YUY2 720p60:NV12/YUY2", "*:RGB24=0 *:RGB32=0", 0, true, "4K60 HDR10, 1440p144, 1080p240, VRR",
                       "pide PCIe 2.0 x4 de verdad (en x1 la imagen sale verde o rota); con Elgato Studio abierto manda HDR a todos (colores lavados)").Id("elgato-4k60promk2"),
            new ModeloCaptura("Elgato", "4K Pro", 0x12AB, 0x0710, "PCIe", P("Game Capture 4K Pro", "Elgato 4K Pro", "4K Pro"), 3840, 2160, 60, false, "4K60 HDR10 / 1440p144 / 1080p240 (PCIe, driver de Elgato)", "")
                .Datos("propio: Elgato", "2160p60:NV12/YUY2/UYVY 1440p144:NV12/YUY2/UYVY 1080p240:NV12/YUY2/UYVY 720p240:NV12/YUY2", "*:RGB24=0 *:RGB32=0", 0, true, "8K60 HDR, 4K240, VRR (sin DSC)",
                       "pide PCIe 2.0 x4 de verdad; su RGB no se puede usar con ffmpeg; con Elgato Studio abierto manda HDR a todos").Id("elgato-4kpro"),
            new ModeloCaptura("Elgato", "Cam Link Pro", 0x12AB, 0x0710, "PCIe", P("Cam Link Pro"), 3840, 2160, 30, false, "4 entradas, cada una 4K30 / 1080p60 (PCIe)", "")
                .Datos("propio: Elgato", "2160p30:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "*:RGB24=0 *:RGB32=0", 0, false, "-", "sin passthrough").Id("elgato-camlinkpro"),
            new ModeloCaptura("Elgato", "4K60 Pro", 0x12AB, 0x0371, "PCIe", P("4K60 Pro"), 3840, 2160, 60, false, "4K60 NV12 (PCIe, driver de Elgato)", "")
                .Datos("propio: Elgato", "2160p60:NV12 1440p60:NV12 1080p60:YUY2/NV12 720p60:YUY2/NV12", "*:RGB24=0 *:RGB32=0", 0, false, "4K60", "YUY2 solo hasta 1080p60").Id("elgato-4k60pro"),
            new ModeloCaptura("Elgato", "HD60 Pro", 0x12AB, 0x0380, "PCIe", P("HD60 Pro"), 1920, 1080, 60, false, "1080p60 (PCIe, driver de Elgato)", "")
                .Datos("propio: Elgato", "1080p60:NV12/YUY2 720p60:NV12/YUY2", "*:RGB24=0 *:RGB32=0", 0, false, "1080p60 (4:2:0)", "").Id("elgato-hd60pro"),
            new ModeloCaptura("Elgato", "HD60 Pro", 0x12AB, 0x0381, "PCIe", new string[0], 1920, 1080, 60, false, "1080p60 (PCIe, driver de Elgato)", "")
                .Datos("propio: Elgato", "1080p60:NV12/YUY2 720p60:NV12/YUY2", "*:RGB24=0 *:RGB32=0", 0, false, "1080p60 (4:2:0)", ""),
            new ModeloCaptura("Elgato", "4K60 S+", 0x0FD9, 0x0068, "USB3", P("4K60 S+"), 3840, 2160, 60, true, "4K60 HDR10 comprimida (H.264 / HEVC)", "la captura llega comprimida")
                .Datos("propio: Elgato", "2160p60:H264 1080p60:H264", "", 225, true, "4K60 HDR10",
                       "siempre comprimida en la tarjeta (H.264 / HEVC, ~200-250 ms de retraso): para jugar mirando el PC no sirve").Id("elgato-4k60s+"),
            new ModeloCaptura("Elgato", "4K60 S+", 0x0FD9, 0x0075, "USB3", new string[0], 3840, 2160, 60, true, "4K60 HDR10 comprimida (H.264 / HEVC)", "la captura llega comprimida")
                .Datos("propio: Elgato", "2160p60:H264 1080p60:H264", "", 225, true, "4K60 HDR10", "siempre comprimida (~200-250 ms): para jugar mirando el PC no sirve"),
            // Cam Link 4K: la original (0066) solo da el modo que le llega (1080p: YUY2/NV12; 4K30: NV12); la nueva (007B / 00A1) escala:
            // YUY2 hasta 1080p60, NV12 4K30 y 4K60 solo MJPEG. No va en USB 2.0. Sin passthrough. (ezcap331 tambien se llama "CAM LINK 4K":
            // va por su VID:PID 32ED:3701, abajo)
            new ModeloCaptura("Elgato", "Cam Link 4K", 0x0FD9, 0x0066, "USB3", P("Cam Link 4K", "Cam Link"), 3840, 2160, 30, false, "4K30 NV12 / 1080p60 YUY2 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "-",
                       "sin passthrough (la tele va por otra salida o un divisor HDMI); la original solo da el modo que le llega").Id("elgato-camlink4k"),
            new ModeloCaptura("Elgato", "Cam Link 4K (nueva)", 0x0FD9, 0x00A1, "USB3", new string[0], 3840, 2160, 30, false, "4K30 NV12 / 1080p60 YUY2 (UVC); 4K60 MJPEG", "")
                .Datos("uvc", "2160p30:NV12 1080p60:YUY2/NV12 720p60:YUY2/NV12 2160p60:MJPG", "", 0, false, "-", "sin passthrough; velocidades enteras (se le pide 60)"),
            new ModeloCaptura("Elgato", "Cam Link 4K (nueva)", 0x0FD9, 0x007B, "USB3", new string[0], 3840, 2160, 30, false, "4K30 NV12 / 1080p60 YUY2 (UVC); 4K60 MJPEG", "")
                .Datos("uvc", "2160p30:NV12 1080p60:YUY2/NV12 720p60:YUY2/NV12 2160p60:MJPG", "", 0, false, "-", "sin passthrough; velocidades enteras (se le pide 60)"),
            new ModeloCaptura("Elgato", "Game Capture HD (HD60, USB 2.0)", 0x0FD9, 0x005C, "USB2", P("Elgato Game Capture HD"), 1920, 1080, 60, true, "1080p60 H.264 (USB 2.0)", "comprimida en la tarjeta")
                .Datos("propio: Elgato", "1080p60:H264", "", 1000, false, "1080p60", "codificador H.264 en la tarjeta: ~1 s de retraso, no sirve para jugar mirando el PC").Id("elgato-hd60"),
            new ModeloCaptura("Elgato", "Game Capture (genérica)", 0x0FD9, 0, "USB", P("Elgato", "Game Capture"), 1920, 1080, 60, false, "", ""),
            new ModeloCaptura("Elgato", "Cam Link 4K", 0x0FD9, 0x0067, "USB3", new string[0], 3840, 2160, 30, false, "4K30 NV12 / 1080p60 YUY2 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "-", "sin passthrough"),
            new ModeloCaptura("Elgato", "HD60 S+", 0x0FD9, 0x006B, "USB3", new string[0], 1920, 1080, 60, false, "1080p60 (UVC)", "")
                .Datos("uvc", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, true, "4K60 HDR10", "sin escalador: solo da el modo que le llega"),

            // ================================================================ otras
            // Magewell (VID 2935; PCIe 1CD7): UVC sin driver, con escalador y conversion de velocidad (si se le pide otra que la de la consola
            // repite o tira imagenes: se le pide 59,94, que su rango admite)
            new ModeloCaptura("Magewell", "USB Capture HDMI 4K Pro", 0x2935, 0, "USB3", P("USB Capture HDMI 4K Pro"), 3840, 2160, 60, false, "4K60 YUY2 a 10 Gbit/s (4K30 NV12 a 5)", "")
                .Datos("uvc", "2160p30:NV12 1080p100:YUY2 1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, true, "4K60", "USB 3.2 Gen 2x2: con 10 / 20 Gbit/s da 4K60 / 4K120 YUY2").Id("magewell-4kpro"),
            new ModeloCaptura("Magewell", "USB Capture HDMI 4K Plus", 0x2935, 0, "USB3", P("USB Capture HDMI 4K"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p60 (UVC)", "")
                .Datos("uvc", "2160p30:NV12/YUY2 1440p60:NV12/YUY2 1080p60:NV12/YUY2/RGB24 720p60:NV12/YUY2", "", 0, false, "4K60 4:4:4 (bucle)", "8 bits (sin HDR)").Id("magewell-4kplus"),
            new ModeloCaptura("Magewell", "USB Capture HDMI (Gen 2 / Plus)", 0x2935, 0, "USB3", P("USB Capture HDMI", "Magewell"), 1920, 1080, 120, false, "1080p hasta 120; YUY2, UYVY, RGB24 (UVC)", "")
                .Datos("uvc", "1080p120:YUY2 1080p60:YUY2/RGB24 720p60:YUY2/RGB24", "", 0, false, "Plus: bucle (4K30 4:4:4 / 4K60 4:2:0 de entrada, la reduce)", "").Id("magewell-usbhdmi"),
            new ModeloCaptura("Magewell", "Pro Capture (PCIe)", 0x1CD7, 0, "PCIe", P("Pro Capture"), 3840, 2160, 60, false, "NV12, YUY2, UYVY, BGR24 (PCIe, driver de Magewell)", "")
                .Datos("propio: Magewell", "2160p60:NV12/YUY2 1080p60:NV12/YUY2/RGB24 720p60:NV12/YUY2", "", 0, false, "",
                       "el nombre lleva delante el numero del selector de la placa (\"00: Pro Capture HDMI...\"); la HDMI (no 4K) llega a ~1080p80").Id("magewell-pro"),
            // j5create: JVA02 / JVA04 / JVA06 son USB 3.1 Gen 1 (YUY2 hasta 1080p60; en USB 2.0 solo MJPEG); JVA14 (0711:0A14) 4K30 NV12
            new ModeloCaptura("j5create", "JVA14 (4K HDMI capture)", 0x0711, 0x0A14, "USB3", P("JVA14"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p120 NV12 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p120:NV12 1080p60:YUY2 720p60:YUY2/NV12", "", 0, true, "4K60 / 1440p144 / 1080p240 HDR", "en USB 2.0 solo 720p30").Id("j5create-jva14"),
            new ModeloCaptura("j5create", "JVA04 (4K HDMI capture)", 0, 0, "USB3", P("JVA04"), 1920, 1080, 60, false, "1080p60 YUY2 (UVC); MJPEG en USB 2.0", "")
                .Datos("uvc", "1080p60:YUY2/MJPG 720p60:YUY2/MJPG", "", 0, false, "4K60 (sin HDR ni 1440p)",
                       "con 4K a la entrada la reduce a 1080p; en un puerto USB 2.0 (o un hub) solo MJPEG: puerto USB 3 directo").Id("j5create-jva04"),
            new ModeloCaptura("j5create", "JVA02 / JVA06 (HDMI capture)", 0, 0, "USB3", P("JVA02", "JVA06"), 1920, 1080, 60, false, "1080p60 YUY2 (UVC); MJPEG en USB 2.0", "")
                .Datos("uvc", "1080p60:YUY2/MJPG 720p60:YUY2/MJPG", "", 0, false, "-", "en un puerto USB 2.0 solo MJPEG").Id("j5create-jva02"),
            // Genki: ShadowCast (1) = USB 2.0 de la clase MS2109 (298F:1996): MJPEG; ShadowCast 2 (0100:1996): YUY2 1080p60
            new ModeloCaptura("Genki", "ShadowCast 2 / 2 Pro", 0x0100, 0x1996, "USB3", P("ShadowCast 2"), 1920, 1080, 60, false, "1080p60 YUY2 (UVC)", "")
                .Datos("uvc", "1080p60:YUY2/MJPG 720p60:YUY2/MJPG", "", 0, false, "2 Pro: 4K60 HDR, VRR",
                       "YUY2 a 1080p60 o menos (el fabricante); el 4K60 del 2 Pro va comprimido y llega ~0,5 s tarde").Id("genki-shadowcast2"),
            new ModeloCaptura("Genki", "ShadowCast", 0x298F, 0x1996, "USB2", P("ShadowCast"), 1920, 1080, 30, true, "MJPEG 1080p30 / 720p60 (USB 2.0)", "")
                .Datos("uvc", "1080p30:MJPG 720p60:MJPG", "1080:MJPG=30", 0, false, "-", "USB 2.0 (como las MS2109): MJPEG; a 1080p lista 60 y da 30").Id("genki-shadowcast"),
            new ModeloCaptura("Razer", "Ripsaw X", 0x1532, 0, "USB3", P("Ripsaw X"), 3840, 2160, 30, false, "4K30 (UVC, sin passthrough)", "")
                .Datos("uvc", "2160p30:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "- (sin salida HDMI)", "sin passthrough: la tele va por otra salida o un divisor HDMI").Id("razer-ripsawx"),
            new ModeloCaptura("Razer", "Ripsaw HD", 0x1532, 0x0D01, "USB3", P("Ripsaw HD", "Ripsaw"), 1920, 1080, 60, false, "1080p60 YUY2 (UVC)", "")
                .Datos("uvc", "1080p60:YUY2 720p60:YUY2", "", 0, false, "4K60", "solo USB 3.0").Id("razer-ripsawhd"),
            new ModeloCaptura("EVGA", "XR1 Pro", 0x3842, 0x310B, "USB3", P("XR1 Pro"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p60 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, true, "4K60 HDR, 1440p144, 1080p240", "").Id("evga-xr1pro"),
            new ModeloCaptura("EVGA", "XR1 Lite", 0x3842, 0x310A, "USB3", P("XR1 Lite"), 1920, 1080, 60, false, "1080p60 NV12 / YUY2 (UVC)", "")
                .Datos("uvc", "1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60", "").Id("evga-xr1lite"),
            new ModeloCaptura("EVGA", "XR1", 0x3842, 0x3103, "USB3", P("XR1"), 1920, 1080, 60, false, "1080p60 (UVC)", "")
                .Datos("uvc", "1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60 (1440p120 / HDR solo sin capturar)", "").Id("evga-xr1"),
            new ModeloCaptura("NZXT", "Signal 4K30", 0x1E71, 0x2700, "USB3", P("Signal 4K30"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p120 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12/YUY2 1080p120:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "4K60 HDR, 1440p144, 1080p240", "convierte el HDR a SDR en la tarjeta").Id("nzxt-signal4k"),
            new ModeloCaptura("NZXT", "Signal HD60", 0x1E71, 0x2701, "USB3", P("Signal HD60", "NZXT Signal"), 1920, 1080, 60, false, "1080p60 YUY2 / NV12 (UVC)", "")
                .Datos("uvc", "1080p60:YUY2/NV12 720p60:YUY2/NV12", "", 0, false, "4K60, 1440p60", "").Id("nzxt-signal"),
            // ezcap (VID 32ED; chips de MacroSilicon): el 331 se llama "CAM LINK 4K", como el de Elgato: se distingue por el VID:PID
            new ModeloCaptura("ezcap", "ezcap331 / 326 / 321C", 0x32ED, 0x3701, "USB3", P("ezcap"), 3840, 2160, 30, false, "4K30 / 1440p60 / 1080p120 NV12; YUY2 1080p60 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1440p60:NV12 1440p50:YUY2 1080p120:NV12 1080p60:YUY2 720p60:NV12/YUY2", "", 50, false, "331: sin; 326: 4K60 HDR, 1440p120, 1080p240", "").Id("ezcap331"),
            new ModeloCaptura("Epiphan", "AV.io HD / 4K", 0x5555, 0, "USB3", P("AV.io"), 3840, 2160, 30, false, "1080p60 NV12 / YUY2; 4K30 NV12 (UVC)", "")
                .Datos("uvc", "2160p30:NV12 1080p60:NV12/YUY2 720p60:NV12/YUY2", "", 0, false, "-", "").Id("epiphan-avio"),
            new ModeloCaptura("StarTech", "USB3HDCAP", 0, 0, "USB3", P("USB3HDCAP", "StarTech"), 1920, 1080, 60, false, "1080p60 YUY2 (driver propio)", "")
                .Datos("propio: StarTech", "1080p60:YUY2 720p60:YUY2", "", 0, false, "-", "no va con controladores USB de ASMedia o Etron").Id("startech-usb3hdcap"),
            new ModeloCaptura("Hauppauge", "HD PVR Pro 60", 0x2040, 0x0677, "USB3", P("HD PVR Pro 60"), 1920, 1080, 60, true, "1080p60 H.264 por UVC (MJPEG opcional)", "comprimida")
                .Datos("uvc", "1080p60:H264/MJPG", "", 500, false, "4K", "H.264 de serie (~0,5 s de retraso): elige su modo MJPEG").Id("hauppauge-pro60"),
            new ModeloCaptura("Hauppauge", "HD PVR / Colossus", 0x2040, 0, "", P("Hauppauge", "HD PVR", "Colossus"), 1920, 1080, 60, true, "", "suele llegar comprimida (H.264)")
                .Datos("propio: Hauppauge", "1080p60:H264", "", 1500, false, "1080p60", "solo H.264 (codificador en la tarjeta): segundos de retraso, no sirve para jugar mirando el PC").Id("hauppauge"),
            new ModeloCaptura("Blackmagic", "DeckLink / UltraStudio / Intensity", 0, 0, "", P("Decklink", "DeckLink", "Blackmagic", "UltraStudio", "Intensity"), 3840, 2160, 60, false, "UYVY / v210", "")
                .Datos("propio: Blackmagic", "", "", 0, false, "", "el modo pedido tiene que ser EXACTO el que manda la consola (1080p59,94 no es 1080p60) o da negro; UYVY: va por mpv"),
            new ModeloCaptura("MiraBox", "Video Capture (VXIS)", 0x1BCF, 0x2C99, "USB3", P("MiraBox"), 1920, 1080, 60, false, "", "")
                .Datos("uvc", "", "", 0, false, "", "chip VXIS (no MacroSilicon)"),
            // MacroSilicon: las genericas ("USB Video", "USB3 Video", "USB3.0 capture", "USB3.0 UHD", UGREEN, Hagibis, ezcap 261...): por VID:PID
            // (sus nombres no dicen nada; "USB Video" tambien es de capturadoras analogicas 534D:0021)
            new ModeloCaptura("MacroSilicon", "MS2109 (USB 2.0, genérica)", 0x534D, 0x2109, "USB2", new string[0], 1920, 1080, 30, true,
                              "MJPEG 1080p30 / 720p60; YUY2 solo a poca resolución", "USB 2.0: a 1080p solo MJPEG a 30 fps (más latencia y CPU)")
                .Datos("uvc", "1080p30:MJPG 720p60:MJPG 480p30:YUY2", "1080:MJPG=30", 0, false, "-",
                       "USB 2.0: MJPEG (a 1080p lista 60 y da 30; pedirle 60 corrompe imagenes); su YUY2 a 1080p / 720p va a 5-10 fps").Id("ms2109"),
            new ModeloCaptura("MacroSilicon", "MS2109 (USB 2.0, genérica)", 0x345F, 0x2109, "USB2", new string[0], 1920, 1080, 30, true, "MJPEG 1080p30 / 720p60", "USB 2.0")
                .Datos("uvc", "1080p30:MJPG 720p60:MJPG 480p30:YUY2", "1080:MJPG=30", 0, false, "-", "USB 2.0: MJPEG"),
            new ModeloCaptura("MacroSilicon", "MS2130 (USB 3.0, genérica)", 0x345F, 0x2130, "USB3", new string[0], 1920, 1080, 60, false, "YUY2 1080p60 (entrada hasta 4K30)", "")
                .Datos("uvc", "1080p60:YUY2 720p60:YUY2 1080p30:MJPG", "", 66, false, "4K30 segun el aparato",
                       "entrada maxima 4K30: con la consola a 4K llega a 30 (ponla a 1080p60); en un puerto USB 2.0 solo MJPEG; rango completo (0-255)").Id("ms2130"),
            new ModeloCaptura("MacroSilicon", "MS2130 (USB 3.0, genérica)", 0x534D, 0x2130, "USB3", new string[0], 1920, 1080, 60, false, "YUY2 1080p60", "")
                .Datos("uvc", "1080p60:YUY2 720p60:YUY2 1080p30:MJPG", "", 66, false, "", "entrada maxima 4K30: consola a 1080p60"),
            new ModeloCaptura("MacroSilicon", "MS2131 (USB 3.0, genérica)", 0x345F, 0x2131, "USB3", new string[0], 1920, 1080, 60, false, "YUY2 1080p60", "")
                .Datos("uvc", "1080p60:YUY2 720p60:YUY2", "", 0, false, "4K30", "entrada maxima 4K30: consola a 1080p60").Id("ms2131"),
            new ModeloCaptura("MacroSilicon", "MS2130S / MS2131S (USB 3.0, genérica)", 0x345F, 0x2132, "USB3", new string[0], 3840, 2160, 30, false, "YUY2 1080p60; NV12 4K30", "")
                .Datos("uvc", "2160p30:NV12 1080p60:YUY2 720p60:YUY2", "", 0, false, "", "su RGB sale del reves: YUY2 / NV12").Id("ms2130s"),
            new ModeloCaptura("MacroSilicon", "MS2130S / MS2131S (USB 3.0, genérica)", 0x345F, 0x2133, "USB3", new string[0], 3840, 2160, 30, false, "YUY2 1080p60; NV12 4K30", "")
                .Datos("uvc", "2160p30:NV12 1080p60:YUY2 720p60:YUY2", "", 0, false, "", "su RGB sale del reves: YUY2 / NV12"),        };
    }
}
