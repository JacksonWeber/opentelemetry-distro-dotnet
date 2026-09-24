// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime.Etw;

namespace Microsoft.Agents.A365.Observability.Etw.Tests
{
    [TestClass]
    public class EtwAssemblyBoundaryTests
    {
        [TestMethod]
        public void EtwApis_AreOwnedByStandaloneEtwAssembly()
        {
            var expectedAssemblyName = "Microsoft.Agents.A365.Observability.Etw";
            var etwTypes = new[]
            {
                typeof(A365EtwLogger<>),
                typeof(IA365EtwLogger<>),
                typeof(EtwLogProcessor),
                typeof(EtwLoggingBuilder),
                typeof(EtwScopeEventProcessor),
                typeof(EtwServiceCollectionExtensions),
                typeof(EtwTracingBuilder),
            };

            foreach (var etwType in etwTypes)
            {
                Assert.AreEqual(expectedAssemblyName, etwType.Assembly.GetName().Name);
            }
        }

        [TestMethod]
        public void EtwLogProcessor_UsesStandaloneFormatterConstructor()
        {
            var constructors = typeof(EtwLogProcessor).GetConstructors();

            Assert.HasCount(1, constructors);
            Assert.AreEqual(typeof(EtwExportFormatter), constructors[0].GetParameters()[0].ParameterType);
        }
    }
}
