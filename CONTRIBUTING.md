# Cómo contribuir / Contributing

¡Gracias por querer ayudar! Fallos, ideas y cambios son bienvenidos. *Bug reports, ideas and pull requests are welcome, in Spanish or
English.*

## Informar de un fallo

Usa la plantilla de **Issues > Nuevo > Fallo**. Lo más útil:

- tu gráfica, pantalla (resolución y Hz), capturadora y consola;
- qué esperabas y qué pasó, y si pasa siempre;
- el log: `datos\logs\consoleenhancer.log` y `engine.log` (o en `%LOCALAPPDATA%\ConsoleEnhancer\logs`). **Revísalo antes** de subirlo:
  puede llevar nombres de tus carpetas o de tus juegos.

No subas capturas de juegos con derechos de autor salvo que hagan falta para entender el fallo (y entonces, solo un recorte).

## Compilar

Doble clic en `preparar.bat` (baja las dependencias, compila y pasa las pruebas). Detalle en el README, *Para desarrolladores*.

## Reglas del código

- **C# 5** (el `csc.exe` de .NET Framework 4.8 que trae Windows): nada de interpolación `$"..."`, `?.`, `nameof`, `=>` en miembros,
  inicializadores de propiedades automáticas ni otras cosas de C# 6 o posterior. `build.bat` y `build_motor.bat` lo comprueban al
  compilar.
- Sin dependencias nuevas que haya que instalar. Si hace falta un programa de terceros, se añade a `scripts\componentes.json`
  (versión fija, release oficial y SHA-256) y se explica su licencia en `THIRD-PARTY.md`.
- Comentarios y log en español; los textos de pantalla en español e inglés (`Tx.T("es", "en")` o `idiomas\en.txt`).
- Antes de mandar un cambio: `preparar.bat sindescargar` sin errores (compila los dos exe y pasa `DeteccionTest --simula=todo`).

## Lo que no puede ir al repositorio

El CI lo comprueba y rechaza el cambio si aparece:

- **Binarios** (`.exe`, `.dll`, `.zip`...): solo el icono propio de `recursos\`. Los programas de terceros se bajan con
  `scripts\descargar_dependencias.ps1`.
- **Nada no redistribuible**: `nvngx_dlssnr.dll` ("DLSS 5"), `runtimes\dlssg_sm86`, cabeceras o librerías de SDK propietarios (NGX
  del SDK de DLSS...), código que eluda mecanismos de autenticación o protección de terceros.
- **Código copiado sin su licencia**: si traes código de otro proyecto, tiene que tener una licencia compatible con la GPL-3.0 (MIT, BSD,
  Apache-2.0, GPL-3.0...), con su aviso de copyright en el fichero y una línea en `THIRD-PARTY.md`.
- **Datos personales**: rutas de tu PC (tu carpeta de usuario, `%USERPROFILE%`...), correos, nombres de equipo, logs, tu `consoleenhancer.ini`, listas de tus juegos.
  Pon tus patrones en `scripts\privado.txt` (no va a git) y `scripts\empaquetar.ps1` también los buscará en el paquete.
- **Imágenes con derechos** (capturas de juegos, logotipos de consolas o de fabricantes).

## Licencia de las contribuciones

Al mandar un cambio aceptas que se publique bajo la licencia del proyecto: GPL-3.0 con el permiso adicional de `LICENSE-EXCEPTION.txt`
(sin él, el proyecto no podría repartirse con las DLL de NVIDIA). Escribe solo código que sea tuyo o que tengas derecho a aportar.

---

**English.** Use the issue templates; attach `consoleenhancer.log` / `engine.log` after checking them for private data. Code is C# 5
(Windows' built-in .NET Framework 4.8 `csc.exe`: no C# 6+ features). Never commit binaries, non-redistributable files (`nvngx_dlssnr.dll`,
`dlssg_sm86`, proprietary SDK headers), code that circumvents third-party protections, code without a GPL-3.0-compatible license and
notice, personal data or copyrighted images. Contributions are accepted under GPL-3.0 plus the additional permission in
`LICENSE-EXCEPTION.txt`. Run `preparar.bat sindescargar` before opening a pull request.
