using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

public sealed class CargoContainerManifestRelationalTests : IAsyncLifetime
{
    private static readonly Guid VoyageId =
        Guid.Parse("6c5b4a39-2817-4f06-9e5d-4c3b2a190807");

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

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task Search_filters_and_pages_in_the_database_across_the_whole_booking()
    {
        var bookingId = await SeedBookingAsync("BK-REL-MANIFEST-01");
        await SeedContainersAsync(bookingId, Enumerable.Range(1, 20)
            .Select(ordinal => Container.Register(
                $"MSKU{ordinal:D7}",
                "42G1",
                ordinal % 2 == 0 ? CargoStatus.Loaded : CargoStatus.Registered,
                cargoBookingId: bookingId,
                portOfLoadingCode: "NLRTM",
                portOfDischargeCode: "USNYC",
                weightKg: 12500m))
            .ToArray());

        await using var dbContext = CreateDbContext();
        var response = await new GetCargoContainersQuery(dbContext).GetContainersAsync(
            bookingId,
            new GetCargoContainersRequest { Search = "msku000000", Status = "loaded", PageNumber = 1 },
            CancellationToken.None);

        Assert.Equal(["MSKU0000002", "MSKU0000004", "MSKU0000006", "MSKU0000008"], response.Items.Select(item => item.ContainerNumber));
        Assert.Equal(4, response.MatchingCount);
        Assert.Equal(20, response.TotalCount);
        Assert.Equal(10, response.LoadedCount);
    }

    [RelationalFact]
    public async Task Legacy_values_are_matched_without_regard_to_case_and_missing_ports_only_hide_under_a_port_filter()
    {
        var bookingId = await SeedBookingAsync("BK-REL-MANIFEST-02");
        await SeedContainersAsync(
            bookingId,
            Container.Register("msku1111111", "42r1", CargoStatus.Registered, cargoBookingId: bookingId, portOfLoadingCode: "nlrtm", portOfDischargeCode: "usnyc"),
            Container.Register("MSKU2222222", "42R1", CargoStatus.Registered, cargoBookingId: bookingId));

        await using var dbContext = CreateDbContext();
        var query = new GetCargoContainersQuery(dbContext);

        var byPort = await query.GetContainersAsync(
            bookingId,
            new GetCargoContainersRequest { Search = "MSKU", ContainerType = "42R1", PortOfLoadingCode = "NLRTM", PortOfDischargeCode = "USNYC" },
            CancellationToken.None);
        var withoutPort = await query.GetContainersAsync(
            bookingId,
            new GetCargoContainersRequest { ContainerType = "42r1" },
            CancellationToken.None);

        Assert.Equal("msku1111111", Assert.Single(byPort.Items).ContainerNumber);
        Assert.Equal(2, withoutPort.MatchingCount);
    }

    [RelationalFact]
    public async Task Search_treats_like_wildcards_as_literal_characters()
    {
        var bookingId = await SeedBookingAsync("BK-REL-MANIFEST-03");
        await SeedContainersAsync(
            bookingId,
            Container.Register("MSKU3333333", "22G1", CargoStatus.Registered, cargoBookingId: bookingId));

        await using var dbContext = CreateDbContext();
        var query = new GetCargoContainersQuery(dbContext);

        foreach (var wildcard in new[] { "%", "_", "[A-Z]" })
        {
            var response = await query.GetContainersAsync(
                bookingId,
                new GetCargoContainersRequest { Search = wildcard },
                CancellationToken.None);

            Assert.Empty(response.Items);
            Assert.Equal(0, response.MatchingCount);
            Assert.Equal(1, response.TotalCount);
        }
    }

    [RelationalFact]
    public async Task The_largest_accepted_page_number_translates_to_a_valid_offset()
    {
        var bookingId = await SeedBookingAsync("BK-REL-MANIFEST-04");
        await SeedContainersAsync(
            bookingId,
            Container.Register("MSKU4444444", "22G1", CargoStatus.Registered, cargoBookingId: bookingId));

        await using var dbContext = CreateDbContext();
        var response = await new GetCargoContainersQuery(dbContext).GetContainersAsync(
            bookingId,
            new GetCargoContainersRequest { Search = "MSKU", PageNumber = GetCargoContainersValidator.MaximumPageNumber },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(1, response.MatchingCount);
    }

    private async Task<Guid> SeedBookingAsync(string bookingNumber)
    {
        await using var dbContext = CreateDbContext();
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            "Steel coils",
            "General Cargo",
            5000m,
            10,
            isHazardous: false);

        dbContext.Set<CargoBooking>().Add(booking);
        await dbContext.SaveChangesAsync();

        return booking.Id;
    }

    private async Task SeedContainersAsync(Guid bookingId, params Container[] containers)
    {
        await using var dbContext = CreateDbContext();
        Assert.All(containers, container => Assert.Equal(bookingId, container.CargoBookingId));
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
