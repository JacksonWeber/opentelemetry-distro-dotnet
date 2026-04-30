# ASP.NET Core Greenfield Setup

## Minimal

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

## With Agent 365 + Console (development)

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;

    if (builder.Environment.IsDevelopment())
    {
        o.Exporters |= ExportTarget.Console;
    }
});

var app = builder.Build();
app.Run();
```

## With custom ActivitySources (longer form)

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
    })
    .WithTracing(tracing => tracing.AddSource("MyCompany.MyAgent.CustomSource"));
```

## With ConfigureResource (service identity)

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService("my-agent", serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["service.namespace"] = "Microsoft.Agents"
        }))
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365 | ExportTarget.AzureMonitor;
    });
```

Chain `ConfigureResource()` **before** `UseMicrosoftOpenTelemetry()`. User attributes merge with distro auto-detected attributes (Azure VM, etc.).
