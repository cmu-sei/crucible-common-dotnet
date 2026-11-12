// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Crucible.Common.Authentication.Claims;

public static class PermissionClaimsCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IPermissionClaimsCache"/>. A <see cref="TimeProvider"/> registered in the
    /// container is used for expiry when there is one.
    /// </summary>
    public static IServiceCollection AddPermissionClaimsCache(
        this IServiceCollection services,
        Action<PermissionClaimsCacheOptions>? configure = null)
    {
        var options = new PermissionClaimsCacheOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IPermissionClaimsCache>(serviceProvider =>
            new PermissionClaimsCache(options, serviceProvider.GetService<TimeProvider>()));

        return services;
    }
}
