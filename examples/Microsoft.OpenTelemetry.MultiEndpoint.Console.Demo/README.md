# Microsoft.OpenTelemetry Multi-Endpoint Console Demo

A minimal non-hosted console application that routes traces, logs, and metrics from one Azure Monitor exporter to two Application Insights resources.

The sample attaches the routing attributes directly to activity tags, log-record attributes, and metric measurement dimensions. It routes only the telemetry it emits itself; instrumentation enabled by the distro does not attach routing attributes on its own.

For the full feature guide, including how to route telemetry from a hosted application, see [Multi-Endpoint Routing](../../docs/multi-endpoint-routing.md).

## Run

Set `MULTIENDPOINT_ROUTE_CONNECTION_STRINGS` to two comma-separated Application Insights connection strings:

```powershell
$env:MULTIENDPOINT_ROUTE_CONNECTION_STRINGS = "<connectionstring1>,<connectionstring2>"
dotnet run --project .\Microsoft.OpenTelemetry.MultiEndpoint.Console.Demo.csproj
```

Multi-endpoint routing does not support Microsoft Entra ID authentication. Live Metrics, standard metrics, and performance counters are disabled because those process-wide signals do not have a single destination.
