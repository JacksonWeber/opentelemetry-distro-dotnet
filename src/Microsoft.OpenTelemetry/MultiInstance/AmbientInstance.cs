// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.MultiInstance;

/// <summary>
/// Holds the ambient "current telemetry instance" id for the executing async flow. This is the
/// .NET analog of JavaScript's <c>AsyncLocalStorage</c> / Python's <c>ContextVar</c>.
/// </summary>
/// <remarks>
/// The value flows with the logical call context (async/await, <c>Task.Run</c>, etc.) on the
/// application thread. It is NOT available on the exporter's background thread, which is why
/// activities are stamped with the instance id at start time (see <see cref="InstanceStampProcessor"/>)
/// rather than read here at export time.
/// </remarks>
internal static class AmbientInstance
{
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>The id of the instance bound to the current async flow, or <see langword="null"/>.</summary>
    public static string? CurrentId => Current.Value;

    /// <summary>Binds <paramref name="instanceId"/> as the current instance until the returned scope is disposed.</summary>
    public static IDisposable Use(string instanceId)
    {
        var previous = Current.Value;
        Current.Value = instanceId;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Scope(string? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = _previous;
        }
    }
}
