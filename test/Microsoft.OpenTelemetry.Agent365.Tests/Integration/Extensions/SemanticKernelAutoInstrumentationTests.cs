// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using OpenTelemetry;
using OpenTelemetry.Trace;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Integration.Extensions;

/// <summary>
/// Integration tests verifying Semantic Kernel auto-instrumentation, via the distro's
/// public <c>UseMicrosoftOpenTelemetry()</c> entry point, emits all 3 span types:
/// invoke_agent, execute_tool, and chat (inference).
/// SK self-instruments every layer on the <c>Microsoft.SemanticKernel*</c> source, which the distro
/// collects and the <c>SemanticKernelSpanProcessor</c> enriches (mapping messages, normalizing the
/// chat operation name).
/// Makes real API calls against Azure OpenAI.
/// Requires: AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT env vars.
/// </summary>
[TestClass]
public class SemanticKernelAutoInstrumentationTests
{
    private static string? Endpoint => Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
    private static string? ApiKey => Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
    private static string? Deployment => Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");

    private static bool HasCredentials =>
        !string.IsNullOrEmpty(Endpoint) &&
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(Deployment);

    private ConcurrentQueue<Activity> _exportedActivities = new();
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        _exportedActivities = new ConcurrentQueue<Activity>();

        var services = new ServiceCollection();
        services.AddLogging();

        // Use the distro's public entry point exactly as a consumer would.
        // ExportTarget.Console enables tracing + source registration without requiring an
        // Agent365 token resolver or network export; we capture the enriched spans ourselves.
        services.AddOpenTelemetry()
            .UseMicrosoftOpenTelemetry(o => o.Exporters = ExportTarget.Console)
            .WithTracing(tracing => tracing
                .AddProcessor(new SimpleActivityExportProcessor(new ConcurrentQueueActivityExporter(_exportedActivities))));

        _serviceProvider = services.BuildServiceProvider();
        _serviceProvider.GetService<TracerProvider>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    /// <summary>
    /// Verifies that invoking a SK ChatCompletionAgent with a tool emits all 3 span types:
    /// invoke_agent, execute_tool, and chat (inference) — all in the same trace.
    /// </summary>
    [TestMethod]
    public async Task AgentWithTool_EmitsAllThreeSpanTypes_InvokeAgent_ExecuteTool_Inference()
    {
        SkipIfNoCredentials();

        var agent = CreateAgentWithWeatherTool();

        await foreach (var _ in agent.InvokeAsync("What is the weather in Seattle right now?"))
        {
        }

        ForceFlush();
        DumpAllActivities("SK AgentWithTool_AllThreeSpans");

        var invokeAgentSpans = SpansWithOperation("invoke_agent");
        invokeAgentSpans.Should().NotBeEmpty("SK ChatCompletionAgent should emit an invoke_agent span");

        var executeToolSpans = SpansWithOperation("execute_tool");
        executeToolSpans.Should().NotBeEmpty("SK should emit execute_tool span(s) when a kernel function is auto-invoked");

        var chatSpans = ChatSpans();
        chatSpans.Should().NotBeEmpty("SK should emit chat (inference) span(s) for LLM calls");

        // Parent-child: execute_tool and chat should share the trace with invoke_agent.
        var traceId = invokeAgentSpans.First().TraceId;

        executeToolSpans.Should().AllSatisfy(span =>
            span.TraceId.Should().Be(traceId, "execute_tool spans should share trace with invoke_agent"));

        chatSpans.Should().AllSatisfy(span =>
            span.TraceId.Should().Be(traceId, "chat spans should share trace with invoke_agent"));
    }

    /// <summary>
    /// Verifies a SK agent invocation without tools emits invoke_agent and chat but no execute_tool.
    /// </summary>
    [TestMethod]
    public async Task AgentWithoutTools_EmitsInvokeAgentAndInference_NoExecuteTool()
    {
        SkipIfNoCredentials();

        var agent = CreateAgentWithoutTools();

        await foreach (var _ in agent.InvokeAsync("What is the capital of France?"))
        {
        }

        ForceFlush();
        DumpAllActivities("SK AgentWithoutTools");

        SpansWithOperation("invoke_agent").Should().NotBeEmpty("agent invocation should emit invoke_agent");
        ChatSpans().Should().NotBeEmpty("agent invocation should emit a chat (inference) span");
        SpansWithOperation("execute_tool").Should().BeEmpty("no tool should be invoked without tools registered");
    }

    /// <summary>
    /// Verifies the chat (inference) span carries OTel GenAI structured input/output messages
    /// after the distro's SemanticKernelSpanProcessor maps them.
    /// </summary>
    [TestMethod]
    public async Task InferenceSpan_HasStructuredMessages()
    {
        SkipIfNoCredentials();

        var agent = CreateAgentWithoutTools();

        await foreach (var _ in agent.InvokeAsync("Say hello in one short sentence."))
        {
        }

        ForceFlush();

        var chatSpan = ChatSpans().FirstOrDefault();
        chatSpan.Should().NotBeNull("a chat inference span should be emitted");

        var input = chatSpan!.GetTagItem("gen_ai.input.messages") as string;
        var output = chatSpan.GetTagItem("gen_ai.output.messages") as string;

        input.Should().NotBeNullOrEmpty("chat span should record input messages");
        input!.TrimStart().Should().StartWith("[", "input messages should be a JSON array per OTel spec");
        output.Should().NotBeNullOrEmpty("chat span should record output messages");
        output!.TrimStart().Should().StartWith("[", "output messages should be a JSON array per OTel spec");
    }

    #region Helpers

    private sealed class WeatherPlugin
    {
        [KernelFunction]
        [Description("Get the current weather for a given location.")]
        public string GetWeather([Description("The city name to check weather for.")] string location)
            => $"The weather in {location} is sunny with a high of 22C.";
    }

    private static ChatCompletionAgent CreateAgentWithWeatherTool()
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddAzureOpenAIChatCompletion(Deployment!, Endpoint!, ApiKey!);
        kernelBuilder.Plugins.AddFromObject(new WeatherPlugin(), "Weather");
        var kernel = kernelBuilder.Build();

        return new ChatCompletionAgent
        {
            Name = "WeatherAgent",
            Instructions = "You are a helpful weather assistant. Always use the GetWeather function when asked about weather.",
            Kernel = kernel,
            Arguments = new KernelArguments(new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Required(),
            }),
        };
    }

    private static ChatCompletionAgent CreateAgentWithoutTools()
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddAzureOpenAIChatCompletion(Deployment!, Endpoint!, ApiKey!);
        var kernel = kernelBuilder.Build();

        return new ChatCompletionAgent
        {
            Name = "Assistant",
            Instructions = "You are a helpful assistant. Reply in one sentence.",
            Kernel = kernel,
        };
    }

    private List<Activity> SpansWithOperation(string operation) =>
        _exportedActivities
            .Where(a => string.Equals(a.GetTagItem("gen_ai.operation.name") as string, operation, StringComparison.OrdinalIgnoreCase))
            .ToList();

    // The SemanticKernelSpanProcessor normalizes the SK "chat.completions"/"chat" operation to
    // InferenceOperationType.Chat.ToString() ("Chat"), so match case-insensitively.
    private List<Activity> ChatSpans() => SpansWithOperation("chat");

    private void ForceFlush()
    {
        var tracerProvider = _serviceProvider?.GetService<TracerProvider>();
        tracerProvider?.ForceFlush();
    }

    private void DumpAllActivities(string label)
    {
        Console.WriteLine($"\n=== {label}: All captured activities ({_exportedActivities.Count}) ===");
        foreach (var act in _exportedActivities)
        {
            var op = act.GetTagItem("gen_ai.operation.name");
            var parent = act.ParentId ?? "(root)";
            Console.WriteLine($"  [{act.Source.Name}] {act.DisplayName} | op={op} | parent={parent}");
        }
        Console.WriteLine("===\n");
    }

    private static void SkipIfNoCredentials()
    {
        if (!HasCredentials)
        {
            Assert.Inconclusive(
                "Skipped: set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT env vars to run.");
        }
    }

    /// <summary>
    /// Exporter that enqueues activities into a thread-safe <see cref="ConcurrentQueue{T}"/> at
    /// export time. Span export/end callbacks can occur on different threads, so a thread-safe
    /// collection avoids races (and enumeration faults while <c>DumpAllActivities</c> iterates).
    /// </summary>
    private sealed class ConcurrentQueueActivityExporter : BaseExporter<Activity>
    {
        private readonly ConcurrentQueue<Activity> _activities;

        public ConcurrentQueueActivityExporter(ConcurrentQueue<Activity> activities) => _activities = activities;

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                _activities.Enqueue(activity);
            }

            return ExportResult.Success;
        }
    }

    #endregion
}
