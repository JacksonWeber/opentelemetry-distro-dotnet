// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MultiEndpointDemo.Routing;

namespace MultiEndpointDemo;

/// <summary>
/// The application's own instruments. Routing dimensions are supplied at the measurement call,
/// because a metric's dimensions are part of its aggregation key and cannot be added later.
/// </summary>
public sealed class OrderMetrics
{
    private readonly Counter<long> _ordersPlaced;
    private readonly Histogram<double> _orderValue;

    public OrderMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(TelemetryNames.Meter);
        _ordersPlaced = meter.CreateCounter<long>("contoso.orders.placed");
        _orderValue = meter.CreateHistogram<double>("contoso.orders.value", unit: "USD");
    }

    public void OrderPlaced(RoutingDestination destination, double value, string region)
    {
        var tags = new TagList
        {
            { TelemetryNames.InstrumentationKey, destination.InstrumentationKey },
            { TelemetryNames.IngestionEndpoint, destination.IngestionEndpoint },
            { TelemetryNames.CloudRole, destination.CloudRole },

            // Keep business dimensions bounded: every combination multiplies the series count,
            // and the routing dimensions already multiply it by the customer count.
            { "contoso.region", region },
        };

        _ordersPlaced.Add(1, tags);
        _orderValue.Record(value, tags);
    }
}
