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
            DistroInstrumentationUsageMeterListener.ResetInternalHttpHostsForTesting();
        }

        [Fact]
        public void OnEnd_DoesNotReportDisabledOrNonmatchingInstrumentations()
        {
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.HttpClient);

            ProcessActivity(processor, "OpenTelemetry.Instrumentation.SqlClient");
            ProcessActivity(processor, "Customer.CustomSource");

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void OnEnd_AddsActualUsageMonotonically()
        {
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient);

            ProcessActivity(processor, "System.Net.Http");
            Assert.Equal(
                DistroInstrumentation.HttpClient,
                DistroSdkStatsUsage.Instrumentations);

            ProcessActivity(processor, "OpenTelemetry.Instrumentation.SqlClient");
            ProcessActivity(processor, "System.Net.Http");

            Assert.Equal(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void OnEnd_AgentFrameworkUpdatesOnlyInstrumentationUsage()
        {
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.AgentFramework);

            ProcessActivity(processor, "Experimental.Microsoft.Agents.AI.Agent");

            Assert.Equal(
                DistroInstrumentation.AgentFramework,
                DistroSdkStatsUsage.Instrumentations);
            Assert.Equal(DistroFeature.None, DistroSdkStatsUsage.Features);
        }

        [Fact]
        public void OnEnd_MicrosoftExtensionsAiMarksOpenAIOnly()
        {
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.OpenAI | DistroInstrumentation.AgentFramework);

            ProcessActivity(processor, "Experimental.Microsoft.Extensions.AI");

            Assert.Equal(
                DistroInstrumentation.OpenAI,
                DistroSdkStatsUsage.Instrumentations);
            Assert.Equal(DistroFeature.None, DistroSdkStatsUsage.Features);
        }

        [Fact]
        public void GetEnabledInstrumentations_UsesOnlyEnabledOptions()
        {
            var options = new InstrumentationOptions
            {
                EnableAzureSdkInstrumentation = false,
                EnableAspNetCoreInstrumentation = true,
                EnableHttpClientInstrumentation = false,
                EnableSqlClientInstrumentation = true,
                EnableOpenAIInstrumentation = false,
                EnableSemanticKernelInstrumentation = true,
                EnableAgentFrameworkInstrumentation = false,
                EnableAgent365Instrumentation = true,
            };

            Assert.Equal(
                DistroInstrumentation.AspNetCore
                    | DistroInstrumentation.SqlClient
                    | DistroInstrumentation.SemanticKernel
                    | DistroInstrumentation.Agent365,
                DistroInstrumentationUsageProcessor.GetEnabledInstrumentations(options));
        }

        [Fact]
        public void MeterListener_TracksActualMetricUsageWithEnabledIntersection()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var httpMeter = new Meter("System.Net.Http");
            using var sqlMeter = new Meter("Microsoft.Data.SqlClient");
            using var customMeter = new Meter("Customer.CustomMeter");
            var httpCounter = httpMeter.CreateCounter<long>("requests");
            var sqlCounter = sqlMeter.CreateCounter<long>("commands");
            var customCounter = customMeter.CreateCounter<long>("operations");

            sqlCounter.Add(1);
            customCounter.Add(1);
            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);

            httpCounter.Add(1);
            Assert.Equal(
                DistroInstrumentation.HttpClient,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void MeterListener_TracksObservableMetricUsageWhenCollected()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            _ = meter.CreateObservableGauge("active-requests", () => 1L);

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);

            usageListener.CollectObservableInstruments();

            Assert.Equal(
                DistroInstrumentation.HttpClient,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public async Task MeterListener_SerializesObservableCollection()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            using var firstCallbackStarted = new ManualResetEventSlim();
            using var releaseFirstCallback = new ManualResetEventSlim();
            var callbackCount = 0;
            _ = meter.CreateObservableGauge(
                "active-requests",
                () =>
                {
                    var count = Interlocked.Increment(ref callbackCount);
                    if (count == 1)
                    {
                        firstCallbackStarted.Set();
                        releaseFirstCallback.Wait(TimeSpan.FromSeconds(5));
                    }

                    return 1L;
                });

            var firstCollection = Task.Run(
                usageListener.CollectObservableInstruments);
            Assert.True(firstCallbackStarted.Wait(TimeSpan.FromSeconds(5)));
            var secondCollection = Task.Run(
                usageListener.CollectObservableInstruments);

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.Equal(1, Volatile.Read(ref callbackCount));

            releaseFirstCallback.Set();
            await Task.WhenAll(firstCollection, secondCollection);
            Assert.Equal(2, Volatile.Read(ref callbackCount));
        }

        [Theory]
        [InlineData("169.254.169.254")]
        [InlineData("dc.services.visualstudio.com")]
        [InlineData("rt.services.visualstudio.com")]
        [InlineData("westus2-2.in.applicationinsights.azure.com")]
        [InlineData("westeurope-5.stats.monitor.azure.com")]
        [InlineData("westus2-1.livediagnostics.monitor.azure.com")]
        [InlineData("westeurope-5.stats.monitor.azure.com.")]
        public void MeterListener_IgnoresDelayedAzureMonitorObservableHttpMetrics(
            string host)
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

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public async Task MeterListener_IgnoresHttpMetricsInsideAsyncSuppressionScope()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            var counter = meter.CreateCounter<long>("http.client.active_requests");

            using (DistroInstrumentationUsageMeterListener.SuppressHttpMetrics())
            {
                await Task.Yield();
                counter.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "server.address",
                        "custom-ingestion.example.com"));
            }

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void MeterListener_IgnoresDelayedDynamicallyRegisteredExporterHost()
        {
            const string Host = "custom-exporter.internal.example";
            DistroInstrumentationUsageMeterListener.RegisterInternalHttpHost(
                $"  {Host}.  ");
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            using var meter = new Meter("System.Net.Http");
            _ = meter.CreateObservableGauge(
                "http.client.open_connections",
                () => new Measurement<long>(
                    1,
                    new KeyValuePair<string, object?>("server.address", Host)));

            usageListener.CollectObservableInstruments();

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
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

            Assert.Equal(
                DistroInstrumentation.HttpClient,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Theory]
        [InlineData("Azure.Core.Http", 1UL << 0)]
        [InlineData("Microsoft.AspNetCore.Hosting", 1UL << 1)]
        [InlineData("System.Net.Http", 1UL << 2)]
        [InlineData("OpenTelemetry.Instrumentation.SqlClient", 1UL << 3)]
        [InlineData("Microsoft.Data.SqlClient", 1UL << 3)]
        [InlineData("System.Data.SqlClient", 1UL << 3)]
        [InlineData("Azure.AI.OpenAI", (1UL << 0) | (1UL << 4))]
        [InlineData("OpenAI.Chat", 1UL << 4)]
        [InlineData("Experimental.Microsoft.Extensions.AI", 1UL << 4)]
        [InlineData("Microsoft.SemanticKernel", 1UL << 5)]
        [InlineData("Experimental.Microsoft.Agents.AI", 1UL << 6)]
        [InlineData("Agent365Sdk", 1UL << 7)]
        public void GetInstrumentations_MapsKnownSourcesAndMeters(
            string sourceName,
            ulong expected)
        {
            Assert.Equal(
                (DistroInstrumentation)expected,
                DistroInstrumentationUsageProcessor.GetInstrumentations(sourceName));
        }

        [Theory]
        [InlineData("Azure.Monitor.OpenTelemetry")]
        [InlineData("Azure.Monitor.OpenTelemetry.Exporter")]
        [InlineData("Azure.Monitor.OpenTelemetry.Exporter.CustomerSdkStats")]
        [InlineData("Experimental.Microsoft.Extensions.AI.Agent")]
        [InlineData("Microsoft.Extensions.AI")]
        [InlineData("Experimental.Microsoft.Agents")]
        [InlineData("Customer.System.Net.Http")]
        [InlineData("Agent365Sdk.Extensions")]
        public void GetInstrumentations_IgnoresNonmatchingAndExporterOwnedNames(
            string sourceName)
        {
            Assert.Equal(
                DistroInstrumentation.None,
                DistroInstrumentationUsageProcessor.GetInstrumentations(sourceName));
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
