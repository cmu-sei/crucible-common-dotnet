// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Crucible.Common.Authentication.Claims;

/// <summary>Registers <see cref="IPermissionClaimsCache"/> with a service collection.</summary>
public static class PermissionClaimsCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IPermissionClaimsCache"/>, configured from
    /// <see cref="IOptions{PermissionClaimsCacheOptions}"/>, and adds <c>HybridCache</c> if the app has not already
    /// registered one (an app that registered its own, with a distributed level for example, keeps it).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Adds to the options' configuration, as <c>services.Configure&lt;PermissionClaimsCacheOptions&gt;(...)</c> does,
    /// so it can be combined with configuration binding, and every call's <paramref name="configure"/> applies, in
    /// order. The options are read once, when the cache is first resolved.
    /// </param>
    public static IServiceCollection AddPermissionClaimsCache(
        this IServiceCollection services,
        Action<PermissionClaimsCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<PermissionClaimsCacheOptions>();
        services.AddHybridCache();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IPermissionClaimsCache>(serviceProvider =>
            new PermissionClaimsCache(
                serviceProvider.GetRequiredService<HybridCache>(),
                serviceProvider.GetRequiredService<IOptions<PermissionClaimsCacheOptions>>().Value));

        return services;
    }
}
