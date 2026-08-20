using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Infrastructure;
using HarborOps.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider(HarborOpsApiFactory.FixedUtcNow));

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
