# Package Swap

## Remove these packages from `.csproj`

```xml
<!-- A365 Observability packages -->
<PackageReference Include="Microsoft.Agents.A365.Observability.Runtime" />
<PackageReference Include="Microsoft.Agents.A365.Observability.Hosting" />
<PackageReference Include="Microsoft.Agents.A365.Observability.Extensions.SemanticKernel" />
<PackageReference Include="Microsoft.Agents.A365.Observability.Extensions.AgentFramework" />
<PackageReference Include="Microsoft.Agents.A365.Observability.Extensions.OpenAI" />

<!-- Individual OTel packages (now included in distro) -->
<PackageReference Include="OpenTelemetry.Extensions.Hosting" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
<PackageReference Include="OpenTelemetry.Exporter.Console" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
```

## Add this single package

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="<latest>" />
```

## Keep unchanged

Non-observability A365 packages stay:
```xml
<PackageReference Include="Microsoft.Agents.A365.Runtime" />
<PackageReference Include="Microsoft.Agents.A365.Notifications" />
<PackageReference Include="Microsoft.Agents.A365.Tooling.Extensions.SemanticKernel" />
```
