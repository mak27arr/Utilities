namespace Utilities.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Utilities.Core.Application.Abstractions;

public static class PersistenceRegistration
{
    public static IServiceCollection AddUtilitiesPersistence(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton<HomeAddressCommandTimingInterceptor>();
        services.AddDbContext<UtilitiesDbContext>((serviceProvider, options) => options
            .AddInterceptors(serviceProvider.GetRequiredService<HomeAddressCommandTimingInterceptor>())
            .UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IUtilitiesStore, EfUtilitiesStore>();
        services.AddScoped<IDatabaseConnectionWarmup, EfDatabaseConnectionWarmup>();
        return services;
    }
}
