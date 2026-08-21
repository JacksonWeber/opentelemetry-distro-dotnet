// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Marks an instrumentation as used only after its registered source produces a completed span.
    /// </summary>
    internal sealed class DistroInstrumentationUsageProcessor : BaseProcessor<Activity>
    {
        private readonly DistroInstrumentation _enabledInstrumentations;

        internal DistroInstrumentationUsageProcessor(DistroInstrumentation enabledInstrumentations)
        {
            _enabledInstrumentations = enabledInstrumentations;
        }

        public override void OnEnd(Activity activity)
        {
            if (activity is null)
            {
                return;
            }

            var used = GetInstrumentations(activity.Source.Name) & _enabledInstrumentations;
            if (used == DistroInstrumentation.None)
            {
                return;
            }

            DistroSdkStatsUsage.MarkInstrumentationInUse(used);

            if ((used & DistroInstrumentation.AgentFramework) != 0)
            {
                DistroSdkStatsUsage.MarkFeatureInUse(DistroFeature.AgentFramework);
            }
        }

        internal static DistroInstrumentation GetEnabledInstrumentations(InstrumentationOptions options)
        {
            var enabled = DistroInstrumentation.None;
            if (options.EnableAzureSdkInstrumentation)
            {
                enabled |= DistroInstrumentation.AzureSdk;
            }

            if (options.EnableAspNetCoreInstrumentation)
            {
                enabled |= DistroInstrumentation.AspNetCore;
            }

            if (options.EnableHttpClientInstrumentation)
            {
                enabled |= DistroInstrumentation.HttpClient;
            }

            if (options.EnableSqlClientInstrumentation)
            {
                enabled |= DistroInstrumentation.SqlClient;
            }

            if (options.EnableOpenAIInstrumentation)
            {
                enabled |= DistroInstrumentation.OpenAI;
            }

            if (options.EnableSemanticKernelInstrumentation)
            {
                enabled |= DistroInstrumentation.SemanticKernel;
            }

            if (options.EnableAgentFrameworkInstrumentation)
            {
                enabled |= DistroInstrumentation.AgentFramework;
            }

            if (options.EnableAgent365Instrumentation)
            {
                enabled |= DistroInstrumentation.Agent365;
            }

            return enabled;
        }

        internal static DistroInstrumentation GetInstrumentations(string sourceName)
        {
            var instrumentations = DistroInstrumentation.None;

            if (sourceName.StartsWith("Azure.", StringComparison.Ordinal)
                && !sourceName.StartsWith("Azure.Monitor.OpenTelemetry", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AzureSdk;
            }

            if (sourceName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AspNetCore;
            }

            if (sourceName.StartsWith("System.Net.Http", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.HttpClient;
            }

            if (sourceName.StartsWith("OpenTelemetry.Instrumentation.SqlClient", StringComparison.Ordinal)
                || sourceName.StartsWith("Microsoft.Data.SqlClient", StringComparison.Ordinal)
                || sourceName.StartsWith("System.Data.SqlClient", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.SqlClient;
            }

            if (sourceName.StartsWith("Azure.AI.OpenAI", StringComparison.Ordinal)
                || sourceName.StartsWith("OpenAI.", StringComparison.Ordinal)
                || string.Equals(sourceName, "Experimental.Microsoft.Extensions.AI", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.OpenAI;
            }

            if (sourceName.StartsWith("Microsoft.SemanticKernel", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.SemanticKernel;
            }

            if (sourceName.StartsWith("Experimental.Microsoft.Agents.AI", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AgentFramework;
            }

            if (string.Equals(sourceName, "Agent365Sdk", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.Agent365;
            }

            return instrumentations;
        }
    }
}
