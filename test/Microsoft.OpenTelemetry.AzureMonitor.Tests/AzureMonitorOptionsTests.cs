// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Reflection;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Azure.Monitor.OpenTelemetry.Exporter;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    public class AzureMonitorOptionsTests
    {
        private const string TestConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        [Fact]
        public void AzureMonitorOptions_PublicTransportRemainsInherited()
        {
            var transport = typeof(AzureMonitorOptions).GetProperty(
                nameof(ClientOptions.Transport), BindingFlags.Instance | BindingFlags.Public);

            Assert.NotNull(transport);
            Assert.Equal(typeof(ClientOptions), transport.DeclaringType);
            Assert.True(transport.GetMethod!.IsPublic);
            Assert.True(transport.SetMethod!.IsPublic);
        }

        [Fact]
        public void AzureMonitorOptions_ReadingTransport_DoesNotMarkItExplicit()
        {
            var options = new AzureMonitorOptions();

            Assert.NotNull(options.Transport);
            Assert.Null(options.ExplicitTransport);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AzureMonitorOptions_InvalidTransportAssignment_DoesNotMarkItExplicit(bool useInternalSetter)
        {
            var options = new AzureMonitorOptions();
            var inheritedTransport = options.Transport;

            Assert.Throws<ArgumentNullException>(() =>
            {
                if (useInternalSetter)
                {
                    options.SetTransport(null!);
                }
                else
                {
                    options.Transport = null!;
                }
            });

            Assert.Same(inheritedTransport, options.Transport);
            Assert.Null(options.ExplicitTransport);
        }

        [Fact]
        public void AzureMonitorOptions_EnableTraceBasedLogsSampler_DefaultValue_IsTrue()
        {
            var options = new AzureMonitorOptions();
            Assert.True(options.EnableTraceBasedLogsSampler);
        }

        [Fact]
        public void AzureMonitorOptions_EnableTraceBasedLogsSampler_CanBeDisabled()
        {
            var options = new AzureMonitorOptions { EnableTraceBasedLogsSampler = false };
            Assert.False(options.EnableTraceBasedLogsSampler);
        }

        [Fact]
        public void AzureMonitorOptions_EnableStandardMetrics_DefaultValue_IsTrue()
        {
            var options = new AzureMonitorOptions();
            Assert.True(options.EnableStandardMetrics);
        }

        [Fact]
        public void AzureMonitorOptions_EnableStandardMetrics_CanBeDisabled()
        {
            var options = new AzureMonitorOptions { EnableStandardMetrics = false };
            Assert.False(options.EnableStandardMetrics);
        }

        [Fact]
        public void AzureMonitorOptions_EnablePerfCounters_DefaultValue_IsTrue()
        {
            var options = new AzureMonitorOptions();
            Assert.True(options.EnablePerfCounters);
        }

        [Fact]
        public void AzureMonitorOptions_EnablePerfCounters_CanBeDisabled()
        {
            var options = new AzureMonitorOptions { EnablePerfCounters = false };
            Assert.False(options.EnablePerfCounters);
        }

        [Fact]
        public void UseAzureMonitor_WithoutConfiguration_UsesDefaultValues()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddOpenTelemetry()
                .UseAzureMonitor(options =>
                {
                    options.ConnectionString = TestConnectionString;
                    options.DisableOfflineStorage = true;
                });

            var serviceProvider = serviceCollection.BuildServiceProvider();

            var azureMonitorOptions = serviceProvider.GetRequiredService<IOptionsMonitor<AzureMonitorOptions>>()
                .Get(Options.DefaultName);

            Assert.True(azureMonitorOptions.EnableTraceBasedLogsSampler);
            Assert.True(azureMonitorOptions.EnableStandardMetrics);
            Assert.True(azureMonitorOptions.EnablePerfCounters);
            Assert.True(azureMonitorOptions.EnableLiveMetrics);
            Assert.Equal(5.0, azureMonitorOptions.TracesPerSecond);
        }
    }
}
