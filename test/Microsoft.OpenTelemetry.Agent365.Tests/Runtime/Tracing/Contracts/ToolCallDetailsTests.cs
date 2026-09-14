// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Contracts;

[TestClass]
public sealed class ToolCallDetailsTests
{
    [TestMethod]
    public void ObjectInitializer_WithTransferDetails_ExposesTypedValues()
    {
        var targetAgent = new AgentDetails(
            agentId: "weather-agent-id",
            agentName: "Weather Agent",
            agentBlueprintId: "weather-blueprint",
            agentPlatformId: "weather-platform",
            agentVersion: "2026.09.10");
        var transfer = new TransferDetails(
            TransferMode.ReturnToCaller,
            targetAgentDetails: targetAgent);

        var details = new ToolCallDetails(
            "handoff",
            arguments: null)
        {
            TransferDetails = transfer,
        };

        details.TransferDetails.Should().BeSameAs(transfer);
        transfer.Mode.Should().Be(TransferMode.ReturnToCaller);
        transfer.TargetType.Should().Be(TransferTargetType.Agent);
        transfer.TargetAgentDetails.Should().BeSameAs(targetAgent);
        transfer.TargetAgentDetails!.AgentId.Should().Be("weather-agent-id");
        transfer.TargetAgentDetails.AgentName.Should().Be("Weather Agent");
        transfer.TargetAgentDetails.AgentBlueprintId.Should().Be("weather-blueprint");
        transfer.TargetAgentDetails.AgentPlatformId.Should().Be("weather-platform");
        transfer.TargetAgentDetails.AgentVersion.Should().Be("2026.09.10");
        transfer.ModeValue.Should().Be("return_to_caller");
        transfer.TargetTypeValue.Should().Be("agent");
    }

    [TestMethod]
    public void Constructor_WithoutTransferDetails_RemainsCompatible()
    {
        var details = new ToolCallDetails(
            "tool",
            "{}",
            "call-1",
            "description",
            "function",
            new Uri("https://example.com"));

        details.TransferDetails.Should().BeNull();

        var (name, arguments, callId, description, type, endpoint) = details;
        name.Should().Be("tool");
        arguments.Should().Be("{}");
        callId.Should().Be("call-1");
        description.Should().Be("description");
        type.Should().Be("function");
        endpoint.Should().Be(new Uri("https://example.com"));
    }

    [TestMethod]
    public void Equals_WithEquivalentTransferDetails_IsTrueAndHasMatchingHashCode()
    {
        var left = new ToolCallDetails(
            "handoff",
            arguments: null)
        {
            TransferDetails = new TransferDetails(
                TransferMode.PassControl,
                new AgentDetails(
                    agentId: "support-agent-id",
                    agentName: "Support Agent",
                    agentBlueprintId: "support-blueprint",
                    agentPlatformId: "support-platform",
                    agentVersion: "1.2.3")),
        };
        var right = new ToolCallDetails(
            "handoff",
            arguments: null)
        {
            TransferDetails = new TransferDetails(
                TransferMode.PassControl,
                new AgentDetails(
                    agentId: "support-agent-id",
                    agentName: "Support Agent",
                    agentBlueprintId: "support-blueprint",
                    agentPlatformId: "support-platform",
                    agentVersion: "1.2.3")),
        };

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [TestMethod]
    public void TransferDetails_WithNullTargetAgentDetails_IsEqualAndHasMatchingHashCode()
    {
        var left = new TransferDetails(TransferMode.PassControl);
        var right = new TransferDetails(TransferMode.PassControl, TransferTargetType.Agent, null);

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [TestMethod]
    public void TransferDetails_ModeOnlyConstructor_DefaultsTargetTypeToAgent()
    {
        var transfer = new TransferDetails(TransferMode.PassControl);

        transfer.TargetType.Should().Be(TransferTargetType.Agent);
        transfer.TargetAgentDetails.Should().BeNull();
    }

    [TestMethod]
    public void TransferDetails_MapsAllStandardValues()
    {
        new TransferDetails(TransferMode.PassControl).ModeValue
            .Should().Be("pass_control");
        new TransferDetails(TransferMode.ReturnToCaller).ModeValue
            .Should().Be("return_to_caller");
        new TransferDetails(TransferMode.PassControl, TransferTargetType.Agent).TargetTypeValue
            .Should().Be("agent");
        new TransferDetails(TransferMode.PassControl, TransferTargetType.Human).TargetTypeValue
            .Should().Be("human");
        new TransferDetails(TransferMode.PassControl, TransferTargetType.Workflow).TargetTypeValue
            .Should().Be("workflow");
    }

    [TestMethod]
    public void Equals_WithDifferentTargetAgentDetails_IsFalse()
    {
        var left = new TransferDetails(
            TransferMode.PassControl,
            new AgentDetails(agentId: "agent-a"));
        var right = new TransferDetails(
            TransferMode.PassControl,
            new AgentDetails(agentId: "agent-b"));

        left.Should().NotBe(right);
    }

    [TestMethod]
    public void Equals_WithDifferentTargetType_IsFalse()
    {
        var left = new TransferDetails(TransferMode.PassControl, TransferTargetType.Agent);
        var right = new TransferDetails(TransferMode.PassControl, TransferTargetType.Workflow);

        left.Should().NotBe(right);
    }

    [TestMethod]
    public void ToolCallDetails_Equals_WithDifferentTransferMode_IsFalse()
    {
        var targetAgent = new AgentDetails(agentId: "support-agent");
        var left = new ToolCallDetails("handoff", arguments: null)
        {
            TransferDetails = new TransferDetails(TransferMode.PassControl, targetAgent),
        };
        var right = new ToolCallDetails("handoff", arguments: null)
        {
            TransferDetails = new TransferDetails(TransferMode.ReturnToCaller, targetAgent),
        };

        left.Should().NotBe(right);
    }

    [TestMethod]
    public void TransferDetails_WithUndefinedMode_ThrowsAtConstructionTime()
    {
        Action act = () => new TransferDetails((TransferMode)999);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("mode");
    }

    [TestMethod]
    public void TransferDetails_WithUndefinedTargetType_ThrowsAtConstructionTime()
    {
        Action act = () => new TransferDetails(TransferMode.PassControl, (TransferTargetType)999);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("targetType");
    }

    [TestMethod]
    public void TransferDetails_WithNonAgentTargetTypeAndTargetAgentDetails_ThrowsAtConstructionTime()
    {
        Action act = () => new TransferDetails(
            TransferMode.PassControl,
            TransferTargetType.Human,
            new AgentDetails(agentId: "agent-a"));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("targetAgentDetails");
    }

    [TestMethod]
    public void Equals_WithSameTypedArgumentsReference_IsTrueAndHasMatchingHashCode()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
        };

        var left = new ToolCallDetails("tool-name", arguments);
        var right = new ToolCallDetails("tool-name", arguments);

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [TestMethod]
    public void Equals_WithDifferentTypedArgumentsReference_IsFalse()
    {
        var left = new ToolCallDetails(
            "tool-name",
            new ExecuteToolCallArguments
            {
                Action = ToolCallAction.Read,
            });

        var right = new ToolCallDetails(
            "tool-name",
            new ExecuteToolCallArguments
            {
                Action = ToolCallAction.Read,
            });

        left.Should().NotBe(right);
    }
}
