# Seguridad / Security

## Qué hace la app con tu PC

- **Red**: solo se conecta para bajar, la primera vez y con tu permiso, mpv y Magpie de sus releases oficiales de GitHub. Cada zip se
  comprueba con su SHA-256 fijado en `scripts\componentes.json` antes de sacarlo. No hay telemetría ni actualizaciones automáticas.
- **Permisos**: va sin administrador. Solo pide elevación (UAC) para guardar un perfil del driver de NVIDIA (Smooth Motion para
  Magpie), y puedes decir que no.
- **Inicio con Windows**: una tarea programada de tu usuario ("ConsoleEnhancer - inicio"), que se quita desde la bandeja o con
  `ConsoleEnhancer.exe --iniciar-con-windows=0`.
- **Datos**: ajustes y logs en `datos\` (portable) o `%LOCALAPPDATA%\ConsoleEnhancer`. Nada sale de tu PC.
- **DLL**: las de NVIDIA de `motor\` van firmadas por NVIDIA (`scripts\empaquetar.ps1` lo comprueba al montar el zip). Los dos exe de
  ConsoleEnhancer no van firmados; cada release publica el SHA-256 de su zip, y puedes compilarlos tú con `preparar.bat`.

## Versiones con soporte

Solo la última release. Antes de informar de un fallo, prueba con ella.

## Informar de una vulnerabilidad

**No abras un issue público.** Usa *Security > Report a vulnerability* (aviso privado de GitHub) en la página del repositorio, con:
qué es, cómo reproducirlo y qué versión. Se contesta en cuanto se pueda; es un proyecto voluntario y sin garantía (GPL-3.0).

---

**English.** The app only goes online to download mpv and Magpie from their official GitHub releases on first start (SHA-256 pinned in
`scripts\componentes.json`); no telemetry, no auto-update, no admin rights except an optional UAC prompt to save an NVIDIA driver
profile. Only the latest release is supported. Report vulnerabilities privately through GitHub's *Security > Report a vulnerability*,
not in a public issue.
