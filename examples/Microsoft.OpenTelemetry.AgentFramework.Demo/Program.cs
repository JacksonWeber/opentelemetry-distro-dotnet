// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

// One-line distro setup — MAF span capture (UseAgentFramework) is enabled by default.
// Console exporter lets you see traces in the terminal without Azure Monitor.
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Console;
});

var app = builder.Build();

// --- Create MAF agent -------------------------------------------------------

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("Set the AZURE_OPENAI_ENDPOINT environment variable.");

var deploymentName =
    Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";

var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set the AZURE_OPENAI_API_KEY environment variable.");

[Description("Get the weather for a given location.")]
static string GetWeather([Description("The location to get the weather for.")] string location)
    => $"The weather in {location} is cloudy with a high of 15°C.";

// Instrument the underlying chat client with the standard Microsoft.Extensions.AI
// `.UseOpenTelemetry()` (emits the `chat` inference span), then instrument the agent layer
// (emits `invoke_agent` + `execute_tool`). The distro's UseAgentFramework() collects all three.
AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(configure: cfg => cfg.EnableSensitiveData = true)
    .Build()
    .AsAIAgent(
        instructions: "You are a helpful assistant that provides concise responses.",
        tools: [AIFunctionFactory.Create(GetWeather)])
    .AsBuilder()
    .UseOpenTelemetry(configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

// --- Endpoints ---------------------------------------------------------------

app.MapGet("/", () => "Microsoft Agent Framework Demo — POST /chat with a JSON body { \"message\": \"...\" }");

app.MapPost("/chat", async (ChatRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required" });
    }

    var response = await agent.RunAsync(request.Message);

    return Results.Ok(new
    {
        reply = response,
        traceId = Activity.Current?.TraceId.ToString()
    });
});

app.Run();

record ChatRequest(string Message);
