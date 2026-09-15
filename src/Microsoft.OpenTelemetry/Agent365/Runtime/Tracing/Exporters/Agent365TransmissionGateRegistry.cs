// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    internal sealed class Agent365TransmissionGateRegistry
    {
        internal const int DefaultCapacity = 256;
        internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(15);

        private readonly object _sync = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<Agent365TransmissionGate> _gateFactory;
        private readonly int _capacity;
        private readonly TimeSpan _idleTimeout;

        internal Agent365TransmissionGateRegistry(
            int capacity = DefaultCapacity,
            TimeSpan? idleTimeout = null,
            Func<DateTimeOffset>? utcNow = null,
            Func<Agent365TransmissionGate>? gateFactory = null)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    "The registry capacity must be at least one.");
            }

            var configuredIdleTimeout = idleTimeout ?? DefaultIdleTimeout;
            if (configuredIdleTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(idleTimeout),
                    configuredIdleTimeout,
                    "The idle timeout must be positive.");
            }

            _capacity = capacity;
            _idleTimeout = configuredIdleTimeout;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _gateFactory = gateFactory ?? (() => new Agent365TransmissionGate(_utcNow));
        }

        internal Lease Acquire(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new ArgumentException(
                    "The tenant identifier must not be null, empty, or whitespace.",
                    nameof(tenantId));
            }

            lock (_sync)
            {
                var now = _utcNow();
                TrimInactiveEntriesNoLock(now);

                if (!_entries.TryGetValue(tenantId, out var entry))
                {
                    entry = new Entry(_gateFactory(), now);
                    _entries.Add(tenantId, entry);
                }

                entry.LastAccessUtc = now;
                entry.ActiveLeaseCount++;

                // A new active entry may temporarily overflow capacity when every tenant is busy, but if
                // any inactive entries are present this acquisition should trim them immediately.
                TrimInactiveEntriesNoLock(now);

                return new Lease(this, tenantId, entry.Gate);
            }
        }

        private void Release(string tenantId)
        {
            lock (_sync)
            {
                if (!_entries.TryGetValue(tenantId, out var entry))
                {
                    return;
                }

                if (entry.ActiveLeaseCount > 0)
                {
                    entry.ActiveLeaseCount--;
                }

                entry.LastAccessUtc = _utcNow();
                TrimInactiveEntriesNoLock(entry.LastAccessUtc);
            }
        }

        private void TrimInactiveEntriesNoLock(DateTimeOffset now)
        {
            List<string>? idleKeys = null;
            foreach (var pair in _entries)
            {
                if (IsEvictable(pair.Value)
                    && now - pair.Value.LastAccessUtc >= _idleTimeout)
                {
                    idleKeys ??= new List<string>();
                    idleKeys.Add(pair.Key);
                }
            }

            if (idleKeys != null)
            {
                foreach (var key in idleKeys)
                {
                    _entries.Remove(key);
                }
            }

            var overflow = _entries.Count - _capacity;
            if (overflow <= 0)
            {
                return;
            }

            List<KeyValuePair<string, Entry>>? inactiveEntries = null;
            foreach (var pair in _entries)
            {
                if (IsEvictable(pair.Value))
                {
                    inactiveEntries ??= new List<KeyValuePair<string, Entry>>();
                    inactiveEntries.Add(pair);
                }
            }

            if (inactiveEntries == null || inactiveEntries.Count == 0)
            {
                return;
            }

            inactiveEntries.Sort(
                (left, right) =>
                {
                    var byAccess = left.Value.LastAccessUtc.CompareTo(right.Value.LastAccessUtc);
                    return byAccess != 0
                        ? byAccess
                        : StringComparer.Ordinal.Compare(left.Key, right.Key);
                });

            for (var index = 0; index < inactiveEntries.Count && overflow > 0; index++)
            {
                if (_entries.Remove(inactiveEntries[index].Key))
                {
                    overflow--;
                }
            }
        }

        private static bool IsEvictable(Entry entry) =>
            entry.ActiveLeaseCount == 0 && !entry.Gate.HasPendingBackoff;

        internal sealed class Lease : IDisposable
        {
            private readonly Agent365TransmissionGateRegistry _registry;
            private readonly string _tenantId;
            private int _disposed;

            internal Lease(
                Agent365TransmissionGateRegistry registry,
                string tenantId,
                Agent365TransmissionGate gate)
            {
                _registry = registry ?? throw new ArgumentNullException(nameof(registry));
                _tenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
                Gate = gate ?? throw new ArgumentNullException(nameof(gate));
            }

            internal Agent365TransmissionGate Gate { get; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                _registry.Release(_tenantId);
            }
        }

        private sealed class Entry
        {
            internal Entry(Agent365TransmissionGate gate, DateTimeOffset lastAccessUtc)
            {
                Gate = gate ?? throw new ArgumentNullException(nameof(gate));
                LastAccessUtc = lastAccessUtc;
            }

            internal Agent365TransmissionGate Gate { get; }

            internal DateTimeOffset LastAccessUtc { get; set; }

            internal int ActiveLeaseCount { get; set; }
        }
    }
}
