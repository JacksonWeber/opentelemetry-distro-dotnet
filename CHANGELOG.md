# Changelog

## Unreleased

- Throttle distro Feature SDK Stats to a 24-hour emission cadence. The exporter now collects Feature SDK Stats on the shared 15-minute Network SDK Stats reader, so the observable gauge applies an emission-ticks throttle (mirroring the Attach gauge) to avoid emitting every 15 minutes.

## 1.0.5 - 2026-06-12

- Bootstrap SDK Stats eagerly in `UseMicrosoftOpenTelemetry` so Attach + Feature SDK Stats are reported when the Azure Monitor exporter is not selected (OTLP-only, Console-only, Agent365-only).
- Added SDK statistics reporting and routed it to a dedicated Microsoft ingestion path. Honors `APPLICATIONINSIGHTS_STATSBEAT_DISABLED=true`.
- Fix `service.name` propagation: add `ServiceNameKey` to `ActivityProcessor`'s attribute allow-list so `BaggageBuilder.OperationSource(...)` is copied onto eligible GenAI spans ([#118](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/118))

## 1.0.4 - 2026-06-01

- Add `ApplyGuardrailScope` for security guardrail evaluation tracing ([#109](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/109))
- Support custom A365 exporter via DI marker detection and add `GenAiActivityFilterProcessor` to pre-filter non-GenAI spans ([#110](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/110))
- Fix duplicate key exception in ExportFormatter ([#112](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/112))

## 1.0.3 - 2026-05-22

- Fix `gen_ai.tool.arguments` attribute name to `gen_ai.tool.call.arguments` ([#88](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/88))
- Add product context fallback for subchannels ([#90](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/90))
- Flatten Agent365 exporter configuration to remove one layer of nesting (remove `.Exporter`) ([#94](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/94))
- Update telemetry SDK name to `microsoft-opentelemetry` ([#95](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/95))
- Revert user.id fallback chain — ingest service only accepts GUIDs ([#96](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/96))
- Scope `ActivityProcessor.OnStart` to GenAI spans only ([#99](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/99))
- Add `ContextualTokenResolver` with agentic user ID support ([#101](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/101))
- Update `Azure.Monitor.OpenTelemetry.Exporter` to 1.8.1 ([#102](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/102))
- Decouple A365 exporter from `Microsoft.Agents.Builder` dependency ([#104](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/104))
- Align message format to OTel spec: remove version envelope ([#105](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/105))

## 1.0.2 - 2026-05-06

- Update to hosting telemetry extraction logic so that user.id baggage is resolved across more channel types ([#86](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/86))

## 1.0.1 - 2026-05-01

- Add explicit `Microsoft.Extensions.Logging` and `Microsoft.Extensions.Configuration.Binder` dependencies for net8.0 runtime compatibility

## 1.0.0 - 2026-05-01

First stable release of the Microsoft OpenTelemetry distro for .NET.

- Updated `Azure.Monitor.OpenTelemetry.Exporter` to 1.8.0 (#72)
- Removed URL query redaction override — OTel defaults now flow through (#71)
- Agent365 exporter: payload chunking and improved export reliability (#63)
- Distro SDK version reporting (#62)
- Source Link, deterministic builds, and `.snupkg` symbol packages (#55)
- Exclude example and test projects from NuGet packaging (#49)
- Resource attribute tests for `ConfigureResource` + `UseMicrosoftOpenTelemetry` (#51)
- Public API surface locked in `PublicAPI.Shipped.txt`

## 1.0.0-beta.2

### Bug Fixes

- Fix explicit `EnableMetrics`/`EnableLogging` flags being ignored in A365-only Console mode (#58)
- Fix `InternalsVisibleTo` assembly signing for conditional build configurations (#48)

### Documentation

- Added custom `ActivitySource` migration guidance for Agent Framework (#52, PR #54)

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
