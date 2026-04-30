# Troubleshooting

## Spans not appearing in Defender

1. Check `gen_ai.agent.id` and `microsoft.tenant.id` on spans — missing identity causes silent drop
2. Verify `BaggageBuilder` is set up before span creation
3. Enable Console exporter to see spans locally: `o.Exporters |= ExportTarget.Console`

## Token resolver returns null

- A365 exporter silently skips export when token is null
- Verify `RegisterObservability()` is called on each turn
- Check auth handler name matches appsettings config

## No spans at all

- Verify `UseMicrosoftOpenTelemetry()` is called
- Check `ExportTarget` includes at least one target
- For custom ActivitySources, verify `.AddSource("name")` is registered

## HTTP 401/403 from A365 endpoint

- 401: Token audience mismatch or expired token
- 403: Missing `Agent365.Observability.OtelWrite` permission or missing license

## Console shows infrastructure spans only (no agent spans)

- Agent Framework: verify `UseOpenTelemetry()` is called on the agent/chat client
- Semantic Kernel: verify `EnableSemanticKernelInstrumentation` is not set to `false`

## Metrics/logs not in Console in A365-only mode

- A365-only mode suppresses Console metrics/logs by default. Override with `o.Instrumentation.EnableMetrics = true` for metrics and `o.Instrumentation.EnableLogging = true` for logs
