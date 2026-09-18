// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using MultiEndpointDemo.Routing;

namespace MultiEndpointDemo;

/// <summary>
/// The application's own activity source. Register it with <c>AddSource</c> so its spans are
/// collected; the routing processor then attaches the destination like any other span.
/// </summary>
public static class DemoTelemetry
{
    public static readonly ActivitySource Source = new(TelemetryNames.ActivitySource);
}
