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

        [Theory]
        [InlineData(TaskStatus.RanToCompletion)]
        [InlineData(TaskStatus.Faulted)]
        [InlineData(TaskStatus.Canceled)]
        public async Task ProcessAsync_ReturnsInnerValueTask(TaskStatus completionStatus)
        {
            DistroSdkStatsUsage.ResetForTesting();
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var inner = new RecordingTransport { AsyncResult = new ValueTask(completion.Task) };
            var transport = new LiveMetricsUsageTrackingTransport(inner);
            using var message = new HttpMessage(
                transport.CreateRequest(), new ResponseClassifier());
            message.Request.Method = RequestMethod.Post;
            message.Request.Uri.Reset(new Uri("https://example.test/QuickPulseService.svc/post"));

            var result = transport.ProcessAsync(message);

            Assert.Equal(inner.AsyncResult, result);
            Assert.False(result.IsCompleted);
            Assert.Same(message, inner.LastMessage);
            Assert.Equal(1, inner.AsyncCalls);
            Assert.Equal(DistroFeature.LiveMetrics, DistroSdkStatsUsage.Features);

            switch (completionStatus)
            {
                case TaskStatus.RanToCompletion:
                    completion.SetResult(null);
                    await result;
                    break;
                case TaskStatus.Faulted:
                    var failure = new InvalidOperationException("Transport failed asynchronously.");
                    completion.SetException(failure);
                    var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => result.AsTask());
                    Assert.Same(failure, actual);
                    break;
                case TaskStatus.Canceled:
                    completion.SetCanceled();
                    await Assert.ThrowsAsync<TaskCanceledException>(() => result.AsTask());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(completionStatus));
            }
        }

        private sealed class RecordingTransport : HttpPipelineTransport
        {
            internal HttpMessage? LastMessage { get; private set; }

            internal int SyncCalls { get; private set; }

            internal int AsyncCalls { get; private set; }

            internal Exception? Failure { get; set; }

            internal ValueTask AsyncResult { get; set; }

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
                return AsyncResult;
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
