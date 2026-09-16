// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    /// <summary>
    /// Testable factory that assembles the durable store-and-forward pieces an exporter owns for its
    /// lifetime: the shared persistent store and the background replay coordinator that drains it. The
    /// same store instance is injected into the live <see cref="Agent365ExporterCore"/> (so failed/deferred
    /// live sends and replay passes operate on one queue) and, together with the core's transmission
    /// coordination state, wired into the coordinator.
    /// <para>
    /// When offline storage is disabled — either explicitly via
    /// <see cref="Agent365ExporterOptions.DisableOfflineStorage"/> or implicitly because storage
    /// initialization failed — <see cref="CreateStorage"/> returns a <see cref="DisabledAgent365Storage"/>
    /// no-op so the live core never sees a null store, and <see cref="CreateCoordinator"/> returns
    /// <c>null</c> so no pointless background loop is started.
    /// </para>
    /// </summary>
    internal static class Agent365DurableDelivery
    {
        /// <summary>
        /// Creates the shared persistent store honoring <paramref name="options"/>. Returns a
        /// <see cref="DisabledAgent365Storage"/> when offline storage is disabled. If offline storage is
        /// enabled but initialization throws, the failure is logged at Error (rather than hidden) and a
        /// <see cref="DisabledAgent365Storage"/> is returned so export continues with durability off.
        /// </summary>
        internal static IAgent365PersistentStorage CreateStorage(Agent365ExporterOptions options, ILogger logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (options.DisableOfflineStorage)
            {
                logger.LogInformation(
                    "Agent365: Offline storage is disabled; undeliverable exports will be dropped and no replay loop will run.");
                return new DisabledAgent365Storage();
            }

            try
            {
                var directory = Agent365StorageDirectoryResolver.Resolve(options.StorageDirectory);
                return Agent365PersistentStorage.Create(directory);
            }
            catch (Exception ex)
            {
                // Do not hide the failure: surface it, then degrade gracefully to no durability rather
                // than failing exporter construction outright.
                logger.LogError(
                    ex,
                    "Agent365: Failed to initialize offline storage at '{StorageDirectory}'; continuing with offline storage disabled.",
                    options.StorageDirectory ?? "(default location)");
                return new DisabledAgent365Storage();
            }
        }

        /// <summary>
        /// Creates the background replay coordinator wired to the core's shared store and tenant gate registry and to
        /// <see cref="Agent365ExporterCore.ReplayRecordAsync"/> with a cancellation-aware HTTP send.
        /// Returns <c>null</c> when the core's store is a <see cref="DisabledAgent365Storage"/> (offline
        /// storage disabled or unavailable), because there is nothing to replay.
        /// </summary>
        internal static IAgent365ReplayCoordinator? CreateCoordinator(
            Agent365ExporterCore core,
            Agent365ExporterOptions options,
            HttpClient httpClient,
            ILogger logger)
        {
            if (core == null) throw new ArgumentNullException(nameof(core));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            var storage = core.Storage;
            if (storage is DisabledAgent365Storage)
            {
                return null;
            }

            // Mirror the live send delegate: the token resolver prefers ContextualTokenResolver inside
            // ReplayRecordAsync, so this simple resolver is only invoked when only TokenResolver is set.
            Func<string, string, Task<string?>> tokenResolver =
                (agentId, tenantId) => options.TokenResolver!(agentId, tenantId);

            // Cancellation-aware send so a shutdown mid-replay cancels the in-flight HTTP call.
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync =
                (request, ct) => httpClient.SendAsync(request, ct);

            return new Agent365ReplayCoordinator(
                storage,
                core.Gates,
                replayAsync: (record, ct) => core.ReplayRecordAsync(record, options, tokenResolver, sendAsync, ct),
                logger);
        }
    }

    /// <summary>
    /// No-op <see cref="IAgent365PersistentStorage"/> used when offline storage is disabled or its
    /// initialization failed. It never touches disk: <see cref="TryStore"/> returns <c>false</c> so
    /// every retryable outcome (gate deferral, retryable HTTP status, transport failure, token-resolver
    /// exception) correctly surfaces as <c>ExportResult.Failure</c> — the caller never claims durable
    /// persistence when nothing was actually written. <see cref="TryGetNext"/> always reports an empty
    /// queue, and <see cref="Dispose"/> is a no-op.
    /// </summary>
    internal sealed class DisabledAgent365Storage : IAgent365PersistentStorage
    {
        public bool TryStore(Agent365DurableRecord record) => false;

        public bool TryGetNext(
#if NETSTANDARD2_0
            out IAgent365StoredRecord? record)
#else
            [NotNullWhen(true)] out IAgent365StoredRecord? record)
#endif
        {
            record = null;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
