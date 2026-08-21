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
            var uri = message.Request.Uri.ToUri();
            TrackRequest(message.Request.Method, uri);
            DistroInstrumentationUsageMeterListener.RegisterInternalHttpHost(uri.Host);
            using var scope = DistroInstrumentationUsageMeterListener.SuppressHttpMetrics();
            _inner.Process(message);
        }

        public override async ValueTask ProcessAsync(HttpMessage message)
        {
            var uri = message.Request.Uri.ToUri();
            TrackRequest(message.Request.Method, uri);
            DistroInstrumentationUsageMeterListener.RegisterInternalHttpHost(uri.Host);
            using var scope = DistroInstrumentationUsageMeterListener.SuppressHttpMetrics();
            await _inner.ProcessAsync(message).ConfigureAwait(false);
        }

        public override void Update(HttpPipelineTransportOptions options) => _inner.Update(options);

        internal static void TrackRequest(RequestMethod method, Uri uri)
        {
            if (method == RequestMethod.Post
                && string.Equals(uri.AbsolutePath, LiveMetricsPostPath, StringComparison.OrdinalIgnoreCase))
            {
                DistroSdkStatsUsage.MarkFeatureInUse(DistroFeature.LiveMetrics);
            }
        }
    }
}
