// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using OpenTelemetry.Logs;

namespace MultiEndpointDemo.Routing;

/// <summary>
/// Resolves the destination for a telemetry item. Implemented by the application, because only the
/// application knows which customer a given piece of work belongs to.
/// </summary>
public interface ICustomerRouting
{
    RoutingDestination? ForActivity(Activity activity);

    RoutingDestination? ForLogRecord(LogRecord record);
}

/// <summary>
/// Reads the customer identifier stamped on the activity by <see cref="CustomerContextMiddleware"/>
/// and looks the destination up in the server-owned <see cref="CustomerCatalog"/>.
/// </summary>
public sealed class CustomerRouting(CustomerCatalog catalog) : ICustomerRouting
{
    public RoutingDestination? ForActivity(Activity activity)
    {
        // Child activities - HttpClient, SQL, Azure SDK - never carry the tag themselves, so walk up
        // to the request activity that does.
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (current.GetTagItem(TelemetryNames.CustomerIdTag) is string customerId)
            {
                return catalog.Resolve(customerId);
            }
        }

        return null;
    }

    // Log records are emitted inline, so the request activity is still current.
    public RoutingDestination? ForLogRecord(LogRecord record) =>
        Activity.Current is { } activity ? ForActivity(activity) : null;
}
