# Agent 365 Observability (.NET)

> **Standalone guide** — covers everything you need to add Agent365 observability using the `Microsoft.OpenTelemetry` distro. For migrating from the standalone Agent365 SDK, see [Migrating from Agent365 SDK](agent365-migration.md).

> **Important:** You need to be part of the [Frontier preview program](https://adoption.microsoft.com/copilot/frontier-program/) to get **early access** to Microsoft Agent 365. Frontier connects you directly with Microsoft's latest AI innovations.

To participate in the Agent 365 ecosystem, add Agent 365 Observability capabilities to your agent. Agent 365 Observability builds on [OpenTelemetry (OTel)](https://opentelemetry.io/docs/specs/otel/protocol/) and provides a unified framework for capturing telemetry consistently and securely across all agent platforms. By implementing this required component, you enable IT admins to monitor your agent's activity in Microsoft admin center and allow security teams to use Defender and Purview for compliance and threat detection.

## Key benefits

- **End-to-end visibility**: Capture comprehensive telemetry for every agent invocation, including sessions, tool calls, and exceptions, giving you full traceability across platforms.
- **Security and compliance enablement**: Feed unified audit logs into Defender and Purview, enabling advanced security scenarios and compliance reporting for your agent.
- **Cross-platform flexibility**: Build on OTel standards and support diverse runtimes and platforms like Copilot Studio, Foundry, and future agent frameworks.
- **Operational efficiency for admins**: Provide centralized observability in Microsoft 365 admin center, reducing troubleshooting time and improving governance with role-based access controls for IT teams managing your agent.

## Supported agents

| Agent type | Details |
|---|---|
| **Microsoft Agent 365-enabled agents** | Use the observability SDK to instrument your agent. |
| **Custom engine agents** | Use the observability SDK to instrument your agent. |
| **Declarative agents** | Observability is supported out of the box. No SDK implementation required. |

## Installation

Install the Microsoft OpenTelemetry Distro NuGet package. This single package includes the Agent365 observability runtime, hosting integration, auto-instrumentation for supported AI frameworks, and standard OpenTelemetry libraries.

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="<latest>" />
```

> **Note:** Add a `nuget.config` in your project root if the package is not available on the public NuGet feed. Check the [README](../README.md) for feed configuration.

## Configuration

In `Program.cs`, call `UseMicrosoftOpenTelemetry()` to enable observability:

```csharp
using Microsoft.OpenTelemetry;
using Microsoft.Agents.A365.Observability.Hosting.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console | ExportTarget.Agent365;

    o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
    {
        return await MyTokenService.GetTokenAsync(agentId, tenantId);
    };
});

var app = builder.Build();

// Register HTTP-level baggage middleware (optional — see Baggage section)
app.UseObservabilityRequestContext((httpContext) =>
{
    var tenantId = GetTenantIdFromContext(httpContext);
    var agentId = GetAgentIdFromContext(httpContext);
    return (tenantId, agentId);
});

app.Run();
```

If you need to add your own application-specific activity sources, use the longer form:

```csharp
using Microsoft.OpenTelemetry;

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
    })
    .WithTracing(tracing => tracing
        .AddSource("MyCompany.MyAgent.CustomSource"));
```

> Activity sources for Agent Framework, Semantic Kernel, OpenAI, and Azure SDK are auto-registered by the distro — you only need `.AddSource()` for your own custom sources.

> ⚠️ **Production warning:** `ExportTarget.Console` is intended for local development only. Do not include it in production deployments — it adds overhead and may log sensitive telemetry to stdout. Use `ExportTarget.Agent365` and/or `ExportTarget.AzureMonitor` in production.

### Export targets

The `ExportTarget` flags enum controls where telemetry is sent:

| Value | Description |
|---|---|
| `ExportTarget.None` | No exporters enabled. Distro-managed telemetry pipelines are disabled unless you configure your own providers separately. |
| `ExportTarget.Console` | Console output (development only). |
| `ExportTarget.Agent365` | Agent365 observability platform. |
| `ExportTarget.AzureMonitor` | Application Insights (auto-detected via `APPLICATIONINSIGHTS_CONNECTION_STRING`). |
| `ExportTarget.Otlp` | OpenTelemetry Protocol export. |

Combine targets with `|`: `ExportTarget.Console | ExportTarget.Agent365 | ExportTarget.AzureMonitor`.

### Agent365 exporter options

Customize the Agent365 exporter behavior via `o.Agent365.Exporter`:

| Property | Description | Default |
|---|---|---|
| `TokenResolver` | `AsyncAuthTokenResolver` delegate: `Task<string?>(string agentId, string tenantId)` | `null` |
| `DomainResolver` | `TenantDomainResolver` delegate: `string(string tenantId)` | `tenantId => "agent365.svc.cloud.microsoft"` |
| `UseS2SEndpoint` | When `true`, uses the service-to-service endpoint path. | `false` |
| `MaxQueueSize`| Maximum queue size for the batch processor. | `2048` |
| `MaxExportBatchSize` | Maximum batch size for export operations. | `512` |
| `ScheduledDelayMilliseconds` | Delay in milliseconds between export batches. | `5000` |
| `ExporterTimeoutMilliseconds` | Timeout in milliseconds for the export operation. | `30000` |

### Azure Monitor options

When `ExportTarget.AzureMonitor` is set, configure via `o.AzureMonitor`:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365 | ExportTarget.AzureMonitor;
    // Connection string is auto-detected from APPLICATIONINSIGHTS_CONNECTION_STRING
    // or set explicitly:
    o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
    o.AzureMonitor.SamplingRatio = 1.0f;
    o.AzureMonitor.EnableLiveMetrics = true;
});
```

### Instrumentation options

Control which auto-instrumentation libraries are enabled via `o.Instrumentation`. All default to `true`:

| Property | Description |
|---|---|
| `EnableTracing` | Enable trace signal pipeline. |
| `EnableMetrics` | Enable metrics signal pipeline. |
| `EnableLogging` | Enable logging signal pipeline. |
| `EnableAspNetCoreInstrumentation` | ASP.NET Core request/response traces. |
| `EnableHttpClientInstrumentation` | HttpClient outgoing request traces. |
| `EnableSqlClientInstrumentation` | SQL client query traces. |
| `EnableAzureSdkInstrumentation` | Azure SDK client traces. |
| `EnableOpenAIInstrumentation` | OpenAI / Azure OpenAI call traces. |
| `EnableSemanticKernelInstrumentation` | Semantic Kernel operation traces. |
| `EnableAgentFrameworkInstrumentation` | Agent Framework operation traces. |
| `EnableAgent365Instrumentation` | Agent365 manual scopes (InvokeAgent, ExecuteTool, etc.). |

Example — disable SQL instrumentation:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
    o.Instrumentation.EnableSqlClientInstrumentation = false;
});
```

### Agent365-only mode — infrastructure instrumentation suppressed by default

When Agent365 is the **only real exporter** (with or without Console), the distro automatically suppresses infrastructure instrumentation (ASP.NET Core, HttpClient, SQL Client, Azure SDK) because Agent365 only exports gen_ai/agent spans — infrastructure spans have nowhere to go and waste CPU.

This applies when:
- `o.Exporters = ExportTarget.Agent365` (with or without `| ExportTarget.Console`)
- Azure Monitor and OTLP are **not** enabled

The following instrumentations are disabled automatically:

| Instrumentation | Default (normal) | Agent365-only mode |
|---|---|---|
| ASP.NET Core | `true` | **`false`** |
| HttpClient | `true` | **`false`** |
| SQL Client | `true` | **`false`** |
| Azure SDK | `true` | **`false`** |
| OpenAI / Azure OpenAI | `true` | `true` (kept) |
| Semantic Kernel | `true` | `true` (kept) |
| Agent Framework | `true` | `true` (kept) |
| Agent365 scopes | `true` | `true` (kept) |

GenAI and agent instrumentation is **always kept** — only noisy infrastructure instrumentation is suppressed.

To re-enable specific instrumentations in Agent365-only mode, set them explicitly in the callback **before** they are suppressed:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365 | ExportTarget.Console;

    // Re-enable HttpClient instrumentation despite A365-only mode
    o.Instrumentation.EnableHttpClientInstrumentation = true;
});
```

> **Note:** When you add `ExportTarget.AzureMonitor` or `ExportTarget.Otlp`, all infrastructure instrumentation is enabled by default — no suppression occurs.

## Token resolver

When using the Agent 365 exporter, you must provide a token resolver function that returns an authentication token. The distro supports two approaches.

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
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
    o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
    {
        return await MyTokenService.GetTokenAsync(agentId, tenantId);
    };
});
```

If you set `TokenResolver` explicitly, the auto DI token cache is **not** registered — your resolver is used instead.

### ServiceTokenCache — simple token caching

For agents that acquire tokens outside the Agent Framework hosting pipeline (e.g., via MSAL directly), use `ServiceTokenCache`:

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Common;

var cache = new ServiceTokenCache(
    defaultExpiration: TimeSpan.FromMinutes(30),
    cleanupInterval: TimeSpan.FromMinutes(5));

cache.RegisterObservability(agentId, tenantId, bearerToken,
    EnvironmentUtils.GetObservabilityAuthenticationScope());

// Later:
var token = await cache.GetObservabilityToken(agentId, tenantId);
```

## Baggage attributes

Use `BaggageBuilder` to set contextual information that flows through all spans in a request. The SDK implements a `SpanProcessor` that copies all nonempty baggage entries to newly started spans without overwriting existing attributes.

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Common;

using var baggageScope = new BaggageBuilder()
    .TenantId("tenant-123")
    .AgentId("agent-456")
    .ConversationId("conv-789")
    .Build();
// Any spans started in this context will receive these as attributes.
```

### BaggageBuilder methods

| Method | Description |
|---|---|
| `.TenantId(string?)` | Set the tenant ID (required for export). |
| `.AgentId(string?)` | Set the agent ID (required for export). |
| `.AgentName(string?)` | Set the agent display name. |
| `.AgentDescription(string?)` | Set the agent description. |
| `.AgentVersion(string?)` | Set the agent version. |
| `.AgentBlueprintId(string?)` | Set the agent blueprint ID. |
| `.AgentPlatformId(string?)` | Set the agent platform ID. |
| `.AgenticUserId(string?)` | Set the agentic user ID. |
| `.AgenticUserEmail(string?)` | Set the agentic user email. |
| `.UserId(string?)` | Set the human user ID. |
| `.UserEmail(string?)` | Set the human user email. |
| `.UserName(string?)` | Set the human user name. |
| `.UserClientIp(IPAddress)` | Set the user's client IP address. |
| `.ConversationId(string?)` | Set the conversation ID. |
| `.ChannelName(string?)` | Set the channel name (e.g., `msteams`). |
| `.ChannelLink(string?)` | Set the channel link. |
| `.SessionId(string?)` | Set the session ID. |
| `.SessionDescription(string?)` | Set the session description. |
| `.ConversationItemLink(string?)` | Set the conversation item link. |
| `.InvokeAgentServer(string?, int?)` | Set the agent server address and port. |
| `.OperationSource(string)` | Set the operation source (SDK, Gateway, MCPServer). |
| `.Build()` | Build and return a disposable baggage scope. |

### Auto-populate from TurnContext

If your agent uses the hosting integration, use the `FromTurnContext` extension method to auto-extract caller, agent, tenant, channel, and conversation details:

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Hosting.Extensions;

using var baggageScope = new BaggageBuilder()
    .FromTurnContext(turnContext)
    .Build();
```

### Baggage middleware

Register baggage middleware to automatically populate baggage for every incoming request without calling `BaggageBuilder` manually.

**Turn-level middleware** — registers on the bot adapter:

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Middleware;

adapter.Use(new BaggageTurnMiddleware());
```

The middleware skips baggage setup for async replies (`ContinueConversation` events) to avoid overwriting baggage that the originating request already set.

**HTTP-level middleware** — for setting tenant and agent IDs before the Bot Framework pipeline runs:

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Middleware;

app.UseObservabilityRequestContext((httpContext) =>
{
    var tenantId = GetTenantIdFromContext(httpContext);
    var agentId = GetAgentIdFromContext(httpContext);
    return (tenantId, agentId);
});
```

## Auto-instrumentation

Auto-instrumentation automatically listens to agentic frameworks' existing telemetry signals and forwards them to Agent 365 observability service. This eliminates the need for manual monitoring code and ensures consistent telemetry capture.

### Supported frameworks (.NET)

| SDK / Framework | Support |
|---|---|
| Semantic Kernel | ✅ |
| OpenAI / Azure OpenAI | ✅ |
| Agent Framework | ✅ |

The distro's `UseMicrosoftOpenTelemetry()` call auto-registers all supported `ActivitySource` listeners:

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

All auto-instrumentation is enabled by default. To disable a specific framework, use `InstrumentationOptions`:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Instrumentation.EnableSemanticKernelInstrumentation = false;
});
```

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

## Manual instrumentation

Use Agent 365 observability scopes to capture detailed telemetry about your agent's internal operations. The SDK provides four scopes: `InvokeAgentScope`, `ExecuteToolScope`, `InferenceScope`, and `OutputScope`.

> **Important:** For successful store validation, your agent **must** implement `InvokeAgentScope`, `InferenceScope`, and `ExecuteToolScope`. These three scopes are required for publishing.

### Common contracts

All scopes share these contract types:

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

// Agent identity — reuse across all scopes in a request
var agentDetails = new AgentDetails(
    agentId: "agent-456",
    agentName: "My Agent",
    agentDescription: "An AI agent powered by Azure OpenAI",
    agenticUserId: "auid-123",
    agenticUserEmail: "agent@contoso.com",
    agentBlueprintId: "blueprint-789",
    tenantId: "tenant-123");

// Request context — reuse across all scopes in a request
var request = new Request(
    content: "User asks a question",
    sessionId: "session-42",
    conversationId: "conv-xyz",
    channel: new Channel(name: "msteams"));

// Caller identity (human user or calling agent)
var callerDetails = new CallerDetails(
    userDetails: new UserDetails(
        userId: "user-123",
        userEmail: "jane.doe@contoso.com",
        userName: "Jane Doe"));
```

### Agent invocation (`InvokeAgentScope`)

Use this scope at the start of your agent process to capture properties like the agent being invoked, user data, and input/output messages.

```csharp
var scopeDetails = new InvokeAgentScopeDetails(
    endpoint: new Uri("https://myagent.contoso.com"));

using var scope = InvokeAgentScope.Start(
    request: request,
    scopeDetails: scopeDetails,
    agentDetails: agentDetails,
    callerDetails: callerDetails);

// Record input messages
scope.RecordInputMessages(new[] { "User asks a question" });

// ... your agent logic here ...

// Record output messages
scope.RecordOutputMessages(new[] { "Here is the response." });
```

**Available methods:**

| Method | Description |
|---|---|
| `RecordInputMessages(string[])` | Record input messages. |
| `RecordInputMessages(InputMessages)` | Record structured input messages. |
| `RecordOutputMessages(string[])` | Record output messages. |
| `RecordOutputMessages(OutputMessages)` | Record structured output messages. |
| `RecordResponse(string)` | Record a single string response. |
| `RecordThreatDiagnosticsSummary(ThreatDiagnosticsSummary)` | Record threat diagnostics. |
| `GetActivityContext()` | Get the `ActivityContext` for parent-child linking. |

### Tool execution (`ExecuteToolScope`)

Track tool execution with telemetry for monitoring and auditing.

```csharp
var toolCallDetails = new ToolCallDetails(
    toolName: "summarize",
    arguments: "{\"text\": \"...\"}",
    toolCallId: "tc-001",
    description: "Summarize provided text",
    toolType: "function",
    endpoint: new Uri("https://tools.contoso.com:8080"));

using var scope = ExecuteToolScope.Start(
    request: request,
    details: toolCallDetails,
    agentDetails: agentDetails);

// ... your tool logic here ...

scope.RecordResponse("{\"summary\": \"The text was summarized.\"}");
```

**Available methods:**

| Method | Description |
|---|---|
| `RecordResponse(string)` | Record a string result. |
| `RecordResponse(IDictionary<string, object>)` | Record a structured result. |
| `RecordThreatDiagnosticsSummary(ThreatDiagnosticsSummary)` | Record threat diagnostics. |

### Inference (`InferenceScope`)

Instrument AI model inference calls to capture token usage, model details, and response metadata.

```csharp
var inferenceDetails = new InferenceCallDetails(
    operationName: InferenceOperationType.Chat,
    model: "gpt-4o-mini",
    providerName: "Azure OpenAI");

using var scope = InferenceScope.Start(
    request: request,
    details: inferenceDetails,
    agentDetails: agentDetails);

// ... your inference logic here ...

scope.RecordOutputMessages(new[] { "AI response message" });
scope.RecordInputTokens(123);
scope.RecordOutputTokens(456);
```

**Available methods:**

| Method | Description |
|---|---|
| `RecordInputMessages(string[])` | Record input messages sent to the model. |
| `RecordOutputMessages(string[])` | Record output messages from the model. |
| `RecordInputTokens(int)` | Record input token count. |
| `RecordOutputTokens(int)` | Record output token count. |
| `RecordFinishReasons(string[])` | Record finish reasons (e.g., `"stop"`). |
| `RecordThoughtProcess(string)` | Record chain-of-thought / reasoning. |

**`InferenceOperationType` values:** `Chat`, `TextCompletion`, `GenerateContent`

### Output (`OutputScope`)

Use this scope for asynchronous scenarios where `InvokeAgentScope`, `ExecuteToolScope`, or `InferenceScope` can't capture output data synchronously. Start `OutputScope` as a child span to record the final output messages after the parent scope finishes.

```csharp
// OutputScope is a child span — capture the parent context while InvokeAgentScope is still active.
// Example: save parentContext before disposing the InvokeAgentScope.
//   var parentContext = invokeScope.GetActivityContext();

var parentContext = savedParentContext; // ActivityContext from InvokeAgentScope.GetActivityContext()

var response = new Response(new[] { "Here is your organized inbox with 15 urgent emails." });

using var scope = OutputScope.Start(
    request: request,
    response: response,
    agentDetails: agentDetails,
    spanDetails: new SpanDetails(parentContext: parentContext));
// Output messages are recorded automatically from the response
```

## Environment variables

The distro recognizes these environment variables:

| Variable | Description |
|---|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Azure Monitor connection string. Auto-detected for `ExportTarget.AzureMonitor`. |
| `A365_OBSERVABILITY_SCOPE_OVERRIDE` | Override the auth scope for A365 token acquisition. |
| `A365_OBSERVABILITY_DOMAIN_OVERRIDE` | Override the A365 endpoint domain (for testing). |
| `OTEL_TRACES_SAMPLER` | OpenTelemetry trace sampler configuration. |
| `OTEL_TRACES_SAMPLER_ARG` | OpenTelemetry trace sampler argument. |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core environment detection. |
| `DOTNET_ENVIRONMENT` | .NET environment detection (fallback for `ASPNETCORE_ENVIRONMENT`). |

> **Note:** `ENABLE_A365_OBSERVABILITY_EXPORTER` is a Python/Node.js concept. In the .NET distro, the A365 exporter is controlled entirely through code via `ExportTarget.Agent365`.

## Validate locally

### Console + Agent365 (validate locally and remotely)

To validate telemetry locally while also sending to Agent365:

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

Send a message via Teams. In the console output, look for agent-related spans:

```
Activity.DisplayName:        chat gpt-*
Activity.DisplayName:        invoke_agent *
Activity.DisplayName:        MessageProcessor
```

> **Note:** The console exporter shows **all** spans — including HTTP, ASP.NET Core, and Azure SDK activity. The `gen_ai` spans listed above are the key ones to verify. You can filter console output by `Activity.Source` to focus on specific sources.

### Debugging export failures

Enable verbose logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.Agents.A365.Observability": "Debug"
    }
  }
}
```

Override environment variables for testing against custom endpoints:

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

### Verify successful export

When using `ExportTarget.Console | ExportTarget.Agent365`, look for successful HTTP responses:

```
Received HTTP response headers after *ms - 200
```

## Validate for store publishing

> **Important:** For successful store validation, your agent **must** implement the `InvokeAgentScope`, `InferenceScope`, and `ExecuteToolScope` scopes. These three scopes are required for publishing.

### `InvokeAgentScope` attributes

```json
{
    "error.type": "Optional",
    "microsoft.a365.agent.blueprint.id": "Required",
    "gen_ai.agent.description": "Optional",
    "gen_ai.agent.id": "Required",
    "gen_ai.agent.name": "Required",
    "microsoft.a365.agent.platform.id": "Optional",
    "microsoft.agent.user.email": "Required",
    "microsoft.agent.user.id": "Required",
    "gen_ai.agent.version": "Optional",
    "microsoft.a365.caller.agent.blueprint.id": "Optional",
    "microsoft.a365.caller.agent.id": "Optional",
    "microsoft.a365.caller.agent.name": "Optional",
    "microsoft.a365.caller.agent.platform.id": "Optional",
    "microsoft.a365.caller.agent.user.email": "Optional",
    "microsoft.a365.caller.agent.user.id": "Optional",
    "microsoft.a365.caller.agent.version": "Optional",
    "client.address": "Required",
    "user.id": "Required",
    "user.name": "Optional",
    "user.email": "Required",
    "microsoft.channel.link": "Optional",
    "microsoft.channel.name": "Required",
    "gen_ai.conversation.id": "Required",
    "microsoft.conversation.item.link": "Optional",
    "gen_ai.input.messages": "Required",
    "gen_ai.operation.name": "Required",
    "gen_ai.output.messages": "Required",
    "server.address": "Required",
    "server.port": "Required",
    "microsoft.session.id": "Optional",
    "microsoft.session.description": "Optional",
    "microsoft.tenant.id": "Required"
}
```

### `ExecuteToolScope` attributes

```json
{
    "error.type": "Optional",
    "microsoft.a365.agent.blueprint.id": "Required",
    "gen_ai.agent.description": "Optional",
    "gen_ai.agent.id": "Required",
    "gen_ai.agent.name": "Required",
    "microsoft.a365.agent.platform.id": "Optional",
    "microsoft.agent.user.email": "Required",
    "microsoft.agent.user.id": "Required",
    "gen_ai.agent.version": "Optional",
    "client.address": "Required",
    "user.id": "Required",
    "user.name": "Optional",
    "user.email": "Required",
    "microsoft.channel.link": "Optional",
    "microsoft.channel.name": "Required",
    "gen_ai.conversation.id": "Required",
    "microsoft.conversation.item.link": "Optional",
    "gen_ai.operation.name": "Required",
    "gen_ai.tool.call.arguments": "Required",
    "gen_ai.tool.call.id": "Required",
    "gen_ai.tool.call.result": "Required",
    "gen_ai.tool.description": "Optional",
    "gen_ai.tool.name": "Required",
    "gen_ai.tool.type": "Required",
    "server.address": "Optional",
    "server.port": "Optional",
    "microsoft.session.id": "Optional",
    "microsoft.session.description": "Optional",
    "microsoft.tenant.id": "Required"
}
```

### `InferenceScope` attributes

```json
{
    "error.type": "Optional",
    "microsoft.a365.agent.blueprint.id": "Required",
    "gen_ai.agent.description": "Optional",
    "gen_ai.agent.id": "Required",
    "gen_ai.agent.name": "Required",
    "microsoft.a365.agent.platform.id": "Optional",
    "microsoft.a365.agent.thought.process": "Optional",
    "microsoft.agent.user.email": "Required",
    "microsoft.agent.user.id": "Required",
    "gen_ai.agent.version": "Optional",
    "client.address": "Required",
    "user.id": "Required",
    "user.name": "Optional",
    "user.email": "Required",
    "microsoft.channel.link": "Optional",
    "microsoft.channel.name": "Required",
    "gen_ai.conversation.id": "Required",
    "microsoft.conversation.item.link": "Optional",
    "gen_ai.input.messages": "Required",
    "gen_ai.operation.name": "Required",
    "gen_ai.output.messages": "Required",
    "gen_ai.provider.name": "Required",
    "gen_ai.request.model": "Required",
    "gen_ai.response.finish_reasons": "Optional",
    "gen_ai.usage.input_tokens": "Optional",
    "gen_ai.usage.output_tokens": "Optional",
    "server.address": "Optional",
    "server.port": "Optional",
    "microsoft.session.description": "Optional",
    "microsoft.session.id": "Optional",
    "microsoft.tenant.id": "Required"
}
```

### `OutputScope` attributes

```json
{
    "microsoft.a365.agent.blueprint.id": "Required",
    "gen_ai.agent.description": "Optional",
    "gen_ai.agent.id": "Required",
    "gen_ai.agent.name": "Required",
    "microsoft.a365.agent.platform.id": "Optional",
    "microsoft.agent.user.email": "Required",
    "microsoft.agent.user.id": "Required",
    "gen_ai.agent.version": "Optional",
    "client.address": "Required",
    "user.id": "Required",
    "user.name": "Optional",
    "user.email": "Required",
    "microsoft.channel.link": "Optional",
    "microsoft.channel.name": "Required",
    "gen_ai.conversation.id": "Required",
    "microsoft.conversation.item.link": "Optional",
    "gen_ai.operation.name": "Required",
    "gen_ai.output.messages": "Required",
    "microsoft.session.id": "Optional",
    "microsoft.session.description": "Optional",
    "microsoft.tenant.id": "Required"
}
```

## Viewing exported logs

To view agent telemetry in Microsoft Purview or Microsoft Defender:

- **Microsoft Purview**: Auditing must be turned on for your organization. See [Turn auditing on or off](https://learn.microsoft.com/en-us/purview/audit-log-enable-disable#turn-on-auditing).
- **Microsoft Defender**: Advanced hunting must be configured to access the `CloudAppEvents` table. See [CloudAppEvents table](https://learn.microsoft.com/en-us/defender-xdr/advanced-hunting-cloudappevents-table).

## Test your agent with observability

After implementing observability, verify telemetry capture:

1. Go to: `https://admin.cloud.microsoft/#/agents/all`
2. Select your agent > Activity
3. You should see sessions and tool calls

## Troubleshooting

### Observability data doesn't appear

**Symptoms:** Agent is running, no telemetry in admin center, can't see agent activity.

**Solutions:**

- Verify `ExportTarget.Agent365` is set in `UseMicrosoftOpenTelemetry()`.
- Check token resolver configuration — the exporter requires a valid token.
- Enable verbose logging and check for observability-related errors.
- Add `ExportTarget.Console` and verify telemetry appears locally.

### Missing tenant ID or agent ID — spans skipped

**Symptoms:** Spans are silently dropped and never exported.

**Resolution:** Ensure `BaggageBuilder` is set up with tenant ID and agent ID before creating spans. These values propagate through the OpenTelemetry context and attach to all spans created within the baggage scope.

### Token resolution failure — export skipped or unauthorized

**Symptoms:** Token resolver returns `null` or throws an error.

**Resolution:**

- Verify that a token resolver is provided and returns a valid Bearer token.
- Ensure correct tenant ID and agent ID are used in `BaggageBuilder`.
- For Azure-hosted agents, verify the Managed Identity has the required API permission for the observability scope.

### HTTP 401 Unauthorized

**Resolution:**

- Verify the token audience matches the observability endpoint scope (default: `api://9b975845-388f-4429-889e-eab1ef63949c/Agent365.Observability.OtelWrite`).
- Ensure the identity has Agent 365 observability permissions.
- The `Agent365.Observability.OtelWrite` permission is required for `.NET 0.3-beta` and newer.

### HTTP 403 Forbidden

**Resolution:**

- Verify the tenant ID is in the Agent 365 allowed tenant list for trace ingestion.
- Confirm the agent identity has the required role or permission for the target tenant.

### HTTP 429 / 5xx — Transient errors

**Resolution:** These are usually transient. The SDK doesn't retry automatically. Consider reducing export frequency by adjusting `MaxExportBatchSize` or `ScheduledDelayMilliseconds` in exporter options.

### Export timeout

**Resolution:**

- Check network connectivity to the observability endpoint.
- Default HTTP request timeout is 30 seconds.
- Increase `ExporterTimeoutMilliseconds` if timeouts occur frequently.

### Export succeeds but telemetry doesn't appear in Defender or Purview

**Resolution:**

- Verify prerequisites for viewing exported logs (Purview auditing, Defender advanced hunting).
- Telemetry can take several minutes to populate after a successful export.

## Reference implementation

See: `examples/Microsoft.OpenTelemetry.Agent365.Demo/` in the distro repo.
