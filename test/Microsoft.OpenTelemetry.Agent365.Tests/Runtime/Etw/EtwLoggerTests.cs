// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Etw;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Agents.A365.Observability.Runtime.Tests.Etw
{
    [TestClass]
    public class EtwLoggerTests
    {
        private class TestEventListener : EventListener
        {
            public List<EventWrittenEventArgs> Events { get; } = new List<EventWrittenEventArgs>();
            protected override void OnEventWritten(EventWrittenEventArgs eventData) => Events.Add(eventData);
        }

        private sealed class LegacyCompatibleEtwLogger<T> : IA365EtwLogger<T>
        {
            public ToolCallDetails? LoggedToolCallDetails { get; private set; }

            public AgentDetails? LoggedAgentDetails { get; private set; }

            public string? LoggedConversationId { get; private set; }

            public string? LoggedResponseContent { get; private set; }

            public void LogInferenceCall(
                InferenceCallDetails inferenceCallDetails,
                AgentDetails agentDetails,
                string conversationId,
                string[]? inputMessages = null,
                string[]? outputMessages = null,
                DateTimeOffset? startTime = null,
                DateTimeOffset? endTime = null,
                string? spanId = null,
                string? parentSpanId = null,
                Channel? channel = null,
                CallerDetails? callerDetails = null,
                string? traceId = null,
                Exception? error = null)
            {
            }

            public void LogInvokeAgent(
                InvokeAgentScopeDetails invokeAgentScopeDetails,
                AgentDetails agentDetails,
                string conversationId,
                Request? request = null,
                CallerDetails? callerDetails = null,
                string[]? inputMessages = null,
                string[]? outputMessages = null,
                DateTimeOffset? startTime = null,
                DateTimeOffset? endTime = null,
                string? spanId = null,
                string? parentSpanId = null,
                string? traceId = null,
                Exception? error = null,
                ActivityKind? spanKind = null)
                {
                }

            public void LogToolCall(
                ToolCallDetails toolCallDetails,
                AgentDetails agentDetails,
                string conversationId,
                string? responseContent = null,
                DateTimeOffset? startTime = null,
                DateTimeOffset? endTime = null,
                string? spanId = null,
                string? parentSpanId = null,
                Channel? channel = null,
                CallerDetails? callerDetails = null,
                string? traceId = null,
                Exception? error = null)
            {
                LoggedToolCallDetails = toolCallDetails;
                LoggedAgentDetails = agentDetails;
                LoggedConversationId = conversationId;
                LoggedResponseContent = responseContent;
            }

            public void LogToolCall(
                ToolCallDetails toolCallDetails,
                ExecuteToolCallResult? result,
                AgentDetails agentDetails,
                string conversationId,
                DateTimeOffset? startTime = null,
                DateTimeOffset? endTime = null,
                string? spanId = null,
                string? parentSpanId = null,
                Channel? channel = null,
                CallerDetails? callerDetails = null,
                string? traceId = null,
                Exception? error = null)
            {
                LogToolCall(
                    toolCallDetails,
                    agentDetails,
                    conversationId,
                    MessageUtils.SerializeToolPayload(result),
                    startTime,
                    endTime,
                    spanId,
                    parentSpanId,
                    channel,
                    callerDetails,
                    traceId,
                    error);
            }

            public void LogOutput(
                AgentDetails agentDetails,
                Response response,
                string? conversationId = null,
                Channel? channel = null,
                CallerDetails? callerDetails = null,
                DateTimeOffset? startTime = null,
                DateTimeOffset? endTime = null,
                string? spanId = null,
                string? parentSpanId = null,
                string? traceId = null,
                Exception? error = null)
            {
            }

            public void LogApplyGuardrail(
                GuardrailDetails guardrailDetails,
                AgentDetails agentDetails,
                string conversationId,
                string parentSpanId,
                DateTimeOffset? startTime = null,
                DateTimeOffset? endTime = null,
                string? spanId = null,
                Channel? channel = null,
                CallerDetails? callerDetails = null,
                string? traceId = null,
                Exception? error = null)
            {
            }
        }

        private ServiceProvider BuildProvider() => new ServiceCollection().AddLoggingWithEtw().BuildServiceProvider();

        [TestMethod]
        public void Logs_InvokeAgent_Event()
        {
            // Arrange
            using var listener = new TestEventListener();
            listener.EnableEvents(EtwEventSource.Log, EventLevel.Informational);
            using var provider = BuildProvider();
            var etwLogger = provider.GetRequiredService<IA365EtwLogger<EtwLoggingBuilderTests>>();
            var agentDetails = new AgentDetails("agent-id", agentName: "agent-name", agentPlatformId: "platform-123");
            var invokeAgentScopeDetails = new InvokeAgentScopeDetails(endpoint: new Uri("https://example.com/agent"));
            string conversationId = "conv-123";

            // Act
            etwLogger.LogInvokeAgent(invokeAgentScopeDetails, agentDetails, conversationId, request: new Request(sessionId: "session-1"));

            // Assert
            var evt = listener.Events.FirstOrDefault(e => e.EventId == 2000);
            Assert.IsNotNull(evt);
            var payloadStr = evt!.Payload![0] as string;
            Assert.IsNotNull(payloadStr);
            var root = JsonDocument.Parse(payloadStr!).RootElement;
            Assert.AreEqual(OpenTelemetryConstants.OperationNames.InvokeAgent.ToString(), root.GetProperty("Name").GetString());
        }

        [TestMethod]
        public void Logs_InferenceCall_Event()
        {
            // Arrange
            using var listener = new TestEventListener();
            listener.EnableEvents(EtwEventSource.Log, EventLevel.Informational);
            using var provider = BuildProvider();
            var etwLogger = provider.GetRequiredService<IA365EtwLogger<EtwLoggingBuilderTests>>();
            var agentDetails = new AgentDetails("agent-id", agentName: "agent-name");
            var inferenceDetails = new InferenceCallDetails(InferenceOperationType.Chat, "model-x", "provider-y");
            string conversationId = "conv-inf-1";

            // Act
            etwLogger.LogInferenceCall(inferenceDetails, agentDetails, conversationId, inputMessages: new[] { "hello" }, outputMessages: new[] { "world" });

            // Assert
            var evt = listener.Events.FirstOrDefault(e => e.EventId == 2000);
            Assert.IsNotNull(evt);
            var payloadStr = evt!.Payload![0] as string;
            Assert.IsNotNull(payloadStr);
            var root = JsonDocument.Parse(payloadStr!).RootElement;
            Assert.AreEqual(OpenTelemetryConstants.OperationNames.ExecuteInference.ToString(), root.GetProperty("Name").GetString());
        }

        [TestMethod]
        public void Logs_ToolCall_Event()
        {
            // Arrange
            using var listener = new TestEventListener();
            listener.EnableEvents(EtwEventSource.Log, EventLevel.Informational);
            using var provider = BuildProvider();
            var etwLogger = provider.GetRequiredService<IA365EtwLogger<EtwLoggingBuilderTests>>();
            var agentDetails = new AgentDetails("agent-id", agentName: "agent-name");
            var toolDetails = new ToolCallDetails("tool-a", arguments: @"{ ""arg"": 1 }", toolCallId: "tool-call-1", description: "desc", toolType: "function");
            string conversationId = "conv-tool-1";
            string responseContent = @"{ ""value"": ""result"" }";

            // Act
            etwLogger.LogToolCall(toolDetails, agentDetails, conversationId, responseContent: responseContent);

            // Assert
            var evt = listener.Events.FirstOrDefault(e => e.EventId == 2000);
            Assert.IsNotNull(evt);
            var payloadStr = evt!.Payload![0] as string;
            Assert.IsNotNull(payloadStr);
            var root = JsonDocument.Parse(payloadStr!).RootElement;
            Assert.AreEqual(OpenTelemetryConstants.OperationNames.ExecuteTool.ToString(), root.GetProperty("Name").GetString());
        }

        [TestMethod]
        public void Interface_ExposesTypedToolResultMember()
        {
            var typedOverload = typeof(IA365EtwLogger<EtwLoggerTests>)
                .GetMethods()
                .SingleOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return method.Name == nameof(IA365EtwLogger<EtwLoggerTests>.LogToolCall) &&
                        parameters.Length > 1 &&
                        parameters[1].ParameterType == typeof(ExecuteToolCallResult);
                });

            typedOverload.Should().NotBeNull();
        }

        [TestMethod]
        public void ConcreteLogger_ExposesTypedToolResultMember()
        {
            var typedOverload = typeof(A365EtwLogger<EtwLoggerTests>)
                .GetMethods()
                .SingleOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return method.Name == nameof(A365EtwLogger<EtwLoggerTests>.LogToolCall) &&
                        parameters.Length > 1 &&
                        parameters[1].ParameterType == typeof(ExecuteToolCallResult);
                });

            typedOverload.Should().NotBeNull();
        }

        [TestMethod]
        public void InterfaceImplementation_UsesTypedResultOverload()
        {
            var legacyLogger = new LegacyCompatibleEtwLogger<EtwLoggerTests>();
            IA365EtwLogger<EtwLoggerTests> logger = legacyLogger;
            var toolDetails = new ToolCallDetails("tool-a", (string?)null);
            var agentDetails = new AgentDetails("agent-id", agentName: "agent-name");
            var result = new ExecuteToolCallResult
            {
                Outcome = new ToolCallResultOutcome
                {
                    Status = ToolCallOutcomeStatus.Success,
                },
                AdditionalProperties =
                {
                    ["provider_summary"] = "ok",
                },
            };

            logger.LogToolCall(toolDetails, result, agentDetails, "conv-tool-legacy");

            legacyLogger.LoggedToolCallDetails.Should().BeSameAs(toolDetails);
            legacyLogger.LoggedAgentDetails.Should().BeSameAs(agentDetails);
            legacyLogger.LoggedConversationId.Should().Be("conv-tool-legacy");
            JsonNode.DeepEquals(
                JsonNode.Parse(legacyLogger.LoggedResponseContent!),
                JsonNode.Parse("{\"schema_version\":\"1.0\",\"outcome\":{\"status\":\"success\"},\"provider_summary\":\"ok\"}"))
                .Should().BeTrue();
        }

        [TestMethod]
        public void Logs_ToolCall_NullTypedResult_PassesNullResponseContent()
        {
            var legacyLogger = new LegacyCompatibleEtwLogger<EtwLoggerTests>();
            IA365EtwLogger<EtwLoggerTests> etwLogger = legacyLogger;
            var agentDetails = new AgentDetails("agent-id", agentName: "agent-name");
            var toolDetails = new ToolCallDetails("tool-a", arguments: @"{ ""arg"": 1 }", toolCallId: "tool-call-1", description: "desc", toolType: "function");

            etwLogger.LogToolCall(
                toolDetails,
                (ExecuteToolCallResult)null!,
                agentDetails,
                "conv-tool-null");

            legacyLogger.LoggedResponseContent.Should().BeNull();
        }

        [TestMethod]
        public void Logs_Output_Event()
        {
            // Arrange
            using var listener = new TestEventListener();
            listener.EnableEvents(EtwEventSource.Log, EventLevel.Informational);
            using var provider = BuildProvider();
            var etwLogger = provider.GetRequiredService<IA365EtwLogger<EtwLoggingBuilderTests>>();
            var agentDetails = new AgentDetails("agent-id", agentName: "agent-name");
            var response = new Response(new[] { "Hello", "World" });
            var conversationId = "conv-output-etw";
            var sourceMetadata = new Channel(name: "EtwChannel", link: "https://channel/etw");
            var callerDetails = new CallerDetails(userDetails: new UserDetails(userId: "caller-etw-123", userName: "Etw Caller", userEmail: "etw-caller@example.com"));

            // Act
            etwLogger.LogOutput(agentDetails, response, conversationId: conversationId, channel: sourceMetadata, callerDetails: callerDetails);

            // Assert
            var evt = listener.Events.FirstOrDefault(e => e.EventId == 2000);
            Assert.IsNotNull(evt);
            var payloadStr = evt!.Payload![0] as string;
            Assert.IsNotNull(payloadStr);
            var root = JsonDocument.Parse(payloadStr!).RootElement;
            Assert.AreEqual(OpenTelemetryConstants.OperationNames.OutputMessages.ToString(), root.GetProperty("Name").GetString());
        }
    }
}
