// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.AgentFramework;

/// <summary>
/// Constants for Microsoft Agent Framework activity source names.
/// </summary>
internal static class AgentFrameworkConstants
{
    /// <summary>
    /// Default activity source name emitted by Microsoft Agent Framework.
    /// This is used when no custom sourceName is specified in <c>.UseOpenTelemetry()</c>.
    /// </summary>
    internal const string DefaultSource = "Experimental.Microsoft.Agents.AI";

    /// <summary>
    /// Activity source for agent-level operations.
    /// </summary>
    internal const string AgentSource = "Experimental.Microsoft.Agents.AI.Agent";

    /// <summary>
    /// Activity source for chat client operations.
    /// </summary>
    internal const string ChatClientSource = "Experimental.Microsoft.Agents.AI.ChatClient";

    /// <summary>
    /// Default activity source/meter name emitted by <c>Microsoft.Extensions.AI</c> when
    /// <c>.UseOpenTelemetry()</c> is called on an <c>IChatClient</c>
    /// without a custom <c>sourceName</c>. Agent Framework delegates LLM calls to the underlying
    /// chat client, so the <c>chat</c> (inference) span is emitted on this source.
    /// </summary>
    internal const string MicrosoftExtensionsAISource = "Experimental.Microsoft.Extensions.AI";
}
