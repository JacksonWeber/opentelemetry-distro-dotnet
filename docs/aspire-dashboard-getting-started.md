# Local development with the Aspire Dashboard

The [.NET Aspire Dashboard](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/overview) provides a local web UI for viewing traces, metrics, and logs during development — no Azure subscription required. It receives telemetry via OTLP (the same protocol the distro uses) and displays it in a browser.

Use it to validate that your app is emitting the telemetry you expect before configuring a production destination like Azure Monitor or [Microsoft Fabric](fabric-getting-started.md).

## Start the dashboard

Run the Aspire Dashboard as a Docker container:

```powershell
docker run --rm -it -d `
    -p 18888:18888 `
    -p 4317:18889 `
    -p 4318:18890 `
    --name aspire-dashboard `
    mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

This exposes:

| Endpoint | Purpose |
|---|---|
| `http://localhost:18888` | Dashboard web UI |
| `http://localhost:4317` | OTLP gRPC receiver (traces, metrics, logs) |
| `http://localhost:4318` | OTLP HTTP receiver |

Open [http://localhost:18888](http://localhost:18888) in your browser — you'll see the dashboard with empty panels.

## Configure your app

Set `ExportTarget.Otlp` so the distro sends telemetry to `localhost:4317` (the dashboard):

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Otlp;
});

var app = builder.Build();
app.MapGet("/", () => "Hello!");
app.Run();
```

Run the app and send a few requests (use the URL printed by `dotnet run`):

```bash
dotnet run
curl http://localhost:<port>/
```

Switch to the dashboard at `http://localhost:18888` — you should see traces, structured logs, and metrics appearing in real time.

## Stop the dashboard before using the OTel Collector

The Aspire Dashboard and the OpenTelemetry Collector both listen on port **4317**. If you're following the [Fabric getting started guide](fabric-getting-started.md) or running any other OTel Collector setup, stop the dashboard first:

```bash
docker stop aspire-dashboard
```

Then start the collector. If you forget, you'll see a `port is already allocated` error.

## Combining with other exporters

You can send to the dashboard **and** Azure Monitor at the same time:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Otlp | ExportTarget.AzureMonitor;
    o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
});
```

## References

- [.NET Aspire Dashboard overview](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/overview)
- [Aspire Dashboard standalone mode](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone)
- [Fabric Getting Started](fabric-getting-started.md) — Send data to Microsoft Fabric / Azure Data Explorer via the OTel Collector
