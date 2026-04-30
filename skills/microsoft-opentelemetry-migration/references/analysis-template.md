# Analysis Template

Scan the project and report findings:

## Package References

Search `.csproj` files for:
- `Microsoft.Agents.A365.Observability.Runtime` → must remove
- `Microsoft.Agents.A365.Observability.Hosting` → must remove
- `Microsoft.Agents.A365.Observability.Extensions.SemanticKernel` → must remove
- `Microsoft.Agents.A365.Observability.Extensions.AgentFramework` → must remove
- `Microsoft.Agents.A365.Observability.Extensions.OpenAI` → must remove
- `OpenTelemetry.Extensions.Hosting` → must remove (included in distro)
- `OpenTelemetry.Instrumentation.AspNetCore` → must remove
- `OpenTelemetry.Instrumentation.Http` → must remove
- `OpenTelemetry.Exporter.Console` → must remove
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` → must remove

## Code Patterns

Search `.cs` files for:
- `ConfigureOpenTelemetry()` → replace with `UseMicrosoftOpenTelemetry()`
- `AddA365Tracing` → replace with `UseMicrosoftOpenTelemetry()`
- `new Agent365ExporterOptions` → use `o.Agent365.Exporter.*` or DI cache
- `TokenStore` → delete class, use DI token cache
- `new Builder(` → replace with `UseMicrosoftOpenTelemetry()`
- `.WithSemanticKernel()` → auto-registered by distro
- `.WithAgentFramework()` → auto-registered by distro
- `.WithOpenAI()` → auto-registered by distro
- `ChatToolCallExtensions.Trace()` → use `ExecuteToolScope.Start()` directly
