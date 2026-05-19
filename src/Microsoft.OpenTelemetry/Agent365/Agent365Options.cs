// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Options for configuring Agent365 observability.
/// All exporter settings (token resolver, domain, S2S, batch settings) are available
/// directly on this class for convenience.
/// </summary>
public class Agent365Options
{
    /// <summary>
    /// Gets the underlying Agent365 exporter options instance. Internal storage for the
    /// flat properties exposed on this class; also used to register the singleton with DI.
    /// </summary>
    internal Agent365ExporterOptions ExporterOptions { get; } = new();

    /// <summary>
    /// Cluster region argument. Defaults to "production".
    /// </summary>
    public string ClusterCategory
    {
        get => ExporterOptions.ClusterCategory;
        set => ExporterOptions.ClusterCategory = value;
    }

    /// <summary>
    /// Async delegate used to resolve the auth token.
    /// Either this or <see cref="ContextualTokenResolver"/> must be set.
    /// When both are set, <see cref="ContextualTokenResolver"/> takes precedence.
    /// </summary>
    public AsyncAuthTokenResolver? TokenResolver
    {
        get => ExporterOptions.TokenResolver;
        set => ExporterOptions.TokenResolver = value;
    }

    /// <summary>
    /// Async delegate used to resolve the auth token with rich context including the agentic user ID.
    /// Takes precedence over <see cref="TokenResolver"/> when set.
    /// </summary>
    public AsyncContextualTokenResolver? ContextualTokenResolver
    {
        get => ExporterOptions.ContextualTokenResolver;
        set => ExporterOptions.ContextualTokenResolver = value;
    }

    /// <summary>
    /// Delegate used to resolve the endpoint host or URL for a given tenant id.
    /// Defaults to returning <see cref="Agent365ExporterOptions.DefaultEndpointHost"/>.
    /// </summary>
    public TenantDomainResolver DomainResolver
    {
        get => ExporterOptions.DomainResolver;
        set => ExporterOptions.DomainResolver = value;
    }

    /// <summary>
    /// When true, uses the service-to-service (S2S) endpoint path.
    /// When false (default), uses the standard endpoint path.
    /// Default is false.
    /// </summary>
    public bool UseS2SEndpoint
    {
        get => ExporterOptions.UseS2SEndpoint;
        set => ExporterOptions.UseS2SEndpoint = value;
    }

    /// <summary>
    /// Maximum queue size for the batch processor.
    /// Default is 2048.
    /// </summary>
    public int MaxQueueSize
    {
        get => ExporterOptions.MaxQueueSize;
        set => ExporterOptions.MaxQueueSize = value;
    }

    /// <summary>
    /// Delay in milliseconds between export batches.
    /// Default is 5000 (5 seconds).
    /// </summary>
    public int ScheduledDelayMilliseconds
    {
        get => ExporterOptions.ScheduledDelayMilliseconds;
        set => ExporterOptions.ScheduledDelayMilliseconds = value;
    }

    /// <summary>
    /// Timeout in milliseconds for the export operation.
    /// Default is 30000 (30 seconds).
    /// </summary>
    public int ExporterTimeoutMilliseconds
    {
        get => ExporterOptions.ExporterTimeoutMilliseconds;
        set => ExporterOptions.ExporterTimeoutMilliseconds = value;
    }

    /// <summary>
    /// Maximum batch size for export operations.
    /// Default is 512.
    /// </summary>
    public int MaxExportBatchSize
    {
        get => ExporterOptions.MaxExportBatchSize;
        set => ExporterOptions.MaxExportBatchSize = value;
    }

    /// <summary>
    /// Upper bound on HTTP request body size in bytes used by per-request payload chunking.
    /// Default is 900,000 bytes.
    /// </summary>
    public long MaxPayloadBytes
    {
        get => ExporterOptions.MaxPayloadBytes;
        set => ExporterOptions.MaxPayloadBytes = value;
    }

    /// <summary>
    /// When true, skips exporter registration. Instrumentation is still added.
    /// Used internally by <see cref="MicrosoftOpenTelemetryBuilderExtensions.UseMicrosoftOpenTelemetry"/>.
    /// </summary>
    internal bool SkipExporter { get; set; }
}
