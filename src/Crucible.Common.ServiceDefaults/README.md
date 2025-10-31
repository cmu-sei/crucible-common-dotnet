# Crucible.Common.ServiceDefaults

Default service configuration for Crucible API apps. Right now, this is mostly just configuration for [OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel), but may involve more stuff as we get going.

## Quick start

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddCrucibleOpenTelemetryServiceDefaults(options =>
{
    options.AddConsoleExporter = builder.Environment.IsDevelopment();
    options.CustomActivitySources = ["Player.Api"];
});
```

For `Startup`-style apps, call `services.AddCrucibleOpenTelemetryServices(env, Configuration);` during service registration.

## Required environment variables

- `OTEL_EXPORTER_OTLP_ENDPOINT`
  Endpoint for the OTLP collector that receives logs, metrics, and traces. Use an `http://` or `https://` URL such as `http://otel-collector:4317`. If this is omitted the SDK still wires instrumentation, but nothing is sent to an external collector unless an optional exporter is enabled.

## Optional environment variables

The OpenTelemetry SDK honors additional standard variables (for example `OTEL_RESOURCE_ATTRIBUTES`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_EXPORTER_OTLP_TIMEOUT`), although this library does not set defaults for them. Configure these when you need to pass authentication headers, tweak timeouts, or add extra resource attributes.

## Optional library settings

Pass an `Action<CrucibleOpenTelemetryOptions>` when calling `AddCrucibleOpenTelemetryServiceDefaults` or `AddCrucibleOpenTelemetryServices` to customize:

- `AddAlwaysOnTracingSampler` – force sampling of every trace span.
- `AddConsoleExporter` – write telemetry to console for local debugging.
- `AddPrometheusExporter` – surface metrics through the built-in Prometheus endpoint.
- `CustomActivitySources` – register additional activity sources to trace.
- `CustomMeters` – register additional meters to collect metrics from.
- `IncludeDefaultActivitySources` – toggle the built-in sources (`Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`, `System.Net.Http`).
- `IncludeDefaultMeters` – toggle the default meters (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`, `Microsoft.EntityFrameworkCore`, `System.Net.Http`).

## What is enabled by default

- Structured logging with OTLP or console exporters.
- Metrics from the .NET runtime, the hosting stack, ASP.NET Core, HttpClient, Entity Framework Core, and process CPU/memory usage.
- Traces for ASP.NET Core, HttpClient, and Entity Framework Core operations.

Adjust the options or supply additional instrumentation packages to tailor telemetry for your service.
