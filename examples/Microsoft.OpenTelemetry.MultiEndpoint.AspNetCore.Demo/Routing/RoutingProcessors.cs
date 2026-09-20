// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace MultiEndpointDemo.Routing;

/// <summary>
/// Stamps the destination on every activity, including those created by instrumentation libraries.
/// </summary>
public sealed class RoutingActivityProcessor(ICustomerRouting routing) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        var destination = routing.ForActivity(activity);

        // SetTag replaces an existing value, and a null value removes the tag, so an activity with no
        // resolved customer cannot keep a stale destination and be routed somewhere unintended.
        activity.SetTag(TelemetryNames.InstrumentationKey, destination?.InstrumentationKey);
        activity.SetTag(TelemetryNames.IngestionEndpoint, destination?.IngestionEndpoint);
        activity.SetTag(TelemetryNames.CloudRole, destination?.CloudRole);
    }
}

/// <summary>
/// Stamps the destination on every log record, so ordinary ILogger calls need no changes.
/// </summary>
public sealed class RoutingLogProcessor(ICustomerRouting routing) : BaseProcessor<LogRecord>
{
    private static readonly string[] RoutingKeys =
    [
        TelemetryNames.InstrumentationKey,
        TelemetryNames.IngestionEndpoint,
        TelemetryNames.CloudRole,
    ];

    public override void OnEnd(LogRecord record)
    {
        var destination = routing.ForLogRecord(record);
        var existing = record.Attributes ?? [];
        var carriesRoutingKey = existing.Any(attribute => RoutingKeys.Contains(attribute.Key));

        if (destination is null && !carriesRoutingKey)
        {
            // Nothing to add and nothing stale to strip: leave the record untouched. It has no
            // destination, so it is dropped.
            return;
        }

        // Routing reads the first occurrence of each key, so appending would lose to a pre-existing
        // value. Rebuild the list without the routing keys, then add the trusted ones.
        var attributes = existing
            .Where(attribute => !RoutingKeys.Contains(attribute.Key))
            .ToList();

        if (destination is not null)
        {
            attributes.Add(new(TelemetryNames.InstrumentationKey, destination.InstrumentationKey));
            attributes.Add(new(TelemetryNames.IngestionEndpoint, destination.IngestionEndpoint));
            attributes.Add(new(TelemetryNames.CloudRole, destination.CloudRole));
        }

        record.Attributes = attributes;
    }
}
