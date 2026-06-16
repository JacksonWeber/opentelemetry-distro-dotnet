// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using Microsoft.OpenTelemetry.AzureMonitor.Internals;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Owns the distro's Feature SDKStats meter and observable gauge. The Azure Monitor
    /// exporter's Statsbeat <c>MeterProvider</c> subscribes to the meter name advertised by
    /// this class (see <c>StatsbeatConstants.DistroFeatureSdkStatsMeterName</c> in the
    /// exporter). That provider collects on the shared 15-minute reader, so this gauge
    /// throttles to one emission per <see cref="EmissionInterval"/> (24 hr) instead of
    /// shipping every 15 min.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The class is a process-wide singleton because the underlying <see cref="Meter"/> is a
    /// process-wide resource and the exporter's MeterProvider subscribes by name. Subsequent
    /// <see cref="Initialize"/> calls atomically swap the snapshot reference so the most
    /// recent <c>UseMicrosoftOpenTelemetry</c> configuration wins; this keeps the disposable
    /// surface of the class minimal and matches the lifecycle of the singleton MeterProvider.
    /// </para>
    /// <para>
    /// Per the SDKStats specification, the gauge returns no measurements when the
    /// configured features bit mask is <see cref="DistroFeature.None"/>. The
    /// <see cref="Func{TResult}"/> of <see cref="IEnumerable{T}"/> overload is used (not the
    /// single-<c>Measurement</c> overload) so an empty result actually skips emission
    /// instead of publishing a phantom zero-valued data point with no tags.
    /// </para>
    /// </remarks>
    internal sealed class DistroFeatureSdkStats : IDisposable
    {
        /// <summary>
        /// Meter name owned by the distro for Feature SDKStats. Must match the constant
        /// subscribed by the Azure Monitor exporter's Statsbeat <see cref="Meter"/> provider.
        /// </summary>
        internal const string MeterName = "MicrosoftOpenTelemetryFeatureSdkStatsMeter";

        /// <summary>Metric name per the SDKStats spec.</summary>
        internal const string MetricName = "Feature";

        /// <summary>Meter version reported alongside <see cref="MeterName"/>.</summary>
        internal const string MeterVersion = "1.0";

        /// <summary>
        /// Minimum time between Feature SDKStats emissions. Feature stats share the exporter's
        /// 15-minute reader but must ship on the 24 hr cadence, so <see cref="Observe"/>
        /// throttles to one emission per interval (matches the exporter's Attach gauge).
        /// </summary>
        internal static readonly TimeSpan EmissionInterval = TimeSpan.FromHours(24);

        private static DistroFeatureSdkStats? s_instance;
        private static readonly object s_lock = new();

        private static readonly IEnumerable<Measurement<long>> EmptyMeasurements = Array.Empty<Measurement<long>>();

        private readonly Meter _meter;
        private DistroFeatureSnapshot _snapshot;

        // Throttle so the Feature gauge emits at most once per EmissionInterval even though
        // the shared reader collects every 15 min.
        private long _lastEmissionTicks;

        private DistroFeatureSdkStats(DistroFeatureSnapshot snapshot)
        {
            // Snapshot is assigned before the meter is created and before the instance is
            // published to s_instance, so any reader that observes the instance via the
            // public Instance property is guaranteed to see a fully-initialized object
            // (no narrow window where _snapshot is null).
            _snapshot = snapshot;
            _meter = new Meter(MeterName, MeterVersion);
            _meter.CreateObservableGauge<long>(MetricName, this.Observe);
        }

        /// <summary>The active singleton, if <see cref="Initialize"/> has been called.</summary>
        internal static DistroFeatureSdkStats? Instance => s_instance;

        /// <summary>
        /// Registers (or updates) the distro Feature SDKStats producer with the supplied
        /// snapshot. Safe to call repeatedly; the most recent snapshot wins.
        /// </summary>
        /// <param name="snapshot">Bit map + cikey + distro version describing the configuration.</param>
        /// <remarks>
        /// The Statsbeat <c>MeterProvider</c> that ships our <c>Feature</c> measurement is
        /// brought up either by the customer's own <c>AzureMonitorMetricExporter</c> (when
        /// Azure Monitor is selected) or by the distro's process-wide
        /// <c>SdkStatsPin</c> (eagerly created in
        /// <c>MicrosoftOpenTelemetryBuilderExtensions.TryEnsureSdkStatsPin</c>) when
        /// it is not. Either way, the pin's lifetime is managed outside this class.
        /// </remarks>
        internal static DistroFeatureSdkStats Initialize(DistroFeatureSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            lock (s_lock)
            {
                if (s_instance is null)
                {
                    // First call: construct with the snapshot already set, then publish.
                    // The ordering here matters — assigning s_instance only after the
                    // constructor returns guarantees Instance never exposes a partially
                    // initialized object.
                    s_instance = new DistroFeatureSdkStats(snapshot);
                }
                else
                {
                    // Subsequent call: swap snapshot under the lock so writes are ordered
                    // with respect to instance publish. Observe() pairs this with a
                    // Volatile.Read for cross-thread visibility on platforms with weak
                    // memory models.
                    Volatile.Write(ref s_instance._snapshot, snapshot);
                }

                return s_instance;
            }
        }

        /// <summary>
        /// Releases the singleton instance and disposes the underlying meter. Test-only;
        /// production code keeps the singleton alive for the lifetime of the process.
        /// </summary>
        internal static void ResetForTesting()
        {
            lock (s_lock)
            {
                s_instance?.Dispose();
                s_instance = null;
            }
        }

        public void Dispose()
        {
            _meter.Dispose();
        }

        private IEnumerable<Measurement<long>> Observe()
        {
            DistroFeatureSnapshot snapshot;
            try
            {
                snapshot = Volatile.Read(ref _snapshot);
                if (snapshot.Features == DistroFeature.None)
                {
                    // SDKStats spec: "Don't send feature/instrumentation SDKStats when the
                    // feature/instrumentation list is empty." Returning an empty enumerable
                    // (not a default Measurement) is required to truly skip emission for
                    // this collection cycle.
                    return EmptyMeasurements;
                }
            }
            catch (Exception ex)
            {
                AzureMonitorAspNetCoreEventSource.Log.DistroFeatureSdkStatsCallbackFailed(ex);
                return EmptyMeasurements;
            }

            // Throttle to the 24 hr cadence: skip collections inside the window. Delta
            // temporality means a skipped collection exports nothing. A negative elapsed value
            // means the wall clock jumped backwards (e.g. NTP/VM sync); treat that as eligible
            // so a backwards jump re-anchors the window instead of suppressing for up to 24 hr.
            long previousTicks = Volatile.Read(ref _lastEmissionTicks);
            long nowTicks = DateTime.UtcNow.Ticks;
            long elapsedTicks = nowTicks - previousTicks;
            if (previousTicks != 0 && elapsedTicks >= 0 && elapsedTicks < EmissionInterval.Ticks)
            {
                return EmptyMeasurements;
            }

            // CAS so a racing collection can't double-emit.
            if (Interlocked.CompareExchange(ref _lastEmissionTicks, nowTicks, previousTicks) != previousTicks)
            {
                return EmptyMeasurements;
            }

            try
            {
                var measurement = new Measurement<long>(
                    (long)snapshot.Features,
                    new KeyValuePair<string, object?>("rp", ResourceProviderHelper.GetResourceProvider()),
                    new KeyValuePair<string, object?>("attach", ResourceProviderHelper.GetAttachMode()),
                    new KeyValuePair<string, object?>("cikey", snapshot.CustomerInstrumentationKey),
                    new KeyValuePair<string, object?>("feature", (long)snapshot.Features),
                    new KeyValuePair<string, object?>("type", 0),
                    new KeyValuePair<string, object?>("os", ResourceProviderHelper.GetOperatingSystem()),
                    new KeyValuePair<string, object?>("language", "dotnet"),
                    new KeyValuePair<string, object?>("version", snapshot.DistroVersion));

                return new[] { measurement };
            }
            catch (Exception ex)
            {
                AzureMonitorAspNetCoreEventSource.Log.DistroFeatureSdkStatsCallbackFailed(ex);
                // Rewind so we retry on the next collection.
                Volatile.Write(ref _lastEmissionTicks, previousTicks);
                return EmptyMeasurements;
            }
        }
    }
}
