namespace Dhole.ApiGateway.ThirdParty;

// Curated, operation-specific fields. The public API builder never asks an
// administrator to invent upstream property names or arbitrary response paths.
public sealed record ThirdPartyFieldOption(
    string Public, string Internal, string Label,
    bool Required = false, bool Selected = false);

public static class ThirdPartyFieldCatalog
{
    private static ThirdPartyFieldOption F(string key, string label, bool required = false, bool selected = false, string? alias = null) =>
        new(alias ?? key, key, label, required, selected || required);
    private static ThirdPartyFieldOption R(string key, string label, bool selected = false, string? alias = null) =>
        new(alias ?? key, key, label, false, selected);

    public static IReadOnlyList<ThirdPartyFieldOption> Inputs(string code) => code switch
    {
        "pricing-rate-request-create" =>
        [
            F("priority", "Prioridad (Verde, Amarillo o Rojo)", true, alias: "prioridad"),
            F("sellerUserId", "Vendedor responsable (ID)", true, alias: "vendedorId"),
            F("clientName", "Cliente", selected: true, alias: "cliente"),
            F("executiveName", "Nombre del ejecutivo"),
            F("shipmentMode", "Modalidad", selected: true, alias: "modalidad"),
            F("originName", "Origen", selected: true, alias: "origen"),
            F("destinationName", "Destino", selected: true, alias: "destino"),
            F("poeId", "Identificador del puerto de entrada"),
            F("poeName", "Puerto de entrada (POE)"),
            F("podId", "Identificador del destino final"),
            F("podName", "Destino final (POD)"),
            F("payload", "Datos de la operación", true, alias: "detalles"),
        ],
        "pricing-rate-delete" => [ F("ids", "Identificadores de las tarifas a eliminar", true) ],
        "pricing-rates-list" =>
        [
            F("pageNumber", "Número de página"),
            F("pageSize", "Resultados por página"),
            F("search", "Búsqueda"),
            F("idtraNumber", "Número IDTRA"),
            F("quoNumber", "Número QUO"),
            F("polId", "Puerto de origen"),
            F("poeId", "Puerto de entrada"),
            F("podId", "Puerto de destino"),
            F("agentId", "Agente"),
            F("carrierId", "Naviera"),
            F("status", "Estado"),
            F("validFrom", "Vigencia desde"),
            F("validTo", "Vigencia hasta"),
        ],
        "pricing-rate-create" => RateWriteFields(false),
        "pricing-rate-update" => RateWriteFields(true),
        _ => [],
    };

    private static IReadOnlyList<ThirdPartyFieldOption> RateWriteFields(bool updating)
    {
        var fields = new List<ThirdPartyFieldOption>
        {
            F("polId", "Puerto origen: identificador", true),
            F("polName", "Puerto origen: nombre", true),
            F("polCode", "Puerto origen: código", true),
            F("poeId", "Puerto entrada: identificador", true),
            F("poeName", "Puerto entrada: nombre", true),
            F("poeCode", "Puerto entrada: código", true),
            F("podId", "Puerto destino: identificador"),
            F("podName", "Puerto destino: nombre"),
            F("podCode", "Puerto destino: código"),
            F("containerTypeId", "Equipo: identificador", true),
            F("containerTypeName", "Equipo: nombre", true),
            F("containerTypeCode", "Equipo: código", true),
            F("currencyId", "Moneda: identificador", true),
            F("currencyName", "Moneda: nombre", true),
            F("currencyCode", "Moneda: código", true),
            F("freeDays", "Días libres", true),
            F("validFrom", "Válida desde", true),
            F("validTo", "Válida hasta", true),
            F("clientName", "Cliente"),
            F("executiveName", "Ejecutivo"),
            F("executiveUserId", "Ejecutivo: identificador"),
            F("shipmentMode", "Modalidad"),
            F("agentId", "Agente: identificador"),
            F("agentName", "Agente: nombre"),
            F("agentCode", "Agente: código"),
            F("carrierId", "Naviera: identificador"),
            F("carrierName", "Naviera: nombre"),
            F("carrierCode", "Naviera: código"),
            F("incotermId", "Incoterm: identificador"),
            F("incotermName", "Incoterm: nombre"),
            F("incotermCode", "Incoterm: código"),
            F("containerQuantity", "Cantidad de contenedores"),
            F("totalPackages", "Cantidad de bultos"),
            F("totalPallets", "Cantidad de tarimas"),
            F("totalWeightKg", "Peso total (kg)"),
            F("totalVolumeCbm", "Volumen total (m³)"),
            F("pickupAddress", "Dirección de recolección"),
            F("transitTime", "Tiempo de tránsito"),
            F("includes", "Incluye"),
            F("subjectTo", "Sujeto a"),
            F("excludes", "Excluye"),
            F("operationType", "Tipo de operación"),
        };
        if (updating)
        {
            fields.Add(F("extraDetails", "Detalles de venta y costo", true));
            fields.Add(F("removedExtraDetailIds", "Detalles eliminados", true));
            fields.Add(F("expectedRevisionNumber", "Número de revisión esperado"));
        }
        else
        {
            fields.Add(F("details", "Detalles de venta y costo", true));
        }
        return fields;
    }

    public static IReadOnlyList<ThirdPartyFieldOption> Outputs(string code) => code switch
    {
        "pricing-rate-request-create" => [ R("value", "Identificador de solicitud", true, alias: "solicitudId") ],
        "pricing-usd-crc" =>
        [
            R("purchase", "Compra", true, alias: "compra"),
            R("sale", "Venta", true, alias: "venta"),
            R("rateDate", "Fecha", true, alias: "fecha"),
            R("source", "Fuente"),
        ],
        "pricing-rate-exchange-eur-usd" =>
        [
            R("dollars", "Valor en dólares", true, alias: "dolares"),
            R("colones", "Valor en colones", true),
            R("rateDate", "Fecha", true, alias: "fecha"),
        ],
        "pricing-rate-requests-open" or "pricing-rate-request-detail" or "pricing-rate-requests-linked" =>
        [
            R("id", "Identificador", true),
            R("status", "Estado", true, alias: "estado"),
            R("clientName", "Cliente", true, alias: "cliente"),
            R("priority", "Prioridad", true, alias: "prioridad"),
            R("sellerName", "Vendedor"),
            R("shipmentMode", "Modalidad"),
            R("requestedAtUtc", "Fecha de solicitud"),
            R("dueAtUtc", "Fecha límite"),
            R("originName", "Origen"),
            R("destinationName", "Destino"),
        ],
        "pricing-rate-detail" or "pricing-rates-list" =>
        [
            R("id", "Identificador", true),
            R("status", "Estado", true, alias: "estado"),
            R("clientName", "Cliente"),
            R("quoNumber", "Número QUO"),
            R("idtraNumber", "Número IDTRA"),
        ],
        "pricing-rate-create" or "pricing-rate-update" => [ R("id", "Identificador") ],
        _ => [],
    };
}
