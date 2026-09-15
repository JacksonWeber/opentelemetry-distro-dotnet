# Agent365 S2S Observability Sample

This .NET 8 console sample uses the Microsoft OpenTelemetry distro and Agent365
manual scope APIs to export a single S2S trace containing:

1. InvokeAgent
2. Inference that selects a tool
3. ExecuteTool
4. Inference that produces the final answer

It does not use Agents Framework, an LLM, or a real tool endpoint.

## Prerequisites

- .NET 8 SDK
- Blueprint application client ID and secret
- Microsoft Entra tenant ID
- An onboarded Agent365 agent app instance ID
- `Agent365.Observability.OtelWrite` application permission with admin consent

## Configure

Copy `appsettings.example.json` to `appsettings.json` and replace every
placeholder. `appsettings.json` is gitignored.

`Agent365:AgentId` is used both as the agent app instance identity during the
MSAL federated token exchange and as the agent identity on spans and the S2S
export route.

## Run

```powershell
dotnet run --project .\Microsoft.OpenTelemetry.Agent365.S2S.Demo.csproj
```

The console exporter prints all four spans. The Agent365 exporter prints an
HTTP success or failure status and the `x-ms-correlation-id` when returned.
Tokens and secrets are never printed. Each execution timestamps the sample
trace from the current UTC time.

## Store publishing attributes

The sample emits the required Invoke Agent, Inference, and Execute Tool scopes
and populates the required publishing attributes listed in the
[Agent365 observability validation guidance](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability?tabs=dotnet#validate-for-store-publishing).

Because this is a self-contained S2S demonstration with no incoming human or
agentic-user request, it uses clearly synthetic `example.invalid` identities,
documentation-only IP address `192.0.2.1`, and deterministic sample GUIDs for
the required user attributes. Production applications must replace these
values with authenticated request context and must not fabricate identities.

## Authentication flow

The sample uses MSAL.NET to:

1. Acquire an Azure AD token-exchange token for the blueprint app with the
   configured `AgentId` as the FMI path.
2. Use that token as the `AgentId` application client assertion.
3. Acquire an application token for the Agent365 observability `/.default`
   scope.
