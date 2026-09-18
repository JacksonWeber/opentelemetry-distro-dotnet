# Multi-endpoint routing to Azure Monitor (.NET)

> **Preview.** This feature is off by default and is enabled with a runtime switch. The behaviour
> described here may change before it is generally available.

Multi-endpoint routing lets one application send telemetry to several Application Insights
components. A service handling work for several customers can send each customer's telemetry to that
customer's component.

Your application supplies the destination for every telemetry item:

- **Traces and logs** - an OpenTelemetry processor attaches the destination as attributes.
- **Metrics** - the destination is passed as dimensions when the measurement is recorded.

Identifying the customer and storing their connection details is your application's job; routing
prescribes no particular store, cache, or context mechanism.

If your application sends all telemetry to one component, use the standard configuration in
[Azure Monitor Getting Started](azure-monitor-getting-started.md).

Read the setup sections in order: each sample uses only types introduced before it. For code you can
run without writing a resolver first, see [Sample](#sample).

## 1. Requirements

- **A `Microsoft.OpenTelemetry` release newer than 1.1.0.** Multi-endpoint routing is not in 1.1.0 or
  any earlier release; those versions ignore the switch and the guidance below has no effect. The
  [changelog](../CHANGELOG.md) lists the release that introduces it.
- .NET 8 or later. Earlier target frameworks are not supported by the samples, which use C# 12
  primary constructors and collection expressions.
- **Each destination must allow connection string authentication.** Routing identifies a component by
  instrumentation key, so a component with
  [local authentication disabled](https://learn.microsoft.com/azure/azure-monitor/app/azure-ad-authentication#disable-local-authentication)
  cannot receive routed telemetry, and Microsoft Entra ID authentication is unavailable while routing
  is on.
- **Each destination needs an explicit ingestion endpoint.** See
  [Destinations and connection strings](#destinations-and-connection-strings).

## 2. The routing attributes

Every routed item carries these attributes:

| Attribute | Required | Value |
|---|---|---|
| `microsoft.instrumentation_key` | Yes | The customer's instrumentation key. |
| `microsoft.ingestion_endpoint` | Yes | The customer's ingestion endpoint. |
| `microsoft.multi_endpoint_cloud_role` | No | Sets `ai.cloud.role`. Missing or blank becomes `unknown_service`. |

Values must be strings. The endpoint must be an absolute `https` URI without credentials, query
string, or fragment.

**An item with missing or invalid routing attributes is dropped.** It is never sent to the
application's own component, even when one is configured.

> **Trust requirement.** Destinations must come from a server-owned store, chosen after you have
> authenticated and authorized the customer. Routing values are validated for shape only; nothing
> verifies that the caller is entitled to that destination, so an attacker-supplied `https` endpoint
> would receive your telemetry. Never derive these values from a request header, propagated baggage,
> or a caller-supplied payload.

### Destinations and connection strings

The routing attributes need an instrumentation key and an **explicit** ingestion endpoint. A
connection string does not always contain one: the Application Insights format also allows
`EndpointSuffix` with an optional `Location`, and falls back to a default endpoint when neither is
present. A connection string of just `InstrumentationKey=...` therefore has no endpoint to route to.

Resolve and validate destinations where you load customer configuration, not inside a processor, and
store the two values:

```csharp
public sealed record RoutingDestination(
    string InstrumentationKey,
    string IngestionEndpoint,
    string? CloudRole);
```

If you store connection strings, require the explicit `IngestionEndpoint` form and reject other forms
when loading configuration:

```text
InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westus2-1.in.applicationinsights.azure.com/
```

## 3. Provide the customer lookup

Routing depends on one application-owned lookup, shown here as an interface with a method per signal.
Each method receives the item being processed and resolves the customer from it.

```csharp
using System.Diagnostics;
using OpenTelemetry.Logs;

public interface ICustomerRouting
{
    RoutingDestination? ForActivity(Activity activity);

    RoutingDestination? ForLogRecord(LogRecord record);
}
```

Both methods return the destination for the item, or `null`. Metrics differ: the destination is
resolved before the measurement is recorded, as described in
[Record metrics with routing dimensions](#6-record-metrics-with-routing-dimensions).

Two rules:

- **Never substitute another customer's destination.** Return `null` when the customer cannot be
  determined or is not authorized. Returning `null` drops the item, which is the safe outcome.
- **Processor callbacks are synchronous and concurrent.** Do not block on remote lookups, make the
  lookup thread-safe, and never read a shared mutable "current customer" field.

### Carry the customer on the telemetry

A processor cannot discover the customer by itself, and **a child activity does not inherit its
parent's tags**. Stamp an opaque customer identifier on the current activity once, after
authentication, and have the resolver walk up to find it. Stamp an identifier your store can resolve,
never the connection string or the routing values themselves.

```csharp
app.UseAuthentication();

app.Use(async (context, next) =>
{
    // Your own authorization; returns null when the caller maps to no authorized customer.
    var customerId = customers.Authorize(context.User);

    if (customerId is not null)
    {
        Activity.Current?.SetTag("contoso.customer_id", customerId);
    }

    await next(context);
});
```

A minimal resolver over a fixed set of customers. Replace the dictionary with your own store:

```csharp
using System.Diagnostics;
using OpenTelemetry.Logs;

public sealed class CustomerRouting(IReadOnlyDictionary<string, RoutingDestination> destinations)
    : ICustomerRouting
{
    public RoutingDestination? ForActivity(Activity activity)
    {
        // Child activities (HttpClient, SQL, Azure SDK) do not carry the tag, so walk up.
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (current.GetTagItem("contoso.customer_id") is string customerId)
            {
                return Lookup(customerId);
            }
        }

        return null;
    }

    public RoutingDestination? ForLogRecord(LogRecord record) =>
        // Log records are emitted inline, so the ambient activity is still current here.
        ForActivity(Activity.Current!);

    private RoutingDestination? Lookup(string customerId) =>
        destinations.TryGetValue(customerId, out var destination) ? destination : null;
}
```

`Activity.Parent` is `null` for an activity started outside the request's execution context, such as
queued work. Carry the customer identifier in the work item and stamp it on the activity you start
for that work.

## 4. Attach the attributes in a processor

One processor per signal covers telemetry from your own code and from the instrumentation libraries
the distro enables, including the GenAI, Agent Framework, Semantic Kernel, and Agent365 sources.

> **Ordering requirement.** Your routing processor must run before Azure Monitor exports the item.
> The distro attaches its export processors to the provider after it is built, so processors
> registered through `WithTracing` and `WithLogging` - as in
> [Enable the switch and register the pipeline](#5-enable-the-switch-and-register-the-pipeline) - run
> first. A processor added to an already-built `TracerProvider` or `LoggerProvider` runs too late, and
> for logs the batch processor has already copied the attributes.

Both processors own the three routing attribute names: they replace existing values and remove them
when the resolver returns `null`, so a stale or caller-supplied value cannot select a destination.

### Traces

```csharp
using System.Diagnostics;
using OpenTelemetry;

public sealed class RoutingActivityProcessor(ICustomerRouting routing) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        var destination = routing.ForActivity(activity);

        activity.SetTag("microsoft.instrumentation_key", destination?.InstrumentationKey);
        activity.SetTag("microsoft.ingestion_endpoint", destination?.IngestionEndpoint);
        activity.SetTag("microsoft.multi_endpoint_cloud_role", destination?.CloudRole);
    }
}
```

`SetTag` replaces the existing value for a key, and a `null` value removes it - but it clears only one
entry per key. If any of your code uses `AddTag`, which permits duplicates, remove the routing keys
before setting them.

### Logs

Routing reads `LogRecord.Attributes` and takes the **first** occurrence of each key, so appending
would lose to a pre-existing value. Rebuild the list instead:

```csharp
using System.Linq;
using OpenTelemetry;
using OpenTelemetry.Logs;

public sealed class RoutingLogProcessor(ICustomerRouting routing) : BaseProcessor<LogRecord>
{
    private static readonly string[] RoutingKeys =
    [
        "microsoft.instrumentation_key",
        "microsoft.ingestion_endpoint",
        "microsoft.multi_endpoint_cloud_role",
    ];

    public override void OnEnd(LogRecord record)
    {
        var attributes = (record.Attributes ?? [])
            .Where(attribute => !RoutingKeys.Contains(attribute.Key))
            .ToList();

        var destination = routing.ForLogRecord(record);

        if (destination is not null)
        {
            attributes.Add(new("microsoft.instrumentation_key", destination.InstrumentationKey));
            attributes.Add(new("microsoft.ingestion_endpoint", destination.IngestionEndpoint));

            if (destination.CloudRole is not null)
            {
                attributes.Add(new("microsoft.multi_endpoint_cloud_role", destination.CloudRole));
            }
        }

        // With no destination the record keeps no routing attributes and is dropped.
        record.Attributes = attributes;
    }
}
```

Logging scopes are not consulted, so routing values placed only in a scope have no effect. Logs
emitted outside a request - at startup, or from a background service serving no single customer -
resolve to `null` and are dropped.

## 5. Enable the switch and register the pipeline

Enable routing in the executable's project file. The switch is read once, the first time it is
needed, so code that touches the exporter before it is set locks in `false` for the process. The
project-file form is applied before any of your code runs:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Azure.Monitor.OpenTelemetry.EnableMultiEndpointRouting"
                                  Value="true" />
</ItemGroup>
```

`AppContext.SetSwitch("Azure.Monitor.OpenTelemetry.EnableMultiEndpointRouting", true)` works too, but
must run before any OpenTelemetry registration. When routing never activates, the
`MultiEndpointRoutingEnabled` event described in [Troubleshooting](#9-troubleshooting) does not
appear.

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ICustomerRouting>(new CustomerRouting(destinations));

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        // A routing-only application has no connection string, so nothing auto-selects Azure Monitor.
        o.Exporters = ExportTarget.AzureMonitor;

        // Routing disables these anyway; turning them off avoids collecting discarded telemetry.
        o.AzureMonitor.EnableLiveMetrics = false;
        o.AzureMonitor.EnableStandardMetrics = false;
        o.AzureMonitor.EnablePerfCounters = false;

        // TracesPerSecond must be null for SamplingRatio to take effect.
        o.AzureMonitor.TracesPerSecond = null;
        o.AzureMonitor.SamplingRatio = 0.1F;
    })
    .WithTracing(tracing => tracing
        .AddSource("Contoso.Application")
        .AddProcessor(sp => new RoutingActivityProcessor(sp.GetRequiredService<ICustomerRouting>())))
    .WithLogging(logging => logging
        .AddProcessor(sp => new RoutingLogProcessor(sp.GetRequiredService<ICustomerRouting>())))
    .WithMetrics(metrics => metrics
        .AddMeter("Contoso.Application"));

var app = builder.Build();
```

The `AddProcessor(sp => ...)` overload resolves dependencies from the container, so your resolver can
use any registered service. Leave `AzureMonitor.ConnectionString` unset if the application has no
component of its own, and call `UseMicrosoftOpenTelemetry` only once per service collection - a
second call throws. For the full list of settings routing changes, see
[What changes while routing is enabled](#what-changes-while-routing-is-enabled).

Do not add Console, OTLP, or Agent365 alongside Azure Monitor without reading
[Additional export targets](#additional-export-targets) first: routing does not govern them, and they
receive every customer's telemetry.

## 6. Record metrics with routing dimensions

A metric's dimensions are part of its aggregation key, so the destination must be supplied **when the
measurement is recorded**. A trace or log processor does not enrich metrics, and the values cannot be
recovered once measurements for different customers have been aggregated together.

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

public static class CustomerMetrics
{
    private static readonly Meter Meter = new("Contoso.Application");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("contoso.requests");

    public static void RecordRequest(RoutingDestination? destination)
    {
        if (destination is null)
        {
            // Do not record this measurement under another customer's route.
            return;
        }

        var tags = new TagList
        {
            { "microsoft.instrumentation_key", destination.InstrumentationKey },
            { "microsoft.ingestion_endpoint", destination.IngestionEndpoint },
            { "microsoft.multi_endpoint_cloud_role", destination.CloudRole },
        };

        Requests.Add(1, tags);
    }
}
```

### Metrics from instrumentation libraries

On .NET 8 and later, the HTTP request-duration instruments accept per-request dimensions.
`IHttpMetricsTagsFeature` covers `http.server.request.duration`. The feature is absent when nothing is
listening, so check for `null`:

```csharp
using Microsoft.AspNetCore.Http.Features;

app.Use(async (context, next) =>
{
    var destination = routing.ForActivity(Activity.Current!);
    var tags = context.Features.Get<IHttpMetricsTagsFeature>();

    if (destination is not null && tags is not null)
    {
        tags.Tags.Add(new("microsoft.instrumentation_key", destination.InstrumentationKey));
        tags.Tags.Add(new("microsoft.ingestion_endpoint", destination.IngestionEndpoint));
    }

    await next(context);
});
```

`HttpMetricsEnrichmentContext` covers `http.client.request.duration`. Add the callback from a
`DelegatingHandler` that takes `ICustomerRouting` as a constructor dependency:

```csharp
using System.Net.Http.Metrics;

protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
{
    var destination = routing.ForActivity(Activity.Current!);

    if (destination is not null)
    {
        HttpMetricsEnrichmentContext.AddCallback(request, ctx =>
        {
            ctx.AddCustomTag("microsoft.instrumentation_key", destination.InstrumentationKey);
            ctx.AddCustomTag("microsoft.ingestion_endpoint", destination.IngestionEndpoint);
        });
    }

    return base.SendAsync(request, cancellationToken);
}
```

These hooks cover only those two instruments. Every other instrument the distro collects - active
request counts, connection metrics, runtime counters - carries no destination and is dropped, raising
one `RoutedInstrumentDropped` event per instrument. Rather than naming them individually, drop
everything you have not explicitly enriched:

```csharp
metrics.AddView(instrument =>
    RoutableInstruments.Contains(instrument.Name) ? null : MetricStreamConfiguration.Drop);
```

### Metric views and cardinality

A view that restricts `TagKeys` must keep the routing dimensions. Views can drop dimensions but cannot
add them back.

Routing dimensions also multiply the number of time series for each metric by your customer count.
The default limit is 2000 series per metric. Once it is reached, **new** attribute combinations are
folded into a single overflow point tagged `otel.metric.overflow=true`, which carries no destination
and is dropped. Customers share this limit, so one high-cardinality customer can consume it and cost
every other customer their new series. Size it deliberately with
`MetricStreamConfiguration.CardinalityLimit` - roughly customers x combinations per instrument - and
keep other dimensions bounded.

## 7. Collect only what you route

The distro enables ASP.NET Core, HttpClient, SQL Client, Azure SDK, and the GenAI and agent
instrumentation by default. Under routing, anything your processors and measurement calls do not
enrich is collected and then dropped: wasted work, and noise in the diagnostics -
`RoutedTelemetryRejected` per dropped span, `RoutedInstrumentDropped` per dropped instrument.

Turn off what you do not need:

```csharp
.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;

    o.Instrumentation.EnableSqlClientInstrumentation = false;
    o.Instrumentation.EnableAzureSdkInstrumentation = false;
    o.Instrumentation.EnableOpenAIInstrumentation = false;
    o.Instrumentation.EnableSemanticKernelInstrumentation = false;
    o.Instrumentation.EnableAgentFrameworkInstrumentation = false;
    o.Instrumentation.EnableAgent365Instrumentation = false;

    // Worth keeping: these spans reach the customer tag through Activity.Parent, and their
    // duration instruments are the two that section 6 can enrich.
    o.Instrumentation.EnableAspNetCoreInstrumentation = true;
    o.Instrumentation.EnableHttpClientInstrumentation = true;
})
```

### What the signal switches do

| Option | Effect |
|---|---|
| `EnableTracing = false` | The distro registers no activity sources or trace instrumentation. Sources you add yourself with `AddSource` still export. |
| `EnableMetrics = false` | The distro registers no meters. Meters you add yourself with `AddMeter` still export. |
| `EnableLogging = false` | Suppresses every log record from reaching any OpenTelemetry exporter. Other logging providers are unaffected. |

Only `EnableLogging` stops telemetry outright. Selecting `ExportTarget.AzureMonitor` turns all three
signal pipelines on, so the providers and the Azure Monitor readers exist whatever the other two flags
say: they control what the distro subscribes to, not whether telemetry can be exported.

### What routing never reaches

Live Metrics, standard metrics, performance counters, and SDK statistics each run on their own
`MeterProvider` inside the exporter. Routing does not apply to them, the view in section 6 does not
touch them, and they are never sent to a customer's component. Routing already disables the first
three; see [What changes while routing is enabled](#what-changes-while-routing-is-enabled).

### How the pipeline is assembled

`UseMicrosoftOpenTelemetry` registers instrumentation, resource detectors, and the Azure Monitor
exporter. The exporter's trace and log processors are not added during that call. They are added by a
callback that runs while the `MeterProvider` is configured, which resolves the `TracerProvider` and
`LoggerProvider` from the container - building them then if they have not been built already - and
appends one processor to each.

Appending to a built provider puts the export processors at the end of the chain, which is why
processors registered through `WithTracing` and `WithLogging` run first, and why a processor you add
to a provider you have already built yourself would run too late. This is the guarantee the routing
processors in [Attach the attributes in a processor](#4-attach-the-attributes-in-a-processor) depend
on.

### Non-hosted applications

Hosted applications use `IServiceCollection.AddOpenTelemetry()`, as in
[Enable the switch and register the pipeline](#5-enable-the-switch-and-register-the-pipeline). Console
applications and background tools without the generic host use `OpenTelemetrySdk.Create`, which takes
the same `UseMicrosoftOpenTelemetry` call and owns the provider lifetime itself:

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var routing = new CustomerRouting(destinations);

using var sdk = OpenTelemetrySdk.Create(otel => otel
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
        o.AzureMonitor.EnableLiveMetrics = false;
        o.AzureMonitor.EnableStandardMetrics = false;
        o.AzureMonitor.EnablePerfCounters = false;
        o.AzureMonitor.TracesPerSecond = null;
        o.AzureMonitor.SamplingRatio = 0.1F;
    })
    .WithTracing(tracing => tracing
        .AddSource("Contoso.Application")
        .AddProcessor(new RoutingActivityProcessor(routing)))
    .WithLogging(logging => logging
        .AddProcessor(new RoutingLogProcessor(routing)))
    .WithMetrics(metrics => metrics
        .AddMeter("Contoso.Application")));

var logger = sdk.GetLoggerFactory().CreateLogger("Contoso.Application");

// Application work. Disposal at the end of scope flushes and shuts down every provider.
```

Take loggers from `sdk.GetLoggerFactory()`: a logger factory you create separately is not connected to
this pipeline, so its records never reach the routing processor. Set the routing switch before
`OpenTelemetrySdk.Create` runs, and keep the SDK alive for the life of the process - disposing early
stops collection and loses telemetry.

## 8. Verify isolation with two customers

Use two authorized test customers with different components, and mark **all three signals**: a marker
on an activity does not reach logs or metrics. Keep the marker key identical everywhere, since the
query matches on it.

```csharp
// Trace
activity?.SetTag("routing_test_marker", marker);

// Log - an attribute, not a scope
logger.LogInformation("Verification {routing_test_marker}", marker);

// Metric - a dimension alongside the routing dimensions
tags.Add("routing_test_marker", marker);
Requests.Add(1, tags);
```

Use markers in verification environments only. A per-request unique value would exhaust the
cardinality limit described above.

Query each component after ingestion:

```kusto
union withsource=Signal requests, dependencies, traces, customMetrics
| where timestamp > ago(30m)
| extend marker = tostring(customDimensions["routing_test_marker"])
| summarize count() by Signal, marker
```

| Component | Must contain | Must not contain |
|---|---|---|
| Customer A | `customer-a-test`, for all three signals | `customer-b-test` |
| Customer B | `customer-b-test`, for all three signals | `customer-a-test` |

Counts alone do not establish isolation, and a successful export does not prove an item is queryable
yet.

Then repeat with concurrent requests for both customers to exercise isolation under load, and
exercise unresolved context: confirm the processors remove existing routing attributes, that nothing
falls back to the application's own component, and that the metric call skips recording.

## 9. Troubleshooting

Routing decisions are reported through the Azure Monitor event source, not through application logs.
Because its name begins with `OpenTelemetry-`, OpenTelemetry self-diagnostics captures it: place an
`OTEL_DIAGNOSTICS.json` file beside the application and read the log file it writes.

```json
{
  "LogDirectory": ".",
  "FileSize": 32768,
  "LogLevel": "Verbose"
}
```

For ETW tooling instead, install `dotnet-trace` with `dotnet tool install -g dotnet-trace`:

```bash
dotnet-trace collect --process-id <PID> --providers OpenTelemetry-AzureMonitor-Exporter::Verbose
```

| Symptom | Check | Diagnostic evidence |
|---|---|---|
| Nothing reaches Azure Monitor | `Exporters` set; switch applied before any OpenTelemetry registration | `MultiEndpointRoutingEnabled` (Informational) confirms routing is active |
| Startup throws | `AzureMonitor.Credential` is set | `NotSupportedException` at startup |
| Traces missing | Resolver returned a destination; child spans can reach the customer tag; sampling | `RoutedTelemetryRejected` (Verbose) names the reason; `RoutedExportSummary` (Informational) counts routed and dropped items |
| Metrics missing | Dimensions supplied at measurement time; meter registered; views keep the dimensions; cardinality overflow | `RoutedMetricRejected` (Verbose), `RoutedInstrumentDropped` (Informational) |
| Logs missing | Processor registered; attributes rather than scopes; resolver returned a destination | **Dropped log records emit no per-record event.** Compare expected counts against `RoutedGroupOutcome`, and assert on `LogRecord.Attributes` in a unit test |
| Everything unrouted disappears | Expected when the application has no connection string | `RoutingWithoutConnectionString`, `DroppedUnroutedTelemetryWithoutConnectionString` (both Warning) |
| Sampling behaves unexpectedly | `TracesPerSecond` is ignored under routing | `RateLimitedSamplingIgnoredForMultiEndpointRouting` (Warning) reports the ratio in use |
| Live Metrics or standard metrics absent | Disabled automatically | `LiveMetricsDisabledForMultiEndpointRouting`, `StandardMetricsDisabledForMultiEndpointRouting` (Warning) |
| Buffered telemetry lost | Partition cap or shared budget | `MultiEndpointPartitionCapReached`, `RoutedTelemetryEvicted`, `FailedToPersistRoutedTelemetry` |

`RoutedGroupOutcome` reports item count and status per **ingestion endpoint**, not per customer, so
customers sharing an endpoint are indistinguishable there. Use it as transport evidence, and the
marker queries in [Verify isolation with two customers](#8-verify-isolation-with-two-customers) to
confirm delivery.

## 10. Reference

### What changes while routing is enabled

| Setting | Default | Under routing |
|---|---|---|
| `AzureMonitor.Credential` | unset | **Must stay unset.** Throws `NotSupportedException` at startup |
| `Exporters` | auto-detected | **Must be set explicitly** when there is no connection string |
| `AzureMonitor.ConnectionString` | from environment | Optional, and never a fallback for items without a valid route |
| `AzureMonitor.TracesPerSecond` | `5.0` | Ignored. Rate limiting is per process, so one destination would consume another's allowance |
| `AzureMonitor.SamplingRatio` | `1.0` | Becomes the effective sampler, once `TracesPerSecond` is `null` |
| `AzureMonitor.EnableLiveMetrics` | `true` | Disabled. Live Metrics cannot serve routed destinations |
| `AzureMonitor.EnableStandardMetrics` | `true` | Not collected |
| `AzureMonitor.EnablePerfCounters` | `true` | Not collected. A process-wide counter has no single destination |

Sampling also affects logs when `EnableTraceBasedLogsSampler` is on, so review those together.

### Buffering and offline storage

Offline storage is shared, not isolated by customer. It supports up to 64 partitions, keyed by
normalized ingestion endpoint, within a shared 100 MiB budget. Customers whose components use the
same endpoint share a partition, so this is not a 64-customer limit.

Telemetry for endpoints beyond the partition limit still transmits but has no offline fallback. When
the shared budget is full, the oldest stored telemetry is evicted, which may belong to another
endpoint.

### Additional export targets

The routing attributes govern Azure Monitor only. Console, OTLP, and Agent365 send to their own
configured destination, so enabling one alongside routing sends **every customer's telemetry** to that
single destination. Apply your own filtering policy before enabling them. The attributes also remain
on the underlying `Activity` and `LogRecord` after Azure Monitor reads them, so they may appear in
that target's output; do not strip them in a processor that runs before Azure Monitor.

### Cloud role and resources

Cloud role can vary by item, but the provider resource is shared across all customers and does not
become a separate resource per customer.

## 11. Use the Azure Monitor exporter without the distro

Routing is a feature of the Azure Monitor exporter, so it also works without
`UseMicrosoftOpenTelemetry`. Register the exporter per signal with
`AddAzureMonitorTraceExporter`, `AddAzureMonitorLogExporter`, and `AddAzureMonitorMetricExporter`, and
keep the same `ICustomerRouting`, processors, and measurement dimensions described above.

You give up what the distro adds: its instrumentation libraries, the Azure resource detectors, the
`telemetry.distro.*` resource attributes, and Live Metrics.

Four rules apply to this path:

- **Add your routing processor before the exporter** on each builder. The per-signal methods add their
  export processor where they are called, so a processor registered afterwards would run too late.
- **Set `EnableLiveMetrics = false`.** Live Metrics is only available through
  `UseAzureMonitorExporter`, and leaving it on makes each per-signal method log a "not supported"
  warning.
- **Sampling comes from the exporter options.** `AddAzureMonitorTraceExporter` applies a sampler built
  from `SamplingRatio` and `TracesPerSecond`. Routing ignores `TracesPerSecond` exactly as it does
  under the distro, so set it to `null` and choose a `SamplingRatio`.
- **Do not mix the two styles.** Combining `UseAzureMonitorExporter` with the per-signal methods
  throws.

Share one options callback across the three signals:

```csharp
using Azure.Monitor.OpenTelemetry.Exporter;

static void ConfigureExporter(AzureMonitorExporterOptions o)
{
    o.EnableLiveMetrics = false;
    o.TracesPerSecond = null;
    o.SamplingRatio = 1.0F;
}
```

### Hosted: `IServiceCollection.AddOpenTelemetry`

```csharp
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddSingleton<ICustomerRouting>(new CustomerRouting(destinations));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Contoso.Application")
        .AddProcessor(sp => new RoutingActivityProcessor(sp.GetRequiredService<ICustomerRouting>()))
        .AddAzureMonitorTraceExporter(ConfigureExporter))
    .WithLogging(logging => logging
        .AddProcessor(sp => new RoutingLogProcessor(sp.GetRequiredService<ICustomerRouting>()))
        .AddAzureMonitorLogExporter(ConfigureExporter))
    .WithMetrics(metrics => metrics
        .AddMeter("Contoso.Application")
        .AddAzureMonitorMetricExporter(ConfigureExporter));
```

### Non-hosted: `OpenTelemetrySdk.Create`

Use the per-signal methods here rather than `UseAzureMonitorExporter`, which attaches its trace and
log exporters through a hosted service that never starts without the generic host.

```csharp
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var routing = new CustomerRouting(destinations);

using var sdk = OpenTelemetrySdk.Create(otel => otel
    .WithTracing(tracing => tracing
        .AddSource("Contoso.Application")
        .AddProcessor(new RoutingActivityProcessor(routing))
        .AddAzureMonitorTraceExporter(ConfigureExporter))
    .WithLogging(logging => logging
        .AddProcessor(new RoutingLogProcessor(routing))
        .AddAzureMonitorLogExporter(ConfigureExporter))
    .WithMetrics(metrics => metrics
        .AddMeter("Contoso.Application")
        .AddAzureMonitorMetricExporter(ConfigureExporter)));

var logger = sdk.GetLoggerFactory().CreateLogger("Contoso.Application");
```

Set the routing switch before either form runs. Everything else - the routing attributes, the
drop-on-unresolved behaviour, storage, and the diagnostics in
[Troubleshooting](#9-troubleshooting) - is unchanged, because it all lives in the exporter.

## Sample

[Microsoft.OpenTelemetry.MultiEndpoint.Console.Demo](../examples/Microsoft.OpenTelemetry.MultiEndpoint.Console.Demo)
routes traces, logs, and metrics to two Application Insights components. It attaches the attributes at
the call site rather than in a processor, because it emits all of its own telemetry.
