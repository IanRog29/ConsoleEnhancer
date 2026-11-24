using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedPlano.cs - 24-11-2026 (1.3.3, decima ronda, agente EFGMAPA). El PANEO PLANO de Enhanced FG. El usuario tras jugar con
    // la 1.3.2: "Con enhanced fg al moverme en un mapa del juego y cosas asi tiembla la imagen, me pasa por ejemplo con el mapa del gta 6 al
    // moverlo yo con el joystick". Ver notas\EFGMAPA.md.
    // La causa (medida con un mapa sintetico desplazado con interfaz fija, el simulador de Enhanced FG): en un mapa las lineas son finas y de
    // mucho contraste (carreteras, texto, bordes de iconos). (1) Los vectores por bloques salen con un cuarto de punto de error (cuantizados y
    // con sesgo del bilineal) y en un borde tan afilado las dos muestras de la inventada ya no casan (|anterior - actual| 0,08-0,11): en la
    // inventada (EfgShaders.Fin) gana entonces el vector 0 (castigo 0,02) donde a los dos lados hay suelo liso -> trozos de carretera que
    // desaparecen o se atenuan en una inventada y vuelven en la real: el temblor. (2) Un bloque con una sola linea recta no sabe cuanto se
    // mueve a lo largo de ella (apertura) y elige cada vez otro vector: trozos que van y vienen.
    // Arreglo: por cada pareja de reales, si una traslacion explica la mayoria de los bloques con textura (paneo plano):
    //  - el global PRECISO: media recortada de los vectores de L0 desde el grueso de efg_giro (EfgPlanoShaders.Fit) y refinado con la diferencia
    //    simetrica de toda la imagen en una rejilla de 7x7 pasos de 1/8 de punto y parabola (Cost / Fin): error < 0,05 puntos;
    //  - en la eleccion (EfgShaders.Sel, bit 4096) es candidato y los demas pagan efg_plano_peso x |m - global| (salvo en la interfaz quieta);
    //  - en la inventada (EfgShaders.Fin, FOpts 2) el vector 0 paga efg_plano_cero de mas fuera de lo quieto (en un paneo el 0 solo vale en la
    //    interfaz).
    // Solo al interpolar (modo calidad; la baja latencia tiene su camino propio, EngineFgEnhancedLat.cs). Necesita efg_giro (su textura y su global grueso).
    // (ronda 10c) efg_plano_acel=1 (de serie): al arrancar / frenar el paneo la inventada va donde iria con aceleracion constante, con los
    // intervalos de verdad entre las tres ultimas reales (la hora del contenido, la del ritmo); sin hora fiable o con un hueco > 1,5x, sin ella.
    // Claves (motor): efg_plano (1 de serie; 0 = como la 1.3.2), efg_plano_peso=0.01, efg_plano_cero=0.1, efg_plano_parte=0.5,
    // efg_plano_cerca=2 y efg_plano_quieto=10 (la interfaz quieta que no se toca: radio en puntos de L0 y reales quietas).
    partial class Settings
    {
        public int EfgPlano = 1;
        public float EfgPlanoPeso = 0.01f, EfgPlanoCero = 0.1f, EfgPlanoParte = 0.5f, EfgPlanoCerca = 2f, EfgPlanoQuieto = 10f;
        public int EfgPlanoZoom = 0;   // (ronda 10d) 1 = semejanza (zoom + paneo) en vez de traslacion; 2 = tambien giro
        public bool EfgPlanoAcel = true;   // (ronda 10c) con la hora del contenido de las dos parejas: mide mejor con llegadas regulares e irregulares

        bool ApplyEfgPlano(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "efg_plano": EfgPlano = Math.Max(0, Math.Min(3, I(val))); return true;
                case "efg_plano_peso": EfgPlanoPeso = (float)Math.Max(0, Math.Min(0.1, D(val))); return true;
                case "efg_plano_cero": EfgPlanoCero = (float)Math.Max(0, Math.Min(1, D(val))); return true;
                case "efg_plano_cerca": EfgPlanoCerca = (float)Math.Max(0, Math.Min(8, D(val))); return true;
                case "efg_plano_quieto": EfgPlanoQuieto = (float)Math.Max(1, Math.Min(60, D(val))); return true;
                case "efg_plano_zoom": EfgPlanoZoom = Math.Max(0, Math.Min(2, I(val))); return true;
                case "efg_plano_acel": EfgPlanoAcel = B(val); return true;
                case "efg_plano_parte": EfgPlanoParte = (float)Math.Max(0.2, Math.Min(0.95, D(val))); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        const int EfgPlanoN = 7;   // rejilla del refinado (7x7 pasos de 1/8 de punto de L0: +-3/8)
        ID3D12PipelineState psoEfgPlanoFit, psoEfgPlanoCost, psoEfgPlanoFin;
        ID3D12Resource efgPlanoCost;
        bool efgPlanoReady;
        readonly long[] efgPlanoHoraT = new long[3];
        int efgPlanoHoras;

        // EfgDoJob, por cada real: su hora del contenido (la del ritmo, EngineFgEnhancedRitmo.cs) en la historia de las 3 ultimas reales
        // seguidas con flujo. Sin hora fiable o sin flujo, la historia empieza de nuevo
        void EfgPlanoHora(EfgJob j, bool flow)
        {
            bool ok = j.Content != 0 && j.Content <= j.Arrival && Clock.Ms(j.Content, j.Arrival) <= j.RealMs * 1.2;
            if (!flow || !ok) { efgPlanoHoras = ok ? 1 : 0; efgPlanoHoraT[2] = j.Content; return; }
            efgPlanoHoraT[0] = efgPlanoHoraT[1]; efgPlanoHoraT[1] = efgPlanoHoraT[2]; efgPlanoHoraT[2] = j.Content;
            efgPlanoHoras = Math.Min(3, efgPlanoHoras + 1);
        }

        // los dos intervalos (ms) de la pareja anterior y de esta; false si no hay los tres o el hueco es muy irregular (> 1,5x)
        bool EfgPlanoIntervalos(out double i1, out double i2)
        {
            i1 = i2 = 0;
            if (efgPlanoHoras < 3) return false;
            i1 = Clock.Ms(efgPlanoHoraT[0], efgPlanoHoraT[1]); i2 = Clock.Ms(efgPlanoHoraT[1], efgPlanoHoraT[2]);
            if (i1 < 2 || i2 < 2 || i1 > 200 || i2 > 200) return false;
            return Math.Max(i1, i2) <= 1.5 * Math.Min(i1, i2) + 0.5;
        }
        long efgPlanoPairs, efgPlanoSi, efgPlanoAcelN, efgPlanoAcelNo;

        static readonly string[] EfgPlanoNames = { "efg_plano_ajuste", "efg_plano_coste", "efg_plano_fin" };
        static string[] EfgPlanoSources() { return new string[] { EfgPlanoShaders.Fit, EfgPlanoShaders.Cost, EfgPlanoShaders.Fin }; }

        bool EfgPlanoOn() { return s.EfgPlano != 0 && efgPlanoReady && EfgGiroOn(); }
        // lo que va a la eleccion (bit 4096 de Opts) y a la inventada (bit 2 de FOpts): solo al interpolar (lo dice quien llama)
        uint EfgPlanoSelOpts() { return EfgPlanoOn() && (s.EfgPlano & 1) != 0 ? 4096u : 0u; }
        uint EfgPlanoFinOpts() { return EfgPlanoOn() && (s.EfgPlano & 1) != 0 ? 2u : 0u; }

        // con los de EFGGIRO (EfgGiroPsos): si falla, sin el paneo plano (lo demas sigue)
        void EfgPlanoPsos()
        {
            if (psoEfgPlanoFin != null || s.EfgPlano == 0) return;
            try
            {
                string[] src = EfgPlanoSources();
                byte[][] cs = new byte[src.Length][];
                System.Threading.Tasks.Parallel.For(0, src.Length, delegate (int i) { cs[i] = CompileCs(EfgPlanoNames[i], src[i]); });
                psoEfgPlanoFit = Pso(cs[0]); psoEfgPlanoCost = Pso(cs[1]); psoEfgPlanoFin = Pso(cs[2]);
            }
            catch (Exception ex) { psoEfgPlanoFin = null; Log.Write("Enhanced FG plano: sin el paneo plano (" + ex.Message + ")"); }
        }

        int EfgPlanoPrecompile()
        {
            int n = 0;
            string[] src = EfgPlanoSources();
            for (int i = 0; i < src.Length && !Program.Quit; i++)
                if (!CsCached(EfgPlanoNames[i], src[i])) { CompileCs(EfgPlanoNames[i], src[i]); n++; }
            return n;
        }

        // EfgGiroEnsure, por cada juego c (p = el anterior): sus tablas en el monton de EFGGIRO y el u1 de la inventada
        void EfgPlanoTables(int c, int p)
        {
            if (s.EfgPlano == 0 || psoEfgPlanoFin == null) return;
            const DXGI_FORMAT R = DXGI_FORMAT.R32_FLOAT;
            if (efgPlanoCost == null) efgPlanoCost = Tex(R, 72, 1, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "Enhanced FG: plano, coste");
            EfgGiroSrv(c, EfgGiroTabPlanoFit, 0, efgF[c, 0, 0], DXGI_FORMAT.R16G16B16A16_FLOAT); EfgGiroUav(c, EfgGiroTabPlanoFit, 0, efgGiroG[c], R);
            EfgGiroSrv(c, EfgGiroTabPlanoCost, 0, efgL[c, 0], DXGI_FORMAT.R16_FLOAT); EfgGiroSrv(c, EfgGiroTabPlanoCost, 1, efgL[p, 0], DXGI_FORMAT.R16_FLOAT);
            EfgGiroUav(c, EfgGiroTabPlanoCost, 0, efgGiroG[c], R); EfgGiroUav(c, EfgGiroTabPlanoCost, 1, efgPlanoCost, R);
            EfgGiroUav(c, EfgGiroTabPlanoFin, 0, efgGiroG[c], R); EfgGiroUav(c, EfgGiroTabPlanoFin, 1, efgPlanoCost, R);
            EfgUav(c, EfgTabFin, 1, efgGiroG[c], R);   // (la inventada lee el castigo del 0: [13])
            if (c == EfgSets - 1)
            {
                efgPlanoReady = true;
                Log.Write("Enhanced FG plano: paneo plano (mapas / menus que se desplazan: el global preciso en la eleccion y sin el 0 fuera de la interfaz; efg_plano=" + s.EfgPlano +
                          ", peso " + s.EfgPlanoPeso.ToString("0.###", IC) + ", 0 " + s.EfgPlanoCero.ToString("0.###", IC) + ", parte " + s.EfgPlanoParte.ToString("0.##", IC) + ")");
            }
        }

        // EfgFlowList, con el flujo de L0 hecho (tras su apoyo) y antes del corte: el paneo plano de esta pareja
        void EfgPlanoFit(int c, bool flow)
        {
            if (!EfgPlanoOn() || !flow) return;
            uint* k = stackalloc uint[16];
            efgList.SetDescriptorHeaps(1, ref efgGiroHeapPtr);
            EfgGiroUavBarrier(efgGiroG[c]);
            EfgZero(k);
            k[0] = (uint)efgBx[0]; k[1] = (uint)efgBy[0]; k[2] = (uint)Math.Max(0, Math.Min(2, s.EfgPlanoZoom)); k[8] = F(efgLw[0] * 0.5f); k[9] = F(efgLh[0] * 0.5f); k[4] = F(0.02f); k[5] = F(0.06f); k[6] = F(12f); k[7] = F(s.EfgPlanoParte);
            efgList.SetPipelineState(psoEfgPlanoFit);
            efgList.SetComputeRootDescriptorTable(1, EfgGiroGpu(c, EfgGiroTabPlanoFit));
            efgList.SetComputeRoot32BitConstants(0, 16, k, 0);
            efgList.Dispatch(1, 1, 1);
            EfgGiroUavBarrier(efgGiroG[c]);
            EfgZero(k);
            k[0] = (uint)efgLw[0]; k[1] = (uint)efgLh[0]; k[2] = (uint)EfgPlanoN; k[3] = efgLw[0] > 1400 ? 6u : 4u; k[4] = F(0.125f); k[5] = F(0.15f);
            efgList.SetPipelineState(psoEfgPlanoCost);
            efgList.SetComputeRootDescriptorTable(1, EfgGiroGpu(c, EfgGiroTabPlanoCost));
            efgList.SetComputeRoot32BitConstants(0, 16, k, 0);
            efgList.Dispatch((uint)EfgPlanoN, (uint)EfgPlanoN, 1);
            EfgGiroUavBarrier(efgPlanoCost);
            EfgZero(k);
            double pi1 = 0, pi2 = 0; bool acel = s.EfgPlanoAcel && EfgPlanoIntervalos(out pi1, out pi2);
            if (acel) efgPlanoAcelN++; else if (s.EfgPlanoAcel) efgPlanoAcelNo++;
            k[0] = (uint)EfgPlanoN; k[1] = acel ? 1u : 0u; k[9] = F(0.6f); k[10] = F(acel ? (float)pi1 : 1f); k[11] = F(acel ? (float)pi2 : 1f); k[4] = F(0.125f); k[5] = F(s.EfgPlanoPeso); k[6] = F(s.EfgPlanoCero); k[7] = F(s.EfgPlanoCerca); k[8] = F(s.EfgPlanoQuieto);
            efgList.SetPipelineState(psoEfgPlanoFin);
            efgList.SetComputeRootDescriptorTable(1, EfgGiroGpu(c, EfgGiroTabPlanoFin));
            efgList.SetComputeRoot32BitConstants(0, 16, k, 0);
            efgList.Dispatch(1, 1, 1);
            EfgGiroUavBarrier(efgGiroG[c]);
            efgList.SetDescriptorHeaps(1, ref efgHeapPtr);
        }

        // el registro de EFGGIRO (cada 10 s): lo del paneo plano de la ultima lectura (rb = los 16 de la textura) y cuantas parejas lo eran
        void EfgPlanoNote(float* rb)
        {
            if (!EfgPlanoOn()) return;
            efgPlanoPairs++;
            if (rb[8] > 0) efgPlanoSi++;
        }
        string EfgPlanoLog(float* rb)
        {
            if (!EfgPlanoOn()) return "";
            string r = "; plano: " + efgPlanoSi + " de " + efgPlanoPairs + " parejas (la ultima: global (" + rb[9].ToString("0.00", IC) + ", " + rb[10].ToString("0.00", IC) +
                       "), " + (rb[11] * 100).ToString("0", IC) + " % de " + rb[12].ToString("0", IC) + " bloques" + (rb[8] > 0 ? ", si)" : ", no)") +
                       (s.EfgPlanoAcel ? "; aceleracion con la hora del contenido en " + efgPlanoAcelN + " parejas (sin ella " + efgPlanoAcelNo + ": sin hora, sin las 3 reales seguidas o hueco > 1,5x)" : "");
            efgPlanoPairs = 0; efgPlanoSi = 0; efgPlanoAcelN = 0; efgPlanoAcelNo = 0;
            return r;
        }
    }
}
