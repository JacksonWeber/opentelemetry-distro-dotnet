# Validation Checklist

After migration, verify:

## Build

- [ ] `dotnet build` succeeds with 0 errors
- [ ] No references to removed packages in `.csproj`
- [ ] No references to removed APIs (`ConfigureOpenTelemetry`, `AddA365Tracing`, `TokenStore`, `Builder`)

## Runtime

- [ ] Agent starts and responds to messages
- [ ] Console exporter shows spans (add `ExportTarget.Console` temporarily)
- [ ] Spans have `gen_ai.agent.id` and `microsoft.tenant.id` attributes
- [ ] `telemetry.sdk.name` attribute appears on spans (value depends on exporter configuration)
- [ ] No HTTP/ASP.NET infrastructure spans in A365-only mode (expected)

## Token flow

- [ ] `RegisterObservability()` is called on each turn
- [ ] A365 exporter doesn't log token errors
- [ ] `TokenStore.cs` file is deleted

## Removed files

- [ ] `TokenStore.cs` — deleted
- [ ] `ConfigureOpenTelemetry()` extension method file — deleted (if it existed)

## Preserved functionality

- [ ] Manual scopes (`InvokeAgentScope`, `InferenceScope`, `ExecuteToolScope`) still work
- [ ] BaggageBuilder still sets context
- [ ] Middleware pipeline unchanged
- [ ] Agent behavior unchanged
