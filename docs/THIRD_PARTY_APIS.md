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
- El backend se comunica con el microservicio usando **identidades técnicas propias y limitadas al recurso/cliente autorizado**. Nunca usar un JWT de superadministrador ni credenciales de Dynamics.

## Aprovisionamiento

El flag está deshabilitado por defecto tanto en `master` como en `develop`.

Proporcionar mediante variables del servicio (sin poner secretos en el repositorio):

```env
ThirdParty__Enabled=true
ThirdParty__ConnectionString=Host=postgres;Database=dhole_integrations;Username=integration_api;Password=<desde gestor de secretos>;SSL Mode=Require
ThirdParty__ServiceTokens__pricing-rate-detail=<JWT TECNICO CON ALCANCE DE SOLO LECTURA>
```

Un ejemplo de target aprobado para GET de Pricing, cuya ruta se verificó en `RateEndpoints.cs`:

```json
{
  "ThirdParty": {
    "Enabled": false,
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

Con `ThirdParty__Enabled=true`, Gateway inicializa las tablas `integrations.public_endpoints`, `integrations.public_keys`, `integrations.public_usage` e `integrations.public_audit` en la base de integraciones. El usuario PostgreSQL usado en el arranque necesita permisos de DDL; en despliegues endurecidos ejecutar esa migración mediante una identidad de migración separada antes de dar acceso a la identidad runtime.

## Administración

```text
GET    /api/integrations/third-party/catalog
GET    /api/integrations/third-party/endpoints
POST   /api/integrations/third-party/endpoints
PUT    /api/integrations/third-party/endpoints/{id}
GET    /api/integrations/third-party/endpoints/{id}/keys
POST   /api/integrations/third-party/endpoints/{id}/keys
DELETE /api/integrations/third-party/keys/{keyId}
```

Los contratos se editan en un drawer con controles Dhole. Las claves se emiten/revocan en esa misma pantalla.

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
