// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    [Collection(nameof(DistroFeatureSdkStatsCollection))]
    public class DistroFeatureSdkStatsTests
    {
        private const string ValidConnectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westus-0.in.applicationinsights.azure.com/";

        public DistroFeatureSdkStatsTests()
        {
            DistroFeatureSdkStats.ResetForTesting();
        }

        [Fact]
        public void Observe_ReturnsMeasurementWithExpectedTags()
        {
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.AzureMonitor,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "9.9.9-test")!;

            DistroFeatureSdkStats.Initialize(snapshot);

            var measurements = CollectObservableMeasurements();

            var match = Assert.Single(measurements, m => m.tags.TryGetValue("version", out var v) && (string?)v == "9.9.9-test");

            // The numeric value equals the feature mask.
            Assert.Equal((long)snapshot.Features, match.value);

            Assert.Equal("dotnet", match.tags["language"]);
            Assert.Equal(0, match.tags["type"]);
            Assert.Equal(snapshot.CustomerInstrumentationKey, match.tags["cikey"]);
            Assert.Equal((long)snapshot.Features, match.tags["feature"]);
            Assert.True(match.tags.ContainsKey("rp"));
            Assert.True(match.tags.ContainsKey("attach"));
            Assert.True(match.tags.ContainsKey("os"));
        }

        [Fact]
        public void Observe_WhenFeaturesAreNone_EmitsNoMeasurement()
        {
            // Exercises the spec-mandated short-circuit: when the snapshot's feature mask is
            // DistroFeature.None, the observable gauge MUST return zero measurements (not a
            // default Measurement<long>(), which would still publish a phantom zero data point
            // with no tags). Use the internal test factory to construct a None-masked snapshot
            // directly — DistroFeatureSnapshot.Build always sets at least Distro|AgentFramework
            // so it cannot produce a None snapshot through the normal code path.
            var snapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.None,
                customerInstrumentationKey: "N/A",
                distroVersion: "9.9.9-none");

            DistroFeatureSdkStats.Initialize(snapshot);

            var measurements = CollectObservableMeasurements();

            Assert.Empty(measurements);
        }

        [Fact]
        public void Observe_WithoutAzureMonitorConnectionString_UsesNAForCikey()
        {
            // Deployments without Azure Monitor (OTLP-only, Console-only, A365-only) still
            // report Feature SDKStats; the spec convention is to populate the cikey dimension
            // with the literal "N/A" so backend KQL doesn't need to filter out missing rows.
            var options = new MicrosoftOpenTelemetryOptions();
            // No ConnectionString set.

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                connectionString: null,
                ExportTarget.Otlp,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "9.9.9-otlp-only");

            Assert.NotNull(snapshot);
            Assert.Equal(DistroFeatureSnapshot.NoCustomerInstrumentationKey, snapshot!.CustomerInstrumentationKey);
            Assert.Equal("N/A", snapshot.CustomerInstrumentationKey);

            DistroFeatureSdkStats.Initialize(snapshot);
            var measurements = CollectObservableMeasurements();

            var match = Assert.Single(measurements, m => m.tags.TryGetValue("version", out var v) && (string?)v == "9.9.9-otlp-only");
            Assert.Equal("N/A", match.tags["cikey"]);
            Assert.Equal((long)snapshot.Features, match.value);
        }

        [Fact]
        public void Initialize_WithStatsbeatPin_DisposesPinOnReset()
        {
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;
            var snapshot = DistroFeatureSnapshot.Build(
                options, ValidConnectionString, ExportTarget.AzureMonitor, false, false, "9.9.9-pin")!;

            var pin = new TrackingDisposable();
            DistroFeatureSdkStats.Initialize(snapshot, pin);

            Assert.False(pin.Disposed);

            DistroFeatureSdkStats.ResetForTesting();

            Assert.True(pin.Disposed);
        }

        [Fact]
        public void Initialize_SecondPin_IsDisposedImmediately()
        {
            // The first pin wins for the process lifetime; a second pin supplied while one
            // is already held must be disposed immediately so we don't leak a transmitter.
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;
            var snapshot = DistroFeatureSnapshot.Build(
                options, ValidConnectionString, ExportTarget.AzureMonitor, false, false, "9.9.9-pin")!;

            var firstPin = new TrackingDisposable();
            var secondPin = new TrackingDisposable();

            DistroFeatureSdkStats.Initialize(snapshot, firstPin);
            DistroFeatureSdkStats.Initialize(snapshot, secondPin);

            Assert.False(firstPin.Disposed);
            Assert.True(secondPin.Disposed);
        }

        private sealed class TrackingDisposable : System.IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        private static List<(long value, Dictionary<string, object?> tags)> CollectObservableMeasurements()
        {
            var results = new List<(long value, Dictionary<string, object?> tags)>();

            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == DistroFeatureSdkStats.MeterName
                        && instrument.Name == DistroFeatureSdkStats.MetricName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };
            listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                var dict = new Dictionary<string, object?>(tags.Length);
                for (int i = 0; i < tags.Length; i++)
                {
                    dict[tags[i].Key] = tags[i].Value;
                }
                results.Add((value, dict));
            });
            listener.Start();
            listener.RecordObservableInstruments();
            return results;
        }
    }

    [CollectionDefinition(nameof(DistroFeatureSdkStatsCollection), DisableParallelization = true)]
    public class DistroFeatureSdkStatsCollection
    {
        // The DistroFeatureSdkStats singleton is process-wide; serialize tests that touch it.
    }
}
