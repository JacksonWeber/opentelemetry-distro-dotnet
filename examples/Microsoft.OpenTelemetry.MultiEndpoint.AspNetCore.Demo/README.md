# Multi-Endpoint Routing: ASP.NET Core Demo

A hosted ASP.NET Core application that routes each customer's telemetry to that customer's
Application Insights component, using one Azure Monitor exporter.

Traces, logs, and metrics all carry the destination:

| Signal | How the destination is attached |
|---|---|
| Traces | `RoutingActivityProcessor` stamps every activity, including ones from instrumentation libraries |
| Logs | `RoutingLogProcessor` stamps every record, so ordinary `ILogger` calls need no changes |
| Application metrics | `OrderMetrics` passes the dimensions to `Counter.Add` and `Histogram.Record` |
| `http.server.request.duration` | `CustomerContextMiddleware` uses `IHttpMetricsTagsFeature` |
| `http.client.request.duration` | `RoutingMetricsHandler` uses `HttpMetricsEnrichmentContext` |

## Prerequisite: enable the routing switch

Multi-endpoint routing is off by default. This sample turns it on in its project file, which applies
the setting before any application code runs:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Azure.Monitor.OpenTelemetry.EnableMultiEndpointRouting"
                                  Value="true" />
</ItemGroup>
```

The switch is read once, the first time it is needed, so it has to be set before the exporter is
constructed. `AppContext.SetSwitch` works too, but only if it runs before OpenTelemetry is
registered.

**Do not copy the routing classes without the switch.** With routing off, the exporter does not
consume the routing attributes: every customer's telemetry goes to whatever single connection string
the process has - `APPLICATIONINSIGHTS_CONNECTION_STRING` is often already set on a development
machine or in App Service - and arrives there carrying the other customers' instrumentation keys and
endpoints as custom dimensions.

## How a request is routed

1. `CustomerContextMiddleware` authorizes the caller and stamps an opaque `contoso.customer_id` on
   the request activity. It never stamps the connection string or the routing values themselves.
2. `CustomerRouting` resolves that identifier against `CustomerCatalog`, the server-owned map loaded
   from configuration. Child activities such as the outgoing HTTP call carry no identifier of their
   own, so it walks up `Activity.Parent` to the request activity.
3. The processors attach `microsoft.instrumentation_key` and `microsoft.ingestion_endpoint`, and the
   exporter sends each item to the endpoint it names.

When no customer can be authorized, the resolver returns `null`, the processors remove any routing
attributes, and the telemetry is dropped. It is never sent to another customer's component.

## Configure

Each customer needs a connection string containing an explicit `IngestionEndpoint`. Supply them
outside source control - user secrets, or environment variables:

```powershell
$env:MultiEndpointDemo__Customers__0__ConnectionString = "<contoso connection string>"
$env:MultiEndpointDemo__Customers__1__ConnectionString = "<fabrikam connection string>"
```

Add more customers by adding entries to the `MultiEndpointDemo:Customers` array in
`appsettings.json` and supplying the matching connection strings.

Misconfiguration fails at startup rather than silently dropping telemetry at runtime: a missing
`Id` or `ApiKey`, a duplicate of either, or a connection string without an explicit
`IngestionEndpoint` all throw.

## Run

```powershell
dotnet run --project .\examples\Microsoft.OpenTelemetry.MultiEndpoint.AspNetCore.Demo
```

Send a request as each customer:

```powershell
Invoke-RestMethod http://localhost:5000/orders -Headers @{ 'X-Api-Key' = 'contoso-key' }
Invoke-RestMethod http://localhost:5000/orders -Headers @{ 'X-Api-Key' = 'fabrikam-key' }
Invoke-RestMethod http://localhost:5000/health
```

`/orders` produces a request span, a nested client span, a log, and two application metrics, all
routed to the caller's component.

`/health` has no API key of its own, so nothing routes it. Note that `/orders` calls `/health` over
HTTP: the outgoing client span belongs to the caller and is routed, but the incoming `/health`
request starts a new activity whose parent is remote, so `Activity.Parent` is null and it resolves
to no customer. That is the boundary of this technique - **each service must resolve the destination
itself**; the routing context does not survive an HTTP hop.

## Verify isolation

Query each component. Each must contain only its own customer's telemetry:

```kusto
union withsource=Signal requests, dependencies, traces, customMetrics
| where timestamp > ago(30m)
| summarize count() by Signal, cloud_RoleName
```

`cloud_RoleName` comes from the `microsoft.multi_endpoint_cloud_role` attribute, so each component
shows the role configured for that customer and no other.

## Before copying this pattern

The API key header stands in for authentication so the sample stays self-contained. Replace
`CustomerCatalog.AuthorizeCustomer` with your real authentication and authorization; only the lookup
that follows it should stay the same.

What must not change: the destination is chosen from a server-owned store after the caller is
authorized. Never take an instrumentation key or ingestion endpoint from a request header,
propagated baggage, or a request body. Those values are validated for shape but not for ownership,
so a caller-supplied endpoint would receive your telemetry.

Traces and logs have a safety net - the processors overwrite whatever routing attributes an item
arrives with. **Metrics do not.** Whatever the measurement call passes is final, and the exporter
takes the first occurrence of each dimension, so never let request data reach an instrument's tags.

## Metric cardinality

Routing dimensions multiply a metric's time series by the number of customers. Past OpenTelemetry's
default limit of 2000 series per instrument, further attribute combinations collapse into a single
overflow point tagged `otel.metric.overflow=true`, which carries no routing dimensions and is
therefore dropped.

`http.server.request.duration` is the most exposed, since it is already multiplied by route and
status code. Keep business dimensions bounded, and raise
`MetricStreamConfiguration.CardinalityLimit` deliberately for the instruments that need it.

## What routing changes

Live Metrics, standard metrics, and performance counters are unavailable while routing is enabled,
and Microsoft Entra ID authentication cannot be used. Rate-limited sampling is ignored in favour of
fixed-rate `SamplingRatio`, because a per-process rate limit would be shared across every customer.
