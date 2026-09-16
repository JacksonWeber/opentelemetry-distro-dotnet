// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Tracing.Exporters;

[TestClass]
public class Agent365TransmissionGateRegistryTests
{
    private readonly List<Agent365TransmissionGate> _createdGates = new();
    private DateTimeOffset _now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private Agent365TransmissionGateRegistry CreateRegistry(
        int capacity = 4,
        TimeSpan? idleTimeout = null) =>
        new(
            capacity: capacity,
            idleTimeout: idleTimeout ?? TimeSpan.FromMinutes(5),
            utcNow: () => _now,
            gateFactory: CreateGate);

    private Agent365TransmissionGate CreateGate()
    {
        var gate = new Agent365TransmissionGate(() => _now);
        _createdGates.Add(gate);
        return gate;
    }

    private void Advance(TimeSpan span) => _now += span;

    private static Agent365TransmissionGate AcquireAndRelease(
        Agent365TransmissionGateRegistry registry,
        string tenantId)
    {
        var lease = registry.Acquire(tenantId);
        try
        {
            return lease.Gate;
        }
        finally
        {
            lease.Dispose();
        }
    }

    [TestMethod]
    public void SameTenantSharesTheExactGateInstance()
    {
        var registry = CreateRegistry();
        using var first = registry.Acquire("tenant-a");
        using var second = registry.Acquire("tenant-a");

        second.Gate.Should().BeSameAs(first.Gate);
        _createdGates.Should().ContainSingle();
    }

    [TestMethod]
    public void DifferentTenantsUseDifferentGateInstances()
    {
        var registry = CreateRegistry();
        using var first = registry.Acquire("tenant-a");
        using var second = registry.Acquire("tenant-b");

        second.Gate.Should().NotBeSameAs(first.Gate);
        _createdGates.Should().HaveCount(2);
    }

    [TestMethod]
    public void CapacityCleanupDoesNotEvictAnActivelyLeasedTenant()
    {
        var registry = CreateRegistry(capacity: 1);
        var activeLease = registry.Acquire("tenant-a");

        try
        {
            using (registry.Acquire("tenant-b"))
            {
            }

            using var reacquired = registry.Acquire("tenant-a");
            reacquired.Gate.Should().BeSameAs(activeLease.Gate);
        }
        finally
        {
            activeLease.Dispose();
        }
    }

    [TestMethod]
    public void InactiveLeastRecentlyUsedEntriesAreEvictedWhenCapacityIsExceeded()
    {
        var registry = CreateRegistry(capacity: 2, idleTimeout: TimeSpan.FromHours(1));
        var gateA = AcquireAndRelease(registry, "tenant-a");
        Advance(TimeSpan.FromMinutes(1));
        var gateB = AcquireAndRelease(registry, "tenant-b");
        Advance(TimeSpan.FromMinutes(1));
        AcquireAndRelease(registry, "tenant-c");

        using (var leaseB = registry.Acquire("tenant-b"))
        {
            leaseB.Gate.Should().BeSameAs(gateB);
        }

        using var leaseA = registry.Acquire("tenant-a");
        leaseA.Gate.Should().NotBeSameAs(gateA);
    }

    [TestMethod]
    public void IdleEntriesAreEvictedAfterTheConfiguredTimeout()
    {
        var registry = CreateRegistry(idleTimeout: TimeSpan.FromMinutes(5));
        var originalGate = AcquireAndRelease(registry, "tenant-a");

        Advance(TimeSpan.FromMinutes(6));

        using var reacquired = registry.Acquire("tenant-a");
        reacquired.Gate.Should().NotBeSameAs(originalGate);
    }

    [TestMethod]
    public void IdleCleanupPreservesGateUntilRetryBackoffExpires()
    {
        var registry = CreateRegistry(idleTimeout: TimeSpan.FromMinutes(5));
        var originalGate = AcquireAndRelease(registry, "tenant-a");
        originalGate.RecordRetryableFailure(TimeSpan.FromMinutes(30));

        Advance(TimeSpan.FromMinutes(6));
        AcquireAndRelease(registry, "tenant-b");

        using var reacquired = registry.Acquire("tenant-a");
        reacquired.Gate.Should().BeSameAs(originalGate);
        reacquired.Gate.TryAcquire(out _).Should().BeFalse(
            "idle cleanup must not discard an outstanding retry backoff");
    }

    [TestMethod]
    public void CapacityCleanupPreservesGateUntilRetryBackoffExpires()
    {
        var registry = CreateRegistry(capacity: 1, idleTimeout: TimeSpan.FromHours(1));
        var originalGate = AcquireAndRelease(registry, "tenant-a");
        originalGate.RecordRetryableFailure(TimeSpan.FromMinutes(30));

        Advance(TimeSpan.FromMinutes(1));
        AcquireAndRelease(registry, "tenant-b");

        using var reacquired = registry.Acquire("tenant-a");
        reacquired.Gate.Should().BeSameAs(originalGate);
        reacquired.Gate.TryAcquire(out _).Should().BeFalse(
            "capacity cleanup must not discard an outstanding retry backoff");
    }

    [TestMethod]
    public void RepeatedLeaseDisposalDoesNotCorruptActiveCountsOrThrow()
    {
        var registry = CreateRegistry(capacity: 1);
        var first = registry.Acquire("tenant-a");
        var second = registry.Acquire("tenant-a");

        second.Dispose();
        second.Dispose();

        using (registry.Acquire("tenant-b"))
        {
        }

        using var third = registry.Acquire("tenant-a");
        third.Gate.Should().BeSameAs(first.Gate);

        first.Dispose();
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void NullEmptyOrWhitespaceTenantIdsThrowArgumentException(string? tenantId)
    {
        var registry = CreateRegistry();

        Action act = () => registry.Acquire(tenantId!);

        act.Should().Throw<ArgumentException>();
    }
}
