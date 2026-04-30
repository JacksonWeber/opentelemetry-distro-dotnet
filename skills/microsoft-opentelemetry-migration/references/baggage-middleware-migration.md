# Baggage Middleware Migration

Baggage middleware is unchanged between A365 SDK and distro. The same classes and namespaces are used.

## No changes needed if already using:

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Middleware;

// BaggageTurnMiddleware — same API
builder.Services.AddSingleton<BaggageTurnMiddleware>();

// BaggageBuilder — same API
using var scope = new BaggageBuilder().TenantId("...").AgentId("...").Build();

// UseObservabilityRequestContext — same API
app.UseObservabilityRequestContext((ctx) => (tenantId, agentId));
```

## OutputLoggingMiddleware — same API

```csharp
builder.Services.AddSingleton<OutputLoggingMiddleware>();
```

## Auto-instrumentation middleware registration

If using Auto mode with middleware array:

```csharp
builder.Services.AddSingleton<Microsoft.Agents.Builder.IMiddleware[]>(sp =>
{
    var scopeMiddleware = sp.GetRequiredService<BaggageTurnMiddleware>();
    var outputMiddleware = sp.GetRequiredService<OutputLoggingMiddleware>();
    return [scopeMiddleware, outputMiddleware];
});
```

This pattern is identical in both A365 SDK and distro.
