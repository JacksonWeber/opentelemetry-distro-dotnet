# Program.cs Migration

## Before (A365 SDK)

```csharp
using Microsoft.Agents.A365.Observability;
using Microsoft.Agents.A365.Observability.Extensions.AgentFramework;
using Microsoft.Agents.A365.Observability.Runtime;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureOpenTelemetry();

builder.Services.AddSingleton(new Agent365ExporterOptions
{
    TokenResolver = (agentId, tenantId) => Task.FromResult(TokenStore.GetToken(agentId, tenantId))
});

builder.AddA365Tracing(config =>
{
    config.WithAgentFramework();
});
```

## After (Distro)

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
});
```

## What to REMOVE

- `builder.ConfigureOpenTelemetry();`
- `builder.Services.AddSingleton(new Agent365ExporterOptions { ... });`
- `builder.AddA365Tracing(config => { ... });`
- `using` statements for `Microsoft.Agents.A365.Observability.*` in Program.cs
- `TokenStore` class file — delete entirely
- `ConfigureOpenTelemetry()` extension method file — delete entirely

## What to ADD

- `using Microsoft.OpenTelemetry;`
- `builder.UseMicrosoftOpenTelemetry(o => { ... });`

## Using statements that STILL WORK (no changes)

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Caching;   // IExporterTokenCache
using Microsoft.Agents.A365.Observability.Runtime.Common;     // BaggageBuilder, EnvironmentUtils
using Microsoft.Agents.A365.Observability.Hosting.Middleware;  // BaggageTurnMiddleware
```

These namespaces are identical in the distro — agent code doesn't need changes.
