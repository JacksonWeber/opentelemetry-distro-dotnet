// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    [Collection("EnvironmentVariableTests")]
    public class DistroInstrumentationUsageProcessorTests
    {
        public DistroInstrumentationUsageProcessorTests()
        {
            DistroFeatureSdkStats.ResetForTesting();
        }

        [Fact]
        public void OnEnd_DoesNotReportDisabledOrNotYetUsedInstrumentations()
        {
            var processor = new DistroInstrumentationUsageProcessor(DistroInstrumentation.HttpClient);

            Assert.Equal(DistroInstrumentation.None, DistroSdkStatsUsage.Instrumentations);

            ProcessActivity(processor, "OpenTelemetry.Instrumentation.SqlClient");

            Assert.Equal(DistroInstrumentation.None, DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void OnEnd_AddsActualUsageMonotonically()
        {
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient);

            ProcessActivity(processor, "System.Net.Http");
            Assert.Equal(DistroInstrumentation.HttpClient, DistroSdkStatsUsage.Instrumentations);

            ProcessActivity(processor, "OpenTelemetry.Instrumentation.SqlClient");
            ProcessActivity(processor, "System.Net.Http");

            Assert.Equal(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void OnEnd_AgentFrameworkMarksFeatureAndInstrumentationUsage()
        {
            var processor = new DistroInstrumentationUsageProcessor(DistroInstrumentation.AgentFramework);

            ProcessActivity(processor, "Experimental.Microsoft.Agents.AI.Agent");

            Assert.Equal(DistroInstrumentation.AgentFramework, DistroSdkStatsUsage.Instrumentations);
            Assert.Equal(DistroFeature.AgentFramework, DistroSdkStatsUsage.Features);
        }

        [Fact]
        public void OnEnd_MicrosoftExtensionsAiMarksOpenAIOnly()
        {
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.OpenAI | DistroInstrumentation.AgentFramework);

            ProcessActivity(processor, "Experimental.Microsoft.Extensions.AI");

            Assert.Equal(DistroInstrumentation.OpenAI, DistroSdkStatsUsage.Instrumentations);
            Assert.Equal(DistroFeature.None, DistroSdkStatsUsage.Features);
        }

        [Fact]
        public void MeterListener_TracksActualMetricUsageWithoutTracing()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var httpMeter = new Meter("System.Net.Http");
            using var sqlMeter = new Meter("Microsoft.Data.SqlClient");
            var httpCounter = httpMeter.CreateCounter<long>("requests");
            var sqlCounter = sqlMeter.CreateCounter<long>("commands");

            sqlCounter.Add(1);
            Assert.Equal(DistroInstrumentation.None, DistroSdkStatsUsage.Instrumentations);

            httpCounter.Add(1);
            Assert.Equal(DistroInstrumentation.HttpClient, DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void MeterListener_AgentFrameworkMetricMarksFeatureAndInstrumentationUsage()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.AgentFramework);
            using var meter = new Meter("Experimental.Microsoft.Agents.AI.Agent");
            var counter = meter.CreateCounter<long>("requests");

            counter.Add(1);

            Assert.Equal(DistroInstrumentation.AgentFramework, DistroSdkStatsUsage.Instrumentations);
            Assert.Equal(DistroFeature.AgentFramework, DistroSdkStatsUsage.Features);
        }

        [Fact]
        public void MeterListener_TracksObservableMetricUsageWhenCollected()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            _ = meter.CreateObservableGauge("active-requests", () => 1L);

            Assert.Equal(DistroInstrumentation.None, DistroSdkStatsUsage.Instrumentations);

            usageListener.CollectObservableInstruments();

            Assert.Equal(DistroInstrumentation.HttpClient, DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void MeterListener_IgnoresAzureMonitorInternalHttpMetrics()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            var counter = meter.CreateCounter<long>("http.client.active_requests");

            counter.Add(
                1,
                new KeyValuePair<string, object?>(
                    "server.address",
                    "data.stats.monitor.azure.com"));

            Assert.Equal(DistroInstrumentation.None, DistroSdkStatsUsage.Instrumentations);

            counter.Add(
                1,
                new KeyValuePair<string, object?>(
                    "server.address",
                    "customer.example.com"));

            Assert.Equal(DistroInstrumentation.HttpClient, DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void MeterListener_IgnoresHttpMetricsInsideExporterSuppressionScope()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            var counter = meter.CreateCounter<long>("http.client.active_requests");

            using (DistroInstrumentationUsageMeterListener.SuppressHttpMetrics())
            {
                counter.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "server.address",
                        "custom-ingestion.example.com"));
            }

            Assert.Equal(DistroInstrumentation.None, DistroSdkStatsUsage.Instrumentations);
        }

        [Theory]
        [InlineData("westus2-2.in.applicationinsights.azure.com")]
        [InlineData("westeurope-5.stats.monitor.azure.com")]
        [InlineData("westus2-1.stats.monitor.azure.com")]
        public void MeterListener_IgnoresDelayedAzureMonitorObservableHttpMetrics(string host)
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            _ = meter.CreateObservableGauge(
                "http.client.open_connections",
                () => new Measurement<long>(
                    1,
                    new KeyValuePair<string, object?>("server.address", host)));

            usageListener.CollectObservableInstruments();

            Assert.Equal(DistroInstrumentation.None, DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void MeterListener_IgnoresDelayedDynamicallyRegisteredExporterHost()
        {
            const string host = "custom-exporter.internal.example";
            DistroInstrumentationUsageMeterListener.RegisterInternalHttpHost(host);
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            _ = meter.CreateObservableGauge(
                "http.client.open_connections",
                () => new Measurement<long>(
                    1,
                    new KeyValuePair<string, object?>("server.address", host)));

            usageListener.CollectObservableInstruments();

            Assert.Equal(DistroInstrumentation.None, DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void MeterListener_TracksDelayedCustomerObservableHttpMetrics()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            _ = meter.CreateObservableGauge(
                "http.client.open_connections",
                () => new Measurement<long>(
                    1,
                    new KeyValuePair<string, object?>(
                        "server.address",
                        "customer.example.com")));

            usageListener.CollectObservableInstruments();

            Assert.Equal(DistroInstrumentation.HttpClient, DistroSdkStatsUsage.Instrumentations);
        }

        [Theory]
        [InlineData("Azure.Core.Http", 1UL << 0)]
        [InlineData("Microsoft.AspNetCore.Hosting", 1UL << 1)]
        [InlineData("System.Net.Http", 1UL << 2)]
        [InlineData("OpenTelemetry.Instrumentation.SqlClient", 1UL << 3)]
        [InlineData("OpenAI.Chat", 1UL << 4)]
        [InlineData("Experimental.Microsoft.Extensions.AI", 1UL << 4)]
        [InlineData("Microsoft.SemanticKernel", 1UL << 5)]
        [InlineData("Experimental.Microsoft.Agents.AI", 1UL << 6)]
        [InlineData("Agent365Sdk", 1UL << 7)]
        public void GetInstrumentations_MapsKnownSources(
            string sourceName,
            ulong expected)
        {
            Assert.True(
                DistroInstrumentationUsageProcessor.GetInstrumentations(sourceName)
                    .HasFlag((DistroInstrumentation)expected));
        }

        [Fact]
        public void GetInstrumentations_IgnoresAzureMonitorExporterInternals()
        {
            Assert.Equal(
                DistroInstrumentation.None,
                DistroInstrumentationUsageProcessor.GetInstrumentations(
                    "Azure.Monitor.OpenTelemetry.Exporter.CustomerSdkStats"));
        }

        private static void ProcessActivity(
            DistroInstrumentationUsageProcessor processor,
            string sourceName)
        {
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(listener);

            using var source = new ActivitySource(sourceName);
            using var activity = source.StartActivity("test");
            Assert.NotNull(activity);
            activity!.Stop();
            processor.OnEnd(activity);
        }
    }
}
