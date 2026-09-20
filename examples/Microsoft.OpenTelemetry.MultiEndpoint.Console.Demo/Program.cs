// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

const string SourceName = "Microsoft.OpenTelemetry.MultiEndpoint.Console.Demo";
const string RouteConnectionStringsVariable = "MULTIENDPOINT_ROUTE_CONNECTION_STRINGS";
const string InstrumentationKeyAttribute = "microsoft.instrumentation_key";
const string IngestionEndpointAttribute = "microsoft.ingestion_endpoint";
const string CloudRoleAttribute = "microsoft.multi_endpoint_cloud_role";

// This switch must be set before constructing the Azure Monitor exporter.
AppContext.SetSwitch("Azure.Monitor.OpenTelemetry.EnableMultiEndpointRouting", true);

var destinations = ParseConnectionStrings(
    Environment.GetEnvironmentVariable(RouteConnectionStringsVariable));

if (destinations.Count < 2)
{
    Console.Error.WriteLine(
        $"Set {RouteConnectionStringsVariable} to a comma-separated list of at least two " +
        "Application Insights connection strings containing InstrumentationKey and IngestionEndpoint.");
    return;
}

using var activitySource = new ActivitySource(SourceName);
using var meter = new Meter(SourceName);
var requestCounter = meter.CreateCounter<long>("demo.requests");
var requestDuration = meter.CreateHistogram<double>("demo.request.duration", "ms");

var sdk = OpenTelemetrySdk.Create(builder =>
{
    builder
        .UseMicrosoftOpenTelemetry(options =>
        {
            options.Exporters = ExportTarget.AzureMonitor;

            // These process-wide signals have no single owner in multi-endpoint mode.
            options.AzureMonitor.EnableLiveMetrics = false;
            options.AzureMonitor.EnableStandardMetrics = false;
            options.AzureMonitor.EnablePerfCounters = false;
            options.AzureMonitor.TracesPerSecond = null;
            options.AzureMonitor.SamplingRatio = 1.0F;
        })
        .WithTracing(tracing => tracing.AddSource(SourceName))
        .WithMetrics(metrics => metrics.AddMeter(SourceName));
});

var logger = sdk.GetLoggerFactory().CreateLogger(SourceName);

foreach (var destination in destinations)
{
    using (var activity = activitySource.StartActivity("process.demo.request"))
    {
        activity?.SetTag(InstrumentationKeyAttribute, destination.InstrumentationKey);
        activity?.SetTag(IngestionEndpointAttribute, destination.IngestionEndpoint);
        activity?.SetTag(CloudRoleAttribute, destination.CloudRole);
        activity?.SetTag("demo.signal", "trace");
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    var logAttributes = new List<KeyValuePair<string, object?>>
    {
        new(InstrumentationKeyAttribute, destination.InstrumentationKey),
        new(IngestionEndpointAttribute, destination.IngestionEndpoint),
        new(CloudRoleAttribute, destination.CloudRole),
        new("demo.signal", "log"),
    };

    logger.Log(
        LogLevel.Information,
        new EventId(1, "MultiEndpointDemo"),
        logAttributes,
        exception: null,
        (_, _) => $"Hello from {destination.CloudRole}");

    var metricTags = new TagList
    {
        { InstrumentationKeyAttribute, destination.InstrumentationKey },
        { IngestionEndpointAttribute, destination.IngestionEndpoint },
        { CloudRoleAttribute, destination.CloudRole },
        { "demo.signal", "metric" },
    };

    requestCounter.Add(1, metricTags);
    requestDuration.Record(Random.Shared.Next(10, 100), metricTags);
}

sdk.TracerProvider?.ForceFlush(timeoutMilliseconds: 10000);
sdk.LoggerProvider?.ForceFlush(timeoutMilliseconds: 10000);
sdk.MeterProvider?.ForceFlush(timeoutMilliseconds: 10000);
sdk.Dispose();

Console.WriteLine("Sent sample traces, logs, and metrics to both Application Insights endpoints.");

static List<(string InstrumentationKey, string IngestionEndpoint, string CloudRole)> ParseConnectionStrings(
    string? connectionStrings)
{
    var destinations = new List<(string, string, string)>();
    if (string.IsNullOrWhiteSpace(connectionStrings))
    {
        return destinations;
    }

    foreach (var connectionString in connectionStrings.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var values = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(
                part => part[0].Trim(),
                part => part[1].Trim(),
                StringComparer.OrdinalIgnoreCase);

        if (values.TryGetValue("InstrumentationKey", out var instrumentationKey)
            && values.TryGetValue("IngestionEndpoint", out var ingestionEndpoint))
        {
            destinations.Add((
                instrumentationKey,
                ingestionEndpoint,
                $"multi-endpoint-demo-{destinations.Count + 1}"));
        }
    }

    return destinations;
}
