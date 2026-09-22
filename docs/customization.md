# Customizing the Microsoft OpenTelemetry Distro (.NET)

> **Advanced guide** — covers resource configuration, additional instrumentation, trace enrichment & filtering, and OTLP exporter tuning beyond the defaults provided by `UseMicrosoftOpenTelemetry()`.

The `Microsoft.OpenTelemetry` distro auto-configures traces, metrics, and logs with sensible defaults. This guide shows how to extend and customize that configuration without losing the built-in behavior.

## Table of contents

- [Configuring the resource](#configuring-the-resource)
- [Adding custom ActivitySources and Meters](#adding-custom-activitysources-and-meters)
- [Adding additional instrumentation](#adding-additional-instrumentation)
- [Enrichment and filtering](#enrichment-and-filtering)
- [OTLP exporter configuration](#otlp-exporter-configuration)
- [Environment variables reference](#environment-variables-reference)

---

## Configuring the resource

The distro auto-detects Azure resource attributes (App Service, VM, Container Apps) and sets the distro name. You can add your own resource attributes — for example, a service name — using the standard OpenTelemetry API.

### Hosted (ASP.NET Core / Worker Services)

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

builder.Services.ConfigureOpenTelemetryTracerProvider((sp, tracerBuilder) =>
    tracerBuilder.ConfigureResource(r => r.AddService("my-service", serviceVersion: "1.0.0")));
```

Or using `AddOpenTelemetry()` chaining:

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
    })
    .ConfigureResource(r => r
        .AddService("my-service", serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = "production",
            ["team.name"] = "platform"
        }));
```

### Non-hosted (Console apps)

```csharp
var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.ConfigureResource(r => r
        .AddService("my-console-tool", serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = "staging"
        }));

    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
    });
});

// Your application logic here.
// Dispose the SDK only at application shutdown.
```

> **Note:** `ConfigureResource()` is additive — your attributes are merged with the distro's auto-detected resource attributes.

### Resource via environment variables

You can also set resource attributes without code changes:

```powershell
$env:OTEL_SERVICE_NAME = "my-service"
$env:OTEL_RESOURCE_ATTRIBUTES = "deployment.environment=production,team.name=platform"
```

---

## Adding custom ActivitySources and Meters

The distro auto-registers activity sources for Agent Framework, Semantic Kernel, OpenAI, and Azure SDK. To capture telemetry from your own custom sources, register them alongside the distro.

### Custom ActivitySource

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

builder.Services.ConfigureOpenTelemetryTracerProvider((sp, tracerBuilder) =>
    tracerBuilder.AddSource("MyCompany.MyProduct.MyLibrary"));
```

Then emit traces from your code:

```csharp
using System.Diagnostics;

private static readonly ActivitySource MySource = new("MyCompany.MyProduct.MyLibrary");

public void DoWork()
{
    using var activity = MySource.StartActivity("DoWork");
    activity?.SetTag("work.item.id", itemId);
    // ... your logic
}
```

### Custom Meter

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

builder.Services.ConfigureOpenTelemetryMeterProvider((sp, meterBuilder) =>
    meterBuilder.AddMeter("MyCompany.MyProduct.MyLibrary"));
```

---

## Adding additional instrumentation

The distro includes instrumentation for ASP.NET Core, HttpClient, SQL Client, and Azure SDK. If you need instrumentation for additional libraries, install the corresponding OpenTelemetry package and register it.

### gRPC client instrumentation

```bash
dotnet add package OpenTelemetry.Instrumentation.GrpcNetClient
```

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

builder.Services.ConfigureOpenTelemetryTracerProvider((sp, tracerBuilder) =>
    tracerBuilder.AddGrpcClientInstrumentation());
```

### Entity Framework Core instrumentation

```bash
dotnet add package OpenTelemetry.Instrumentation.EntityFrameworkCore
```

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

builder.Services.ConfigureOpenTelemetryTracerProvider((sp, tracerBuilder) =>
    tracerBuilder.AddEntityFrameworkCoreInstrumentation());
```

### Disabling built-in instrumentation

To turn off instrumentation the distro registers by default, use `InstrumentationOptions`:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
    o.Instrumentation.EnableSqlClientInstrumentation = false;
    o.Instrumentation.EnableHttpClientInstrumentation = false;
});
```

See the [Azure Monitor Getting Started](azure-monitor-getting-started.md#instrumentation-options) guide for the full list of toggles.

---

## Enrichment and filtering

The distro's built-in instrumentation libraries support enrichment (adding custom tags to spans) and filtering (suppressing unwanted telemetry). Use `builder.Services.Configure<T>()` to customize after calling `UseMicrosoftOpenTelemetry()`.

### ASP.NET Core trace enrichment and filtering

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
{
    options.RecordException = true;

    // Filter: exclude health check endpoints from tracing
    options.Filter = context =>
        !context.Request.Path.StartsWithSegments("/health")
        && !context.Request.Path.StartsWithSegments("/alive");

    // Enrich: add custom tags from the incoming request
    options.EnrichWithHttpRequest = (activity, request) =>
    {
        activity.SetTag("http.request.body.size", request.ContentLength);
        activity.SetTag("user_agent", request.Headers.UserAgent.ToString());
    };

    // Enrich: add custom tags from the response
    options.EnrichWithHttpResponse = (activity, response) =>
    {
        activity.SetTag("http.response.body.size", response.ContentLength);
    };
});
```

### HttpClient trace enrichment and filtering

```csharp
builder.Services.Configure<HttpClientTraceInstrumentationOptions>(options =>
{
    options.RecordException = true;

    // Enrich: add custom tags from outgoing requests
    options.EnrichWithHttpRequestMessage = (activity, request) =>
    {
        activity.SetTag("http.request.method", request.Method.ToString());
        activity.SetTag("http.request.host", request.RequestUri?.Host);
    };

    // Enrich: add custom tags from responses
    options.EnrichWithHttpResponseMessage = (activity, response) =>
    {
        activity.SetTag("http.response.status_code", (int)response.StatusCode);

        var headerList = response.Content?.Headers?
            .Select(h => $"{h.Key}={string.Join(",", h.Value)}")
            .ToArray();

        if (headerList is { Length: > 0 })
        {
            activity.SetTag("http.response.headers", headerList);
        }
    };

    // Filter: suppress telemetry for health check requests
    options.FilterHttpRequestMessage = request =>
        !request.RequestUri?.AbsolutePath.Contains("health", StringComparison.OrdinalIgnoreCase) ?? true;
});
```

### SQL Client customization

While the SQL Client instrumentation is still in beta, it is vendored within the distro. For customization, manually add the package reference:

```bash
dotnet add package --prerelease OpenTelemetry.Instrumentation.SqlClient
```

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
    })
    .WithTracing(tracing =>
    {
        tracing.AddSqlClientInstrumentation(options =>
        {
            options.SetDbStatementForStoredProcedure = false;
        });
    });
```

### HTTP client metrics

When HTTP client instrumentation and metrics are enabled, only `http.client.request.duration` is collected from `System.Net.Http` by default. This retains request latency, count, and failure dimensions while reducing default telemetry volume. `http.client.active_requests`, `http.client.open_connections`, `http.client.connection.duration`, `http.client.request.time_in_queue`, and future instruments from this meter are opt-in. HTTP dependency tracing and server metrics are unchanged.

To collect an additional metric, configure an OpenTelemetry view before the provider is built:

```csharp
using OpenTelemetry.Metrics;

builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
    metrics.AddView(instrument =>
        instrument.Meter.Name == "System.Net.Http"
        && instrument.Name == "http.client.open_connections"
            ? new MetricStreamConfiguration()
            : null));
```

To restore all metrics from this meter, remove the `instrument.Name` condition. This does not enable metrics or instrumentation that you have disabled through `InstrumentationOptions`.

Views are additive: a matching non-drop view collects a metric even when the distro's drop view also matches, regardless of registration order. Existing broad non-drop views can therefore re-enable these metrics. Calling `AddMeter("System.Net.Http")` again does not override the drop view. Views apply to all exporters on the same meter provider, including Azure Monitor and OTLP.

To disable request duration too, use `metrics.AddView("http.client.request.duration", MetricStreamConfiguration.Drop)` without a competing non-drop view. DNS metrics belong to the separate `System.Net.NameResolution` meter. This change does not affect `_APPRESOURCEPREVIEW_` resource-metric emission.

### Dropping specific metrics instruments

To exclude specific instruments from being collected:

```csharp
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
    metrics.AddView(instrumentName: "http.server.active_requests", MetricStreamConfiguration.Drop));
```

Refer to [Drop an instrument](https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/docs/metrics/customizing-the-sdk#drop-an-instrument) for more examples.

---

## OTLP exporter configuration

When using `ExportTarget.Otlp`, the distro registers the OTLP exporter with default settings. You can customize the exporter's endpoint, protocol, headers, and other options using `builder.Services.Configure<OtlpExporterOptions>()` or environment variables.

### Configure via code

```csharp
using OpenTelemetry.Exporter;

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Otlp;
});

builder.Services.Configure<OtlpExporterOptions>(otlp =>
{
    otlp.Endpoint = new Uri("http://localhost:4317");
    otlp.Protocol = OtlpExportProtocol.Grpc;
    otlp.Headers = "x-api-key=my-secret-key";
    otlp.TimeoutMilliseconds = 10000;
});
```

### Configure via `appsettings.json`

```csharp
builder.Services.Configure<OtlpExporterOptions>(
    builder.Configuration.GetSection("OpenTelemetry:Otlp"));
```

```json
{
  "OpenTelemetry": {
    "Otlp": {
      "Endpoint": "http://localhost:4317",
      "Protocol": "grpc",
      "Headers": "x-api-key=my-secret-key",
      "TimeoutMilliseconds": 10000
    }
  }
}
```

### OtlpExporterOptions reference

| Property | Type | Description | Default |
|---|---|---|---|
| `Endpoint` | `Uri` | Target endpoint for the exporter. | `localhost:4317` (gRPC) or `localhost:4318` (HTTP) |
| `Protocol` | `OtlpExportProtocol` | Transport protocol: `Grpc` or `HttpProtobuf`. | `Grpc` |
| `Headers` | `string?` | Optional headers sent with each request (comma-separated `key=value` pairs). | `null` |
| `TimeoutMilliseconds` | `int` | Maximum time to wait for the backend to process a batch. | `10000` |
| `HttpClientFactory` | `Func<HttpClient>?` | Factory for the `HttpClient` used with `HttpProtobuf` protocol. | `null` |

> **Note:** When using `OtlpExportProtocol.HttpProtobuf`, the full URL must include the signal-specific path (e.g., `http://localhost:4318/v1/traces`).

### Per-signal OTLP configuration

The `OtlpExporterOptions` class is shared across all signals. To configure different settings per signal, use named options:

```csharp
builder.Services.Configure<OtlpExporterOptions>("tracing",
    builder.Configuration.GetSection("OpenTelemetry:Tracing:Otlp"));
builder.Services.Configure<OtlpExporterOptions>("metrics",
    builder.Configuration.GetSection("OpenTelemetry:Metrics:Otlp"));
builder.Services.Configure<OtlpExporterOptions>("logging",
    builder.Configuration.GetSection("OpenTelemetry:Logging:Otlp"));
```

### Configuring metric reader options

Control the metrics export interval and temporality preference:

```csharp
builder.Services.Configure<MetricReaderOptions>(o =>
{
    o.TemporalityPreference = MetricReaderTemporalityPreference.Delta;
    o.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
});
```

### Non-hosted (Console apps)

For console apps, configure OTLP using environment variables (see below) or pass options inline:

```csharp
var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Otlp;
    });
});

// Environment variable OTEL_EXPORTER_OTLP_ENDPOINT controls the endpoint.
// Dispose the SDK only at application shutdown.
```

---

## Environment variables reference

The OTLP exporter supports the standard [OpenTelemetry environment variables](https://opentelemetry.io/docs/specs/otel/protocol/exporter/). These are read through `IConfiguration`, so they can also be set in `appsettings.json` or on the command line.

> **Note:** Code-based configuration (`OtlpExporterOptions` setters) takes precedence over environment variables.

### All signals

| Environment variable | `OtlpExporterOptions` property | Description |
|---|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `Endpoint` | Target endpoint URL |
| `OTEL_EXPORTER_OTLP_HEADERS` | `Headers` | Request headers (`key=value` pairs) |
| `OTEL_EXPORTER_OTLP_TIMEOUT` | `TimeoutMilliseconds` | Export timeout in milliseconds |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `Protocol` | `grpc` or `http/protobuf` |

### Signal-specific endpoint overrides

| Environment variable | Signal |
|---|---|
| `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` | Traces |
| `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` | Metrics |
| `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT` | Logs |

Signal-specific headers, timeout, and protocol overrides follow the same pattern (e.g., `OTEL_EXPORTER_OTLP_TRACES_HEADERS`).

### Batch processor (traces)

| Environment variable | Description |
|---|---|
| `OTEL_BSP_SCHEDULE_DELAY` | Delay between batch exports (ms) |
| `OTEL_BSP_EXPORT_TIMEOUT` | Export timeout (ms) |
| `OTEL_BSP_MAX_QUEUE_SIZE` | Maximum queue size |
| `OTEL_BSP_MAX_EXPORT_BATCH_SIZE` | Maximum batch size |

### Batch processor (logs)

| Environment variable | Description |
|---|---|
| `OTEL_BLRP_SCHEDULE_DELAY` | Delay between batch exports (ms) |
| `OTEL_BLRP_EXPORT_TIMEOUT` | Export timeout (ms) |
| `OTEL_BLRP_MAX_QUEUE_SIZE` | Maximum queue size |
| `OTEL_BLRP_MAX_EXPORT_BATCH_SIZE` | Maximum batch size |

### Periodic metric reader

| Environment variable | Description |
|---|---|
| `OTEL_METRIC_EXPORT_INTERVAL` | Export interval (ms) |
| `OTEL_METRIC_EXPORT_TIMEOUT` | Export timeout (ms) |
| `OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE` | Temporality: `cumulative` (default) or `delta` |
| `OTEL_EXPORTER_OTLP_METRICS_DEFAULT_HISTOGRAM_AGGREGATION` | `explicit_bucket_histogram` (default) or `base2_exponential_bucket_histogram` |

### mTLS authentication (.NET 8.0+)

| Environment variable | Description |
|---|---|
| `OTEL_EXPORTER_OTLP_CERTIFICATE` | Path to CA certificate file (PEM) |
| `OTEL_EXPORTER_OTLP_CLIENT_CERTIFICATE` | Path to client certificate file (PEM) |
| `OTEL_EXPORTER_OTLP_CLIENT_KEY` | Path to client private key file (PEM) |

### Resource and sampler

| Environment variable | Description |
|---|---|
| `OTEL_SERVICE_NAME` | Sets the `service.name` resource attribute |
| `OTEL_RESOURCE_ATTRIBUTES` | Comma-separated `key=value` resource attributes |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Azure Monitor connection string (auto-detected) |
| `OTEL_TRACES_SAMPLER` | Trace sampler (`microsoft.rate_limited` or `microsoft.fixed_percentage`) |
| `OTEL_TRACES_SAMPLER_ARG` | Sampler argument (rate or ratio value) |

### Example: sending OTLP to Aspire Dashboard

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"
dotnet run
```

Navigate to [http://localhost:18888](http://localhost:18888) to explore traces, logs, and metrics in the Aspire Dashboard.
