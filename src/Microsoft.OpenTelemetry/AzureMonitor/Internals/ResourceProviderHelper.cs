// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry.AzureMonitor.Internals
{
    /// <summary>
    /// Detects the Azure resource provider (App Service, Functions, AKS, VM) hosting the
    /// current process and reports the operating system. Mirrors the precedence and timeout
    /// behavior used by the Azure Monitor exporter's <c>AzureMonitorStatsbeat</c> so that
    /// distro-emitted Feature SDKStats carry the same <c>rp</c>/<c>os</c> dimension values as
    /// the exporter's <c>Attach</c> metric.
    /// </summary>
    internal static class ResourceProviderHelper
    {
        /// <summary>Azure Instance Metadata Service endpoint used for VM detection.</summary>
        internal const string AzureMetadataServiceUrl =
            "http://169.254.169.254/metadata/instance/compute?api-version=2017-08-01&format=json";

        private const string FunctionsWorkerRuntime = "FUNCTIONS_WORKER_RUNTIME";
        private const string WebsiteSiteName = "WEBSITE_SITE_NAME";
        private const string AksArmNamespaceId = "AKS_ARM_NAMESPACE_ID";

        private static readonly object s_lock = new();
        private static bool s_detected;
        private static string s_resourceProvider = "unknown";
        private static string s_operatingSystem = GetOs();

        /// <summary>
        /// Returns the detected resource provider name: <c>functions</c>, <c>appsvc</c>,
        /// <c>aks</c>, <c>vm</c>, or <c>unknown</c>. Cached after the first call.
        /// </summary>
        internal static string GetResourceProvider()
        {
            EnsureDetected();
            return s_resourceProvider;
        }

        /// <summary>
        /// Returns the operating system name: <c>windows</c>, <c>linux</c>, <c>osx</c>, or
        /// <c>unknown</c>. May be overridden from IMDS when the resource provider is <c>vm</c>.
        /// </summary>
        internal static string GetOperatingSystem()
        {
            EnsureDetected();
            return s_operatingSystem;
        }

        /// <summary>
        /// Returns the attach mode reported alongside Feature SDKStats. Always <c>Manual</c>
        /// for the distro today; auto-attach scenarios will be wired here when supported.
        /// </summary>
        internal static string GetAttachMode() => "Manual";

        /// <summary>
        /// Resets the cached detection state. Test-only.
        /// </summary>
        internal static void ResetForTesting()
        {
            lock (s_lock)
            {
                s_detected = false;
                s_resourceProvider = "unknown";
                s_operatingSystem = GetOs();
            }
        }

        private static void EnsureDetected()
        {
            if (s_detected)
            {
                return;
            }

            lock (s_lock)
            {
                if (s_detected)
                {
                    return;
                }

                s_resourceProvider = DetectResourceProvider();
                s_detected = true;
            }
        }

        private static string DetectResourceProvider()
        {
            // Precedence mirrors AzureMonitorStatsbeat.SetResourceProviderDetails:
            // Functions → AppSvc → AKS → IMDS (VM) → unknown.
            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(FunctionsWorkerRuntime)))
                {
                    return "functions";
                }

                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(WebsiteSiteName)))
                {
                    return "appsvc";
                }

                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AksArmNamespaceId)))
                {
                    return "aks";
                }
            }
            catch (Exception)
            {
                // Reading environment variables can throw under restricted security policies.
                // Treat as "unknown" rather than letting the gauge callback fail.
                return "unknown";
            }

            return TryGetVmMetadata() ? "vm" : "unknown";
        }

        private static bool TryGetVmMetadata()
        {
            try
            {
                // Suppress instrumentation so this internal HTTP call doesn't generate
                // an HttpClient activity / metric of its own.
                using var _ = SuppressInstrumentationScope.Begin();
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                httpClient.DefaultRequestHeaders.Add("Metadata", "True");

                var responseString = httpClient.GetStringAsync(new Uri(AzureMetadataServiceUrl)).GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(responseString);

                if (document.RootElement.TryGetProperty("osType", out var osType) && osType.ValueKind == JsonValueKind.String)
                {
                    var value = osType.GetString();
                    s_operatingSystem = string.IsNullOrEmpty(value) ? "unknown" : value!.ToLowerInvariant();
                }

                return true;
            }
            catch (Exception)
            {
                // Non-VM environments will throw (169.254.169.254 unreachable). Silently
                // fall back to "unknown" — this is best-effort telemetry, not user-visible.
                return false;
            }
        }

        private static string GetOs()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "windows";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "linux";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "osx";
            }

            return "unknown";
        }
    }
}
