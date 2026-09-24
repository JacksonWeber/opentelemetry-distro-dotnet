using Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders;
using Microsoft.Agents.A365.Observability.Runtime.Etw;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;

var data = InvokeAgentDataBuilder.Build(
    new InvokeAgentScopeDetails(new Uri("https://example.com/agent")),
    new AgentDetails("agent-id", tenantId: "tenant-id"),
    "conversation-id");

var json = new EtwExportFormatter().FormatLogData(data.ToDictionary());
EtwEventSource.Log.LogJson(json);
