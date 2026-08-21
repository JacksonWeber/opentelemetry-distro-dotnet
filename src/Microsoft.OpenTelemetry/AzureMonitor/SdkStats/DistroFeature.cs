// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Bit flags reported by the Microsoft OpenTelemetry distro as the <c>feature</c> dimension
    /// of <c>type=0</c> Feature SDKStats (see the SDKStats specification).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each set bit represents a distro feature that is enabled in the current process. Values
    /// are stable; new bits must be appended only — never renumbered — because backend Kusto
    /// decoders are keyed off these indexes.
    /// </para>
    /// <para>
    /// Bits 0-7 are reserved for the existing .NET Track* feature mappings. Distro features
    /// start at bit 8 so backend decoders can process both generations without assigning
    /// different meanings to the same bit.
    /// </para>
    /// </remarks>
    [Flags]
    internal enum DistroFeature : ulong
    {
        /// <summary>No features reported.</summary>
        None = 0,

        /// <summary>The Microsoft OpenTelemetry distro is in use.</summary>
        Distro = 1UL << 8,

        /// <summary>Microsoft Entra ID (AAD) authentication is configured.</summary>
        AadHandling = 1UL << 9,

        /// <summary>Live Metrics entered active collection after a portal subscription.</summary>
        LiveMetrics = 1UL << 10,

        /// <summary>Standard metrics emission is enabled.</summary>
        StandardMetrics = 1UL << 11,

        /// <summary>Performance counters emission is enabled.</summary>
        PerfCounters = 1UL << 12,

        /// <summary>Offline (disk-based) retry persistence is enabled.</summary>
        DiskRetry = 1UL << 13,

        /// <summary>Customer-facing SDK stats are enabled.</summary>
        CustomerSdkStats = 1UL << 14,

        /// <summary>Trace-based log sampling is enabled.</summary>
        TraceBasedLogsSampler = 1UL << 15,

        /// <summary>The Azure Monitor exporter is selected.</summary>
        ExporterAzureMonitor = 1UL << 16,

        /// <summary>The OTLP exporter is selected.</summary>
        ExporterOtlp = 1UL << 17,

        /// <summary>The Agent365 exporter is selected.</summary>
        ExporterAgent365 = 1UL << 18,

        /// <summary>The Console exporter is selected.</summary>
        ExporterConsole = 1UL << 19,

        /// <summary>
        /// Microsoft Agent Framework telemetry was observed in this process.
        /// </summary>
        AgentFramework = 1UL << 20,

        /// <summary>
        /// Agent365-only mode (Agent365 exporter selected with no Azure Monitor and no OTLP
        /// exporter) — infrastructure instrumentation is suppressed by default.
        /// </summary>
        A365OnlyMode = 1UL << 21,

        // Bits 22-63 reserved for future features. Do not renumber.
    }
}
