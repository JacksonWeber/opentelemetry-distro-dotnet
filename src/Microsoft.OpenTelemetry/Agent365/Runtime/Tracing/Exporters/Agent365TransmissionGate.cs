// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Threading;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    internal sealed class Agent365TransmissionGate
    {
        internal static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(10);
        internal static readonly TimeSpan MaximumDelay = TimeSpan.FromHours(1);

        private readonly Func<DateTimeOffset> _utcNow;
        private GateSnapshot _snapshot = GateSnapshot.CreateClosed();
        private int _randomState;

        private enum GateState { Closed, Backoff, Probe }

        internal Agent365TransmissionGate(Func<DateTimeOffset>? utcNow = null, Random? random = null)
        {
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _randomState = (random ?? new Random()).Next(1, int.MaxValue);
        }

        internal int ConsecutiveErrors => Volatile.Read(ref _snapshot).ConsecutiveErrors;

        internal TimeSpan CurrentDelay => Volatile.Read(ref _snapshot).NextProbeTime - _utcNow();

        internal bool HasPendingBackoff
        {
            get
            {
                var current = Volatile.Read(ref _snapshot);
                return current.State == GateState.Backoff && _utcNow() < current.NextProbeTime;
            }
        }

        internal bool TryAcquire(out bool ownsProbe)
        {
            while (true)
            {
                var current = Volatile.Read(ref _snapshot);
                ownsProbe = false;

                if (current.State == GateState.Closed)
                {
                    return true;
                }

                if (current.State == GateState.Backoff && _utcNow() < current.NextProbeTime)
                {
                    return false;
                }

                if (current.ProbeInFlight)
                {
                    return false;
                }

                var claimed = new GateSnapshot(
                    GateState.Probe,
                    current.ConsecutiveErrors,
                    current.NextProbeTime,
                    probeInFlight: true);

                if (ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, claimed, current), current))
                {
                    ownsProbe = true;
                    return true;
                }
            }
        }

        internal void RecordSuccess()
        {
            while (true)
            {
                var current = Volatile.Read(ref _snapshot);
                var closed = GateSnapshot.CreateClosed();
                if (ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, closed, current), current))
                {
                    return;
                }
            }
        }

        internal void RecordRetryableFailure(TimeSpan? retryAfter)
        {
            while (true)
            {
                var current = Volatile.Read(ref _snapshot);
                var consecutiveErrors = current.ConsecutiveErrors == int.MaxValue
                    ? int.MaxValue
                    : current.ConsecutiveErrors + 1;

                TimeSpan delay;
                if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
                {
                    delay = retryAfter.Value > MaximumDelay ? MaximumDelay : retryAfter.Value;
                }
                else
                {
                    delay = CalculateFallback(consecutiveErrors);
                }

                var now = _utcNow();
                var maxSafe = DateTimeOffset.MaxValue - now;
                var failed = new GateSnapshot(
                    GateState.Backoff,
                    consecutiveErrors,
                    now + (delay > maxSafe ? maxSafe : delay),
                    probeInFlight: false);

                if (ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, failed, current), current))
                {
                    return;
                }
            }
        }

        internal void ReleaseProbe()
        {
            while (true)
            {
                var current = Volatile.Read(ref _snapshot);
                if (current.State != GateState.Probe || !current.ProbeInFlight)
                {
                    return;
                }

                var released = new GateSnapshot(
                    GateState.Probe,
                    current.ConsecutiveErrors,
                    current.NextProbeTime,
                    probeInFlight: false);

                if (ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, released, current), current))
                {
                    return;
                }
            }
        }

        internal static bool IsRetryable(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return statusCode == HttpStatusCode.Unauthorized
                || statusCode == HttpStatusCode.RequestTimeout
                || code == 429
                || code >= 500 && code <= 599;
        }

        private TimeSpan CalculateFallback(int consecutiveErrors)
        {
            var slot = (Math.Pow(2, consecutiveErrors) - 1) / 2;
            var upperSeconds = Math.Max(
                MinimumDelay.TotalSeconds,
                Math.Min(slot * MinimumDelay.TotalSeconds, MaximumDelay.TotalSeconds));
            var seconds = NextRandom(1, Math.Max(2, (int)Math.Ceiling(upperSeconds)));
            return TimeSpan.FromSeconds(
                Math.Max(MinimumDelay.TotalSeconds, Math.Min(seconds, MaximumDelay.TotalSeconds)));
        }

        private int NextRandom(int minValue, int maxValue)
        {
            while (true)
            {
                var current = Volatile.Read(ref _randomState);
                var next = unchecked((int)(((uint)current * 1664525U) + 1013904223U));
                if (Interlocked.CompareExchange(ref _randomState, next, current) == current)
                {
                    return minValue + (int)((uint)next % (uint)(maxValue - minValue));
                }
            }
        }

        private sealed class GateSnapshot
        {
            internal GateSnapshot(
                GateState state,
                int consecutiveErrors,
                DateTimeOffset nextProbeTime,
                bool probeInFlight)
            {
                State = state;
                ConsecutiveErrors = consecutiveErrors;
                NextProbeTime = nextProbeTime;
                ProbeInFlight = probeInFlight;
            }

            internal GateState State { get; }

            internal int ConsecutiveErrors { get; }

            internal DateTimeOffset NextProbeTime { get; }

            internal bool ProbeInFlight { get; }

            internal static GateSnapshot CreateClosed() => new GateSnapshot(
                GateState.Closed,
                consecutiveErrors: 0,
                nextProbeTime: default,
                probeInFlight: false);
        }
    }
}
