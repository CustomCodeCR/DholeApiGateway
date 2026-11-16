# Dhole · APIs para terceros

Esta capacidad **publica endpoints propios de Dhole** (Pricing, CRM, SRM, Configuración, Reportes y Operaciones cuando exista), **no** endpoints Dataverse. Vive en API Gateway y administra contratos desde DholeWeb. Es deliberadamente opt-in para no publicar datos por instalar una versión nueva.

## Arquitectura

- La consola `/integrations/third-party` requiere el scope JWT `integrations.thirdparty.manage`. El backend también lo exige, no solo la interfaz.
- El administrador selecciona una operación del catálogo `ThirdParty:Targets`. El catálogo se configura por despliegue; **la UI jamás acepta una URL upstream arbitraria**.
- La definición especifica `slug`, nombre, `target`, campos de entrada, campos de salida, `responseRoot` y estado activo.
- Una API se crea como borrador y se publica explícitamente. Toda modificación o despublicación invalida sus API keys anteriores.
- Cada clave tiene hash SHA-256 de un secreto aleatorio de 256 bits, vence, tiene cuota por minuto en PostgreSQL, se puede revocar y solo sirve para una API. Se muestra una única vez.
- Las solicitudes externas se reciben en `/api/external/{slug}` o `/api/external/{slug}/{id}` usando la cabecera `X-Api-Key`. No se reenvía la clave a los microservicios.
- Los campos de entrada y los campos devueltos están en allowlists explícitas. Las rutas internas, secretos y respuestas completas no se devuelven al tercero.
- Se registra operación, HTTP status, clave y duración; no se guardan cuerpos ni claves en la auditoría.
- El backend se comunica con el microservicio usando **identidades técnicas propias y limitadas al recurso/cliente autorizado**. Nunca usar un JWT de superadministrador ni credenciales de Dynamics. Para los ocho catálogos fijos GET de Config, Gateway emite un JWT de servicio con solo `config.catalog-items.view` usando la configuración JWT del ecosistema; no se utiliza la sesión del administrador. Para el POST fijo `pricing-rate-request-create` Gateway también puede emitir un JWT efímero con únicamente `pricing.rate-request.create`, atado al contrato aprobado y a su vendedor fijo. En Pricing el actor técnico está limitado a este único POST; si se fija un vendedor real, Auth comprueba que sigue activo. La identidad institucional Castro Fallas utiliza el actor técnico sin inventar un vendedor de Auth. Las demás operaciones requieren `ThirdParty:ServiceTokens:<target-code>` explícito, limitado por usuario/cliente y scope.

## Aprovisionamiento

El módulo de administración y el inventario de rutas se registran siempre, sin flags. La persistencia exige una conexión a PostgreSQL; si falta o falla la migración se responde HTTP 503 con el motivo, sin ocultar rutas como HTTP 404.

Proporcionar mediante variables del servicio (sin poner secretos en el repositorio):

**No hay que crear la base ni las tablas a mano.** El Gateway registra `ThirdPartyDbContext` y ejecuta `Database.MigrateAsync()` al iniciar, cuando existe `ThirdParty__ConnectionString`. El proveedor EF Core de Npgsql crea `dhole_integrations` si no existe y aplica la migración inicial versionada para el esquema `integrations`. Las migraciones posteriores se ejecutan al reiniciar con una imagen actualizada.

El usuario de PostgreSQL configurado en la cadena requiere permiso **CREATEDB** si la base todavía no existe y permisos **CREATE/ALTER** en la base y esquema para aplicar migraciones. Si no se conceden esos permisos a la identidad del Gateway, aprovisionar el almacenamiento con una identidad de migración en el despliegue. No es necesario un comando manual `psql CREATE DATABASE` para una instalación inicial con los permisos correctos.

Si las tablas ya fueron creadas anteriormente por el arranque SQL manual (sin `__EFMigrationsHistory`), hacer una migración/baseline controlada antes de activar EF Core; no aplicar la migración inicial sobre esas tablas porque intentará volver a crearlas.

```env
ThirdParty__ConnectionString=Host=postgres;Database=dhole_integrations;Username=integration_api;Password=<desde gestor de secretos>;SSL Mode=Require
ThirdParty__ServiceTokens__pricing-rate-detail=<JWT TECNICO CON ALCANCE DE SOLO LECTURA>
```

Un ejemplo de target aprobado para GET de Pricing, cuya ruta se verificó en `RateEndpoints.cs`:

```json
{
  "ThirdParty": {
    "ConnectionString": "",
    "Targets": [
      {
        "Code": "pricing-rate-detail",
        "Service": "pricing",
        "Method": "GET",
        "Path": "/api/pricing/rates/{id}",
        "Description": "Detalle de una tarifa autorizada para el cliente de integración"
      }
    ],
    "ServiceTokens": {}
  }
}
```

Ese target es **solo un ejemplo**. La publicación requiere una identidad técnica de Pricing que permita consultar únicamente las tarifas del cliente asignado y verificar el acceso por registro. **Filtrar columnas no sustituye la seguridad por filas o vendedor.** No activar un target hasta validar ese control en el microservicio.

El servicio de destino se resuelve exclusivamente desde `Gateway:Routes`. Para publicar Operaciones en el futuro, desplegar su microservicio, registrar su ruta en Gateway, verificar el endpoint y agregar el target aprobado al catálogo. No hace falta crear otro módulo de administración.

Con una conexión de integración válida, EF Core crea o actualiza automáticamente las tablas `integrations.public_endpoints`, `integrations.public_keys`, `integrations.public_usage` e `integrations.public_audit` en la base de integraciones y lleva el historial en `__EFMigrationsHistory`. Si falta la conexión o falla la migración, los endpoints de lectura de catálogo siguen disponibles y las acciones dependientes de la base devuelven HTTP 503. La identidad para migraciones puede ser distinta de la identidad de ejecución, por seguridad.

## Administración

```text
GET    /api/integrations/third-party/status
GET    /api/integrations/third-party/catalog
GET    /api/integrations/third-party/endpoints
POST   /api/integrations/third-party/endpoints
PUT    /api/integrations/third-party/endpoints/{id}
GET    /api/integrations/third-party/endpoints/{id}/keys
POST   /api/integrations/third-party/endpoints/{id}/keys
DELETE /api/integrations/third-party/keys/{keyId}
```

Los contratos se configuran en el espacio de trabajo del API Manager; las API Keys se crean y administran por separado y pueden vincularse a múltiples APIs.

Contrato de muestra para el target anterior (se crea inactivo):

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "name": "Consulta de tarifa",
  "slug": "consultar-tarifa",
  "target": "pricing-rate-detail",
  "inputs": [],
  "outputs": [
    { "public": "numero", "internal": "quotationNumber", "required": false },
    { "public": "estado", "internal": "status", "required": false }
  ],
  "responseRoot": null,
  "enabled": false
}
```

Los nombres internos del ejemplo son **ilustrativos**: deben comprobarse contra la forma JSON real devuelta por Pricing antes de publicar.

Cliente externo:

```bash
curl -H "X-Api-Key: <clave> " \
  "https://api.logisticacastrofallas.com/api/external/consultar-tarifa/<rate-guid>"
```

## Reglas para producción

No habilitar sin pruebas de aislamiento de clientes, autorización de cada servicio, revocación, cuotas, concurrencia, respuestas JSON, CSRF de la consola, paginación de GET y manejo de errores de backend. En POST/PUT/DELETE deben definirse idempotencia y semántica de concurrencia según el microservicio (por ejemplo ETag y claves de idempotencia). El despliegue del código **no equivale a publicar automáticamente APIs**.

## Metadatos completos de Config (DH-20261115-API-METADATA-161)

Para las APIs de catálogos de Config, los campos `metadata` (objeto JSON completo) y `metadataJson` (valor original) están disponibles como respuesta. Se pueden seleccionar `terminalType`, `transportMode`, `routeRole`, `countryCode`, `iataCode`, `icaoCode` y otros campos reales de `metadataJson`. Las propiedades inexistentes se omiten, nunca se suponen por el nombre del grupo. En POL, POE y POD pueden declararse como filtros de entrada `terminalType`, `transportMode`, `countryCode` e `iataCode`; Gateway los aplica por registro después de obtener los datos de Config. La previsualización GET y el endpoint público usan la misma selección y filtrado. Los contratos publicados previos no cambian automáticamente.

## Publicación de catálogos Config

Los contratos GET predeterminados POL, POE, POD, Incoterms, tamaños y tipos de contenedor y furgón están autorizados automáticamente a usar una **identidad técnica con alcance exclusivo de lectura** cuando Gateway dispone de su configuración Auth:Jwt válida. Publicarlos requiere una acción del administrador con `integrations.thirdparty.manage`; el consumo externo sigue requiriendo una API Key activa y vinculada a la API publicada. La generación del JWT interno ocurre únicamente después de validar esa clave, su vínculo, su estado y su cuota. No se envían credenciales al consumidor.

Esta autorización automática **no** cubre otras operaciones POST/PUT/PATCH/DELETE, rutas descubiertas, catálogos no permitidos ni otros servicios. El único POST permitido es `pricing-rate-request-create`: no admite `sellerUserId` público y utiliza únicamente el vendedor fijado por el administrador en `defaults.sellerUserId`, o la identidad institucional técnica si no existe ese valor. Requieren tokens por ruta y revisión de permisos, aislamiento por cliente y pruebas de integración. La ausencia de credenciales sigue devolviendo 503 y bloquea la publicación. No se incorporan flags ni secretos al repositorio.
