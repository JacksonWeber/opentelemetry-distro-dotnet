// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Stateless helpers for distro-owned Network SDKStats: stamp-host extraction and the
    /// response-status classification (retryable / throttling) used by the failure / retry /
    /// throttle instruments. The Agent365 (<c>a365</c>) endpoint follows the same HTTP status
    /// semantics as Breeze per the SDKStats specification.
    /// </summary>
    internal static class DistroNetworkSdkStatsHelper
    {
        /// <summary>
        /// Extract the stamp-specific host segment from an ingestion endpoint host name
        /// (e.g. <c>westus2-1.in.applicationinsights.azure.com</c> → <c>westus2-1</c>).
        /// </summary>
        internal static string ExtractStampHost(string? requestHost)
        {
            if (string.IsNullOrEmpty(requestHost))
            {
                return "unknown";
            }

            const string wwwPrefix = "www.";
            string host = requestHost!;
            if (host.StartsWith(wwwPrefix, StringComparison.OrdinalIgnoreCase))
            {
                host = host.Substring(wwwPrefix.Length);
            }

            int firstDot = host.IndexOf('.');
            return firstDot > 0 ? host.Substring(0, firstDot) : host;
        }

        /// <summary>
        /// Whether a status code is retryable per the Network SDKStats spec
        /// (<c>a365</c> uses the Breeze code set): 401, 403, 408, 429, 500, 502, 503, or 504.
        /// </summary>
        internal static bool IsRetryable(int statusCode) =>
            statusCode == 401
            || statusCode == 403
            || statusCode == 408
            || statusCode == 429
            || statusCode == 500
            || statusCode == 502
            || statusCode == 503
            || statusCode == 504;

        /// <summary>
        /// Whether a status code is a throttling response per the Network SDKStats spec
        /// (<c>a365</c> uses the Breeze code set): 402 or 439.
        /// </summary>
        internal static bool IsThrottle(int statusCode) =>
            statusCode == 402
            || statusCode == 439;
    }
}
