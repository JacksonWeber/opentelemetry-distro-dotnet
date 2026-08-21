// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using Microsoft.OpenTelemetry.AzureMonitor.Internals;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Marks enabled instrumentations when their meters record an actual measurement.
    /// </summary>
    internal sealed class DistroInstrumentationUsageMeterListener : IDisposable
    {
        internal static readonly TimeSpan ObservableCollectionInterval = TimeSpan.FromMinutes(1);

        private static readonly AsyncLocal<int> HttpMetricSuppressionDepth = new();
        private static readonly ConcurrentDictionary<string, byte> InternalHttpHosts =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly DistroInstrumentation _enabledInstrumentations;
        private readonly MeterListener _listener;
        private readonly Timer _observableCollectionTimer;
        private readonly object _observableCollectionLock = new();
        private bool _disposed;

        internal DistroInstrumentationUsageMeterListener(
            DistroInstrumentation enabledInstrumentations)
        {
            _enabledInstrumentations = enabledInstrumentations;
            _listener = new MeterListener
            {
                InstrumentPublished = EnableKnownInstrumentation,
            };

            _listener.SetMeasurementEventCallback<byte>(MarkInstrumentation);
            _listener.SetMeasurementEventCallback<short>(MarkInstrumentation);
            _listener.SetMeasurementEventCallback<int>(MarkInstrumentation);
            _listener.SetMeasurementEventCallback<long>(MarkInstrumentation);
            _listener.SetMeasurementEventCallback<float>(MarkInstrumentation);
            _listener.SetMeasurementEventCallback<double>(MarkInstrumentation);
            _listener.SetMeasurementEventCallback<decimal>(MarkInstrumentation);
            _listener.Start();
            _observableCollectionTimer = new Timer(
                _ => CollectObservableInstruments(),
                null,
                ObservableCollectionInterval,
                Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            lock (_observableCollectionLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _observableCollectionTimer.Dispose();
                _listener.Dispose();
            }
        }

        internal void CollectObservableInstruments()
        {
            lock (_observableCollectionLock)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    _listener.RecordObservableInstruments();
                }
                catch (Exception ex)
                {
                    AzureMonitorAspNetCoreEventSource.Log.DistroFeatureSdkStatsCallbackFailed(ex);
                }
                finally
                {
                    if (!_disposed)
                    {
                        // Rearm only after collection completes so slow customer callbacks
                        // cannot queue overlapping observable collection work.
                        _observableCollectionTimer.Change(
                            ObservableCollectionInterval,
                            Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        internal static IDisposable SuppressHttpMetrics()
        {
            HttpMetricSuppressionDepth.Value++;
            return new HttpMetricSuppressionScope();
        }

        internal static void RegisterInternalHttpHost(string? host)
        {
            var normalizedHost = NormalizeHost(host);
            if (normalizedHost is not null)
            {
                InternalHttpHosts.TryAdd(normalizedHost, 0);
            }
        }

        internal static void ResetInternalHttpHostsForTesting() => InternalHttpHosts.Clear();

        private static string? NormalizeHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            return host!.Trim().TrimEnd('.');
        }

        private void EnableKnownInstrumentation(Instrument instrument, MeterListener listener)
        {
            var instrumentation =
                DistroInstrumentationUsageProcessor.GetInstrumentations(instrument.Meter.Name)
                & _enabledInstrumentations;
            if (instrumentation != DistroInstrumentation.None)
            {
                listener.EnableMeasurementEvents(instrument, instrumentation);
            }
        }

        private static void MarkInstrumentation<T>(
            Instrument instrument,
            T measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where T : struct
        {
            if (state is DistroInstrumentation instrumentation)
            {
                if (instrumentation == DistroInstrumentation.HttpClient
                    && IsAzureMonitorInternalHttpRequest(tags))
                {
                    return;
                }

                DistroSdkStatsUsage.MarkInstrumentationInUse(instrumentation);
            }
        }

        private static bool IsAzureMonitorInternalHttpRequest(
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (HttpMetricSuppressionDepth.Value > 0)
            {
                return true;
            }

            string? serverAddress = null;
            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, "server.address", StringComparison.Ordinal))
                {
                    serverAddress = tag.Value?.ToString();
                    break;
                }
            }

            var address = NormalizeHost(serverAddress);
            if (address is null)
            {
                return false;
            }

            return string.Equals(address, "169.254.169.254", StringComparison.OrdinalIgnoreCase)
                || string.Equals(address, "dc.services.visualstudio.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(address, "rt.services.visualstudio.com", StringComparison.OrdinalIgnoreCase)
                || address.EndsWith(".stats.monitor.azure.com", StringComparison.OrdinalIgnoreCase)
                || address.EndsWith(".in.applicationinsights.azure.com", StringComparison.OrdinalIgnoreCase)
                || address.EndsWith(".livediagnostics.monitor.azure.com", StringComparison.OrdinalIgnoreCase)
                || InternalHttpHosts.ContainsKey(address);
        }

        private sealed class HttpMetricSuppressionScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                HttpMetricSuppressionDepth.Value--;
            }
        }
    }
}
