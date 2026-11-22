# Seguridad / Security

## Qué hace la app con tu PC

- **Red**: solo se conecta para bajar, la primera vez y con tu permiso, mpv y Magpie de sus releases oficiales de GitHub. Cada zip se
  comprueba con su SHA-256 fijado en `scripts\components.json` antes de sacarlo. No hay telemetría ni actualizaciones automáticas.
- **Permisos**: va sin administrador. Solo pide elevación (UAC) para guardar un perfil del driver de NVIDIA (Smooth Motion para
  Magpie), y puedes decir que no.
- **Inicio con Windows**: solo si lo pides (asistente, bandeja o panel de ajustes): un acceso directo `ConsoleEnhancer.lnk` en tu
  carpeta Inicio (1.2.08; antes, una tarea programada "ConsoleEnhancer - inicio", que se quita sola). Desde la 1.2.3 una copia nueva
  no se pone sola en el inicio aunque la anterior lo estuviera: lo pregunta. Se quita desde la bandeja o con
  `ConsoleEnhancer.exe --iniciar-con-windows=0`.
- **Desinstalar**: (1.2.3) sin scripts ni PowerShell: la propia app borra sus archivos al cerrarse; su exe, que no se puede borrar
  mientras está abierto, va a `%TEMP%` y se borra la próxima vez que abras ConsoleEnhancer.
- **Microsoft Defender**: la app no cambia su configuración. Solo lee su registro de sucesos para avisar si Defender la ha cerrado, y
  puede abrir Seguridad de Windows para que tú añadas una exclusión.
- **Datos**: ajustes y logs en `data\` (portable) o `%LOCALAPPDATA%\ConsoleEnhancer`. Nada sale de tu PC.
- **DLL y exe**: las DLL de NVIDIA de `engine\` van firmadas por NVIDIA (`scripts\empaquetar.ps1` lo comprueba al montar el zip). Los
  tres exe de ConsoleEnhancer aún no van firmados; llevan su versión y su editor (Propiedades > Detalles), cada release publica el
  SHA-256 de su zip y de cada fichero de dentro (`.files.sha256`), y puedes compilarlos tú con `preparar.bat`.

## Versiones con soporte

Solo la última release. Antes de informar de un fallo, prueba con ella.

## Informar de una vulnerabilidad

**No abras un issue público.** Usa *Security > Report a vulnerability* (aviso privado de GitHub) en la página del repositorio, con:
qué es, cómo reproducirlo y qué versión. Se contesta en cuanto se pueda; es un proyecto voluntario y sin garantía (GPL-3.0).

---

**English.** The app only goes online to download mpv and Magpie from their official GitHub releases on first start (SHA-256 pinned in
`scripts\components.json`); no telemetry, no auto-update, no admin rights except an optional UAC prompt to save an NVIDIA driver
profile. It only starts with Windows if you ask for it (since 1.2.3 a new copy asks instead of adding itself), and uninstalling runs
no scripts. The exes are not code-signed yet; each release lists the SHA-256 of the zip and of every file in it. Only the latest
release is supported. Report vulnerabilities privately through GitHub's *Security > Report a vulnerability*,
not in a public issue.
