// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using OpenTelemetry;
using OpenTelemetry.Resources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    /// <summary>
    /// Utility methods for Agent365 trace exporters.
    /// Provides helpers for partitioning activities and building endpoint URIs.
    /// </summary>
    public class Agent365ExporterCore
    {
        private const string CorrelationIdHeaderKey = "x-ms-correlation-id";
        private const string DocsUrl403 = "https://aka.ms/a365-403";
        private const string FoundryUrl403 = "https://aka.ms/foundry-grant-agent-365-permissions";
        private readonly ExportFormatter _formatter;
        private readonly ILogger<Agent365ExporterCore> _logger;
        private readonly Agent365TransmissionGate _gate;
        private readonly Lazy<IAgent365PersistentStorage> _storage;
        private readonly Func<DateTimeOffset> _utcNow;

        // The ingest service performs a case-insensitive check for "chat", so we send the
        // gen_ai.operation.name through unchanged. Both the lowercase canonical value and the
        // InferenceOperationType.Chat enum name are accepted in this set so that activities
        // tagged with either form are not filtered out by PartitionByIdentity.
        private enum AddResult { Added, NonGenAI, MissingIdentity, Null }

        /// <summary>
        /// Initializes a new instance of the <see cref="Agent365ExporterCore"/> class.
        /// </summary>
        /// <param name="formatter">The formatter instance used to format export payloads.</param>
        /// <param name="logger">The logger instance used to log messages during the export process.</param>
        /// <remarks>
        /// A core built through this public constructor is never wired to a replay coordinator (only the
        /// builder path injects a real store and starts the background drain). It therefore defaults to a
        /// no-op <see cref="DisabledAgent365Storage"/> so a failed export is dropped gracefully rather than
        /// persisted to an on-disk store that nothing would ever drain — which would accumulate write-only,
        /// undrained durable records and leak the store's maintenance timer.
        /// </remarks>
        public Agent365ExporterCore(ExportFormatter formatter, ILogger<Agent365ExporterCore> logger)
            : this(formatter, logger, null, new DisabledAgent365Storage(), null)
        {
        }

        internal Agent365ExporterCore(
            ExportFormatter formatter,
            ILogger<Agent365ExporterCore> logger,
            Func<DateTimeOffset>? utcNow,
            IAgent365PersistentStorage? storage,
            Agent365TransmissionGate? gate)
        {
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _logger = logger ?? NullLogger<Agent365ExporterCore>.Instance;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _gate = gate ?? new Agent365TransmissionGate(_utcNow);

            // Resolved lazily so that a core which never persists (e.g. everything delivers on the
            // first attempt, or an injected fake is supplied) never creates the on-disk
            // FileBlobProvider and its maintenance timer. An injected storage is returned as-is.
            _storage = new Lazy<IAgent365PersistentStorage>(
                () => storage ?? Agent365PersistentStorage.Create(Agent365StorageDirectoryResolver.Resolve(null)),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// The durable store this core persists undeliverable exports to. Shared with the replay
        /// coordinator so both the live persist path and the replay drain operate on the same records.
        /// Resolving this forces the (otherwise lazy) storage to materialize.
        /// </summary>
        internal IAgent365PersistentStorage Storage => _storage.Value;

        /// <summary>
        /// The transmission gate this core coordinates send availability through. Shared with the replay
        /// coordinator so live sends and replay passes observe a single backoff/half-open state.
        /// </summary>
        internal Agent365TransmissionGate Gate => _gate;

        /// <summary>
        /// Partitions a batch of activities by tenant and agent identity.
        /// Only genAI activities (those with a known gen_ai.operation.name) are included.
        /// </summary>
        /// <param name="batch">The collection of activities to partition.</param>
        /// <returns>
        /// A list of tuples containing TenantId, AgentId, and the corresponding activities.
        /// </returns>
        public List<(string TenantId, string AgentId, List<Activity> Activities)> PartitionByIdentity(IEnumerable<Activity> batch)
        {
            var map = new Dictionary<(string tenant, string agent), List<Activity>>();
            int nonGenAICount = 0;
            int missingIdentityCount = 0;

            foreach (var activity in batch)
            {
                var result = Agent365ExporterCore.TryAddActivityToMap(activity, map);
                if (result == AddResult.NonGenAI) nonGenAICount++;
                else if (result == AddResult.MissingIdentity) missingIdentityCount++;
            }

            LogPartitionResults(map.Count, nonGenAICount, missingIdentityCount);
            return map.Select(kvp => (kvp.Key.tenant, kvp.Key.agent, kvp.Value)).ToList();
        }

        /// <summary>
        /// Partitions a batch of activities by tenant and agent identity.
        /// Only genAI activities (those with a known gen_ai.operation.name) are included.
        /// </summary>
        /// <param name="batch">The collection of activities to partition.</param>
        /// <returns>
        /// A list of tuples containing TenantId, AgentId, and the corresponding activities.
        /// </returns>
        public List<(string TenantId, string AgentId, List<Activity> Activities)> PartitionByIdentity(in Batch<Activity> batch)
        {
            var map = new Dictionary<(string tenant, string agent), List<Activity>>();
            int nonGenAICount = 0;
            int missingIdentityCount = 0;

            foreach (var activity in batch)
            {
                var result = Agent365ExporterCore.TryAddActivityToMap(activity, map);
                if (result == AddResult.NonGenAI) nonGenAICount++;
                else if (result == AddResult.MissingIdentity) missingIdentityCount++;
            }

            LogPartitionResults(map.Count, nonGenAICount, missingIdentityCount);
            return map.Select(kvp => (kvp.Key.tenant, kvp.Key.agent, kvp.Value)).ToList();
        }

        /// <summary>
        /// Builds the endpoint path for the trace export request based on tenant ID, agent ID and S2S setting.
        /// </summary>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="agentId">The agent identifier.</param>
        /// <param name="useS2SEndpoint">Whether to use the S2S endpoint.</param>
        /// <returns>The endpoint path string.</returns>
        public string BuildEndpointPath(string tenantId, string agentId, bool useS2SEndpoint)
        {
            var encodedTenantId = Uri.EscapeDataString(tenantId);
            var encodedAgentId = Uri.EscapeDataString(agentId);

            return useS2SEndpoint
                ? $"/observabilityService/tenants/{encodedTenantId}/otlp/agents/{encodedAgentId}/traces"
                : $"/observability/tenants/{encodedTenantId}/otlp/agents/{encodedAgentId}/traces";
        }

        /// <summary>
        /// Builds the full request URI for the trace export request.
        /// If the endpoint already includes a scheme (https://), it is used as-is.
        /// Otherwise, https:// is prepended. Plaintext http:// is not supported.
        /// </summary>
        /// <param name="endpoint">The base endpoint (domain or full HTTPS URL).</param>
        /// <param name="endpointPath">The endpoint path.</param>
        /// <returns>The full request URI string.</returns>
        /// <exception cref="ArgumentException">Thrown when the endpoint uses an http:// (non-TLS) scheme.</exception>
        public string BuildRequestUri(string endpoint, string endpointPath)
        {
            var normalizedEndpoint = endpoint.TrimEnd('/');

            if (normalizedEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Plaintext HTTP endpoints are not supported. Use HTTPS to protect credentials in transit.", nameof(endpoint));
            }

            if (normalizedEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return $"{normalizedEndpoint}{endpointPath}?api-version=1";
            }

            return $"https://{normalizedEndpoint}{endpointPath}?api-version=1";
        }

        /// <summary>
        /// Exports a batch of activities grouped by tenant and agent identity.
        /// </summary>
        /// <param name="groups"></param>
        /// <param name="resource"></param>
        /// <param name="options"></param>
        /// <param name="tokenResolver"></param>
        /// <param name="sendAsync"></param>
        /// <returns></returns>
        public Task<ExportResult> ExportBatchCoreAsync(
            IEnumerable<(string TenantId, string AgentId, List<Activity> Activities)> groups,
            Resource resource,
            Agent365ExporterOptions options,
            Func<string, string, Task<string?>> tokenResolver,
            Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync)
        {
            return ExportBatchCoreAsync(
                groups,
                resource,
                options,
                tokenResolver,
                sendAsync,
                CancellationToken.None);
        }

        internal async Task<ExportResult> ExportBatchCoreAsync(
            IEnumerable<(string TenantId, string AgentId, List<Activity> Activities)> groups,
            Resource resource,
            Agent365ExporterOptions options,
            Func<string, string, Task<string?>> tokenResolver,
            Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync,
            CancellationToken cancellationToken)
        {
            // A permanent failure (403 etc.) or a null/empty token for one identity group must not
            // discard telemetry for the other, unrelated groups. Such a failure is aggregated here and
            // the remaining groups are still processed; the batch is reported failed only after every
            // group has been given a chance to deliver or persist.
            var anyPermanentFailure = false;

            // The live send delegate already binds the export cancellation token (it is created as
            // request => httpClient.SendAsync(request, cancellationToken)). SendChunkOnceAsync now takes
            // a token-aware delegate to support the replay path; adapt the live one without changing its
            // behavior by discarding the (identical) inner token.
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> tokenAwareSendAsync =
                (request, _) => sendAsync(request);

            foreach (var g in groups)
            {
                var (tenantId, agentId, activities) = g;

                // Split the per-identity batch into byte-size chunks under MaxPayloadBytes.
                // Per-span truncation already caps individual spans at 250 KB; this provides
                // batch-level enforcement of the 1 MB server limit.
                var chunks = PayloadChunking.ChunkBySize(
                    activities,
                    PayloadChunking.EstimateActivityBytes,
                    options.MaxPayloadBytes);

                if (chunks.Count > 1)
                {
                    this._logger?.LogInformation(
                        "Agent365ExporterCore: Split {SpanCount} spans into {ChunkCount} chunks for tenantId {TenantId}, agentId {AgentId}.",
                        activities.Count, chunks.Count, tenantId, agentId);
                }

                var endpointOverride = Environment.GetEnvironmentVariable("A365_OBSERVABILITY_DOMAIN_OVERRIDE");
                var endpoint = !string.IsNullOrEmpty(endpointOverride)
                    ? endpointOverride
                    : options.DomainResolver.Invoke(tenantId);

                var endpointPath = BuildEndpointPath(tenantId, agentId, options.UseS2SEndpoint);
                var requestUri = BuildRequestUri(endpoint, endpointPath);

                // Host used for the a365 Network SDKStats 'host' dimension (stamp is extracted
                // by the recorder). Resolved once per identity group; null when the URI is
                // malformed, in which case the recorder falls back to "unknown".
                string? requestHost = Uri.TryCreate(requestUri, UriKind.Absolute, out var parsedUri) ? parsedUri.Host : null;

                // Agentic user ID is a 1:1 property of the identity group (agent -> agentic user);
                // resolved once from the first activity and used both for token resolution and for
                // the durable record persisted on hand-off.
                var agenticUserId = activities.Count > 0
                    ? activities[0].GetAttributeOrBaggage(OpenTelemetryConstants.AgentAUIDKey)
                    : null;

                string? token = null;
                var tokenResolverThrew = false;
                try
                {
                    // Prefer ContextualTokenResolver when set.
                    if (options.ContextualTokenResolver != null)
                    {
                        var identity = new AgentIdentity(agentId, agenticUserId);
                        var context = new TokenResolverContext(identity, tenantId);
                        token = await options.ContextualTokenResolver(context).ConfigureAwait(false);
                    }
                    else
                    {
                        token = await tokenResolver(agentId, tenantId).ConfigureAwait(false);
                    }

                    this._logger?.LogDebug("Agent365ExporterCore: Obtained token for agent {AgentId} tenant {TenantId}.", agentId, tenantId);
                }
                catch (Exception ex)
                {
                    tokenResolverThrew = true;
                    this._logger?.LogError(ex, "Agent365ExporterCore: TokenResolver threw for agent {AgentId} tenant {TenantId}.", agentId, tenantId);
                }

                // A token-resolver *exception* is treated as a transient outage: persist every chunk
                // for durable retry so telemetry is not lost. A null/empty token with no exception is
                // a permanent misconfiguration: fail fast without persisting.
                if (tokenResolverThrew)
                {
                    if (!PersistAllChunks(chunks, resource, tenantId, agentId, agenticUserId, options.UseS2SEndpoint))
                        return ExportResult.Failure;
                    continue;
                }

                if (string.IsNullOrEmpty(token))
                {
                    this._logger?.LogWarning("Agent365ExporterCore: No token obtained. Skipping export for this identity.");
                    // Permanent misconfiguration for this identity only: do not persist, do not abort the
                    // whole batch. Aggregate the failure and continue with the other identity groups.
                    anyPermanentFailure = true;
                    continue;
                }

                // Send each chunk once. On a retryable/transport failure or a closed gate the chunk is
                // handed to durable storage; a storage failure aborts the batch because the OpenTelemetry
                // batch processor does not re-export a failed batch, so returning Failure only signals the
                // drop and cannot itself trigger a retry.
                for (int i = 0; i < chunks.Count; i++)
                {
                    // Honor cancellation before touching the gate or storage for every chunk. A token that
                    // is already cancelled throws here, before any send or persist, so nothing is written.
                    cancellationToken.ThrowIfCancellationRequested();

                    var chunk = chunks[i];
                    var json = _formatter.FormatMany(chunk, resource);

                    var bodyBytes = Encoding.UTF8.GetByteCount(json);
                    this._logger?.LogDebug(
                        "Agent365ExporterCore: Sending chunk {ChunkIndex} of {ChunkCount} ({SpanCount} spans, {BodyBytes} bytes) to {RequestUri}.",
                        i + 1, chunks.Count, chunk.Count, bodyBytes, requestUri);

                    var record = new Agent365DurableRecord(
                        tenantId,
                        agentId,
                        agenticUserId,
                        options.UseS2SEndpoint,
                        json,
                        _utcNow());

                    if (!_gate.TryAcquire(out var ownsProbe))
                    {
                        // Gate is in backoff: skip the network entirely. Only log the outcome after
                        // TryStore so the message is accurate regardless of storage type.
                        if (!_storage.Value.TryStore(record))
                        {
                            this._logger?.LogWarning(
                                "Agent365ExporterCore: Transmission gate closed; persistence failed or is disabled for chunk {ChunkIndex} of {ChunkCount} — dropping.",
                                i + 1, chunks.Count);
                            return ExportResult.Failure;
                        }

                        this._logger?.LogWarning(
                            "Agent365ExporterCore: Transmission gate closed; persisted chunk {ChunkIndex} of {ChunkCount} for durable retry.",
                            i + 1, chunks.Count);
                        continue;
                    }

                    // The probe is released in finally only when this invocation acquired the single
                    // half-open probe (ownsProbe). Delivered/RetryableFailure already reset the probe
                    // via RecordSuccess/RecordRetryableFailure, leaving the finally a no-op; it covers
                    // the exits that record neither: permanent failure, caller cancellation, or an
                    // unexpected exception bubbling out.
                    var permanentFailureForGroup = false;
                    try
                    {
                        var outcome = await SendChunkOnceAsync(
                            requestUri,
                            requestHost,
                            json,
                            token!,
                            i + 1,
                            chunks.Count,
                            tokenAwareSendAsync,
                            cancellationToken).ConfigureAwait(false);

                        switch (outcome.Disposition)
                        {
                            case Agent365SendDisposition.Delivered:
                                _gate.RecordSuccess();
                                break;

                            case Agent365SendDisposition.RetryableFailure:
                                _gate.RecordRetryableFailure(outcome.RetryAfter);
                                if (!_storage.Value.TryStore(record))
                                {
                                    this._logger?.LogWarning(
                                        "Agent365ExporterCore: Retryable failure; persistence failed or is disabled for chunk {ChunkIndex} of {ChunkCount} — dropping.",
                                        i + 1, chunks.Count);
                                    return ExportResult.Failure;
                                }

                                break;

                            case Agent365SendDisposition.PermanentFailure:
                                // Aggregate and stop this identity's chunk sequence: later chunks for the
                                // same identity share the permanent condition and must not be sent. Other
                                // identity groups are unaffected and still processed.
                                anyPermanentFailure = true;
                                permanentFailureForGroup = true;
                                break;

                            case Agent365SendDisposition.Canceled:
                                throw new OperationCanceledException(cancellationToken);
                        }
                    }
                    finally
                    {
                        if (ownsProbe)
                            _gate.ReleaseProbe();
                    }

                    if (permanentFailureForGroup)
                        break;
                }
            }

            // Every group has now delivered, persisted, or been skipped. Fail only if at least one
            // group hit a permanent condition (permanent status or null/empty token); otherwise the
            // batch was fully handled.
            return anyPermanentFailure ? ExportResult.Failure : ExportResult.Success;
        }

        /// <summary>
        /// Replays a single durable record with freshly resolved authentication. Rebuilds the request
        /// endpoint from the record's tenant/agent/S2S fields, resolves a <em>fresh</em> token (preferring
        /// <see cref="Agent365ExporterOptions.ContextualTokenResolver"/> with the record's agent id and
        /// agentic user id), and sends the persisted payload exactly once. The single attempt is classified
        /// into an <see cref="Agent365SendOutcome"/> for the replay coordinator to act on. A token that
        /// cannot be resolved (null/empty result, or a resolver exception) yields
        /// <see cref="Agent365SendDisposition.TokenUnavailable"/> so the coordinator retains the
        /// already-persisted record for a later pass instead of discarding telemetry.
        /// <para>
        /// The record's essential fields (tenant, agent, payload) are already validated when the blob is
        /// deserialized, so request construction here cannot fail on this record's own data; the only
        /// exceptions it can raise are global/unknown faults (a throwing
        /// <see cref="Agent365ExporterOptions.DomainResolver"/>, a misconfigured plaintext endpoint, or an
        /// unexpected transport fault not classified as retryable). Those are intentionally allowed to
        /// propagate so the coordinator retains the record and stops the pass — preferring data safety over
        /// deleting durable telemetry as if it were per-record poison. Never logs the bearer token or payload.
        /// </para>
        /// </summary>
        internal async Task<Agent365SendOutcome> ReplayRecordAsync(
            Agent365DurableRecord record,
            Agent365ExporterOptions options,
            Func<string, string, Task<string?>> tokenResolver,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
            CancellationToken cancellationToken)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (tokenResolver == null) throw new ArgumentNullException(nameof(tokenResolver));
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));

            if (cancellationToken.IsCancellationRequested)
            {
                return new Agent365SendOutcome(Agent365SendDisposition.Canceled, null);
            }

            // Build the endpoint fresh from the record; an env override wins, exactly as the live path.
            var endpointOverride = Environment.GetEnvironmentVariable("A365_OBSERVABILITY_DOMAIN_OVERRIDE");
            var endpoint = !string.IsNullOrEmpty(endpointOverride)
                ? endpointOverride
                : options.DomainResolver.Invoke(record.TenantId);

            var endpointPath = BuildEndpointPath(record.TenantId, record.AgentId, record.UseS2SEndpoint);
            var requestUri = BuildRequestUri(endpoint, endpointPath);
            string? requestHost = Uri.TryCreate(requestUri, UriKind.Absolute, out var parsedUri) ? parsedUri.Host : null;

            string? token;
            try
            {
                // Prefer ContextualTokenResolver, resolving with the record's agent + agentic user id so
                // the AI-teammate (agentic user) and S2S (null user) scenarios both get the right context.
                if (options.ContextualTokenResolver != null)
                {
                    var identity = new AgentIdentity(record.AgentId, record.AgenticUserId);
                    var context = new TokenResolverContext(identity, record.TenantId);
                    token = await options.ContextualTokenResolver(context).ConfigureAwait(false);
                }
                else
                {
                    token = await tokenResolver(record.AgentId, record.TenantId).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // A resolver outage during replay is transient: retain the already-persisted record and
                // try again on a later pass rather than deleting telemetry.
                _logger.LogWarning(
                    ex,
                    "Agent365ExporterCore: TokenResolver threw during replay for agent {AgentId} tenant {TenantId}; retaining record.",
                    record.AgentId,
                    record.TenantId);
                return new Agent365SendOutcome(Agent365SendDisposition.TokenUnavailable, null);
            }

            if (string.IsNullOrEmpty(token))
            {
                // No token, but the record is already durable: retain it (unlike the live path, which fails
                // fast without persisting) so a later pass can deliver once a token becomes available.
                _logger.LogWarning(
                    "Agent365ExporterCore: No token obtained during replay for agent {AgentId} tenant {TenantId}; retaining record.",
                    record.AgentId,
                    record.TenantId);
                return new Agent365SendOutcome(Agent365SendDisposition.TokenUnavailable, null);
            }

            return await SendChunkOnceAsync(
                requestUri,
                requestHost,
                record.Payload,
                token!,
                chunkIndex: 1,
                chunkCount: 1,
                sendAsync,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Persists every chunk of an identity group to durable storage without attempting a network
        /// send. Used when a token-resolver exception makes an immediate send impossible but the
        /// telemetry must not be lost. Returns <c>false</c> on the first storage failure so the caller
        /// can surface an exporter failure (the batch processor does not re-export a failed batch).
        /// </summary>
        private bool PersistAllChunks(
            List<List<Activity>> chunks,
            Resource resource,
            string tenantId,
            string agentId,
            string? agenticUserId,
            bool useS2SEndpoint)
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                var json = _formatter.FormatMany(chunks[i], resource);
                var record = new Agent365DurableRecord(
                    tenantId,
                    agentId,
                    agenticUserId,
                    useS2SEndpoint,
                    json,
                    _utcNow());

                if (!_storage.Value.TryStore(record))
                    return false;
            }

            return true;
        }

        private static HttpRequestMessage CreateRequest(string requestUri, string json, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        private static void TrackExceptionAttempt(string? requestHost, Exception exception)
        {
            DistroNetworkSdkStats.Instance?.TrackException(requestHost, exception.GetType().FullName);
        }

        /// <summary>
        /// Sends a single chunk exactly once and classifies the outcome for the durable delivery
        /// pipeline. A 2xx response is <see cref="Agent365SendDisposition.Delivered"/>. A retryable
        /// status (401/408/429/5xx) or a transport-level failure (connection error or timeout) is a
        /// <see cref="Agent365SendDisposition.RetryableFailure"/> carrying the server's Retry-After
        /// when present. A permanent non-success (e.g. 403) is a
        /// <see cref="Agent365SendDisposition.PermanentFailure"/>; caller cancellation is reported as
        /// <see cref="Agent365SendDisposition.Canceled"/>. Never logs the bearer token or payload.
        /// </summary>
        private async Task<Agent365SendOutcome> SendChunkOnceAsync(
            string requestUri,
            string? requestHost,
            string json,
            string token,
            int chunkIndex,
            int chunkCount,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new Agent365SendOutcome(Agent365SendDisposition.Canceled, null);
            }

            using var request = CreateRequest(requestUri, json, token);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Thread the caller's token into the actual HTTP request so an in-flight send is
                // cancelled when the caller (e.g. the replay coordinator on shutdown) cancels.
                using var response = await sendAsync(request, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                var correlationId = response.Headers.Contains(CorrelationIdHeaderKey)
                    ? response.Headers.GetValues(CorrelationIdHeaderKey).FirstOrDefault()
                    : null;

                if (response.IsSuccessStatusCode)
                {
                    DistroNetworkSdkStats.Instance?.TrackResponse(requestHost, (int)response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
                    _logger?.LogInformation(
                        "Agent365ExporterCore: HTTP {StatusCode} success for chunk {ChunkIndex} of {ChunkCount}. Correlation ID: {CorrelationId}.",
                        (int)response.StatusCode,
                        chunkIndex,
                        chunkCount,
                        correlationId ?? "N/A");
                    return new Agent365SendOutcome(Agent365SendDisposition.Delivered, null);
                }

                var retryable = Agent365TransmissionGate.IsRetryable(response.StatusCode);

                DistroNetworkSdkStats.Instance?.TrackResponse(requestHost, (int)response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

                if (retryable)
                {
                    _logger.LogWarning(
                        "Agent365ExporterCore: HTTP {StatusCode} for chunk {ChunkIndex} of {ChunkCount}; persisting for durable retry. Correlation ID: {CorrelationId}.",
                        (int)response.StatusCode,
                        chunkIndex,
                        chunkCount,
                        correlationId ?? "N/A");
                    return new Agent365SendOutcome(
                        Agent365SendDisposition.RetryableFailure,
                        GetRetryAfter(response.Headers, _utcNow()));
                }

                // Permanent non-success (e.g. 403): preserve the actionable diagnostic and stop.
                LogNonSuccessResponse(response, correlationId, token, chunkIndex, chunkCount);
                return new Agent365SendOutcome(Agent365SendDisposition.PermanentFailure, null);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                return new Agent365SendOutcome(Agent365SendDisposition.Canceled, null);
            }
            catch (TaskCanceledException ex)
            {
                // A TaskCanceledException without caller cancellation is an HTTP client timeout.
                stopwatch.Stop();
                return HandleTransportFailure(ex, "timeout", requestHost, chunkIndex, chunkCount);
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                return HandleTransportFailure(ex, "error", requestHost, chunkIndex, chunkCount);
            }
        }

        /// <summary>
        /// Classifies a transport-level failure (connection error or client timeout) as a retryable
        /// outcome with no server-provided Retry-After (the gate applies jittered backoff). Records
        /// the exception for SDKStats and logs a warning without the bearer token or payload.
        /// </summary>
        private Agent365SendOutcome HandleTransportFailure(
            Exception exception,
            string kind,
            string? requestHost,
            int chunkIndex,
            int chunkCount)
        {
            TrackExceptionAttempt(requestHost, exception);
            _logger.LogWarning(
                exception,
                "Agent365ExporterCore: Network {Kind} for chunk {ChunkIndex} of {ChunkCount}; persisting for durable retry.",
                kind,
                chunkIndex,
                chunkCount);
            return new Agent365SendOutcome(Agent365SendDisposition.RetryableFailure, null);
        }

        /// <summary>
        /// Extracts the Retry-After hint from a non-success response. Prefers the delta-seconds form,
        /// falling back to the HTTP-date form relative to <paramref name="utcNow"/>. Returns
        /// <c>null</c> when absent or non-positive; the transmission gate then applies jittered
        /// exponential backoff.
        /// </summary>
        private static TimeSpan? GetRetryAfter(HttpResponseHeaders headers, DateTimeOffset utcNow)
        {
            var retryAfter = headers.RetryAfter;
            if (retryAfter == null)
            {
                return null;
            }

            if (retryAfter.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            {
                return delta;
            }

            if (retryAfter.Date is DateTimeOffset date)
            {
                var untilDate = date - utcNow;
                if (untilDate > TimeSpan.Zero)
                {
                    return untilDate;
                }
            }

            return null;
        }

        private static AddResult TryAddActivityToMap(Activity activity, Dictionary<(string tenant, string agent), List<Activity>> map)
        {
            if (activity is null) return AddResult.Null;

            var operationName = activity.GetAttributeOrBaggage(OpenTelemetryConstants.GenAiOperationNameKey);
            if (string.IsNullOrEmpty(operationName) || !OpenTelemetryConstants.GenAiOperationNames.Contains(operationName!))
                return AddResult.NonGenAI;

            var tenant = activity.GetAttributeOrBaggage(OpenTelemetryConstants.TenantIdKey);
            var agent = activity.GetAttributeOrBaggage(OpenTelemetryConstants.GenAiAgentIdKey) ?? activity.GetAttributeOrBaggage(OpenTelemetryConstants.AgentPlatformIdKey);

            if (string.IsNullOrEmpty(tenant) || string.IsNullOrEmpty(agent))
                return AddResult.MissingIdentity;

            var key = (tenant!, agent!);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<Activity>();
                map[key] = list;
            }
            list.Add(activity);
            return AddResult.Added;
        }

        private void LogPartitionResults(int groupCount, int nonGenAICount, int missingIdentityCount)
        {
            if (nonGenAICount > 0)
                _logger?.LogDebug("[Agent365Exporter] {NonGenAICount} non-genAI spans filtered out", nonGenAICount);
            if (missingIdentityCount > 0)
                _logger?.LogDebug("[Agent365Exporter] {MissingIdentityCount} spans skipped due to missing tenant or agent ID", missingIdentityCount);

            var skippedCount = nonGenAICount + missingIdentityCount;
            if (skippedCount > 0)
                _logger?.LogDebug("[Agent365Exporter] Partitioned into {GroupCount} identity groups ({SkippedCount} spans skipped)", groupCount, skippedCount);
        }

        private void LogNonSuccessResponse(HttpResponseMessage resp, string? correlationId, string token, int chunkIndex, int chunkCount)
        {
            var wwwAuth = resp.Headers.WwwAuthenticate.ToString();

            if (resp.StatusCode == HttpStatusCode.Forbidden && wwwAuth.Contains("insufficient_scope", StringComparison.OrdinalIgnoreCase))
            {
                var spStr = ExtractTokenIdentity(token);
                var identityDescription = !string.IsNullOrEmpty(spStr)
                    ? $" service principal ({spStr})"
                    : " your application's service principal";

                _logger?.LogError(
                    $"HTTP 403 authorization error: the token is missing the required 'Agent365.Observability.OtelWrite' app role. Grant the 'Agent365.Observability.OtelWrite' role to{identityDescription} and ensure admin consent has been granted. | Setup instructions: {DocsUrl403} | For Foundry: {FoundryUrl403} | Correlation ID: {correlationId ?? "N/A"}.");
            }
            else
            {
                _logger?.LogError(
                    "Agent365ExporterCore: HTTP {StatusCode} error. " +
                    "Chunk {ChunkIndex} of {ChunkCount} failed; aborting batch. " +
                    "WWW-Authenticate: {WwwAuthenticate}. Correlation ID: {CorrelationId}.",
                    (int)resp.StatusCode,
                    chunkIndex,
                    chunkCount,
                    string.IsNullOrEmpty(wwwAuth) ? "N/A" : wwwAuth,
                    correlationId ?? "N/A");
            }
        }

        /// <summary>
        /// Decodes the JWT payload to extract service principal identity claims.
        /// Returns a descriptive string like "app ID: xxx, object ID: yyy" or empty if not decodable.
        /// </summary>
        internal static string ExtractTokenIdentity(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3)
                    return string.Empty;

                var payloadBase64 = parts[1];
                // Pad to valid base64
                switch (payloadBase64.Length % 4)
                {
                    case 2: payloadBase64 += "=="; break;
                    case 3: payloadBase64 += "="; break;
                }

                var payloadBytes = Convert.FromBase64String(payloadBase64.Replace('-', '+').Replace('_', '/'));
                using var doc = JsonDocument.Parse(payloadBytes);
                var root = doc.RootElement;

                var spParts = new List<string>();
                if (root.TryGetProperty("appid", out var appId) && appId.ValueKind == JsonValueKind.String)
                    spParts.Add($"app ID: {appId.GetString()}");
                else if (root.TryGetProperty("azp", out var azp) && azp.ValueKind == JsonValueKind.String)
                    spParts.Add($"app ID: {azp.GetString()}");

                if (root.TryGetProperty("oid", out var oid) && oid.ValueKind == JsonValueKind.String)
                    spParts.Add($"object ID: {oid.GetString()}");

                return string.Join(", ", spParts);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Classification of a single send attempt used to drive the durable delivery pipeline.
    /// </summary>
    internal enum Agent365SendDisposition
    {
        /// <summary>The server accepted the chunk (2xx).</summary>
        Delivered,

        /// <summary>A retryable status (401/408/429/5xx) or transport failure; persist for later.</summary>
        RetryableFailure,

        /// <summary>A permanent non-success (e.g. 403) that must not be retried.</summary>
        PermanentFailure,

        /// <summary>The caller's cancellation token was signalled; abort without persisting.</summary>
        Canceled,

        /// <summary>
        /// No auth token could be resolved (null/empty result or a resolver exception). Produced only by
        /// the replay path: the record is already durable, so it is retained for a later pass rather than
        /// discarded. Never produced by the live send path (which fails a null token fast without
        /// persisting).
        /// </summary>
        TokenUnavailable
    }

    /// <summary>
    /// Result of a single send attempt: its <see cref="Disposition"/> and, for a retryable failure,
    /// the server-provided Retry-After hint (<c>null</c> when absent, so the gate applies backoff).
    /// </summary>
    internal readonly record struct Agent365SendOutcome
    {
        internal Agent365SendOutcome(Agent365SendDisposition disposition, TimeSpan? retryAfter)
        {
            Disposition = disposition;
            RetryAfter = retryAfter;
        }

        internal Agent365SendDisposition Disposition { get; }

        internal TimeSpan? RetryAfter { get; }
    }
}