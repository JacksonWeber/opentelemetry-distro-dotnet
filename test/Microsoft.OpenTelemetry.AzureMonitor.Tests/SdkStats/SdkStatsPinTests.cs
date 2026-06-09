// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using OpenTelemetry;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    /// <summary>
    /// Tests for <see cref="SdkStatsPin"/> and the eager-pin glue in
    /// <see cref="MicrosoftOpenTelemetryBuilderExtensions.UseMicrosoftOpenTelemetry{TBuilder}"/>.
    /// The pin is a process-wide singleton that triggers the Azure Monitor exporter's
    /// SDK Stats MeterProvider (and the Attach observable gauge it owns) as a ctor-time
    /// side effect of an inert <c>AzureMonitorMetricExporter</c>. Reuses the
    /// non-parallel <see cref="DistroFeatureSdkStatsCollection"/> so other tests in the
    /// SdkStats namespace don't race on the process-wide singleton state.
    /// </summary>
    [Collection(nameof(DistroFeatureSdkStatsCollection))]
    public class SdkStatsPinTests : IDisposable
    {
        private const string KillSwitchEnvVar = "APPLICATIONINSIGHTS_STATSBEAT_DISABLED";

        private readonly string? _previousKillSwitch;

        public SdkStatsPinTests()
        {
            _previousKillSwitch = Environment.GetEnvironmentVariable(KillSwitchEnvVar);
            // Default each test to "stats off" so we don't fire real HTTP traffic during
            // CI unless the test explicitly opts in by clearing the env var.
            Environment.SetEnvironmentVariable(KillSwitchEnvVar, "true");
            SdkStatsPin.ResetForTesting();
        }

        public void Dispose()
        {
            SdkStatsPin.ResetForTesting();
            Environment.SetEnvironmentVariable(KillSwitchEnvVar, _previousKillSwitch);
        }

        [Fact]
        public void Ensure_ConstructsPinOnFirstCall()
        {
            // Allow the pin to construct for this test only.
            Environment.SetEnvironmentVariable(KillSwitchEnvVar, null);

            Assert.False(SdkStatsPin.IsInitializedForTesting);

            SdkStatsPin.Ensure();

            Assert.True(SdkStatsPin.IsInitializedForTesting);
        }

        [Fact]
        public void Ensure_IsIdempotent()
        {
            Environment.SetEnvironmentVariable(KillSwitchEnvVar, null);

            SdkStatsPin.Ensure();
            SdkStatsPin.Ensure();
            SdkStatsPin.Ensure();

            // No exception, still initialized — the singleton guards against duplicate
            // SDK Stats MeterProviders (which would cause duplicate Attach
            // emissions on every export cycle).
            Assert.True(SdkStatsPin.IsInitializedForTesting);
        }

        [Fact]
        public void ResetForTesting_ClearsTheSingleton()
        {
            Environment.SetEnvironmentVariable(KillSwitchEnvVar, null);
            SdkStatsPin.Ensure();
            Assert.True(SdkStatsPin.IsInitializedForTesting);

            SdkStatsPin.ResetForTesting();

            Assert.False(SdkStatsPin.IsInitializedForTesting);
        }

        [Fact]
        public void UseMicrosoftOpenTelemetry_WithOtlpOnly_EnsuresAttachPin()
        {
            // OTLP-only deployment: customer never constructs an Azure Monitor exporter,
            // so without the eager pin SDK Stats (and Attach SDK Stats) would never come up.
            Environment.SetEnvironmentVariable(KillSwitchEnvVar, null);

            var services = new ServiceCollection();
            services.AddOpenTelemetry().UseMicrosoftOpenTelemetry(o =>
            {
                o.Exporters = ExportTarget.Otlp;
            });

            Assert.True(
                SdkStatsPin.IsInitializedForTesting,
                "UseMicrosoftOpenTelemetry with an exporter other than AzureMonitor must " +
                "eagerly initialize the SDK Stats pin so the SDK Stats MeterProvider " +
                "comes up and Attach measurements flow on the 24-hour cadence.");
        }

        [Fact]
        public void UseMicrosoftOpenTelemetry_WithAzureMonitor_DoesNotPinTwice()
        {
            // Customer's own AzureMonitor exporter triggers SDK Stats via the exporter's
            // own transmitter initialization. A second pin would create a second SDK Stats
            // instance (and a second Attach observable gauge on the same process-wide
            // static meter), which would double-report Attach.
            Environment.SetEnvironmentVariable(KillSwitchEnvVar, null);

            var services = new ServiceCollection();
            services.AddOpenTelemetry().UseMicrosoftOpenTelemetry(o =>
            {
                o.Exporters = ExportTarget.AzureMonitor;
                o.AzureMonitor.ConnectionString =
                    "InstrumentationKey=00000000-0000-0000-0000-000000000000;" +
                    "IngestionEndpoint=https://westus-0.in.applicationinsights.azure.com/";
            });

            Assert.False(
                SdkStatsPin.IsInitializedForTesting,
                "The eager SDK Stats pin must not run when the customer selected the " +
                "AzureMonitor exporter — their own exporter is responsible for bootstrapping " +
                "SDK Stats and a second pin would emit duplicate Attach measurements.");
        }

        [Fact]
        public void UseMicrosoftOpenTelemetry_WithNoExporters_DoesNotPin()
        {
            // Zero-exporter deployments produce no customer telemetry — emitting Attach
            // SDK Stats in that case would be pure noise without a corresponding signal.
            Environment.SetEnvironmentVariable(KillSwitchEnvVar, null);

            var services = new ServiceCollection();
            services.AddOpenTelemetry().UseMicrosoftOpenTelemetry(_ => { });

            Assert.False(SdkStatsPin.IsInitializedForTesting);
        }

        [Fact]
        public void UseMicrosoftOpenTelemetry_HonorsKillSwitch()
        {
            // APPLICATIONINSIGHTS_STATSBEAT_DISABLED is the shared kill switch for the
            // whole SDK Stats family; the eager Attach pin must honor it.
            Environment.SetEnvironmentVariable(KillSwitchEnvVar, "true");

            var services = new ServiceCollection();
            services.AddOpenTelemetry().UseMicrosoftOpenTelemetry(o =>
            {
                o.Exporters = ExportTarget.Otlp;
            });

            Assert.False(SdkStatsPin.IsInitializedForTesting);
        }
    }
}
