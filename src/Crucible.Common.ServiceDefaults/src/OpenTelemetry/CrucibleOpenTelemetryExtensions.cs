// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Crucible.Common.ServiceDefaults.OpenTelemetry;

public static class CrucibleOpenTelemetryExtensions
{
    // wrapper for apps still using IHostBuilder (most apps created pre .NET Core 8)
    public static ILoggingBuilder AddCrucibleOpenTelemetryLogging(this ILoggingBuilder logging)
    {
        AddLogging(logging);
        return logging;
    }

    // wrapper for apps still using IHostBuilder (most apps created pre .NET Core 8)
    public static IServiceCollection AddCrucibleOpenTelemetryServices(this IServiceCollection services, IHostEnvironment hostEnvironment, IConfigurationManager configuration, Action<CrucibleOpenTelemetryOptions>? optionsBuilder = null)
    {
        var options = BuildOptions(optionsBuilder);

        AddServices(services, hostEnvironment, options);
        AddExporters(services, configuration["OTEL_EXPORTER_OTLP_ENDPOINT"], options);

        return services;
    }

    public static IHostApplicationBuilder AddCrucibleOpenTelemetryServiceDefaults(this IHostApplicationBuilder builder, Action<CrucibleOpenTelemetryOptions>? optionsBuilder = null)
    {
        var options = BuildOptions(optionsBuilder);

        builder.ConfigureOpenTelemetry(options);
        // builder.AddDefaultHealthChecks();
        // builder.Services.AddServiceDiscovery();

        // builder.Services.ConfigureHttpClientDefaults(http =>
        // {
        //     // Turn on resilience by default
        //     http.AddStandardResilienceHandler();

        //     // Turn on service discovery by default
        //     http.UseServiceDiscovery();
        // });

        return builder;
    }

    private static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder, CrucibleOpenTelemetryOptions options)
    {
        // configure logging
        AddLogging(builder.Logging);
        AddServices(builder.Services, builder.Environment, options);
        AddExporters(builder.Services, builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"], options);

        return builder;
    }

    private static void AddLogging(this ILoggingBuilder logging)
    {
        logging.AddOpenTelemetry(x =>
        {
            x.IncludeScopes = true;
            x.IncludeFormattedMessage = true;

            // Not doing this yet, but protects against "unknown_service" in  traces/metrics, maybe?
            // x.SetResourceBuilder
            // (
            //     ResourceBuilder
            //         .CreateDefault()
            //         .AddService
            //         (
            //             serviceName: builder.Environment.ApplicationName,
            //             serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString(),
            //             serviceInstanceId: Environment.MachineName
            //         )
            // );
        });
    }

    private static void AddServices(this IServiceCollection services, IHostEnvironment env, CrucibleOpenTelemetryOptions options)
    {
        services
            .AddOpenTelemetry()
            .WithMetrics(x =>
            {
                x.AddRuntimeInstrumentation();

                if (options.IncludeDefaultMeters)
                {
                    x.AddMeter
                    (
                        "Microsoft.AspNetCore.Hosting",
                        "Microsoft.AspNetCore.Server.Kestrel",
                        "Microsoft.EntityFrameworkCore",
                        "System.Net.Http"
                    );
                }

                if (options.CustomMeters.Any())
                {
                    x.AddMeter([.. options.CustomMeters]);
                }
            })
            .WithTracing(x =>
            {
                if (options.IncludeDefaultActivitySources)
                {
                    x.AddSource("Microsoft.AspNetCore");
                    x.AddSource("Microsoft.EntityFrameworkCore");
                    x.AddSource("System.Net.Http");
                }

                if (options.CustomActivitySources.Any())
                {
                    x.AddSource([.. options.CustomActivitySources]);
                }

                if (env.IsDevelopment())
                {
                    x.SetSampler<AlwaysOnSampler>();
                }

                x
                    // record structured logs for traces
                    .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();
            });
    }

    private static void AddExporters(this IServiceCollection services, string? otelExporterEndpoint, CrucibleOpenTelemetryOptions options)
    {
        var isOtlpEndpointConfigured = !string.IsNullOrWhiteSpace(otelExporterEndpoint);

        services.Configure<OpenTelemetryLoggerOptions>(logging =>
        {
            if (isOtlpEndpointConfigured)
            {
                logging.AddOtlpExporter();
            }

            if (options.AddConsoleExporter)
            {
                logging.AddConsoleExporter();
            }
        });

        services.ConfigureOpenTelemetryMeterProvider(metrics =>
        {
            if (isOtlpEndpointConfigured)
            {
                metrics.AddOtlpExporter();
            }

            if (options.AddConsoleExporter)
            {
                metrics.AddConsoleExporter();
            }

            if (options.AddPrometheusExporter)
            {
                metrics.AddPrometheusExporter();
            }
        });

        services.ConfigureOpenTelemetryTracerProvider(tracing =>
        {
            if (isOtlpEndpointConfigured)
            {
                tracing.AddOtlpExporter();
            }

            if (options.AddConsoleExporter)
            {
                tracing.AddConsoleExporter();
            }
        });
    }

    private static CrucibleOpenTelemetryOptions BuildOptions(Action<CrucibleOpenTelemetryOptions>? optionsBuilder = null)
    {
        var options = new CrucibleOpenTelemetryOptions();

        if (optionsBuilder is not null)
        {
            optionsBuilder(options);
        }

        return options;
    }
}
