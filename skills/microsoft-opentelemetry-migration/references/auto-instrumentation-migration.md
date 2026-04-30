# Auto-Instrumentation Migration

## Before: Explicit framework registration

```csharp
builder.AddA365Tracing(config =>
{
    config.WithSemanticKernel();     // → now auto-registered
    config.WithAgentFramework();     // → now auto-registered
    config.WithOpenAI();             // → now auto-registered
});
```

## After: All auto-registered by distro

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
    // SK, Agent Framework, OpenAI — all auto-registered, no code needed
});
```

## Auto-registered ActivitySources (no `.AddSource()` needed)

| Source | Origin |
|---|---|
| `Agent365Sdk` | Manual scopes |
| `Microsoft.SemanticKernel*` | Semantic Kernel |
| `Azure.AI.OpenAI*` | Azure OpenAI |
| `OpenAI.*` | OpenAI |
| `Experimental.Microsoft.Extensions.AI` | Extensions.AI |
| `Experimental.Microsoft.Agents.AI*` | Agent Framework |

## Custom sources still need explicit registration

If you used a custom `sourceName` in `.UseOpenTelemetry(sourceName: "MyApp")`:

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o => { o.Exporters = ExportTarget.Agent365; })
    .WithTracing(tracing => tracing.AddSource("MyApp"))
    .WithMetrics(metrics => metrics.AddMeter("MyApp"));
```

## Disabling specific instrumentation

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Instrumentation.EnableSemanticKernelInstrumentation = false;
    o.Instrumentation.EnableSqlClientInstrumentation = false;
});
```
