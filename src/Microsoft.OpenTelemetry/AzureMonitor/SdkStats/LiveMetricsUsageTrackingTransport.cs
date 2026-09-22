// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Core.Pipeline;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Delegating Azure Core transport that recognizes Live Metrics active-collection posts.
    /// Ping traffic only means Live Metrics is enabled; the service sends the SDK into the
    /// post state only while a user is actively subscribed in the portal.
    /// Detection uses outgoing requests only, without polling or additional network calls.
    /// Request inspection stops once Live Metrics has been observed in this process.
    /// </summary>
    internal sealed class LiveMetricsUsageTrackingTransport : HttpPipelineTransport
    {
        private const string LiveMetricsPostPath = "/QuickPulseService.svc/post";
        private readonly HttpPipelineTransport _inner;

        internal LiveMetricsUsageTrackingTransport(HttpPipelineTransport inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        internal HttpPipelineTransport InnerTransport => _inner;

        public override Request CreateRequest() => _inner.CreateRequest();

        public override void Process(HttpMessage message)
        {
            TrackRequest(message);
            _inner.Process(message);
        }

        public override ValueTask ProcessAsync(HttpMessage message)
        {
            TrackRequest(message);
            return _inner.ProcessAsync(message);
        }

        public override void Update(HttpPipelineTransportOptions options) => _inner.Update(options);

        internal static void TrackRequest(RequestMethod method, string path)
        {
            if (method == RequestMethod.Post
                && string.Equals(path, LiveMetricsPostPath, StringComparison.OrdinalIgnoreCase))
            {
                DistroSdkStatsUsage.MarkFeatureInUse(DistroFeature.LiveMetrics);
            }
        }

        private static void TrackRequest(HttpMessage message)
        {
            if ((DistroSdkStatsUsage.Features & DistroFeature.LiveMetrics) != 0)
            {
                return;
            }

            var request = message.Request;
            if (request.Method == RequestMethod.Post)
            {
                TrackRequest(RequestMethod.Post, request.Uri.Path);
            }
        }
    }
}
