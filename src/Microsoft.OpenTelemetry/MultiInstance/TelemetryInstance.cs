// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.OpenTelemetry.MultiInstance;

/// <summary>
/// A fully isolated telemetry pipeline (traces, logs, and metrics) that exports to one Azure
/// Monitor resource. Multiple instances coexist in one process without clobbering each other, and
/// none is registered as the process-global provider.
/// </summary>
/// <remarks>
/// Because an <see cref="Activity"/> is a single shared object observed by every listener, a plain
/// provider-per-instance fans out. This type stamps each activity with its owning instance id at
/// start (<see cref="InstanceStampProcessor"/>) and gates export to that id
/// (<see cref="GatingActivityExportProcessor"/>). Spans route via <see cref="BeginScope"/> (shared
/// source) or via the direct <see cref="ActivitySource"/>, <see cref="Logger"/>, and <see cref="Meter"/>.
/// </remarks>
public sealed class TelemetryInstance : IDisposable
{
    private readonly TracerProvider _tracerProvider;
    private readonly MeterProvider _meterProvider;
    private readonly ILoggerFactory _loggerFactory;
    private long _exportedSpanCount;
    private bool _disposed;

    internal TelemetryInstance(string name, string connectionString, IReadOnlyList<string> sharedSources)
    {
        Name = name;

        // Unique id so two instances with the same name stay isolated (the id is what gets stamped
        // and gated). The private source name embeds it so direct-handle spans never collide.
        Id = Guid.NewGuid().ToString("N");
        var privateSourceName = $"{name}.Direct.{Id}";

        ActivitySource = new ActivitySource(privateSourceName);
        Meter = new Meter($"{name}.Meter.{Id}");

        // service.name -> Azure Monitor cloud role name.
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: name, serviceInstanceId: $"{name}-{Environment.MachineName}");

        // Traces: standalone provider (never the global default). Stamp marks the owning instance;
        // the gating processor drops activities belonging to other instances.
        var traceExporter = new AzureMonitorTraceExporter(new AzureMonitorExporterOptions
        {
            ConnectionString = connectionString,
        });

        var tracerBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource(privateSourceName)
            .AddProcessor(new InstanceStampProcessor(Id, privateSourceName))
            .AddProcessor(new GatingActivityExportProcessor(Id, traceExporter, _ => Interlocked.Increment(ref _exportedSpanCount)));

        foreach (var source in sharedSources)
        {
            tracerBuilder.AddSource(source);
        }

        _tracerProvider = tracerBuilder.Build();

        // Metrics: private MeterProvider listens only to this instance's Meter, so no gating needed.
        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter(Meter.Name)
            .AddAzureMonitorMetricExporter(o => o.ConnectionString = connectionString)
            .Build();

        // Logs: private LoggerFactory with its own Azure Monitor log exporter.
        _loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddOpenTelemetry(otel =>
            {
                otel.SetResourceBuilder(resourceBuilder);
                otel.IncludeFormattedMessage = true;
                otel.IncludeScopes = true;
                otel.AddAzureMonitorLogExporter(o => o.ConnectionString = connectionString);
            });
        });

        Logger = _loggerFactory.CreateLogger(name);
    }

    /// <summary>Gets the friendly name; also used as the Azure Monitor cloud role name.</summary>
    public string Name { get; }

    /// <summary>Gets the stable id used to route telemetry to this instance.</summary>
    internal string Id { get; }

    /// <summary>Gets an <see cref="ActivitySource"/> bound directly to this instance.</summary>
    public ActivitySource ActivitySource { get; }

    /// <summary>Gets a logger that writes only to this instance's Azure Monitor resource.</summary>
    public ILogger Logger { get; }

    /// <summary>Gets a <see cref="Meter"/> whose measurements are exported only to this instance's resource.</summary>
    public Meter Meter { get; }

    /// <summary>Gets the number of spans this instance forwarded to its exporter. Used by tests to assert isolation.</summary>
    internal long ExportedSpanCount => Interlocked.Read(ref _exportedSpanCount);

    /// <summary>
    /// Binds this instance as the current instance for the executing async flow. While the returned
    /// scope is active, spans from any shared <see cref="ActivitySource"/> route to this instance.
    /// </summary>
    /// <returns>A scope that unbinds this instance when disposed.</returns>
    public IDisposable BeginScope() => AmbientInstance.Use(Id);

    /// <summary>Flushes this instance's traces and metrics. Logs are flushed on <see cref="Dispose"/>.</summary>
    /// <param name="timeoutMilliseconds">Maximum time to wait for the flush to complete.</param>
    public void ForceFlush(int timeoutMilliseconds = 10000)
    {
        _tracerProvider.ForceFlush(timeoutMilliseconds);
        _meterProvider.ForceFlush(timeoutMilliseconds);
    }

    /// <summary>Flushes and tears down this instance's traces, metrics, and logs.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ActivitySource.Dispose();
        Meter.Dispose();
        _tracerProvider.Dispose();
        _meterProvider.Dispose();
        _loggerFactory.Dispose(); // flushes and drains the logging pipeline
    }
}
