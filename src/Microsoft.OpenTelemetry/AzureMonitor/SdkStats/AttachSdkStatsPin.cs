// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using Azure.Monitor.OpenTelemetry.Exporter;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Process singleton that ensures the Azure Monitor exporter's SDK Stats
    /// <c>MeterProvider</c> is initialized when no Azure Monitor exporter has been
    /// selected. Constructs a single inert <see cref="AzureMonitorMetricExporter"/>
    /// pointed at a placeholder connection string and holds it for the lifetime of the
    /// process; it ships no telemetry of its own, but its constructor side effect brings
    /// up the SDK Stats <c>MeterProvider</c> that exports Attach + Feature SDK Stats on
    /// the existing 24-hour cadence.
    /// </summary>
    /// <remarks>
    /// The pin is a singleton because the underlying SDK Stats <c>MeterProvider</c> is a
    /// process-wide resource — a second pin would build a second <c>MeterProvider</c> and
    /// double-report Attach measurements. <see cref="Ensure"/> is idempotent and
    /// thread-safe via <see cref="Interlocked.CompareExchange{T}"/>.
    /// </remarks>
    internal static class AttachSdkStatsPin
    {
        // InstrumentationKey=N/A matches the SDK Stats spec convention for deployments
        // without a customer Application Insights resource. The IngestionEndpoint host
        // is parsed by the exporter to select a SDK Stats region; the distro's
        // RouteSdkStatsToDistroEndpoint AppContext switch (set earlier in
        // UseMicrosoftOpenTelemetry) reroutes the actual export to stats.monitor.azure.com.
        private const string PlaceholderConnectionString =
            "InstrumentationKey=N/A;IngestionEndpoint=https://westus-0.in.applicationinsights.azure.com/";

        private static AzureMonitorMetricExporter? s_pin;

        /// <summary>
        /// Idempotently constructs the inert exporter pin, triggering SDK Stats
        /// initialization as a constructor side effect.
        /// </summary>
        /// <remarks>
        /// Caller is responsible for the kill-switch check
        /// (<c>APPLICATIONINSIGHTS_STATSBEAT_DISABLED</c>) and for skipping when the
        /// customer has selected the Azure Monitor exporter. Exceptions are logged to
        /// the distro event source and swallowed; SDKStats are best-effort and must not
        /// break customer instrumentation.
        /// </remarks>
        internal static void Ensure()
        {
            if (Volatile.Read(ref s_pin) != null)
            {
                return;
            }

            AzureMonitorMetricExporter? created;
            try
            {
                created = new AzureMonitorMetricExporter(new AzureMonitorExporterOptions
                {
                    ConnectionString = PlaceholderConnectionString,
                    DisableOfflineStorage = true,
                });
            }
            catch (Exception ex)
            {
                Microsoft.OpenTelemetry.AzureMonitorAspNetCoreEventSource.Log.AttachSdkStatsPinFailed(ex);
                return;
            }

            // First writer wins. Dispose any duplicate created by a concurrent caller.
            if (Interlocked.CompareExchange(ref s_pin, created, null) != null)
            {
                try { created.Dispose(); } catch { /* best effort */ }
                return;
            }

            Microsoft.OpenTelemetry.AzureMonitorAspNetCoreEventSource.Log.AttachSdkStatsPinInitialized();
        }

        /// <summary>
        /// Releases the singleton pin and disposes the underlying exporter. Test-only;
        /// production code keeps the pin alive for the lifetime of the process.
        /// </summary>
        internal static void ResetForTesting()
        {
            var previous = Interlocked.Exchange(ref s_pin, null);
            if (previous != null)
            {
                try { previous.Dispose(); } catch { /* best effort */ }
            }
        }

        internal static bool IsInitializedForTesting => Volatile.Read(ref s_pin) != null;
    }
}

