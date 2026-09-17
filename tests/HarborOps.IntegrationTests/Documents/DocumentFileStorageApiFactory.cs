using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Infrastructure;
using HarborOps.Modules.Identity.Application;
using HarborOps.SharedKernel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HarborOps.IntegrationTests.Documents;

/// <summary>
/// Separate from <see cref="HarborOpsApiFactory"/> (sealed, shared by every other module's
/// tests) because this namespace additionally needs an isolated on-disk temp folder for
/// <see cref="LocalDiskDocumentFileStorageOptions.RootPath"/> per test class, mirroring the
/// per-class unique in-memory database name.
/// </summary>
public class DocumentFileStorageApiFactory : WebApplicationFactory<Program>
{
    public string RootPath { get; } =
        Path.Combine(Path.GetTempPath(), "HarborOpsTests", Guid.NewGuid().ToString("N"));

    private readonly string _databaseName = $"HarborOpsIntegrationTests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<LoginThrottlingOptions>(options =>
            {
                options.AccountFailedAttemptLimit = 10000;
                options.ClientAttemptLimit = 10000;
            });
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            services.RemoveAll<DbContext>();
            services.RemoveAll<HarborOpsDbContext>();
            services.RemoveAll<DbContextOptions<HarborOpsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<HarborOpsDbContext>>();

            services.AddDbContext<HarborOpsDbContext>(
                options => options.UseInMemoryDatabase(_databaseName));
            services.AddScoped<DbContext>(
                serviceProvider => serviceProvider.GetRequiredService<HarborOpsDbContext>());

            // FakeDateTimeProvider already implements both IDateTimeProvider and TimeProvider
            // (see FakeDateTimeProvider.cs) - one shared instance registered under both service
            // types keeps DocumentLifecycleService's TimeProvider (review/revoke actions) fixed
            // to the same instant as the rest of the Documents module, instead of resolving the
            // real TimeProvider.System that another module's TryAddSingleton happens to supply.
            var fakeDateTimeProvider = new FakeDateTimeProvider(HarborOpsApiFactory.FixedUtcNow);
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(fakeDateTimeProvider);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(fakeDateTimeProvider);

            services.Configure<LocalDiskDocumentFileStorageOptions>(options => options.RootPath = RootPath);
            services.Configure<DocumentFileValidationOptions>(ConfigureFileValidationOptions);
        });
    }

    protected virtual void ConfigureFileValidationOptions(DocumentFileValidationOptions options)
    {
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of the test-scoped temp folder; nothing the
            // caller can act on if this fails.
        }
    }
}

/// <summary>
/// Same as <see cref="DocumentFileStorageApiFactory"/> but with a tiny
/// <see cref="DocumentFileValidationOptions.MaxSizeBytes"/> so 413 tests don't need to
/// actually send tens of megabytes over the in-process test server.
/// </summary>
public sealed class SmallMaxSizeDocumentFileStorageApiFactory : DocumentFileStorageApiFactory
{
    // Must comfortably exceed multipart/form-data framing overhead: a 3-byte file part
    // (boundary + Content-Disposition/Content-Type headers) measures ~217 bytes on the wire,
    // so a setup upload used as a precondition in some 413 tests would be falsely rejected
    // if this were too close to that. 2048 leaves ~9x headroom while still being tiny next
    // to the real default (25 MB), so oversized test payloads (MaxSizeBytes + 1 raw bytes)
    // still trivially exceed it.
    public const long MaxSizeBytes = 2048;

    protected override void ConfigureFileValidationOptions(DocumentFileValidationOptions options)
    {
        options.MaxSizeBytes = MaxSizeBytes;
    }
}
