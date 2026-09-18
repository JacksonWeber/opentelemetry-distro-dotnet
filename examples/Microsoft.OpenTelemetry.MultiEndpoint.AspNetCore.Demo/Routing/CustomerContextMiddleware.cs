// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace MultiEndpointDemo.Routing;

/// <summary>
/// Establishes the customer for the request: authorizes the caller, stamps an opaque identifier on
/// the request activity, and adds the routing dimensions to the ASP.NET Core request-duration metric.
/// </summary>
/// <remarks>
/// The identifier is written to the activity rather than to ambient state because the request
/// activity outlives the middleware pipeline, and child activities can reach it through
/// <see cref="Activity.Parent"/>.
/// </remarks>
public sealed class CustomerContextMiddleware(RequestDelegate next, CustomerCatalog catalog)
{
    public const string ApiKeyHeader = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        var customerId = catalog.AuthorizeCustomer(context.Request.Headers[ApiKeyHeader].FirstOrDefault());

        if (customerId is not null)
        {
            var destination = catalog.Resolve(customerId);

            context.Items[TelemetryNames.CustomerIdTag] = customerId;
            context.Items[TelemetryNames.DestinationItem] = destination;
            Activity.Current?.SetTag(TelemetryNames.CustomerIdTag, customerId);

            // Metric dimensions must be supplied while the measurement is being recorded, so they
            // cannot come from the processors above. The feature is absent when nothing is listening.
            var tags = context.Features.Get<IHttpMetricsTagsFeature>();

            if (destination is not null && tags is not null)
            {
                tags.Tags.Add(new(TelemetryNames.InstrumentationKey, destination.InstrumentationKey));
                tags.Tags.Add(new(TelemetryNames.IngestionEndpoint, destination.IngestionEndpoint));
                tags.Tags.Add(new(TelemetryNames.CloudRole, destination.CloudRole));
            }
        }

        await next(context);
    }
}
