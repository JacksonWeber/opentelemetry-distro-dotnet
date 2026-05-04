# Migrating from Agent365 SDK to Microsoft OpenTelemetry Distro (.NET)

> **Migration quick-reference** — before/after code, span attributes, and troubleshooting for migrating from the standalone Agent365 SDK to the `Microsoft.OpenTelemetry` distro. For the full standalone guide (no migration context), see [Agent 365 Getting Started](agent365-getting-started.md).

## Installation (.NET)

Install the Microsoft OpenTelemetry Distro package. This single package replaces both the Agent365 observability packages and the raw OpenTelemetry packages.

### Package to ADD

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="<latest>" />
```

### Packages to REMOVE (replaced by the distro)

```xml
<!-- Remove all of these -->
<PackageReference Include="Microsoft.Agents.A365.Observability.Extensions.AgentFramework" Version="0.2.151-beta" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.12.0" />
```

### Packages to KEEP (unchanged)

```xml
<PackageReference Include="Microsoft.Agents.A365.Notifications" Version="0.2.151-beta" />
<PackageReference Include="Microsoft.Agents.A365.Tooling.Extensions.AgentFramework" Version="0.2.151-beta" />
<!-- ... all other non-observability packages stay as-is -->
```

### API differences from A365 SDK

The distro bundles the same observability functionality as the A365 SDK packages, with a few intentional differences:

| A365 SDK | Distro | Notes |
|----------|--------|-------|
| `ChatToolCallExtensions.Trace()` | Not included | The distro does not depend on the `OpenAI` NuGet package. Use `ExecuteToolScope.Start()` directly instead (see [workaround](#chattoolcallextensions-workaround)). |
| `Builder` (fluent API) | `UseMicrosoftOpenTelemetry()` | The distro replaces the A365 `Builder` class with a single entry point. See [Configuration](#configuration-net). |
| `WithAgentFramework(additionalSources)` | `.WithTracing(t => t.AddSource("..."))` | Agent Framework–specific. The distro subscribes to the default `Experimental.Microsoft.Agents.AI*` sources only. Register custom source names via standard OpenTelemetry `.AddSource()`. See [Agent Framework getting-started: custom ActivitySource name](agent-framework-getting-started.md#using-a-custom-activitysource-name). |
| `.ConfigureResource()` (service identity) | `.ConfigureResource()` (unchanged) | Works the same way. Chain it before `UseMicrosoftOpenTelemetry()`. The distro merges user-configured attributes with auto-detected ones (Azure VM, etc.). See [Customization: Configuring the Resource](customization.md#configuring-the-resource). |

#### `ChatToolCallExtensions` workaround

If you previously used `chatToolCall.Trace(agentId, tenantId)` from `Microsoft.Agents.A365.Observability.Extensions.OpenAI`, replace it with direct scope creation — all required types are public in the distro:

```csharp
using var scope = ExecuteToolScope.Start(
    new Request(),
    new ToolCallDetails(
        chatToolCall.FunctionName,
        chatToolCall.FunctionArguments?.ToString(),
        chatToolCall.Id,
        null,
        chatToolCall.Kind.ToString()),
    new AgentDetails(agentId: agentId, tenantId: tenantId));

// execute tool...
scope.RecordResponse(result);
```

## Configuration (.NET)

### Before (Agent365 SDK — deprecated)

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

### After (Microsoft OpenTelemetry Distro)

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;

    // Option A (recommended): Let the distro auto-manage tokens via DI.
    // IExporterTokenCache<AgenticTokenStruct> is registered automatically.
    // Your agent calls RegisterObservability() at runtime.

    // Option B: Provide your own token resolver (matches old Agent365ExporterOptions pattern)
    o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
    {
        return await MyTokenService.GetTokenAsync(agentId, tenantId);
    };
});
```

If you need to add your own application-specific activity sources, use the longer form:

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365;
    })
    .WithTracing(tracing => tracing
        .AddSource("MyCompany.MyAgent.CustomSource"));
```

> Activity sources for Agent Framework, Semantic Kernel, and OpenAI are auto-registered by the distro — you only need `.AddSource()` for your own custom sources. **Note:** If you used a custom `sourceName` in Agent Framework's `.UseOpenTelemetry(sourceName: "...")`, you must register it here — the distro does not auto-detect custom names. See [Agent Framework: custom ActivitySource name](agent-framework-getting-started.md#using-a-custom-activitysource-name).

> ⚠️ **Production warning:** `ExportTarget.Console` is intended for local development only. Do not include it in production deployments — it adds overhead and may log sensitive telemetry to stdout. Use `ExportTarget.Agent365` and/or `ExportTarget.AzureMonitor` in production.

### What to REMOVE from Program.cs

- `builder.ConfigureOpenTelemetry();` — replaced by `UseMicrosoftOpenTelemetry()`
- `builder.Services.AddSingleton(new Agent365ExporterOptions { ... });` — token cache is auto-registered via DI
- `builder.AddA365Tracing(config => { ... });` — replaced by `UseMicrosoftOpenTelemetry()`
- `using` statements for `Microsoft.Agents.A365.Observability.*` in Program.cs — replace with `using Microsoft.OpenTelemetry;` and `using OpenTelemetry;`
- `TokenStore` class file — delete it if present. Use `o.Agent365.Exporter.TokenResolver` directly, or let the distro auto-manage tokens via `IExporterTokenCache<AgenticTokenStruct>` (injected via DI)
- `ConfigureOpenTelemetry()` extension method file (if you have one) — delete it, the distro handles all OTel setup

## ConfigureResource for service identity

If your old SDK project used a custom `AgentOTELExtensions.cs` with `.AddService()` to set `service.name`, `service.namespace`, `service.version`, and `deployment.environment`, migrate to the standard `.ConfigureResource()` API. Without this, spans show `unknown_service:...` as the service name.

### Before (Agent365 SDK)

```csharp
// Custom AgentOTELExtensions.cs
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("MyAgent", serviceVersion: "1.0.0")));
```

### After (Distro)

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName: "MyAgent", serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["service.namespace"] = "Microsoft.Agents"
        }))
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365;
    });
```

> `ConfigureResource()` is additive — your attributes are merged with the distro's auto-detected resource attributes (Azure VM, App Service, etc.). See [Customization: Configuring the Resource](customization.md#configuring-the-resource) for the full guide.

## Token resolver

When using the Agent 365 exporter, a token resolver is required. The distro can either auto-provide one via DI or you can set one explicitly via `o.Agent365.Exporter.TokenResolver`.

### Auto (DI) — recommended for Agent Framework apps

When no custom `TokenResolver` is set, the distro automatically calls `AddAgenticTracingExporter()` internally, registering `IExporterTokenCache<AgenticTokenStruct>` and `Agent365ExporterOptions` via DI. Your agent calls `RegisterObservability()` at runtime to supply credentials, and the cache handles token acquisition and refresh.

**Setup in `Program.cs`:**

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
    // No TokenResolver needed — the distro registers the agentic token cache automatically.
});
```

**In your agent class:**

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Common;

public class MyAgent : AgentApplication
{
    private readonly IExporterTokenCache<AgenticTokenStruct> _agentTokenCache;
    private readonly ILogger<MyAgent> _logger;

    public MyAgent(
        AgentApplicationOptions options,
        IExporterTokenCache<AgenticTokenStruct> agentTokenCache,
        ILogger<MyAgent> logger) : base(options)
    {
        _agentTokenCache = agentTokenCache
            ?? throw new ArgumentNullException(nameof(agentTokenCache));
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    protected async Task MessageActivityAsync(
        ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        using var baggageScope = new BaggageBuilder()
            .TenantId(turnContext.Activity.Recipient.TenantId)
            .AgentId(turnContext.Activity.Recipient.AgenticAppId)
            .Build();

        try
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
        catch (Exception ex)
        {
            _logger.LogWarning("Error registering for observability: {Message}", ex.Message);
        }

        // ... your agent logic
    }
}
```

### Custom resolver — advanced

For non-agent apps, service-to-service, or custom auth scenarios, set the token resolver directly:

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365;
        o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
        {
            return await MyTokenService.GetTokenAsync(agentId, tenantId);
        };
    });
```

### Auto vs custom comparison

| Approach | When to use | How it works |
|---|---|---|
| **Auto (DI)** — default | Agent Framework apps with `UserAuthorization` | Distro internally calls `AddAgenticTracingExporter()`, registering `IExporterTokenCache<AgenticTokenStruct>`. Your agent calls `RegisterObservability()`. Token exchange happens via `ExchangeTurnTokenAsync`. |
| **Custom resolver** | Non-agent apps, service-to-service, or custom auth | Set `o.Agent365.Exporter.TokenResolver` directly. You own token acquisition. |


## Baggage middleware

The A365 SDK migration to the distro does **not** auto-register baggage middleware. You must explicitly register it. Without middleware, auto-instrumented spans (e.g., from Semantic Kernel) will lack identity attributes (`microsoft.tenant.id`, `gen_ai.agent.id`) and be **silently dropped** by the A365 exporter.

### `BaggageTurnMiddleware` — Bot Framework pipeline

Propagates agent/tenant/user/conversation context as baggage to all child spans within a Bot Framework turn. Skips baggage setup for async replies (`ContinueConversation` events).

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Middleware;

// Register on your Bot adapter
adapter.Use(new BaggageTurnMiddleware());
```

### `UseObservabilityRequestContext` — ASP.NET HTTP pipeline

Sets tenant and agent IDs at the HTTP level before the Bot Framework pipeline runs. This adds `ObservabilityBaggageMiddleware` to the ASP.NET Core pipeline.

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Middleware;

var app = builder.Build();

// Must run BEFORE the Bot Framework pipeline
app.UseObservabilityRequestContext((httpContext) =>
{
    var tenantId = GetTenantIdFromContext(httpContext);
    var agentId = GetAgentIdFromContext(httpContext);
    return (tenantId, agentId);
});

app.Run();
```

> **Ordering matters:** `UseObservabilityRequestContext()` must be registered before any Bot Framework middleware so that baggage is available to all downstream spans.

## Auto-instrumentation

Auto-instrumentation automatically listens to agentic frameworks' existing telemetry signals and forwards them to Agent 365 observability service. The distro handles all registration automatically via `UseMicrosoftOpenTelemetry()`.

> **How it works:** All A365 auto-instrumentations are **span processors that enrich existing spans** — they do not create new spans. Semantic Kernel and Agent Framework have dedicated span processors that extract and transform span attributes (e.g., input/output messages). OpenAI has source subscriptions only. The distro registers these processors automatically when `UseMicrosoftOpenTelemetry()` is called.

The distro automatically subscribes to these `ActivitySource` names when the relevant instrumentation is enabled:

| ActivitySource | Origin |
|---|---|
| `Agent365Sdk` | Agent365 manual scopes (InvokeAgent, ExecuteTool, Inference, Output) |
| `Microsoft.SemanticKernel*` | Semantic Kernel operations (wildcard) |
| `Azure.AI.OpenAI*` | Azure OpenAI calls (wildcard) |
| `OpenAI.*` | OpenAI calls (wildcard) |
| `Experimental.Microsoft.Extensions.AI` | LLM calls via Microsoft.Extensions.AI |
| `Experimental.Microsoft.Agents.AI` | Agent operations (invoke_agent, chat, execute_tool) |
| `Experimental.Microsoft.Agents.AI.Agent` | Agent-level telemetry |
| `Experimental.Microsoft.Agents.AI.ChatClient` | Chat client telemetry |
| `Azure.*` | Azure SDK (via Azure Monitor integration) |

### Semantic Kernel

Auto-instrumentation requires `BaggageBuilder` to set agent ID and tenant ID. Ensure that the ID used when creating a `ChatCompletionAgent` matches the agent ID passed to `BaggageBuilder`:

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Common;

public async Task ProcessUserRequest(string userInput)
{
    using var baggageScope = new BaggageBuilder()
        .AgentId("agent-456")
        .TenantId("tenant-123")
        .Build();

    var chatCompletionAgent = new ChatCompletionAgent
    {
        Id = "agent-456",  // Must match BaggageBuilder.AgentId
        // ... other configuration
    };

    // Semantic Kernel calls are automatically traced
    await foreach (var response in chatCompletionAgent.InvokeAsync(userInput))
    {
        // Process response.Content
    }
}
```

No `.AddSource()` needed — the distro auto-registers `Microsoft.SemanticKernel*` and the internal `SemanticKernelSpanProcessor`.

### OpenAI

Auto-instrumentation requires `BaggageBuilder`. OpenAI calls are automatically traced when `EnableOpenAIInstrumentation` is `true` (default):

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Common;

public async Task<string> ProcessUserRequest(string userInput)
{
    using var baggageScope = new BaggageBuilder()
        .AgentId("agent-456")
        .TenantId("tenant-123")
        .Build();

    // OpenAI calls are automatically traced
    var response = await openAIClient.GetChatCompletionsAsync(...);

    // For tool calls, use ExecuteToolScope for manual tracing:
    var toolDetails = new ToolCallDetails(
        toolName: "search",
        arguments: "{\"query\": \"...\"}",
        toolCallId: "tc-001",
        toolType: "function");

    using var toolScope = ExecuteToolScope.Start(request, toolDetails, agentDetails);
    var result = await RunToolAsync();
    toolScope.RecordResponse(result);
}
```

The distro subscribes to `OpenAI.*` and `Azure.AI.OpenAI*` sources automatically. However, to enable the OpenAI SDK to emit telemetry, you must set the `AppContext` switch in your application:

```csharp
AppContext.SetSwitch("OpenAI.Experimental.EnableOpenTelemetry", true);
```

### Agent Framework

Auto-instrumentation requires `BaggageBuilder`:

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Common;

public class MyAgent : AgentApplication
{
    protected async Task MessageActivityAsync(
        ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        using var baggageScope = new BaggageBuilder()
            .AgentId("agent-456")
            .TenantId("tenant-123")
            .Build();

        // Agent Framework calls are automatically traced
    }
}
```

> **Custom ActivitySource names:** If the old SDK used a custom `sourceName` in `.UseOpenTelemetry(sourceName: "MyCustomSource")`, the distro does **not** auto-detect it — only the three default `Experimental.Microsoft.Agents.AI*` sources are subscribed. You must register it explicitly via `.AddSource("MyCustomSource")`. See [Agent Framework getting-started: custom ActivitySource name](agent-framework-getting-started.md#using-a-custom-activitysource-name).

## Exporter and options reference

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        // --- Export targets (pick one or combine with |) ---
        o.Exporters = ExportTarget.Agent365;         // Agent365 observability platform
        // o.Exporters |= ExportTarget.AzureMonitor; // Application Insights
        // o.Exporters |= ExportTarget.Console;      // Console output (dev only)

        // --- Agent365 exporter settings ---

        // Option A: Let the distro auto-manage tokens via DI (recommended)
        // IExporterTokenCache<AgenticTokenStruct> is registered automatically.
        // Your agent calls RegisterObservability() at runtime.
        // No TokenResolver needed here — it's wired internally.

        // Option B: Provide your own token resolver (advanced/custom)
        o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
        {
            return await MyTokenService.GetTokenAsync(agentId, tenantId);
        };

        // Optional: custom domain resolver
        o.Agent365.Exporter.DomainResolver = tenantId => "agent365.svc.cloud.microsoft";

        // Optional: use S2S endpoint path
        o.Agent365.Exporter.UseS2SEndpoint = false;

        // Optional: batch export tuning
        o.Agent365.Exporter.MaxQueueSize = 2048;
        o.Agent365.Exporter.MaxExportBatchSize = 512;
        o.Agent365.Exporter.ScheduledDelayMilliseconds = 5000;
        o.Agent365.Exporter.ExporterTimeoutMilliseconds = 30000;

        // --- Azure Monitor settings ---
        // o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
        // o.AzureMonitor.SamplingRatio = 1.0f;
        // o.AzureMonitor.EnableLiveMetrics = true;
    });
```

### Auth scopes

> ⚠️ **Breaking change:** The observability authentication scope changed from `https://api.powerplatform.com/.default` (old A365 SDK) to a new value. If you hardcoded the old scope, you will get auth failures after migration. You must also grant the `Agent365.Observability.OtelWrite` permission — see [HTTP 403 Forbidden](#http-403-forbidden).

There is no `AuthScopes` property on `Agent365ExporterOptions`. Do not hardcode scope strings. Auth scopes are controlled two ways:

1. **Via `RegisterObservability()`** — the `observabilityScopes` parameter on the token cache. Use the helper to get the correct scope automatically:
   ```csharp
   using Microsoft.Agents.A365.Observability.Runtime.Common;

   _agentTokenCache.RegisterObservability(agentId, tenantId, tokenGenerator,
       EnvironmentUtils.GetObservabilityAuthenticationScope());
   ```

2. **Via environment variable** — `A365_OBSERVABILITY_SCOPE_OVERRIDE` overrides the default scope for testing:
   ```powershell
   $env:A365_OBSERVABILITY_SCOPE_OVERRIDE = "api://test/.default"
   ```

## Validate locally

### Console + Agent365 (validate locally and remotely)

To validate telemetry locally while also sending to Agent365, use both exporters:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
});
```

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run
```

Send a message via Teams. In the console output, look for:

```
Activity.DisplayName:        chat gpt-*
Activity.DisplayName:        invoke_agent *
Activity.DisplayName:        MessageProcessor
```

### Debugging A365 export failures

To investigate export issues, enable verbose logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.Agents.A365.Observability": "Debug"
    }
  }
}
```

You can also set override environment variables for testing against custom endpoints:

```powershell
$env:A365_OBSERVABILITY_DOMAIN_OVERRIDE = "https://your-test-endpoint.example.com"
$env:A365_OBSERVABILITY_SCOPE_OVERRIDE = "https://api.powerplatform.com/.default"
```

Key log messages to look for:

```
dbug: Agent365ExporterCore: Obtained token for agent {agentId} tenant {tenantId}.
dbug: Agent365ExporterCore: Sending {spanCount} spans to {requestUri}.
dbug: Agent365ExporterCore: HTTP {statusCode} exporting spans. 'x-ms-correlation-id': '{correlationId}'.
warn: Agent365ExporterCore: No token obtained. Skipping export for this identity.
fail: Agent365ExporterCore: TokenResolver threw for agent {agentId} tenant {tenantId}.
fail: Agent365ExporterCore: Exception exporting spans.
```

### Verify in A365 export

When using `ExportTarget.Console | ExportTarget.Agent365`, look for successful HTTP responses:

```
Received HTTP response headers after *ms - 200
```

## Troubleshooting

### Observability data doesn't appear

**Symptoms:** Agent is running, no telemetry in admin center, can't see agent activity.

**Solutions:**

- Verify observability exporter is enabled via `ExportTarget.Agent365` in `UseMicrosoftOpenTelemetry()`.
- Check token resolver configuration — exporter requires a valid token.
- Enable verbose logging and check for observability-related errors.
- Add `ExportTarget.Console` and check if telemetry is generated locally.

### Missing tenant ID or agent ID — spans skipped

**Symptoms:** Spans are silently dropped and never exported.

**Resolution:** Ensure `BaggageBuilder` is set up with tenant ID and agent ID before creating spans. These values propagate through the OpenTelemetry context and attach to all spans created within the baggage scope. See [Baggage middleware](#baggage-middleware) — without middleware registration, auto-instrumented spans will lack identity attributes.

### Token resolution failure — export skipped or unauthorized

**Symptoms:** Token resolver returns `null` or throws an error.

**Resolution:**

- Verify that a token resolver is provided and returns a valid Bearer token.
- Ensure correct tenant ID and agent ID are used for `BaggageBuilder`.
- For Azure-hosted agents, verify the Managed Identity has the required API permission for the observability scope.

### HTTP 401 Unauthorized

**Symptoms:** Export fails with HTTP 401. The exporter doesn't retry this error.

**Resolution:**

- Verify the token audience matches the observability endpoint scope.
- Check that the token resolver isn't returning a delegated user token, a token for an incorrect audience, or an expired token.

### HTTP 403 Forbidden

**Symptoms:** Export fails with HTTP 403. The exporter doesn't retry this error. An HTTP 403 error can have different causes. Check the following resolutions in order.

**Resolution:**

- **Missing license** — Verify that your tenant has one of the following licenses assigned in [Microsoft 365 admin center](https://admin.cloud.microsoft/?source=applauncher#/homepage):
  - **Test - Microsoft 365 E7**
  - **Microsoft 365 E7**
  - **Microsoft Agent 365 Frontier**
- **Missing `Agent365.Observability.OtelWrite` permission** — If you recently upgraded your observability packages, you need to grant this permission. Grant it using one of the following options:

  **Option A — Agent 365 CLI** (requires `a365.config.json` and `a365.generated.config.json` in your config directory, a Global Administrator account, and [Agent 365 CLI v1.1.139-preview](https://www.nuget.org/packages/Microsoft.Agents.A365.DevTools.Cli/1.1.139-preview) or later)

  ```
  a365 setup admin --config-dir "<path-to-config-dir>"
  ```

  This command grants all missing permissions, including the new Observability scopes.

  **Option B — Entra Portal** (no config files required; requires Global Administrator access to the blueprint app registration)

  1. Go to **Entra portal** > **App registrations** > select your Blueprint app.
  2. Go to **API permissions** > **Add a permission** > **APIs my organization uses** > search for `9b975845-388f-4429-889e-eab1ef63949c`.
  3. Select **Delegated permissions** > check `Agent365.Observability.OtelWrite` > **Add permissions**.
  4. Repeat step 2–3, this time select **Application permissions** > check `Agent365.Observability.OtelWrite` > **Add permissions**.
  5. Click **Grant admin consent** and confirm.

  Both `Agent365.Observability.OtelWrite` (Delegated) and `Agent365.Observability.OtelWrite` (Application) should show `Granted` status.

### Logging configuration

Use the `Microsoft.Agents.A365.Observability` logger category for debug-level diagnostics:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.Agents.A365.Observability": "Debug"
    }
  }
}
```

### HTTP 429 / 5xx — Transient errors

**Resolution:** These are usually transient. The .NET SDK doesn't retry automatically. Consider reducing export frequency by adjusting `MaxExportBatchSize` or `ScheduledDelayMilliseconds` in exporter options.

### Export timeout

**Resolution:**

- Check network connectivity to the observability endpoint.
- Default HTTP request timeout is 30 seconds.
- Increase `ExporterTimeoutMilliseconds` in exporter options if timeouts occur frequently.

### Export succeeds but telemetry doesn't appear in Defender or Purview

**Resolution:**

- Verify prerequisites for viewing exported logs (Purview auditing, Defender advanced hunting).
- Telemetry can take several minutes to populate after a successful export.

### Cross-language reference

For troubleshooting patterns consistent across languages (logging level migration, environment variables), see the [Node.js migration guide](https://github.com/microsoft/opentelemetry-distro-javascript/blob/main/MIGRATION_A365.md).

## Reference implementation

See: `examples/Microsoft.OpenTelemetry.Agent365.Demo/` in the distro repo.
