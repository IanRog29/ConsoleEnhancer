## Qué cambia / What changes

<!-- Qué hace el cambio y por qué. Si arregla un issue: "Arregla #123". -->

## Comprobado / Checklist

- [ ] `preparar.bat sindescargar` sin errores (compilan los dos exe y pasa `DeteccionTest --simula=todo`)
- [ ] Solo C# 5 (nada de C# 6 o posterior)
- [ ] Sin binarios, nada no redistribuible ("DLSS 5", `dlssg_sm86`, cabeceras de SDK propietarios) ni datos personales (rutas, logs, ini)
- [ ] El código de otros proyectos lleva su licencia compatible con la GPL-3.0 y está en `THIRD-PARTY.md`
- [ ] Textos de pantalla en español e inglés; `CHANGELOG.md` (sección *Sin publicar*) si cambia algo que se ve

Al mandar este cambio acepto publicarlo con la licencia del proyecto (GPL-3.0 + `LICENSE-EXCEPTION.txt`). /
By submitting this change I agree to license it under the project's license (GPL-3.0 + `LICENSE-EXCEPTION.txt`).
