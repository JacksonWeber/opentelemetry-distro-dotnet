---
name: microsoft-opentelemetry-migration
description: 'Migrate from A365 Observability SDK to Microsoft.OpenTelemetry distro. Use when converting an existing agent that uses Microsoft.Agents.A365.Observability packages to the unified distro. Covers package swap, API mapping, Program.cs rewrite, token resolver migration, and validation.'
---

# A365 SDK → Microsoft.OpenTelemetry Distro Migration

## When to Use

- User asks to "migrate from A365 SDK" or "switch to the distro"
- Project references `Microsoft.Agents.A365.Observability.*` packages
- User mentions `ConfigureOpenTelemetry()`, `AddA365Tracing()`, `Builder` class, or `TokenStore`

## Procedure

### 1. Analyze Current State

Scan the project for A365 SDK patterns. See [analysis template](./references/analysis-template.md).

Look for:
- `Microsoft.Agents.A365.Observability.*` package references
- `builder.ConfigureOpenTelemetry()`
- `builder.AddA365Tracing(config => { ... })`
- `Agent365ExporterOptions` / `TokenStore`
- `Builder` class (fluent API from A365 SDK)

### 2. Swap Packages

See [package swap](./references/package-swap.md).

Remove A365 observability + individual OTel packages. Add `Microsoft.OpenTelemetry`.

### 3. Rewrite Program.cs

See [Program.cs migration](./references/program-cs-migration.md).

Replace `ConfigureOpenTelemetry()` + `AddA365Tracing()` with `UseMicrosoftOpenTelemetry()`.

### 4. Migrate Token Resolver

See [token resolver migration](./references/token-resolver-migration.md).

Replace `TokenStore` pattern with DI token cache or custom resolver.

### 5. Migrate Baggage / Middleware

See [baggage middleware migration](./references/baggage-middleware-migration.md).

### 6. Handle API Differences

See [API differences](./references/api-differences.md).

### 7. Migrate Auto-Instrumentation Config

See [auto-instrumentation migration](./references/auto-instrumentation-migration.md).

### 8. Validate

See [validation checklist](./references/validation-checklist.md).

- Build succeeds
- No references to removed APIs
- Console exporter shows spans with identity attributes
- Agent responds correctly
