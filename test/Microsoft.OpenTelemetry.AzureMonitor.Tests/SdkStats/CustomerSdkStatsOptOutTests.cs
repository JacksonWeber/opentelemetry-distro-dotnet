// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.OpenTelemetry.AzureMonitor.Internals;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    // Guards the on-by-default opt-out semantics of customer SDK stats. This is a behavior
    // change that is easy to regress (e.g. accidental inversion back to opt-in), so the
    // env-var parsing and its mapping onto the CustomerSdkStats feature bit are pinned here.
    [Collection(nameof(CustomerSdkStatsOptOutCollection))]
    public class CustomerSdkStatsOptOutTests : IDisposable
    {
        private const string DisabledEnvVar =
            EnvironmentVariableConstants.APPLICATIONINSIGHTS_SDKSTATS_DISABLED;

        private const string ValidConnectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westus-0.in.applicationinsights.azure.com/";

        private readonly string? _original;

        public CustomerSdkStatsOptOutTests()
        {
            _original = Environment.GetEnvironmentVariable(DisabledEnvVar);
            Environment.SetEnvironmentVariable(DisabledEnvVar, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(DisabledEnvVar, _original);
        }

        [Theory]
        [InlineData(null)]      // unset => enabled (on by default)
        [InlineData("")]        // empty => enabled
        [InlineData("false")]   // explicit opt-in value => enabled
        [InlineData("False")]
        [InlineData("0")]       // only the literal "true" opts out
        [InlineData("yes")]
        public void IsCustomerSdkStatsEnabled_ReturnsTrue_WhenNotDisabled(string? value)
        {
            Environment.SetEnvironmentVariable(DisabledEnvVar, value);

            Assert.True(MicrosoftOpenTelemetryBuilderExtensions.IsCustomerSdkStatsEnabled());
        }

        [Theory]
        [InlineData("true")]
        [InlineData("TRUE")]
        [InlineData("True")]
        public void IsCustomerSdkStatsEnabled_ReturnsFalse_WhenDisabledIsTrue(string value)
        {
            Environment.SetEnvironmentVariable(DisabledEnvVar, value);

            Assert.False(MicrosoftOpenTelemetryBuilderExtensions.IsCustomerSdkStatsEnabled());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("false")]
        public void FeatureBit_IsReported_WhenNotDisabled(string? value)
        {
            Environment.SetEnvironmentVariable(DisabledEnvVar, value);

            var snapshot = BuildSnapshot(MicrosoftOpenTelemetryBuilderExtensions.IsCustomerSdkStatsEnabled());

            Assert.True(snapshot.Features.HasFlag(DistroFeature.CustomerSdkStats));
        }

        [Fact]
        public void FeatureBit_IsCleared_WhenDisabledIsTrue()
        {
            Environment.SetEnvironmentVariable(DisabledEnvVar, "true");

            var snapshot = BuildSnapshot(MicrosoftOpenTelemetryBuilderExtensions.IsCustomerSdkStatsEnabled());

            Assert.False(snapshot.Features.HasFlag(DistroFeature.CustomerSdkStats));
        }

        private static DistroFeatureSnapshot BuildSnapshot(bool customerSdkStatsEnabled)
        {
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;

            return DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.AzureMonitor,
                customerSdkStatsEnabled,
                a365OnlyMode: false,
                distroVersion: "9.9.9-optout")!;
        }
    }

    [CollectionDefinition(nameof(CustomerSdkStatsOptOutCollection), DisableParallelization = true)]
    public class CustomerSdkStatsOptOutCollection
    {
        // APPLICATIONINSIGHTS_SDKSTATS_DISABLED is process-wide; serialize tests that mutate it.
    }
}
