---
name: microsoft-opentelemetry-setup
description: 'Set up Microsoft.OpenTelemetry distro in a new .NET application. Use when adding observability to an agent, ASP.NET Core app, console app, or Agent Framework project. Covers package installation, exporter configuration, token resolver setup, baggage context, and instrumentation options.'
---

# Microsoft OpenTelemetry Distro — Greenfield Setup

## When to Use

- User asks to "add observability" or "add telemetry" to a .NET app
- User wants to set up Agent 365, Azure Monitor, or OTLP export
- User is starting a new agent project and needs tracing
- User asks about `Microsoft.OpenTelemetry` package

## Procedure

### 1. Detect App Type

Scan the project to determine the app type:
- **ASP.NET Core** — has `WebApplication.CreateBuilder()` or `IHostApplicationBuilder` → use [hosted pattern](./references/greenfield-aspnetcore.md)
- **Console / Background Service** — no web host, uses `OpenTelemetrySdk.Create()` → use [console pattern](./references/greenfield-console.md)
- **Agent Framework** — references `Microsoft.Agents.Builder` or `Microsoft.Agents.AI` → use [Agent Framework pattern](./references/greenfield-agentframework.md)

### 2. Install Package

Add to the project's `.csproj`:

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="<latest>" />
```

Remove any individual OpenTelemetry packages the distro replaces:
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Instrumentation.AspNetCore`
- `OpenTelemetry.Instrumentation.Http`
- `OpenTelemetry.Instrumentation.SqlClient`
- `OpenTelemetry.Exporter.Console`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`

### 3. Configure Exporters

See [export targets reference](./references/export-targets.md) for all options.

Minimal setup in `Program.cs`:

```csharp
using Microsoft.OpenTelemetry;

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
});
```

### 4. Set Up Authentication

See [token resolver patterns](./references/token-resolver-patterns.md).

- **Agent Framework apps**: No code needed — DI token cache auto-registers
- **Custom apps**: Set `o.Agent365.Exporter.TokenResolver`

### 5. Add Baggage Context

See [baggage setup](./references/baggage-setup.md).

Baggage propagates tenant/agent identity to all child spans.

### 6. Tune Instrumentation (optional)

See [instrumentation options](./references/instrumentation-options.md).

### 7. Verify

- Build the project
- Run with `ExportTarget.Console` to see spans in stdout
- Verify `gen_ai.agent.id` and `microsoft.tenant.id` appear on spans
- See [troubleshooting](./references/troubleshooting.md) if spans are missing
