// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.MultiInstance;

/// <summary>
/// Factory for isolated <see cref="TelemetryInstance"/> pipelines, enabling multiple Azure Monitor
/// resources to be targeted from a single process. This is the additive, opt-in entry point for the
/// distro's multi-instance support; the single-instance <c>UseMicrosoftOpenTelemetry</c> path is
/// unchanged.
/// </summary>
public static class MultiInstanceTelemetry
{
    /// <summary>
    /// Creates an isolated telemetry instance that exports to a single Azure Monitor resource. The
    /// instance builds its own standalone tracer, meter, and logging pipelines and is never
    /// registered as the process-global provider.
    /// </summary>
    /// <param name="name">Friendly name; also used as the Azure Monitor cloud role name.</param>
    /// <param name="connectionString">Azure Monitor connection string for this instance.</param>
    /// <param name="sharedSources">
    /// Names of <see cref="System.Diagnostics.ActivitySource"/>s shared across instances. Spans from
    /// these sources route to this instance only while one of its <see cref="TelemetryInstance.BeginScope"/>
    /// scopes is active.
    /// </param>
    /// <returns>An isolated <see cref="TelemetryInstance"/> handle.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="connectionString"/> is null or whitespace, or a
    /// <paramref name="sharedSources"/> entry is null or whitespace.
    /// </exception>
    public static TelemetryInstance CreateAzureMonitorInstance(
        string name,
        string connectionString,
        params string[] sharedSources)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Instance name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        sharedSources ??= Array.Empty<string>();
        foreach (var source in sharedSources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Shared source names cannot be null or whitespace.", nameof(sharedSources));
            }
        }

        return new TelemetryInstance(name, connectionString, sharedSources);
    }
}
