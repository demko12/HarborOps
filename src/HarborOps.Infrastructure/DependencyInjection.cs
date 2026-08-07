using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHarborOpsInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<HarborOpsDbContext>(
            options => options.UseSqlServer(connectionString));
        services.AddScoped<DbContext>(
            serviceProvider => serviceProvider.GetRequiredService<HarborOpsDbContext>());
        services.AddIdentityModule()
            .AddEntityFrameworkStores<HarborOpsDbContext>();

        return services;
    }
}
