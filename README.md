# Microsoft.OpenTelemetry

A unified OpenTelemetry distribution for .NET. One-line onboarding for ASP.NET Core apps, [Microsoft Agent Framework](https://github.com/microsoft/agent-framework), and [Agent365](https://learn.microsoft.com/en-us/microsoft-agent-365/) — Microsoft's managed observability backend for AI agents.

**Targets:** `net8.0`, `netstandard2.0`

## Install

```bash
dotnet add package Microsoft.OpenTelemetry
```

## Quick Start

### ASP.NET Core / Worker Services (hosted)

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

var app = builder.Build();
app.Run();
```

### Console / non-hosted apps

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;

var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
    });
});

// Your application logic here.
// The SDK must stay alive for the lifetime of the app.
// Disposing the SDK flushes pending telemetry and shuts down all providers.

sdk.Dispose();
```

> **Important:** Do not dispose the SDK until your application is shutting down. Disposing it early stops all telemetry collection and export — you will lose data.

> `UseMicrosoftOpenTelemetry()` works on any `IOpenTelemetryBuilder` — both the host-integrated and `OpenTelemetrySdk.Create()` paths are supported.

## Signals & destinations

| Signal | Azure Monitor | Agent365 | OTLP | Console |
|---|:---:|:---:|:---:|:---:|
| Traces | ✅ | ✅ | ✅ | ✅ |
| Metrics | ✅ | — | ✅ | ✅ |
| Logs | ✅ | — | ✅ | ✅ |

Exporters auto-detect when `Exporters` isn't set:
- **Azure Monitor** — enabled when `ConnectionString` is set (code, env var `APPLICATIONINSIGHTS_CONNECTION_STRING`, or `IConfiguration`).
- **Agent365** — enabled when `TokenResolver` is set (or when the DI token cache is registered by `Microsoft.Agents.A365.Observability.Hosting`).

Set explicitly to override auto-detection: `o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Agent365;`

## Instrumentation (always active)

- ASP.NET Core incoming HTTP requests
- HTTP client outbound calls (with Azure SDK dedup filter)
- SQL client queries
- Azure SDK client calls
- Resource detection (Azure App Service, VM, Container Apps)
- Agent365 scopes (`InvokeAgentScope`, `InferenceScope`, `ExecuteToolScope`, `OutputScope`) and baggage propagation
- Microsoft Agent Framework — `Experimental.Microsoft.Agents.AI` activity sources
- Semantic Kernel — `Microsoft.SemanticKernel*` activity sources
- OpenAI / Azure OpenAI — `Azure.AI.OpenAI*`, `OpenAI.*`, `Experimental.Microsoft.Extensions.AI`
- Azure SDK EventSource → `ILogger` log forwarding
- Metrics — `Microsoft.AspNetCore.Hosting`, `System.Net.Http`

## Onboarding by scenario

Pick the section that matches your workload. All can be combined in the same app.

---

### 1. Azure Monitor

Send traces, metrics, and logs to Application Insights / Azure Monitor.

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
    o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
});
```

**Configuration sources** — the connection string can be set via code, `appsettings.json`, or the `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable.

📖 **Full guide:** [Azure Monitor Getting Started](docs/azure-monitor-getting-started.md)

---

### 2. Microsoft Agent Framework

Capture activity from Agent Framework agents and export to Azure Monitor, OTLP, or both.

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Otlp;
});
```

Agent Framework activity sources are captured automatically — no extra configuration needed.

📖 **Full guide:** [Agent Framework Getting Started](docs/agent-framework-getting-started.md)

---

### 3. Agent365

Send agent telemetry (invoke agent, inference, tool execution, output) to the [Agent365](https://learn.microsoft.com/en-us/microsoft-agent-365/) observability backend.

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
    o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
    {
        return await MyTokenService.GetTokenAsync(agentId, tenantId);
    };
});
```

📖 **Full guide:** [Agent365 Getting Started](docs/agent365-getting-started.md)

---

### Combining destinations

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Agent365 | ExportTarget.Otlp;

    o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
    o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
    {
        return await MyTokenService.GetTokenAsync(agentId, tenantId);
    };
});
```

## Options reference

Full surface of `MicrosoftOpenTelemetryOptions`. Everything is opt-in; values shown are defaults.

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    // --- Export targets (pick one or combine with |) ---
    o.Exporters = ExportTarget.Console         // Console output (dev)
                | ExportTarget.Agent365        // Agent365 observability platform
                | ExportTarget.AzureMonitor    // Application Insights
                | ExportTarget.Otlp;           // OTLP (Aspire, Jaeger, Grafana)

    // --- Azure Monitor settings ---
    o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
    o.AzureMonitor.Credential = new DefaultAzureCredential(); // Optional AAD auth
    o.AzureMonitor.SamplingRatio = 1.0f;
    o.AzureMonitor.TracesPerSecond = 5.0;     // Rate-limited sampling (default)
    o.AzureMonitor.EnableLiveMetrics = true;
    o.AzureMonitor.EnableStandardMetrics = true;
    o.AzureMonitor.EnablePerfCounters = true;
    o.AzureMonitor.EnableTraceBasedLogsSampler = true;
    o.AzureMonitor.DisableOfflineStorage = false;
    o.AzureMonitor.StorageDirectory = null;

    // --- Agent365 exporter settings ---

    // Option A: Auto-managed tokens via DI (recommended for Agent Framework apps).
    // The Microsoft.Agents.A365.Observability.Hosting package registers
    // IExporterTokenCache<AgenticTokenStruct>. No TokenResolver needed.

    // Option B: Custom token resolver (non-agent apps, S2S, custom auth)
    o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
    {
        return await MyTokenService.GetTokenAsync(agentId, tenantId);
    };

    // Optional: custom domain resolver (default: agent365.svc.cloud.microsoft)
    o.Agent365.Exporter.DomainResolver = tenantId => "agent365.svc.cloud.microsoft";

    // Optional: use S2S endpoint path
    o.Agent365.Exporter.UseS2SEndpoint = false;

    // Optional: batch export tuning
    o.Agent365.Exporter.MaxQueueSize = 2048;
    o.Agent365.Exporter.MaxExportBatchSize = 512;
    o.Agent365.Exporter.ScheduledDelayMilliseconds = 5000;
    o.Agent365.Exporter.ExporterTimeoutMilliseconds = 30000;

    // --- Instrumentation options (all default to true) ---
    o.Instrumentation.EnableTracing = true;
    o.Instrumentation.EnableMetrics = true;
    o.Instrumentation.EnableLogging = true;
    o.Instrumentation.EnableAspNetCoreInstrumentation = true;
    o.Instrumentation.EnableHttpClientInstrumentation = true;
    o.Instrumentation.EnableSqlClientInstrumentation = true;
    o.Instrumentation.EnableAzureSdkInstrumentation = true;
    o.Instrumentation.EnableOpenAIInstrumentation = true;
    o.Instrumentation.EnableSemanticKernelInstrumentation = true;
    o.Instrumentation.EnableAgentFrameworkInstrumentation = true;
    o.Instrumentation.EnableAgent365Instrumentation = true;
});
```

### Token resolver: auto vs custom

| Approach | When to use | How it works |
|---|---|---|
| **Auto (DI) — default** | Agent Framework apps that reference `Microsoft.Agents.A365.Observability.Hosting` | `IExporterTokenCache<AgenticTokenStruct>` is registered automatically. Per-request token exchange happens via `ExchangeTurnTokenAsync`. |
| **Custom resolver** | Non-agent apps, service-to-service, or custom auth | Set `o.Agent365.Exporter.TokenResolver` directly. You own token acquisition. |

> If `TokenResolver` is set explicitly, the auto DI token cache is **not** registered — your resolver wins.

## Verify it works

Send a request through your agent (e.g., a Teams message). In the console output you should see activity spans from the instrumented sources:

```
Activity.DisplayName:        chat gpt-*
Activity.DisplayName:        invoke_agent *
Activity.DisplayName:        MessageProcessor
```

And a successful Agent365 export:

```
Received HTTP response headers after *ms - 200
```

## Internal logging

The distro's internal components (exporters, span processors) use `ILoggerFactory` from DI when available.
In ASP.NET Core and hosted apps, this means internal diagnostics flow through the app's configured logging pipeline automatically.

**Non-DI / console apps:** If your app does not register `ILoggerFactory` in DI, internal diagnostics are silently discarded (`NullLoggerFactory`). To see internal log output, add `Microsoft.Extensions.Logging.Console` and wire it up:

```bash
dotnet add package Microsoft.Extensions.Logging.Console
```

```csharp
builder.Services.AddLogging(logging => logging.AddConsole());
```

## Documentation

- [Azure Monitor Getting Started](docs/azure-monitor-getting-started.md) — Send traces, metrics, and logs to Application Insights
- [Agent Framework Getting Started](docs/agent-framework-getting-started.md) — Instrument Agent Framework agents with Azure Monitor and OTLP
- [Customization Guide](docs/customization.md) — Resource configuration, enrichment, filtering, and OTLP exporter tuning
- [Agent 365 Getting Started](docs/agent365-getting-started.md) — Add Agent365 observability using the distro
- [Agent 365 Migration Guide](docs/agent365-migration.md) — Migrate from the standalone Agent365 SDK to the distro
- [Agent 365 Migration Testing](docs/testing-agent365.md) — Detailed migration checklist with auto-instrumentation, env vars, and span comparison

## Examples

- [Azure.Monitor.OpenTelemetry.AspNetCore.Demo](examples/Azure.Monitor.OpenTelemetry.AspNetCore.Demo) — ASP.NET Core → Azure Monitor
- [Microsoft.OpenTelemetry.Agent365.Demo](examples/Microsoft.OpenTelemetry.Agent365.Demo) — Agent Framework app → Agent365
- [Microsoft.OpenTelemetry.AgentFramework.Demo](examples/Microsoft.OpenTelemetry.AgentFramework.Demo) — Agent Framework → OTLP / Azure Monitor

## Build & test

```bash
dotnet build Microsoft.OpenTelemetry.slnx
dotnet test Microsoft.OpenTelemetry.slnx
```

## Microsoft Open Source Code of Conduct

This project has adopted the
[Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more
information see the
[Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/)
or contact [opencode@microsoft.com](mailto:opencode@microsoft.com)
with any additional questions or comments.

## Data Collection

As this SDK is designed to enable applications to perform data collection which is sent to the Microsoft collection endpoints the following is required to identify our privacy statement.

The software may collect information about you and your use of the software and send it to Microsoft. Microsoft may use this information to provide services and improve our products and services. You may turn off the telemetry as described in the repository. There are also some features in the software that may enable you and Microsoft to collect data from users of your applications. If you use these features, you must comply with applicable law, including providing appropriate notices to users of your applications together with a copy of Microsoft’s privacy statement. Our privacy statement is located at https://go.microsoft.com/fwlink/?LinkID=824704. You can learn more about data collection and use in the help documentation and our privacy statement. Your use of the software operates as your consent to these practices.

### Internal Telemetry

Internal telemetry can be disabled by setting the environment variable `APPLICATIONINSIGHTS_STATSBEAT_DISABLED` to `true`.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft trademarks or logos is subject to and must follow [Microsoft’s Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general). Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship. Any use of third-party trademarks or logos are subject to those third-party’s policies.

## Reporting Security Issues

See [SECURITY.md](SECURITY.md).
