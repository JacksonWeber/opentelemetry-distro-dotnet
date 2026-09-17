# Microsoft.OpenTelemetry Multi-Endpoint Console Demo

A minimal non-hosted console application that routes traces, logs, and metrics from one Azure Monitor exporter to two Application Insights resources.

Multi-endpoint mode requires these routing attributes on every telemetry item:

- `microsoft.instrumentation_key`
- `microsoft.ingestion_endpoint`

The optional `microsoft.multi_endpoint_cloud_role` attribute sets `ai.cloud.role`. If it is missing or invalid, the exporter uses `unknown_service`.

The sample adds all three attributes directly to activity tags, log-record attributes, and metric measurement dimensions. Telemetry missing either required routing attribute is dropped in multi-endpoint mode.

## Run

Set `MULTIENDPOINT_ROUTE_CONNECTION_STRINGS` to two comma-separated Application Insights connection strings:

```powershell
$env:MULTIENDPOINT_ROUTE_CONNECTION_STRINGS = "<connectionstring1>,<connectionstring2>"
dotnet run --project .\Microsoft.OpenTelemetry.MultiEndpoint.Console.Demo.csproj
```

Multi-endpoint routing does not support Microsoft Entra ID authentication. Live Metrics, standard metrics, and performance counters are disabled because those process-wide signals do not have a single destination.
