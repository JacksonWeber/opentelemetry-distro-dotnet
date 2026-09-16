// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
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
        public void DefaultObservationWindow_IsTenMinutes()
        {
            Assert.Equal(
                TimeSpan.FromMinutes(10),
                DistroInstrumentationUsageProcessor.DefaultObservationWindow);
        }

        [Fact]
        public void OnEnd_DoesNotReportDisabledOrNonmatchingSources()
        {
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.HttpClient);

            ProcessActivity(processor, "OpenTelemetry.Instrumentation.SqlClient.Tests");
            ProcessActivity(processor, "Customer.CustomSource");

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
            Assert.True(processor.HasRemainingInstrumentations);
        }

        [Theory]
        [InlineData("OpenTelemetry.Instrumentation.AspNetCore", 1UL << 1, true)]
        [InlineData("OpenTelemetry.Instrumentation.AspNetCore", 1UL << 1, false)]
        [InlineData("OpenTelemetry.Instrumentation.Http.HttpClient", 1UL << 2, true)]
        [InlineData("OpenTelemetry.Instrumentation.Http.HttpClient", 1UL << 2, false)]
        [InlineData("OpenTelemetry.Instrumentation.Http.HttpWebRequest", 1UL << 2, true)]
        [InlineData("OpenTelemetry.Instrumentation.Http.HttpWebRequest", 1UL << 2, false)]
        public void OnEnd_OnlyReportsEnabledLegacyInstrumentations(
            string sourceName,
            ulong instrumentation,
            bool enabled)
        {
            var expected = enabled
                ? (DistroInstrumentation)instrumentation
                : DistroInstrumentation.None;
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.SqlClient | expected);

            ProcessActivity(processor, sourceName);

            Assert.Equal(expected, DistroSdkStatsUsage.Instrumentations);
            Assert.True(processor.HasRemainingInstrumentations);
        }

        [Fact]
        public void OnEnd_TracksCompletedActivitiesAndSkipsDuplicates()
        {
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient);

            ProcessActivity(processor, "System.Net.Http.Tests");
            Assert.Equal(
                DistroInstrumentation.HttpClient,
                DistroSdkStatsUsage.Instrumentations);
            Assert.True(processor.HasRemainingInstrumentations);

            ProcessActivity(processor, "System.Net.Http.Tests");
            ProcessActivity(processor, "OpenTelemetry.Instrumentation.SqlClient.Tests");

            Assert.Equal(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient,
                DistroSdkStatsUsage.Instrumentations);
            Assert.False(processor.HasRemainingInstrumentations);

            DistroSdkStatsUsage.ResetForTesting();
            ProcessActivity(processor, "System.Net.Http.Tests");
            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public async Task OnEnd_HandlesOverlappingActivitiesBestEffort()
        {
            const string AgentFrameworkSource =
                "Experimental.Microsoft.Agents.AI.Concurrent";
            const string Agent365Source = "Agent365Sdk";
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.AgentFramework | DistroInstrumentation.Agent365);
            using var provider = Sdk.CreateTracerProviderBuilder()
                .AddSource(AgentFrameworkSource, Agent365Source)
                .AddProcessor(processor)
                .Build();
            using var agentFrameworkSource = new ActivitySource(AgentFrameworkSource);
            using var agent365Source = new ActivitySource(Agent365Source);
            var activities = new Task[32];

            for (var i = 0; i < activities.Length; i++)
            {
                var source = i % 2 == 0
                    ? agentFrameworkSource
                    : agent365Source;
                activities[i] = Task.Run(() =>
                {
                    using var activity = source.StartActivity("test");
                    Assert.NotNull(activity);
                });
            }

            await Task.WhenAll(activities);

            Assert.Equal(
                DistroInstrumentation.AgentFramework | DistroInstrumentation.Agent365,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void OnEnd_StopsDetectingAfterObservationWindow()
        {
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.Agent365,
                observationWindow: TimeSpan.FromMilliseconds(25));

            Thread.Sleep(TimeSpan.FromMilliseconds(100));
            ProcessActivity(processor, "Agent365Sdk");

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
            Assert.False(processor.HasRemainingInstrumentations);
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
        public void Builder_DoesNotRegisterDetectionWhenTracingIsDisabled()
        {
            const string SourceName = "Microsoft.Data.SqlClient.Tests.TracingDisabled";
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(options =>
                {
                    options.Exporters = ExportTarget.Console;
                    options.Instrumentation.EnableTracing = false;
                    options.Instrumentation.EnableMetrics = true;
                })
                .WithTracing(tracing => tracing.AddSource(SourceName));

            using var serviceProvider = services.BuildServiceProvider();
            _ = serviceProvider.GetRequiredService<TracerProvider>();
            _ = serviceProvider.GetRequiredService<MeterProvider>();

            using var meter = new Meter(SourceName);
            meter.CreateCounter<long>("commands").Add(1);

            using var source = new ActivitySource(SourceName);
            using (var activity = source.StartActivity("test"))
            {
                Assert.NotNull(activity);
            }

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void TracerProvider_TracksCompletedActivityThroughProcessor()
        {
            const string SourceName = "Agent365Sdk";
            var processor = new DistroInstrumentationUsageProcessor(
                DistroInstrumentation.Agent365);
            using var provider = Sdk.CreateTracerProviderBuilder()
                .AddSource(SourceName)
                .AddProcessor(processor)
                .Build();
            using var source = new ActivitySource(SourceName);

            using (var activity = source.StartActivity("test"))
            {
                Assert.NotNull(activity);
                Assert.Equal(
                    DistroInstrumentation.None,
                    DistroSdkStatsUsage.Instrumentations);
            }

            Assert.Equal(
                DistroInstrumentation.Agent365,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void OpenTelemetrySdkCreate_TracksCompletedActivity()
        {
            const string SourceName = "Experimental.Microsoft.Agents.AI";
            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(options =>
                {
                    options.Exporters = ExportTarget.Console;
                    options.Instrumentation.EnableAzureSdkInstrumentation = false;
                    options.Instrumentation.EnableAspNetCoreInstrumentation = false;
                    options.Instrumentation.EnableHttpClientInstrumentation = false;
                    options.Instrumentation.EnableSqlClientInstrumentation = false;
                    options.Instrumentation.EnableOpenAIInstrumentation = false;
                    options.Instrumentation.EnableSemanticKernelInstrumentation = false;
                    options.Instrumentation.EnableAgentFrameworkInstrumentation = true;
                    options.Instrumentation.EnableAgent365Instrumentation = false;
                });
            });
            DistroSdkStatsUsage.ResetForTesting();
            using var source = new ActivitySource(SourceName);

            using (var activity = source.StartActivity("test"))
            {
                Assert.NotNull(activity);
            }

            Assert.Equal(
                DistroInstrumentation.AgentFramework,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Theory]
        [InlineData("Azure.Core.Http", 1UL << 0)]
        [InlineData("Microsoft.AspNetCore", 1UL << 1)]
        [InlineData("Microsoft.AspNetCore.Hosting", 1UL << 1)]
        [InlineData("OpenTelemetry.Instrumentation.AspNetCore", 1UL << 1)]
        [InlineData("System.Net.Http", 1UL << 2)]
        [InlineData("OpenTelemetry.Instrumentation.Http.HttpClient", 1UL << 2)]
        [InlineData("OpenTelemetry.Instrumentation.Http.HttpWebRequest", 1UL << 2)]
        [InlineData("OpenTelemetry.Instrumentation.SqlClient", 1UL << 3)]
        [InlineData("Microsoft.Data.SqlClient", 1UL << 3)]
        [InlineData("System.Data.SqlClient", 1UL << 3)]
        [InlineData("Azure.AI.OpenAI", (1UL << 0) | (1UL << 4))]
        [InlineData("OpenAI.Chat", 1UL << 4)]
        [InlineData("Experimental.Microsoft.Extensions.AI", 1UL << 4)]
        [InlineData("Microsoft.SemanticKernel", 1UL << 5)]
        [InlineData("Experimental.Microsoft.Agents.AI", 1UL << 6)]
        [InlineData("Agent365Sdk", 1UL << 7)]
        public void GetInstrumentations_MapsKnownSources(
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
        [InlineData("OpenTelemetry.Instrumentation.AspNetCore.Extensions")]
        [InlineData("OpenTelemetry.Instrumentation.Http.HttpClient.Extensions")]
        [InlineData("OpenTelemetry.Instrumentation.Http.HttpWebRequest.Extensions")]
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
            using var provider = Sdk.CreateTracerProviderBuilder()
                .AddSource(sourceName)
                .AddProcessor(processor)
                .Build();
            using var source = new ActivitySource(sourceName);
            using var activity = source.StartActivity("test");
            Assert.NotNull(activity);
        }
    }
}
