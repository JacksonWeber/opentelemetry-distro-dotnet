# Token Resolver Patterns

## Option A — Auto (DI) token cache (recommended for Agent Framework)

No `TokenResolver` needed in `Program.cs`. The distro auto-registers `IExporterTokenCache<AgenticTokenStruct>`.

```csharp
// Program.cs
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
});
```

In your agent, inject and call `RegisterObservability()`:

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Common;

public class MyAgent : AgentApplication
{
    private readonly IExporterTokenCache<AgenticTokenStruct> _agentTokenCache;

    public MyAgent(AgentApplicationOptions options,
        IExporterTokenCache<AgenticTokenStruct> agentTokenCache) : base(options)
    {
        _agentTokenCache = agentTokenCache;
    }

    protected async Task MessageActivityAsync(ITurnContext turnContext, ...)
    {
        _agentTokenCache.RegisterObservability(
            turnContext.Activity.Recipient.AgenticAppId,
            turnContext.Activity.Recipient.TenantId,
            new AgenticTokenStruct(
                userAuthorization: UserAuthorization,
                turnContext: turnContext,
                authHandlerName: "AGENTIC"),
            EnvironmentUtils.GetObservabilityAuthenticationScope());
    }
}
```

## Option B — Custom resolver (non-agent apps, S2S)

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
    o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
    {
        return await MyTokenService.GetTokenAsync(agentId, tenantId);
    };
});
```

When `TokenResolver` is set explicitly, the auto DI token cache is **not** registered.

## Option C — ServiceTokenCache (MSAL-direct scenarios)

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Caching;

var cache = new ServiceTokenCache(
    defaultExpiration: TimeSpan.FromMinutes(30),
    cleanupInterval: TimeSpan.FromMinutes(5));

cache.RegisterObservability(agentId, tenantId, bearerToken,
    EnvironmentUtils.GetObservabilityAuthenticationScope());
```
