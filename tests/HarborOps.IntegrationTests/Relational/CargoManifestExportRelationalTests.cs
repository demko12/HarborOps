using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

public sealed class CargoManifestExportRelationalTests : IAsyncLifetime
{
    private static readonly Guid VoyageId =
        Guid.Parse("4d3c2b1a-0918-4f7e-8d6c-5b4a39281706");

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task An_export_waits_for_an_uncommitted_change_and_then_reflects_all_of_it()
    {
        var bookingId = await SeedBookingAsync("BK-REL-EXPORT-01");
        var renamed = Container.Register("MSKU0000001", "22G1", CargoStatus.Registered, cargoBookingId: bookingId, portOfLoadingCode: "NLRTM", portOfDischargeCode: "USNYC", weightKg: 1000m);
        await SeedContainersAsync(renamed, Container.Register("MSKU0000002", "22G1", CargoStatus.Registered, cargoBookingId: bookingId, weightKg: 1000m));

        await using var writerContext = CreateDbContext();
        await using var writerTransaction = await writerContext.Database.BeginTransactionAsync();
        writerContext.Set<Container>().AddRange(Enumerable.Range(3, 5).Select(ordinal => Container.Register(
            $"MSKU{ordinal:D7}", "22G1", CargoStatus.Registered, cargoBookingId: bookingId, weightKg: 1000m)));
        var tracked = await writerContext.Set<Container>().SingleAsync(container => container.Id == renamed.Id);
        tracked.Correct("MSKU0009999", "22G1", "NLRTM", "USNYC", 1000m);
        await writerContext.SaveChangesAsync();

        await using var readerContext = CreateDbContext();
        var export = ExportAsync(readerContext, bookingId, new ExportCargoContainersRequest());

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(export.IsCompleted);

        await writerTransaction.CommitAsync();
        var result = await export.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(7, result.Rows.Count);
        Assert.Equal(7, result.MatchingCount);
        Assert.Equal(7, result.BookingContainerCount);
        Assert.Equal(7000m, result.KnownWeightKg);
        Assert.Equal("MSKU0009999", result.Rows[^1].ContainerNumber);
        Assert.DoesNotContain(result.Rows, row => row.ContainerNumber == "MSKU0000001");
        Assert.Equal(result.Rows.Count, result.Rows.Select(row => row.ContainerId).Distinct().Count());
    }

    [RelationalFact]
    public async Task Exports_stay_internally_consistent_while_containers_are_added_and_renumbered()
    {
        var bookingId = await SeedBookingAsync("BK-REL-EXPORT-02");
        await SeedContainersAsync(Enumerable.Range(1, 40).Select(ordinal => Container.Register(
            $"CONU{ordinal:D7}", "22G1", CargoStatus.Registered, cargoBookingId: bookingId, weightKg: 100m)).ToArray());

        using var stop = new CancellationTokenSource();
        var writer = Task.Run(() => WriteContinuouslyAsync(bookingId, stop.Token));
        var exports = new List<CargoManifestExport>();

        try
        {
            for (var iteration = 0; iteration < 25; iteration++)
            {
                await using var readerContext = CreateDbContext();
                exports.Add(await ExportAsync(readerContext, bookingId, new ExportCargoContainersRequest()));
                exports.Add(await ExportAsync(readerContext, bookingId, new ExportCargoContainersRequest { Search = "CONU" }));
            }
        }
        finally
        {
            await stop.CancelAsync();
            await writer;
        }

        Assert.Contains(exports, export => export.BookingContainerCount > 40);
        Assert.All(exports, export =>
        {
            Assert.Equal(export.Rows.Count, export.MatchingCount);
            Assert.Equal(export.Rows.Count, export.Rows.Select(row => row.ContainerId).Distinct().Count());
            Assert.Equal(export.Rows.Sum(row => row.WeightKg ?? 0m), export.KnownWeightKg);
            Assert.Equal(
                export.Rows.OrderBy(row => row.ContainerNumber, StringComparer.Ordinal).ThenBy(row => row.ContainerId).Select(row => row.ContainerId),
                export.Rows.Select(row => row.ContainerId));
            Assert.True(export.MatchingCount <= export.BookingContainerCount);

            if (!export.IsFiltered)
            {
                Assert.Equal(export.BookingContainerCount, export.MatchingCount);
            }
        });
    }

    [RelationalFact]
    public async Task The_row_limit_is_exact_on_sql_server()
    {
        var bookingId = await SeedBookingAsync("BK-REL-EXPORT-03");
        await SeedContainersAsync(Enumerable.Range(1, GetCargoManifestExportQuery.MaximumRows + 1).Select(ordinal => Container.Register(
            ordinal <= GetCargoManifestExportQuery.MaximumRows ? $"LIMU{ordinal:D7}" : "OVRU0000001",
            "22G1",
            CargoStatus.Registered,
            cargoBookingId: bookingId)).ToArray());

        await using var dbContext = CreateDbContext();

        var atLimit = await ExportAsync(dbContext, bookingId, new ExportCargoContainersRequest { Search = "LIMU" });

        Assert.Equal(GetCargoManifestExportQuery.MaximumRows, atLimit.Rows.Count);
        Assert.Equal(GetCargoManifestExportQuery.MaximumRows + 1, atLimit.BookingContainerCount);
        await Assert.ThrowsAsync<CargoManifestExportLimitExceededException>(
            () => ExportAsync(dbContext, bookingId, new ExportCargoContainersRequest()));
    }

    [RelationalFact]
    public async Task Unicode_and_formula_text_survive_sql_server_and_are_escaped_in_the_csv()
    {
        var bookingId = await SeedBookingAsync("BK-ČŽ-Đš-04");
        await SeedContainersAsync(
            Container.Register("MSKU0000001", "=Hladnjača ❄", CargoStatus.Registered, cargoBookingId: bookingId, weightKg: 1.5m),
            Container.Register("MSKU0000002", "Sušeno, \"suvo\"", CargoStatus.Registered, cargoBookingId: bookingId));

        await using var dbContext = CreateDbContext();
        var export = await ExportAsync(dbContext, bookingId, new ExportCargoContainersRequest());
        var csv = Encoding.UTF8.GetString(CargoManifestCsv.Write(export));

        Assert.Contains("BK-ČŽ-Đš-04,MSKU0000001,'=Hladnjača ❄,,,1.500,Registered,\r\n", csv, StringComparison.Ordinal);
        Assert.Contains("BK-ČŽ-Đš-04,MSKU0000002,\"Sušeno, \"\"suvo\"\"\",,,,Registered,\r\n", csv, StringComparison.Ordinal);
        Assert.Equal(1, export.UnknownWeightCount);
    }

    private async Task WriteContinuouslyAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var ordinal = 1000;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var writerContext = CreateDbContext();
                writerContext.Set<Container>().AddRange(Enumerable.Range(0, 3).Select(_ => Container.Register(
                    $"CONU{++ordinal:D7}", "22G1", CargoStatus.Registered, cargoBookingId: bookingId, weightKg: 100m)));

                var moved = await writerContext.Set<Container>()
                    .Where(container => container.CargoBookingId == bookingId)
                    .OrderBy(container => container.ContainerNumber)
                    .FirstAsync(CancellationToken.None);
                moved.Correct($"CONU{++ordinal:D7}", "22G1", "NLRTM", "USNYC", 100m);

                await writerContext.SaveChangesAsync(CancellationToken.None);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
            {
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
            }
        }
    }

    private static Task<CargoManifestExport> ExportAsync(
        HarborOpsDbContext dbContext,
        Guid bookingId,
        ExportCargoContainersRequest request)
    {
        return new GetCargoManifestExportQuery(dbContext, TimeProvider.System)
            .GetAsync(bookingId, request, CancellationToken.None);
    }

    private async Task<Guid> SeedBookingAsync(string bookingNumber)
    {
        await using var dbContext = CreateDbContext();
        var booking = CargoBooking.Register(bookingNumber, VoyageId, "Steel coils", "General Cargo", 5000m, 10, isHazardous: false);

        dbContext.Set<CargoBooking>().Add(booking);
        await dbContext.SaveChangesAsync();

        return booking.Id;
    }

    private async Task SeedContainersAsync(params Container[] containers)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Set<Container>().AddRange(containers);
        await dbContext.SaveChangesAsync();
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
