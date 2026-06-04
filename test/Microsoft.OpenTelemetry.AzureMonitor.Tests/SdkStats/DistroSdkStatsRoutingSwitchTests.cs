// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    [Collection(nameof(DistroFeatureSdkStatsCollection))]
    public class DistroSdkStatsRoutingSwitchTests
    {
        private const string SwitchName = "Azure.Monitor.OpenTelemetry.Exporter.RouteSdkStatsToDistroEndpoint";

        [Fact]
        public void UseMicrosoftOpenTelemetry_SetsRouteSdkStatsToDistroEndpointAppContextSwitch()
        {
            // Ensure a clean starting state. The switch may be left "on" by a previous test
            // in the same process; reset to false to verify UseMicrosoftOpenTelemetry sets it.
            AppContext.SetSwitch(SwitchName, false);

            var services = new ServiceCollection();
            services.AddOpenTelemetry().UseMicrosoftOpenTelemetry(_ => { });

            Assert.True(
                AppContext.TryGetSwitch(SwitchName, out var enabled) && enabled,
                "UseMicrosoftOpenTelemetry should set the distro SDK statistics routing AppContext switch.");
        }
    }
}
