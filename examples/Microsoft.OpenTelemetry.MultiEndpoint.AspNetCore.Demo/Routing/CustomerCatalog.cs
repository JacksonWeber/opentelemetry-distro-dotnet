// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Options;

namespace MultiEndpointDemo.Routing;

/// <summary>
/// The server-owned mapping from customer to destination. This is the trust boundary: a destination
/// is only ever chosen from here, never from anything the caller supplied.
/// </summary>
public sealed class CustomerCatalog
{
    private readonly Dictionary<string, string> _customerIdByApiKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoutingDestination> _destinationByCustomerId = new(StringComparer.Ordinal);

    public CustomerCatalog(IOptions<DemoOptions> options, ILogger<CustomerCatalog> logger)
    {
        foreach (var customer in options.Value.Customers)
        {
            if (string.IsNullOrWhiteSpace(customer.Id) || string.IsNullOrWhiteSpace(customer.ApiKey))
            {
                throw new InvalidOperationException("Every configured customer needs an Id and an ApiKey.");
            }

            var destination = RoutingDestination.FromConnectionString(
                customer.ConnectionString,
                customer.CloudRole ?? customer.Id);

            if (destination is null)
            {
                // Loud at startup beats telemetry vanishing at runtime.
                throw new InvalidOperationException(
                    $"Customer '{customer.Id}' needs a connection string with an InstrumentationKey and an " +
                    "explicit https IngestionEndpoint carrying no credentials, query string, or fragment.");
            }

            // A duplicate would quietly send one customer's telemetry to another's component.
            if (!_destinationByCustomerId.TryAdd(customer.Id, destination))
            {
                throw new InvalidOperationException($"Customer '{customer.Id}' is configured more than once.");
            }

            if (!_customerIdByApiKey.TryAdd(customer.ApiKey, customer.Id))
            {
                throw new InvalidOperationException($"Customer '{customer.Id}' reuses another customer's ApiKey.");
            }
        }

        logger.LogInformation("Loaded {CustomerCount} routed customers.", _destinationByCustomerId.Count);
    }

    public int Count => _destinationByCustomerId.Count;

    /// <summary>
    /// Stands in for authentication. A real service would authenticate the caller and map the
    /// resulting principal to a customer; only the lookup that follows would stay the same.
    /// </summary>
    public string? AuthorizeCustomer(string? apiKey) =>
        !string.IsNullOrEmpty(apiKey) && _customerIdByApiKey.TryGetValue(apiKey, out var customerId)
            ? customerId
            : null;

    public RoutingDestination? Resolve(string customerId) =>
        _destinationByCustomerId.TryGetValue(customerId, out var destination) ? destination : null;
}
