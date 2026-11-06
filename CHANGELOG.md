# Changelog

## [1.3.0] - 2026-11-05

- Endpoint `/api/system/versions` para versiones declaradas en el manifiesto de release.
- El health del Gateway publica la versión de su ensamblado en ejecución.
- Manifiesto empaquetado con el servicio y validado por CI.

## [1.2.1] - 2026-11-05

- Activa el despliegue automático al recibir push en master y conserva staging en develop.
- Mantiene incremento SemVer de componente y metadatos de versión global.

## [1.2.0] - 2026-11-05

- Incorporates universal Dhole SemVer policy with GitHub Actions checks.
- Synchronizes service assembly metadata with the ecosystem version.
- Adds permanent Codex instructions and coordinated release requirements.
