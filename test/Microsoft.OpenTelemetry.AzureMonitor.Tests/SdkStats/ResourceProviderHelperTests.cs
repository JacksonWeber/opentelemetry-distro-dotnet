// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.OpenTelemetry.AzureMonitor.Internals;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    [Collection(nameof(ResourceProviderHelperCollection))]
    public class ResourceProviderHelperTests : IDisposable
    {
        // Snapshot existing env vars so we can restore them after the test.
        private readonly string? _functionsRuntime;
        private readonly string? _websiteSiteName;
        private readonly string? _aksArmNamespaceId;

        public ResourceProviderHelperTests()
        {
            _functionsRuntime = Environment.GetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME");
            _websiteSiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
            _aksArmNamespaceId = Environment.GetEnvironmentVariable("AKS_ARM_NAMESPACE_ID");

            Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", null);
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", null);
            Environment.SetEnvironmentVariable("AKS_ARM_NAMESPACE_ID", null);
            ResourceProviderHelper.ResetForTesting();
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", _functionsRuntime);
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", _websiteSiteName);
            Environment.SetEnvironmentVariable("AKS_ARM_NAMESPACE_ID", _aksArmNamespaceId);
            ResourceProviderHelper.ResetForTesting();
        }

        [Fact]
        public void GetResourceProvider_DetectsFunctions()
        {
            Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated");

            Assert.Equal("functions", ResourceProviderHelper.GetResourceProvider());
        }

        [Fact]
        public void GetResourceProvider_DetectsAppService()
        {
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", "my-app");

            Assert.Equal("appsvc", ResourceProviderHelper.GetResourceProvider());
        }

        [Fact]
        public void GetResourceProvider_DetectsAks()
        {
            Environment.SetEnvironmentVariable("AKS_ARM_NAMESPACE_ID", "ns");

            Assert.Equal("aks", ResourceProviderHelper.GetResourceProvider());
        }

        [Fact]
        public void GetResourceProvider_FunctionsTakesPrecedenceOverAppService()
        {
            Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated");
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", "my-app");

            Assert.Equal("functions", ResourceProviderHelper.GetResourceProvider());
        }

        [Fact]
        public void GetOperatingSystem_ReturnsKnownPlatform()
        {
            // Just sanity-check that we get a non-empty platform string. The actual value
            // depends on the host running the test.
            var os = ResourceProviderHelper.GetOperatingSystem();
            Assert.False(string.IsNullOrEmpty(os));
            Assert.Contains(os, new[] { "windows", "linux", "osx", "unknown" });
        }

        [Fact]
        public void GetAttachMode_ReturnsManual()
        {
            Assert.Equal("Manual", ResourceProviderHelper.GetAttachMode());
        }
    }

    [CollectionDefinition(nameof(ResourceProviderHelperCollection), DisableParallelization = true)]
    public class ResourceProviderHelperCollection
    {
        // Resource provider detection caches results process-wide; serialize tests.
    }
}
