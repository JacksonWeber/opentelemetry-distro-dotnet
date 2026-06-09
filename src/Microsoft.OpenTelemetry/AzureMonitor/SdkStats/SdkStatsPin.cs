// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.OpenTelemetry.AzureMonitor.Internals;

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
    /// double-report Attach measurements. <see cref="EnsureIfApplicable"/> is the
    /// production entry point and owns the kill-switch / exporter-selection policy;
    /// <see cref="Ensure"/> is the unguarded primitive intended for unit tests only.
    /// Both are idempotent and thread-safe via <see cref="Interlocked.CompareExchange{T}"/>.
    /// </remarks>
    internal static class SdkStatsPin
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
        /// Production entry point. Idempotently initializes the pin when the supplied
        /// exporter selection requires the distro to bootstrap SDK Stats itself, and
        /// the <c>APPLICATIONINSIGHTS_STATSBEAT_DISABLED</c> kill switch is not set.
        /// No-op when AzureMonitor is in <paramref name="effectiveExporters"/> (the
        /// customer's own exporter triggers SDK Stats) or when
        /// <paramref name="effectiveExporters"/> is <see cref="ExportTarget.None"/>.
        /// </summary>
        internal static void EnsureIfApplicable(ExportTarget effectiveExporters)
        {
            if (effectiveExporters == ExportTarget.None
                || effectiveExporters.HasFlag(ExportTarget.AzureMonitor))
            {
                return;
            }

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
                return;
            }

            Ensure();
        }

        /// <summary>
        /// Unguarded primitive that idempotently constructs the inert exporter pin,
        /// triggering SDK Stats initialization as a constructor side effect.
        /// </summary>
        /// <remarks>
        /// Production code must go through <see cref="EnsureIfApplicable"/> so the
        /// kill switch and exporter-selection policy are honored. This method is kept
        /// internal for unit tests that exercise the pin in isolation. Exceptions are
        /// logged to the distro event source and swallowed; SDKStats are best-effort
        /// and must not break customer instrumentation.
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
                Microsoft.OpenTelemetry.AzureMonitorAspNetCoreEventSource.Log.SdkStatsPinFailed(ex);
                return;
            }

            // First writer wins. Dispose any duplicate created by a concurrent caller.
            if (Interlocked.CompareExchange(ref s_pin, created, null) != null)
            {
                try { created.Dispose(); } catch { /* best effort */ }
                return;
            }

            Microsoft.OpenTelemetry.AzureMonitorAspNetCoreEventSource.Log.SdkStatsPinInitialized();
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

