// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Compression;
using System.Xml.Linq;

namespace Microsoft.OpenTelemetry.Package.Tests;

internal static class PackageArchive
{
    private static readonly string ArtifactsDirectory = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "package-validation-path.txt")).Trim();

    public static ZipArchive Open(string packageId, string version, string extension)
    {
        var packagePath = Path.Combine(ArtifactsDirectory, $"{packageId}.{version}.{extension}");

        return ZipFile.OpenRead(packagePath);
    }

    public static XDocument ReadNuspec(ZipArchive archive)
    {
        var nuspec = archive.Entries.Single(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        using var stream = nuspec.Open();
        return XDocument.Load(stream);
    }
}
