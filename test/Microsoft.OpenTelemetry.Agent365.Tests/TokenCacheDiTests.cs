// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using OpenTelemetry;
using Xunit;
using Assert = Xunit.Assert;

namespace Microsoft.OpenTelemetry.Agent365.Tests
{
    [Collection("EnvironmentVariableTests")]
    public class TokenCacheDiTests
    {
        [Fact]
        public void CustomTokenResolver_IExporterTokenCache_StillRegistered()
        {
            // Issue #42: Setting custom TokenResolver should NOT prevent
            // IExporterTokenCache<AgenticTokenStruct> from being registered.
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.Exporter.TokenResolver = (agentId, tenantId) =>
                        Task.FromResult<string?>("custom-token");
                });

            // IExporterTokenCache<AgenticTokenStruct> must be resolvable
            Assert.Contains(services, s =>
                s.ServiceType == typeof(IExporterTokenCache<AgenticTokenStruct>));
        }

        [Fact]
        public void CustomTokenResolver_ExporterOptions_Overridden()
        {
            // When a custom TokenResolver is provided, the Agent365ExporterOptions
            // singleton should use the inline resolver, not the cache-based one.
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.Exporter.TokenResolver = (agentId, tenantId) =>
                        Task.FromResult<string?>("custom-token");
                });

            // Agent365ExporterOptions should be registered (both cache-based and inline)
            var exporterOptionsDescriptors = services
                .Where(s => s.ServiceType == typeof(Agent365ExporterOptions))
                .ToList();

            // Should have at least 2: one from AddAgenticTracingExporter (factory) + one inline (instance)
            Assert.True(exporterOptionsDescriptors.Count >= 2,
                $"Expected at least 2 Agent365ExporterOptions registrations, got {exporterOptionsDescriptors.Count}");
        }

        [Fact]
        public void NoTokenResolver_IExporterTokenCache_Registered()
        {
            // Without custom TokenResolver, IExporterTokenCache should still be registered.
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                });

            Assert.Contains(services, s =>
                s.ServiceType == typeof(IExporterTokenCache<AgenticTokenStruct>));
        }

        [Fact]
        public void NoTokenResolver_ExporterOptions_SingleRegistration()
        {
            // Without custom TokenResolver, only the cache-based Agent365ExporterOptions
            // should be registered (no inline override).
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                });

            var inlineDescriptors = services
                .Where(s => s.ServiceType == typeof(Agent365ExporterOptions)
                         && s.ImplementationInstance != null)
                .ToList();

            Assert.Empty(inlineDescriptors);
        }
    }
}
