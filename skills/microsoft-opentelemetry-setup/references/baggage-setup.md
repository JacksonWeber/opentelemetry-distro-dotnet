# Baggage Setup

Baggage propagates tenant/agent identity to all child spans. Required for A365 export.

## Manual BaggageBuilder

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Common;

using var baggageScope = new BaggageBuilder()
    .TenantId("tenant-123")
    .AgentId("agent-456")
    .ConversationId("conv-789")
    .Build();
// All spans in this scope get these attributes
```

## Auto-populate from TurnContext

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Extensions;

using var baggageScope = new BaggageBuilder()
    .FromTurnContext(turnContext)
    .Build();
```

## BaggageTurnMiddleware (auto per-turn)

Register on the adapter to auto-set baggage for every turn:

```csharp
builder.Services.AddSingleton<BaggageTurnMiddleware>();
builder.Services.AddSingleton<Microsoft.Agents.Builder.IMiddleware[]>(sp =>
    new Microsoft.Agents.Builder.IMiddleware[] { sp.GetRequiredService<BaggageTurnMiddleware>() });
```

## HTTP-level middleware (optional)

Set baggage before the Bot Framework pipeline:

```csharp
app.UseObservabilityRequestContext((httpContext) =>
{
    var tenantId = GetTenantIdFromContext(httpContext);
    var agentId = GetAgentIdFromContext(httpContext);
    return (tenantId, agentId);
});
```

## Why baggage matters

The A365 exporter requires `microsoft.tenant.id` and `gen_ai.agent.id` on every span. Spans without these are **silently dropped**. `BaggageBuilder` sets these in W3C Baggage, and `BaggageSpanProcessor` copies them to all child spans.
