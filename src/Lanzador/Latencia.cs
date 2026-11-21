// Latencia.cs - claves de la latencia del camino de mpv + Magpie y del sonido de la consola (21-11, 1.2.1).
//   mpv_cola_ms=N        lo que el demuxer de mpv puede leer por delante de la imagen (--demuxer-readahead-secs; 0 = el de mpv, 1 s).
//                        Con --untimed y sin quitar imagenes, si mpv se retrasa (la grafica ocupada con Magpie) lo leido sale tarde y por
//                        orden: el rtbufsize de ~3 imagenes de LavfOptions no lo para (mpv vacia el bufer de DirectShow en el suyo, hasta
//                        150 MB: 9 imagenes a 4K, ~36 a 1080p). Con N (p. ej. 50) mpv deja de leer y DirectShow tira las que no caben.
//                        La cola se ve en el log cada 20 s ("cola de mpv max ...", scripts\ce_diag.lua)
//   audio_bloque_ms=N    trozos del sonido de la capturadora (audio_buffer_size de DirectShow; de serie 20, como antes)
//   audio_cola_alta_ms   con mas sonido en la cola de mpv que esto, mpv va al 1,004 de velocidad (de serie 70, como antes)
//   audio_cola_baja_ms   ... hasta bajar de esto (de serie 30)
// Cada 5 minutos el log dice la cola del sonido ("Audio de la consola: cola ..."): con la cola baja y sin cortes se pueden bajar los
// tres (p. ej. 10 / 40 / 15: ~25 ms menos de retraso del sonido, que va detras de la imagen)
using System;
using System.Globalization;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public int MpvQueueMs = 0;
        public int AudioBlockMs = 20;
        public int AudioQueueHighMs = 70, AudioQueueLowMs = 30;

        bool CeParseLatencia(string key, string val)
        {
            int v;
            switch (key)
            {
                case "mpv_cola_ms":
                    if (!int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return true;
                    MpvQueueMs = Math.Max(0, Math.Min(1000, v)); return true;
                case "audio_bloque_ms":
                    if (!int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return true;
                    AudioBlockMs = Math.Max(5, Math.Min(100, v)); return true;
                case "audio_cola_alta_ms":
                    if (!int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return true;
                    AudioQueueHighMs = Math.Max(10, Math.Min(500, v)); return true;
                case "audio_cola_baja_ms":
                    if (!int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return true;
                    AudioQueueLowMs = Math.Max(0, Math.Min(400, v)); return true;
            }
            return false;
        }

        // Los dos umbrales en segundos (la baja siempre por debajo de la alta)
        public double AudioQueueHighS { get { return AudioQueueHighMs / 1000.0; } }
        public double AudioQueueLowS { get { return Math.Min(AudioQueueLowMs, Math.Max(0, AudioQueueHighMs - 5)) / 1000.0; } }
    }

    // La cola del sonido de la consola (demuxer-cache-duration del mpv de audio, una vez por segundo en AudioLink) y el tiempo con la
    // velocidad subida: al log cada 5 minutos
    static class AudioQueueLog
    {
        static readonly object sync = new object();
        static int n, fastSecs;
        static double sum, min = double.MaxValue, max;
        static DateTime since = DateTime.UtcNow;

        public static void Add(double queueS, bool fast)
        {
            if (queueS < 0 || double.IsNaN(queueS)) return;
            string line = null;
            lock (sync)
            {
                n++; sum += queueS; if (fast) fastSecs++;
                if (queueS < min) min = queueS;
                if (queueS > max) max = queueS;
                if ((DateTime.UtcNow - since).TotalSeconds >= 300 && n > 0)
                {
                    line = string.Format(CultureInfo.InvariantCulture,
                        "Audio de la consola: cola media {0:0} ms (min {1:0}, max {2:0}) en {3} s, con la velocidad subida {4} s (trozos de {5} ms; umbrales {6:0}/{7:0} ms)",
                        sum / n * 1000, min * 1000, max * 1000, n, fastSecs, Block, High * 1000, Low * 1000);
                    n = 0; fastSecs = 0; sum = 0; min = double.MaxValue; max = 0; since = DateTime.UtcNow;
                }
            }
            if (line != null) Log.Write(line);
        }

        public static void Reset() { lock (sync) { n = 0; fastSecs = 0; sum = 0; min = double.MaxValue; max = 0; since = DateTime.UtcNow; } }

        // (lo que se apunta en la linea: lo que lleva el mpv de audio de ahora)
        public static int Block;
        public static double High, Low;
    }
}
