// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Scopes;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Etw;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Tracing;

[TestClass]
public sealed class ExecuteToolScopeTest : ActivityTest
{
    private sealed class TestEventListener : EventListener
    {
        public List<EventWrittenEventArgs> Events { get; } = new();

        protected override void OnEventWritten(EventWrittenEventArgs eventData) => Events.Add(eventData);
    }

    [TestMethod]
    public void Start_Arguments_Set()
    {
        const string expected = "Input: 42";
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(Util.GetDefaultRequest(), new ToolCallDetails("TestTool", expected), Util.GetAgentDetails());
        });
        
        activity.ShouldHaveTagContaining(OpenTelemetryConstants.GenAiToolArgumentsKey, expected);
    }

    [TestMethod]
    public void RecordResponse_Response_Set()
    {
        const string expected = "Output: 42";
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(Util.GetDefaultRequest(), new ToolCallDetails("TestTool", "x"), Util.GetAgentDetails());
            scope.RecordResponse(expected);
        });

        var tagValue = activity.Tags.First(t => t.Key == OpenTelemetryConstants.GenAiToolCallResultKey).Value;
        tagValue.Should().Contain("\"result\"");
        tagValue.Should().Contain(expected);
    }

    [TestMethod]
    public void RecordError_SetsExpectedFields()
    {
        const string expected = "Test error";
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(Util.GetDefaultRequest(), new ToolCallDetails("TestTool", "x"), Util.GetAgentDetails());
            scope?.RecordError(new Exception(expected));
        });
        
        activity.ShouldBeError(expected);
    }

    [TestMethod]
    public void SetStartTime_SetsActivityStartTime()
    {
        var customStartTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("TestTool", "args"), 
                Util.GetAgentDetails());
            scope.SetStartTime(customStartTime);
        });

        // Activity start time should be close to the custom start time
        var startTime = new DateTimeOffset(activity.StartTimeUtc);
        startTime.Should().BeCloseTo(customStartTime, TimeSpan.FromMilliseconds(100));
    }

    [TestMethod]
    public void Start_SetsConversationId_WhenProvided()
    {
        var conversationId = "conv-tool-123";
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                new Request(conversationId: conversationId),
                new ToolCallDetails("TestTool", "args"),
                Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiConversationIdKey, conversationId);
    }

    [TestMethod]
    public void Start_SetsSessionId_WhenProvidedOnRequest()
    {
        var sessionId = "session-tool-123";
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                new Request(sessionId: sessionId),
                new ToolCallDetails("TestTool", "args"),
                Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.SessionIdKey, sessionId);
    }

    [TestMethod]
    public void Start_SetsChannel_Tags()
    {
        var metadata = new Channel(name: "ChannelY", link: "https://channel/link/y");

        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                new Request(channel: metadata),
                new ToolCallDetails("TestTool", "args"),
                Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.ChannelNameKey, metadata.Name!);
        activity.ShouldHaveTag(OpenTelemetryConstants.ChannelLinkKey, metadata.Link!);
    }

    [TestMethod]
    public void Start_WithTransferDetails_SetsExplicitTransferAttributes()
    {
        var sourceAgent = new AgentDetails(agentId: "source-agent-id", agentName: "Source Agent");
        var targetAgent = new AgentDetails(
            agentId: "weather-agent-id",
            agentName: "Weather Agent",
            agentBlueprintId: "weather-blueprint",
            agentPlatformId: "weather-platform",
            agentVersion: "2026.09.10");

        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails(
                    "handoff",
                    arguments: null)
                {
                    TransferDetails = new TransferDetails(
                        TransferMode.ReturnToCaller,
                        targetAgent),
                },
                sourceAgent);
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiAgentIdKey, "source-agent-id");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferModeKey, "return_to_caller");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetTypeKey, "agent");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetAgentIdKey, "weather-agent-id");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetAgentNameKey, "Weather Agent");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey, "weather-blueprint");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetAgentPlatformIdKey, "weather-platform");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetAgentVersionKey, "2026.09.10");
        activity.Tags
            .Where(tag => tag.Key.StartsWith("microsoft.a365.transfer.target.", StringComparison.Ordinal))
            .Select(tag => tag.Key)
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
        activity.Tags
            .Select(tag => tag.Key)
            .Should()
            .NotContain(key => key.StartsWith("gen_ai.transfer.", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Start_WithoutTransferDetails_OmitsTransferAttributes()
    {
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("ordinary-tool", (string?)null),
                Util.GetAgentDetails());
        });

        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferModeKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetTypeKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentIdKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentNameKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentPlatformIdKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentVersionKey);
    }

    [TestMethod]
    public void Start_WithTransferModeOnly_OmitsAllTargetAgentAttributes()
    {
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("handoff", arguments: null)
                {
                    TransferDetails = new TransferDetails(TransferMode.PassControl),
                },
                Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.TransferModeKey, "pass_control");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetTypeKey, "agent");
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentIdKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentNameKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentPlatformIdKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentVersionKey);
        activity.Tags
            .Where(tag => tag.Key.StartsWith("microsoft.a365.transfer.target.", StringComparison.Ordinal))
            .Select(tag => tag.Key)
            .Should()
            .BeEquivalentTo(new[]
            {
                OpenTelemetryConstants.TransferTargetTypeKey,
            });
        activity.Tags
            .Select(tag => tag.Key)
            .Should()
            .NotContain(key => key.StartsWith("gen_ai.transfer.", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Start_WithPartialTargetAgentDetails_OmitsNullTargetAgentAttributes()
    {
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails(
                    "handoff",
                    arguments: null)
                {
                    TransferDetails = new TransferDetails(
                        TransferMode.ReturnToCaller,
                        new AgentDetails(
                            agentId: "weather-agent-id",
                            agentVersion: "2026.09.11")),
                },
                Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.TransferModeKey, "return_to_caller");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetAgentIdKey, "weather-agent-id");
        activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetAgentVersionKey, "2026.09.11");
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentNameKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey);
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentPlatformIdKey);
    }

    [TestMethod]
    public void Start_WithExplicitNonAgentTransferTargetTypes_SetsOnlyTargetTypeAttribute()
    {
        void AssertTargetType(TransferTargetType targetType, string expectedValue)
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = ExecuteToolScope.Start(
                    Util.GetDefaultRequest(),
                    new ToolCallDetails(
                        "handoff",
                        arguments: null)
                    {
                        TransferDetails = new TransferDetails(
                            TransferMode.PassControl,
                            targetType),
                    },
                    Util.GetAgentDetails());
            });

            activity.ShouldHaveTag(OpenTelemetryConstants.TransferModeKey, "pass_control");
            activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetTypeKey, expectedValue);
            activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentIdKey);
            activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentNameKey);
            activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentBlueprintIdKey);
            activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentPlatformIdKey);
            activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetAgentVersionKey);
            activity.Tags
                .Where(tag => tag.Key.StartsWith("microsoft.a365.transfer.target.", StringComparison.Ordinal))
                .Select(tag => tag.Key)
                .Should()
                .BeEquivalentTo(new[]
                {
                    OpenTelemetryConstants.TransferTargetTypeKey,
                });
            activity.Tags
                .Select(tag => tag.Key)
                .Should()
                .NotContain(key => key.StartsWith("gen_ai.transfer.", StringComparison.Ordinal));
        }

        AssertTargetType(TransferTargetType.Human, "human");
        AssertTargetType(TransferTargetType.Workflow, "workflow");
    }

    [TestMethod]
    public void ThreatDiagnosticsSummary_IsSetCorrectly_WhenProvided()
    {
        // Arrange
        var threatSummary = new ThreatDiagnosticsSummary(
            blockAction: false,
            reasonCode: 0,
            reason: "No threat detected.",
            diagnostics: null);
        var toolCallDetails = new ToolCallDetails("SecurityTool", "scan args");
        var agentDetails = Util.GetAgentDetails();

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                toolCallDetails,
                agentDetails,
                threatDiagnosticsSummary: threatSummary);
        });

        // Assert
        activity.ShouldHaveTag(OpenTelemetryConstants.ThreatDiagnosticsSummaryKey, "{\"blockAction\":false,\"reasonCode\":0,\"reason\":\"No threat detected.\",\"diagnostics\":null}");
    }

    [TestMethod]
    public void ThreatDiagnosticsSummary_IsNotSet_WhenNull()
    {
        // Arrange
        var toolCallDetails = new ToolCallDetails("TestTool", "args");
        var agentDetails = Util.GetAgentDetails();

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                toolCallDetails,
                agentDetails,
                threatDiagnosticsSummary: null);
        });

        // Assert
        activity.Tags.Should().NotContainKey(OpenTelemetryConstants.ThreatDiagnosticsSummaryKey);
    }

    [TestMethod]
    public void RecordThreatDiagnosticsSummary_SetsTagCorrectly()
    {
        // Arrange
        var threatSummary = new ThreatDiagnosticsSummary(
            blockAction: true,
            reasonCode: 200,
            reason: "Blocked due to policy violation.",
            diagnostics: "{\"policy\":\"data-loss-prevention\"}");
        var toolCallDetails = new ToolCallDetails("TestTool", "args");
        var agentDetails = Util.GetAgentDetails();

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(Util.GetDefaultRequest(), toolCallDetails, agentDetails);
            scope.RecordThreatDiagnosticsSummary(threatSummary);
        });

        // Assert
        var tagValue = activity.Tags.First(t => t.Key == OpenTelemetryConstants.ThreatDiagnosticsSummaryKey).Value;
        tagValue.Should().Contain("\"blockAction\":true");
        tagValue.Should().Contain("\"reasonCode\":200");
        tagValue.Should().Contain("\"reason\":\"Blocked due to policy violation.\"");
        tagValue.Should().Contain("data-loss-prevention");
    }

    [TestMethod]
    public void Start_SetsCallerDetails_WhenProvided()
    {
        // Arrange
        var userDetails = new UserDetails(
            userId: "caller-123",
            userName: "Test Caller",
            userEmail: "caller@example.com",
            userClientIP: System.Net.IPAddress.Parse("192.168.1.1"));

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("TestTool", "args"),
                Util.GetAgentDetails(),
                userDetails: userDetails);
        });

        // Assert
        activity.ShouldHaveTag(OpenTelemetryConstants.UserIdKey, userDetails.UserId!);
        activity.ShouldHaveTag(OpenTelemetryConstants.UserNameKey, userDetails.UserName!);
        activity.ShouldHaveTag(OpenTelemetryConstants.UserEmailKey, userDetails.UserEmail!);
        activity.ShouldHaveTag(OpenTelemetryConstants.CallerClientIpKey, userDetails.UserClientIP!.ToString());
    }

    [TestMethod]
    public void Start_WithThrowingArgumentsDictionary_UsesFallbackJson()
    {
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("TestTool", new ThrowingDictionary()),
                Util.GetAgentDetails());
        });

        var tagValue = activity.Tags.First(t => t.Key == OpenTelemetryConstants.GenAiToolArgumentsKey).Value;
        tagValue.Should().NotBeNull();
        using var document = JsonDocument.Parse(tagValue!);
        document.RootElement.GetProperty("serialization_error").GetString()
            .Should().Be("Failed to serialize execute tool payload.");
    }

    [TestMethod]
    public void Start_WithTypedArguments_RecordsSchemaJson()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            Parameters = new Dictionary<string, object?> { ["location"] = "Seattle" },
            Resources = new List<ToolCallResource>(),
        };

        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails(toolName: "get_weather", toolCallArguments: arguments),
                Util.GetAgentDetails());
        });

        var json = activity.Tags.Single(
            pair => pair.Key == OpenTelemetryConstants.GenAiToolArgumentsKey).Value;
        using var document = JsonDocument.Parse(json!);
        document.RootElement.GetProperty("action").GetString().Should().Be("read");
        document.RootElement.GetProperty("schema_version").GetString().Should().Be("1.0");
    }

    [TestMethod]
    public void ToolCallDetails_WithTypedArguments_ExposesTypedArgumentsProperty()
    {
        var arguments = new ExecuteToolCallArguments();

        var details = new ToolCallDetails(toolName: "get_weather", toolCallArguments: arguments);

        details.ToolCallArguments.Should().BeSameAs(arguments);
    }

    [TestMethod]
    public void Start_WithTypedArgumentsAndLegacyString_PrefersTypedArguments()
    {
        var details = new ToolCallDetails("get_weather", "legacy");
        typeof(ToolCallDetails)
            .GetField("<ToolCallArguments>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(
                details,
                new ExecuteToolCallArguments
                {
                    Action = ToolCallAction.Read,
                });

        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                details,
                Util.GetAgentDetails());
        });

        var json = activity.Tags.Single(
            pair => pair.Key == OpenTelemetryConstants.GenAiToolArgumentsKey).Value;
        using var document = JsonDocument.Parse(json!);
        document.RootElement.GetProperty("action").GetString().Should().Be("read");
        document.RootElement.TryGetProperty("arguments", out _).Should().BeFalse();
    }

    [TestMethod]
    public void RecordResponse_WithTypedResult_RecordsSchemaJson()
    {
        var result = new ExecuteToolCallResult
        {
            Outcome = new ToolCallResultOutcome
            {
                Status = ToolCallOutcomeStatus.Success,
            },
            Data = new Dictionary<string, object?>(),
            Resources = new List<ToolCallResultResource>(),
            Pagination = new ToolCallResultPagination
            {
                HasMore = false,
                TotalCount = 0,
            },
        };

        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("get_weather", (string?)null),
                Util.GetAgentDetails());
            scope.RecordResponse(result);
        });

        var json = activity.Tags.Single(
            pair => pair.Key == OpenTelemetryConstants.GenAiToolCallResultKey).Value;
        using var document = JsonDocument.Parse(json!);
        document.RootElement.GetProperty("schema_version").GetString().Should().Be("1.0");
        document.RootElement.GetProperty("outcome")
            .GetProperty("status").GetString().Should().Be("success");
    }

    [TestMethod]
    public void RecordResponse_WithNullTypedResult_OmitsResultTag()
    {
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("get_weather", (string?)null),
                Util.GetAgentDetails());
            scope.RecordResponse((ExecuteToolCallResult)null!);
        });

        activity.Tags.Should().NotContain(
            pair => pair.Key == OpenTelemetryConstants.GenAiToolCallResultKey);
    }

    [TestMethod]
    public void TypedArgumentsAndResult_MatchEtwLoggerJson()
    {
        var request = new Request(conversationId: "conv-tool-compare");
        var agentDetails = Util.GetAgentDetails();
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            Resources = new List<ToolCallResource>
            {
                new()
                {
                    Id = "doc-1",
                    Provider = "sharepoint",
                    Type = "document",
                    Identifiers = new List<ToolCallIdentifier>
                    {
                        new()
                        {
                            Type = "microsoft.graph.drive_item_id",
                            Value = "01ABCDEF",
                        },
                    },
                    Container = new ToolCallContainer
                    {
                        Id = "sharepoint://contoso.sharepoint.com/sites/Engineering",
                        Uri = "https://contoso.sharepoint.com/sites/Engineering",
                        Type = "site",
                    },
                },
            },
            Parameters = new Dictionary<string, object?>
            {
                ["format"] = "text",
                ["includeMetadata"] = true,
            },
        };
        var result = new ExecuteToolCallResult
        {
            Outcome = new ToolCallResultOutcome
            {
                Status = ToolCallOutcomeStatus.Success,
            },
            Resources = new List<ToolCallResultResource>
            {
                new()
                {
                    Id = "doc-1",
                    Type = "document",
                    Data = new Dictionary<string, object?>
                    {
                        ["title"] = "Quarterly plan",
                    },
                },
            },
            Data = new Dictionary<string, object?>
            {
                ["summary"] = "Document retrieved",
            },
            Pagination = new ToolCallResultPagination
            {
                HasMore = false,
                TotalCount = 1,
            },
        };
        var details = new ToolCallDetails("sharepoint_get_document", arguments);

        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(request, details, agentDetails);
            scope.RecordResponse(result);
        });

        using var listener = new TestEventListener();
        listener.EnableEvents(EtwEventSource.Log, EventLevel.Informational);
        using var provider = new ServiceCollection().AddLoggingWithEtw().BuildServiceProvider();
        var logger = provider.GetRequiredService<IA365EtwLogger<ExecuteToolScopeTest>>();

        logger.LogToolCall(details, result, agentDetails, request.ConversationId!);

        var etwPayload = listener.Events.Single(e => e.EventId == 2000).Payload![0] as string;
        using var etwDocument = JsonDocument.Parse(etwPayload!);
        var etwAttributes = etwDocument.RootElement.GetProperty("Attributes");
        var scopeArgumentsJson = activity.Tags.Single(pair => pair.Key == OpenTelemetryConstants.GenAiToolArgumentsKey).Value;
        var scopeResultJson = activity.Tags.Single(pair => pair.Key == OpenTelemetryConstants.GenAiToolCallResultKey).Value;

        JsonNode.DeepEquals(
            JsonNode.Parse(scopeArgumentsJson!),
            JsonNode.Parse(etwAttributes.GetProperty(OpenTelemetryConstants.GenAiToolArgumentsKey).GetString()!))
            .Should().BeTrue();
        JsonNode.DeepEquals(
            JsonNode.Parse(scopeResultJson!),
            JsonNode.Parse(etwAttributes.GetProperty(OpenTelemetryConstants.GenAiToolCallResultKey).GetString()!))
            .Should().BeTrue();
    }

    [TestMethod]
    public void RecordResponse_ExposesTypedExecuteToolResultOverload()
    {
        typeof(ExecuteToolScope)
            .GetMethod(nameof(ExecuteToolScope.RecordResponse), new[] { typeof(ExecuteToolCallResult) })
            .Should().NotBeNull();
    }

    [TestMethod]
    public void Start_WithCustomStartTime_SetsActivityStartTime()
    {
        // Arrange
        var customStartTime = new DateTimeOffset(2023, 11, 14, 22, 13, 20, TimeSpan.Zero);

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("TestTool", "args"),
                Util.GetAgentDetails(),
                spanDetails: new SpanDetails(startTime: customStartTime));
        });

        // Assert
        var startTime = new DateTimeOffset(activity.StartTimeUtc);
        startTime.Should().BeCloseTo(customStartTime, TimeSpan.FromMilliseconds(100));
    }

    [TestMethod]
    public void Start_WithCustomStartAndEndTime_SetsActivityTimes()
    {
        // Arrange
        var customStartTime = new DateTimeOffset(2023, 11, 14, 22, 13, 20, TimeSpan.Zero);
        var customEndTime = new DateTimeOffset(2023, 11, 14, 22, 13, 25, TimeSpan.Zero); // 5 seconds later

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("TestTool", "args"),
                Util.GetAgentDetails(),
                spanDetails: new SpanDetails(startTime: customStartTime, endTime: customEndTime));
        });

        // Assert - Start time should be set to custom time
        var startTime = new DateTimeOffset(activity.StartTimeUtc);
        startTime.Should().BeCloseTo(customStartTime, TimeSpan.FromMilliseconds(100));
    }

    [TestMethod]
    public void SetEndTime_OverridesEndTime()
    {
        // Arrange
        var customStartTime = new DateTimeOffset(2023, 11, 14, 22, 13, 40, TimeSpan.Zero);
        var initialEndTime = new DateTimeOffset(2023, 11, 14, 22, 13, 45, TimeSpan.Zero);
        var laterEndTime = new DateTimeOffset(2023, 11, 14, 22, 13, 48, TimeSpan.Zero);

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("TestTool", "args"),
                Util.GetAgentDetails(),
                spanDetails: new SpanDetails(startTime: customStartTime, endTime: initialEndTime));
            scope.SetEndTime(laterEndTime);
        });

        // Assert - The start time should be set
        var startTime = new DateTimeOffset(activity.StartTimeUtc);
        startTime.Should().BeCloseTo(customStartTime, TimeSpan.FromMilliseconds(100));
    }

    [TestMethod]
    public void SpanKind_DefaultsToInternal()
    {
        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("TestTool", "args"),
                Util.GetAgentDetails());
        });

        // Assert
        activity.Kind.Should().Be(System.Diagnostics.ActivityKind.Internal);
    }

    [TestMethod]
    public void SpanKind_OverrideToClient()
    {
        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = ExecuteToolScope.Start(
                Util.GetDefaultRequest(),
                new ToolCallDetails("TestTool", "args"),
                Util.GetAgentDetails(),
                spanDetails: new SpanDetails(spanKind: System.Diagnostics.ActivityKind.Client));
        });

        // Assert
        activity.Kind.Should().Be(System.Diagnostics.ActivityKind.Client);
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
