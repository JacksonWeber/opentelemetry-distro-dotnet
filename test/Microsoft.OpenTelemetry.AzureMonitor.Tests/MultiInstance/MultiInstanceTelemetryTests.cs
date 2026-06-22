// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.OpenTelemetry.MultiInstance;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.MultiInstance;

/// <summary>
/// Verifies the distro's multi-instance support: isolated pipelines that route telemetry to
/// distinct destinations from a single process, with no fan-out between instances.
/// </summary>
public class MultiInstanceTelemetryTests
{
    private const string SharedSource = "Test.Shared.Workload";

    /// <summary>
    /// The core mechanic, exercised offline with in-memory exporters: a shared ActivitySource is
    /// routed to exactly one instance based on the active scope, and an unscoped span is dropped by
    /// every instance (no fan-out, no cross-talk).
    /// </summary>
    [Fact]
    public void StampAndGate_RouteSharedSourceSpansToActiveInstanceOnly()
    {
        var exportedA = new List<Activity>();
        var exportedB = new List<Activity>();

        using var providerA = BuildGatedProvider("A", exportedA);
        using var providerB = BuildGatedProvider("B", exportedB);
        using var source = new ActivitySource(SharedSource);

        using (AmbientInstance.Use("A"))
        {
            source.StartActivity("alpha")?.Dispose();
        }

        using (AmbientInstance.Use("B"))
        {
            source.StartActivity("beta")?.Dispose();
        }

        // No active scope -> no instance owns it.
        source.StartActivity("orphan")?.Dispose();

        providerA.ForceFlush();
        providerB.ForceFlush();

        Assert.Equal(new[] { "alpha" }, exportedA.Select(a => a.DisplayName));
        Assert.Equal(new[] { "beta" }, exportedB.Select(a => a.DisplayName));
    }

    /// <summary>
    /// End-to-end through the public API: two Azure Monitor instances isolate their spans whether
    /// routed by scope (shared source) or emitted via the direct handle. Asserted via the gate's
    /// forwarded-span count, which does not depend on network egress.
    /// </summary>
    [Fact]
    public void CreateAzureMonitorInstance_IsolatesSpansAcrossInstances()
    {
        using var a = MultiInstanceTelemetry.CreateAzureMonitorInstance("Telemetry A", FakeConnectionString(1), SharedSource);
        using var b = MultiInstanceTelemetry.CreateAzureMonitorInstance("Telemetry B", FakeConnectionString(2), SharedSource);
        using var source = new ActivitySource(SharedSource);

        using (a.BeginScope())
        {
            source.StartActivity("a1")?.Dispose();
            source.StartActivity("a2")?.Dispose();
        }

        using (b.BeginScope())
        {
            source.StartActivity("b1")?.Dispose();
        }

        // Unscoped shared-source span belongs to no instance.
        source.StartActivity("orphan")?.Dispose();

        // Direct handles always route to their own instance, no scope needed.
        a.ActivitySource.StartActivity("a-direct")?.Dispose();
        b.ActivitySource.StartActivity("b-direct")?.Dispose();

        Assert.Equal(3, a.ExportedSpanCount); // a1, a2, a-direct
        Assert.Equal(2, b.ExportedSpanCount); // b1, b-direct
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateAzureMonitorInstance_Throws_WhenNameMissing(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            MultiInstanceTelemetry.CreateAzureMonitorInstance(name!, FakeConnectionString(1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateAzureMonitorInstance_Throws_WhenConnectionStringMissing(string? connectionString)
    {
        Assert.Throws<ArgumentException>(() =>
            MultiInstanceTelemetry.CreateAzureMonitorInstance("Telemetry A", connectionString!));
    }

    [Fact]
    public void CreateAzureMonitorInstance_ExposesIsolatedHandles()
    {
        using var a = MultiInstanceTelemetry.CreateAzureMonitorInstance("Telemetry A", FakeConnectionString(1));
        using var b = MultiInstanceTelemetry.CreateAzureMonitorInstance("Telemetry B", FakeConnectionString(2));

        Assert.Equal("Telemetry A", a.Name);
        Assert.Equal("Telemetry B", b.Name);
        Assert.NotSame(a.ActivitySource, b.ActivitySource);
        Assert.NotSame(a.Meter, b.Meter);
        Assert.NotNull(a.Logger);
        Assert.NotNull(b.Logger);
    }

    private static TracerProvider BuildGatedProvider(string id, ICollection<Activity> sink)
        => Sdk.CreateTracerProviderBuilder()
            .SetSampler(new AlwaysOnSampler())
            .AddSource(SharedSource)
            .AddProcessor(new InstanceStampProcessor(id, $"{id}.Direct"))
            .AddProcessor(new GatingActivityExportProcessor(id, new InMemoryExporter<Activity>(sink)))
            .Build();

    // Syntactically valid connection string pointing at an unused local endpoint — no network is
    // required because the assertions rely on the in-process gate, not on successful egress.
    private static string FakeConnectionString(int n)
        => $"InstrumentationKey=0000000{n}-0000-0000-0000-00000000000{n};IngestionEndpoint=https://localhost/";
}
