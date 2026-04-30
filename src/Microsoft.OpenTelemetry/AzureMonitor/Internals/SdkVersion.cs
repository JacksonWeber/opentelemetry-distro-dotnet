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
        /// The distro version string (e.g. "1.0.0-beta.2"), lazily resolved from
        /// <see cref="AssemblyInformationalVersionAttribute"/> with the SourceLink
        /// commit hash suffix stripped.
        /// </summary>
        internal static readonly string Value = ResolveVersion();

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
