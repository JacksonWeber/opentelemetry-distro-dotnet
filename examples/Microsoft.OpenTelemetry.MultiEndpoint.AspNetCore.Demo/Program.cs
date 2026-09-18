// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.OpenTelemetry;
using MultiEndpointDemo;
using MultiEndpointDemo.Routing;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// The routing switch is set in the project file, so it is applied before any of this runs.

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("MultiEndpointDemo"));
builder.Services.AddSingleton<CustomerCatalog>();
builder.Services.AddSingleton<ICustomerRouting, CustomerRouting>();
builder.Services.AddSingleton<OrderMetrics>();
builder.Services.AddTransient<RoutingMetricsHandler>();

// Applies to every client the factory creates. A client built with `new HttpClient()` bypasses the
// factory, so its metrics carry no destination and are dropped.
builder.Services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler<RoutingMetricsHandler>());
builder.Services.AddHttpClient("downstream");

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        // Required: a routing-only application has no connection string of its own, so nothing
        // auto-selects Azure Monitor. Leave o.AzureMonitor.Credential unset - routing and
        // Microsoft Entra ID authentication cannot be combined.
        o.Exporters = ExportTarget.AzureMonitor;

        // Routing disables these anyway; turning them off avoids collecting discarded telemetry.
        o.AzureMonitor.EnableLiveMetrics = false;
        o.AzureMonitor.EnableStandardMetrics = false;
        o.AzureMonitor.EnablePerfCounters = false;

        // Rate-limited sampling is ignored under routing, because the limit is per process and
        // would be shared across every customer. Set it to null and choose a ratio.
        o.AzureMonitor.TracesPerSecond = null;
        o.AzureMonitor.SamplingRatio = 1.0F;

        // Nothing in this sample produces SQL or Azure SDK spans, and telemetry that is collected
        // but carries no destination is dropped after the work of collecting it.
        o.Instrumentation.EnableSqlClientInstrumentation = false;
        o.Instrumentation.EnableAzureSdkInstrumentation = false;
    })
    .WithTracing(tracing => tracing
        .AddSource(TelemetryNames.ActivitySource)
        .AddProcessor(sp => new RoutingActivityProcessor(sp.GetRequiredService<ICustomerRouting>())))
    .WithLogging(logging => logging
        .AddProcessor(sp => new RoutingLogProcessor(sp.GetRequiredService<ICustomerRouting>())))
    .WithMetrics(metrics => metrics
        .AddMeter(TelemetryNames.Meter));

var app = builder.Build();

app.UseMiddleware<CustomerContextMiddleware>();

// Routed: everything this endpoint produces carries the caller's destination.
app.MapGet("/orders", async (
    HttpContext context,
    OrderMetrics metrics,
    IHttpClientFactory clientFactory,
    IServer server,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    // Resolved once by the middleware, after the caller was authorized.
    if (context.Items[TelemetryNames.DestinationItem] is not RoutingDestination destination
        || context.Items[TelemetryNames.CustomerIdTag] is not string customerId)
    {
        return Results.Unauthorized();
    }

    var orderId = Guid.NewGuid().ToString("N")[..8];

    using var activity = DemoTelemetry.Source.StartActivity("place.order");
    activity?.SetTag("contoso.order_id", orderId);

    // A child activity that never sees the customer identifier itself: the routing processor finds
    // it by walking up to the request activity.
    //
    // The address comes from the server, not from context.Request.Host: the Host header is supplied
    // by the caller, so building an outbound URI from it would let a request point this call at any
    // host it liked.
    var baseAddress = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();

    if (baseAddress is null)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    var client = clientFactory.CreateClient("downstream");
    using var response = await client.GetAsync(new Uri(new Uri(baseAddress), "/health"), cancellationToken);

    // An ordinary ILogger call: the log processor attaches the destination.
    logger.LogInformation("Order {OrderId} accepted for {CustomerId}.", orderId, customerId);

    metrics.OrderPlaced(destination, Random.Shared.Next(20, 500), region: "emea");

    return Results.Ok(new { orderId, customerId, downstream = (int)response.StatusCode });
});

// Not routed: no caller, so no destination. Its telemetry is dropped rather than sent anywhere.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
