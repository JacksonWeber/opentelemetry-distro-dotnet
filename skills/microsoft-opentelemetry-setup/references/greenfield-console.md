# Console / Non-Hosted App Setup

Use `OpenTelemetrySdk.Create()` for apps without ASP.NET Core hosting.

## Minimal

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;

var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Console | ExportTarget.AzureMonitor;
    });
});

// ... your app logic ...

// ForceFlush before exit (important for short-lived processes)
sdk.TracerProvider?.ForceFlush(timeoutMilliseconds: 10000);
sdk.LoggerProvider?.ForceFlush(timeoutMilliseconds: 10000);
sdk.MeterProvider?.ForceFlush(timeoutMilliseconds: 10000);
sdk.Dispose();
```

## With custom ActivitySource

```csharp
using System.Diagnostics;

var activitySource = new ActivitySource("MyConsoleApp");

var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Console | ExportTarget.Otlp;
    })
    .WithTracing(tracing => tracing.AddSource("MyConsoleApp"))
    .WithMetrics(metrics => metrics.AddMeter("MyConsoleApp"));
});
```

## ILogger

```csharp
using Microsoft.Extensions.Logging;

var logger = sdk.GetLoggerFactory().CreateLogger("MyConsoleApp");
logger.LogInformation("Processing started");
```

## Key differences from hosted

- No `IHostApplicationBuilder` — use `OpenTelemetrySdk.Create()`
- Must call `ForceFlush()` before exit — no hosted service to flush automatically
- Must keep `sdk` alive for the app lifetime
- `Dispose()` on shutdown
