// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace MultiEndpointDemo;

/// <summary>
/// The demo's customer catalogue, bound from the <c>MultiEndpointDemo</c> configuration section.
/// </summary>
public sealed class DemoOptions
{
    public List<CustomerOptions> Customers { get; set; } = [];
}

public sealed class CustomerOptions
{
    /// <summary>Stable identifier stamped on telemetry; never a secret.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Stands in for a real credential. See the README before copying this pattern.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Must contain an explicit IngestionEndpoint for routing.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    public string? CloudRole { get; set; }
}
