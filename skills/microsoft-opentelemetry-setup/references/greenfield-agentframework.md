# Agent Framework Greenfield Setup

## Setup

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
});

// Register agent, kernel, etc.
builder.AddAgent<MyAgent>();
```

## Auto-captured spans (no code needed)

| Span | Source |
|---|---|
| `invoke_agent <name>` | `Experimental.Microsoft.Agents.AI` |
| `chat <model>` | `Experimental.Microsoft.Agents.AI.ChatClient` |
| `execute_tool <fn>` | `Experimental.Microsoft.Agents.AI.Agent` |

## Auto-captured metrics

| Metric | Type |
|---|---|
| `gen_ai.client.operation.duration` | Histogram |
| `gen_ai.client.token.usage` | Histogram |

## Custom ActivitySource name

If you use a custom `sourceName` in `.UseOpenTelemetry(sourceName: "MyApp")`, you **must** register it with the distro:

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o => { o.Exporters = ExportTarget.Agent365; })
    .WithTracing(tracing => tracing.AddSource("MyApp"))
    .WithMetrics(metrics => metrics.AddMeter("MyApp"));
```

The distro only auto-registers default `Experimental.Microsoft.Agents.AI*` sources. Custom names need explicit `.AddSource()`.

## Instrumenting the ChatClient

```csharp
var chatClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "MyApp", configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

var agent = new ChatClientAgent(chatClient, name: "MyAgent", instructions: "...")
    .WithOpenTelemetry(sourceName: "MyApp", configure: cfg => cfg.EnableSensitiveData = true);
```
