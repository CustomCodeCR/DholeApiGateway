## [1.10.3] - 2026-11-16 (DholeSystem checkout resiliente en deploy)

- PATCH DH-20261116-GATEWAY-DEPLOY-DNS-168: ambos workflows de Gateway usan un helper compartido para clonar la infraestructura de la rama correspondiente con hasta cuatro intentos y esperas acotadas ante fallos transitorios de DNS o red.
- Solo acepta clones completos con Compose y prepare-environments.sh; evita reutilizar infraestructura previa, diagnostica fallos persistentes del DNS del runner y falla sin construir ni desplegar una imagen nueva.
- Mantiene deploy automático por develop/master, validaciones de servidor, Docker, API Keys y configuración de seguridad.

## [1.10.2] - 2026-11-16 (Identidad técnica para Solicitar tarifa)

- PATCH DH-20261116-API-PRICING-IDENTITY-167: Gateway emite JWT efímero con scope exclusivo `pricing.rate-request.create` para el POST aprobado `pricing-rate-request-create`, sin token manual ni flags.
- El vendedor real procede de `defaults.sellerUserId` fijado en servidor; no se permite vendedor enviado por el consumidor. Sin vendedor fijo se usa actor técnico institucional Castro Fallas.
- Se mantienen API Key, idempotencia, validación de scopes, auditoría y exclusión de rutas arbitrarias.

## [1.10.1] - 2026-11-16 (API Manager: vendedor institucional Castro Fallas)

- PATCH DH-20261116-API-CRM-166: `sellerUserId` deja de ser obligatorio en el contrato externo de solicitud de tarifas. Pricing utiliza el usuario autenticado de la identidad técnica si no se especifica, sin impersonación ni elevación.
- Los vendedores reales de cuentas y prospectos se fijan explícitamente desde CRM y continúan sujetos a la validación de delegación de Pricing. No se altera la base de datos ni la autenticación.

## [1.10.0] - 2026-11-16 (API Manager: valores predeterminados protegidos)

- MINOR DH-20261116-API-MANAGER-165: contratos POST/PUT/PATCH soportan valores predeterminados del lado del servidor, restringidos a campos aprobados y bloqueados para el consumidor externo; contratos antiguos mantienen compatibilidad JSONB.
- Reconoce campos obligatorios cubiertos por defaults y preserva controles de API Key, cuotas, identidad técnica e idempotencia.
- El explorador consulta el inventario completo de los microservicios usando un JWT técnico temporal con scope exclusivo `integrations.thirdparty.manage`, sin exponer ni reenviar el JWT de usuario. Ante fallos muestra las rutas aprobadas y diagnostica el problema, sin presentar una lista vacía.

## [1.9.1] - 2026-11-16 (API Manager: identidad técnica limitada para catálogos Config)

- PATCH DH-20261116-API-MANAGER-164: permite publicar los ocho catálogos GET de Config mediante identidad técnica con scope exclusivo config.catalog-items.view, sin usar la sesión ni permisos del administrador.
- El JWT interno se genera por solicitud únicamente para catálogos fijos permitidos; tokens explícitos por ruta siguen siendo obligatorios para otros servicios. Se preservan las API Keys, límites y auditoría.
- Incluye pruebas de regresión de identidad y gate de compilación .NET. Sin flags.

## [1.9.0] - 2026-11-15 (API Manager: metadatos completos de Config)

- MINOR DH-20261115-API-METADATA-161: las ocho APIs de catálogos permiten seleccionar metadataJson, el objeto metadata completo y propiedades de cada registro, incluidos terminalType (CY/SD/APT), transportMode, routeRole, país y códigos IATA/ICAO.
- Los filtros de terminal, transporte, país e IATA se aplican en Gateway tras consultar Config, con los mismos resultados en previsualización GET y API pública; no se envían filtros desconocidos a Config.
- Preserva API Keys, rutas, REST/realtime y contratos publicados; las nuevas propiedades son optativas. Incluye pruebas de filtrado y JSON anidado.

## [1.8.6] - 2026-11-15 (API Keys migration: correct foreign-key schema)

- PATCH DH-20261115-THIRDPARTY-FK-SCHEMA-160: corrige ForeignKey en 20261114220000_IndependentApiKeys: usa principalSchema="integrations", principalTable="public_keys"/"public_endpoints" y principalColumn="id" por nombre. Antes, argumentos posicionales generaban SQLSTATE 3F000 (schema "id" does not exist).
- Incluye regresión automatizada que inspecciona las dos foreign keys; no borra ni reinicia bases de datos, contratos, claves ni historial EF.
- Garantiza que master prepare de forma idempotente el esquema integrations antes de ejecutar las migraciones EF Core.

## [1.8.3] - 2026-11-15 (Diagnóstico PostgreSQL sin modificación de datos)

- PATCH DH-20261115-THIRDPARTY-STORAGE-READONLY-157: workflow de solo lectura en runner self-hosted para identificar código SQLSTATE, existencia de dhole_integrations, tablas e historial de EF sin imprimir secretos.
- Módulos, contratos y API Keys sin cambios; no se ejecuta ningún DDL ni migración desde el diagnóstico.

## [1.8.2] - 2026-11-15 (API Manager PostgreSQL startup diagnostic)

- PATCH DH-20261115-THIRDPARTY-STORAGE-DIAG-156: deploy de Gateway clasifica en logs del runner SQLSTATE y excepciones de migración PostgreSQL sin imprimir credenciales, cadenas de conexión ni datos sensibles.
- La comprobación es solo de lectura y no modifica almacenamiento, tokens ni tablas.

## [1.8.1] - 2026-11-15 (API Manager: rutas siempre registradas y diagnóstico de storage)

- PATCH DH-20261115-THIRD-PARTY-404-155: elimina ThirdParty:Enabled y registra rutas protegidas del API Manager independientemente de la disponibilidad de PostgreSQL.
- Si no existe conexión o falla una migración, los catálogos siguen disponibles y las operaciones que requieren almacenamiento devuelven HTTP 503 con un código específico, en vez de 404 engañosos.
- Añade GET /api/integrations/third-party/status protegido por integraciones.thirdparty.manage. Conserva JWT, API Keys y autorización de servicios.

## [1.8.0] - 2026-11-14 (API Keys independientes y preview GET seguro)

- DH-20261114-API-KEYS-PREVIEW-154
- MINOR: API Keys independientes con vínculo muchos-a-muchos a múltiples APIs, revocación y migración compatible con claves históricas.
- La previsualización privada solo admite GET con los permisos del JWT administrativo; no precisa publicar ni configurar claves y no permite mutaciones.
- La edición del contrato desvincula únicamente esa ruta cuando cambia el alcance, sin revocar las demás autorizaciones de la clave; habilitar/deshabilitar preserva vínculos.

## [1.7.0] - 2026-11-14 (API Manager universal route discovery)

- MINOR: descubre el inventario protegido de rutas REST de los servicios registrados en el Gateway y habilita la selección/mapeo explícito de contratos.
- Mantiene API Keys por ruta, autorización de integración y destinos internos registrados; no expone el inventario público sin JWT.

## [1.6.2] - 2026-11-14 (API Manager 1.55.0 synchronized release)

- MINOR respecto a master 1.5.3: API externa v1 con ocho catálogos de Config y Solicitudes de Tarifas, API Keys, mapeo por operación y alcance de solo lectura.
- Corrige el registro de versiones para promoción coordinada 1.55.0; publica únicamente rutas aprobadas.

## [1.6.1] - 2026-11-14 (Dhole API Manager promotion)

- MINOR de funcionalidad acumulada respecto a master 1.5.3: rutas `/api/external/v1`, ocho catálogos GET de Config con slugs internos inmutables y mapeo de campos.
- Preserva API Keys y solicitudes de tarifas existentes; operaciones deshabilitadas hasta asignar identidad técnica.
- Coordinación de manifiesto global Dhole 1.53.0 y release candidato sin autorizar producción.

## [1.5.3] - 2026-11-11 (Dhole Pricing seller request history manifest)

- PATCH DH-20261111-PRICING-SELLER-REQUEST-HISTORY-093: catálogo de versiones coordinado Pricing 1.11.3, Web 1.32.2 y Gateway 1.5.3; ecosistema 1.32.2. Sin cambios de routing, API REST ni SignalR.
- Release preparado para la promoción a master después de staging y autorización de producción.

# Changelog

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
