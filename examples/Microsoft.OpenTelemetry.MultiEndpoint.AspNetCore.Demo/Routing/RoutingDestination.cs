// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace MultiEndpointDemo.Routing;

/// <summary>
/// The destination for one customer: the two values routing requires, plus an optional cloud role.
/// </summary>
public sealed record RoutingDestination(
    string InstrumentationKey,
    string IngestionEndpoint,
    string CloudRole)
{
    /// <summary>
    /// Reads the two routing values out of a connection string.
    /// </summary>
    /// <remarks>
    /// Routing needs an explicit ingestion endpoint. The Application Insights connection string
    /// format also allows EndpointSuffix with an optional Location, and falls back to a default
    /// endpoint when neither is present; those forms are rejected here so a misconfigured customer
    /// fails at startup instead of having every telemetry item silently dropped.
    /// </remarks>
    public static RoutingDestination? FromConnectionString(string? connectionString, string cloudRole)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var values = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToLookup(part => part[0].Trim(), part => part[1].Trim(), StringComparer.OrdinalIgnoreCase);

        var key = values["InstrumentationKey"].FirstOrDefault();
        var endpoint = values["IngestionEndpoint"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        // Validate here so a bad endpoint fails at startup. The exporter applies the same rules at
        // export time, where the only symptom would be telemetry quietly going nowhere.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0)
        {
            return null;
        }

        return new RoutingDestination(key.Trim(), endpoint, cloudRole);
    }
}
