// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    public class DistroFeatureSnapshotTests
    {
        private const string ValidConnectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westus-0.in.applicationinsights.azure.com/";

        [Fact]
        public void Build_WithoutConnectionString_UsesNAForCikey()
        {
            // For OTLP-only / Console-only / Agent365-only deployments there is no customer
            // Application Insights connection string. Per the SDKStats spec convention, the
            // cikey dimension is reported as the literal "N/A" so backend KQL queries don't
            // have to filter out missing rows.
            var options = new MicrosoftOpenTelemetryOptions();

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                connectionString: null,
                ExportTarget.Otlp,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "1.0.0");

            Assert.NotNull(snapshot);
            Assert.Equal("N/A", snapshot!.CustomerInstrumentationKey);
            Assert.Equal(DistroFeatureSnapshot.NoCustomerInstrumentationKey, snapshot.CustomerInstrumentationKey);
            // Always-on bits still set even without a customer connection string.
            Assert.True(snapshot.Features.HasFlag(DistroFeature.Distro));
            Assert.True(snapshot.Features.HasFlag(DistroFeature.AgentFramework));
            Assert.True(snapshot.Features.HasFlag(DistroFeature.ExporterOtlp));
            Assert.False(snapshot.Features.HasFlag(DistroFeature.ExporterAzureMonitor));
        }

        [Fact]
        public void Build_WithMinimalConnectionString_SetsBaseFeatures()
        {
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;
            // Force-disable everything that defaults to true so we can observe the minimal bit set.
            options.AzureMonitor.EnableLiveMetrics = false;
            options.AzureMonitor.EnableStandardMetrics = false;
            options.AzureMonitor.EnablePerfCounters = false;
            options.AzureMonitor.DisableOfflineStorage = true;
            options.AzureMonitor.EnableTraceBasedLogsSampler = false;

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.None,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "1.0.0");

            Assert.NotNull(snapshot);
            // DISTRO and AGENT_FRAMEWORK are always set; nothing else when everything is off.
            var expected = DistroFeature.Distro | DistroFeature.AgentFramework;
            Assert.Equal(expected, snapshot!.Features);
            Assert.Equal("00000000-0000-0000-0000-000000000000", snapshot.CustomerInstrumentationKey);
            Assert.Equal("1.0.0", snapshot.DistroVersion);
        }

        [Fact]
        public void Build_WithAllDefaults_SetsExpectedFeatureBits()
        {
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;
            // Defaults: LiveMetrics=true, StandardMetrics=true, PerfCounters=true,
            // DisableOfflineStorage=false (so DiskRetry=true), TraceBasedLogsSampler=true.

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.AzureMonitor,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "1.0.0");

            Assert.NotNull(snapshot);

            var expected =
                DistroFeature.Distro
                | DistroFeature.LiveMetrics
                | DistroFeature.StandardMetrics
                | DistroFeature.PerfCounters
                | DistroFeature.DiskRetry
                | DistroFeature.TraceBasedLogsSampler
                | DistroFeature.ExporterAzureMonitor
                | DistroFeature.AgentFramework;

            Assert.Equal(expected, snapshot!.Features);
        }

        [Fact]
        public void Build_WithAllExportersAndCustomerSdkStats_SetsAllExpectedBits()
        {
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.AzureMonitor | ExportTarget.Otlp | ExportTarget.Agent365 | ExportTarget.Console,
                customerSdkStatsEnabled: true,
                a365OnlyMode: false,
                distroVersion: "1.0.0");

            Assert.NotNull(snapshot);
            Assert.True(snapshot!.Features.HasFlag(DistroFeature.ExporterAzureMonitor));
            Assert.True(snapshot.Features.HasFlag(DistroFeature.ExporterOtlp));
            Assert.True(snapshot.Features.HasFlag(DistroFeature.ExporterAgent365));
            Assert.True(snapshot.Features.HasFlag(DistroFeature.ExporterConsole));
            Assert.True(snapshot.Features.HasFlag(DistroFeature.CustomerSdkStats));
        }

        [Fact]
        public void Build_WithA365OnlyMode_SetsA365OnlyModeBit()
        {
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.Agent365,
                customerSdkStatsEnabled: false,
                a365OnlyMode: true,
                distroVersion: "1.0.0");

            Assert.NotNull(snapshot);
            Assert.True(snapshot!.Features.HasFlag(DistroFeature.A365OnlyMode));
        }

        [Theory]
        [InlineData("InstrumentationKey=abc-123", "abc-123")]
        [InlineData("InstrumentationKey=abc-123;IngestionEndpoint=https://x", "abc-123")]
        [InlineData("IngestionEndpoint=https://x;InstrumentationKey=abc-123", "abc-123")]
        [InlineData("instrumentationkey=abc-123", "abc-123")] // case-insensitive
        [InlineData("InstrumentationKey = abc-123 ;X=Y", "abc-123")] // whitespace tolerant
        [InlineData(" ; ;InstrumentationKey=abc-123; ", "abc-123")] // tolerates empty segments
        public void TryExtractInstrumentationKey_ParsesValidStrings(string connectionString, string expected)
        {
            Assert.Equal(expected, DistroFeatureSnapshot.TryExtractInstrumentationKey(connectionString));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("IngestionEndpoint=https://x")]
        [InlineData("=value-without-key")]
        [InlineData("InstrumentationKey=")]
        [InlineData("NoEqualsSign")]
        public void TryExtractInstrumentationKey_ReturnsNullForInvalidStrings(string? connectionString)
        {
            Assert.Null(DistroFeatureSnapshot.TryExtractInstrumentationKey(connectionString));
        }
    }
}
