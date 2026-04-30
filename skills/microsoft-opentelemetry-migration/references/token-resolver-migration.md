# Token Resolver Migration

## Before: TokenStore pattern

```csharp
// Program.cs
builder.Services.AddSingleton(new Agent365ExporterOptions
{
    TokenResolver = (agentId, tenantId) => Task.FromResult(TokenStore.GetToken(agentId, tenantId))
});

// TokenStore.cs
public static class TokenStore
{
    private static readonly ConcurrentDictionary<string, string> _tokens = new();
    public static void SetToken(string agentId, string tenantId, string token) => ...
    public static string? GetToken(string agentId, string tenantId) => ...
}

// Agent code
TokenStore.SetToken(agentId, tenantId, token);
```

## After: DI token cache (recommended)

```csharp
// Program.cs — no TokenResolver needed
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
});

// Agent code — inject via constructor
private readonly IExporterTokenCache<AgenticTokenStruct> _agentTokenCache;

_agentTokenCache.RegisterObservability(agentId, tenantId, new AgenticTokenStruct(
    userAuthorization: UserAuthorization,
    turnContext: turnContext,
    authHandlerName: "AGENTIC"),
    EnvironmentUtils.GetObservabilityAuthenticationScope());
```

## After: Custom resolver (if not using Agent Framework hosting)

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

## Cleanup

- Delete `TokenStore.cs` file
- Remove `TokenStore.SetToken(...)` / `TokenStore.GetToken(...)` calls
- Remove `builder.Services.AddSingleton(new Agent365ExporterOptions { ... })`
