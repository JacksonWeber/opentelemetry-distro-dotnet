// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry.MultiInstance;

/// <summary>
/// A <see cref="BatchActivityExportProcessor"/> that only forwards activities stamped with its own
/// telemetry-instance id, dropping everything else. This is what prevents fan-out: when several
/// isolated <c>TracerProvider</c>s listen to the same shared <c>ActivitySource</c>, each one sees
/// every activity, but only the matching instance actually exports it.
/// </summary>
/// <remarks>
/// We subclass the real batch processor rather than wrapping it because the OpenTelemetry SDK wires
/// the registered processor directly via <c>SetParentProvider</c> (which is not overridable).
/// Subclassing keeps that wiring intact, so the genuine Azure Monitor exporter still receives this
/// instance's <c>Resource</c> — preserving the per-instance cloud role name.
/// </remarks>
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
