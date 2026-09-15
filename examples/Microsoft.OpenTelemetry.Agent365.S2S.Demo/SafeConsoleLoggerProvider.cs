// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

/// <summary>
/// An <see cref="ILoggerProvider"/> that writes formatted log lines to an injected
/// <see cref="TextWriter"/> while intentionally discarding the <see cref="Exception"/>
/// passed to any log call. Unlike <c>AddSimpleConsole</c>, this provider never renders an
/// exception's message, type, stack trace, or inner-exception chain, so it is safe to use
/// with calls such as <c>Agent365ExporterCore.LogError(ex, ...)</c> whose exceptions may
/// wrap sensitive authentication failure details.
/// </summary>
internal sealed class SafeConsoleLoggerProvider : ILoggerProvider
{
    private readonly TextWriter writer;
    private readonly LogLevel minimumLevel;
    private readonly object syncRoot = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeConsoleLoggerProvider"/> class.
    /// </summary>
    /// <param name="writer">
    /// The writer to receive log lines. The provider does not own this writer and will
    /// not dispose or close it, so callers may safely pass <see cref="Console.Out"/> or
    /// <see cref="Console.Error"/> without those streams being closed on disposal.
    /// </param>
    /// <param name="minimumLevel">The minimum level a log call must meet to be written.</param>
    internal SafeConsoleLoggerProvider(TextWriter writer, LogLevel minimumLevel = LogLevel.Information)
    {
        ArgumentNullException.ThrowIfNull(writer);

        this.writer = writer;
        this.minimumLevel = minimumLevel;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) =>
        new SafeConsoleLogger(categoryName, this.writer, this.syncRoot, this.minimumLevel);

    /// <inheritdoc/>
    public void Dispose()
    {
        // Intentionally does not dispose `writer`: this provider never owns
        // Console.Out/Console.Error (or any other externally supplied writer) and must
        // not close a stream that other components may still be using.
    }

    private sealed class SafeConsoleLogger(
        string categoryName,
        TextWriter writer,
        object syncRoot,
        LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!this.IsEnabled(logLevel))
            {
                return;
            }

            // Format with a null exception argument so no exception message, type, stack
            // trace, or inner-exception chain is ever rendered, even though the caller may
            // have supplied a real exception above.
            var message = formatter(state, null);
            var line = $"{DateTimeOffset.Now:HH:mm:ss} [{LevelLabel(logLevel)}] {categoryName}: {message}";

            lock (syncRoot)
            {
                writer.WriteLine(line);
            }
        }

        private static string LevelLabel(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => logLevel.ToString(),
        };
    }
}
