// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using Microsoft.OpenTelemetry.AzureMonitor.Internals;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Owns the distro's short-interval Network SDKStats meter and instruments for telemetry
    /// the distro itself ships to the Agent365 (<c>a365</c>) ingestion endpoint. The Azure
    /// Monitor exporter's Statsbeat <c>MeterProvider</c> subscribes to <see cref="MeterName"/>
    /// (see <c>StatsbeatConstants.DistroNetworkSdkStatsMeterName</c> in the exporter), so these
    /// measurements flow out on the shared 15-minute reader even when the Azure Monitor
    /// exporter is not selected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the Network analog of <see cref="DistroFeatureSdkStats"/>. The exporter only
    /// records Network SDKStats for its own Breeze transmitter; when a customer runs the distro
    /// with the Agent365 exporter (and no Azure Monitor exporter), the distro owns the
    /// <c>a365</c> Network signal and records it here. Instrument names follow the SDKStats
    /// specification; the <c>endpoint</c> dimension is <c>a365</c> and <c>version</c> is the
    /// distro package version.
    /// </para>
    /// <para>
    /// The class is a process-wide singleton because the underlying <see cref="Meter"/> is a
    /// process-wide resource and the exporter's MeterProvider subscribes by name. Recording is
    /// imperative (per request) rather than via an observable gauge, so per-request
    /// <c>host</c> / <c>statusCode</c> / <c>exceptionType</c> dimensions can be attached.
    /// </para>
    /// </remarks>
    internal sealed class DistroNetworkSdkStats : IDisposable
    {
        /// <summary>
        /// Meter name owned by the distro for Network SDKStats. Must match the constant
        /// subscribed by the Azure Monitor exporter's Statsbeat <see cref="Meter"/> provider.
        /// </summary>
        internal const string MeterName = "MicrosoftOpenTelemetryNetworkSdkStatsMeter";

        /// <summary>Meter version reported alongside <see cref="MeterName"/>.</summary>
        internal const string MeterVersion = "1.0";

        /// <summary>Value of the <c>endpoint</c> dimension for telemetry sent to Agent365.</summary>
        internal const string EndpointA365 = "a365";

        private const string Language = "dotnet";

        // HTTP status codes that the per-request classification treats specially. 200 is the
        // only success code per spec; 206/307/308 are handled by the pipeline and ignored.
        private const int StatusSuccess = 200;
        private const int StatusPartialSuccess = 206;
        private const int StatusTemporaryRedirect = 307;
        private const int StatusPermanentRedirect = 308;

        private static DistroNetworkSdkStats? s_instance;
        private static readonly object s_lock = new();

        private static readonly string s_runtimeVersion = ResolveRuntimeVersion();

        private readonly Meter _meter;
        private readonly Counter<long> _requestSuccessCount;
        private readonly Counter<long> _requestFailureCount;
        private readonly Histogram<double> _requestDuration;
        private readonly Counter<long> _retryCount;
        private readonly Counter<long> _throttleCount;
        private readonly Counter<long> _exceptionCount;

        private string _customerInstrumentationKey;
        private string _distroVersion;

        private DistroNetworkSdkStats(string customerInstrumentationKey, string distroVersion)
        {
            _customerInstrumentationKey = customerInstrumentationKey;
            _distroVersion = distroVersion;
            _meter = new Meter(MeterName, MeterVersion);

            _requestSuccessCount = _meter.CreateCounter<long>(
                "Request_Success_Count",
                description: "Count of requests accepted by the destination ingestion endpoint.");
            _requestFailureCount = _meter.CreateCounter<long>(
                "Request_Failure_Count",
                description: "Count of requests that returned a non-retryable, non-throttling failure from the destination ingestion endpoint.");
            _requestDuration = _meter.CreateHistogram<double>(
                "Request_Duration",
                unit: "ms",
                description: "Duration of requests to the destination ingestion endpoint.");
            _retryCount = _meter.CreateCounter<long>(
                "Retry_Count",
                description: "Count of requests for which the destination ingestion endpoint returned a retryable response.");
            _throttleCount = _meter.CreateCounter<long>(
                "Throttle_Count",
                description: "Count of requests for which the destination ingestion endpoint returned a quota / rate-limit response.");
            _exceptionCount = _meter.CreateCounter<long>(
                "Exception_Count",
                description: "Count of requests that failed with an exception (no response code received).");
        }

        /// <summary>The active singleton, if <see cref="Initialize"/> has been called.</summary>
        internal static DistroNetworkSdkStats? Instance => s_instance;

        /// <summary>
        /// Registers (or updates) the distro Network SDKStats producer. Safe to call repeatedly;
        /// the most recent customer iKey / distro version wins.
        /// </summary>
        /// <param name="customerInstrumentationKey">
        /// Customer instrumentation key, or <c>"N/A"</c> when no Azure Monitor connection string
        /// is configured (the common Agent365-only case).
        /// </param>
        /// <param name="distroVersion">Distro package version reported as the <c>version</c> dimension.</param>
        internal static DistroNetworkSdkStats Initialize(string customerInstrumentationKey, string distroVersion)
        {
            lock (s_lock)
            {
                if (s_instance is null)
                {
                    s_instance = new DistroNetworkSdkStats(customerInstrumentationKey, distroVersion);
                }
                else
                {
                    Volatile.Write(ref s_instance._customerInstrumentationKey, customerInstrumentationKey);
                    Volatile.Write(ref s_instance._distroVersion, distroVersion);
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

        /// <summary>
        /// Records the outcome of a single Agent365 export request given the HTTP status code
        /// and elapsed time. Classifies the status code per the SDKStats spec and records the
        /// matching instrument(s) plus <c>Request_Duration</c>.
        /// </summary>
        /// <param name="requestHost">Host portion of the request URI; the stamp segment is extracted.</param>
        /// <param name="statusCode">HTTP status code returned by the Agent365 endpoint.</param>
        /// <param name="durationMs">Elapsed request time in milliseconds.</param>
        internal void TrackResponse(string? requestHost, int statusCode, double durationMs)
        {
            TrackDuration(requestHost, durationMs);

            if (statusCode == StatusSuccess)
            {
                TrackSuccess(requestHost);
                return;
            }

            // 206 (per-envelope partial) and 307/308 redirects are not terminal outcomes and
            // are not counted as success or failure (matches the exporter's Breeze handling).
            if (statusCode == StatusPartialSuccess
                || statusCode == StatusTemporaryRedirect
                || statusCode == StatusPermanentRedirect)
            {
                return;
            }

            if (DistroNetworkSdkStatsHelper.IsRetryable(statusCode))
            {
                TrackRetry(requestHost, statusCode);
            }
            else if (DistroNetworkSdkStatsHelper.IsThrottle(statusCode))
            {
                TrackThrottle(requestHost, statusCode);
            }
            else
            {
                TrackFailure(requestHost, statusCode);
            }
        }

        /// <summary>Record a successful (HTTP 200) transmission. Backs <c>Request_Success_Count</c>.</summary>
        internal void TrackSuccess(string? requestHost)
        {
            try
            {
                _requestSuccessCount.Add(1, BuildBaseTags(requestHost));
            }
            catch
            {
                // SDKStats are best-effort internal telemetry; never surface a recording failure.
            }
        }

        /// <summary>Record request duration regardless of outcome. Backs <c>Request_Duration</c>.</summary>
        internal void TrackDuration(string? requestHost, double durationMs)
        {
            try
            {
                _requestDuration.Record(durationMs, BuildBaseTags(requestHost));
            }
            catch
            {
                // See TrackSuccess.
            }
        }

        /// <summary>Record a non-retryable, non-throttling failure. Backs <c>Request_Failure_Count</c>.</summary>
        internal void TrackFailure(string? requestHost, int statusCode)
        {
            try
            {
                _requestFailureCount.Add(1, BuildStatusCodeTags(requestHost, statusCode));
            }
            catch
            {
                // See TrackSuccess.
            }
        }

        /// <summary>Record a retryable response. Backs <c>Retry_Count</c>.</summary>
        internal void TrackRetry(string? requestHost, int statusCode)
        {
            try
            {
                _retryCount.Add(1, BuildStatusCodeTags(requestHost, statusCode));
            }
            catch
            {
                // See TrackSuccess.
            }
        }

        /// <summary>Record a throttling response. Backs <c>Throttle_Count</c>.</summary>
        internal void TrackThrottle(string? requestHost, int statusCode)
        {
            try
            {
                _throttleCount.Add(1, BuildStatusCodeTags(requestHost, statusCode));
            }
            catch
            {
                // See TrackSuccess.
            }
        }

        /// <summary>Record an exception during the HTTP call (no response code). Backs <c>Exception_Count</c>.</summary>
        internal void TrackException(string? requestHost, string? exceptionType)
        {
            try
            {
                var tags = BuildBaseTags(requestHost);
                tags.Add("exceptionType", exceptionType ?? "unknown");
                _exceptionCount.Add(1, tags);
            }
            catch
            {
                // See TrackSuccess.
            }
        }

        private TagList BuildBaseTags(string? requestHost)
        {
            return new TagList
            {
                { "rp", ResourceProviderHelper.GetResourceProvider() },
                { "attach", ResourceProviderHelper.GetAttachMode() },
                { "cikey", Volatile.Read(ref _customerInstrumentationKey) },
                { "runtimeVersion", s_runtimeVersion },
                { "os", ResourceProviderHelper.GetOperatingSystem() },
                { "language", Language },
                { "version", Volatile.Read(ref _distroVersion) },
                { "endpoint", EndpointA365 },
                { "host", DistroNetworkSdkStatsHelper.ExtractStampHost(requestHost) },
            };
        }

        private TagList BuildStatusCodeTags(string? requestHost, int statusCode)
        {
            var tags = BuildBaseTags(requestHost);
            tags.Add("statusCode", statusCode);
            return tags;
        }

        private static string ResolveRuntimeVersion()
        {
            try
            {
                // Mirrors the exporter's runtimeVersion (assembly version of System.Private.CoreLib).
                return typeof(object).Assembly.GetName().Version?.ToString() ?? "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
