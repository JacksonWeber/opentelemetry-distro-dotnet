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
    /// This bit space is owned by the distro and is independent from the classic Application
    /// Insights SDK's <c>StatsbeatFeatures</c> bit space, which targets a different meter
    /// (<c>FeatureStatsbeatMeter</c>).
    /// </para>
    /// </remarks>
    [Flags]
    internal enum DistroFeature : ulong
    {
        /// <summary>No features reported.</summary>
        None = 0,

        /// <summary>The Microsoft OpenTelemetry distro is in use.</summary>
        Distro = 1UL << 0,

        /// <summary>Microsoft Entra ID (AAD) authentication is configured.</summary>
        AadHandling = 1UL << 1,

        /// <summary>Live Metrics is enabled.</summary>
        LiveMetrics = 1UL << 2,

        /// <summary>Standard metrics emission is enabled.</summary>
        StandardMetrics = 1UL << 3,

        /// <summary>Performance counters emission is enabled.</summary>
        PerfCounters = 1UL << 4,

        /// <summary>Offline (disk-based) retry persistence is enabled.</summary>
        DiskRetry = 1UL << 5,

        /// <summary>Customer-facing SDK stats are enabled.</summary>
        CustomerSdkStats = 1UL << 6,

        /// <summary>Trace-based log sampling is enabled.</summary>
        TraceBasedLogsSampler = 1UL << 7,

        /// <summary>The Azure Monitor exporter is selected.</summary>
        ExporterAzureMonitor = 1UL << 8,

        /// <summary>The OTLP exporter is selected.</summary>
        ExporterOtlp = 1UL << 9,

        /// <summary>The Agent365 exporter is selected.</summary>
        ExporterAgent365 = 1UL << 10,

        /// <summary>The Console exporter is selected.</summary>
        ExporterConsole = 1UL << 11,

        /// <summary>
        /// Microsoft Agent Framework wiring is active. Currently always set because the distro
        /// wires <c>UseAgentFramework</c> unconditionally. Becomes dynamic when an opt-out is
        /// introduced.
        /// </summary>
        AgentFramework = 1UL << 12,

        /// <summary>
        /// Agent365-only mode (Agent365 exporter selected with no Azure Monitor and no OTLP
        /// exporter) — infrastructure instrumentation is suppressed by default.
        /// </summary>
        A365OnlyMode = 1UL << 13,

        // Bits 14-63 reserved for future features. Do not renumber.
    }
}
