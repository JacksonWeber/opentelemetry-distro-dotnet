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
/// A fully isolated telemetry pipeline (traces, logs, and metrics) that exports to one specific
/// Azure Monitor resource. Multiple instances can coexist in the same process without clobbering
/// each other, and none of them is registered as the process-global provider.
/// </summary>
/// <remarks>
/// <para>
/// .NET has no process-global provider that every telemetry call routes through (tracing flows
/// through <see cref="ActivitySource"/> and <c>ActivityListener</c>), so a naive "one
/// <c>TracerProvider</c> per instance" fans out — a single <see cref="Activity"/> is delivered to
/// every provider listening to the source. This type prevents that by stamping each activity with
/// the owning instance id at start (<see cref="InstanceStampProcessor"/>) and gating export so each
/// instance only emits its own activities (<see cref="GatingActivityExportProcessor"/>).
/// </para>
/// <para>Two usage modes are supported:</para>
/// <list type="bullet">
///   <item>Global/shared <see cref="ActivitySource"/> routed by <see cref="BeginScope"/>.</item>
///   <item>Direct handles via <see cref="ActivitySource"/>, <see cref="Logger"/>, and <see cref="Meter"/>.</item>
/// </list>
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
        Id = name;
        var privateSourceName = $"{name}.Direct";

        ActivitySource = new ActivitySource(privateSourceName);
        Meter = new Meter($"{name}.Meter");

        // service.name -> Azure Monitor cloud role name, so each resource is clearly labelled.
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: name, serviceInstanceId: $"{name}-{Environment.MachineName}");

        // ── Traces ──────────────────────────────────────────────────────────────────────
        // Standalone TracerProvider (never set as the global default). Listens to this
        // instance's private source plus any shared sources used for ambient routing. The
        // stamp processor marks each activity with the owning instance id; the gating export
        // processor drops anything not belonging to this instance, so a shared ActivitySource
        // fans in to exactly one resource.
        var traceExporter = new AzureMonitorTraceExporter(new AzureMonitorExporterOptions
        {
            ConnectionString = connectionString,
        });

        var tracerBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .SetSampler(new AlwaysOnSampler())
            .AddSource(privateSourceName)
            .AddProcessor(new InstanceStampProcessor(Id, privateSourceName))
            .AddProcessor(new GatingActivityExportProcessor(Id, traceExporter, _ => Interlocked.Increment(ref _exportedSpanCount)));

        foreach (var source in sharedSources)
        {
            tracerBuilder.AddSource(source);
        }

        _tracerProvider = tracerBuilder.Build();

        // ── Metrics ─────────────────────────────────────────────────────────────────────
        // Private MeterProvider listens only to this instance's Meter, so no fan-out and no
        // gating is needed. (Meter measurements carry no ambient context, so ambient routing
        // does not apply — each instance owns its own Meter.)
        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter(Meter.Name)
            .AddAzureMonitorMetricExporter(o => o.ConnectionString = connectionString)
            .Build();

        // ── Logs ────────────────────────────────────────────────────────────────────────
        // Private LoggerFactory with its own OpenTelemetry logging pipeline and Azure Monitor
        // log exporter. The app logs through Logger, so records go only here.
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

    /// <summary>Gets an <see cref="ActivitySource"/> bound directly to this instance — spans from it always export here.</summary>
    public ActivitySource ActivitySource { get; }

    /// <summary>Gets a logger that writes only to this instance's Azure Monitor resource.</summary>
    public ILogger Logger { get; }

    /// <summary>Gets a <see cref="Meter"/> whose measurements are exported only to this instance's resource.</summary>
    public Meter Meter { get; }

    /// <summary>
    /// Gets the number of spans this instance has forwarded to its exporter — i.e. the spans that
    /// passed the instance gate. Used by tests to assert isolation.
    /// </summary>
    internal long ExportedSpanCount => Interlocked.Read(ref _exportedSpanCount);

    /// <summary>
    /// Binds this instance as the ambient "current instance" for the executing async flow. While
    /// the returned scope is active, spans created from any shared <see cref="ActivitySource"/>
    /// route to this instance's Azure Monitor resource.
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
