namespace Dhole.ApiGateway.ThirdParty;

// Real-world sample schema for POST /api/pricing/rate-requests/.
// Field names follow the Pricing request payload, never sample user values.
// Nested object paths are converted by the API Gateway into valid JSON objects.
// Array fields are selected as complete arrays; item shapes are documented separately.
public static class ThirdPartyRateRequestFormCatalog
{
    public static IReadOnlyList<ThirdPartyFieldOption> Fields { get; } =
    [
        new("form_rateType", "payload.form.rateType", "Tipo de tarifa", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_modality", "payload.form.modality", "Modalidad general", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_shipmentMode", "payload.form.shipmentMode", "Modalidad de embarque", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_originId", "payload.form.originId", "Origen (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_destinationId", "payload.form.destinationId", "Destino (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_podId", "payload.form.podId", "Destino final (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_equipmentSize", "payload.form.equipmentSize", "Tamaño de equipo", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_equipmentType", "payload.form.equipmentType", "Tipo de equipo", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_equipmentId", "payload.form.equipmentId", "Equipo (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_equipmentQuantity", "payload.form.equipmentQuantity", "Cantidad de equipos", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_incotermId", "payload.form.incotermId", "Incoterm (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_cargoCondition", "payload.form.cargoCondition", "Condición de la carga", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_serviceIds", "payload.form.serviceIds", "Servicios solicitados", false, false) { Type = "lista", Group = "Formulario de tarifa" },
        new("form_loadDate", "payload.form.loadDate", "Fecha disponible", false, false) { Type = "fecha", Group = "Formulario de tarifa" },
        new("form_validTo", "payload.form.validTo", "Vigencia final", false, false) { Type = "fecha", Group = "Formulario de tarifa" },
        new("form_selectedImportRateId", "payload.form.selectedImportRateId", "Tarifa importada seleccionada", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_manualRate", "payload.form.manualRate", "Tarifa manual", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_clientId", "payload.form.clientId", "Cliente (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_clientName", "payload.form.clientName", "Nombre de cliente", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_executiveId", "payload.form.executiveId", "Ejecutivo (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_executiveName", "payload.form.executiveName", "Nombre de ejecutivo", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_idtraNumber", "payload.form.idtraNumber", "Número IDTRA", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_otherReferences", "payload.form.otherReferences", "Otras referencias", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_pickupAddress", "payload.form.pickupAddress", "Dirección de recolección", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_warehouseId", "payload.form.warehouseId", "Almacén (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_pickupLatitude", "payload.form.pickupLatitude", "Latitud de recolección", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_pickupLongitude", "payload.form.pickupLongitude", "Longitud de recolección", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_freeDays", "payload.form.freeDays", "Días libres", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_transitDays", "payload.form.transitDays", "Días de tránsito", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_agentId", "payload.form.agentId", "Agente (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_carrierId", "payload.form.carrierId", "Naviera (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_currencyId", "payload.form.currencyId", "Moneda (ID)", false, false) { Type = "UUID", Group = "Formulario de tarifa" },
        new("form_freightCost", "payload.form.freightCost", "Costo del flete", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_freightSale", "payload.form.freightSale", "Venta del flete", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_cabysSearch", "payload.form.cabysSearch", "Buscar CABYS", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_cabysCode", "payload.form.cabysCode", "Código CABYS", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_cargoDescription", "payload.form.cargoDescription", "Descripción de carga", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_cargoObservations", "payload.form.cargoObservations", "Observaciones de carga", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_cargoValue", "payload.form.cargoValue", "Valor de carga", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_cargoWeightKg", "payload.form.cargoWeightKg", "Peso en kilogramos", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_cargoPackages", "payload.form.cargoPackages", "Cantidad de bultos", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_storageDays", "payload.form.storageDays", "Días de almacenaje", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_cargoPallets", "payload.form.cargoPallets", "Cantidad de tarimas", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_cargoLengthCm", "payload.form.cargoLengthCm", "Largo en centímetros", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_cargoWidthCm", "payload.form.cargoWidthCm", "Ancho en centímetros", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_cargoHeightCm", "payload.form.cargoHeightCm", "Alto en centímetros", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_dangerousCargo", "payload.form.dangerousCargo", "Carga peligrosa", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_nonStackable", "payload.form.nonStackable", "No estibable", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_overweight", "payload.form.overweight", "Sobrepeso", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_merchantHaulage", "payload.form.merchantHaulage", "Merchant Haulage", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_carrierHaulage", "payload.form.carrierHaulage", "Carrier Haulage", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_miamiCommercialPlan", "payload.form.miamiCommercialPlan", "Plan comercial Miami", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_miamiWhsQty", "payload.form.miamiWhsQty", "Cantidad WHS Miami", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_miamiIncludeSed", "payload.form.miamiIncludeSed", "Incluir SED Miami", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_miamiSedQty", "payload.form.miamiSedQty", "Cantidad SED Miami", false, false) { Type = "número", Group = "Formulario de tarifa" },
        new("form_miamiBonded", "payload.form.miamiBonded", "Miami bonded", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_miamiDimensionInputUnit", "payload.form.miamiDimensionInputUnit", "Unidad de dimensiones Miami", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_miamiWeightInputUnit", "payload.form.miamiWeightInputUnit", "Unidad de peso Miami", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_portHandlingMode", "payload.form.portHandlingMode", "Tipo de manejo portuario", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_emptyReturn", "payload.form.emptyReturn", "Retorno vacío", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_electronicSeal", "payload.form.electronicSeal", "Marchamo electrónico", false, false) { Type = "booleano", Group = "Formulario de tarifa" },
        new("form_manualName", "payload.form.manualName", "Nombre de cargo manual", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_manualSection", "payload.form.manualSection", "Sección de cargo manual", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_manualChargeBasis", "payload.form.manualChargeBasis", "Base de cobro manual", false, false) { Type = "texto", Group = "Formulario de tarifa" },
        new("form_fclContainers", "payload.form.fclContainers", "Contenedores FCL (lista)", false, false) { Type = "lista", Group = "Formulario de tarifa" },
        new("form_landContainers", "payload.form.landContainers", "Contenedores terrestres (lista)", false, false) { Type = "lista", Group = "Formulario de tarifa" },
        new("form_inlandPickupLocation_address", "payload.form.inlandPickupLocation.address", "Dirección de recolección interior", false, false) { Type = "texto", Group = "Recolección interior" },
        new("form_inlandPickupLocation_latitude", "payload.form.inlandPickupLocation.latitude", "Latitud recolección interior", false, false) { Type = "número", Group = "Recolección interior" },
        new("form_inlandPickupLocation_longitude", "payload.form.inlandPickupLocation.longitude", "Longitud recolección interior", false, false) { Type = "número", Group = "Recolección interior" },
        new("form_inlandDeliveryLocation_address", "payload.form.inlandDeliveryLocation.address", "Dirección de entrega interior", false, false) { Type = "texto", Group = "Entrega interior" },
        new("form_inlandDeliveryLocation_latitude", "payload.form.inlandDeliveryLocation.latitude", "Latitud entrega interior", false, false) { Type = "número", Group = "Entrega interior" },
        new("form_inlandDeliveryLocation_longitude", "payload.form.inlandDeliveryLocation.longitude", "Longitud entrega interior", false, false) { Type = "número", Group = "Entrega interior" },
        new("pickupLocations", "payload.pickupLocations", "Puntos de recolección (lista)", false, false) { Type = "lista", Group = "Datos adjuntos" },
        new("supportEntityId", "payload.supportEntityId", "Identificador de expediente", false, false) { Type = "UUID", Group = "Datos adjuntos" },
        new("supportDocuments", "payload.supportDocuments", "Documentos de respaldo (lista)", false, false) { Type = "lista", Group = "Datos adjuntos" },
        new("requestContext_executiveName", "payload.requestContext.executiveName", "Ejecutivo de la solicitud", false, false) { Type = "texto", Group = "Contexto de solicitud" },
        new("requestContext_equipmentType", "payload.requestContext.equipmentType", "Equipo solicitado", false, false) { Type = "texto", Group = "Contexto de solicitud" },
        new("requestContext_poeId", "payload.requestContext.poeId", "Puerto de entrada (ID)", false, false) { Type = "UUID", Group = "Contexto de solicitud" },
        new("requestContext_poeName", "payload.requestContext.poeName", "Puerto de entrada", false, false) { Type = "texto", Group = "Contexto de solicitud" },
        new("requestContext_podId", "payload.requestContext.podId", "Puerto de destino (ID)", false, false) { Type = "UUID", Group = "Contexto de solicitud" },
        new("requestContext_podName", "payload.requestContext.podName", "Puerto de destino", false, false) { Type = "texto", Group = "Contexto de solicitud" },
        new("requestContext_equipmentQuantity", "payload.requestContext.equipmentQuantity", "Cantidad de equipos", false, false) { Type = "número", Group = "Contexto de solicitud" },
        new("requestContext_containers", "payload.requestContext.containers", "Contenedores (lista)", false, false) { Type = "lista", Group = "Contexto de solicitud" },
        new("requestContext_modality", "payload.requestContext.modality", "Modalidad del contexto", false, false) { Type = "texto", Group = "Contexto de solicitud" },
        new("requestContext_incotermName", "payload.requestContext.incotermName", "Incoterm", false, false) { Type = "texto", Group = "Contexto de solicitud" },
        new("sellerContext_cargoReadyDate", "payload.sellerContext.cargoReadyDate", "Fecha de carga lista", false, false) { Type = "fecha", Group = "Contexto del vendedor" },
        new("sellerContext_portHandlingMode", "payload.sellerContext.portHandlingMode", "Manejo portuario", false, false) { Type = "texto", Group = "Contexto del vendedor" },
        new("sellerContext_consolidatedCargoLines", "payload.sellerContext.consolidatedCargoLines", "Líneas de carga consolidada (lista)", false, false) { Type = "lista", Group = "Contexto del vendedor" },
        new("sellerContext_cargoVolumeUnit", "payload.sellerContext.cargoVolumeUnit", "Unidad de volumen", false, false) { Type = "texto", Group = "Contexto del vendedor" },
        new("sellerContext_cargoWeightInputUnit", "payload.sellerContext.cargoWeightInputUnit", "Unidad peso entrada", false, false) { Type = "texto", Group = "Contexto del vendedor" },
        new("sellerContext_cargoDimensionInputUnit", "payload.sellerContext.cargoDimensionInputUnit", "Unidad dimensiones entrada", false, false) { Type = "texto", Group = "Contexto del vendedor" },
        new("sellerContext_cargoDimensionStorageUnit", "payload.sellerContext.cargoDimensionStorageUnit", "Unidad dimensiones guardadas", false, false) { Type = "texto", Group = "Contexto del vendedor" },
        new("sellerContext_physicalVolumeCbm", "payload.sellerContext.physicalVolumeCbm", "Volumen físico m³", false, false) { Type = "número", Group = "Contexto del vendedor" },
        new("sellerContext_billableVolumeCbm", "payload.sellerContext.billableVolumeCbm", "Volumen facturable m³", false, false) { Type = "número", Group = "Contexto del vendedor" },
        new("sellerContext_chargeableVolumeCbm", "payload.sellerContext.chargeableVolumeCbm", "Volumen cobrable m³", false, false) { Type = "número", Group = "Contexto del vendedor" },
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<ThirdPartyFieldOption>> Items =
        new Dictionary<string, IReadOnlyList<ThirdPartyFieldOption>>
        {
        ["payload.form.fclContainers"] =
        [
            new("containerTypeId", "containerTypeId", "Tipo de equipo (ID)", false, false) { Type = "UUID", Group = "Elementos de lista" },
            new("containerTypeName", "containerTypeName", "Nombre del equipo", false, false) { Type = "texto", Group = "Elementos de lista" },
            new("containerTypeCode", "containerTypeCode", "Código del equipo", false, false) { Type = "texto", Group = "Elementos de lista" },
            new("quantity", "quantity", "Cantidad de contenedores", false, false) { Type = "número", Group = "Elementos de lista" },
            new("freightCostAmount", "freightCostAmount", "Costo del flete", false, false) { Type = "número", Group = "Elementos de lista" },
            new("freightSaleAmount", "freightSaleAmount", "Venta del flete", false, false) { Type = "número", Group = "Elementos de lista" },
            new("unitWeightsKg", "unitWeightsKg", "Pesos de unidades (lista)", false, false) { Type = "lista", Group = "Elementos de lista" },
        ],
        ["payload.form.landContainers"] =
        [
            new("containerTypeId", "containerTypeId", "Tipo de equipo (ID)", false, false) { Type = "UUID", Group = "Elementos de lista" },
            new("containerTypeName", "containerTypeName", "Nombre del equipo", false, false) { Type = "texto", Group = "Elementos de lista" },
            new("containerTypeCode", "containerTypeCode", "Código del equipo", false, false) { Type = "texto", Group = "Elementos de lista" },
            new("quantity", "quantity", "Cantidad", false, false) { Type = "número", Group = "Elementos de lista" },
            new("unitWeightsKg", "unitWeightsKg", "Pesos de unidades", false, false) { Type = "lista", Group = "Elementos de lista" },
        ],
        ["payload.requestContext.containers"] =
        [
            new("containerTypeId", "containerTypeId", "Tipo de equipo (ID)", false, false) { Type = "UUID", Group = "Elementos de lista" },
            new("containerTypeName", "containerTypeName", "Nombre del equipo", false, false) { Type = "texto", Group = "Elementos de lista" },
            new("containerTypeCode", "containerTypeCode", "Código del equipo", false, false) { Type = "texto", Group = "Elementos de lista" },
            new("quantity", "quantity", "Cantidad", false, false) { Type = "número", Group = "Elementos de lista" },
            new("unitWeightsKg", "unitWeightsKg", "Pesos de unidades", false, false) { Type = "lista", Group = "Elementos de lista" },
        ],
        ["payload.pickupLocations"] =
        [
            new("address", "address", "Dirección", false, false) { Type = "texto", Group = "Elementos de lista" },
            new("latitude", "latitude", "Latitud", false, false) { Type = "número", Group = "Elementos de lista" },
            new("longitude", "longitude", "Longitud", false, false) { Type = "número", Group = "Elementos de lista" },
        ],
        ["payload.supportDocuments"] =
        [

        ],
        ["payload.sellerContext.consolidatedCargoLines"] =
        [
            new("description", "description", "Descripción", false, false) { Type = "texto", Group = "Elementos de lista" },
            new("packages", "packages", "Bultos", false, false) { Type = "número", Group = "Elementos de lista" },
            new("pallets", "pallets", "Tarimas", false, false) { Type = "número", Group = "Elementos de lista" },
            new("weightKg", "weightKg", "Peso kg", false, false) { Type = "número", Group = "Elementos de lista" },
            new("lengthCm", "lengthCm", "Largo cm", false, false) { Type = "número", Group = "Elementos de lista" },
            new("widthCm", "widthCm", "Ancho cm", false, false) { Type = "número", Group = "Elementos de lista" },
            new("heightCm", "heightCm", "Alto cm", false, false) { Type = "número", Group = "Elementos de lista" },
        ],
        };

    public static IReadOnlyList<ThirdPartyFieldOption> ArrayFields(string path) =>
        Items.TryGetValue(path, out var fields) ? fields : [];
}
