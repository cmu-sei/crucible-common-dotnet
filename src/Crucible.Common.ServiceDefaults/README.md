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
- `IncludeDefaultMeters` – toggle the default meters described below.

## What is enabled by default

- Structured logging with OTLP or console exporters.
- Metrics from the .NET runtime, the hosting stack, ASP.NET Core, HttpClient, Entity Framework Core, and process CPU/memory usage.
- Traces for ASP.NET Core, HttpClient, and Entity Framework Core operations.

Adjust the options or supply additional instrumentation packages to tailor telemetry for your service.

### Default metrics and meters

The metrics pipeline wires several OpenTelemetry instrumentations:

- [`AddRuntimeInstrumentation`](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.Runtime) publishes `System.Runtime` metrics for garbage collection, thread pool saturation, and exception rates.
- [`AddProcessInstrumentation`](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.Process) reports process CPU usage, working set, and other host-level resource counters.
- [`AddHttpClientInstrumentation`](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.Http) records dependency call duration, size, and error metrics for outgoing HTTP requests.
- [`AddAspNetCoreInstrumentation`](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.AspNetCore) captures request duration, size, and other server metrics for incoming ASP.NET Core traffic.

When `IncludeDefaultMeters` remains `true`, the provider also subscribes to these built-in meters:

- [`Microsoft.AspNetCore.Hosting`](https://learn.microsoft.com/en-us/aspnet/core/diagnostics/metrics) – high-level hosting metrics such as request queue depth and current requests.
- [`Microsoft.AspNetCore.Server.Kestrel`](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/diagnostics) – Kestrel transport metrics for connections, TLS handshakes, and request/response throughput.
- [`Microsoft.EntityFrameworkCore`](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/metrics) – database command execution counts and timings.
- [`System.Net.Http`](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/telemetry/metrics) – outgoing request rate, duration, and failure counts from the .NET networking stack.
- [`System.Net.NameResolution`](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/telemetry/metrics) – DNS cache and lookup timings surfaced by the .NET networking stack
