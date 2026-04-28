// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    /// <summary>
    /// Tests that verify user-configured resource attributes (via ConfigureResource)
    /// are preserved when using UseMicrosoftOpenTelemetry. Guards against regression
    /// of https://github.com/microsoft/opentelemetry-distro-dotnet/issues/28.
    /// </summary>
    [Collection("EnvironmentVariableTests")]
    public class ResourceAttributeTests : IDisposable
    {
        private const string TestConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
        private const string TestSourceName = "ResourceAttributeTests";

        private readonly ActivitySource _activitySource = new(TestSourceName);

        public void Dispose()
        {
            _activitySource.Dispose();
        }

        /// <summary>
        /// Recommended pattern (issue #28): ConfigureResource() chained before
        /// UseMicrosoftOpenTelemetry() on the same AddOpenTelemetry() call.
        /// Verifies that user-set service identity attributes are present on the
        /// built TracerProvider's resource and that spans flow through the pipeline.
        /// </summary>
        [Fact]
        public void ConfigureResource_BeforeUseMicrosoftOpenTelemetry_PreservesUserAttributes()
        {
            var services = new ServiceCollection();
            var exportedActivities = new List<Activity>();

            services.AddOpenTelemetry()
                .ConfigureResource(r => r
                    .AddService(
                        serviceName: "A365.SemanticKernel",
                        serviceNamespace: "Microsoft.Agents",
                        serviceVersion: "1.0.0")
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = "Production",
                    }))
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithTracing(tracing => tracing
                    .AddSource(TestSourceName)
                    .AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using (_activitySource.StartActivity("test-span")) { }
            tracerProvider.ForceFlush();

            Assert.NotEmpty(exportedActivities);

            var attrs = tracerProvider.GetResource().Attributes
                .ToDictionary(a => a.Key, a => a.Value);

            Assert.Equal("A365.SemanticKernel", attrs["service.name"]);
            Assert.Equal("Microsoft.Agents", attrs["service.namespace"]);
            Assert.Equal("1.0.0", attrs["service.version"]);
            Assert.Equal("Production", attrs["deployment.environment"]);
        }

        /// <summary>
        /// Verifies that the distro's auto-added telemetry.distro.name attribute
        /// coexists with user-configured resource attributes.
        /// </summary>
        [Fact]
        public void ConfigureResource_BeforeUseMicrosoftOpenTelemetry_MergesWithDistroAttributes()
        {
            var services = new ServiceCollection();
            var exportedActivities = new List<Activity>();

            services.AddOpenTelemetry()
                .ConfigureResource(r => r
                    .AddService(
                        serviceName: "A365.SemanticKernel",
                        serviceNamespace: "Microsoft.Agents",
                        serviceVersion: "1.0.0")
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = "Development",
                    }))
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithTracing(tracing => tracing
                    .AddSource(TestSourceName)
                    .AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using (_activitySource.StartActivity("test-span")) { }
            tracerProvider.ForceFlush();

            Assert.NotEmpty(exportedActivities);

            var attrs = tracerProvider.GetResource().Attributes
                .ToDictionary(a => a.Key, a => a.Value);

            // User attributes present
            Assert.Equal("A365.SemanticKernel", attrs["service.name"]);
            Assert.Equal("Microsoft.Agents", attrs["service.namespace"]);
            Assert.Equal("1.0.0", attrs["service.version"]);
            Assert.Equal("Development", attrs["deployment.environment"]);

            // Distro-added attribute also present
            Assert.True(attrs.ContainsKey("telemetry.distro.name"),
                "Expected distro to add telemetry.distro.name attribute.");
            Assert.Equal("Microsoft.OpenTelemetry", attrs["telemetry.distro.name"]);
        }

        /// <summary>
        /// Verifies resource attributes are consistent when multiple exporters are enabled
        /// (Console + AzureMonitor). The resource is shared across all signals and exporters.
        /// </summary>
        [Fact]
        public void ConfigureResource_WithMultipleExporters_ResourceConsistent()
        {
            var services = new ServiceCollection();
            var exportedActivities = new List<Activity>();

            services.AddOpenTelemetry()
                .ConfigureResource(r => r
                    .AddService(
                        serviceName: "MultiExporter.Service",
                        serviceNamespace: "Test.Namespace",
                        serviceVersion: "2.0.0")
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = "Staging",
                    }))
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console | ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                })
                .WithTracing(tracing => tracing
                    .AddSource(TestSourceName)
                    .AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using (_activitySource.StartActivity("test-span")) { }
            tracerProvider.ForceFlush();

            Assert.NotEmpty(exportedActivities);

            var attrs = tracerProvider.GetResource().Attributes
                .ToDictionary(a => a.Key, a => a.Value);

            // User attributes present
            Assert.Equal("MultiExporter.Service", attrs["service.name"]);
            Assert.Equal("Test.Namespace", attrs["service.namespace"]);
            Assert.Equal("2.0.0", attrs["service.version"]);
            Assert.Equal("Staging", attrs["deployment.environment"]);

            // Distro attribute also present
            Assert.Equal("Microsoft.OpenTelemetry", attrs["telemetry.distro.name"]);
        }

        /// <summary>
        /// Verifies that ConfigureResource() called after UseMicrosoftOpenTelemetry()
        /// on the same builder chain still merges user attributes correctly.
        /// </summary>
        [Fact]
        public void ConfigureResource_AfterUseMicrosoftOpenTelemetry_SameChain_PreservesUserAttributes()
        {
            var services = new ServiceCollection();
            var exportedActivities = new List<Activity>();

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console | ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                })
                .ConfigureResource(r => r
                    .AddService(
                        serviceName: "AfterDistro.Service",
                        serviceNamespace: "Test.Namespace",
                        serviceVersion: "3.0.0")
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = "Staging",
                    }))
                .WithTracing(tracing => tracing
                    .AddSource(TestSourceName)
                    .AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using (_activitySource.StartActivity("test-span")) { }
            tracerProvider.ForceFlush();

            Assert.NotEmpty(exportedActivities);

            var attrs = tracerProvider.GetResource().Attributes
                .ToDictionary(a => a.Key, a => a.Value);

            Assert.Equal("AfterDistro.Service", attrs["service.name"]);
            Assert.Equal("Test.Namespace", attrs["service.namespace"]);
            Assert.Equal("3.0.0", attrs["service.version"]);
            Assert.Equal("Staging", attrs["deployment.environment"]);
            Assert.Equal("Microsoft.OpenTelemetry", attrs["telemetry.distro.name"]);
        }

        /// <summary>
        /// Verifies that ConfigureResource() on a separate AddOpenTelemetry() call
        /// before the distro call still merges user attributes correctly.
        /// </summary>
        [Fact]
        public void ConfigureResource_SeparateCallBeforeDistro_PreservesUserAttributes()
        {
            var services = new ServiceCollection();
            var exportedActivities = new List<Activity>();

            services.AddOpenTelemetry()
                .ConfigureResource(r => r
                    .AddService(
                        serviceName: "SeparateBefore.Service",
                        serviceNamespace: "Test.Namespace",
                        serviceVersion: "4.0.0")
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = "QA",
                    }));

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console | ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                })
                .WithTracing(tracing => tracing
                    .AddSource(TestSourceName)
                    .AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using (_activitySource.StartActivity("test-span")) { }
            tracerProvider.ForceFlush();

            Assert.NotEmpty(exportedActivities);

            var attrs = tracerProvider.GetResource().Attributes
                .ToDictionary(a => a.Key, a => a.Value);

            Assert.Equal("SeparateBefore.Service", attrs["service.name"]);
            Assert.Equal("Test.Namespace", attrs["service.namespace"]);
            Assert.Equal("4.0.0", attrs["service.version"]);
            Assert.Equal("QA", attrs["deployment.environment"]);
            Assert.Equal("Microsoft.OpenTelemetry", attrs["telemetry.distro.name"]);
        }

        /// <summary>
        /// Verifies that ConfigureResource() on a separate AddOpenTelemetry() call
        /// after the distro call still merges user attributes correctly.
        /// </summary>
        [Fact]
        public void ConfigureResource_SeparateCallAfterDistro_PreservesUserAttributes()
        {
            var services = new ServiceCollection();
            var exportedActivities = new List<Activity>();

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console | ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                });

            services.AddOpenTelemetry()
                .ConfigureResource(r => r
                    .AddService(
                        serviceName: "SeparateAfter.Service",
                        serviceNamespace: "Test.Namespace",
                        serviceVersion: "5.0.0")
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = "Integration",
                    }))
                .WithTracing(tracing => tracing
                    .AddSource(TestSourceName)
                    .AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using (_activitySource.StartActivity("test-span")) { }
            tracerProvider.ForceFlush();

            Assert.NotEmpty(exportedActivities);

            var attrs = tracerProvider.GetResource().Attributes
                .ToDictionary(a => a.Key, a => a.Value);

            Assert.Equal("SeparateAfter.Service", attrs["service.name"]);
            Assert.Equal("Test.Namespace", attrs["service.namespace"]);
            Assert.Equal("5.0.0", attrs["service.version"]);
            Assert.Equal("Integration", attrs["deployment.environment"]);
            Assert.Equal("Microsoft.OpenTelemetry", attrs["telemetry.distro.name"]);
        }

        /// <summary>
        /// Verifies that calling .Clear() on the ResourceBuilder before setting custom
        /// attributes still results in user attributes being present and service.name
        /// is not the default unknown_service value.
        /// </summary>
        [Fact]
        public void ConfigureResource_WithClear_UserAttributesSurvive()
        {
            var services = new ServiceCollection();
            var exportedActivities = new List<Activity>();

            services.AddOpenTelemetry()
                .ConfigureResource(r => r
                    .Clear()
                    .AddService(
                        serviceName: "Cleared.Service",
                        serviceNamespace: "Test.Namespace",
                        serviceVersion: "1.0.0",
                        serviceInstanceId: Environment.MachineName)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = "Production",
                    }))
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console | ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                })
                .WithTracing(tracing => tracing
                    .AddSource(TestSourceName)
                    .AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using (_activitySource.StartActivity("test-span")) { }
            tracerProvider.ForceFlush();

            Assert.NotEmpty(exportedActivities);

            var attrs = tracerProvider.GetResource().Attributes
                .ToDictionary(a => a.Key, a => a.Value);

            Assert.Equal("Cleared.Service", attrs["service.name"]);
            Assert.DoesNotContain(attrs, a =>
                a.Key == "service.name" && a.Value?.ToString()?.StartsWith("unknown_service") == true);
            Assert.Equal("Test.Namespace", attrs["service.namespace"]);
            Assert.Equal("1.0.0", attrs["service.version"]);
            Assert.Equal("Production", attrs["deployment.environment"]);
        }
    }
}
#endif
