// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OpenTelemetry.AgentFramework;
using Microsoft.Extensions.DependencyInjection;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using Microsoft.Agents.A365.Observability.Extensions.AgentFramework;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Extension methods for configuring Microsoft Agent Framework observability on <see cref="IOpenTelemetryBuilder"/>.
/// </summary>
internal static class AgentFrameworkOpenTelemetryBuilderExtensions
{
    /// <summary>
    /// Configures the distro to capture telemetry from Microsoft Agent Framework.
    /// Listens to the default <c>Experimental.Microsoft.Agents.AI</c> activity sources
    /// that Agent Framework emits when <c>.UseOpenTelemetry()</c> or <c>.WithOpenTelemetry()</c> is called.
    /// </summary>
    /// <param name="builder">The OpenTelemetry builder.</param>
    /// <returns>The supplied <see cref="IOpenTelemetryBuilder"/> for chaining calls.</returns>
    /// <remarks>
    /// <para>
    /// Microsoft Agent Framework emits spans following the
    /// <see href="https://opentelemetry.io/docs/specs/semconv/gen-ai/">OpenTelemetry GenAI Semantic Conventions</see>.
    /// </para>
    /// <para>Usage:</para>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .UseAzureMonitor(o => o.ConnectionString = "...")
    ///     .UseAgentFramework();
    /// </code>
    /// </remarks>
    internal static IOpenTelemetryBuilder UseAgentFramework(this IOpenTelemetryBuilder builder)
    {
        return builder.UseAgentFramework(new InstrumentationOptions());
    }

    /// <summary>
    /// Configures the distro to capture telemetry from Microsoft Agent Framework
    /// with instrumentation options controlling which signals are active.
    /// </summary>
    internal static IOpenTelemetryBuilder UseAgentFramework(this IOpenTelemetryBuilder builder, InstrumentationOptions instrumentationOptions)
    {
        if (!instrumentationOptions.EnableAgentFrameworkInstrumentation)
        {
            return builder;
        }

        if (instrumentationOptions.EnableTracing)
        {
            builder.WithTracing(tracing =>
            {
                // Default Microsoft Agent Framework activity sources, plus the
                // Microsoft.Extensions.AI source the agent's underlying IChatClient emits the
                // `chat` (inference) span when instrumented with the default `.UseOpenTelemetry()`.
                tracing
                    .AddSource(AgentFrameworkConstants.DefaultSource)
                    .AddSource(AgentFrameworkConstants.AgentSource)
                    .AddSource(AgentFrameworkConstants.ChatClientSource)
                    .AddSource(AgentFrameworkConstants.MicrosoftExtensionsAISource)
                    .AddProcessor(new AgentFrameworkSpanProcessor(AgentFrameworkConstants.MicrosoftExtensionsAISource));
            });
        }

        if (instrumentationOptions.EnableMetrics)
        {
            // Also capture metrics from the same sources
            builder.WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(AgentFrameworkConstants.DefaultSource)
                    .AddMeter(AgentFrameworkConstants.AgentSource)
                    .AddMeter(AgentFrameworkConstants.ChatClientSource)
                    .AddMeter(AgentFrameworkConstants.MicrosoftExtensionsAISource);
            });
        }

        return builder;
    }
}
