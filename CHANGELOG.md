# Changelog

## [1.4.0] — 2026-11-07 (candidato F3, NO liberado)

- Introduce Hub universal WSS candidato `/hubs/realtime` y endpoint de tickets opacos `/hubs/realtime/ticket`, ambos aislados detrás de los flags generales desactivados.
- Seguridad por recurso: los grupos los calcula exclusivamente el servidor, el propietario REST reautoriza en la suscripción y antes de cada emisión, y las notificaciones no contienen entidades completas ni datos sensibles.
- Bridge durable Redis Streams con grupo estable por instancia, Inbox PostgreSQL, cuarentena de eventos inválidos y replay de pendientes. No sustituye el Hub existente de notificaciones.
- Pruebas de F3 aisladas en CI, sin integración a `develop` ni `master`, sin despliegue, sin activación realtime.
- El gate F3 requiere F0–F2 aprobados, WSS real por Gateway, permisos en producción/staging y topología multiinstancia verificada; todavía no se declara aprobado.


## [1.3.1] - 2026-11-06

- Actualiza el manifiesto central para el cierre de cola AI de correos recibidos originalmente en octubre de 2026.
- Registra DholeAIService 1.3.9 y DholeDataExtractionService 1.3.5, conservando versiones existentes de componentes no incluidos en el release.
- Sin cambios de rutas API; ecosistema Dhole 1.3.12, release DH-20261106-OCTOBER-EMAIL-AI-QUEUE-014.

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
