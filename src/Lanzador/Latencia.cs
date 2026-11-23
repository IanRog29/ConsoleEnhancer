// Latencia.cs - claves de la latencia del sonido de la consola (21-11, 1.2.1; 23-11, 1.2.4 SINMPV: ya sin las de mpv).
//   audio_bloque_ms=N    trozos del sonido de la capturadora (como audio_buffer_size de DirectShow; de serie 20, como antes)
//   audio_cola_alta_ms   con mas sonido en cola que esto, el lanzador le pide al proceso de sonido 1,004 de velocidad (de serie 70)
//   audio_cola_baja_ms   ... hasta bajar de esto (de serie 30). (1.2.4: el proceso de sonido del motor ya corrige la deriva el solo, con
//                        su cola objetivo audio_cola_ms=30 de audio_motor_extra; estos dos quedan de reserva)
//   mpv_cola_ms, mpv_cscale (de mpv, hasta la 1.2.3): ya no hacen nada
// Cada 5 minutos el log dice la cola del sonido ("Audio de la consola: cola ..."); el detalle, en data\logs\audio.log
using System;
using System.Globalization;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public int AudioBlockMs = 20;
        public int AudioQueueHighMs = 70, AudioQueueLowMs = 30;
        // 23-11 (1.2.4, SINMPV): el sonido por el motor (ConsoleEnhancerEngine.exe --audio). audio_motor_extra= claves para el (separadas por
        // espacios: audio_cola_ms=30 audio_salida_ms=10 audio_deriva=1 audio_cola_max_ms=100); test_audio=clic|tono[,ppm=N][,jit=N] (solo
        // pruebas: un generador con el ritmo de la capturadora en vez de abrirla)
        public string AudioEngineExtra = "", TestAudio = "";
        bool CeParseLatencia(string key, string val)
        {
            int v;
            switch (key)
            {
                case "mpv_cscale": return true;   // (23-11, 1.2.4 SINMPV: sin mpv)
                case "audio_motor_extra": AudioEngineExtra = val.Trim(); return true;
                case "test_audio": TestAudio = val.Trim(); return true;
                case "mpv_cola_ms": return true;  // (23-11, 1.2.4 SINMPV: sin mpv)
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
