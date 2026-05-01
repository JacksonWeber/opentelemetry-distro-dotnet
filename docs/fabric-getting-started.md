# Send OpenTelemetry data to Microsoft Fabric

This guide walks you through sending traces, metrics, and logs from a .NET application to [Microsoft Fabric Real-Time Intelligence](https://learn.microsoft.com/en-us/fabric/real-time-intelligence/overview) (or Azure Data Explorer).

## How it works

Your .NET app doesn't connect to Fabric directly. Instead, it sends telemetry to an **OpenTelemetry Collector** running alongside it — either locally or in a cluster. The collector then forwards the data into your Fabric/ADX database.

```
┌──────────────┐     OTLP/gRPC     ┌──────────────────┐     Kusto Ingest     ┌─────────────────────────┐
│  .NET App    │ ───────────────►  │  OTel Collector  │ ──────────────────►  │  Fabric / Azure Data    │
│  (distro)    │    :4317          │  (ADX exporter)  │                      │  Explorer               │
└──────────────┘                   └──────────────────┘                      └─────────────────────────┘
```

**What each component does:**

- **Your .NET app** — uses `Microsoft.OpenTelemetry` to automatically capture HTTP requests, logs, and metrics, then exports them via OTLP (a standard telemetry protocol) on port 4317.
- **OTel Collector** — a lightweight process that receives OTLP data and forwards it to one or more destinations. We use the [Azure Data Explorer exporter](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/exporter/azuredataexplorerexporter) plugin to write into Kusto tables.
- **Fabric / ADX** — stores the telemetry in three KQL tables (traces, metrics, logs) that you can query with KQL.

## Prerequisites

- [.NET 8.0+ SDK](https://dotnet.microsoft.com/download)
- An Azure Data Explorer cluster **or** a [Fabric KQL database](https://learn.microsoft.com/en-us/fabric/real-time-analytics/create-database) — a [free ADX cluster](https://dataexplorer.azure.com/freecluster) works for testing
- Azure CLI installed and logged in (`az login`) — used for authentication
- The `Microsoft.OpenTelemetry` NuGet package (installed in Step 3)

> **Tip:** Before connecting to Fabric, you can verify your app emits telemetry correctly using the [Aspire Dashboard](aspire-dashboard-getting-started.md) — a free, local viewer for traces, metrics, and logs. Once validated, stop the dashboard (`docker stop aspire-dashboard`) and continue with the steps below.

## Step 1: Create target tables

The collector writes telemetry into three pre-created tables. Open the [Azure Data Explorer web UI](https://dataexplorer.azure.com/) (or [Fabric KQL queryset](https://learn.microsoft.com/en-us/fabric/real-time-analytics/kusto-query-set)), select your database, and run each command below:

```kql
// Table for log records
.create-merge table OTELLogs (Timestamp:datetime, ObservedTimestamp:datetime, TraceID:string, SpanID:string, SeverityText:string, SeverityNumber:int, Body:string, ResourceAttributes:dynamic, LogsAttributes:dynamic)

// Table for metric data points
.create-merge table OTELMetrics (Timestamp:datetime, MetricName:string, MetricType:string, MetricUnit:string, MetricDescription:string, MetricValue:real, Host:string, ResourceAttributes:dynamic, MetricAttributes:dynamic)

// Table for distributed traces (spans)
.create-merge table OTELTraces (TraceID:string, SpanID:string, ParentID:string, SpanName:string, SpanStatus:string, SpanKind:string, StartTime:datetime, EndTime:datetime, ResourceAttributes:dynamic, TraceAttributes:dynamic, Events:dynamic, Links:dynamic)
```

> **Tip:** `.create-merge` is safe to run multiple times — it creates the table if it doesn't exist, or merges new columns into an existing table.

## Step 2: Grant permissions

The collector needs permission to write data into your database. Run one of these commands in the same query window:

**For local development** (uses your Azure CLI identity):

```kql
.add database <yourdbname> ingestors ('aaduser=you@yourdomain.com') 'Dev testing'
```

**For production** (uses a service principal):

```kql
.add database <yourdbname> ingestors ('aadapp=<ApplicationID>') 'OTel Collector'
```

> Replace `<yourdbname>` with your actual database name, and `<ApplicationID>` with the client ID from your [Entra app registration](https://learn.microsoft.com/en-us/azure/data-explorer/provision-entra-id-app?tabs=portal).

## Step 3: Create a .NET app with the distro

Create a new ASP.NET Core app and install the distro:

```bash
dotnet new web -n FabricDemo
cd FabricDemo
dotnet add package Microsoft.OpenTelemetry
```

Replace `Program.cs` with:

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Otlp;
});

var app = builder.Build();
app.MapGet("/", () => "Hello from distro → Fabric!");
app.Run();
```

That's it for the app. `UseMicrosoftOpenTelemetry` automatically instruments HTTP requests, captures logs, and collects metrics. Setting `ExportTarget.Otlp` sends everything to the collector via the OTLP/gRPC protocol.

By default, the OTLP exporter sends to `http://localhost:4317` (gRPC). To override the endpoint, set the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable:

```bash
# Remote collector or TLS
OTEL_EXPORTER_OTLP_ENDPOINT=https://<collector-host>:4317
```

> **gRPC vs HTTP:** The distro uses OTLP/gRPC (port 4317) by default. To use OTLP/HTTP (port 4318) instead, also set `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`.

## Step 4: Download the OpenTelemetry Collector

The collector is a standalone process that receives OTLP data from your app and forwards it to Fabric/ADX. You need the **contrib** distribution (which includes the Azure Data Explorer exporter).

> **Important:** If you're running the [Aspire Dashboard](aspire-dashboard-getting-started.md), stop it first — both use port 4317:
> ```bash
> docker stop aspire-dashboard
> ```

### Option A: Binary (recommended for getting started)

Download **otelcol-contrib** version **0.121.0** (or later) for your OS from [OTel Collector Contrib releases](https://github.com/open-telemetry/opentelemetry-collector-releases/releases) (look for `otelcol-contrib_*` assets).

### Option B: Docker

```bash
docker pull otel/opentelemetry-collector-contrib:0.121.0
```

### Option C: Kubernetes

For production deployments, see the [Azure Data Explorer exporter docs](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/exporter/azuredataexplorerexporter) for Kubernetes examples using Workload Identity.

## Step 5: Configure the collector

Create a file called `collector-config.yaml`:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

processors:
  batch:
    send_batch_size: 512
    timeout: 5s

exporters:
  azuredataexplorer:
    cluster_uri: "https://<your-cluster>.kusto.windows.net"
    # Authentication — pick one:
    #   Option 1: DefaultAzureCredential (Azure CLI, Managed Identity, Workload Identity)
    use_azure_auth: true
    #   Option 2: Service principal with client secret
    # application_id: "<client-id>"
    # application_key: "<client-secret>"
    # tenant_id: "<tenant-id>"

    db_name: "<yourdbname>"
    metrics_table_name: "OTELMetrics"
    logs_table_name: "OTELLogs"
    traces_table_name: "OTELTraces"
    ingestion_type: "queued"   # or "managed" for streaming

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [azuredataexplorer]
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [azuredataexplorer]
    logs:
      receivers: [otlp]
      processors: [batch]
      exporters: [azuredataexplorer]
```

**Update these placeholders:**

| Placeholder | Replace with | Example |
|---|---|---|
| `<your-cluster>` | Your cluster hostname (without `https://`) | `mycluster.westus2.kusto.windows.net` |
| `<yourdbname>` | The database name where you created the tables | `oteldb` |

### Authentication options

| Method | Config | When to use |
|---|---|---|
| **DefaultAzureCredential** | `use_azure_auth: true` | Local dev (`az login`), Managed Identity, Workload Identity (AKS) |
| **Service principal** | `application_id` + `application_key` + `tenant_id` | CI/CD, headless environments |

> **Note:** When using `use_azure_auth: true`, the collector authenticates via the [DefaultAzureCredential](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential) chain. For local development, `az login` is the simplest option. In production (AKS), use [Workload Identity federation](https://learn.microsoft.com/en-us/azure/aks/workload-identity-overview) with a federated credential on your Entra app registration — no client secrets needed.

## Step 6: Start the collector

Make sure you're logged into Azure CLI (for `use_azure_auth: true`):

```bash
az login
```

Then start the collector:

```bash
# Binary (Windows)
otelcol-contrib.exe --config collector-config.yaml

# Binary (Linux / macOS)
./otelcol-contrib --config collector-config.yaml

# Docker
docker run --rm -p 4317:4317 -p 4318:4318 \
  -v $(pwd)/collector-config.yaml:/etc/otelcol-contrib/config.yaml \
  otel/opentelemetry-collector-contrib:0.121.0
```

> **Docker + `use_azure_auth`:** Azure CLI credentials aren't available inside the Docker container. Use `application_id` + `application_key` + `tenant_id` in the config instead, or run the binary directly.

You should see `Everything is ready. Begin running and processing data.` — the collector is now listening on port 4317.

## Step 7: Run the app and verify

With the collector running in one terminal, open a second terminal and start the app:

```bash
dotnet run
```

Send a few requests to generate telemetry (use the URL printed by `dotnet run`):

```bash
curl http://localhost:<port>/
curl http://localhost:<port>/weather
```

After the collector batch interval (5s default), query your tables in the ADX web UI:

```kql
// Check for traces (each HTTP request creates a span)
OTELTraces | take 10

// Check for logs (ASP.NET Core request logging)
OTELLogs | take 10

// Check for metrics (request counts, durations)
OTELMetrics | take 10
```

> **Don't see data?** Check the collector terminal for errors. Common issues: wrong `cluster_uri`, missing permissions (re-run Step 2), or `az login` session expired.

## Combining with Azure Monitor

You can send to both Azure Monitor **and** Fabric simultaneously. The app exports via OTLP (collector → Fabric) and directly to Azure Monitor — no extra collector needed for Azure Monitor:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Otlp;
    o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
});
```

## References

- [Ingest data from OpenTelemetry to Azure Data Explorer](https://learn.microsoft.com/en-us/azure/data-explorer/open-telemetry-connector?tabs=command-line) — Full ADX + OTel Collector setup guide
- [Create a Microsoft Entra app registration](https://learn.microsoft.com/en-us/azure/data-explorer/provision-entra-id-app?tabs=portal) — Service principal setup for ADX authentication
- [Azure Data Explorer exporter](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/exporter/azuredataexplorerexporter) — Collector plugin source code and configuration reference
- [Fabric Real-Time Intelligence overview](https://learn.microsoft.com/en-us/fabric/real-time-intelligence/overview) — Microsoft Fabric's real-time analytics capability
- [Example: Microsoft.OpenTelemetry.Fabric.Demo](../examples/Microsoft.OpenTelemetry.Fabric.Demo) — Working sample app with collector config
