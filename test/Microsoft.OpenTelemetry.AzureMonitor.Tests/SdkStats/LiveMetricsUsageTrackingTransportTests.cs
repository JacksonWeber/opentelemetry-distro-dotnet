// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    [Collection("EnvironmentVariableTests")]
    public class LiveMetricsUsageTrackingTransportTests
    {
        [Theory]
        [InlineData("POST", "/QuickPulseService.svc/post", true)]
        [InlineData("POST", "/quickpulseservice.svc/POST", true)]
        [InlineData("POST", "/QuickPulseService.svc/ping", false)]
        [InlineData("GET", "/QuickPulseService.svc/post", false)]
        [InlineData("POST", "/v2.1/track", false)]
        [InlineData("POST", "/QuickPulseService.svc/post/extra", false)]
        public async Task Process_TracksOnlyActiveCollectionAndForwardsRequests(
            string method,
            string path,
            bool expectedLiveMetrics)
        {
            var inner = new RecordingTransport();
            var transport = new LiveMetricsUsageTrackingTransport(inner);

            foreach (var async in new[] { false, true })
            {
                DistroSdkStatsUsage.ResetForTesting();
                using var message = new HttpMessage(
                    transport.CreateRequest(), new ResponseClassifier());
                message.Request.Method = new RequestMethod(method);
                message.Request.Uri.Reset(new Uri("https://example.test" + path));

                if (async)
                {
                    await transport.ProcessAsync(message);
                }
                else
                {
                    transport.Process(message);
                }

                Assert.Same(message, inner.LastMessage);
                Assert.Equal(
                    expectedLiveMetrics ? DistroFeature.LiveMetrics : DistroFeature.None,
                    DistroSdkStatsUsage.Features);
                Assert.Equal(DistroInstrumentation.None, DistroSdkStatsUsage.Instrumentations);
            }

            Assert.Equal(1, inner.SyncCalls);
            Assert.Equal(1, inner.AsyncCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Process_PreservesTransportFailures(bool async)
        {
            DistroSdkStatsUsage.ResetForTesting();
            var failure = new InvalidOperationException("Transport failed.");
            var inner = new RecordingTransport { Failure = failure };
            var transport = new LiveMetricsUsageTrackingTransport(inner);
            using var message = new HttpMessage(
                transport.CreateRequest(), new ResponseClassifier());
            message.Request.Method = RequestMethod.Post;
            message.Request.Uri.Reset(new Uri("https://example.test/QuickPulseService.svc/post"));

            var actual = async
                ? await Assert.ThrowsAsync<InvalidOperationException>(
                    () => transport.ProcessAsync(message).AsTask())
                : Assert.Throws<InvalidOperationException>(() => transport.Process(message));

            Assert.Same(failure, actual);
            Assert.Equal(DistroFeature.LiveMetrics, DistroSdkStatsUsage.Features);
        }

        private sealed class RecordingTransport : HttpPipelineTransport
        {
            internal HttpMessage? LastMessage { get; private set; }

            internal int SyncCalls { get; private set; }

            internal int AsyncCalls { get; private set; }

            internal Exception? Failure { get; set; }

            public override Request CreateRequest() => HttpClientTransport.Shared.CreateRequest();

            public override void Process(HttpMessage message)
            {
                SyncCalls++;
                Record(message);
            }

            public override ValueTask ProcessAsync(HttpMessage message)
            {
                AsyncCalls++;
                Record(message);
                return default;
            }

            private void Record(HttpMessage message)
            {
                LastMessage = message;
                if (Failure != null)
                {
                    throw Failure;
                }
            }
        }
    }
}
