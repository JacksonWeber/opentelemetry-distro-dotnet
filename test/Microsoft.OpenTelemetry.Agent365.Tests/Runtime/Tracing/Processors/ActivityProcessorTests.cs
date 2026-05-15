// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Tests.Tracing;

using System.Diagnostics;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using static Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryConstants;

[TestClass]
public sealed class ActivityProcessorTests : ActivityTest
{
    private const string ExternalSourceName = "System.Net.Http";

    /// <summary>
    /// Activities from a non-Agent365Sdk source must pass through untouched even when
    /// Agent365 baggage is set in the ambient context.
    /// </summary>
    [TestMethod]
    public void OnStart_DoesNotMutate_NonAgent365Activities()
    {
        // Arrange - register the processor for an external source
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(ExternalSourceName)
            .AddProcessor(new ActivityProcessor())
            .Build();

        Activity? capturedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == ExternalSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => capturedActivity = a,
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        // Act - set Agent365 baggage then start a span from an unrelated source
        using (new BaggageBuilder()
            .TenantId("tenant-123")
            .AgentId("agent-abc")
            .SessionId("session-xyz")
            .Build())
        {
            var externalSource = new ActivitySource(ExternalSourceName);
            using var activity = externalSource.StartActivity("HTTP GET /api/data");

            // Assert - no Agent365 / GenAI tags must be applied
            capturedActivity.Should().NotBeNull();
            capturedActivity!.GetTagItem(TenantIdKey).Should().BeNull(
                because: "non-Agent365 spans must not receive microsoft.tenant.id");
            capturedActivity.GetTagItem(GenAiAgentIdKey).Should().BeNull(
                because: "non-Agent365 spans must not receive gen_ai.agent.id");
            capturedActivity.GetTagItem(GenAiAgentNameKey).Should().BeNull(
                because: "non-Agent365 spans must not receive gen_ai.agent.name");
            capturedActivity.GetTagItem(SessionIdKey).Should().BeNull(
                because: "non-Agent365 spans must not receive microsoft.session.id");
            capturedActivity.GetTagItem(TelemetrySdkNameKey).Should().BeNull(
                because: "non-Agent365 spans must not receive telemetry.sdk.name");
        }
    }

    /// <summary>
    /// An activity that originates from the Agent365Sdk source but carries no
    /// <c>gen_ai.operation.name</c> tag (i.e. not one of the four GenAI scopes)
    /// must also pass through untouched.
    /// </summary>
    [TestMethod]
    public void OnStart_DoesNotMutate_Agent365ActivitiesWithoutGenAiOperationName()
    {
        // Arrange - register the processor for the Agent365 source
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(SourceName)
            .AddProcessor(new ActivityProcessor())
            .Build();

        Activity? capturedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => capturedActivity = a,
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        // Act - set baggage and start a raw Agent365Sdk span (no gen_ai.operation.name tag)
        using (new BaggageBuilder()
            .TenantId("tenant-123")
            .AgentId("agent-abc")
            .Build())
        {
            var agent365Source = new ActivitySource(SourceName);
            using var activity = agent365Source.StartActivity("custom-non-genai-span");

            // Assert - no GenAI tags must be applied to a non-GenAI Agent365Sdk span
            capturedActivity.Should().NotBeNull();
            capturedActivity!.GetTagItem(TenantIdKey).Should().BeNull(
                because: "Agent365Sdk spans without gen_ai.operation.name must not receive microsoft.tenant.id");
            capturedActivity.GetTagItem(GenAiAgentIdKey).Should().BeNull(
                because: "Agent365Sdk spans without gen_ai.operation.name must not receive gen_ai.agent.id");
            capturedActivity.GetTagItem(TelemetrySdkNameKey).Should().BeNull(
                because: "Agent365Sdk spans without gen_ai.operation.name must not receive telemetry.sdk.name");
        }
    }

    /// <summary>
    /// The four GenAI scope types (invoke_agent, execute_tool, inference, output_messages)
    /// must have baggage-backed tags coalesced onto them by the processor.
    /// </summary>
    [TestMethod]
    public void OnStart_Mutates_GenAiScopeActivities()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - set baggage then start each GenAI scope
        using (new BaggageBuilder()
            .TenantId("tenant-123")
            .AgentId("agent-abc")
            .Build())
        {
            var activities = new[]
            {
                ListenForActivity(() =>
                {
                    using var scope = InvokeAgentScope.Start(
                        new Request(),
                        new InvokeAgentScopeDetails(endpoint: null),
                        new AgentDetails("agent-1"));
                }),
                ListenForActivity(() =>
                {
                    using var scope = ExecuteToolScope.Start(
                        new Request(),
                        new ToolCallDetails("tool-name", "{}"),
                        new AgentDetails("agent-1"));
                }),
                ListenForActivity(() =>
                {
                    using var scope = InferenceScope.Start(
                        new Request(),
                        new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                        new AgentDetails("agent-1"));
                }),
                ListenForActivity(() =>
                {
                    using var scope = OutputScope.Start(
                        new Request(),
                        new Response(new[] { "output-message" }),
                        new AgentDetails("agent-1"));
                }),
            };

            // Assert - baggage-backed tags must be coalesced onto GenAI spans
            foreach (var activity in activities)
            {
                activity.GetTagItem(TenantIdKey).Should().Be("tenant-123",
                    because: "GenAI spans must receive microsoft.tenant.id from baggage");
                activity.GetTagItem(GenAiAgentIdKey).Should().NotBeNull(
                    because: "GenAI spans must receive gen_ai.agent.id");
                activity.GetTagItem(TelemetrySdkNameKey).Should().Be(TelemetrySdkNameValue,
                    because: "GenAI spans must receive the telemetry.sdk.name tag");
            }
        }
    }
}
