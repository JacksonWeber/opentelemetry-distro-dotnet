// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

namespace Microsoft.Agents.A365.Observability.Runtime.Tests.Tracing.Contracts;

[TestClass]
public sealed class ExecuteToolJsonModelsTests
{
    [TestMethod]
    public void Arguments_DefaultsSchemaVersionAndStoresStandardProperties()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            Parameters = new Dictionary<string, object?> { ["format"] = "text" },
            Resources = new List<ToolCallResource>(),
        };

        arguments.SchemaVersion.Should().Be("1.0");
        arguments.Action.Should().Be(ToolCallAction.Read);
        arguments.Parameters!["format"].Should().Be("text");
    }

    [TestMethod]
    public void Result_DefaultsSchemaVersion()
    {
        new ExecuteToolCallResult().SchemaVersion.Should().Be("1.0");
    }

    [TestMethod]
    public void ExtensionData_SerializesAtContainingObjectLevel()
    {
        var result = new ExecuteToolCallResult
        {
            Outcome = new ToolCallResultOutcome
            {
                Status = ToolCallOutcomeStatus.Success,
                AdditionalProperties =
                {
                    ["provider_outcome"] = "accepted",
                },
            },
            AdditionalProperties =
            {
                ["provider_result"] = 42,
            },
        };

        using var document = JsonDocument.Parse(MessageUtils.SerializeToolPayload(result)!);
        var root = document.RootElement;

        root.GetProperty("provider_result").GetInt32().Should().Be(42);
        root.GetProperty("outcome").GetProperty("provider_outcome").GetString()
            .Should().Be("accepted");
        root.TryGetProperty("additional_properties", out _).Should().BeFalse();
        root.GetProperty("outcome").TryGetProperty("additional_properties", out _).Should().BeFalse();
    }

    [TestMethod]
    public void IdentifierAndContainerAcceptExtensionData()
    {
        var identifier = new ToolCallIdentifier
        {
            AdditionalProperties =
            {
                ["provider_scope"] = "tenant",
            },
        };
        var container = new ToolCallContainer
        {
            AdditionalProperties =
            {
                ["provider_path"] = "/sites/Engineering",
            },
        };

        identifier.AdditionalProperties["provider_scope"].Should().Be("tenant");
        container.AdditionalProperties["provider_path"].Should().Be("/sites/Engineering");
    }

    [TestMethod]
    public void DefaultJsonSerializer_UsesSchemaNamesAndStringEnums()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            AdditionalProperties =
            {
                ["provider_option"] = true,
            },
        };

        var json = JsonSerializer.Serialize(arguments);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("schema_version").GetString().Should().Be("1.0");
        document.RootElement.GetProperty("action").GetString().Should().Be("read");
        document.RootElement.GetProperty("provider_option").GetBoolean().Should().BeTrue();
        document.RootElement.TryGetProperty("SchemaVersion", out _).Should().BeFalse();
    }

    [TestMethod]
    public void DefaultJsonSerializer_DeserializesSchemaNamesAndStringEnums()
    {
        var arguments = JsonSerializer.Deserialize<ExecuteToolCallArguments>(
            """{"schema_version":"2.0","action":"read","provider_option":true}""");

        arguments.Should().NotBeNull();
        arguments!.SchemaVersion.Should().Be("2.0");
        arguments.Action.Should().Be(ToolCallAction.Read);
        arguments.AdditionalProperties.Should().ContainKey("provider_option");
        arguments.AdditionalProperties.Should().NotContainKey("schema_version");
        arguments.AdditionalProperties.Should().NotContainKey("action");
    }

    [TestMethod]
    public void DefaultJsonSerializer_RejectsUndefinedEnumValues()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = (ToolCallAction)999,
        };

        var act = () => JsonSerializer.Serialize(arguments);

        act.Should().Throw<JsonException>();
    }

    [TestMethod]
    public void DefaultJsonSerializer_RejectsNumericAction()
    {
        var act = () => JsonSerializer.Deserialize<ExecuteToolCallArguments>(
            """{"action":1}""");

        act.Should().Throw<JsonException>();
    }

    [TestMethod]
    public void DefaultJsonSerializer_RejectsNumericOutcomeStatus()
    {
        var act = () => JsonSerializer.Deserialize<ToolCallResultOutcome>(
            """{"status":0}""");

        act.Should().Throw<JsonException>();
    }

    [TestMethod]
    public void DefaultJsonSerializer_RejectsNumericPolicyDecision()
    {
        var act = () => JsonSerializer.Deserialize<ToolCallResultPolicy>(
            """{"decision":0}""");

        act.Should().Throw<JsonException>();
    }
}
