// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Reflection;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.DTOs;
using Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using System.Text.Json;

namespace Microsoft.Agents.A365.Observability.Runtime.Tests.DTOs.Builders
{
    [TestClass]
    public class ExecuteToolDataBuilderTests
    {
        [TestMethod]
        public void Build_WithMinimalParameters_SetsBasicAttributes()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolA", "{a:1}");
            var agent = new AgentDetails("agent-1", "AgentOne", tenantId: Guid.NewGuid().ToString());
            var conversationId = "conv-min";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId);

            // Assert
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.GenAiToolNameKey).WhoseValue.Should().Be("toolA");
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.GenAiToolArgumentsKey);
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.GenAiAgentIdKey).WhoseValue.Should().Be("agent-1");
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.TenantIdKey);
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.GenAiConversationIdKey).WhoseValue.Should().Be(conversationId);
        }

        [TestMethod]
        public void Build_WithChannel_IncludesChannelAttributes()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolSource", (string?)null);
            var agent = new AgentDetails("agent-src");
            var conversationId = "conv-src-tool";
            var source = new Channel(name: "ChannelTool", link: "https://channel/tool");

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId, channel: source);

            // Assert
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.ChannelNameKey).WhoseValue.Should().Be("ChannelTool");
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.ChannelLinkKey).WhoseValue.Should().Be("https://channel/tool");
        }

        [TestMethod]
        public void Build_WithFullToolDetails_IncludesAllToolAttributes()
        {
            // Arrange
            var endpoint = new Uri("https://example.com:7071");
            var toolDetails = new ToolCallDetails("toolB", "{b:2}", "call-123", "Test tool", "function", endpoint);
            var agent = new AgentDetails("agent-2", "AgentTwo", "Desc", agenticUserId: "auid", agenticUserEmail: "upn@example.com", agentBlueprintId: "bp-1");
            var conversationId = "conv-full";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId);

            // Assert
            var attrs = data.Attributes;
            attrs.Should().ContainKey(OpenTelemetryConstants.GenAiToolCallIdKey).WhoseValue.Should().Be("call-123");
            attrs.Should().ContainKey(OpenTelemetryConstants.GenAiToolDescriptionKey).WhoseValue.Should().Be("Test tool");
            attrs.Should().ContainKey(OpenTelemetryConstants.GenAiToolTypeKey).WhoseValue.Should().Be("function");
            attrs.Should().ContainKey(OpenTelemetryConstants.ServerAddressKey).WhoseValue.Should().Be("example.com");
            attrs.Should().ContainKey(OpenTelemetryConstants.ServerPortKey).WhoseValue.Should().Be("7071");
            attrs.Should().ContainKey(OpenTelemetryConstants.AgentAUIDKey).WhoseValue.Should().Be("auid");
            attrs.Should().ContainKey(OpenTelemetryConstants.AgentEmailKey).WhoseValue.Should().Be("upn@example.com");
            attrs.Should().ContainKey(OpenTelemetryConstants.AgentBlueprintIdKey).WhoseValue.Should().Be("bp-1");
            attrs.Should().ContainKey(OpenTelemetryConstants.GenAiConversationIdKey).WhoseValue.Should().Be(conversationId);
        }

        [TestMethod]
        public void Build_WithNonStandardPort_IncludesPort()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolC", (string?)null, endpoint: new Uri("https://example.com:8081"));
            var agent = new AgentDetails("agent-3");
            var conversationId = "conv-port";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId);

            // Assert
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.ServerPortKey).WhoseValue.Should().Be("8081");
        }

        [TestMethod]
        public void Build_WithStandardPort443_ExcludesPort()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolD", (string?)null, endpoint: new Uri("https://example.com:443"));
            var agent = new AgentDetails("agent-4");
            var conversationId = "conv-443";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId);

            // Assert
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.ServerAddressKey);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.ServerPortKey);
        }

        [TestMethod]
        public void Build_WithResponseContent_IncludesEventContent()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolE", (string?)null);
            var agent = new AgentDetails("agent-5");
            var conversationId = "conv-content";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId, responseContent: "result-value");

            // Assert
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.GenAiToolCallResultKey).WhoseValue!.ToString()!.Should().Contain("result-value");
        }

        [TestMethod]
        public void Build_WithConversationId_IncludesConversationId()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolF", (string?)null);
            var agent = new AgentDetails("agent-6");
            var conversationId = "conv-123";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId);

            // Assert
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.GenAiConversationIdKey).WhoseValue.Should().Be("conv-123");
        }

        [TestMethod]
        public void Build_WithNullOptionalParameters_OmitsThoseAttributes()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolG", (string?)null); // no optional fields, no endpoint
            var agent = new AgentDetails("agent-7");
            var conversationId = "conv-null";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId);

            // Assert
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.GenAiToolCallIdKey);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.GenAiToolDescriptionKey);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.GenAiToolTypeKey);
            data.Attributes.Should().ContainKey(OpenTelemetryConstants.GenAiConversationIdKey).WhoseValue.Should().Be(conversationId);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.GenAiToolCallResultKey);
        }

        [TestMethod]
        public void Build_WithTransferDetails_IncludesExplicitTransferAttributes()
        {
            var sourceAgent = new AgentDetails(agentId: "source-agent-id", agentName: "Source Agent");
            var targetAgent = new AgentDetails(
                agentId: "support-agent-id",
                agentName: "Support Agent",
                agentBlueprintId: "support-blueprint",
                agentPlatformId: "support-platform",
                agentVersion: "1.2.3");
            var tool = new ToolCallDetails(
                "handoff",
                arguments: null)
            {
                TransferDetails = new TransferDetails(
                    TransferMode.PassControl,
                    targetAgent),
            };

            var data = ExecuteToolDataBuilder.Build(
                tool,
                sourceAgent,
                "conversation-1");

            data.Attributes[OpenTelemetryConstants.GenAiAgentIdKey].Should().Be("source-agent-id");
            data.Attributes[OpenTelemetryConstants.TransferModeKey].Should().Be("pass_control");
            data.Attributes[OpenTelemetryConstants.TransferTargetTypeKey].Should().Be("agent");
            data.Attributes[OpenTelemetryConstants.TransferTargetAgentIdKey].Should().Be("support-agent-id");
            data.Attributes[OpenTelemetryConstants.TransferTargetAgentNameKey].Should().Be("Support Agent");
            data.Attributes[OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey].Should().Be("support-blueprint");
            data.Attributes[OpenTelemetryConstants.TransferTargetAgentPlatformIdKey].Should().Be("support-platform");
            data.Attributes[OpenTelemetryConstants.TransferTargetAgentVersionKey].Should().Be("1.2.3");
            data.Attributes.Keys
                .Where(key => key.StartsWith("microsoft.a365.transfer.target.", StringComparison.Ordinal))
                .Should()
                .BeEquivalentTo(new[]
                {
                    OpenTelemetryConstants.TransferTargetTypeKey,
                    OpenTelemetryConstants.TransferTargetAgentIdKey,
                    OpenTelemetryConstants.TransferTargetAgentNameKey,
                    OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey,
                    OpenTelemetryConstants.TransferTargetAgentPlatformIdKey,
                    OpenTelemetryConstants.TransferTargetAgentVersionKey,
                });
            data.Attributes.Keys.Should().NotContain(key => key.StartsWith("gen_ai.transfer.", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Build_WithModeOnly_OmitsOptionalTargetAttributes()
        {
            var tool = new ToolCallDetails(
                "handoff",
                arguments: null)
            {
                TransferDetails = new TransferDetails(TransferMode.ReturnToCaller),
            };

            var data = ExecuteToolDataBuilder.Build(
                tool,
                new AgentDetails("source-agent"),
                "conversation-1");

            data.Attributes[OpenTelemetryConstants.TransferModeKey].Should().Be("return_to_caller");
            data.Attributes[OpenTelemetryConstants.TransferTargetTypeKey].Should().Be("agent");
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentIdKey);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentNameKey);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentPlatformIdKey);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentVersionKey);
            data.Attributes.Keys
                .Where(key => key.StartsWith("microsoft.a365.transfer.target.", StringComparison.Ordinal))
                .Should()
                .BeEquivalentTo(new[]
                {
                    OpenTelemetryConstants.TransferTargetTypeKey,
                });
            data.Attributes.Keys.Should().NotContain(key => key.StartsWith("gen_ai.transfer.", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Build_WithPartialTargetAgentDetails_OmitsNullTargetAgentAttributes()
        {
            var tool = new ToolCallDetails(
                "handoff",
                arguments: null)
            {
                TransferDetails = new TransferDetails(
                    TransferMode.ReturnToCaller,
                    new AgentDetails(
                        agentId: "support-agent-id",
                        agentVersion: "9.9.9")),
            };

            var data = ExecuteToolDataBuilder.Build(
                tool,
                new AgentDetails("source-agent"),
                "conversation-1");

            data.Attributes[OpenTelemetryConstants.TransferModeKey].Should().Be("return_to_caller");
            data.Attributes[OpenTelemetryConstants.TransferTargetAgentIdKey].Should().Be("support-agent-id");
            data.Attributes[OpenTelemetryConstants.TransferTargetAgentVersionKey].Should().Be("9.9.9");
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentNameKey);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey);
            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentPlatformIdKey);
        }

        [TestMethod]
        public void Build_WithExplicitNonAgentTransferTargetTypes_IncludesOnlyTargetType()
        {
            void AssertTargetType(TransferTargetType targetType, string expectedValue)
            {
                var tool = new ToolCallDetails(
                    "handoff",
                    arguments: null)
                {
                    TransferDetails = new TransferDetails(
                        TransferMode.PassControl,
                        targetType),
                };

                var data = ExecuteToolDataBuilder.Build(
                    tool,
                    new AgentDetails("source-agent"),
                    "conversation-1");

                data.Attributes[OpenTelemetryConstants.TransferModeKey].Should().Be("pass_control");
                data.Attributes[OpenTelemetryConstants.TransferTargetTypeKey].Should().Be(expectedValue);
                data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentIdKey);
                data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentNameKey);
                data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey);
                data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentPlatformIdKey);
                data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentVersionKey);
                data.Attributes.Keys
                    .Where(key => key.StartsWith("microsoft.a365.transfer.target.", StringComparison.Ordinal))
                    .Should()
                    .BeEquivalentTo(new[]
                    {
                        OpenTelemetryConstants.TransferTargetTypeKey,
                    });
                data.Attributes.Keys.Should().NotContain(key => key.StartsWith("gen_ai.transfer.", StringComparison.Ordinal));
            }

            AssertTargetType(TransferTargetType.Human, "human");
            AssertTargetType(TransferTargetType.Workflow, "workflow");
        }

        [TestMethod]
        public void Build_SetsTimingInformation_WhenProvided()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolH", (string?)null);
            var agent = new AgentDetails("agent-8");
            var start = DateTimeOffset.UtcNow.AddMinutes(-3);
            var end = DateTimeOffset.UtcNow;
            var conversationId = "conv-time";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId, startTime: start, endTime: end);

            // Assert
            data.StartTime.Should().Be(start);
            data.EndTime.Should().Be(end);
            data.Duration.Should().BeCloseTo(TimeSpan.FromMinutes(3), TimeSpan.FromMilliseconds(100));
        }

        [TestMethod]
        public void Build_SetsSpanIds_WhenProvided()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolI", (string?)null);
            var agent = new AgentDetails("agent-9");
            var spanId = "span-tool";
            var parentSpanId = "parent-tool";
            var conversationId = "conv-span";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId, spanId: spanId, parentSpanId: parentSpanId);

            // Assert
            data.SpanId.Should().Be(spanId);
            data.ParentSpanId.Should().Be(parentSpanId);
        }

        [TestMethod]
        public void Build_WithAllParameters_SetsAllExpectedAttributes()
        {
            // Arrange
            var endpoint = new Uri("https://example.org:6060");
            var toolDetails = new ToolCallDetails("toolJ", "{x:1}", "call-999", "Full tool", "extension", endpoint);
            var agent = new AgentDetails("agent-10", "AgentTen", "Desc", agenticUserId: "auid10", agenticUserEmail: "upn10@example.com", agentBlueprintId: "bp-10");
            var conversationId = "conv-all";
            var start = DateTimeOffset.UtcNow.AddSeconds(-30);
            var end = DateTimeOffset.UtcNow;
            var spanId = "span-all";
            var parentSpanId = "parent-all";
            var responseContent = "tool-response";
            var callerDetails = new CallerDetails(userDetails: new UserDetails(userId: "caller-tool-123", userName: "Caller Tool Name", userEmail: "callertool@example.com", userClientIP: System.Net.IPAddress.Parse("10.0.0.50")));

            // Act
            var data = ExecuteToolDataBuilder.Build(
                toolDetails,
                agent,
                conversationId,
                responseContent: responseContent,
                startTime: start,
                endTime: end,
                spanId: spanId,
                parentSpanId: parentSpanId,
                callerDetails: callerDetails);

            // Assert
            var attrs = data.Attributes;
            attrs.Should().ContainKey(OpenTelemetryConstants.GenAiToolCallIdKey);
            attrs.Should().ContainKey(OpenTelemetryConstants.GenAiToolDescriptionKey);
            attrs.Should().ContainKey(OpenTelemetryConstants.GenAiToolTypeKey);
            attrs.Should().ContainKey(OpenTelemetryConstants.GenAiConversationIdKey);
            attrs.Should().ContainKey(OpenTelemetryConstants.GenAiToolCallResultKey);
            attrs.Should().ContainKey(OpenTelemetryConstants.UserIdKey).WhoseValue.Should().Be("caller-tool-123");
            attrs.Should().ContainKey(OpenTelemetryConstants.UserNameKey).WhoseValue.Should().Be("Caller Tool Name");
            attrs.Should().ContainKey(OpenTelemetryConstants.UserEmailKey).WhoseValue.Should().Be("callertool@example.com");
            attrs.Should().ContainKey(OpenTelemetryConstants.CallerClientIpKey).WhoseValue.Should().Be("10.0.0.50");
            data.StartTime.Should().Be(start);
            data.EndTime.Should().Be(end);
            data.Duration.Should().BeCloseTo(end - start, TimeSpan.FromMilliseconds(100));
            data.SpanId.Should().Be(spanId);
            data.ParentSpanId.Should().Be(parentSpanId);
        }

        [TestMethod]
        public void Build_WithOnlyStartTime_DurationZero()
        {
            // Arrange
            var toolDetails = new ToolCallDetails("toolK", (string?)null);
            var agent = new AgentDetails("agent-11");
            var start = DateTimeOffset.UtcNow;
            var conversationId = "conv-zero";

            // Act
            var data = ExecuteToolDataBuilder.Build(toolDetails, agent, conversationId, startTime: start);

            // Assert
            data.StartTime.Should().Be(start);
            data.EndTime.Should().BeNull();
            data.Duration.Should().Be(TimeSpan.Zero);
        }

        [TestMethod]
        public void Build_AddsExtraAttributes_WhenNotReserved()
        {
            // Arrange
            var tool = new ToolCallDetails("tool-extra", (string?)null);
            var agent = new AgentDetails("agent-extra", "ExtraToolAgent");
            var conversationId = "conv-extra-tool";
            var extras = new Dictionary<string, object?>
            {
                {"tool.custom", "abc"},
                {"tool.number", 7}
            };

            // Act
            var data = ExecuteToolDataBuilder.Build(tool, agent, conversationId, extraAttributes: extras);

            // Assert
            data.Attributes.Should().ContainKey("tool.custom").WhoseValue.Should().Be("abc");
            data.Attributes.Should().ContainKey("tool.number").WhoseValue.Should().Be(7);
        }

        [TestMethod]
        public void Build_DoesNotOverrideReservedKeys_WithExtraAttributes()
        {
            // Arrange
            var tool = new ToolCallDetails("tool-resv", (string?)null);
            var agent = new AgentDetails("agent-resv", "ReservedToolAgent");
            var conversationId = "conv-resv-tool";
            var extras = new Dictionary<string, object?>
            {
                {OpenTelemetryConstants.GenAiToolNameKey, "fake-tool"},
                {OpenTelemetryConstants.GenAiAgentIdKey, "fake-agent"},
                {"tool.other", "ok"}
            };

            // Act
            var data = ExecuteToolDataBuilder.Build(tool, agent, conversationId, extraAttributes: extras);

            // Assert
            data.Attributes[OpenTelemetryConstants.GenAiToolNameKey].Should().Be("tool-resv");
            data.Attributes[OpenTelemetryConstants.GenAiAgentIdKey].Should().Be("agent-resv");
            data.Attributes.Should().ContainKey("tool.other").WhoseValue.Should().Be("ok");
        }

        [TestMethod]
        public void Build_IgnoresNullValues_InExtraAttributes()
        {
            // Arrange
            var tool = new ToolCallDetails("tool-null", (string?)null);
            var agent = new AgentDetails("agent-null", "NullToolAgent");
            var conversationId = "conv-null-tool";
            var extras = new Dictionary<string, object?>
            {
                {"tool.null", null},
                {"tool.valid", "yes"}
            };

            // Act
            var data = ExecuteToolDataBuilder.Build(tool, agent, conversationId, extraAttributes: extras);

            // Assert
            data.Attributes.Should().NotContainKey("tool.null");
            data.Attributes.Should().ContainKey("tool.valid").WhoseValue.Should().Be("yes");
        }

        [TestMethod]
        public void Build_SpanKind_DefaultsToNull()
        {
            // Arrange
            var tool = new ToolCallDetails("toolSK", (string?)null);
            var agent = new AgentDetails("agent-sk");
            var conversationId = "conv-sk-default";

            // Act
            var data = ExecuteToolDataBuilder.Build(tool, agent, conversationId);

            // Assert
            data.SpanKind.Should().BeNull();
        }

        [TestMethod]
        public void Build_SpanKind_PassesThroughProvidedValue()
        {
            // Arrange
            var tool = new ToolCallDetails("toolSK", (string?)null);
            var agent = new AgentDetails("agent-sk");
            var conversationId = "conv-sk-client";

            // Act
            var data = ExecuteToolDataBuilder.Build(
                tool, agent, conversationId,
                spanKind: SpanKindConstants.Client);

            // Assert
            data.SpanKind.Should().Be(SpanKindConstants.Client);
        }

        [TestMethod]
        public void Build_WithThrowingArgumentsDictionary_UsesFallbackJson()
        {
            var tool = new ToolCallDetails("tool-throwing", new ThrowingDictionary());
            var agent = new AgentDetails("agent-throwing");
            var conversationId = "conv-throwing";

            var data = ExecuteToolDataBuilder.Build(tool, agent, conversationId);

            using var document = JsonDocument.Parse(data.Attributes[OpenTelemetryConstants.GenAiToolArgumentsKey]!.ToString()!);
            document.RootElement.GetProperty("serialization_error").GetString()
                .Should().Be("Failed to serialize execute tool payload.");
        }

        [TestMethod]
        public void Build_WithTypedArguments_UsesSchemaJson()
        {
            var tool = new ToolCallDetails(
                "tool-typed",
                new ExecuteToolCallArguments
                {
                    Action = ToolCallAction.Read,
                    Parameters = new Dictionary<string, object?> { ["location"] = "Seattle" },
                    Resources = new List<ToolCallResource>(),
                });
            var agent = new AgentDetails("agent-typed");
            var conversationId = "conv-typed";

            var data = ExecuteToolDataBuilder.Build(tool, agent, conversationId);

            using var document = JsonDocument.Parse(data.Attributes[OpenTelemetryConstants.GenAiToolArgumentsKey]!.ToString()!);
            document.RootElement.GetProperty("action").GetString().Should().Be("read");
            document.RootElement.GetProperty("schema_version").GetString().Should().Be("1.0");
        }

        [TestMethod]
        public void Build_WithNestedTypedDictionaryArguments_PreservesObjectShape()
        {
            var tool = new ToolCallDetails(
                "tool-nested-dictionary",
                new Dictionary<string, object>
                {
                    ["parameters"] = new Dictionary<string, int>
                    {
                        ["maxResults"] = 5,
                        ["offset"] = 2,
                    },
                });
            var agent = new AgentDetails("agent-nested-dictionary");
            var conversationId = "conv-nested-dictionary";

            var data = ExecuteToolDataBuilder.Build(tool, agent, conversationId);

            using var document = JsonDocument.Parse(data.Attributes[OpenTelemetryConstants.GenAiToolArgumentsKey]!.ToString()!);
            var parameters = document.RootElement.GetProperty("parameters");

            parameters.ValueKind.Should().Be(JsonValueKind.Object);
            parameters.GetProperty("maxResults").GetInt32().Should().Be(5);
            parameters.GetProperty("offset").GetInt32().Should().Be(2);
        }

        [TestMethod]
        public void Build_WithTypedArgumentsAndLegacyString_PrefersTypedArguments()
        {
            var tool = new ToolCallDetails("tool-precedence", "legacy");
            var typedArguments = new ExecuteToolCallArguments
            {
                Action = ToolCallAction.Delete,
            };
            typeof(ToolCallDetails)
                .GetField("<ToolCallArguments>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(tool, typedArguments);
            var agent = new AgentDetails("agent-precedence");
            var conversationId = "conv-precedence";

            var data = ExecuteToolDataBuilder.Build(tool, agent, conversationId);

            using var document = JsonDocument.Parse(data.Attributes[OpenTelemetryConstants.GenAiToolArgumentsKey]!.ToString()!);
            document.RootElement.GetProperty("action").GetString().Should().Be("delete");
            document.RootElement.TryGetProperty("arguments", out _).Should().BeFalse();
        }

        [TestMethod]
        public void Build_WithTypedPayloads_UsesSchemaSerializer()
        {
            var arguments = new ExecuteToolCallArguments
            {
                Action = ToolCallAction.Read,
                Resources = new List<ToolCallResource>(),
                Parameters = new Dictionary<string, object?>(),
            };
            var result = new ExecuteToolCallResult
            {
                Outcome = new ToolCallResultOutcome
                {
                    Status = ToolCallOutcomeStatus.Success,
                },
            };

            var data = ExecuteToolDataBuilder.Build(
                new ToolCallDetails("tool", arguments),
                result,
                new AgentDetails("agent"),
                "conversation");

            using var argumentsJson = JsonDocument.Parse(
                (string)data.Attributes[OpenTelemetryConstants.GenAiToolArgumentsKey]!);
            using var resultJson = JsonDocument.Parse(
                (string)data.Attributes[OpenTelemetryConstants.GenAiToolCallResultKey]!);

            argumentsJson.RootElement.GetProperty("action").GetString().Should().Be("read");
            resultJson.RootElement.GetProperty("schema_version").GetString().Should().Be("1.0");
            resultJson.RootElement.GetProperty("outcome")
                .GetProperty("status").GetString().Should().Be("success");
        }

        [TestMethod]
        public void Build_WithNullTypedResult_OmitsResultAttribute()
        {
            var tool = new ToolCallDetails("tool-null-result", (string?)null);
            var agent = new AgentDetails("agent-null-result");

            var data = ExecuteToolDataBuilder.Build(
                tool,
                (ExecuteToolCallResult)null!,
                agent,
                "conversation-null-result");

            data.Attributes.Should().NotContainKey(OpenTelemetryConstants.GenAiToolCallResultKey);
        }

        private sealed class ThrowingDictionary : IDictionary<string, object>
        {
            public object this[string key] { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public ICollection<string> Keys => Array.Empty<string>();
            public ICollection<object> Values => Array.Empty<object>();
            public int Count => 1;
            public bool IsReadOnly => true;

            public void Add(string key, object value) => throw new NotSupportedException();
            public void Add(KeyValuePair<string, object> item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Contains(KeyValuePair<string, object> item) => false;
            public bool ContainsKey(string key) => false;
            public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => throw new NotSupportedException();
            public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => throw new InvalidOperationException("test");
            public bool Remove(string key) => throw new NotSupportedException();
            public bool Remove(KeyValuePair<string, object> item) => throw new NotSupportedException();
            public bool TryGetValue(string key, out object value)
            {
                value = null!;
                return false;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString() => nameof(ThrowingDictionary);
        }
    }
}
