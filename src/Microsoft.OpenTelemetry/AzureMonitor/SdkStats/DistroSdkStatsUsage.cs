// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Process-wide, monotonic record of runtime-observed features and instrumentations.
    /// Configuration features remain in the last-write-wins <see cref="DistroFeatureSnapshot"/>.
    /// Writers can run on telemetry and transport threads while the observable gauge reads the
    /// masks on the SDKStats collection thread.
    /// </summary>
    internal static class DistroSdkStatsUsage
    {
        private static long s_features;
        private static long s_instrumentations;

        internal static DistroFeature Features =>
            (DistroFeature)(ulong)Volatile.Read(ref s_features);

        internal static DistroInstrumentation Instrumentations =>
            (DistroInstrumentation)(ulong)Volatile.Read(ref s_instrumentations);

        internal static void MarkFeatureInUse(DistroFeature feature) =>
            AddFlags(ref s_features, (ulong)feature);

        internal static void MarkInstrumentationInUse(DistroInstrumentation instrumentation) =>
            AddFlags(ref s_instrumentations, (ulong)instrumentation);

        internal static void ResetForTesting()
        {
            Interlocked.Exchange(ref s_features, 0);
            Interlocked.Exchange(ref s_instrumentations, 0);
        }

        private static void AddFlags(ref long target, ulong flags)
        {
            if (flags == 0)
            {
                return;
            }

            long observed;
            long updated;
            do
            {
                observed = Volatile.Read(ref target);
                updated = observed | unchecked((long)flags);
                if (updated == observed)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref target, updated, observed) != observed);
        }
    }
}
