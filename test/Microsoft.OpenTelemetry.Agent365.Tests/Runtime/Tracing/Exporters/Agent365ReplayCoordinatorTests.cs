// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Exporters;

/// <summary>
/// Exercises the durable replay contract of <see cref="Agent365ReplayCoordinator"/> and the fresh
/// authentication entry point <see cref="Agent365ExporterCore.ReplayRecordAsync"/>:
/// each pass scans a bounded number of leased records, attempts at most ten, and only after a record
/// is leased and successfully deserialized consults that record's tenant's gate through the shared
/// <see cref="Agent365TransmissionGateRegistry"/> — the same registry the live send path coordinates
/// through, so a tenant backed off by a live failure is also skipped by replay and vice versa. A
/// tenant whose gate is in backoff has its record retained without an HTTP attempt, but the pass keeps
/// scanning later records so another tenant still gets its chance in the same pass. Per record, once
/// a fresh token is resolved and the send is attempted: success deletes, a retryable failure backs off
/// only that tenant's gate and retains the record (the pass continues to later records), a permanent
/// send failure or an unreadable/poison (undeserializable) blob deletes it, an unknown replay fault or
/// global misconfiguration retains the current record and stops the whole pass (durable telemetry is
/// never deleted for an unknown fault), a missing token retains the record for a later pass, and a
/// delete failure after a successful send logs the duplicate risk. Each record's owned half-open probe
/// is always released when that record recorded no terminal gate outcome.
/// </summary>
[TestClass]
public sealed class Agent365ReplayCoordinatorTests
{
    private DateTimeOffset _now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ helpers

    private static Agent365DurableRecord CreateRecord(
        string tenantId = "tenant-1",
        string agentId = "agent-1",
        string? agenticUserId = null,
        bool useS2SEndpoint = false,
        string? payload = null) =>
        new(
            tenantId,
            agentId,
            agenticUserId,
            useS2SEndpoint,
            payload ?? "{\"resourceSpans\":[]}",
            new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero));

    private Agent365ReplayCoordinator CreateCoordinator(
        IAgent365PersistentStorage storage,
        Func<string, string, Task<string?>>? tokenResolver = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? sendAsync = null,
        Agent365TransmissionGate? gate = null,
        Agent365TransmissionGateRegistry? gates = null,
        ILogger? logger = null,
        int maxRecordsPerPass = 10,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? replayInterval = null,
        AsyncContextualTokenResolver? contextualResolver = null,
        TenantDomainResolver? domainResolver = null)
    {
        // gate/gates are left null by default so most tests exercise the real per-tenant registry
        // (each tenant id gets its own independently-tracked gate). Tests that need a handle on a
        // specific tenant's gate object pass `gate:` (pinning every tenant to that one instance,
        // sufficient for single-tenant scenarios) or `gates:` (a real registry shared across the
        // live and replay paths, needed for genuine multi-tenant scenarios).
        tokenResolver ??= (_, _) => Task.FromResult<string?>("fresh-token");
        sendAsync ??= (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        var options = new Agent365ExporterOptions
        {
            DomainResolver = domainResolver ?? (_ => "api.example.com"),
            ContextualTokenResolver = contextualResolver,
        };

        var core = new Agent365ExporterCore(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            NullLogger<Agent365ExporterCore>.Instance,
            () => _now,
            storage,
            gate,
            gates);

        return new Agent365ReplayCoordinator(
            storage,
            core.Gates,
            replayAsync: (record, ct) => core.ReplayRecordAsync(record, options, tokenResolver, sendAsync, ct),
            logger ?? NullLogger.Instance,
            replayInterval,
            maxRecordsPerPass,
            delayAsync);
    }

    /// <summary>Puts a gate into the half-open Probe state so the next acquire owns the single probe.</summary>
    private Agent365TransmissionGate ProbeGate()
    {
        var gate = new Agent365TransmissionGate(() => _now);
        gate.RecordRetryableFailure(TimeSpan.FromSeconds(30));
        _now = _now.AddSeconds(31);
        return gate;
    }

    /// <summary>Puts a gate into Backoff so acquiring a permit fails (replay is skipped this pass).</summary>
    private Agent365TransmissionGate BackoffGate()
    {
        var gate = new Agent365TransmissionGate(() => _now);
        gate.RecordRetryableFailure(null);
        return gate;
    }

    // ------------------------------------------------------------------ success

    [TestMethod]
    public async Task SuccessfulReplayUsesFreshTokenAndDeletesRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var tokens = 0;
        var sentAuthorization = string.Empty;
        var coordinator = CreateCoordinator(
            storage,
            tokenResolver: (_, _) =>
            {
                tokens++;
                return Task.FromResult<string?>("fresh-token");
            },
            sendAsync: (request, _) =>
            {
                sentAuthorization = request.Headers.Authorization!.Parameter!;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        tokens.Should().Be(1);
        sentAuthorization.Should().Be("fresh-token");
        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task SuccessfulReplayBuildsFreshEndpointFromRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord(tenantId: "tenant-9", agentId: "agent-9"));
        var storage = new FakeStorage(stored);
        Uri? sentUri = null;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, _) =>
            {
                sentUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sentUri.Should().NotBeNull();
        sentUri!.ToString().Should().Be(
            "https://api.example.com/observability/tenants/tenant-9/otlp/agents/agent-9/traces?api-version=1");
        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task SuccessfulReplaySendsRecordPayload()
    {
        var stored = FakeStoredRecord.From(CreateRecord(payload: "{\"resourceSpans\":[{\"marker\":42}]}"));
        var storage = new FakeStorage(stored);
        var body = string.Empty;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: async (request, _) =>
            {
                body = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        body.Should().Contain("\"marker\":42");
        stored.DeleteCalls.Should().Be(1);
    }

    // ------------------------------------------------------------------ retryable

    [TestMethod]
    public async Task RetryableFailureForSameTenantRetainsLaterRecordWithoutSending()
    {
        // Both records belong to the default tenant ("tenant-1"). Once the first record backs that
        // tenant's gate off, the second record for the SAME tenant must still be leased and read (to
        // learn its tenant) but must not attempt HTTP while that tenant's gate is in backoff.
        var first = FakeStoredRecord.From(CreateRecord(agentId: "agent-1"));
        var second = FakeStoredRecord.From(CreateRecord(agentId: "agent-2"));
        var storage = new FakeStorage(first, second);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(1, "the second same-tenant record is skipped once that tenant's gate is in backoff");
        first.DeleteCalls.Should().Be(0, "the first record is retained after its retryable failure");
        second.LeaseCalls.Should().Be(1, "the second record is still leased to learn its tenant before the gate check");
        second.ReadCalls.Should().Be(1, "the second record is still read to learn its tenant before the gate check");
        second.DeleteCalls.Should().Be(0, "the second record is retained without an HTTP attempt while its tenant's gate is in backoff");
    }

    [TestMethod]
    public async Task RetryableFailureForOneTenantDoesNotStopAnotherTenant()
    {
        var tenantA = FakeStoredRecord.From(CreateRecord(tenantId: "tenant-a", agentId: "agent-a"));
        var tenantB = FakeStoredRecord.From(CreateRecord(tenantId: "tenant-b", agentId: "agent-b"));
        var storage = new FakeStorage(tenantA, tenantB);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, _) =>
            {
                sends++;
                var isTenantA = request.RequestUri!.ToString().Contains("tenants/tenant-a/");
                return Task.FromResult(new HttpResponseMessage(
                    isTenantA ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(2, "a retryable failure for tenant A must not stop the pass from reaching tenant B");
        tenantA.DeleteCalls.Should().Be(0, "tenant A's record is retained after its retryable failure");
        tenantB.DeleteCalls.Should().Be(1, "tenant B is delivered and deleted in the same pass");
    }

    [TestMethod]
    public async Task TenantInBackoffIsSkippedWhileAnotherTenantReplays()
    {
        // Pre-close tenant A's gate through a lease taken directly from the shared registry, exactly
        // as a prior live send or replay pass would have left it.
        var registry = new Agent365TransmissionGateRegistry(utcNow: () => _now);
        using (var preLease = registry.Acquire("tenant-a"))
        {
            preLease.Gate.RecordRetryableFailure(null);
        }

        var tenantA = FakeStoredRecord.From(CreateRecord(tenantId: "tenant-a", agentId: "agent-a"));
        var tenantB = FakeStoredRecord.From(CreateRecord(tenantId: "tenant-b", agentId: "agent-b"));
        var storage = new FakeStorage(tenantA, tenantB);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            gates: registry,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(1, "tenant A's gate is already in backoff, so only tenant B attempts HTTP");
        tenantA.LeaseCalls.Should().Be(1, "tenant A's record is still leased to learn its tenant before the gate check");
        tenantA.ReadCalls.Should().Be(1, "tenant A's record is still read to learn its tenant before the gate check");
        tenantA.DeleteCalls.Should().Be(0, "tenant A's record is retained while its gate is in backoff");
        tenantB.DeleteCalls.Should().Be(1, "tenant B is unaffected by tenant A's backoff and is delivered in the same pass");
    }

    [TestMethod]
    public async Task BackedOffPrefixLongerThanMaxRecordsPerPassDoesNotStarveALaterTenant()
    {
        // A long prefix of records for a backed-off tenant must not consume the per-pass cap: each of
        // them is only leased/read (to learn its tenant) then skipped without an HTTP attempt, so the
        // pass must still reach and send a later, different tenant's record in the same pass even
        // though the backed-off prefix alone is as long as (or longer than) maxRecordsPerPass.
        const int maxRecordsPerPass = 10;
        var registry = new Agent365TransmissionGateRegistry(utcNow: () => _now);
        using (var preLease = registry.Acquire("tenant-a"))
        {
            preLease.Gate.RecordRetryableFailure(null);
        }

        var backedOffRecords = Enumerable.Range(0, maxRecordsPerPass)
            .Select(_ => FakeStoredRecord.From(CreateRecord(tenantId: "tenant-a", agentId: "agent-a")))
            .ToArray();
        var tenantB = FakeStoredRecord.From(CreateRecord(tenantId: "tenant-b", agentId: "agent-b"));
        var storage = new FakeStorage(backedOffRecords.Concat(new[] { tenantB }).ToArray());
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            gates: registry,
            maxRecordsPerPass: maxRecordsPerPass,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(1, "only tenant B's record ever gets a real HTTP attempt in this pass");
        backedOffRecords.Should().OnlyContain(
            r => r.LeaseCalls == 1 && r.ReadCalls == 1 && r.DeleteCalls == 0,
            "each backed-off record is still leased/read but retained without an HTTP attempt");
        tenantB.DeleteCalls.Should().Be(1, "tenant B is delivered and deleted in the same pass despite the long backed-off prefix");
        storage.PendingCount.Should().Be(0, "the backed-off prefix must not consume the pass cap before tenant B is reached");
    }

    [TestMethod]
    public async Task AllBackedOffRecordsStopAtTheScanBudget()
    {
        const int maxRecordsPerPass = 10;
        var scanBudget = Agent365ReplayCoordinator.CalculateDefaultScanBudget(maxRecordsPerPass);
        var registry = new Agent365TransmissionGateRegistry(utcNow: () => _now);
        using (var preLease = registry.Acquire("tenant-a"))
        {
            preLease.Gate.RecordRetryableFailure(null);
        }

        var backedOffRecords = Enumerable.Range(0, scanBudget + 1)
            .Select(i => FakeStoredRecord.From(CreateRecord(tenantId: "tenant-a", agentId: $"agent-a-{i}")))
            .ToArray();
        var storage = new FakeStorage(backedOffRecords);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            gates: registry,
            maxRecordsPerPass: maxRecordsPerPass,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "every record belongs to a tenant already in backoff, so no HTTP attempt occurs");
        backedOffRecords.Take(scanBudget).Should().OnlyContain(
            r => r.LeaseCalls == 1 && r.ReadCalls == 1 && r.DeleteCalls == 0,
            "the pass scans only up to the scan budget before stopping");
        backedOffRecords.Skip(scanBudget).Should().OnlyContain(
            r => r.LeaseCalls == 0 && r.ReadCalls == 0 && r.DeleteCalls == 0,
            "records beyond the scan budget are left untouched for a later pass");
        storage.PendingCount.Should().Be(1, "one backed-off record remains queued because the pass stopped at its scan budget");
    }

    [TestMethod]
    public void DefaultScanBudgetUsesAtLeastTenTimesTheAttemptCapAndSaturatesSafely()
    {
        Agent365ReplayCoordinator.CalculateDefaultScanBudget(10).Should().BeGreaterOrEqualTo(100);
        Agent365ReplayCoordinator.CalculateDefaultScanBudget(int.MaxValue).Should().Be(int.MaxValue);
    }

    [TestMethod]
    public async Task RetryableFailureBacksOffTheSharedGate()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = new Agent365TransmissionGate(() => _now);
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.ConsecutiveErrors.Should().Be(1);
        gate.TryAcquire(out _).Should().BeFalse("the gate is now in backoff");
        stored.DeleteCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task RetryAfterHeaderIsHonoredByGateDuringReplay()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = new Agent365TransmissionGate(() => _now);
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
                return Task.FromResult(response);
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.CurrentDelay.Should().BeCloseTo(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(1));
    }

    // ------------------------------------------------------------------ permanent / poison

    [TestMethod]
    public async Task PermanentFailureDeletesRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        stored.DeleteCalls.Should().Be(1, "a permanent failure discards the poison record");
    }

    [TestMethod]
    public async Task PermanentFailureDoesNotBackOffTheGate()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = new Agent365TransmissionGate(() => _now);
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.ConsecutiveErrors.Should().Be(0, "a permanent failure is not an availability signal");
        gate.TryAcquire(out _).Should().BeTrue("the gate stays closed");
    }

    [TestMethod]
    public async Task InvalidRecordDeletesPoisonBlobWithoutSending()
    {
        var poison = FakeStoredRecord.Corrupt();
        var storage = new FakeStorage(poison);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "a record that cannot be read is never sent");
        poison.DeleteCalls.Should().Be(1, "an unreadable poison blob is deleted");
    }

    [TestMethod]
    public async Task StorageReadFailureRetainsRecordAndStopsPass()
    {
        var unreadable = FakeStoredRecord.From(CreateRecord(agentId: "first"));
        unreadable.ReadResult = Agent365StoredRecordReadResult.ReadFailure;
        var next = FakeStoredRecord.From(CreateRecord(agentId: "second"));
        var storage = new FakeStorage(unreadable, next);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "a record that could not be read must not be sent");
        unreadable.DeleteCalls.Should().Be(0, "a transient storage read failure must retain durable telemetry");
        storage.PendingCount.Should().Be(1, "the pass stops so later records are not reordered past the unreadable record");
    }

    // ------------------------------------------------------------------ missing token

    [TestMethod]
    public async Task NullTokenRetainsRecordForLaterPass()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            tokenResolver: (_, _) => Task.FromResult<string?>(null),
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "no request is sent without a token");
        stored.DeleteCalls.Should().Be(0, "the record is retained until a token is available");
    }

    [TestMethod]
    public async Task NullTokenDoesNotBackOffTheGate()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = new Agent365TransmissionGate(() => _now);
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            tokenResolver: (_, _) => Task.FromResult<string?>(null));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.ConsecutiveErrors.Should().Be(0, "a missing token is not a transport failure");
        gate.TryAcquire(out _).Should().BeTrue("the gate stays closed for the live path");
    }

    [TestMethod]
    public async Task TokenResolverExceptionRetainsRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            tokenResolver: (_, _) => throw new InvalidOperationException("token boom"),
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0);
        stored.DeleteCalls.Should().Be(0, "a token-resolver outage retains the record for a later pass");
    }

    // ------------------------------------------------------------------ delete failure

    [TestMethod]
    public async Task DeleteFailureAfterSuccessLogsDuplicateRisk()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        stored.DeleteResult = false;
        var storage = new FakeStorage(stored);
        var logger = new ReplayTestLogger();
        var coordinator = CreateCoordinator(
            storage,
            logger: logger,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        stored.DeleteCalls.Should().Be(1, "a delete is attempted after a successful send");

        var warning = logger.Entries.Should().ContainSingle(
            e => e.Level == LogLevel.Warning && e.Message.ToLowerInvariant().Contains("duplicate"),
            "a successful send whose record cannot be deleted risks a duplicate on the next pass").Subject;

        var message = warning.Message.ToLowerInvariant();
        message.Should().Contain(
            "re-sent",
            "the warning must state the stored record may be replayed (re-sent) again on a later pass");
        message.Should().NotContain(
            "dedup",
            "the pipeline offers no deduplication, so the warning must not claim the duplicate will be prevented");
    }

    // ------------------------------------------------------------------ per-pass cap

    [TestMethod]
    public async Task OnePassHandlesAtMostTenRecords()
    {
        var records = Enumerable.Range(0, 12)
            .Select(_ => FakeStoredRecord.From(CreateRecord()))
            .ToArray();
        var storage = new FakeStorage(records);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(10, "a pass handles at most ten records");
        storage.PendingCount.Should().Be(2, "the remaining records are left for the next pass");
        records.Take(10).Should().OnlyContain(r => r.DeleteCalls == 1);
        records.Skip(10).Should().OnlyContain(r => r.DeleteCalls == 0 && r.ReadCalls == 0);
    }

    // ------------------------------------------------------------------ leases

    [TestMethod]
    public async Task LeasedBlobIsSkipped()
    {
        var leased = FakeStoredRecord.From(CreateRecord());
        leased.LeaseResult = false;
        var storage = new FakeStorage(leased);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "a blob leased by another worker is skipped");
        leased.ReadCalls.Should().Be(0, "a skipped blob is never read");
        leased.DeleteCalls.Should().Be(0, "a skipped blob is left in place");
    }

    [TestMethod]
    public async Task RecordsAreLeasedBeforeReading()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var coordinator = CreateCoordinator(storage);

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        stored.LeaseCalls.Should().Be(1);
        stored.LeasedDuration.Should().BeGreaterThan(TimeSpan.Zero, "a positive lease is taken before delivery");
    }

    // ------------------------------------------------------------------ agentic user id

    [TestMethod]
    public async Task ReplayWithAgenticUserIdUsesContextualResolver()
    {
        var stored = FakeStoredRecord.From(CreateRecord(agenticUserId: "user-77"));
        var storage = new FakeStorage(stored);
        TokenResolverContext? captured = null;
        var coordinator = CreateCoordinator(
            storage,
            contextualResolver: ctx =>
            {
                captured = ctx;
                return Task.FromResult<string?>("ctx-token");
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Identity.AgentId.Should().Be("agent-1");
        captured.Identity.AgenticUserId.Should().Be("user-77");
        captured.TenantId.Should().Be("tenant-1");
        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task ReplayWithoutAgenticUserIdPassesNullUserId()
    {
        var stored = FakeStoredRecord.From(CreateRecord(agenticUserId: null, useS2SEndpoint: true));
        var storage = new FakeStorage(stored);
        TokenResolverContext? captured = null;
        var coordinator = CreateCoordinator(
            storage,
            contextualResolver: ctx =>
            {
                captured = ctx;
                return Task.FromResult<string?>("ctx-token");
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Identity.AgentId.Should().Be("agent-1");
        captured.Identity.AgenticUserId.Should().BeNull();
        stored.DeleteCalls.Should().Be(1);
    }

    // ------------------------------------------------------------------ gate permits

    [TestMethod]
    public async Task GateInBackoffSkipsSendButStillLeasesAndReadsTheRecord()
    {
        // The tenant gate is only consulted after a record is leased and successfully deserialized
        // (its tenant must be known first), so a backed-off tenant still has its record leased/read;
        // only the HTTP attempt is skipped.
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            gate: BackoffGate(),
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "no HTTP attempt is made while the tenant's gate is in backoff");
        stored.LeaseCalls.Should().Be(1, "the record is still leased to learn its tenant before the gate check");
        stored.ReadCalls.Should().Be(1, "the record is still read to learn its tenant before the gate check");
        stored.DeleteCalls.Should().Be(0, "the record is retained while the tenant's gate is in backoff");
    }

    [TestMethod]
    public async Task OwnedProbeIsReleasedWhenNoTerminalOutcome()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = ProbeGate();
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        // A permanent failure records no terminal gate outcome, so the half-open probe must be
        // returned: a subsequent acquire still owns the single probe (it was not leaked).
        gate.TryAcquire(out var ownsProbe).Should().BeTrue();
        ownsProbe.Should().BeTrue("the owned probe was released, not leaked");
    }

    [TestMethod]
    public async Task SecondSameTenantRecordReacquiresProbeReleasedByFirstNonTerminalRecord()
    {
        // Regression coverage: within a single pass, two same-tenant records share one half-open
        // probe. The first record's non-terminal outcome (permanent failure) must release the probe
        // it owned so the second, later record for the SAME tenant can immediately reacquire it and
        // still get its own real HTTP attempt in this same pass (not silently skipped as backed off).
        var first = FakeStoredRecord.From(CreateRecord(agentId: "agent-1"));
        var second = FakeStoredRecord.From(CreateRecord(agentId: "agent-2"));
        var storage = new FakeStorage(first, second);
        var gate = ProbeGate();
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(
                    sends == 1 ? HttpStatusCode.Forbidden : HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(2, "the second same-tenant record must reacquire the probe the first record released");
        first.DeleteCalls.Should().Be(1, "the first record's permanent failure deletes it");
        second.DeleteCalls.Should().Be(1, "the second record reacquires the probe and is delivered");
    }

    [TestMethod]
    public async Task SuccessfulProbeClosesGate()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = ProbeGate();
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.ConsecutiveErrors.Should().Be(0);
        gate.TryAcquire(out var ownsProbe).Should().BeTrue();
        ownsProbe.Should().BeFalse("a successful probe closes the gate");
        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task EmptyStorageDoesNothing()
    {
        var storage = new FakeStorage();
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0);
    }

    // ------------------------------------------------------------------ lifecycle

    [TestMethod]
    public async Task StartRunsReplayPassesUntilStopped()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var delay = new ControlledDelay();
        var coordinator = CreateCoordinator(
            storage,
            delayAsync: delay.WaitAsync,
            replayInterval: TimeSpan.FromMinutes(2),
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        coordinator.Start();

        // The loop awaits the (controlled) delay before its first pass; release it once.
        await delay.WaitForCallAsync(1);
        delay.ReleaseOnce();

        // After the first pass the record is delivered and deleted, and the loop awaits again.
        await WaitUntilAsync(() => stored.DeleteCalls == 1);
        await delay.WaitForCallAsync(2);

        await coordinator.StopAsync(CancellationToken.None);

        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task StopAsyncWithoutStartCompletes()
    {
        var coordinator = CreateCoordinator(new FakeStorage());

        Func<Task> stop = () => coordinator.StopAsync(CancellationToken.None);

        await stop.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task StartIsIdempotent()
    {
        var storage = new FakeStorage();
        var delay = new ControlledDelay();
        var coordinator = CreateCoordinator(storage, delayAsync: delay.WaitAsync);

        coordinator.Start();
        coordinator.Start();

        await delay.WaitForCallAsync(1);
        delay.Calls.Should().Be(1, "a single background loop runs even after repeated Start calls");

        await coordinator.StopAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------------ per-record / global fault isolation

    [TestMethod]
    public async Task ReplayThrowingRecordIsRetainedAndStopsThePass()
    {
        // A non-cancellation exception thrown out of replay is an unknown fault: it is far more likely a
        // global misconfiguration or a transient dependency outage than bad data in this one record, and
        // the record already deserialized cleanly. Deleting it (and continuing) could discard up to
        // maxRecordsPerPass durable records on a single global fault. The pass must instead retain the
        // current record and stop, touching no further records this pass.
        var throwing = FakeStoredRecord.From(CreateRecord(agentId: "first"));
        var next = FakeStoredRecord.From(CreateRecord(agentId: "second"));
        var storage = new FakeStorage(throwing, next);
        var gate = new Agent365TransmissionGate(() => _now);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) =>
            {
                sends++;
                throw new InvalidOperationException("replay boom");
            });

        Func<Task> act = () => coordinator.ReplayOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("one record's replay exception must not tear down the whole pass");
        sends.Should().Be(1, "the pass stops after the first throwing record; no further records are sent");
        throwing.DeleteCalls.Should().Be(0, "an unknown replay fault retains the record; durable telemetry is not deleted");
        next.DeleteCalls.Should().Be(0, "the pass stopped, so the following record is left untouched");
        storage.PendingCount.Should().Be(1, "the second record is never served because the pass stopped");
        gate.ConsecutiveErrors.Should().Be(0, "an unknown replay fault is not a transport availability signal");
    }

    [TestMethod]
    public async Task DomainResolverFailureRetainsRecordAndStopsWithoutDeleting()
    {
        // A throwing DomainResolver is a global misconfiguration that would affect every record. It must
        // never be misclassified as poison and delete durable telemetry.
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = new Agent365TransmissionGate(() => _now);
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            domainResolver: _ => throw new InvalidOperationException("resolver misconfigured"));

        Func<Task> act = () => coordinator.ReplayOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("a global resolver misconfiguration must not tear down the pass");
        stored.DeleteCalls.Should().Be(0, "a global domain-resolver failure must not delete durable telemetry");
        gate.ConsecutiveErrors.Should().Be(0, "a misconfiguration is not a transport availability signal");
    }

    [TestMethod]
    public async Task GlobalPlaintextEndpointRetainsRecordAndStopsWithoutDeleting()
    {
        // A globally-resolved plaintext (http://) endpoint makes request construction throw for every
        // record. It is a global misconfiguration, not per-record poison, so telemetry must be retained.
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var coordinator = CreateCoordinator(
            storage,
            domainResolver: _ => "http://api.example.com");

        Func<Task> act = () => coordinator.ReplayOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("a global plaintext-endpoint misconfiguration must not tear down the pass");
        stored.DeleteCalls.Should().Be(0, "a global plaintext-endpoint misconfiguration must not delete durable telemetry");
    }

    [TestMethod]
    public async Task OwnedProbeIsReleasedWhenReplayThrows()
    {
        // The unknown-fault path retains the record and returns early. Like the other non-terminal exits,
        // it must return the single half-open probe it owns rather than leaking it.
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = ProbeGate();
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) => throw new InvalidOperationException("replay boom"));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        stored.DeleteCalls.Should().Be(0, "an unknown replay fault retains the record");
        gate.TryAcquire(out var ownsProbe).Should().BeTrue();
        ownsProbe.Should().BeTrue("the owned probe was released on the retain-and-stop path, not leaked");
    }

    [TestMethod]
    public async Task ThrownCancellationDuringReplayRetainsRecordAndPropagates()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        using var cts = new CancellationTokenSource();
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, token) =>
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        Func<Task> act = () => coordinator.ReplayOnceAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "cooperative cancellation is preserved, not swallowed and treated as poison");
        stored.DeleteCalls.Should().Be(0, "a cancelled replay retains the record rather than deleting it");
    }

    // ------------------------------------------------------------------ lease-failure spin guard

    [TestMethod]
    public async Task LeaseFailureDoesNotSpinWhenStorageReservesSameBlob()
    {
        // The real FileBlobProvider.TryGetNext is non-destructive: it re-serves the same first
        // unleased blob on every call. A failed lease must stop the pass, not loop up to
        // maxRecordsPerPass times re-fetching and re-leasing the identical blob.
        var leased = FakeStoredRecord.From(CreateRecord());
        leased.LeaseResult = false;
        var storage = new ReServingStorage(leased);
        var coordinator = CreateCoordinator(storage, maxRecordsPerPass: 10);

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        leased.LeaseCalls.Should().Be(1, "a failed lease stops the pass instead of re-leasing the re-served blob");
        storage.GetNextCalls.Should().Be(1, "the pass does not repeatedly re-fetch the same unleased blob");
    }

    // ------------------------------------------------------------------ interval validation

    [TestMethod]
    public void NonPositiveReplayIntervalThrows()
    {
        Action zero = () => CreateCoordinator(new FakeStorage(), replayInterval: TimeSpan.Zero);
        Action negative = () => CreateCoordinator(new FakeStorage(), replayInterval: TimeSpan.FromSeconds(-1));

        zero.Should().Throw<ArgumentOutOfRangeException>("a zero interval would spin the loop with no delay");
        negative.Should().Throw<ArgumentOutOfRangeException>("a negative interval is invalid");
    }

    // ------------------------------------------------------------------ non-shutdown cancellation

    [TestMethod]
    public async Task NonShutdownCancellationDoesNotKillTheLoop()
    {
        var storage = new FakeStorage();
        var calls = 0;
        var secondReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<TimeSpan, CancellationToken, Task> delay = (interval, token) =>
        {
            var n = Interlocked.Increment(ref calls);
            if (n == 1)
            {
                // A cancellation that is NOT the coordinator's shutdown must not tear down the loop.
                throw new OperationCanceledException();
            }

            if (n == 2)
            {
                secondReached.TrySetResult(true);
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        };
        var coordinator = CreateCoordinator(storage, delayAsync: delay);

        coordinator.Start();
        await Task.WhenAny(secondReached.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        await coordinator.StopAsync(CancellationToken.None);

        secondReached.Task.IsCompletedSuccessfully.Should().BeTrue(
            "a non-shutdown cancellation must be logged and the loop must keep running for later passes");
    }

    // ------------------------------------------------------------------ start/stop lifecycle race

    [TestMethod]
    public async Task StopAsyncAwaitsTheLoopLaunchedByStart()
    {
        var storage = new FakeStorage();
        var delay = new ControlledDelay();
        var coordinator = CreateCoordinator(storage, delayAsync: delay.WaitAsync);

        coordinator.Start();
        await delay.WaitForCallAsync(1);

        Func<Task> stop = () => coordinator.StopAsync(CancellationToken.None);
        await stop.Should().CompleteWithinAsync(
            TimeSpan.FromSeconds(5),
            "StopAsync must observe and await the run task launched by Start, then return once it unwinds");

        var callsAtStop = delay.Calls;
        await Task.Delay(50);
        delay.Calls.Should().Be(callsAtStop, "the loop has fully stopped after StopAsync returns");
    }

    // ------------------------------------------------------------------ coordinator token reaches the send

    [TestMethod]
    public async Task InFlightSendReceivesCoordinatorToken()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        using var cts = new CancellationTokenSource();
        CancellationToken received = default;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, token) =>
            {
                received = token;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(cts.Token);

        received.Should().Be(cts.Token, "the coordinator's cancellation token flows to the actual HTTP send");
    }

    [TestMethod]
    public async Task MidFlightShutdownCancelsInFlightSendAndRetainsRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        using var cts = new CancellationTokenSource();
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: async (request, token) =>
            {
                // Shutdown arrives while the request is in flight; the send observes the token.
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        Func<Task> act = () => coordinator.ReplayOnceAsync(cts.Token);

        await act.Should().NotThrowAsync("a mid-flight cancellation is classified as a Canceled outcome, not thrown");
        stored.DeleteCalls.Should().Be(0, "a send cancelled mid-flight by shutdown retains the record for a later pass");
    }

    // ------------------------------------------------------------------ S2S endpoint

    [TestMethod]
    public async Task ReplayWithS2SEndpointBuildsS2SUri()
    {
        var stored = FakeStoredRecord.From(CreateRecord(tenantId: "tenant-9", agentId: "agent-9", useS2SEndpoint: true));
        var storage = new FakeStorage(stored);
        Uri? sentUri = null;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, _) =>
            {
                sentUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sentUri.Should().NotBeNull();
        sentUri!.ToString().Should().Be(
            "https://api.example.com/observabilityService/tenants/tenant-9/otlp/agents/agent-9/traces?api-version=1");
        stored.DeleteCalls.Should().Be(1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// Minimal non-generic <see cref="ILogger"/> that records every entry, used to assert the
/// coordinator's duplicate-risk warning.
/// </summary>
internal sealed class ReplayTestLogger : ILogger
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception), exception));
    }
}

/// <summary>
/// A deterministic delay double for the background replay loop. Each <see cref="WaitAsync"/> call
/// awaits a fresh completion source that the test releases with <see cref="ReleaseOnce"/>, so passes
/// are stepped explicitly rather than waiting on wall-clock time. Cancellation completes the pending
/// wait so the loop can exit on <c>StopAsync</c>.
/// </summary>
internal sealed class ControlledDelay
{
    private readonly object _lock = new();
    private TaskCompletionSource<bool> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public int Calls { get { lock (_lock) { return _calls; } } }

    public Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            _calls++;
            tcs = _next;
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled());
        }

        return tcs.Task;
    }

    public void ReleaseOnce()
    {
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            tcs = _next;
            _next = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        tcs.TrySetResult(true);
    }

    public async Task WaitForCallAsync(int callCount, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Calls < callCount)
        {
            if (Environment.TickCount > deadline)
                throw new TimeoutException($"Delay was not awaited {callCount} time(s) within the timeout.");
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// Non-destructive storage double that reproduces the real <c>FileBlobProvider</c> contract: every
/// <see cref="TryGetNext"/> re-serves the same unleased record (it is never dequeued). Used to prove
/// the coordinator does not spin over a re-served blob whose lease keeps failing.
/// </summary>
internal sealed class ReServingStorage : IAgent365PersistentStorage
{
    private readonly IAgent365StoredRecord _record;

    public ReServingStorage(IAgent365StoredRecord record) => _record = record;

    public int GetNextCalls { get; private set; }

    public bool TryStore(Agent365DurableRecord record) => true;

    public bool TryGetNext([NotNullWhen(true)] out IAgent365StoredRecord? record)
    {
        GetNextCalls++;
        record = _record;
        return true;
    }

    public void Dispose()
    {
    }
}
