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
}
