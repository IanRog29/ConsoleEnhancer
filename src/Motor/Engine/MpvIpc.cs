using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace ConsoleEnhancer.Motor
{
    // Lo que el lanzador (Switch2Magpie) le dice a mpv por su tuberia JSON (--input-ipc-server), hablado por el motor:
    // asi el lanzador cambia mpv + Magpie por el motor sin cambiar su forma de hablar.
    //   {"command":["show-text","texto",2500]}  {"command":["get_property","time-pos"],"request_id":77}
    //   {"command":["script-message","switch2-grab",ruta,"light"]}  ...
    // y los avisos del script Lua salen como {"event":"client-message","args":["switch2-...",...]}
    class MpvIpc
    {
        readonly string name;
        readonly Func<List<string>, string> handler;   // devuelve el JSON de "data" (null = sin datos)
        readonly List<StreamWriter> clients = new List<StreamWriter>();
        volatile bool stop;
        // 16-11 (motor en espera): aviso que no llego a nadie (nadie conectado aun): se manda al primer cliente que se conecte.
        // Candado: keepLock y luego clients (siempre en ese orden)
        readonly object keepLock = new object();
        string[] kept;

        public MpvIpc(string name, Func<List<string>, string> handler) { this.name = name; this.handler = handler; }

        public void Start()
        {
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Name = "tuberia mpv";
            t.Start();
        }

        public void Stop() { stop = true; }

        void Loop()
        {
            while (!stop)
            {
                NamedPipeServerStream s = null;
                try
                {
                    // 16-11: sin tope de instancias (como mpv). Con el motor en espera el lanzador se vuelve a conectar en cada
                    // despertar: con 8, unas cuantas conexiones viejas sin cerrar dejaban el motor sin poder despertarse
                    s = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    s.WaitForConnection();
                    NamedPipeServerStream c = s;
                    s = null;
                    Thread t = new Thread(delegate () { Serve(c); });
                    t.IsBackground = true;
                    t.Start();
                }
                catch (Exception ex) { if (!stop) { Log.Write("Tuberia mpv: " + ex.Message); Thread.Sleep(200); } }
                finally { if (s != null) s.Dispose(); }
            }
        }

        void Serve(NamedPipeServerStream s)
        {
            StreamWriter w = null;
            try
            {
                using (s)
                using (StreamReader r = new StreamReader(s, new UTF8Encoding(false)))
                {
                    w = new StreamWriter(s, new UTF8Encoding(false));
                    w.AutoFlush = true;
                    w.NewLine = "\n";
                    string[] k;
                    int n;
                    lock (keepLock)
                    {
                        lock (clients) { clients.Add(w); n = clients.Count; }
                        k = kept;
                        kept = null;
                    }
                    Log.Write("Tuberia mpv: cliente conectado (" + n + " en total)");
                    if (k != null)
                    {
                        try { lock (w) w.WriteLine(Line(k)); }
                        catch { lock (keepLock) if (kept == null) kept = k; throw; }
                    }
                    string line;
                    while (!stop && (line = r.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0) continue;
                        List<string> cmd;
                        long reqId;
                        if (!Parse(line, out cmd, out reqId)) continue;
                        string data = null;
                        try { data = handler(cmd); }
                        catch (Exception ex) { Log.Write("Orden " + (cmd.Count > 0 ? cmd[0] : "") + ": " + ex.Message); }
                        if (reqId >= 0)
                        {
                            string resp = "{" + (data != null ? "\"data\":" + data + "," : "") + "\"request_id\":" + reqId + ",\"error\":\"success\"}";
                            lock (w) w.WriteLine(resp);
                        }
                    }
                    if (!stop) Log.Write("Tuberia mpv: cliente desconectado");
                }
            }
            catch (IOException ex) { Log.Write("Tuberia mpv: cliente desconectado (" + ex.Message + ")"); }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Log.Write("Tuberia mpv (cliente): " + ex); }
            finally { if (w != null) lock (clients) clients.Remove(w); }
        }

        // Aviso a todos los clientes, como un script-message de un script de mpv. Devuelve a cuantos ha llegado (0: nadie conectado)
        public int Event(params string[] args)
        {
            int sent = 0;
            string line = Line(args);
            StreamWriter[] all;
            lock (clients) all = clients.ToArray();
            foreach (StreamWriter w in all)
            {
                try { lock (w) w.WriteLine(line); sent++; }
                catch { lock (clients) clients.Remove(w); }
            }
            return sent;
        }

        // 16-11 (espera): como Event, pero si no llega a nadie se guarda y se manda al primer cliente que se conecte
        // (switch2-standby-ready: el lanzador se conecta cuando la tuberia ya existe, quiza despues del aviso)
        public int EventOrKeep(params string[] args)
        {
            lock (keepLock)
            {
                int n = Event(args);
                kept = n > 0 ? null : args;
                return n;
            }
        }

        // El aviso guardado ya no vale (p. ej. switch2-standby-ready tras un switch2-wake)
        public void DropKept() { lock (keepLock) kept = null; }

        static string Line(string[] args)
        {
            StringBuilder sb = new StringBuilder("{\"event\":\"client-message\",\"args\":[");
            for (int i = 0; i < args.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Quote(args[i] ?? "")); }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // {"command":[...],"request_id":N} -> lista de textos (los numeros como texto). Tambien la forma de texto plano de mpv
        static bool Parse(string line, out List<string> cmd, out long reqId)
        {
            cmd = new List<string>();
            reqId = -1;
            if (!line.StartsWith("{", StringComparison.Ordinal))
            {
                foreach (string p in line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) cmd.Add(p);
                return cmd.Count > 0;
            }
            int ci = line.IndexOf("\"command\"", StringComparison.Ordinal);
            if (ci < 0) return false;
            int i = line.IndexOf('[', ci);
            if (i < 0) return false;
            i++;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == ']') { i++; break; }
                if (c == ',' || c == ' ') { i++; continue; }
                if (c == '"')
                {
                    StringBuilder sb = new StringBuilder();
                    i++;
                    while (i < line.Length && line[i] != '"')
                    {
                        if (line[i] == '\\' && i + 1 < line.Length)
                        {
                            char e = line[i + 1];
                            if (e == 'n') sb.Append('\n');
                            else if (e == 't') sb.Append('\t');
                            else if (e == 'u' && i + 5 < line.Length) { sb.Append((char)int.Parse(line.Substring(i + 2, 4), NumberStyles.HexNumber)); i += 4; }
                            else sb.Append(e);
                            i += 2;
                        }
                        else sb.Append(line[i++]);
                    }
                    i++;
                    cmd.Add(sb.ToString());
                }
                else
                {
                    int j = i;
                    while (j < line.Length && line[j] != ',' && line[j] != ']') j++;
                    cmd.Add(line.Substring(i, j - i).Trim());
                    i = j;
                }
            }
            int ri = line.IndexOf("\"request_id\"", StringComparison.Ordinal);
            if (ri >= 0)
            {
                int k = line.IndexOf(':', ri) + 1;
                int e = k;
                while (e < line.Length && (char.IsDigit(line[e]) || line[e] == ' ' || line[e] == '-')) e++;
                long.TryParse(line.Substring(k, e - k).Trim(), out reqId);
            }
            return cmd.Count > 0;
        }
    }
}
