using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace ConsoleEnhancer.Motor
{
    // Tuberia \\.\pipe\<nombre>: una orden por linea, una respuesta por linea. La usa el lanzador (Switch2Magpie)
    //   osd <ms> <texto>     | set clave=valor ...  | status | snap <ruta.bmp> | show | hide | quit
    class PipeServer
    {
        readonly string name;
        readonly Func<string, string> handler;
        volatile bool stop;
        Thread thread;

        public PipeServer(string name, Func<string, string> handler) { this.name = name; this.handler = handler; }

        public void Start()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "tuberia";
            thread.Start();
        }

        public void Stop() { stop = true; }

        void Loop()
        {
            while (!stop)
            {
                NamedPipeServerStream s = null;
                try
                {
                    s = new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.None);
                    s.WaitForConnection();
                    NamedPipeServerStream conn = s;
                    s = null;
                    Thread t = new Thread(delegate () { Serve(conn); });
                    t.IsBackground = true;
                    t.Start();
                }
                catch (Exception ex)
                {
                    if (!stop) Log.Write("Tuberia: " + ex.Message);
                    Thread.Sleep(200);
                }
                finally { if (s != null) s.Dispose(); }
            }
        }

        void Serve(NamedPipeServerStream s)
        {
            try
            {
                using (s)
                using (StreamReader r = new StreamReader(s, new UTF8Encoding(false)))
                using (StreamWriter w = new StreamWriter(s, new UTF8Encoding(false)))
                {
                    w.AutoFlush = true;
                    string line;
                    while (!stop && (line = r.ReadLine()) != null)
                    {
                        string resp;
                        try { resp = handler(line.Trim()); }
                        catch (Exception ex) { resp = "error " + ex.Message; }
                        w.WriteLine(resp ?? "ok");
                    }
                }
            }
            catch (IOException) { }
            catch (Exception ex) { Log.Write("Tuberia (cliente): " + ex.Message); }
        }
    }
}
