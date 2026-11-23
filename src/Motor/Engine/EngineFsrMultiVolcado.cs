using System;
using System.Diagnostics;
using System.IO;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFsrMultiVolcado.cs - 23-11-2026 (1.2.4 ronda 6, FGMULTI). SOLO PRUEBAS: lo que el hilo de FSR FG presenta (inventadas y reales), a
    // un fichero .fgm como el que graba fgreal de FGCAL por Desktop Duplication (recorte 1920x1440 en 960,360 en luma, y los 16 cuadros del
    // codigo de la escena de prueba), para medir la calidad sin depender de la tele (con la tele en reposo DWM compone 1 vez por segundo y la
    // duplicacion no ve nada). fsrfg_multi_volcado=<fichero.fgm>, _desde=N (Present que se saltan), _n=N; fsrfg_multi_sin_presentar=1
    // (el hilo sigue a su ritmo pero sin Present).
    partial class Settings
    {
        public string FsrMultiDump = "";
        public int FsrMultiDumpFrom = 120, FsrMultiDumpN = 300;
        public bool FsrMultiNoPresent = false;

        bool ApplyFsrMultiDump(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "fsrfg_multi_volcado": FsrMultiDump = val.Trim(); return true;
                case "fsrfg_multi_volcado_desde": FsrMultiDumpFrom = Math.Max(0, I(val)); return true;
                case "fsrfg_multi_volcado_n": FsrMultiDumpN = Math.Max(1, I(val)); return true;
                case "fsrfg_multi_sin_presentar": FsrMultiNoPresent = B(val); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        const int FmdRing = 8, FmdX = 960, FmdY = 360, FmdW = 1920, FmdH = 1440;
        readonly ID3D12Resource[] fmdRb = new ID3D12Resource[FmdRing];
        readonly IntPtr[] fmdPtr = new IntPtr[FmdRing];
        readonly ulong[] fmdFence = new ulong[FmdRing];
        readonly long[] fmdPres = new long[FmdRing];
        readonly bool[] fmdUsed = new bool[FmdRing];
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT fmdFpA, fmdFpB;
        bool fmdReady;
        int fmdSeen, fmdTaken, fmdCount, fmdPending = -1;
        FileStream fmdFile;
        System.Threading.Thread fmdThread;
        readonly object fmdLock = new object();

        // (hilo de presentacion, en FmhCopyPresent antes de cerrar la lista: src en COPY_SOURCE) el recorte y el codigo a la memoria de lectura
        void FmdRecord(ID3D12Resource src, long pres)
        {
            fmdPending = -1;
            if (s.FsrMultiDump.Length == 0) return;
            FmdCollect();
            fmdSeen++;
            if (fmdSeen <= s.FsrMultiDumpFrom || fmdTaken >= s.FsrMultiDumpN || outW < FmdX + FmdW || outH < Parallax.CodeY + Parallax.CodeSize) return;
            if (!fmdReady)
            {
                fmdFpA = Foot(0, ColorFmt, FmdW, FmdH, 4);
                fmdFpB = Foot(Align512(fmdFpA.Offset + (ulong)fmdFpA.Footprint.RowPitch * FmdH), ColorFmt, Parallax.CodeBits * Parallax.CodeSize, Parallax.CodeSize, 4);
                long total = (long)(fmdFpB.Offset + (ulong)fmdFpB.Footprint.RowPitch * (ulong)Parallax.CodeSize);
                for (int i = 0; i < FmdRing; i++)
                {
                    fmdRb[i] = MakeBuffer(D3D12_HEAP_TYPE.READBACK, total, D3D12_RESOURCE_STATES.COPY_DEST, "FSR FG: volcado " + i);
                    Hr.Check(fmdRb[i].Map(0, null, out fmdPtr[i]), "Map (FSR FG, volcado)");
                }
                fmdFile = new FileStream(s.FsrMultiDump, FileMode.Create, FileAccess.Write);
                BinaryWriter w = new BinaryWriter(fmdFile);
                w.Write(0x314D4746); w.Write(FmdX); w.Write(FmdY); w.Write(FmdW); w.Write(FmdH); w.Write(0); w.Write(Stopwatch.Frequency);
                w.Flush();
                fmdReady = true;
                fmdThread = new System.Threading.Thread(FmdWriter);
                fmdThread.IsBackground = true; fmdThread.Name = "volcado de FSR FG";
                fmdThread.Start();
                Log.Write("FSR FG: volcado de lo presentado a " + s.FsrMultiDump + " (pruebas)");
            }
            int e = -1;
            lock (fmdLock) for (int i = 0; i < FmdRing && e < 0; i++) if (!fmdUsed[i]) e = i;
            if (e < 0) return;   // (todas en vuelo: esta no; el .fgm lo dira como perdida)
            D3D12_BOX box = new D3D12_BOX(FmdX, FmdY, FmdX + FmdW, FmdY + FmdH);
            D3D12_TEXTURE_COPY_LOCATION dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(fmdRb[e], fmdFpA);
            D3D12_TEXTURE_COPY_LOCATION sl = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(src, 0);
            fmhList.CopyTextureRegion(ref dst, 0, 0, 0, ref sl, &box);
            D3D12_BOX cb = new D3D12_BOX(Parallax.CodeX, Parallax.CodeY, Parallax.CodeX + Parallax.CodeBits * Parallax.CodeSize, Parallax.CodeY + Parallax.CodeSize);
            dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(fmdRb[e], fmdFpB);
            fmhList.CopyTextureRegion(ref dst, 0, 0, 0, ref sl, &cb);
            lock (fmdLock) { fmdUsed[e] = true; fmdPres[e] = pres; fmdFence[e] = 0; }
            fmdPending = e;
            fmdTaken++;
        }

        // (tras Signal de la lista) la valla de lo grabado en FmdRecord
        void FmdSubmitted(ulong v) { if (fmdPending >= 0) { lock (fmdLock) fmdFence[fmdPending] = v; fmdPending = -1; } }

        // (hilo del volcado) lo ya copiado, a luma y al fichero, en el orden en que se presento
        void FmdWriter()
        {
            while (!Program.Quit)
            {
                if (!FmdWriteOne()) System.Threading.Thread.Sleep(2);
            }
        }

        // (del hilo de presentacion: ya no hace nada, lo escribe FmdWriter)
        void FmdCollect() { }

        bool FmdWriteOne()
        {
            if (!fmdReady || fmdFile == null) return false;
            ulong done = fmhFence.GetCompletedValue();
            {
                int e = -1; long best = long.MaxValue;
                lock (fmdLock) for (int i = 0; i < FmdRing; i++) if (fmdUsed[i] && fmdFence[i] != 0 && fmdPres[i] < best) { best = fmdPres[i]; e = i; }
                if (e < 0 || done < fmdFence[e]) return false;
                byte* p = (byte*)fmdPtr[e];
                byte[] luma = new byte[FmdW * FmdH];
                uint ra = fmdFpA.Footprint.RowPitch;
                for (int y = 0; y < FmdH; y++)
                {
                    byte* r = p + (ulong)y * ra;
                    int o = y * FmdW;
                    for (int x = 0; x < FmdW; x++, r += 4) { int v = (int)(0.2126 * r[0] + 0.7152 * r[1] + 0.0722 * r[2] + 0.5); luma[o + x] = (byte)(v > 255 ? 255 : v); }
                }
                byte[] code = new byte[16];
                byte* q = p + fmdFpB.Offset;
                uint rb = fmdFpB.Footprint.RowPitch;
                for (int b = 0; b < 16; b++)
                {
                    int x0 = b * Parallax.CodeSize + Parallax.CodeSize / 2, y0 = Parallax.CodeSize / 2;
                    double sm = 0;
                    for (int k = -8; k < 8; k++) { byte* r = q + (ulong)y0 * rb + (ulong)((x0 + k) * 4); sm += 0.2126 * r[0] + 0.7152 * r[1] + 0.0722 * r[2]; }
                    code[b] = (byte)(sm / 16);
                }
                BinaryWriter w = new BinaryWriter(fmdFile);
                w.Write(fmdPres[e]); w.Write(fmdPres[e]); w.Write(1u); w.Write(code); w.Write(luma);
                fmdCount++;
                long end = fmdFile.Position;
                fmdFile.Position = 20; w.Write(fmdCount); fmdFile.Position = end;
                w.Flush();
                lock (fmdLock) { fmdUsed[e] = false; fmdFence[e] = 0; }
                return true;
            }
        }
    }
}
