// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Crucible.Common.Authentication.Claims;

/// <summary>Registers <see cref="IPermissionClaimsCache"/> with a service collection.</summary>
public static class PermissionClaimsCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IPermissionClaimsCache"/>, configured from
    /// <see cref="IOptions{PermissionClaimsCacheOptions}"/>. A <see cref="TimeProvider"/> registered in the container is
    /// used for expiry when there is one.
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

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IPermissionClaimsCache>(serviceProvider =>
            new PermissionClaimsCache(
                serviceProvider.GetRequiredService<IOptions<PermissionClaimsCacheOptions>>().Value,
                serviceProvider.GetService<TimeProvider>()));

        return services;
    }
}
