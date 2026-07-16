// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Reflection;

namespace Microsoft.OpenTelemetry.AzureMonitor.Internals
{
    /// <summary>
    /// Provides the distro's package version derived from the assembly metadata.
    /// </summary>
    internal static class SdkVersion
    {
        /// <summary>
        /// SDK Version component label for the Microsoft OpenTelemetry distro, per the
        /// <see href="https://github.com/microsoft/Telemetry-Collection-Spec">SDK Version spec</see>
        /// (<c>mot</c> = Microsoft OpenTelemetry distro). This is the highest-level component that
        /// initializes the exporter and emits distro-owned SDKStats.
        /// </summary>
        internal const string ComponentLabel = "mot";

        /// <summary>
        /// The distro version string (e.g. "1.0.0-beta.2"), lazily resolved from
        /// <see cref="AssemblyInformationalVersionAttribute"/> with the SourceLink
        /// commit hash suffix stripped.
        /// </summary>
        internal static readonly string Value = ResolveVersion();

        /// <summary>
        /// Formats the SDKStats <c>version</c> dimension by prefixing the distro package
        /// version with the <see cref="ComponentLabel"/> (e.g. "mot1.0.0-beta.2"). Per the
        /// SDKStats spec, this reports the version of the highest-level component that emitted
        /// the metric — the Microsoft OpenTelemetry distro — and matches the format the Azure
        /// Monitor exporter produces for the SDKStats it emits on the distro's behalf.
        /// </summary>
        /// <param name="distroVersion">The distro package version to label.</param>
        internal static string GetSdkStatsVersion(string distroVersion) => ComponentLabel + distroVersion;

        private static string ResolveVersion()
        {
            try
            {
                var attr = typeof(SdkVersion)
                    .Assembly
                    .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault();

                if (attr == null)
                {
                    return "unknown";
                }

                // InformationalVersion may contain build metadata after '+' (SourceLink hash).
                var version = attr.InformationalVersion;
                var plusIndex = version.IndexOf('+');
                return plusIndex >= 0 ? version.Substring(0, plusIndex) : version;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
