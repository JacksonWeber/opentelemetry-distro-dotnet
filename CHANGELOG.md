# Changelog

## 1.0.0-beta.1

### Features

- Unified Microsoft OpenTelemetry distro combining Azure Monitor, Agent365, and Microsoft Agent Framework observability
- Single-line onboarding via `builder.UseMicrosoftOpenTelemetry()`
- `ExportTarget` flags enum for explicit exporter selection (Azure Monitor, Agent365, OTLP, Console)
- Auto-detection of exporters from connection string (code, env var, IConfiguration) and token resolver
- Azure Monitor distro: ASP.NET Core, HTTP Client, SQL Client instrumentation, resource detection, Azure SDK log forwarding
- Agent365 observability: InvokeAgent, Inference, ExecuteTool, Output scopes, baggage context propagation, Agent365 exporter
- Microsoft Agent Framework: captures `Experimental.Microsoft.Agents.AI` activity sources and metrics
- Agent365 framework extensions: Semantic Kernel, Agent Framework, Azure OpenAI (opt-in via fluent API)
- `IHostApplicationBuilder` and `IOpenTelemetryBuilder` entry points
- Agent365-only mode: infrastructure instrumentation auto-disabled when A365 is the sole exporter (#29)
- `InstrumentationOptions` for fine-grained control over instrumentation (enable/disable SK, Agent Framework, metrics, logging independently)
- Non-hosted entry point via `OpenTelemetrySdk.Create()` with `UseMicrosoftOpenTelemetry()` for console apps and background services
- Strong-name signing with Microsoft shared library key
- Targets `net8.0` and `netstandard2.0`

### Bug Fixes

- Fix SK auto-instrumentation message format to use structured JSON envelope matching A365 SDK output (#35, PR #40)
- Always register `IExporterTokenCache` in DI regardless of custom `TokenResolver` configuration (#42, PR #46)
- Fix Azure Monitor exporter in non-hosted scenarios — `ExporterRegistrationHostedService` now starts correctly without `IHostedService` (PR #43)

### Documentation

- Added Agent365-only mode instrumentation suppression documentation (PR #44)
- Added API differences section to migration guide with `ChatToolCallExtensions.Trace()` workaround (#37, PR #41)
- Clarified custom `ActivitySource` name usage for Agent Framework (PR #39)
- Added Console demo example using `OpenTelemetrySdk.Create()` with Console + OTLP + Azure Monitor exporters (PR #43)
