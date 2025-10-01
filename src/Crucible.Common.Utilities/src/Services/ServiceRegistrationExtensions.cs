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
