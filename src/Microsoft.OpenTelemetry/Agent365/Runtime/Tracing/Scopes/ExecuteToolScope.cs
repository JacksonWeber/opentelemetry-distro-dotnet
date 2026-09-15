// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

using Microsoft.Agents.A365.Observability.Runtime.Tracing;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes
{
    /// <summary>
    /// Provides OpenTelemetry tracing scope for AI tool execution operations.
    /// </summary>
    /// <remarks>
    /// <see href="https://learn.microsoft.com/microsoft-agent-365/developer/observability?tabs=dotnet#tool-execution">Learn more about tool execution</see>
    /// </remarks>
    public sealed class ExecuteToolScope : OpenTelemetryScope
    {
        /// <summary>
        /// Creates and starts a new scope for tool execution tracing.
        /// </summary>
        /// <param name="request">Request details for the tool execution.</param>
        /// <param name="details">Details of the tool call (name, args, type, call ID, description, endpoint).</param>
        /// <param name="agentDetails">Information about the agent executing the tool (service, version, identifiers).</param>
        /// <param name="userDetails">Optional human user details.</param>
        /// <param name="spanDetails">Optional span configuration (parent context, timing, kind, span links).</param>
        /// <param name="threatDiagnosticsSummary">Optional threat diagnostics summary containing security-related information about blocked actions.</param>
        /// <returns>A new ExecuteToolScope instance.</returns>
        /// <remarks>
        /// <para>
        /// <b>Certification Requirements:</b> The following parameters must be set for the agent to pass certification requirements:
        /// <list type="bullet">
        ///   <item><paramref name="details"/></item>
        ///   <item><paramref name="agentDetails"/></item>
        /// </list>
        /// </para>
        /// <para>
        /// <see href="https://go.microsoft.com/fwlink/?linkid=2344479">Learn more about certification requirements</see>
        /// </para>
        /// </remarks>
        public static ExecuteToolScope Start(Request request, ToolCallDetails details, AgentDetails agentDetails, UserDetails? userDetails = null, SpanDetails? spanDetails = null, ThreatDiagnosticsSummary? threatDiagnosticsSummary = null) => new ExecuteToolScope(request, details, agentDetails, userDetails, spanDetails, threatDiagnosticsSummary);

        private ExecuteToolScope(Request request, ToolCallDetails details, AgentDetails agentDetails, UserDetails? userDetails, SpanDetails? spanDetails, ThreatDiagnosticsSummary? threatDiagnosticsSummary)
            : base(
                operationName: OpenTelemetryConstants.ExecuteToolOperationName,
                activityName: $"{OpenTelemetryConstants.ExecuteToolOperationName} {details.ToolName}",
                agentDetails: agentDetails,
                spanDetails: new SpanDetails(spanDetails?.SpanKind ?? ActivityKind.Internal, spanDetails?.ParentContext, spanDetails?.StartTime, spanDetails?.EndTime, spanDetails?.SpanLinks),
                userDetails: userDetails)
        {
            var (toolName, arguments, toolCallId, description, toolType, endpoint) = details;
            SetTagMaybe(OpenTelemetryConstants.GenAiToolNameKey, toolName);

            // Per OTEL spec: arguments SHOULD be recorded in structured form.
            // Prefer typed arguments, then legacy dictionary arguments, then legacy string payloads.
            if (details.ToolCallArguments != null)
            {
                SetTagMaybe(
                    OpenTelemetryConstants.GenAiToolArgumentsKey,
                    MessageUtils.SerializeToolPayload(details.ToolCallArguments));
            }
            else if (details.ArgumentsObject != null)
            {
                SetTagMaybe(
                    OpenTelemetryConstants.GenAiToolArgumentsKey,
                    MessageUtils.SerializeToolPayload(details.ArgumentsObject));
            }
            else if (arguments != null)
            {
                if (MessageUtils.IsJson(arguments))
                {
                    SetTagMaybe(OpenTelemetryConstants.GenAiToolArgumentsKey, arguments);
                }
                else
                {
                    SetTagMaybe(OpenTelemetryConstants.GenAiToolArgumentsKey,
                        MessageUtils.Serialize(new Dictionary<string, object> { { "arguments", arguments } }));
                }
            }

            SetTagMaybe(OpenTelemetryConstants.GenAiToolTypeKey, toolType);
            SetTagMaybe(OpenTelemetryConstants.GenAiToolCallIdKey, toolCallId);
            SetTagMaybe(OpenTelemetryConstants.GenAiToolDescriptionKey, description);

            var transferDetails = details.TransferDetails;
            if (transferDetails != null)
            {
                SetTagMaybe(OpenTelemetryConstants.TransferModeKey, transferDetails.ModeValue);
                SetTagMaybe(OpenTelemetryConstants.TransferTargetTypeKey, transferDetails.TargetTypeValue);

                var targetAgentDetails = transferDetails.TargetAgentDetails;
                if (targetAgentDetails != null)
                {
                    SetTagMaybe(OpenTelemetryConstants.TransferTargetAgentIdKey, targetAgentDetails.AgentId);
                    SetTagMaybe(OpenTelemetryConstants.TransferTargetAgentNameKey, targetAgentDetails.AgentName);
                    SetTagMaybe(OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey, targetAgentDetails.AgentBlueprintId);
                    SetTagMaybe(OpenTelemetryConstants.TransferTargetAgentPlatformIdKey, targetAgentDetails.AgentPlatformId);
                    SetTagMaybe(OpenTelemetryConstants.TransferTargetAgentVersionKey, targetAgentDetails.AgentVersion);
                }
            }

            SetTagMaybe(OpenTelemetryConstants.ThreatDiagnosticsSummaryKey, threatDiagnosticsSummary?.ToJson());
            SetTagMaybe(OpenTelemetryConstants.GenAiConversationIdKey, request?.ConversationId);
            SetTagMaybe(OpenTelemetryConstants.SessionIdKey, request?.SessionId);

            if (endpoint != null)
            {
                SetTagMaybe(OpenTelemetryConstants.ServerAddressKey, endpoint.Host);
                if (endpoint.Port != 443)
                {
                    SetTagMaybe(OpenTelemetryConstants.ServerPortKey, endpoint.Port.ToString());
                }
            }

            if (request?.Channel != null)
            {
                SetTagMaybe(OpenTelemetryConstants.ChannelNameKey, request.Channel.Name);
                SetTagMaybe(OpenTelemetryConstants.ChannelLinkKey, request.Channel.Link);
            }
        }

        /// <summary>
        /// Records response information for telemetry tracking.
        /// Per OTEL spec, the result SHOULD be recorded in structured form.
        /// If the string is already valid JSON, it is recorded as-is.
        /// Otherwise, it is wrapped as <c>{"result":"..."}</c>.
        /// </summary>
        public void RecordResponse(string response)
        {
            if (MessageUtils.IsJson(response))
            {
                SetTagMaybe(OpenTelemetryConstants.GenAiToolCallResultKey, response);
            }
            else
            {
                SetTagMaybe(OpenTelemetryConstants.GenAiToolCallResultKey,
                    MessageUtils.Serialize(new Dictionary<string, object> { { "result", response ?? string.Empty } }));
            }
        }

        /// <summary>
        /// Records a structured tool call result for telemetry tracking.
        /// Per OTEL spec, the result SHOULD be recorded in structured form.
        /// The dictionary is serialized to JSON.
        /// </summary>
        /// <param name="result">Tool call result as a structured dictionary.</param>
        public void RecordResponse(IDictionary<string, object> result)
        {
            SetTagMaybe(
                OpenTelemetryConstants.GenAiToolCallResultKey,
                MessageUtils.SerializeToolPayload(result));
        }

        /// <summary>
        /// Records a typed tool call result for telemetry tracking.
        /// Per OTEL spec, the result SHOULD be recorded in structured form.
        /// </summary>
        /// <param name="result">Optional tool call result as a typed schema model.</param>
        public void RecordResponse(ExecuteToolCallResult? result)
        {
            SetTagMaybe(
                OpenTelemetryConstants.GenAiToolCallResultKey,
                MessageUtils.SerializeToolPayload(result));
        }

        /// <summary>
        /// Records threat diagnostics summary for telemetry tracking.
        /// </summary>
        /// <param name="threatDiagnosticsSummary">The threat diagnostics summary containing security-related information about blocked actions.</param>
        public void RecordThreatDiagnosticsSummary(ThreatDiagnosticsSummary threatDiagnosticsSummary)
        {
            SetTagMaybe(OpenTelemetryConstants.ThreatDiagnosticsSummaryKey, threatDiagnosticsSummary.ToJson());
        }
    }
}