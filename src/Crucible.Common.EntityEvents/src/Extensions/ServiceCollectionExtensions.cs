// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

#nullable enable

using System;
using Crucible.Common.EntityEvents.Abstractions;
using Crucible.Common.EntityEvents.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Crucible.Common.EntityEvents.Extensions;

/// <summary>
/// Extension methods for registering entity event services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="EntityEventInterceptor"/> as a service.
    /// The interceptor should be added to your DbContext using AddInterceptors().
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddEntityEventInterceptor();
    /// services.AddPooledDbContextFactory&lt;MyContext&gt;((sp, builder) =&gt; builder
    ///     .AddInterceptors(sp.GetRequiredService&lt;EntityEventInterceptor&gt;())
    ///     .UseNpgsql(connectionString));
    /// </code>
    /// </example>
    public static IServiceCollection AddEntityEventInterceptor(this IServiceCollection services)
    {
        return services.AddTransient<EntityEventInterceptor>();
    }

    /// <summary>
    /// Registers a pooled DbContext factory with entity event support.
    /// Automatically configures the interceptor and scoped DbContext with ServiceProvider.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type (must inherit from EventPublishingDbContext).</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure the DbContext options (e.g., UseNpgsql, UseInMemoryDatabase).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddEventPublishingDbContextFactory&lt;MyContext&gt;((sp, builder) =&gt;
    ///     builder.UseNpgsql(connectionString));
    /// </code>
    /// </example>
    public static IServiceCollection AddEventPublishingDbContextFactory<TContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : EventPublishingDbContext
    {
        services.AddEntityEventInterceptor();
        services.AddPooledDbContextFactory<TContext>((sp, builder) =>
        {
            builder.AddInterceptors(sp.GetRequiredService<EntityEventInterceptor>());
            configure(sp, builder);
        });
        services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<TContext>>();
            var context = factory.CreateDbContext();
            context.ServiceProvider = sp;
            return context;
        });
        return services;
    }
}
