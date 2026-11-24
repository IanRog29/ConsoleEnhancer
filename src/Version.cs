// Version.cs - la version de ConsoleEnhancer y quien la publica, en las propiedades de los tres exe (clic derecho > Propiedades >
// Detalles, el Administrador de tareas, Microsoft Defender / SmartScreen). Lo compilan build.bat (ConsoleEnhancer.exe y
// "ConsoleEnhancer Settings.exe") y build_motor.bat (engine\ConsoleEnhancerEngine.exe). Con estos atributos csc escribe el recurso de
// version de Windows (VERSIONINFO): CompanyName, FileVersion, ProductVersion y LegalCopyright; FileDescription, ProductName y Comments
// salen del AssemblyInfo de cada exe (AssemblyTitle / AssemblyProduct / AssemblyDescription) y OriginalFilename / InternalName, del
// nombre del exe. El manifiesto de cada exe: resources\app.manifest y resources\engine.manifest.
// 1.2.3 (22-11-2026, DEFENDER): hasta la 1.2.2 los exe iban sin version (0.0.0.0), sin editor y con el manifiesto de serie de csc.
//
// AL PUBLICAR: CeVersion.Texto = la etiqueta sin la v (git tag v1.2.3 -> "1.2.3"). Lo comprueba scripts\empaquetar.ps1 con -Version
// (el CI de GitHub lo pasa con la etiqueta): si no coincide, no se monta el zip.
using System.Reflection;

[assembly: AssemblyCompany("IanRog29")]
[assembly: AssemblyCopyright("Copyright (C) 2026 IanRog29 and ConsoleEnhancer contributors. License: GPL-3.0")]
[assembly: AssemblyVersion(CeVersion.Numero)]
[assembly: AssemblyFileVersion(CeVersion.Numero)]
[assembly: AssemblyInformationalVersion(CeVersion.Texto)]

static class CeVersion
{
    // La version de la app (la de la etiqueta de git sin la v)
    public const string Texto = "1.3.2";
    // La de Windows: cuatro numeros (1.3.2: "1.3.2" + ".0"; con dos, como "1.3", seria Texto + ".0.0")
    public const string Numero = Texto + ".0";
}
