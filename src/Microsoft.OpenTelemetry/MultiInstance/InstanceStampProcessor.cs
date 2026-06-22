// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry.MultiInstance;

/// <summary>
/// Stamps each <see cref="Activity"/> with its owning telemetry-instance id at start, so the id
/// survives the hop to the exporter's background thread (where <see cref="AmbientInstance"/> is
/// unavailable). Activities from this instance's private source are always stamped with its id;
/// activities from shared sources are stamped with the current scope's id. Stamping is idempotent.
/// </summary>
internal sealed class InstanceStampProcessor : BaseProcessor<Activity>
{
    internal const string PropertyName = "msi.instance";

    private readonly string _instanceId;
    private readonly string _privateSourceName;

    public InstanceStampProcessor(string instanceId, string privateSourceName)
    {
        _instanceId = instanceId;
        _privateSourceName = privateSourceName;
    }

    public override void OnStart(Activity data)
    {
        if (data.GetCustomProperty(PropertyName) is not null)
        {
            return;
        }

        if (data.Source.Name == _privateSourceName)
        {
            // Direct-handle activity always belongs to this instance.
            data.SetCustomProperty(PropertyName, _instanceId);
        }
        else if (AmbientInstance.CurrentId is { } ambient)
        {
            // Shared-source activity routed by the active scope.
            data.SetCustomProperty(PropertyName, ambient);
        }
    }
}
