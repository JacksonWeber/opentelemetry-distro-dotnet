# API Differences

| A365 SDK | Distro | Notes |
|----------|--------|-------|
| `ChatToolCallExtensions.Trace()` | Not included | Use `ExecuteToolScope.Start()` directly |
| `Builder` (fluent API) | `UseMicrosoftOpenTelemetry()` | Single entry point |
| `WithAgentFramework(additionalSources)` | `.WithTracing(t => t.AddSource("..."))` | Custom source names need explicit registration |
| `.ConfigureResource()` | `.ConfigureResource()` (unchanged) | Chain before `UseMicrosoftOpenTelemetry()` |
| `ConfigureOpenTelemetry()` | Removed | Replaced by `UseMicrosoftOpenTelemetry()` |
| `AddA365Tracing()` | Removed | Replaced by `UseMicrosoftOpenTelemetry()` |
| `Agent365ExporterOptions` (manual) | `o.Agent365.Exporter.*` | Configured inside callback |
| `TokenStore` | `IExporterTokenCache<AgenticTokenStruct>` | Auto-registered via DI |

## ChatToolCallExtensions workaround

The workaround below uses OpenAI SDK types (`ChatToolCall`). If you use a different LLM SDK, adapt the property names accordingly.

```csharp
// Before: chatToolCall.Trace(agentId, tenantId)
// After:
using var scope = ExecuteToolScope.Start(
    new Request(),
    new ToolCallDetails(
        chatToolCall.FunctionName,
        chatToolCall.FunctionArguments?.ToString(),
        chatToolCall.Id, null,
        chatToolCall.Kind.ToString()),
    new AgentDetails(agentId: agentId, tenantId: tenantId));
scope.RecordResponse(result);
```

## What stays the same

- `InvokeAgentScope`, `InferenceScope`, `ExecuteToolScope`, `OutputScope` — same API
- `BaggageBuilder` — same API
- `BaggageTurnMiddleware`, `OutputLoggingMiddleware` — same API
- `IExporterTokenCache<AgenticTokenStruct>` — same interface
- `EnvironmentUtils.GetObservabilityAuthenticationScope()` — same
- All `Microsoft.Agents.A365.Observability.*` namespaces — identical in distro
