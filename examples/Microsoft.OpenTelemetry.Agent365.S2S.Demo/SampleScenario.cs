using System.Diagnostics;
using System.Net;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal static class SampleScenario
{
    private static readonly Uri AgentEndpoint = new("https://weather-agent.contoso.com:8443");
    private static readonly Uri ToolEndpoint = new("https://weather-api.contoso.com");

    private const string SessionId = "session-s2s-123";
    private const string ConversationId = "conversation-s2s-789";
    private const string ChannelName = "service";
    private const string ServiceName = "Microsoft.OpenTelemetry.Agent365.S2S.Demo";
    private const string Question = "What is the weather in Seattle right now?";
    private const string ToolSummary = "The current Seattle weather is 62°F and partly cloudy.";
    private const string FinalAnswer = "It is currently 62°F and partly cloudy in Seattle.";
    private const string AgentName = "Weather Agent";
    private const string AgentDescription = "Answers current Seattle weather questions.";
    private const string ModelName = "gpt-4o-mini";
    private const string PublishingAgenticUserId = "11111111-1111-1111-1111-111111111111";
    private const string PublishingAgenticUserEmail = "sample-agent@example.invalid";
    private const string PublishingUserId = "22222222-2222-2222-2222-222222222222";
    private const string PublishingUserName = "Sample User";
    private const string PublishingUserEmail = "sample-user@example.invalid";
    private static readonly IPAddress PublishingClientAddress = IPAddress.Parse("192.0.2.1");
    private static readonly TimeSpan FirstInferenceDuration = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan ToolDuration = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan FinalInferenceDuration = TimeSpan.FromMilliseconds(30);

    internal static async Task RunAsync(
        SampleOptions options,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        delay ??= SimulateDelayAsync;
        timeProvider ??= TimeProvider.System;
        cancellationToken.ThrowIfCancellationRequested();

        var agent = new AgentDetails(
            agentId: options.AgentId,
            agentName: AgentName,
            agentDescription: AgentDescription,
            agenticUserId: PublishingAgenticUserId,
            agenticUserEmail: PublishingAgenticUserEmail,
            agentBlueprintId: options.ClientId,
            tenantId: options.TenantId,
            providerName: "openai",
            agentVersion: "1.0.0");
        var request = new Request(
            content: Question,
            sessionId: SessionId,
            channel: new Channel(ChannelName),
            conversationId: ConversationId,
            operationSource: ServiceName);
        var clock = new ScenarioClock(timeProvider.GetUtcNow());

        using var baggage = new BaggageBuilder()
            .TenantId(options.TenantId)
            .AgentId(options.AgentId)
            .AgentName(AgentName)
            .AgentDescription(AgentDescription)
            .AgenticUserId(PublishingAgenticUserId)
            .AgenticUserEmail(PublishingAgenticUserEmail)
            .AgentBlueprintId(options.ClientId)
            .UserId(PublishingUserId)
            .UserName(PublishingUserName)
            .UserEmail(PublishingUserEmail)
            .UserClientIp(PublishingClientAddress)
            .ChannelName(ChannelName)
            .ConversationId(ConversationId)
            .SessionId(SessionId)
            .OperationSource(ServiceName)
            .Build();

        using var invoke = InvokeAgentScope.Start(
            request,
            new InvokeAgentScopeDetails(AgentEndpoint),
            agent,
            spanDetails: new SpanDetails(
                spanKind: ActivityKind.Internal,
                startTime: clock.Current));
        var parentContext = invoke.GetActivityContext();

        using (var firstInference = InferenceScope.Start(
            request,
            new InferenceCallDetails(
                InferenceOperationType.Chat,
                ModelName,
                "openai"),
            agent,
            spanDetails: new SpanDetails(
                parentContext: parentContext,
                startTime: clock.Current)))
        {
            firstInference.RecordInputMessages(
                [
                    "You are a precise weather assistant.",
                    Question,
                ]);

            await AdvanceAsync(delay, clock, FirstInferenceDuration, cancellationToken)
                .ConfigureAwait(false);

            firstInference.RecordInputTokens(24);
            firstInference.RecordOutputTokens(12);
            firstInference.RecordFinishReasons(["tool_call"]);
            firstInference.RecordOutputMessages(["I'll check the current Seattle weather."]);
            firstInference.SetEndTime(clock.Current);
        }

        using (var tool = ExecuteToolScope.Start(
            request,
            new ToolCallDetails(
                "get_weather",
                new Dictionary<string, object>
                {
                    ["city"] = "Seattle",
                    ["units"] = "fahrenheit",
                },
                toolCallId: "call-weather-001",
                description: "Gets the current weather for a city.",
                toolType: "function",
                endpoint: ToolEndpoint),
            agent,
            spanDetails: new SpanDetails(
                parentContext: parentContext,
                startTime: clock.Current)))
        {
            await AdvanceAsync(delay, clock, ToolDuration, cancellationToken)
                .ConfigureAwait(false);

            tool.RecordResponse(new Dictionary<string, object>
            {
                ["temperature"] = 62,
                ["condition"] = "partly cloudy",
            });
            tool.SetEndTime(clock.Current);
        }

        using (var finalInference = InferenceScope.Start(
            request,
            new InferenceCallDetails(
                InferenceOperationType.Chat,
                ModelName,
                "openai"),
            agent,
            spanDetails: new SpanDetails(
                parentContext: parentContext,
                startTime: clock.Current)))
        {
            finalInference.RecordInputMessages(
                [
                    Question,
                    ToolSummary,
                ]);

            await AdvanceAsync(delay, clock, FinalInferenceDuration, cancellationToken)
                .ConfigureAwait(false);

            finalInference.RecordInputTokens(36);
            finalInference.RecordOutputTokens(18);
            finalInference.RecordFinishReasons(["stop"]);
            finalInference.RecordOutputMessages([FinalAnswer]);
            finalInference.SetEndTime(clock.Current);
        }

        invoke.RecordResponse(FinalAnswer);
        invoke.SetEndTime(clock.Current);
    }

    private static async Task AdvanceAsync(
        Func<TimeSpan, CancellationToken, Task> delay,
        ScenarioClock clock,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await delay(duration, cancellationToken).ConfigureAwait(false);
        clock.Advance(duration);
    }

    private static Task SimulateDelayAsync(
        TimeSpan _,
        CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : Task.CompletedTask;

    private sealed class ScenarioClock(DateTimeOffset current)
    {
        internal DateTimeOffset Current { get; private set; } = current;

        internal void Advance(TimeSpan duration) => Current += duration;
    }

}
