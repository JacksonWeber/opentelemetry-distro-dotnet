// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.AzureMonitor.Internals;

/// <summary>
/// Environment variable constants used by the distro.
/// </summary>
internal static class EnvironmentVariableConstants
{
    internal const string APPLICATIONINSIGHTS_CONNECTION_STRING = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    internal const string OTEL_TRACES_SAMPLER = "OTEL_TRACES_SAMPLER";
    internal const string OTEL_TRACES_SAMPLER_ARG = "OTEL_TRACES_SAMPLER_ARG";

    /// <summary>
    /// Kill switch shared with the Azure Monitor exporter: when set to <c>"true"</c>
    /// (case-insensitive), the distro skips registering its Feature SDKStats producer.
    /// </summary>
    internal const string APPLICATIONINSIGHTS_STATSBEAT_DISABLED = "APPLICATIONINSIGHTS_STATSBEAT_DISABLED";

    /// <summary>
    /// Customer-facing SDK stats opt-in. When set to <c>"false"</c> (case-insensitive),
    /// the Azure Monitor exporter enables Customer SDK Stats and the distro reports the
    /// <see cref="SdkStats.DistroFeature.CustomerSdkStats"/> feature bit.
    /// </summary>
    internal const string APPLICATIONINSIGHTS_SDKSTATS_DISABLED = "APPLICATIONINSIGHTS_SDKSTATS_DISABLED";
}
