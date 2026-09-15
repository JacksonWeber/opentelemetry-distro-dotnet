// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Marks enabled instrumentations that produce completed activities during a bounded startup window.
    /// </summary>
    internal sealed class DistroInstrumentationUsageProcessor : BaseProcessor<Activity>
    {
        internal static readonly TimeSpan DefaultObservationWindow = TimeSpan.FromMinutes(10);

        private readonly long _observationDeadline;

        // OnEnd calls may overlap. A stale write can only restore enabled candidate bits,
        // causing duplicate work; usage marking occurs first and is idempotent.
        private DistroInstrumentation _remainingInstrumentations;

        internal DistroInstrumentationUsageProcessor(
            DistroInstrumentation enabledInstrumentations,
            TimeSpan? observationWindow = null)
        {
            var effectiveObservationWindow = observationWindow ?? DefaultObservationWindow;
            if (effectiveObservationWindow <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(observationWindow),
                    "The instrumentation observation window must be greater than zero.");
            }

            _remainingInstrumentations = enabledInstrumentations;
            _observationDeadline = Stopwatch.GetTimestamp()
                + (long)(effectiveObservationWindow.TotalSeconds * Stopwatch.Frequency);
        }

        internal bool HasRemainingInstrumentations =>
            _remainingInstrumentations != DistroInstrumentation.None;

        public override void OnEnd(Activity activity)
        {
            var remaining = _remainingInstrumentations;
            if (remaining == DistroInstrumentation.None)
            {
                return;
            }

            if (Stopwatch.GetTimestamp() >= _observationDeadline)
            {
                _remainingInstrumentations = DistroInstrumentation.None;
                return;
            }

            var observed = GetInstrumentations(activity.Source.Name, remaining);
            if (observed == DistroInstrumentation.None)
            {
                return;
            }

            DistroSdkStatsUsage.MarkInstrumentationInUse(observed);
            _remainingInstrumentations = remaining & ~observed;
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

        internal static DistroInstrumentation GetInstrumentations(string sourceName) =>
            GetInstrumentations(sourceName, candidates: null);

        private static DistroInstrumentation GetInstrumentations(
            string sourceName,
            DistroInstrumentation? candidates)
        {
            var instrumentations = DistroInstrumentation.None;

            if (IsCandidate(candidates, DistroInstrumentation.AzureSdk)
                && sourceName.StartsWith("Azure.", StringComparison.Ordinal)
                && !sourceName.StartsWith("Azure.Monitor.OpenTelemetry", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AzureSdk;
            }

            if (IsCandidate(candidates, DistroInstrumentation.AspNetCore)
                && (sourceName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                    || string.Equals(sourceName, "OpenTelemetry.Instrumentation.AspNetCore", StringComparison.Ordinal)))
            {
                instrumentations |= DistroInstrumentation.AspNetCore;
            }

            if (IsCandidate(candidates, DistroInstrumentation.HttpClient)
                && (sourceName.StartsWith("System.Net.Http", StringComparison.Ordinal)
                    || string.Equals(sourceName, "OpenTelemetry.Instrumentation.Http.HttpClient", StringComparison.Ordinal)
                    || string.Equals(sourceName, "OpenTelemetry.Instrumentation.Http.HttpWebRequest", StringComparison.Ordinal)))
            {
                instrumentations |= DistroInstrumentation.HttpClient;
            }

            if (IsCandidate(candidates, DistroInstrumentation.SqlClient)
                && (sourceName.StartsWith("OpenTelemetry.Instrumentation.SqlClient", StringComparison.Ordinal)
                    || sourceName.StartsWith("Microsoft.Data.SqlClient", StringComparison.Ordinal)
                    || sourceName.StartsWith("System.Data.SqlClient", StringComparison.Ordinal)))
            {
                instrumentations |= DistroInstrumentation.SqlClient;
            }

            if (IsCandidate(candidates, DistroInstrumentation.OpenAI)
                && (sourceName.StartsWith("Azure.AI.OpenAI", StringComparison.Ordinal)
                    || sourceName.StartsWith("OpenAI.", StringComparison.Ordinal)
                    || string.Equals(sourceName, "Experimental.Microsoft.Extensions.AI", StringComparison.Ordinal)))
            {
                instrumentations |= DistroInstrumentation.OpenAI;
            }

            if (IsCandidate(candidates, DistroInstrumentation.SemanticKernel)
                && sourceName.StartsWith("Microsoft.SemanticKernel", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.SemanticKernel;
            }

            if (IsCandidate(candidates, DistroInstrumentation.AgentFramework)
                && sourceName.StartsWith("Experimental.Microsoft.Agents.AI", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AgentFramework;
            }

            if (IsCandidate(candidates, DistroInstrumentation.Agent365)
                && string.Equals(sourceName, "Agent365Sdk", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.Agent365;
            }

            return instrumentations;
        }

        private static bool IsCandidate(
            DistroInstrumentation? candidates,
            DistroInstrumentation instrumentation) =>
            candidates == null
            || (candidates.Value & instrumentation) != DistroInstrumentation.None;
    }
}
