// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Etw;
using Microsoft.OpenTelemetry;

namespace Microsoft.OpenTelemetry.Agent365.Tests
{
    /// <summary>
    /// Guards the intentional 1.3 assembly boundary: rebuilt consumers resolve moved Agent365 types
    /// directly from the Contracts and ETW assemblies, not through the distro.
    /// </summary>
    [TestClass]
    public class DistroAssemblyBoundaryTests
    {
        private static Assembly DistroAssembly => typeof(Agent365Options).Assembly;

        [TestMethod]
        public void DistroAssembly_DoesNotForwardMovedAgent365Types()
        {
            DistroAssembly.GetName().Name.Should().Be("Microsoft.OpenTelemetry");
            typeof(EtwLoggingBuilder).Assembly.GetName().Name
                .Should().Be("Microsoft.Agents.A365.Observability.Etw");
            DistroAssembly.GetForwardedTypes()
                .Where(type => type.Namespace?.StartsWith(
                    "Microsoft.Agents.A365.Observability",
                    StringComparison.Ordinal) == true)
                .Should().BeEmpty(
                "1.3 requires consumers to rebuild against the standalone Contracts and ETW assemblies");
        }
    }
}
