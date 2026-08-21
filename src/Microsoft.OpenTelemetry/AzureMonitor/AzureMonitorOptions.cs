// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using System.Reflection;
using Azure.Core;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Monitor.OpenTelemetry.LiveMetrics;
using Azure.Core.Pipeline;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;

namespace Microsoft.OpenTelemetry
{
    /// <summary>
    /// Options that allow users to configure the Azure Monitor.
    /// </summary>
    public class AzureMonitorOptions : ClientOptions
    {
        private static readonly PropertyInfo? IsCustomTransportSetProperty =
            typeof(ClientOptions).GetProperty(
                "IsCustomTransportSet",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly HttpPipelineTransport _inheritedTransport;

        /// <summary>
        /// The Connection String provides users with a single configuration setting to identify the Azure Monitor resource and endpoint.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.microsoft.com/azure/azure-monitor/app/sdk-connection-string"/>.
        /// </remarks>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Get or sets the value of <see cref="TokenCredential" />.
        /// If <see cref="TokenCredential" /> is not set, AAD authentication is disabled
        /// and Instrumentation Key from the Connection String will be used.
        /// </summary>
        /// <remarks>
        /// <see href="https://learn.microsoft.com/en-us/azure/azure-monitor/app/sdk-connection-string?tabs=net#is-the-connection-string-a-secret"/>.
        /// </remarks>
        public TokenCredential? Credential { get; set; }

        /// <summary>
        /// Disable offline storage.
        /// </summary>
        public bool DisableOfflineStorage { get; set; }

        /// <summary>
        /// Enables or disables the Live Metrics feature. This property is enabled by default.
        /// Note: Enabling Live Metrics incurs no additional billing or costs. However, it does introduce
        /// a performance overhead due to extra data collection, processing, and networking calls. This overhead
        /// is only significant when the LiveMetrics portal is actively used in the UI. Once the portal is closed,
        /// LiveMetrics reverts to a 'silent' mode with minimal to no overhead.
        /// <see href="https://learn.microsoft.com/azure/azure-monitor/app/live-stream?tabs=dotnet6"/>.
        /// </summary>
        public bool EnableLiveMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether standard metrics should be collected.
        /// Default is true.
        /// </summary>
        public bool EnableStandardMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether performance counters should be collected.
        /// Default is true.
        /// </summary>
        public bool EnablePerfCounters { get; set; } = true;

        /// <summary>
        /// Enables or disables filtering logs based on trace sampling decisions.
        /// </summary>
        /// <remarks>
        /// When enabled, only logs associated with sampled traces are exported.
        /// Logs without trace context are always exported.
        /// This reduces log volume while maintaining trace-log correlation.
        /// </remarks>
        public bool EnableTraceBasedLogsSampler { get; set; } = true;

        /// <summary>
        /// Gets or sets the ratio of telemetry items to be sampled. The value must be between 0.0F and 1.0F, inclusive.
        /// For example, specifying 0.4 means that 40% of traces are sampled and 60% are dropped.
        /// </summary>
        public float SamplingRatio { get; set; } = 1.0F;

        /// <summary>
        /// Gets or sets the number of traces per second to be sampled when using rate-limited sampling.
        /// For example, specifying 0.5 means one request every two seconds.
        /// When both TracesPerSecond and SamplingRatio are specified, TracesPerSecond takes precedence.
        /// </summary>
        public double? TracesPerSecond { get; set; } = 5.0;

        /// <summary>
        /// Override the default directory for offline storage.
        /// </summary>
        public string? StorageDirectory { get; set; }

        /// <summary>
        /// When true, skips exporter registration. Instrumentation is still added.
        /// Used internally by <see cref="MicrosoftOpenTelemetryBuilderExtensions.UseMicrosoftOpenTelemetry"/>.
        /// </summary>
        internal bool SkipExporter { get; set; }

        internal HttpPipelineTransport? ExplicitTransport
        {
            get
            {
                bool setterWasCalled =
                    IsCustomTransportSetProperty?.GetValue(this) is true;
                return setterWasCalled || !ReferenceEquals(Transport, _inheritedTransport)
                    ? Transport
                    : null;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureMonitorOptions"/>.
        /// </summary>
        public AzureMonitorOptions()
        {
            _inheritedTransport = Transport;
            // users can explicitly change it, but by default we don't want internal logs to be reported to Azure Monitor.
            this.Diagnostics.IsDistributedTracingEnabled = false;
            this.Diagnostics.IsLoggingEnabled = false;
        }

        internal void SetValueToExporterOptions(AzureMonitorExporterOptions exporterOptions)
        {
            exporterOptions.ConnectionString = ConnectionString;
            exporterOptions.Credential = Credential;
            exporterOptions.DisableOfflineStorage = DisableOfflineStorage;
            exporterOptions.SamplingRatio = SamplingRatio;
            exporterOptions.TracesPerSecond = TracesPerSecond;
            exporterOptions.StorageDirectory = StorageDirectory;
            exporterOptions.EnableLiveMetrics = EnableLiveMetrics;
            exporterOptions.EnableStandardMetrics = EnableStandardMetrics;
            exporterOptions.EnablePerformanceCounters = EnablePerfCounters;
            exporterOptions.EnableTraceBasedLogsSampler = EnableTraceBasedLogsSampler;
            // The exporter copies its transport into the internal Live Metrics client. Wrapping
            // here lets SDKStats distinguish silent pinging from active subscribed POSTs without
            // relying on exporter internals or changing customer transport behavior.
            var transport =
                ExplicitTransport ?? exporterOptions.Transport ?? HttpClientTransport.Shared;
            exporterOptions.Transport = transport is LiveMetricsUsageTrackingTransport
                ? transport
                : new LiveMetricsUsageTrackingTransport(transport);
            exporterOptions.Diagnostics.IsDistributedTracingEnabled = Diagnostics.IsDistributedTracingEnabled;
            exporterOptions.Diagnostics.IsLoggingEnabled = Diagnostics.IsLoggingEnabled;
        }
    }
}
