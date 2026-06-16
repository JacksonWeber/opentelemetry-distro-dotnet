// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection;
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
        public void Observe_ThrottlesToSingleEmission_AcrossRapidCollections()
        {
            // The exporter collects this gauge on the shared 15-minute reader. Verify the
            // throttle holds it to one emission per 24 hr window so Feature stats don't ship
            // every 15 min.
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.AzureMonitor,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "9.9.9-throttle")!;

            DistroFeatureSdkStats.Initialize(snapshot);

            const int simulatedCollections = 5;
            int emissions = 0;
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
            listener.SetMeasurementEventCallback<long>((_, _, _, _) => emissions++);
            listener.Start();

            for (int i = 0; i < simulatedCollections; i++)
            {
                listener.RecordObservableInstruments();
            }

            // 5 collections at the 15-min cadence, but the throttle allows only one until the
            // 24 hr window elapses.
            Assert.Equal(1, emissions);
        }

        [Fact]
        public void Observe_EmitsAgain_WhenClockJumpsBackwards()
        {
            // Simulate the last emission being recorded ~48 hr in the future, i.e. the wall
            // clock has since jumped backwards (NTP/VM sync). The backwards-jump guard must
            // allow an emission now instead of suppressing until wall-clock time catches up.
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.AzureMonitor,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "9.9.9-clockback")!;

            DistroFeatureSdkStats.Initialize(snapshot);

            var instance = DistroFeatureSdkStats.Instance!;
            long futureTicks = DateTime.UtcNow.Ticks + TimeSpan.FromHours(48).Ticks;
            typeof(DistroFeatureSdkStats)
                .GetField("_lastEmissionTicks", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(instance, futureTicks);

            var measurements = CollectObservableMeasurements();

            Assert.Single(measurements);
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
