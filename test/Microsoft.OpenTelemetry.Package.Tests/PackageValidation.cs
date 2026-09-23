// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Package.Tests;

internal static class PackageValidation
{
    public static readonly string Version = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "package-validation-version.txt")).Trim();
}
