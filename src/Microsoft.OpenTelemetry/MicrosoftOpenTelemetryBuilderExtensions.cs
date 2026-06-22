// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenTelemetry.AzureMonitor.Internals;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using global::OpenTelemetry.Metrics;
using global::OpenTelemetry.Logs;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Unified extension methods for configuring the Microsoft OpenTelemetry distro.
/// </summary>
public static class MicrosoftOpenTelemetryBuilderExtensions
{
    /// <summary>
    /// Configures the Microsoft OpenTelemetry distro on an <see cref="IHostApplicationBuilder"/>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Callback to configure <see cref="MicrosoftOpenTelemetryOptions"/>.</param>
    /// <returns>The supplied <typeparamref name="TBuilder"/> for chaining calls.</returns>
    /// <remarks>
    /// <para>This is the recommended entry point for ASP.NET Core and Worker Service apps.</para>
    /// <para>Usage:</para>
    /// <code>
    /// builder.UseMicrosoftOpenTelemetry(o =>
    /// {
    ///     o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
    ///     o.Agent365.TokenResolver = (agentId, tenantId) => GetTokenAsync(agentId, tenantId);
    ///     o.OtlpEndpoint = new Uri("http://localhost:4317");
    /// });
    /// </code>
    /// </remarks>
    public static TBuilder UseMicrosoftOpenTelemetry<TBuilder>(
        this TBuilder builder,
        Action<MicrosoftOpenTelemetryOptions> configure) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenTelemetry().UseMicrosoftOpenTelemetry(configure);
        return builder;
    }

    /// <summary>
    /// Configures the Microsoft OpenTelemetry distro with default settings.
    /// Exporters are auto-detected from environment variables and IConfiguration.
    /// </summary>
    public static TBuilder UseMicrosoftOpenTelemetry<TBuilder>(
        this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        return builder.UseMicrosoftOpenTelemetry(o => { });
    }
    /// <summary>
    /// Configures the Microsoft OpenTelemetry distro with Azure Monitor, Agent365,
    /// Microsoft Agent Framework, and shared exporters (OTLP, Console) in a single call.
    /// </summary>
    /// <param name="builder">The OpenTelemetry builder.</param>
    /// <param name="configure">Callback to configure <see cref="MicrosoftOpenTelemetryOptions"/>.</param>
    /// <returns>The supplied <see cref="IOpenTelemetryBuilder"/> for chaining calls.</returns>
    /// <remarks>
    /// <para>This is the recommended single entry point for the Microsoft OpenTelemetry distro.</para>
    /// <para>Usage:</para>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .UseMicrosoftOpenTelemetry(o =>
    ///     {
    ///         // Select exporters
    ///         o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Agent365;
    ///
    ///         // Azure Monitor
    ///         o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
    ///
    ///         // Agent365
    ///         o.Agent365.TokenResolver = (agentId, tenantId) => GetTokenAsync(agentId, tenantId);
    ///     });
    /// </code>
    /// </remarks>
    public static IOpenTelemetryBuilder UseMicrosoftOpenTelemetry(
        this IOpenTelemetryBuilder builder,
        Action<MicrosoftOpenTelemetryOptions> configure)
    {
        // Guard against duplicate calls
        if (builder.Services.Any(s => s.ImplementationInstance is UseMicrosoftOpenTelemetryRegistration))
        {
            throw new NotSupportedException(
                "Multiple calls to UseMicrosoftOpenTelemetry on the same IServiceCollection are not supported.");
        }

        builder.Services.AddSingleton(UseMicrosoftOpenTelemetryRegistration.Instance);

        // Route SDK statistics emitted in this process to the distro-owned ingestion
        // endpoints instead of the legacy internal Application Insights resources. The
        // switch is honored by Azure.Monitor.OpenTelemetry.Exporter's SDK Stats subsystem
        // and applies to every AzureMonitorMetricExporter constructed after this point —
        // including the customer's own exporter when running under the distro. Must be set
        // before any exporter is constructed (the ctor triggers SDK statistics init).
        AppContext.SetSwitch(
            "Azure.Monitor.OpenTelemetry.Exporter.RouteSdkStatsToDistroEndpoint",
            true);

        var options = new MicrosoftOpenTelemetryOptions();
        configure(options);

        // Auto-detect exporters from configuration if not explicitly set
        var exporters = options.Exporters;
        if (!options.ExportersExplicitlySet)
        {
            exporters = ExportTarget.None;

            // Check code-provided, IConfiguration, and raw env var for connection string
            if (!string.IsNullOrWhiteSpace(options.AzureMonitor.ConnectionString)
                || HasAzureMonitorConnectionString(builder.Services))
                exporters |= ExportTarget.AzureMonitor;
            if (options.Agent365.TokenResolver != null || options.Agent365.ContextualTokenResolver != null)
                exporters |= ExportTarget.Agent365;
            
        }

        // When A365 is the only real exporter (with or without Console), suppress noisy
        // infrastructure instrumentation (HTTP, ASP.NET, SQL, Azure SDK) by default.
        // A365 only exports gen_ai/agent spans, so creating Activities for infrastructure
        // spans wastes CPU. Console in this mode also only shows traces.
        // NOTE: This is scoped to built-in exporters selected via ExportTarget. If a caller
        // chains additional exporters directly on the returned builder, those are not considered.
        // Users can re-enable individual instrumentations by setting them in the callback:
        //   o.Instrumentation.EnableHttpClientInstrumentation = true;
        var a365OnlyMode = exporters.HasFlag(ExportTarget.Agent365)
                        && !exporters.HasFlag(ExportTarget.AzureMonitor)
                        && !exporters.HasFlag(ExportTarget.Otlp);

        var consoleTracesOnly = a365OnlyMode && exporters.HasFlag(ExportTarget.Console);

        if (a365OnlyMode)
        {
            options.Instrumentation.SuppressDefaultInfraInstrumentation();
        }

        // Determine which signals have at least one exporter destination.
        // Agent365 only exports traces — metrics and logs sent to it would go nowhere.
        // Note: Console is included here even when consoleTracesOnly is true, because
        // the global flags affect ALL exporters (including any the caller chains after us).
        // Console-specific suppression is handled later by skipping AddConsoleExporter() calls.
        var hasTracingExporter = exporters != ExportTarget.None; // all exporters support traces
        var hasMetricsExporter = exporters.HasFlag(ExportTarget.AzureMonitor)
                              || exporters.HasFlag(ExportTarget.Otlp)
                              || exporters.HasFlag(ExportTarget.Console);
        var hasLoggingExporter = hasMetricsExporter; // same set supports logs

        // Effective signal flags: user intent AND exporter availability
        var effectiveTracing = options.Instrumentation.EnableTracing && hasTracingExporter;
        var effectiveMetrics = options.Instrumentation.EnableMetrics && hasMetricsExporter;
        var effectiveLogging = options.Instrumentation.EnableLogging && hasLoggingExporter;

        // Build an effective InstrumentationOptions that subsystems will use
        var effectiveInstrumentation = new InstrumentationOptions
        {
            EnableTracing = effectiveTracing,
            EnableMetrics = effectiveMetrics,
            EnableLogging = effectiveLogging,
            EnableAspNetCoreInstrumentation = options.Instrumentation.EnableAspNetCoreInstrumentation,
            EnableHttpClientInstrumentation = options.Instrumentation.EnableHttpClientInstrumentation,
            EnableSqlClientInstrumentation = options.Instrumentation.EnableSqlClientInstrumentation,
            EnableAzureSdkInstrumentation = options.Instrumentation.EnableAzureSdkInstrumentation,
            EnableOpenAIInstrumentation = options.Instrumentation.EnableOpenAIInstrumentation,
            EnableSemanticKernelInstrumentation = options.Instrumentation.EnableSemanticKernelInstrumentation,
            EnableAgentFrameworkInstrumentation = options.Instrumentation.EnableAgentFrameworkInstrumentation,
            EnableAgent365Instrumentation = options.Instrumentation.EnableAgent365Instrumentation,
        };

        // --- Azure Monitor (always: instrumentation; exporter gated by Exporters flag) ---
        builder.UseAzureMonitor(o =>
        {
            o.SkipExporter = !exporters.HasFlag(ExportTarget.AzureMonitor);
            o.ConnectionString = options.AzureMonitor.ConnectionString;
            o.Credential = options.AzureMonitor.Credential;
            o.DisableOfflineStorage = options.AzureMonitor.DisableOfflineStorage;
            o.StorageDirectory = options.AzureMonitor.StorageDirectory;
            o.EnableLiveMetrics = options.AzureMonitor.EnableLiveMetrics;
            o.EnableStandardMetrics = options.AzureMonitor.EnableStandardMetrics;
            o.EnablePerfCounters = options.AzureMonitor.EnablePerfCounters;
            o.EnableTraceBasedLogsSampler = options.AzureMonitor.EnableTraceBasedLogsSampler;
            o.SamplingRatio = options.AzureMonitor.SamplingRatio;
            o.TracesPerSecond = options.AzureMonitor.TracesPerSecond;
        }, effectiveInstrumentation);

        // --- Agent365 (always: scopes + baggage + span processors; exporter gated by Exporters flag) ---
        builder.UseAgent365(o =>
        {
            o.SkipExporter = !exporters.HasFlag(ExportTarget.Agent365);
            o.TokenResolver = options.Agent365.TokenResolver;
            o.ContextualTokenResolver = options.Agent365.ContextualTokenResolver;
            o.DomainResolver = options.Agent365.DomainResolver;
            o.ClusterCategory = options.Agent365.ClusterCategory;
            o.UseS2SEndpoint = options.Agent365.UseS2SEndpoint;
            o.MaxQueueSize = options.Agent365.MaxQueueSize;
            o.ScheduledDelayMilliseconds = options.Agent365.ScheduledDelayMilliseconds;
            o.ExporterTimeoutMilliseconds = options.Agent365.ExporterTimeoutMilliseconds;
            o.MaxExportBatchSize = options.Agent365.MaxExportBatchSize;
            o.MaxPayloadBytes = options.Agent365.MaxPayloadBytes;
        }, effectiveInstrumentation);

        // --- Microsoft Agent Framework (always: captures MAF spans + processor) ---
        builder.UseAgentFramework(effectiveInstrumentation);

        // --- OTLP exporter ---
        if (exporters.HasFlag(ExportTarget.Otlp))
        {
            if (effectiveInstrumentation.EnableTracing)
            {
                builder.WithTracing(tracing =>
                {
                    tracing.AddOtlpExporter();
                });
            }

            if (effectiveInstrumentation.EnableMetrics)
            {
                builder.WithMetrics(metrics =>
                {
                    metrics.AddOtlpExporter();
                });
            }

            if (effectiveInstrumentation.EnableLogging)
            {
                builder.WithLogging(logging =>
                {
                    logging.AddOtlpExporter();
                });
            }
        }

        // --- Console exporter ---
        if (exporters.HasFlag(ExportTarget.Console))
        {
            // When consoleTracesOnly is true, noisy instrumentation (HTTP, ASP.NET, SQL,
            // Azure SDK) has already been disabled above, so only gen_ai/agent Activities
            // are created. Plain AddConsoleExporter() is sufficient — no filtering needed.
            if (effectiveInstrumentation.EnableTracing)
            {
                builder.WithTracing(tracing => tracing.AddConsoleExporter());
            }

            if (effectiveInstrumentation.EnableMetrics && (!consoleTracesOnly || options.Instrumentation.MetricsExplicitlySet))
            {
                builder.WithMetrics(metrics => metrics.AddConsoleExporter());
            }

            if (effectiveInstrumentation.EnableLogging && (!consoleTracesOnly || options.Instrumentation.LoggingExplicitlySet))
            {
                builder.WithLogging(logging => logging.AddConsoleExporter());
            }
        }

        // --- A365-only mode startup message ---
        if (a365OnlyMode)
        {
            LogA365OnlyModeMessage(builder.Services);
        }

        // --- Logging kill switch ---
        // When effective logging is disabled, suppress all log records from reaching
        // OpenTelemetryLoggerProvider (affects Azure Monitor, OTLP, Console, etc.).
        // This is a provider-scoped ILogger filter — other loggers are not affected.
        if (!effectiveInstrumentation.EnableLogging)
        {
            builder.Services.AddLogging(logging =>
            {
                logging.AddFilter<OpenTelemetryLoggerProvider>(null, LogLevel.None);
            });
        }

        // --- Non-hosted scenario support ---
        // Azure Monitor's trace and log exporters are registered via IHostedService
        // (ExporterRegistrationHostedService), which only starts in hosted apps.
        // For non-hosted apps (e.g., OpenTelemetrySdk.Create()), we initialize them
        // during MeterProvider build — by then TracerProvider and LoggerProvider are
        // already built and cached as singletons, so resolving them is safe.
        //
        // We remove the IHostedService registration to prevent double-initialization
        // in hosted apps (ExporterRegistrationHostedService.Initialize() is NOT
        // idempotent — calling it twice adds duplicate exporters).
        // Same pattern used by ApplicationInsights 3.x SDK (TelemetryConfiguration.StartHostedServices).
        if (exporters.HasFlag(ExportTarget.AzureMonitor))
        {
            // Find and remove only the Azure Monitor ExporterRegistrationHostedService.
            // Other IHostedService registrations (e.g., A365OnlyModeStartupLogger) must stay.
            ServiceDescriptor? exporterServiceDescriptor = null;
            for (int i = builder.Services.Count - 1; i >= 0; i--)
            {
                var descriptor = builder.Services[i];
                if (descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                    && descriptor.ImplementationFactory != null)
                {
                    // Verify this is the Azure Monitor exporter registration, not an unrelated hosted service.
                    var declaringType = descriptor.ImplementationFactory.Method.DeclaringType?.FullName ?? string.Empty;
                    if (declaringType.Contains("Azure.Monitor") || declaringType.Contains("AzureMonitor"))
                    {
                        exporterServiceDescriptor = descriptor;
                        builder.Services.RemoveAt(i);
                        break;
                    }
                }
            }

            if (exporterServiceDescriptor != null)
            {
                var savedFactory = exporterServiceDescriptor.ImplementationFactory!;

                // MeterProvider builds last (after Tracer and Logger), so both providers
                // are available for the exporter to attach to.
                builder.Services.ConfigureOpenTelemetryMeterProvider((sp, _) =>
                {
                    var service = (Microsoft.Extensions.Hosting.IHostedService)savedFactory(sp);
                    service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                });
            }
        }

        // Bootstrap SDK Stats eagerly when no Azure Monitor exporter is selected so that
        // Attach + Feature SDK Stats are reported in OTLP-only / Console-only / Agent365-only
        // deployments. Must be called after `exporters` is finalized (to avoid double-pinning
        // when AzureMonitor is selected) and after the AppContext switch above is set (so
        // SDK Stats routes to the distro-owned ingestion endpoint).
        SdkStatsPin.EnsureIfApplicable(exporters);
        RegisterDistroFeatureSdkStats(builder.Services, options, exporters, a365OnlyMode);

        return builder;
    }

    private static void RegisterDistroFeatureSdkStats(
        IServiceCollection services,
        MicrosoftOpenTelemetryOptions options,
        ExportTarget effectiveExporters,
        bool a365OnlyMode)
    {
        // Kill switch is checked at registration time so we don't even attach the MeterProvider
        // configurator when stats are disabled.
        string? disabled;
        try
        {
            disabled = Environment.GetEnvironmentVariable(
                EnvironmentVariableConstants.APPLICATIONINSIGHTS_STATSBEAT_DISABLED);
        }
        catch (Exception)
        {
            disabled = null;
        }

        if (string.Equals(disabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            AzureMonitorAspNetCoreEventSource.Log.DistroFeatureSdkStatsDisabledByEnvVar();
            return;
        }

        // Defer snapshot construction until the MeterProvider builds: by then the exporter's
        // AzureMonitorOptions has been populated from IConfiguration and environment variables,
        // so the connection string (and the customer iKey we extract from it) is the same value
        // the exporter is actually using.
        services.ConfigureOpenTelemetryMeterProvider((sp, _) =>
        {
            try
            {
                // Mirror the exporter's opt-in semantics: APPLICATIONINSIGHTS_SDKSTATS_DISABLED
                // must be set to "false" to enable customer SDK stats.
                var customerSdkStatsDisabled = Environment.GetEnvironmentVariable(
                    EnvironmentVariableConstants.APPLICATIONINSIGHTS_SDKSTATS_DISABLED);
                var customerSdkStatsEnabled = string.Equals(customerSdkStatsDisabled, "false", StringComparison.OrdinalIgnoreCase);

                // Resolve the effective connection string the exporter will use at transmit
                // time without writing it back into the caller-supplied options instance.
                // The customer's MicrosoftOpenTelemetryOptions is treated as immutable once
                // configure(options) returns; mutating it from a deferred callback would be
                // a surprising side effect on an object that may still be referenced by user
                // code or other configurators.
                var effectiveConnectionString =
                    !string.IsNullOrEmpty(options.AzureMonitor.ConnectionString)
                        ? options.AzureMonitor.ConnectionString
                        : ResolveEffectiveConnectionString(sp);

                var snapshot = DistroFeatureSnapshot.Build(
                    options,
                    effectiveConnectionString,
                    effectiveExporters,
                    customerSdkStatsEnabled,
                    a365OnlyMode,
                    SdkVersion.Value);

                if (snapshot is null)
                {
                    return;
                }

                // The SDK Stats pin (eagerly created in TryEnsureSdkStatsPin
                // above) already brought up SDK Stats; nothing left for this callback to do
                // on the pin side.
                DistroFeatureSdkStats.Initialize(snapshot);

                // Network SDKStats: the Azure Monitor exporter only records Network stats for
                // its own Breeze transmitter. When the Agent365 exporter is active, the distro
                // owns the a365 Network signal and records it itself (see Agent365ExporterCore).
                // Other languages don't yet support Network SDKStats for OTLP, so this is scoped
                // to Agent365 only.
                if (effectiveExporters.HasFlag(ExportTarget.Agent365))
                {
                    DistroNetworkSdkStats.Initialize(snapshot.CustomerInstrumentationKey, snapshot.DistroVersion);
                }
            }
            catch (Exception ex)
            {
                // Stats are best-effort; never let registration failures break user instrumentation.
                AzureMonitorAspNetCoreEventSource.Log.DistroFeatureSdkStatsCallbackFailed(ex);
            }
        });
    }

    private static string? ResolveEffectiveConnectionString(IServiceProvider sp)
    {
        try
        {
            // The distro's AzureMonitorOptions is bound from IConfiguration and the
            // APPLICATIONINSIGHTS_CONNECTION_STRING env var by DefaultAzureMonitorOptions.
            // Reading IOptions<AzureMonitorOptions> from DI yields the same effective
            // connection string the exporter will use at transmit time.
            var amOptions = sp.GetService<IOptions<Microsoft.OpenTelemetry.AzureMonitorOptions>>()?.Value;
            return amOptions?.ConnectionString;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if an Azure Monitor connection string is available from IConfiguration
    /// (appsettings.json, env var provider, Key Vault, etc.) or raw environment variable.
    /// Does NOT call BuildServiceProvider().
    /// </summary>
    private static bool HasAzureMonitorConnectionString(IServiceCollection services)
    {
        // Try to find IConfiguration from service descriptors (registered as singleton instance
        // by HostApplicationBuilder, WebApplicationBuilder, and Host.CreateDefaultBuilder)
        IConfiguration? config = null;
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IConfiguration)
                && services[i].ImplementationInstance is IConfiguration c)
            {
                config = c;
                break;
            }
        }

        if (config != null)
        {
            // Check "AzureMonitor" config section (appsettings.json)
            var sectionConnStr = config.GetSection("AzureMonitor")?["ConnectionString"];
            if (!string.IsNullOrWhiteSpace(sectionConnStr))
                return true;

            // Check APPLICATIONINSIGHTS_CONNECTION_STRING via IConfiguration
            // (covers env var provider, in-memory collection, and other IConfiguration sources)
            var configConnStr = config["APPLICATIONINSIGHTS_CONNECTION_STRING"];
            if (!string.IsNullOrWhiteSpace(configConnStr))
                return true;
        }

        // Fallback: check raw environment variable (for non-host DI apps without IConfiguration)
        var envConnStr = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        return !string.IsNullOrWhiteSpace(envConnStr);
    }

    /// <summary>
    /// Registers a one-time startup log message explaining that Agent365-only mode
    /// has disabled infrastructure instrumentation.
    /// </summary>
    private static void LogA365OnlyModeMessage(IServiceCollection services)
    {
        services.AddHostedService<A365OnlyModeStartupLogger>();
    }

    /// <summary>
    /// Logs a one-time informational message at startup when Agent365-only mode is active.
    /// </summary>
    private sealed class A365OnlyModeStartupLogger : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly ILogger<A365OnlyModeStartupLogger> _logger;

        public A365OnlyModeStartupLogger(ILogger<A365OnlyModeStartupLogger> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Microsoft.OpenTelemetry: Agent365-only mode active. Infrastructure instrumentation " +
                "(HTTP, ASP.NET, SQL, Azure SDK) is disabled — Agent365 exports gen_ai/agent traces only. " +
                "To re-enable, set EnableHttpClientInstrumentation = true (or similar) in " +
                "UseMicrosoftOpenTelemetry options, or add ExportTarget.AzureMonitor / ExportTarget.Otlp for full observability.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
