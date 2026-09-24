// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;

namespace Microsoft.OpenTelemetry.Package.Tests;

[TestClass]
public sealed class PackageDependencyTests
{
    private const string ContractsPackageId = "Microsoft.Agents.A365.Observability.Contracts";
    private const string DistroPackageId = "Microsoft.OpenTelemetry";
    private const string EtwPackageId = "Microsoft.Agents.A365.Observability.Etw";
    [TestMethod]
    public void DistroNuspecDependsOnContractsButNotEtw()
    {
        using var archive = PackageArchive.Open(DistroPackageId, PackageValidation.Version, "nupkg");
        var dependencyIds = GetDependencyIds(archive);

        dependencyIds.Should().Contain(ContractsPackageId);
        dependencyIds.Should().NotContain(EtwPackageId);
    }

    [TestMethod]
    public void StandaloneEtwNuspecDependsOnContracts()
    {
        using var archive = PackageArchive.Open(EtwPackageId, PackageValidation.Version, "nupkg");
        GetDependencyIds(archive).Should().Contain(ContractsPackageId);
    }

    private static IEnumerable<string> GetDependencyIds(System.IO.Compression.ZipArchive archive)
    {
        var nuspec = PackageArchive.ReadNuspec(archive);
        var packageNamespace = nuspec.Root!.Name.Namespace;

        return nuspec.Descendants(packageNamespace + "dependency")
            .Select(dependency => dependency.Attribute("id")?.Value)
            .Where(id => id is not null)
            .Cast<string>();
    }
}
