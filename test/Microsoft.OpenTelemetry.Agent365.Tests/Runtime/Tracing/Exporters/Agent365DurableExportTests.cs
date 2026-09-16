// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using OpenTelemetry;
using OpenTelemetry.Resources;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Threading;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Exporters;

/// <summary>
/// Exercises the store-and-forward delivery contract of <see cref="Agent365ExporterCore"/>:
/// a single send attempt followed by a durable hand-off through
/// <see cref="Agent365TransmissionGate"/> and <see cref="IAgent365PersistentStorage"/>.
/// Retryable outcomes (401/408/429/5xx/transport) persist and report the batch handled;
/// permanent outcomes (403/4xx) and null tokens fail without persisting; storage failures
/// surface as an exporter failure (the batch processor does not re-export a failed batch).
/// </summary>
[TestClass]
public sealed class Agent365DurableExportTests
{
    private DateTimeOffset _now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private Agent365ExporterCore CreateCore(
        IAgent365PersistentStorage? storage = null,
        Agent365TransmissionGate? gate = null,
        ILogger<Agent365ExporterCore>? logger = null)
    {
        return new Agent365ExporterCore(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            logger ?? NullLogger<Agent365ExporterCore>.Instance,
            () => _now,
            storage ?? new FakeStorage(),
            gate);
    }

    private Agent365TransmissionGate OpenGate()
    {
        // Enter Backoff without advancing the clock so TryAcquire stays blocked for the test.
        var gate = new Agent365TransmissionGate(() => _now);
        gate.RecordRetryableFailure(null);
        return gate;
    }

    private static Activity CreateActivity(string? agenticUserId = null, string tenantId = "tenant-1", string agentId = "agent-1")
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Agent365Sdk",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        var source = new ActivitySource("Agent365Sdk");
        var activity = source.StartActivity("test-span", ActivityKind.Client);
        if (activity == null)
            throw new InvalidOperationException("Failed to start activity.");

        activity.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "invoke_agent");
        activity.SetTag(OpenTelemetryConstants.TenantIdKey, tenantId);
        activity.SetTag(OpenTelemetryConstants.GenAiAgentIdKey, agentId);
        if (!string.IsNullOrEmpty(agenticUserId))
            activity.SetTag(OpenTelemetryConstants.AgentAUIDKey, agenticUserId);
        activity.Stop();
        return activity;
    }

    private async Task<ExportResult> ExportActivitiesAsync(
        Agent365ExporterCore core,
        IEnumerable<Activity> activities,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync,
        long maxPayloadBytes = 900_000,
        Func<string, string, Task<string?>>? tokenResolver = null,
        CancellationToken cancellationToken = default)
    {
        var groups = core.PartitionByIdentity(activities);
        var options = new Agent365ExporterOptions
        {
            DomainResolver = _ => "api.example.com",
            TokenResolver = (_, _) => Task.FromResult<string?>("test-token"),
            MaxPayloadBytes = maxPayloadBytes
        };
        return await core.ExportBatchCoreAsync(
            groups: groups,
            resource: ResourceBuilder.CreateEmpty().Build(),
            options: options,
            tokenResolver: tokenResolver ?? ((_, _) => Task.FromResult<string?>("test-token")),
            sendAsync: sendAsync,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<ExportResult> ExportOneAsync(
        Agent365ExporterCore core,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync,
        Func<string, string, Task<string?>>? tokenResolver = null,
        Activity? activity = null,
        CancellationToken cancellationToken = default)
    {
        activity ??= CreateActivity();
        var groups = core.PartitionByIdentity(new[] { activity });
        var options = new Agent365ExporterOptions
        {
            DomainResolver = _ => "api.example.com",
            TokenResolver = (_, _) => Task.FromResult<string?>("test-token")
        };
        return await core.ExportBatchCoreAsync(
            groups: groups,
            resource: ResourceBuilder.CreateEmpty().Build(),
            options: options,
            tokenResolver: tokenResolver ?? ((_, _) => Task.FromResult<string?>("test-token")),
            sendAsync: sendAsync,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ---- Core store-and-forward contract (task brief) ------------------------------------

    [TestMethod]
    public async Task RetryableChunkIsPersistedAfterOneAttemptAndReportedHandled()
    {
        var storage = new FakeStorage();
        var sends = 0;

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            });

        result.Should().Be(ExportResult.Success);
        sends.Should().Be(1);
        storage.Records.Should().ContainSingle();
        storage.Records[0].Payload.Should().Contain("resourceSpans");
    }

    [TestMethod]
    public async Task OpenGatePersistsWithoutHttpCall()
    {
        var storage = new FakeStorage();
        var sends = 0;

        var result = await ExportOneAsync(
            CreateCore(storage: storage, gate: OpenGate()),
            _ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        result.Should().Be(ExportResult.Success);
        sends.Should().Be(0);
        storage.Records.Should().ContainSingle();
    }

    [TestMethod]
    public async Task StorageFailureReturnsExporterFailure()
    {
        var storage = new FakeStorage { StoreResult = false };

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        result.Should().Be(ExportResult.Failure);
    }

    [TestMethod]
    public async Task OpenGateStorageFailureReturnsExporterFailure()
    {
        var storage = new FakeStorage { StoreResult = false };
        var sends = 0;

        var result = await ExportOneAsync(
            CreateCore(storage: storage, gate: OpenGate()),
            _ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        result.Should().Be(ExportResult.Failure);
        sends.Should().Be(0);
    }

    [TestMethod]
    public async Task PermanentFailureIsNotPersisted()
    {
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        result.Should().Be(ExportResult.Failure);
        storage.Records.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DeliveredChunkIsNotPersistedAndKeepsGateClosed()
    {
        var gate = new Agent365TransmissionGate(() => _now);
        var storage = new FakeStorage();
        var sends = 0;

        var result = await ExportOneAsync(
            CreateCore(storage: storage, gate: gate),
            _ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        result.Should().Be(ExportResult.Success);
        sends.Should().Be(1);
        storage.Records.Should().BeEmpty();
        gate.ConsecutiveErrors.Should().Be(0);
    }

    // ---- Retryable vs permanent status coverage ------------------------------------------

    [DataTestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.RequestTimeout)]
    [DataRow((HttpStatusCode)429)]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.BadGateway)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.GatewayTimeout)]
    public async Task RetryableStatusIsPersistedAndReportedHandled(HttpStatusCode status)
    {
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromResult(new HttpResponseMessage(status)));

        result.Should().Be(ExportResult.Success);
        storage.Records.Should().ContainSingle();
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.Forbidden)]
    [DataRow(HttpStatusCode.BadRequest)]
    [DataRow(HttpStatusCode.NotFound)]
    [DataRow(HttpStatusCode.Conflict)]
    public async Task PermanentStatusIsNotPersisted(HttpStatusCode status)
    {
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromResult(new HttpResponseMessage(status)));

        result.Should().Be(ExportResult.Failure);
        storage.Records.Should().BeEmpty();
    }

    // ---- Token resolution ----------------------------------------------------------------

    [TestMethod]
    public async Task TokenResolverExceptionPersistsChunk()
    {
        var storage = new FakeStorage();
        var sends = 0;

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            tokenResolver: (_, _) => throw new InvalidOperationException("token boom"));

        result.Should().Be(ExportResult.Success);
        sends.Should().Be(0);
        storage.Records.Should().ContainSingle();
    }

    [TestMethod]
    public async Task TokenResolverExceptionWithStorageFailureReturnsExporterFailure()
    {
        var storage = new FakeStorage { StoreResult = false };

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            tokenResolver: (_, _) => throw new InvalidOperationException("token boom"));

        result.Should().Be(ExportResult.Failure);
    }

    [TestMethod]
    public async Task NullTokenIsPermanentAndDoesNotPersist()
    {
        var storage = new FakeStorage();
        var sends = 0;

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            tokenResolver: (_, _) => Task.FromResult<string?>(null));

        result.Should().Be(ExportResult.Failure);
        sends.Should().Be(0);
        storage.Records.Should().BeEmpty();
    }

    [TestMethod]
    public async Task EmptyTokenIsPermanentAndDoesNotPersist()
    {
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            tokenResolver: (_, _) => Task.FromResult<string?>(string.Empty));

        result.Should().Be(ExportResult.Failure);
        storage.Records.Should().BeEmpty();
    }

    // ---- Agentic user id -----------------------------------------------------------------

    [TestMethod]
    public async Task PersistedRecordIncludesAgenticUserId()
    {
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            activity: CreateActivity(agenticUserId: "user-42"));

        result.Should().Be(ExportResult.Success);
        storage.Records.Should().ContainSingle();
        storage.Records[0].AgenticUserId.Should().Be("user-42");
    }

    [TestMethod]
    public async Task PersistedRecordOmitsAgenticUserIdWhenAbsent()
    {
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        result.Should().Be(ExportResult.Success);
        storage.Records.Should().ContainSingle();
        storage.Records[0].AgenticUserId.Should().BeNull();
    }

    [TestMethod]
    public async Task PersistedRecordCarriesIdentityAndClock()
    {
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        result.Should().Be(ExportResult.Success);
        var record = storage.Records.Should().ContainSingle().Which;
        record.TenantId.Should().Be("tenant-1");
        record.AgentId.Should().Be("agent-1");
        record.CreatedAtUtc.Should().Be(_now);
    }

    // ---- Transport failures --------------------------------------------------------------

    [TestMethod]
    public async Task TransportExceptionPersistsChunk()
    {
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("network")));

        result.Should().Be(ExportResult.Success);
        storage.Records.Should().ContainSingle();
    }

    [TestMethod]
    public async Task TimeoutPersistsChunkWhenCallerNotCanceled()
    {
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")));

        result.Should().Be(ExportResult.Success);
        storage.Records.Should().ContainSingle();
    }

    [TestMethod]
    public async Task TransportFailureWithStorageFailureReturnsExporterFailure()
    {
        var storage = new FakeStorage { StoreResult = false };

        var result = await ExportOneAsync(
            CreateCore(storage: storage),
            _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("network")));

        result.Should().Be(ExportResult.Failure);
    }

    // ---- Caller cancellation -------------------------------------------------------------

    [TestMethod]
    public async Task CallerCancellationThrowsAndDoesNotPersist()
    {
        var storage = new FakeStorage();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sends = 0;

        Func<Task> act = () => ExportOneAsync(
            CreateCore(storage: storage),
            _ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sends.Should().Be(0);
        storage.Records.Should().BeEmpty();
    }

    // ---- Multi-chunk delivery / isolation ------------------------------------------------

    [TestMethod]
    public async Task RetryableFirstChunkPersistsCurrentAndRemainingChunks()
    {
        // Three same-identity spans forced into three separate chunks (MaxPayloadBytes = 1, each
        // span's estimate far exceeds it, so ChunkBySize yields one span per chunk).
        var storage = new FakeStorage();
        var sends = 0;

        var result = await ExportActivitiesAsync(
            CreateCore(storage: storage),
            new[] { CreateActivity(), CreateActivity(), CreateActivity() },
            _ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            },
            maxPayloadBytes: 1);

        // The first chunk is sent once and returns a retryable status: it is persisted and the gate
        // enters backoff. The two remaining chunks are then persisted without any further network call.
        result.Should().Be(ExportResult.Success);
        sends.Should().Be(1);
        storage.Records.Should().HaveCount(3);
    }

    [TestMethod]
    public async Task RetryableFailureForOneTenantDoesNotBlockAnotherTenant()
    {
        var storage = new FakeStorage();
        var tenantASends = 0;
        var tenantBSends = 0;

        var result = await ExportActivitiesAsync(
            CreateCore(storage: storage),
            new[]
            {
                CreateActivity(tenantId: "tenant-a"),
                CreateActivity(tenantId: "tenant-b"),
            },
            request =>
            {
                var uri = request.RequestUri!.ToString();
                if (uri.Contains("tenants/tenant-a/"))
                {
                    tenantASends++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                tenantBSends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        result.Should().Be(ExportResult.Success);
        tenantASends.Should().Be(1);
        tenantBSends.Should().Be(1);
        storage.Records.Should().ContainSingle();
        storage.Records[0].TenantId.Should().Be("tenant-a");
    }

    [TestMethod]
    public async Task LiveRetryableFailureBacksOffReplayForSameTenantThroughSharedRegistry()
    {
        // One core, one real (non-overridden) tenant gate registry: a live 503 for tenant A must be
        // visible to a replay coordinator built from the SAME core.Gates registry, proving live and
        // replay coordinate through the exact same per-tenant gate rather than independent state.
        var storage = new FakeStorage();
        var core = CreateCore(storage: storage);

        var liveResult = await ExportOneAsync(
            core,
            sendAsync: _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            activity: CreateActivity(tenantId: "tenant-a"));

        liveResult.Should().Be(ExportResult.Success, "the retryable live failure hands the chunk to durable storage");
        storage.Records.Should().ContainSingle();
        storage.Records[0].TenantId.Should().Be("tenant-a");

        var storedRecord = FakeStoredRecord.From(storage.Records[0]);
        var replayStorage = new FakeStorage(storedRecord);
        var replaySends = 0;
        var coordinator = new Agent365ReplayCoordinator(
            replayStorage,
            core.Gates,
            replayAsync: (record, ct) => core.ReplayRecordAsync(
                record,
                new Agent365ExporterOptions { DomainResolver = _ => "api.example.com" },
                tokenResolver: (_, _) => Task.FromResult<string?>("replay-token"),
                sendAsync: (_, _) =>
                {
                    replaySends++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                },
                ct),
            NullLogger.Instance);

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        replaySends.Should().Be(
            0, "tenant A's gate is already in backoff from the live send, so replay must not attempt HTTP");
        storedRecord.DeleteCalls.Should().Be(
            0, "the record stays retained while the shared registry's tenant A gate is in backoff");
    }

    [TestMethod]
    public async Task PermanentFirstChunkStopsRemainingChunksForThatIdentity()
    {
        // Three same-identity chunks; the first send is a permanent 403. Later chunks for the same
        // identity share the permanent condition and must not be sent or persisted.
        var storage = new FakeStorage();
        var sends = 0;

        var result = await ExportActivitiesAsync(
            CreateCore(storage: storage),
            new[] { CreateActivity(), CreateActivity(), CreateActivity() },
            _ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            },
            maxPayloadBytes: 1);

        result.Should().Be(ExportResult.Failure);
        sends.Should().Be(1);
        storage.Records.Should().BeEmpty();
    }

    [TestMethod]
    public async Task PermanentFailureInOneGroupDoesNotDiscardOtherGroups()
    {
        // Two unrelated identity groups. agent-perm returns a permanent 403; agent-ok returns a
        // retryable 503. The permanent failure must not discard the unrelated group: agent-ok is still
        // processed and persisted, and the batch fails only because agent-perm hit a permanent status.
        var storage = new FakeStorage();
        var permSends = 0;
        var okSends = 0;

        var result = await ExportActivitiesAsync(
            CreateCore(storage: storage),
            new[]
            {
                CreateActivity(agentId: "agent-perm"),
                CreateActivity(agentId: "agent-ok"),
            },
            request =>
            {
                var uri = request.RequestUri!.ToString();
                if (uri.Contains("agent-perm"))
                {
                    permSends++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                }

                okSends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            });

        result.Should().Be(ExportResult.Failure);
        permSends.Should().Be(1);
        okSends.Should().Be(1);
        storage.Records.Should().ContainSingle();
        storage.Records[0].AgentId.Should().Be("agent-ok");
    }

    [TestMethod]
    public async Task NullTokenForOneGroupDoesNotDiscardOtherGroups()
    {
        // agent-null resolves a null token (permanent misconfiguration, not persisted); agent-ok
        // resolves a valid token and returns a retryable 503 that is persisted. The null-token group
        // must not abort the batch before the unrelated group is processed.
        var storage = new FakeStorage();

        var result = await ExportActivitiesAsync(
            CreateCore(storage: storage),
            new[]
            {
                CreateActivity(agentId: "agent-null"),
                CreateActivity(agentId: "agent-ok"),
            },
            request =>
            {
                var uri = request.RequestUri!.ToString();
                return Task.FromResult(new HttpResponseMessage(
                    uri.Contains("agent-ok") ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
            },
            tokenResolver: (agentId, _) =>
                Task.FromResult<string?>(agentId == "agent-null" ? null : "test-token"));

        result.Should().Be(ExportResult.Failure);
        storage.Records.Should().ContainSingle();
        storage.Records[0].AgentId.Should().Be("agent-ok");
    }

    [TestMethod]
    public async Task MalformedWhitespaceTenantGroupDoesNotPreventLaterValidTenantGroupFromSending()
    {
        // ExportBatchCoreAsync is internal and can be called directly with a caller-constructed groups
        // list that bypasses PartitionByIdentity's own filtering. A whitespace-only tenant id reaching
        // the gate registry's Acquire(...) would throw ArgumentException; that must be handled
        // defensively as a per-group permanent failure rather than propagating out of the whole batch
        // and discarding a later, valid tenant group.
        var storage = new FakeStorage();
        var okSends = 0;
        var core = CreateCore(storage: storage);
        using var malformedActivity = CreateActivity(tenantId: "  ", agentId: "agent-malformed");
        using var okActivity = CreateActivity(tenantId: "tenant-ok", agentId: "agent-ok");

        var groups = new List<(string TenantId, string AgentId, List<Activity> Activities)>
        {
            ("   ", "agent-malformed", new List<Activity> { malformedActivity }),
            ("tenant-ok", "agent-ok", new List<Activity> { okActivity }),
        };

        var options = new Agent365ExporterOptions
        {
            DomainResolver = _ => "api.example.com",
            TokenResolver = (_, _) => Task.FromResult<string?>("test-token"),
        };

        Func<Task<ExportResult>> act = () => core.ExportBatchCoreAsync(
            groups: groups,
            resource: ResourceBuilder.CreateEmpty().Build(),
            options: options,
            tokenResolver: (_, _) => Task.FromResult<string?>("test-token"),
            sendAsync: _ =>
            {
                okSends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            cancellationToken: CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Which.Should().Be(ExportResult.Failure, "the malformed group is a permanent failure for the batch");
        okSends.Should().Be(1, "the later valid tenant group must still be sent");
        storage.Records.Should().BeEmpty("neither group persists: the malformed one is rejected, the valid one is delivered");
    }

    [TestMethod]
    public async Task CancellationBetweenChunksThrowsAfterFirstChunkPersisted()
    {
        // Two same-identity chunks. The caller cancels after the first send; the per-chunk cancellation
        // check must throw before the second chunk touches the gate or storage.
        var storage = new FakeStorage();
        using var cts = new CancellationTokenSource();
        var sends = 0;

        Func<Task> act = () => ExportActivitiesAsync(
            CreateCore(storage: storage),
            new[] { CreateActivity(), CreateActivity() },
            _ =>
            {
                sends++;
                cts.Cancel();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            },
            maxPayloadBytes: 1,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Only the first chunk was sent and persisted; the second chunk short-circuits on cancellation.
        sends.Should().Be(1);
        storage.Records.Should().ContainSingle();
    }

    // ---- Gate backoff propagation (Retry-After / jitter) ---------------------------------

    [TestMethod]
    public async Task RetryAfterDeltaHeaderIsHonoredByGate()
    {
        var gate = new Agent365TransmissionGate(() => _now);
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage, gate: gate),
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
                return Task.FromResult(response);
            });

        result.Should().Be(ExportResult.Success);
        storage.Records.Should().ContainSingle();
        gate.CurrentDelay.Should().Be(TimeSpan.FromSeconds(45));
    }

    [TestMethod]
    public async Task RetryAfterDateHeaderIsHonoredByGate()
    {
        var gate = new Agent365TransmissionGate(() => _now);
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage, gate: gate),
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(_now.AddSeconds(90));
                return Task.FromResult(response);
            });

        result.Should().Be(ExportResult.Success);
        gate.CurrentDelay.Should().BeCloseTo(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task TransportFailureUsesJitteredBackoff()
    {
        var gate = new Agent365TransmissionGate(() => _now);
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage, gate: gate),
            _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("network")));

        result.Should().Be(ExportResult.Success);
        gate.CurrentDelay.Should().BeGreaterThanOrEqualTo(Agent365TransmissionGate.MinimumDelay);
        gate.CurrentDelay.Should().BeLessThanOrEqualTo(Agent365TransmissionGate.MaximumDelay);
    }

    // ---- Disabled offline storage: retryable outcomes must surface as Failure --------------

    /// <summary>
    /// When the core uses <see cref="DisabledAgent365Storage"/> (offline storage disabled or init
    /// failed), a retryable 503 cannot be durably queued: <see cref="ExportResult.Failure"/> is
    /// returned so the caller knows the telemetry was dropped, not silently swallowed.
    /// </summary>
    [TestMethod]
    public async Task DisabledStorage_503_ReturnsFailure()
    {
        var result = await ExportOneAsync(
            CreateCore(storage: new DisabledAgent365Storage()),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        result.Should().Be(ExportResult.Failure);
    }

    [TestMethod]
    public async Task PublicCtor_503_ReturnsFailure()
    {
        // The public Agent365ExporterCore(formatter, logger) constructor wires DisabledAgent365Storage.
        var core = new Agent365ExporterCore(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            NullLogger<Agent365ExporterCore>.Instance);

        var result = await ExportOneAsync(
            core,
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        result.Should().Be(ExportResult.Failure);
    }

    [TestMethod]
    public async Task DisabledStorage_TransportFailure_ReturnsFailure()
    {
        var result = await ExportOneAsync(
            CreateCore(storage: new DisabledAgent365Storage()),
            _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("network")));

        result.Should().Be(ExportResult.Failure);
    }

    [TestMethod]
    public async Task DisabledStorage_GateClosed_ReturnsFailure()
    {
        var result = await ExportOneAsync(
            CreateCore(storage: new DisabledAgent365Storage(), gate: OpenGate()),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        result.Should().Be(ExportResult.Failure);
    }

    [TestMethod]
    public async Task DisabledStorage_TokenResolverException_ReturnsFailure()
    {
        var result = await ExportOneAsync(
            CreateCore(storage: new DisabledAgent365Storage()),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            tokenResolver: (_, _) => throw new InvalidOperationException("token boom"));

        result.Should().Be(ExportResult.Failure);
    }

    // ---- 403 actionable logging (preserved) ----------------------------------------------

    [TestMethod]
    public async Task Forbidden403WithInsufficientScopeLogsActionableError()
    {
        var logger = new CapturingLogger();
        var storage = new FakeStorage();

        var result = await ExportOneAsync(
            CreateCore(storage: storage, logger: logger),
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
                response.Headers.WwwAuthenticate.ParseAdd(
                    "Bearer error=\"insufficient_scope\", " +
                    "error_description=\"Required app role: Agent365.Observability.OtelWrite\", " +
                    "scope=\"Agent365.Observability.OtelWrite\"");
                return Task.FromResult(response);
            });

        result.Should().Be(ExportResult.Failure);
        storage.Records.Should().BeEmpty();
        var error = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which;
        error.Message.Should().Contain("Agent365.Observability.OtelWrite");
        error.Message.Should().Contain("aka.ms/a365-403");
    }

    // ---- Exception_Count metric (SDKStats) -----------------------------------------------

    [TestMethod]
    public async Task HttpRequestExceptionRecordsExceptionCountMetric()
    {
        DistroNetworkSdkStats.ResetForTesting();
        DistroNetworkSdkStats.Initialize("N/A", "test");
        try
        {
            var recorded = new List<(string Instrument, long Value, string? ExceptionType)>();
            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == DistroNetworkSdkStats.MeterName)
                        l.EnableMeasurementEvents(instrument);
                },
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (instrument.Name != "Exception_Count") return;
                string? exType = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "exceptionType") { exType = tag.Value as string; break; }
                }
                recorded.Add((instrument.Name, value, exType));
            });
            listener.Start();

            await ExportOneAsync(
                CreateCore(),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("network")));

            recorded.Should().ContainSingle(m =>
                m.Instrument == "Exception_Count" &&
                m.ExceptionType == typeof(HttpRequestException).FullName);
        }
        finally
        {
            DistroNetworkSdkStats.ResetForTesting();
        }
    }

    // ---- Helpers -------------------------------------------------------------------------

    private sealed class CapturingLogger : ILogger<Agent365ExporterCore>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

/// <summary>
/// In-memory <see cref="IAgent365PersistentStorage"/> test double. <see cref="StoreResult"/>
/// controls whether <see cref="TryStore"/> succeeds; successful stores are captured in
/// <see cref="Records"/>. For the replay path, records seeded via the constructor are handed out by
/// <see cref="TryGetNext"/> in order (one per call) so a pass drains at most its per-pass cap.
/// Never touches disk. Shared with <c>Agent365ExporterTests</c> and <c>Agent365ReplayCoordinatorTests</c>.
/// </summary>
internal sealed class FakeStorage : IAgent365PersistentStorage
{
    private readonly Queue<IAgent365StoredRecord> _pending = new();

    public FakeStorage(params FakeStoredRecord[] pending)
    {
        foreach (var record in pending)
            _pending.Enqueue(record);
    }

    public List<Agent365DurableRecord> Records { get; } = new();

    public bool StoreResult { get; set; } = true;

    /// <summary>Number of seeded replay records not yet handed out by <see cref="TryGetNext"/>.</summary>
    public int PendingCount => _pending.Count;

    public bool TryStore(Agent365DurableRecord record)
    {
        if (StoreResult)
            Records.Add(record);
        return StoreResult;
    }

    public bool TryGetNext([NotNullWhen(true)] out IAgent365StoredRecord? record)
    {
        if (_pending.Count > 0)
        {
            record = _pending.Dequeue();
            return true;
        }

        record = null;
        return false;
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// In-memory <see cref="IAgent365StoredRecord"/> test double for the replay path. Each of
/// <see cref="TryLease"/>, <see cref="Read"/> and <see cref="TryDelete"/> returns its configurable
/// result and records how often it was called; <see cref="DeleteCalls"/> lets a test distinguish a
/// deleted record from a retained one. Never touches disk.
/// </summary>
internal sealed class FakeStoredRecord : IAgent365StoredRecord
{
    private readonly Agent365DurableRecord? _record;

    private FakeStoredRecord(Agent365DurableRecord? record)
    {
        _record = record;
    }

    /// <summary>Creates a readable stored record wrapping <paramref name="record"/>.</summary>
    public static FakeStoredRecord From(Agent365DurableRecord record) => new(record);

    /// <summary>Creates a poison stored record whose payload is invalid.</summary>
    public static FakeStoredRecord Corrupt() => new(null) { ReadResult = Agent365StoredRecordReadResult.InvalidPayload };

    public bool LeaseResult { get; set; } = true;
    public Agent365StoredRecordReadResult ReadResult { get; set; } = Agent365StoredRecordReadResult.Success;
    public bool DeleteResult { get; set; } = true;

    public int LeaseCalls { get; private set; }
    public int ReadCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public TimeSpan LeasedDuration { get; private set; }

    public bool TryLease(TimeSpan duration)
    {
        LeaseCalls++;
        if (LeaseResult)
            LeasedDuration = duration;
        return LeaseResult;
    }

    public Agent365StoredRecordReadResult Read([NotNullWhen(true)] out Agent365DurableRecord? record)
    {
        ReadCalls++;
        if (ReadResult == Agent365StoredRecordReadResult.Success && _record != null)
        {
            record = _record;
            return Agent365StoredRecordReadResult.Success;
        }

        record = null;
        return ReadResult;
    }

    public bool TryDelete()
    {
        DeleteCalls++;
        return DeleteResult;
    }
}
