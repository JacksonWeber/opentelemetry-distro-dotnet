// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using FluentAssertions.Execution;
using System.Text;

namespace Microsoft.OpenTelemetry.Package.Tests;

[TestClass]
public sealed class PackageContentsTests
{
    private const string EtwPackageId = "Microsoft.Agents.A365.Observability.Etw";
    private const string DistroPackageId = "Microsoft.OpenTelemetry";
    private static readonly string[] EmbeddedAssemblyNames =
    [
        "Microsoft.OpenTelemetry",
        "Microsoft.Agents.A365.Observability.Etw",
    ];

    private const string ContractsAssemblyName =
        "Microsoft.Agents.A365.Observability.Contracts";

    [DataTestMethod]
    [DataRow("netstandard2.0")]
    [DataRow("net8.0")]
    public void DistroNupkgEmbedsDistroAndEtwButNotContracts(string targetFramework)
    {
        using var archive = PackageArchive.Open(DistroPackageId, PackageValidation.Version, "nupkg");
        var entries = archive.Entries.Select(entry => entry.FullName);
        var expectedEntries = EmbeddedAssemblyNames.SelectMany(assemblyName => new[]
        {
            $"lib/{targetFramework}/{assemblyName}.dll",
            $"lib/{targetFramework}/{assemblyName}.xml",
        });

        using var scope = new AssertionScope();
        entries.Should().Contain(expectedEntries);
        entries.Should().NotContain(
        [
            $"lib/{targetFramework}/{ContractsAssemblyName}.dll",
            $"lib/{targetFramework}/{ContractsAssemblyName}.xml",
        ]);
    }

    [DataTestMethod]
    [DataRow("netstandard2.0")]
    [DataRow("net8.0")]
    public void DistroSnupkgEmbedsDistroAndEtwButNotContracts(string targetFramework)
    {
        using var archive = PackageArchive.Open(DistroPackageId, PackageValidation.Version, "snupkg");

        using var scope = new AssertionScope();
        foreach (var assemblyName in EmbeddedAssemblyNames)
        {
            var expectedEntry = $"lib/{targetFramework}/{assemblyName}.pdb";
            var entry = archive.GetEntry(expectedEntry);
            entry.Should().NotBeNull($"the symbol package should contain {expectedEntry}");

            if (entry is null)
            {
                continue;
            }

            using var stream = entry.Open();
            var signature = new byte[4];
            stream.ReadExactly(signature);
            Encoding.ASCII.GetString(signature).Should().Be(
                "BSJB",
                $"{expectedEntry} should be a portable PDB");
        }

        archive.GetEntry($"lib/{targetFramework}/{ContractsAssemblyName}.pdb")
            .Should().BeNull();
    }

    [DataTestMethod]
    [DataRow("netstandard2.0")]
    [DataRow("net8.0")]
    public void StandaloneEtwNupkgContainsEtwButNotContracts(string targetFramework)
    {
        using var archive = PackageArchive.Open(EtwPackageId, PackageValidation.Version, "nupkg");
        var entries = archive.Entries.Select(entry => entry.FullName);

        using var scope = new AssertionScope();
        entries.Should().Contain(
        [
            $"lib/{targetFramework}/{EtwPackageId}.dll",
            $"lib/{targetFramework}/{EtwPackageId}.xml",
        ]);
        entries.Should().NotContain(
        [
            $"lib/{targetFramework}/{ContractsAssemblyName}.dll",
            $"lib/{targetFramework}/{ContractsAssemblyName}.xml",
        ]);
    }

    [DataTestMethod]
    [DataRow("netstandard2.0")]
    [DataRow("net8.0")]
    public void StandaloneEtwSnupkgContainsEtwButNotContracts(string targetFramework)
    {
        using var archive = PackageArchive.Open(EtwPackageId, PackageValidation.Version, "snupkg");

        using var scope = new AssertionScope();
        archive.GetEntry($"lib/{targetFramework}/{EtwPackageId}.pdb")
            .Should().NotBeNull();
        archive.GetEntry($"lib/{targetFramework}/{ContractsAssemblyName}.pdb")
            .Should().BeNull();
    }
}
