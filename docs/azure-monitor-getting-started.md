# Azure Monitor Observability (.NET)

> **Standalone guide** — covers everything you need to add Azure Monitor observability using the `Microsoft.OpenTelemetry` distro.

The Microsoft OpenTelemetry distro provides a unified framework for sending telemetry data to Azure Monitor (Application Insights) following the OpenTelemetry specification. This library instruments your ASP.NET Core applications to collect and send traces, metrics, and logs to Azure Monitor for analysis and monitoring.

## Key benefits

- **One-line onboarding**: A single `UseMicrosoftOpenTelemetry()` call configures traces, metrics, and logs with Azure Monitor exporters, resource detectors, and instrumentation libraries.
- **Built-in instrumentation**: ASP.NET Core, HttpClient, SQL Client, and Azure SDK instrumentation are included and enabled by default.
- **Live Metrics**: Integrated support for [Live Metrics](https://learn.microsoft.com/azure/azure-monitor/app/live-stream) enabling real-time monitoring of application performance.
- **Resource detection**: Automatic resource attributes for Azure App Service, Azure VMs, and Azure Container Apps.
- **Unified distro**: Combine Azure Monitor with other export targets (Agent365, OTLP, Console) in a single configuration.

## What is included in the distro

The Microsoft OpenTelemetry distro includes:

* **Traces**
  * **ASP.NET Core Instrumentation**: Automatic tracing for incoming HTTP requests.
  * **HTTP Client Instrumentation**: Automatic tracing for outgoing HTTP requests made using [System.Net.Http.HttpClient](https://learn.microsoft.com/dotnet/api/system.net.http.httpclient).
  * **SQL Client Instrumentation**: Automatic tracing for SQL queries executed using [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient) and [System.Data.SqlClient](https://www.nuget.org/packages/System.Data.SqlClient).
  * **Azure SDK Instrumentation**: Automatic tracing for Azure SDK client calls.

* **Metrics**
  * **Application Insights Standard Metrics**: Automatic collection of Application Insights standard metrics.
  * **ASP.NET Core and HTTP Client Metrics**: Built-in metrics from `Microsoft.AspNetCore.Hosting` and `System.Net.Http` on .NET 8.0+.

* **Logs**
  * Logs created with `Microsoft.Extensions.Logging`. See [Logging in .NET Core and ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/logging) for details.

* **Resource Detectors**
  * **AppServiceResourceDetector**: Resource attributes for Azure App Service.
  * **AzureVMResourceDetector**: Resource attributes for Azure Virtual Machines.
  * **AzureContainerAppsResourceDetector**: Resource attributes for Azure Container Apps.

* **[Live Metrics](https://learn.microsoft.com/azure/azure-monitor/app/live-stream)**: Real-time monitoring of application performance.

* **GenAI / Agent Auto-Instrumentation**
  * **Microsoft Agent Framework**: Automatic tracing for Agent Framework operations (`Experimental.Microsoft.Agents.AI`, `Experimental.Microsoft.Agents.AI.Agent`, `Experimental.Microsoft.Agents.AI.ChatClient`).
  * **Semantic Kernel**: Automatic tracing for Semantic Kernel operations (`Microsoft.SemanticKernel*`).
  * **OpenAI / Azure OpenAI**: Automatic tracing for OpenAI calls (`Azure.AI.OpenAI*`, `OpenAI.*`, `Experimental.Microsoft.Extensions.AI`).
  * **Agent365 Scopes**: Manual observability scopes (`Agent365Sdk`) for `InvokeAgent`, `ExecuteTool`, `Inference`, and `Output` operations.

> **Note:** GenAI and Agent instrumentation activity sources are always registered by the distro regardless of which export targets are enabled. This means traces from Semantic Kernel, OpenAI, and Agent Framework are captured and sent to Azure Monitor when `ExportTarget.AzureMonitor` is active.

## Prerequisites

- **Azure Subscription:** To use Azure services, including Azure Monitor, you'll need a subscription. If you do not have an existing Azure account, you may sign up for a [free trial](https://azure.microsoft.com/free/dotnet/) or use your [Visual Studio Subscription](https://visualstudio.microsoft.com/subscriptions/) benefits when you [create an account](https://azure.microsoft.com/account).
- **Azure Application Insights Connection String:** To send telemetry data to the monitoring service you'll need a connection string from Azure Application Insights. If you are not familiar with creating Azure resources, you may wish to follow the step-by-step guide for [Create an Application Insights resource](https://learn.microsoft.com/azure/azure-monitor/app/create-new-resource) and [copy the connection string](https://learn.microsoft.com/azure/azure-monitor/app/sdk-connection-string?tabs=net#find-your-connection-string).
- **ASP.NET Core App:** An ASP.NET Core application is required. You can either bring your own app or follow [Get started with ASP.NET Core MVC](https://learn.microsoft.com/aspnet/core/tutorials/first-mvc-app/start-mvc) to create a new one.

## Installation

Install the Microsoft OpenTelemetry Distro NuGet package. This single package includes the Azure Monitor exporter, auto-instrumentation for supported frameworks, resource detectors, and standard OpenTelemetry libraries.

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="<latest>" />
```

> **Note:** Add a `nuget.config` in your project root if the package is not available on the public NuGet feed. Check the [README](../README.md) for feed configuration.

## Choosing the right OpenTelemetry builder

.NET offers two ways to initialize OpenTelemetry. Choose the one that fits your application:

| Approach | DI/Host Integration | Multi-signal | Lifecycle | Best For |
|---|---|---|---|---|
| `builder.UseMicrosoftOpenTelemetry()` | ✅ Yes | ✅ Yes | Auto-managed | ASP.NET Core, Worker Services |
| `OpenTelemetrySdk.Create()` | ❌ No | ✅ Yes | Manual (`using`) | Console apps, background tools, non-hosted apps |

> **Recommendation from OpenTelemetry:** `OpenTelemetrySdk.Create()` is the recommended approach for non-hosted applications. It provides a unified, multi-signal setup under a single disposable boundary.

## Configuration

### Host-integrated (ASP.NET Core / Worker Services) — recommended

In `Program.cs`, call `UseMicrosoftOpenTelemetry()` to enable observability with Azure Monitor:

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

var app = builder.Build();
app.Run();
```

The connection string is auto-detected from the `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable. To set it explicitly:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
    o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
});
```

If you need to add your own application-specific activity sources, use the longer form:

```csharp
using Microsoft.OpenTelemetry;

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
    })
    .WithTracing(tracing => tracing
        .AddSource("MyCompany.MyProduct.MyLibrary"));
```

> ⚠️ **Note:** When `Exporters` is not explicitly set, Azure Monitor is auto-detected if a connection string is available (via code, environment variable, or `IConfiguration`). Setting `Exporters` explicitly overrides auto-detection.

### Non-hosted (Console apps, background tools) — `OpenTelemetrySdk.Create()`

For console applications and non-hosted scenarios that don't use the .NET Generic Host, use `OpenTelemetrySdk.Create()` with the distro's `UseMicrosoftOpenTelemetry()` extension:

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;

var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
        o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
    });
});

// Your application logic here — all telemetry is captured automatically.

// Keep the SDK alive for the lifetime of your application.
// Dispose only at shutdown — this flushes pending telemetry and shuts down all providers.
sdk.Dispose();
```

> **Important:** Do not dispose the SDK until your application is shutting down. Disposing it early (e.g., via a `using` block that exits too soon) stops all telemetry collection and export — you will lose data.

> **Note:** `UseMicrosoftOpenTelemetry()` works on any `IOpenTelemetryBuilder` — including the one provided by `OpenTelemetrySdk.Create()`. This gives you the same auto-instrumentation, resource detectors, and exporter configuration as the hosted approach.

You can also chain additional custom sources after the distro call:

```csharp
var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
    })
    .WithTracing(tracing => tracing
        .AddSource("MyCompany.MyApp.CustomSource"));
});
```

> **Note:** `OpenTelemetrySdk.Create()` manages the lifecycle of all providers. Dispose the returned object at application shutdown to flush pending telemetry.

### Export targets

The `ExportTarget` flags enum controls where telemetry is sent:

| Value | Description |
|---|---|
| `ExportTarget.None` | No exporters enabled. Distro-managed telemetry pipelines are disabled unless you configure your own providers separately. |
| `ExportTarget.Console` | Console output (development only). |
| `ExportTarget.Agent365` | Agent365 observability platform. |
| `ExportTarget.AzureMonitor` | Application Insights (auto-detected via `APPLICATIONINSIGHTS_CONNECTION_STRING`). |
| `ExportTarget.Otlp` | OpenTelemetry Protocol export. |

Combine targets with `|`: `ExportTarget.AzureMonitor | ExportTarget.Console`.

### Azure Monitor options

Configure Azure Monitor behavior via `o.AzureMonitor`:

| Property | Type | Description | Default |
|---|---|---|---|
| `ConnectionString` | `string?` | Azure Application Insights connection string. Auto-detected from `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable. | `null` |
| `Credential` | `TokenCredential?` | AAD token credential for authentication. When not set, uses instrumentation key from connection string. | `null` |
| `SamplingRatio` | `float` | Ratio of telemetry to sample (0.0–1.0). | `1.0` |
| `TracesPerSecond` | `double?` | Rate limit for traces per second. Takes precedence over `SamplingRatio` when set. | `5.0` |
| `EnableLiveMetrics` | `bool` | Enable [Live Metrics](https://learn.microsoft.com/azure/azure-monitor/app/live-stream) for real-time monitoring. | `true` |
| `EnableStandardMetrics` | `bool` | Enable Application Insights standard metrics collection. | `true` |
| `EnablePerfCounters` | `bool` | Enable performance counter collection. | `true` |
| `EnableTraceBasedLogsSampler` | `bool` | When enabled, only logs associated with sampled traces are exported. Logs without trace context are always exported. | `true` |
| `DisableOfflineStorage` | `bool` | Disable offline storage for telemetry. | `false` |
| `StorageDirectory` | `string?` | Override the default directory for offline storage. | `null` |

> **Sending to more than one Application Insights component?** A single application can route each
> telemetry item to a different component. See [Multi-Endpoint Routing](multi-endpoint-routing.md)
> (preview). Several of the options above behave differently while routing is enabled.

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
    o.Exporters = ExportTarget.AzureMonitor;
    o.Instrumentation.EnableSqlClientInstrumentation = false;
});
```

## Authenticate the client

Azure Active Directory (AAD) authentication is an optional feature. To enable AAD authentication, set the `Credential` property in `AzureMonitorOptions`. This is made easy with the [Azure Identity library](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/identity/Azure.Identity/README.md):

```csharp
using Azure.Identity;

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
    o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
    o.AzureMonitor.Credential = new DefaultAzureCredential();
});
```

With this configuration, the distro uses the credentials of the currently logged-in user or of the service principal to authenticate and send telemetry data to Azure Monitor.

If `Credential` is not set, the instrumentation key from the connection string is used instead.

## Advanced configuration

### Customizing sampling behavior

The distro uses **rate-limited sampling** by default, collecting up to **5.0 traces per second**.

**Option 1: Set the rate-limited sampler to a configured traces per second**

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
    o.AzureMonitor.TracesPerSecond = 10.0; // Collect up to 10 traces per second
});
```

**Option 2: Switch to percentage-based sampling**

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
    o.AzureMonitor.SamplingRatio = 0.5F; // Sample 50% of traces
    o.AzureMonitor.TracesPerSecond = null; // Disable rate-limited sampling
});
```

**Option 3: Use environment variables**

For rate-limited sampling:

```
OTEL_TRACES_SAMPLER=microsoft.rate_limited
OTEL_TRACES_SAMPLER_ARG=10
```

For percentage-based sampling:

```
OTEL_TRACES_SAMPLER=microsoft.fixed_percentage
OTEL_TRACES_SAMPLER_ARG=0.5
```

> **Note:** When both `TracesPerSecond` and `SamplingRatio` are configured, `TracesPerSecond` takes precedence.

### Adding custom ActivitySource to traces

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});
builder.Services.ConfigureOpenTelemetryTracerProvider((sp, builder) =>
    builder.AddSource("MyCompany.MyProduct.MyLibrary"));
```

### Adding custom Meter to metrics

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});
builder.Services.ConfigureOpenTelemetryMeterProvider((sp, builder) =>
    builder.AddMeter("MyCompany.MyProduct.MyLibrary"));
```

### Adding additional instrumentation

If you need to instrument a library or framework that isn't included in the distro, add additional instrumentation using OpenTelemetry instrumentation packages. For example, to add instrumentation for gRPC clients, install [OpenTelemetry.Instrumentation.GrpcNetClient](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.GrpcNetClient/):

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});
builder.Services.ConfigureOpenTelemetryTracerProvider((sp, builder) =>
    builder.AddGrpcClientInstrumentation());
```

### Disable Live Metrics

To disable the Live Metrics feature:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
    o.AzureMonitor.EnableLiveMetrics = false;
});
```

### Drop a metrics instrument

To exclude specific instruments from being collected:

```csharp
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
    metrics.AddView(instrumentName: "http.server.active_requests", MetricStreamConfiguration.Drop));
```

Refer to [Drop an instrument](https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/docs/metrics/customizing-the-sdk#drop-an-instrument) for more examples.

### Customizing instrumentation libraries

The distro includes .NET OpenTelemetry instrumentation for [ASP.NET Core](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.AspNetCore/), [HttpClient](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Http/), and [SQLClient](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.SqlClient). You can customize these or manually add additional instrumentation using the OpenTelemetry API.

#### Customizing AspNetCoreTraceInstrumentationOptions

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});
builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
{
    options.RecordException = true;
    options.Filter = (httpContext) =>
    {
        // only collect telemetry about HTTP GET requests
        return HttpMethods.IsGet(httpContext.Request.Method);
    };
});
```

#### Customizing HttpClientTraceInstrumentationOptions

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});
builder.Services.Configure<HttpClientTraceInstrumentationOptions>(options =>
{
    options.RecordException = true;
    options.FilterHttpRequestMessage = (httpRequestMessage) =>
    {
        // only collect telemetry about HTTP GET requests
        return HttpMethods.IsGet(httpRequestMessage.Method.Method);
    };
});
```

#### Customizing SqlClientInstrumentationOptions

While the [SQLClient](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.SqlClient) instrumentation is still in beta, it is vendored within the package. Once it reaches a stable release, it will be included as a standard package reference. Until then, for customization, manually add the package reference:

```
dotnet add package --prerelease OpenTelemetry.Instrumentation.SqlClient
```

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
    })
    .WithTracing(tracing =>
    {
        tracing.AddSqlClientInstrumentation(options =>
        {
            options.SetDbStatementForStoredProcedure = false;
        });
    });
```

## Environment variables

The distro recognizes these environment variables for Azure Monitor:

| Variable | Description |
|---|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Azure Monitor connection string. Auto-detected for `ExportTarget.AzureMonitor`. |
| `OTEL_TRACES_SAMPLER` | OpenTelemetry trace sampler configuration (`microsoft.rate_limited` or `microsoft.fixed_percentage`). |
| `OTEL_TRACES_SAMPLER_ARG` | OpenTelemetry trace sampler argument (rate or ratio value). |

## Examples

### Log scopes

Log [scopes](https://learn.microsoft.com/dotnet/core/extensions/logging#log-scopes) allow you to add additional properties to the logs generated by your application. Although the distro does support scopes, this feature is off by default in OpenTelemetry. To leverage log scopes, you must explicitly enable them:

```csharp
builder.Services.Configure<OpenTelemetryLoggerOptions>((loggingOptions) =>
{
    loggingOptions.IncludeScopes = true;
});
```

When using `ILogger` scopes, use a `List<KeyValuePair<string, object?>>` for best performance. All logs written within the context of the scope will include the specified information. Azure Monitor adds these scope values to the Log's CustomProperties:

```csharp
List<KeyValuePair<string, object?>> scope =
[
    new("scopeKey", "scopeValue")
];

using (logger.BeginScope(scope))
{
    logger.LogInformation("Example message.");
}
```

In scenarios involving multiple scopes or a single scope with multiple key-value pairs, if duplicate keys are present, only the first occurrence from the outermost scope will be recorded. When the same key is used both within a logging scope and directly in the log statement, the value in the log message template takes precedence.

### Custom events

Azure Monitor relies on OpenTelemetry's Log Signal to create CustomEvents. To send a CustomEvent via ILogger, include the `"microsoft.custom_event.name"` attribute in the message template:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

var app = builder.Build();

app.Logger.LogInformation("{microsoft.custom_event.name} {key1} {key2}", "MyCustomEventName", "value1", "value2");
```

This generates a CustomEvent structured like this:

```json
{
    "name": "Event",
    "data": {
        "baseType": "EventData",
        "baseData": {
            "name": "MyCustomEventName",
            "properties": {
                "key1": "value1",
                "key2": "value2"
            }
        }
    }
}
```

> **Note:** Severity and CategoryName are not recorded in the CustomEvent. Any `ILogger.Log` method can be used. Users should take care to select a severity that is not filtered out by their configuration.

## Validate locally

### Console + Azure Monitor (validate locally and remotely)

To validate telemetry locally while also sending to Azure Monitor:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console | ExportTarget.AzureMonitor;
    o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
});
```

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run
```

In the console output, look for spans related to your application:

```
Activity.DisplayName:        GET /api/...
Activity.Source:              Microsoft.AspNetCore
```

> **Note:** The console exporter shows **all** spans — including HTTP, ASP.NET Core, and Azure SDK activity. You can filter console output by `Activity.Source` to focus on specific sources.

## Troubleshooting

The distro uses EventSource for its own internal logging. The logs are available to any EventListener by opting into the source named `OpenTelemetry-AzureMonitor-Exporter`.

OpenTelemetry also provides its own [self-diagnostics feature](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/README.md#troubleshooting) to collect internal logs.

### Missing request telemetry

If an app has a reference to the [OpenTelemetry.Instrumentation.AspNetCore](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.AspNetCore) package, it could be missing request telemetry. To resolve:

- Remove the reference to the `OpenTelemetry.Instrumentation.AspNetCore` package, **or**
- Add `AddAspNetCoreInstrumentation` to the TracerProvider configuration as per the [OpenTelemetry documentation](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.AspNetCore).

### Missing dependency telemetry

If an app references the [OpenTelemetry.Instrumentation.Http](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Http) or [OpenTelemetry.Instrumentation.SqlClient](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.SqlClient) packages, it might be missing dependency telemetry. To resolve:

- Remove the respective package references, **or**
- Add `AddHttpClientInstrumentation` or `AddSqlClientInstrumentation` to the TracerProvider configuration. See the OpenTelemetry documentation for [HTTP](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.Http) and [SQL Client](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.SqlClient).

> **Note:** If all telemetries are missing or if the above troubleshooting steps do not help, please collect [self-diagnostics logs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/README.md#troubleshooting).
