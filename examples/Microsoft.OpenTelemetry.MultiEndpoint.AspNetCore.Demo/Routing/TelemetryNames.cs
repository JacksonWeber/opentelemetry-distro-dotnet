// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace MultiEndpointDemo.Routing;

public static class TelemetryNames
{
    public const string ActivitySource = "Contoso.Orders";
    public const string Meter = "Contoso.Orders";

    /// <summary>Opaque customer identifier. Never the connection string or the routing values.</summary>
    public const string CustomerIdTag = "contoso.customer_id";

    /// <summary><see cref="HttpContext.Items"/> key holding the resolved destination for the request.</summary>
    public const string DestinationItem = "contoso.routing_destination";

    public const string InstrumentationKey = "microsoft.instrumentation_key";
    public const string IngestionEndpoint = "microsoft.ingestion_endpoint";
    public const string CloudRole = "microsoft.multi_endpoint_cloud_role";
}
