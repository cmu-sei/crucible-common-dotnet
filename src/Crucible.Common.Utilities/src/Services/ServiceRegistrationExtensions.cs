// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Crucible.Common.Utilities.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Crucible.Common.Utilities;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddCrucibleUtilityServices(this IServiceCollection collection)
    {
        return collection
            .AddTransient<ISlugService, SlugService>();
    }
}
