// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Crucible.Common.ServiceDefaults.OpenTelemetry;

namespace Crucible.Common.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Call to configure default services.
    ///
    /// NOTE: This function is exposed primarily for apps created before .NET Core 8 that bootstrap with IHostBuilder rather than the newer IHostApplicationBuilder.
    /// If your app uses IHostApplicationBuilder, you shouldn't need to call this function directly.
    /// </summary>
    /// <param name="services">Your app's service collection.</param>
    /// <param name="hostEnvironment">The hosting environment in which your app is starting up.</param>
    /// <param name="configuration">Your app's configuration.</param>
    /// <param name="openTelemetryOptions">A builder used to customize OpenTelemetry configuration.</param>
    /// <returns></returns>
    public static IServiceCollection AddServiceDefaults(
        this IServiceCollection services,
        IHostEnvironment hostEnvironment,
        IConfiguration configuration,
        Action<OpenTelemetryOptions>? openTelemetryOptions = null)
    {
        services.AddOpenTelemetryServiceDefaults(hostEnvironment, configuration, openTelemetryOptions);
        return services;
    }

    /// <summary>
    /// Call to configure default services.
    /// </summary>
    /// <param name="builder">Your app's </param>
    /// <param name="openTelemetryOptions">A builder used to customize OpenTelemetry configuration.</param>
    /// <returns></returns>
    public static IHostApplicationBuilder AddServiceDefaults(
        this IHostApplicationBuilder builder,
        Action<OpenTelemetryOptions>? openTelemetryOptions = null)
    {
        builder.AddOpenTelemetryServiceDefaults(openTelemetryOptions);
        return builder;
    }
}
