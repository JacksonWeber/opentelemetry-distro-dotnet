# InstrumentationOptions

All default to `true`. Set in the `UseMicrosoftOpenTelemetry` callback.

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
    o.Instrumentation.EnableSqlClientInstrumentation = false;
    o.Instrumentation.EnableMetrics = true; // explicit override in A365-only mode
});
```

## Signal pipelines

| Property | Default | Effect when `false` |
|---|---|---|
| `EnableTracing` | `true` | No TracerProvider configured |
| `EnableMetrics` | `true` | No MeterProvider configured |
| `EnableLogging` | `true` | OTel log provider suppressed |

## Library instrumentation

| Property | Default | What it controls |
|---|---|---|
| `EnableAspNetCoreInstrumentation` | `true` | Incoming HTTP requests |
| `EnableHttpClientInstrumentation` | `true` | Outgoing HTTP requests |
| `EnableSqlClientInstrumentation` | `true` | SQL queries |
| `EnableAzureSdkInstrumentation` | `true` | Azure SDK clients |
| `EnableOpenAIInstrumentation` | `true` | OpenAI / Azure OpenAI |
| `EnableSemanticKernelInstrumentation` | `true` | Semantic Kernel |
| `EnableAgentFrameworkInstrumentation` | `true` | Agent Framework |
| `EnableAgent365Instrumentation` | `true` | Agent365 manual scopes |

## A365-only mode auto-suppression

When Agent365 is the only exporter (± Console), infra instrumentation is auto-suppressed. GenAI instrumentation is always kept. Explicit user settings override the suppression.
