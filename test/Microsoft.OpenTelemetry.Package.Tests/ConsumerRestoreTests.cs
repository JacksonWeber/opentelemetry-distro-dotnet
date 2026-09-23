// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;

namespace Microsoft.OpenTelemetry.Package.Tests;

[TestClass]
public sealed class ConsumerRestoreTests
{
    private const string ContractsPackageId = "Microsoft.Agents.A365.Observability.Contracts";
    private const string DistroPackageId = "Microsoft.OpenTelemetry";
    private const string EtwPackageId = "Microsoft.Agents.A365.Observability.Etw";
    private const string NuGetOrgSource = "https://api.nuget.org/v3/index.json";
    [TestMethod]
    public async Task DistroConsumerRestoresContractsTransitively()
    {
        var result = await RestoreAndBuildConsumerAsync(
            "DistroConsumer",
            "distro-source",
            "DistroSmokePackageVersion");

        GetSourcePackages(result.PackageSource).Should().BeEquivalentTo(
        [
            $"{DistroPackageId}.{PackageValidation.Version}.nupkg",
            $"{ContractsPackageId}.{PackageValidation.Version}.nupkg",
        ]);

        var libraries = ReadPackageLibraries(result.AssetsFile);
        GetResolvedVersion(libraries, DistroPackageId).Should().Be(PackageValidation.Version);
        GetResolvedVersion(libraries, ContractsPackageId).Should().Be(PackageValidation.Version);
        libraries.Should().NotContain(library => GetPackageId(library).Equals(EtwPackageId, StringComparison.OrdinalIgnoreCase));

        var outputAssemblies = GetOutputAssemblies(result.OutputDirectory);
        outputAssemblies.Should().Contain(
        [
            $"{DistroPackageId}.dll",
            $"{EtwPackageId}.dll",
            $"{ContractsPackageId}.dll",
        ]);
    }

    [TestMethod]
    public async Task StandaloneEtwConsumerRestoresContractsTransitively()
    {
        var result = await RestoreAndBuildConsumerAsync(
            "StandaloneEtwConsumer",
            "etw-source",
            "EtwSdkSmokePackageVersion");

        GetSourcePackages(result.PackageSource).Should().BeEquivalentTo(
        [
            $"{EtwPackageId}.{PackageValidation.Version}.nupkg",
            $"{ContractsPackageId}.{PackageValidation.Version}.nupkg",
        ]);

        var libraries = ReadPackageLibraries(result.AssetsFile);
        GetResolvedVersion(libraries, EtwPackageId).Should().Be(PackageValidation.Version);
        GetResolvedVersion(libraries, ContractsPackageId).Should().Be(PackageValidation.Version);

        var outputAssemblies = GetOutputAssemblies(result.OutputDirectory);
        outputAssemblies.Should().Contain(
        [
            $"{EtwPackageId}.dll",
            $"{ContractsPackageId}.dll",
        ]);
        outputAssemblies.Should().NotContain($"{DistroPackageId}.dll");
    }

    private static async Task<ConsumerBuildResult> RestoreAndBuildConsumerAsync(
        string consumerName,
        string packageSourceName,
        string versionPropertyName)
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "test",
            "package-smoke",
            consumerName,
            $"{consumerName}.csproj");
        var packageValidationDirectory = GetPackageValidationDirectory(repositoryRoot);
        var packageSource = Path.Combine(packageValidationDirectory, packageSourceName);
        var testOutputRoot = Path.Combine(packageValidationDirectory, "consumer-validation", consumerName);
        var intermediateOutput = Path.Combine(testOutputRoot, "obj");
        var outputDirectory = Path.Combine(testOutputRoot, "bin");
        var nugetConfigPath = Path.Combine(testOutputRoot, "NuGet.Config");

        if (Directory.Exists(testOutputRoot))
        {
            Directory.Delete(testOutputRoot, recursive: true);
        }

        Directory.CreateDirectory(testOutputRoot);
        WriteNuGetConfig(nugetConfigPath, packageSource);

        var commonProperties = new[]
        {
            $"-p:{versionPropertyName}={PackageValidation.Version}",
            $"-p:BaseIntermediateOutputPath={intermediateOutput}{Path.DirectorySeparatorChar}",
            $"-p:BaseOutputPath={outputDirectory}{Path.DirectorySeparatorChar}",
            $"-p:MSBuildProjectExtensionsPath={intermediateOutput}{Path.DirectorySeparatorChar}",
            "-p:NuGetAudit=false",
        };

        var restoreResult = await RunDotNetAsync(
            repositoryRoot,
            [
                "restore",
                projectPath,
                "--configfile",
                nugetConfigPath,
                "--no-cache",
                .. commonProperties,
            ]);
        AssertProcessSucceeded("restore", restoreResult);

        var buildResult = await RunDotNetAsync(
            repositoryRoot,
            [
                "build",
                projectPath,
                "--configuration",
                "Release",
                "--no-restore",
                .. commonProperties,
            ]);
        AssertProcessSucceeded("build", buildResult);

        return new ConsumerBuildResult(
            Path.Combine(intermediateOutput, "project.assets.json"),
            outputDirectory,
            packageSource);
    }

    private static async Task<ProcessResult> RunDotNetAsync(string workingDirectory, IEnumerable<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("dotnet process timed out after 10 minutes.");
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static void AssertProcessSucceeded(string operation, ProcessResult result)
    {
        Assert.AreEqual(
            0,
            result.ExitCode,
            $"dotnet {operation} failed with exit code {result.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{result.StandardError}");
    }

    private static IReadOnlyCollection<string> ReadPackageLibraries(string assetsFile)
    {
        File.Exists(assetsFile).Should().BeTrue($"restore should create {assetsFile}");

        using var document = JsonDocument.Parse(File.ReadAllText(assetsFile));
        return document.RootElement
            .GetProperty("libraries")
            .EnumerateObject()
            .Select(library => library.Name)
            .ToArray();
    }

    private static string GetResolvedVersion(IEnumerable<string> libraries, string packageId)
    {
        var packagePrefix = $"{packageId}/";
        var library = libraries.Single(
            library => library.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase));

        return library[packagePrefix.Length..];
    }

    private static string GetPackageId(string library)
    {
        var separatorIndex = library.IndexOf('/');
        return separatorIndex < 0 ? library : library[..separatorIndex];
    }

    private static IReadOnlyCollection<string> GetOutputAssemblies(string outputDirectory)
    {
        Directory.Exists(outputDirectory).Should().BeTrue($"build should create {outputDirectory}");

        return Directory
            .EnumerateFiles(outputDirectory, "*.dll", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(fileName => fileName is not null)
            .Cast<string>()
            .ToArray();
    }

    private static IReadOnlyCollection<string> GetSourcePackages(string packageSource)
    {
        return Directory
            .EnumerateFiles(packageSource, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(fileName => fileName is not null)
            .Cast<string>()
            .ToArray();
    }

    private static void WriteNuGetConfig(string path, string packageSource)
    {
        var document = new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement(
                        "add",
                        new XAttribute("key", "validation"),
                        new XAttribute("value", packageSource)),
                    new XElement(
                        "add",
                        new XAttribute("key", "nuget.org"),
                        new XAttribute("value", NuGetOrgSource),
                        new XAttribute("protocolVersion", "3")))));

        document.Save(path);
    }

    private static string GetPackageValidationDirectory(string repositoryRoot)
    {
        var path = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "package-validation-path.txt")).Trim();

        return Path.IsPathFullyQualified(path)
            ? path
            : Path.GetFullPath(path, repositoryRoot);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(directory.FullName, "test", "package-smoke")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find the repository root from {AppContext.BaseDirectory}.");
    }

    private sealed record ConsumerBuildResult(
        string AssetsFile,
        string OutputDirectory,
        string PackageSource);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
