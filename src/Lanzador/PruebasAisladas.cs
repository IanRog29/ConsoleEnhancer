// PruebasAisladas.cs - 22-11-2026 (1.2.2, agente ERRORES): lo que las pruebas sin consola (--test-*) ponen de serie mientras corren,
// para que no dependan de lo elegido en el PC donde se pasan (y sin escribir nada en data\).
// C# 5
using System;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // El modelo de DLSS elegido en el menu de ajustes (games\dlss_model_choice.txt, para todos los juegos) manda sobre el de la prueba:
        // --test-escala pedia DLSS E y, con la M elegida por el usuario, la config de Magpie salia con M (9 fallos en su PC). Pone v solo en
        // memoria (el fichero no se toca) y devuelve el de antes, para volver a ponerlo al acabar
        internal static string DlssPickSoloPrueba(string v)
        {
            string antes = DlssPick;
            string n = (v ?? "").Trim().ToUpperInvariant();
            if (Array.IndexOf(DlssPicks, n) < 0) n = "";
            lock (dlssPickSync) dlssPick = n;
            return antes;
        }
    }
}
