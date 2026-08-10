using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Documents;
using HarborOps.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.IntegrationTests;

public sealed class HarborOpsApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTime FixedUtcNow = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _databaseName = $"HarborOpsIntegrationTests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContext>();
            services.RemoveAll<HarborOpsDbContext>();
            services.RemoveAll<DbContextOptions<HarborOpsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<HarborOpsDbContext>>();

            services.AddDbContext<HarborOpsDbContext>(
                options => options.UseInMemoryDatabase(_databaseName));
            services.AddScoped<DbContext>(
                serviceProvider => serviceProvider.GetRequiredService<HarborOpsDbContext>());

            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider(FixedUtcNow));
        });
    }
}
