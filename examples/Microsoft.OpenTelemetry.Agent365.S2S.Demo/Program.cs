// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            var configuration = LoadConfiguration();
            var options = SampleOptions.Load(configuration);
            var tokenProvider = new S2STokenProvider(
                options,
                new MsalTokenExchangeClient(),
                TimeProvider.System);

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.Services.AddLogging(logging =>
                {
                    logging.ClearProviders();

                    // Use a sample-local safe console provider instead of AddSimpleConsole:
                    // AddSimpleConsole renders full exception chains (message, type, stack
                    // trace, inner exceptions) for calls such as
                    // Agent365ExporterCore.LogError(ex, ...), which can surface sensitive
                    // authentication failure details. SafeConsoleLoggerProvider formats the
                    // log state/arguments but always discards the exception object.
                    logging.AddProvider(new SafeConsoleLoggerProvider(Console.Out, LogLevel.Information));
                    logging.SetMinimumLevel(LogLevel.Information);
                });

                otel.UseMicrosoftOpenTelemetry(distro =>
                {
                    distro.Exporters = ExportTarget.Console | ExportTarget.Agent365;
                    distro.Agent365.TokenResolver = (agentId, tenantId) =>
                        tokenProvider.ResolveAsync(agentId, tenantId);
                    distro.Agent365.UseS2SEndpoint = true;
                    distro.Agent365.ClusterCategory = options.ClusterCategory;
                });
            });

            Console.WriteLine("Generating InvokeAgent, Inference, ExecuteTool, and Inference spans.");
            await SampleScenario.RunAsync(options).ConfigureAwait(false);

            if (sdk.TracerProvider?.ForceFlush(timeoutMilliseconds: 30_000) != true)
            {
                Console.Error.WriteLine("Trace flush did not complete within 30 seconds.");
                return 1;
            }

            Console.WriteLine("Telemetry flushed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static IConfigurationRoot LoadConfiguration()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Configuration file '{path}' was not found. Copy appsettings.example.json to appsettings.json and fill in the values.");
        }

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }
}
