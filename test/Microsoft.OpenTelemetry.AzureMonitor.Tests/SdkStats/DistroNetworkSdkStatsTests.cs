// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    [Collection(nameof(DistroNetworkSdkStatsCollection))]
    public class DistroNetworkSdkStatsTests
    {
        public DistroNetworkSdkStatsTests()
        {
            DistroNetworkSdkStats.ResetForTesting();
        }

        [Fact]
        public void TrackResponse_Success_RecordsRequestSuccessWithExpectedTags()
        {
            DistroNetworkSdkStats.Initialize("N/A", "9.9.9-test");

            var measurements = Collect(() =>
                DistroNetworkSdkStats.Instance!.TrackResponse(
                    "westus2-1.in.applicationinsights.azure.com", statusCode: 200, durationMs: 12.5));

            var success = Assert.Single(measurements, m => m.instrument == "Request_Success_Count");
            Assert.Equal(1, success.value);
            Assert.Equal("a365", success.tags["endpoint"]);
            Assert.Equal("9.9.9-test", success.tags["version"]);
            Assert.Equal("N/A", success.tags["cikey"]);
            Assert.Equal("dotnet", success.tags["language"]);
            Assert.Equal("westus2-1", success.tags["host"]);
            Assert.True(success.tags.ContainsKey("rp"));
            Assert.True(success.tags.ContainsKey("attach"));
            Assert.True(success.tags.ContainsKey("os"));
            Assert.True(success.tags.ContainsKey("runtimeVersion"));

            // Duration is always recorded alongside the outcome.
            Assert.Contains(measurements, m => m.instrument == "Request_Duration");
        }

        [Theory]
        [InlineData(500, "Retry_Count")]
        [InlineData(429, "Retry_Count")]
        [InlineData(402, "Throttle_Count")]
        [InlineData(439, "Throttle_Count")]
        [InlineData(400, "Request_Failure_Count")]
        [InlineData(404, "Request_Failure_Count")]
        public void TrackResponse_ClassifiesStatusCodes(int statusCode, string expectedInstrument)
        {
            DistroNetworkSdkStats.Initialize("N/A", "9.9.9-test");

            var measurements = Collect(() =>
                DistroNetworkSdkStats.Instance!.TrackResponse("host.example.com", statusCode, durationMs: 1.0));

            var match = Assert.Single(measurements, m => m.instrument == expectedInstrument);
            Assert.Equal(1, match.value);
            Assert.Equal(statusCode, match.tags["statusCode"]);
            // Failure/retry/throttle never report a success for the same call.
            Assert.DoesNotContain(measurements, m => m.instrument == "Request_Success_Count");
        }

        [Theory]
        [InlineData(206)]
        [InlineData(307)]
        [InlineData(308)]
        public void TrackResponse_IgnoresPartialAndRedirects(int statusCode)
        {
            DistroNetworkSdkStats.Initialize("N/A", "9.9.9-test");

            var measurements = Collect(() =>
                DistroNetworkSdkStats.Instance!.TrackResponse("host.example.com", statusCode, durationMs: 1.0));

            // Only duration is recorded; no success/failure/retry/throttle.
            Assert.DoesNotContain(measurements, m => m.instrument == "Request_Success_Count");
            Assert.DoesNotContain(measurements, m => m.instrument == "Request_Failure_Count");
            Assert.DoesNotContain(measurements, m => m.instrument == "Retry_Count");
            Assert.DoesNotContain(measurements, m => m.instrument == "Throttle_Count");
            Assert.Contains(measurements, m => m.instrument == "Request_Duration");
        }

        [Fact]
        public void TrackException_RecordsExceptionWithType()
        {
            DistroNetworkSdkStats.Initialize("ikey-123", "9.9.9-test");

            var measurements = Collect(() =>
                DistroNetworkSdkStats.Instance!.TrackException(
                    requestHost: null, exceptionType: "System.Net.Http.HttpRequestException"));

            var ex = Assert.Single(measurements, m => m.instrument == "Exception_Count");
            Assert.Equal(1, ex.value);
            Assert.Equal("System.Net.Http.HttpRequestException", ex.tags["exceptionType"]);
            Assert.Equal("ikey-123", ex.tags["cikey"]);
            // Null host falls back to "unknown" per the helper.
            Assert.Equal("unknown", ex.tags["host"]);
        }

        [Fact]
        public void Initialize_IsIdempotent_AndUpdatesContext()
        {
            var first = DistroNetworkSdkStats.Initialize("N/A", "1.0.0");
            var second = DistroNetworkSdkStats.Initialize("ikey-xyz", "2.0.0");

            Assert.Same(first, second);

            var measurements = Collect(() =>
                DistroNetworkSdkStats.Instance!.TrackSuccess("host.example.com"));

            var success = Assert.Single(measurements, m => m.instrument == "Request_Success_Count");
            Assert.Equal("ikey-xyz", success.tags["cikey"]);
            Assert.Equal("2.0.0", success.tags["version"]);
        }

        private static List<(string instrument, long value, Dictionary<string, object?> tags)> Collect(System.Action record)
        {
            var results = new List<(string instrument, long value, Dictionary<string, object?> tags)>();

            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == DistroNetworkSdkStats.MeterName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };

            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                results.Add((instrument.Name, value, ToDict(tags))));
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                results.Add((instrument.Name, (long)value, ToDict(tags))));
            listener.Start();

            record();
            return results;
        }

        private static Dictionary<string, object?> ToDict(System.ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dict = new Dictionary<string, object?>(tags.Length);
            for (int i = 0; i < tags.Length; i++)
            {
                dict[tags[i].Key] = tags[i].Value;
            }

            return dict;
        }
    }

    [CollectionDefinition(nameof(DistroNetworkSdkStatsCollection), DisableParallelization = true)]
    public class DistroNetworkSdkStatsCollection
    {
        // The DistroNetworkSdkStats singleton is process-wide; serialize tests that touch it.
    }
}
