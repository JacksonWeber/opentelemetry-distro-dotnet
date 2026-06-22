// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry.MultiInstance;

/// <summary>
/// A <see cref="BatchActivityExportProcessor"/> that forwards only activities stamped with its own
/// instance id, dropping the rest. This prevents fan-out when several isolated providers listen to
/// the same shared source. It subclasses the batch processor (rather than wrapping it) so the SDK's
/// <c>SetParentProvider</c> wiring stays intact and the exporter keeps this instance's Resource.
/// </summary>
internal sealed class GatingActivityExportProcessor : BatchActivityExportProcessor
{
    private readonly string _instanceId;
    private readonly Action<Activity>? _onForwarded;

    public GatingActivityExportProcessor(string instanceId, BaseExporter<Activity> exporter, Action<Activity>? onForwarded = null)
        : base(exporter)
    {
        _instanceId = instanceId;
        _onForwarded = onForwarded;
    }

    public override void OnEnd(Activity data)
    {
        if ((data.GetCustomProperty(InstanceStampProcessor.PropertyName) as string) == _instanceId)
        {
            _onForwarded?.Invoke(data);
            base.OnEnd(data);
        }
    }
}
