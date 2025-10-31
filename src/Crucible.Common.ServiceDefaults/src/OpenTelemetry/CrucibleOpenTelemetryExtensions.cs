// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Crucible.Common.ServiceDefaults.OpenTelemetry;

public static class CrucibleOpenTelemetryExtensions
{
    /// <summary>
    /// Call to configure default configuration for OpenTelemetry-enhanced logging.
    ///
    /// NOTE: This function is exposed primarily for apps created before .NET Core 8 that bootstrap with IHostBuilder rather than the newer IHostApplicationBuilder.
    /// If your app uses IHostApplicationBuilder, you shouldn't need to call this function directly.
    /// </summary>
    /// <param name="logging"></param>
    /// <returns></returns>
    public static ILoggingBuilder AddCrucibleOpenTelemetryLogging(this ILoggingBuilder logging)
    {
        AddLogging(logging, ResolveServiceIdentity());
        return logging;
    }

    /// <summary>
    /// Call to configure default OpenTelemetry services. Customizable with the <cref>optionsBuilder</cref> parameter. See its properties for details.
    ///
    /// NOTE: This function is exposed primarily for apps created before .NET Core 8 that bootstrap with IHostBuilder rather than the newer IHostApplicationBuilder.
    /// If your app uses IHostApplicationBuilder, you shouldn't need to call this function directly.
    /// </summary>
    /// <param name="services">Your app's service collection.</param>
    /// <param name="hostEnvironment">The hosting environment in which your app is starting up.</param>
    /// <param name="configuration">Your app's configuration.</param>
    /// <param name="optionsBuilder">A builder used to customize OpenTelemetry configuration.</param>
    /// <returns></returns>
    public static IServiceCollection AddCrucibleOpenTelemetryServices(this IServiceCollection services, IHostEnvironment hostEnvironment, IConfiguration configuration, Action<CrucibleOpenTelemetryOptions>? optionsBuilder = null)
    {
        var options = BuildOptions(optionsBuilder);
        var identity = ResolveServiceIdentity(hostEnvironment);

        AddServices(services, options, identity);
        AddExporters(services, configuration["OTEL_EXPORTER_OTLP_ENDPOINT"], options);

        return services;
    }

    /// <summary>
    /// Add default service and logging configuration for OpenTelemetry. Customizable with the <cref>optionsBuilder</cref> parameter. See its properties for details.
    /// </summary>
    /// <param name="builder">Your app's </param>
    /// <param name="optionsBuilder"></param>
    /// <returns></returns>
    public static IHostApplicationBuilder AddCrucibleOpenTelemetryServiceDefaults(this IHostApplicationBuilder builder, Action<CrucibleOpenTelemetryOptions>? optionsBuilder = null)
    {
        var options = BuildOptions(optionsBuilder);
        var identity = ResolveServiceIdentity(builder.Environment);

        builder.ConfigureOpenTelemetry(options, identity);
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

    private static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder, CrucibleOpenTelemetryOptions options, ServiceIdentity identity)
    {
        AddServices(builder.Services, options, identity);
        AddExporters(builder.Services, builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"], options);

        return builder;
    }

    private static void AddLogging(this ILoggingBuilder logging, ServiceIdentity identity)
    {
        logging.AddOpenTelemetry(x =>
        {
            x.IncludeScopes = true;
            x.IncludeFormattedMessage = true;
            x.ParseStateValues = true;
            x.SetResourceBuilder(
                ResourceBuilder
                    .CreateDefault()
                    .AddService(
                        serviceName: identity.ServiceName,
                        serviceVersion: identity.ServiceVersion,
                        serviceInstanceId: identity.ServiceInstanceId));
        });
    }

    private static void AddServices(this IServiceCollection services, CrucibleOpenTelemetryOptions options, ServiceIdentity identity)
    {
        services.AddLogging(logging => AddLogging(logging, identity));
        services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource.AddService(
                    serviceName: identity.ServiceName,
                    serviceVersion: identity.ServiceVersion,
                    serviceInstanceId: identity.ServiceInstanceId))
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

                if (options.AddAlwaysOnTracingSampler)
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

    private static ServiceIdentity ResolveServiceIdentity(IHostEnvironment? environment = null)
    {
        var assembly = Assembly.GetEntryAssembly();
        var serviceName = !string.IsNullOrWhiteSpace(environment?.ApplicationName)
            ? environment!.ApplicationName
            : assembly?.GetName().Name ?? AppDomain.CurrentDomain.FriendlyName;

        var serviceVersion = assembly?.GetName().Version?.ToString();
        var serviceInstanceId = Environment.MachineName;

        return new ServiceIdentity(serviceName, serviceVersion, serviceInstanceId);
    }

    private readonly record struct ServiceIdentity(string ServiceName, string? ServiceVersion, string ServiceInstanceId);
}
