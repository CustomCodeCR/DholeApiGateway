# Changelog

## [1.5.0] - 2026-11-09 (NotificationsHub universal realtime pilot; candidate)

- MINOR: Gateway no longer registers the duplicate universal SignalR host; existing Notifications WebSocket proxy retained.
- Ecosystem candidate 1.14.0 / 16 Dhole repositories; CustomCodeFramework excluded. Release `rt-20261107-01`, isolated branch `feature/rt-notificationshub-20261109-01`.
- Staging compile, deployment and post-deploy security audit pending; `develop`, `master` and production not changed by this candidate.


## [1.4.7] - 2026-11-08 (Dhole realtime coordinated F2/F6/F10 hardening)

- PATCH ecosystem 1.13.15; service-version catalog synchronized across the 13 service producers and Gateway. F10 production branch-deletion step removed in DholeSystem; F2/F6 AOF runtime checks are read-only.
- Gateway realtime feature flags remain OFF; staging WSS/owner ACL not formally approved; no deploy/merge claimed.



## [1.4.6] - 2026-11-08 (F9 pre-staging candidate)

- Corrige secuencia de smoke F9: ejecutar HTTPS/Hub en el workflow propio de Gateway inmediatamente después de su deploy a staging, usando solo script versionado en DholeSystem/develop, sin JWT QA. Resultado sanitizado disponible como artifact; fallo 404 bloquea certificación, nunca se marca éxito.

- Alineado catálogo de versiones de los 13 servicios a release global 1.13.14; validación TLS/WSS post-deploy staging sigue pendiente, sin cambios de autenticación ni activación del Hub. Evidencia aislada de F3 permanece válida para revisión de PR; merges/despliegues requieren gates de seguridad del release.



## [1.4.5] - 2026-11-08 (F3 no-QA WSS CI; candidate)

- Integra prueba HTTPS/WSS con Traefik y Gateway real en GitHub-hosted CI, dos identidades sintéticas autorizadas y una denegada; Redis real efímero, JWT y TLS autogenerados y verificación de ACL REST. Sin variables o secretos manuales de staging.
- CI #42538238853 PASS: suite Gateway original y nuevo ensayo de transporte TLS/WSS. Esto no acredita entorno de staging, revocación comercial real ni gate F3 formal; no se activaron flags ni se desplegó.
- Bump PATCH y catálogo de versiones de los 13 servicios; versión global candidata 1.13.13; ramas develop/master intactas.



## [1.4.4] - 2026-11-08 (F9 candidate only)

- Actualizado el registro `service-versions.json` con la versión global `1.13.12` y los 13 microservicios coordinados. PATCH Gateway 1.4.3 → 1.4.4; API REST/WS y flags sin cambios; requiere gates formales antes del release.



## [1.4.3] - 2026-11-08 (F9 candidato, sin despliegue)

- Fija el catálogo `service-versions.json` de Gateway con versiones definitivas de los 13 microservicios y Gateway para el batch global `1.13.11`. PATCH Gateway `1.4.2` → `1.4.3` para evitar entradas obsoletas tras los últimos bumps de sincronización.
- Sin modificación de rutas REST, SignalR, ACL ni secretos; no se han activado los flags ni fusionado la PR F9.



## [1.4.2] - 2026-11-08 (F9 candidato, sin despliegue)

- Reconciliado `Dhole.ApiGateway/service-versions.json` con la versión global candidata `1.13.11` y el catálogo de versiones independientes de los 14 servicios (13 microservicios + Gateway), corrigiendo el fallo del check principal de versión de Gateway.
- El Hub universal, JWT/ticket y el bridge mantienen los flags de aprobación desactivados; sin cambios de rutas REST o ACL. PATCH de Gateway `1.4.1` → `1.4.2`.



## [1.4.1] - 2026-11-08 (candidato F9, sin publicación)

- Sincroniza metadatos de versión global del batch Dhole Realtime `rt-20261107-01` a `1.13.10`; actualización PATCH independiente de DholeApiGateway `1.4.0` → `1.4.1`. No cambia contratos REST, datos, lógica de negocio ni flags; requiere gates F0–F8 y CI de SHA final antes de merge.



## [1.4.0] — 2026-11-07 (candidato F3, NO liberado)

- F6.02: añade el stream Redis durable `dhole.pricing.realtime.v1` con cursor/consumer group propio y validación de audiencias `pricing.rate`, preservando el stream CRM y la reautorización REST por entrega. [CI #42492026596](https://github.com/CustomCodeCR/DholeApiGateway/actions/runs/42492026596) 106/106 PASS.
- F6 de seguridad: `resourceRevoked` envía un control sin datos de negocio al cliente previamente suscrito para eliminar la vista al revocar el acceso (403/404). [CI #42492902414](https://github.com/CustomCodeCR/DholeApiGateway/actions/runs/42492902414) PASS, incluye revocación multiusuario. Gateway `1.4.0` sigue versión candidata del mismo release.
- Introduce Hub universal WSS candidato `/hubs/realtime` y endpoint de tickets opacos `/hubs/realtime/ticket`, ambos aislados detrás de los flags generales desactivados.
- Seguridad por recurso: los grupos los calcula exclusivamente el servidor, el propietario REST reautoriza en la suscripción y antes de cada emisión, y las notificaciones no contienen entidades completas ni datos sensibles.
- Bridge durable Redis Streams con grupo estable por instancia, Inbox PostgreSQL, cuarentena de eventos inválidos y replay de pendientes. No sustituye el Hub existente de notificaciones.
- Pruebas F3 de CI aislada: 100/100 PASS en run 42485284958 (SHA 9ec7c1a60b21450c04aebca181106cf6ea11c4ce), incluidas dos instancias TestServer Gateway contra Redis/Postgres y recuperación del cursor durable tras reinicio. No prueba WSS TLS real con Traefik de staging.
- Sin integración a `develop` ni `master`, sin despliegue ni activación realtime.
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
