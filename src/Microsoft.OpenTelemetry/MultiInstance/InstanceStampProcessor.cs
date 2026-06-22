// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry.MultiInstance;

/// <summary>
/// Stamps each <see cref="Activity"/> with the owning telemetry-instance id at start time, so the
/// id survives the hop to the exporter's background thread (where <see cref="AmbientInstance"/> is
/// no longer available).
/// </summary>
/// <remarks>
/// Runs on the application thread inside <c>ActivitySource.StartActivity</c>, where the ambient
/// value is present. Two stamping rules apply:
/// <list type="number">
///   <item>Activities from this instance's own private source are always stamped with this
///   instance id (direct-handle usage needs no ambient scope).</item>
///   <item>Activities from shared sources are stamped with the ambient instance id, so the global
///   OpenTelemetry API routes to whichever instance is current.</item>
/// </list>
/// Stamping is idempotent: the first writer wins, and concurrent instances writing the same ambient
/// value is harmless.
/// </remarks>
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
            // Direct-handle activity for this instance — always belongs to it.
            data.SetCustomProperty(PropertyName, _instanceId);
        }
        else if (AmbientInstance.CurrentId is { } ambient)
        {
            // Shared-source activity routed by the active "run-with-instance" scope.
            data.SetCustomProperty(PropertyName, ambient);
        }
    }
}
