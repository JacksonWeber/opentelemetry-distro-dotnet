// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net.Http.Metrics;

namespace MultiEndpointDemo.Routing;

/// <summary>
/// Adds the routing dimensions to <c>http.client.request.duration</c> for outgoing calls.
/// </summary>
/// <remarks>
/// The trace and log processors cannot help here: a metric's dimensions are part of its aggregation
/// key, so they have to be present when the measurement is recorded.
/// </remarks>
public sealed class RoutingMetricsHandler(ICustomerRouting routing) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var destination = Activity.Current is { } activity ? routing.ForActivity(activity) : null;

        if (destination is not null)
        {
            HttpMetricsEnrichmentContext.AddCallback(request, context =>
            {
                context.AddCustomTag(TelemetryNames.InstrumentationKey, destination.InstrumentationKey);
                context.AddCustomTag(TelemetryNames.IngestionEndpoint, destination.IngestionEndpoint);
                context.AddCustomTag(TelemetryNames.CloudRole, destination.CloudRole);
            });
        }

        return base.SendAsync(request, cancellationToken);
    }
}
