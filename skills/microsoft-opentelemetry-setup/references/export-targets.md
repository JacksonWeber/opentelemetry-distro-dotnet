# Export Targets

## ExportTarget flags enum

| Value | Traces | Metrics | Logs |
|---|---|---|---|
| `ExportTarget.Console` | Yes | Yes* | Yes* |
| `ExportTarget.Agent365` | Yes | No | No |
| `ExportTarget.AzureMonitor` | Yes | Yes | Yes |
| `ExportTarget.Otlp` | Yes | Yes | Yes |

*In A365-only mode (`Agent365` with or without `Console`), Console metrics/logs are suppressed by default. Override with `o.Instrumentation.EnableMetrics = true` and/or `o.Instrumentation.EnableLogging = true`.

## Combining

```csharp
o.Exporters = ExportTarget.Console | ExportTarget.Agent365 | ExportTarget.AzureMonitor;
```

## Auto-detection

When `Exporters` isn't explicitly set:
- **AzureMonitor**: enabled if `ConnectionString` is set (code, env var, or IConfiguration)
- **Agent365**: enabled if `TokenResolver` is set or DI token cache is registered

## Agent365-only mode

When Agent365 is the only real exporter (± Console), infrastructure instrumentation is auto-suppressed:

| Instrumentation | Normal | A365-only |
|---|---|---|
| ASP.NET Core | On | **Off** |
| HttpClient | On | **Off** |
| SQL Client | On | **Off** |
| Azure SDK | On | **Off** |
| Semantic Kernel | On | On |
| Agent Framework | On | On |
| OpenAI | On | On |

Re-enable individually: `o.Instrumentation.EnableHttpClientInstrumentation = true;`

## Azure Monitor options

```csharp
o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
o.AzureMonitor.SamplingRatio = 1.0f;
o.AzureMonitor.EnableLiveMetrics = true;
```

## Agent365 exporter options

```csharp
o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) => { ... };
o.Agent365.Exporter.DomainResolver = tenantId => "agent365.svc.cloud.microsoft";
o.Agent365.Exporter.UseS2SEndpoint = false;
o.Agent365.Exporter.MaxQueueSize = 2048;
o.Agent365.Exporter.MaxExportBatchSize = 512;
o.Agent365.Exporter.ScheduledDelayMilliseconds = 5000;
o.Agent365.Exporter.ExporterTimeoutMilliseconds = 30000;
```
