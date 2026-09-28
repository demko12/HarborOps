using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class GetCargoManifestExportQueryTests
{
    private static readonly Guid VoyageId = Guid.Parse("5b4a3928-1706-4f5e-8d4c-3b2a19087f6e");
    private static readonly Guid VesselId = Guid.Parse("2a19087f-6e5d-4c3b-8a19-087f6e5d4c3b");
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 9, 28, 14, 32, 5, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_returns_every_matching_row_across_manifest_pages_in_number_order()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0001");
        await AddContainersAsync(dbContext, booking.Id, Enumerable.Range(1, 20).Reverse().Select(ordinal => Container.Register(
            $"MSKU{ordinal:D7}", "42R1", CargoStatus.Registered, cargoBookingId: booking.Id)));
        await AddContainersAsync(dbContext, booking.Id, [Container.Register("TGHU0000001", "42R1", CargoStatus.Registered, cargoBookingId: booking.Id)]);

        var export = await CreateQuery(dbContext).GetAsync(
            booking.Id,
            new ExportCargoContainersRequest { Search = "msku" },
            CancellationToken.None);

        Assert.Equal(
            Enumerable.Range(1, 20).Select(ordinal => $"MSKU{ordinal:D7}"),
            export.Rows.Select(row => row.ContainerNumber));
        Assert.Equal(20, export.MatchingCount);
        Assert.Equal(21, export.BookingContainerCount);
        Assert.True(export.IsFiltered);
    }

    [Fact]
    public async Task GetAsync_breaks_a_number_tie_by_id()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0002");
        var first = Container.Register("MSKU1234567", "22G1", CargoStatus.Registered, cargoBookingId: booking.Id);
        var second = Container.Register("MSKU1234567", "22G1", CargoStatus.Registered, cargoBookingId: booking.Id);
        await AddContainersAsync(dbContext, booking.Id, [first, second]);

        var export = await CreateQuery(dbContext).GetAsync(booking.Id, new ExportCargoContainersRequest(), CancellationToken.None);

        Assert.Equal(new[] { first.Id, second.Id }.Order(), export.Rows.Select(row => row.ContainerId));
    }

    [Fact]
    public async Task GetAsync_without_filters_exports_the_whole_booking_as_a_complete_manifest()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0003");
        await AddContainersAsync(dbContext, booking.Id,
        [
            Container.Register("MSKU0000001", "42R1", CargoStatus.Loaded, VesselId, booking.Id, "NLRTM", "USNYC", 12500.5m),
            Container.Register("MSKU0000002", "22G1", CargoStatus.Registered, cargoBookingId: booking.Id),
        ]);

        var export = await CreateQuery(dbContext).GetAsync(
            booking.Id,
            new ExportCargoContainersRequest { Search = "  ", Status = "", PortOfLoadingCode = null },
            CancellationToken.None);

        Assert.False(export.IsFiltered);
        Assert.Equal(new CargoManifestExportFilters(null, null, null, null, null), export.Filters);
        Assert.Equal(2, export.MatchingCount);
        Assert.Equal(2, export.BookingContainerCount);
        Assert.Equal("BK-EXPORT-0003", export.BookingNumber);
        Assert.Equal(booking.Id, export.CargoBookingId);
        Assert.Equal(FixedUtcNow.UtcDateTime, export.GeneratedAtUtc);
        Assert.Equal(DateTimeKind.Utc, export.GeneratedAtUtc.Kind);
        Assert.Equal(10_000, export.RowLimit);

        var row = export.Rows[0];
        Assert.Equal("MSKU0000001", row.ContainerNumber);
        Assert.Equal("42R1", row.ContainerType);
        Assert.Equal(nameof(CargoStatus.Loaded), row.Status);
        Assert.Equal("NLRTM", row.PortOfLoadingCode);
        Assert.Equal("USNYC", row.PortOfDischargeCode);
        Assert.Equal(12500.5m, row.WeightKg);
        Assert.Equal(VesselId, row.VesselId);
    }

    [Theory]
    [InlineData(" msku00000 ", null, null, null, null, new[] { "MSKU0000001", "MSKU0000002" })]
    [InlineData(null, "loaded", null, null, null, new[] { "MSKU0000001", "TGHU0000003" })]
    [InlineData(null, null, " 22g1", null, null, new[] { "MSKU0000002" })]
    [InlineData(null, null, null, "deham", null, new[] { "TGHU0000003" })]
    [InlineData(null, null, null, null, "SGSIN ", new[] { "MSKU0000002" })]
    [InlineData("MSKU", "Loaded", "42R1", "NLRTM", "USNYC", new[] { "MSKU0000001" })]
    [InlineData("ZZZZ", null, null, null, null, new string[0])]
    public async Task GetAsync_applies_the_manifest_filter_rules(
        string? search,
        string? status,
        string? containerType,
        string? portOfLoadingCode,
        string? portOfDischargeCode,
        string[] expectedNumbers)
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0004");
        await AddContainersAsync(dbContext, booking.Id,
        [
            Container.Register("MSKU0000001", "42R1", CargoStatus.Loaded, cargoBookingId: booking.Id, portOfLoadingCode: "NLRTM", portOfDischargeCode: "USNYC"),
            Container.Register("MSKU0000002", "22G1", CargoStatus.Registered, cargoBookingId: booking.Id, portOfLoadingCode: "NLRTM", portOfDischargeCode: "SGSIN"),
            Container.Register("TGHU0000003", "42R1", CargoStatus.Loaded, cargoBookingId: booking.Id, portOfLoadingCode: "DEHAM", portOfDischargeCode: "USNYC"),
            Container.Register("TGHU0000004", "42R1", CargoStatus.Registered, cargoBookingId: booking.Id),
        ]);

        var export = await CreateQuery(dbContext).GetAsync(
            booking.Id,
            new ExportCargoContainersRequest
            {
                Search = search,
                Status = status,
                ContainerType = containerType,
                PortOfLoadingCode = portOfLoadingCode,
                PortOfDischargeCode = portOfDischargeCode,
            },
            CancellationToken.None);

        Assert.Equal(expectedNumbers, export.Rows.Select(row => row.ContainerNumber));
        Assert.Equal(expectedNumbers.Length, export.MatchingCount);
        Assert.Equal(4, export.BookingContainerCount);
        Assert.True(export.IsFiltered);
    }

    [Fact]
    public async Task GetAsync_reports_the_normalized_filters_it_applied()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0005");

        var export = await CreateQuery(dbContext).GetAsync(
            booking.Id,
            new ExportCargoContainersRequest
            {
                Search = " msku ",
                Status = "loaded",
                ContainerType = "42r1",
                PortOfLoadingCode = " nlrtm",
                PortOfDischargeCode = "usnyc ",
            },
            CancellationToken.None);

        Assert.Equal(new CargoManifestExportFilters("MSKU", "Loaded", "42R1", "NLRTM", "USNYC"), export.Filters);
    }

    [Fact]
    public async Task GetAsync_keeps_legacy_values_missing_and_counts_unknown_weight_separately()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0006");
        await AddContainersAsync(dbContext, booking.Id,
        [
            Container.Register("MSKU0000001", "22G1", CargoStatus.Registered, cargoBookingId: booking.Id, weightKg: 12500.250m),
            Container.Register("MSKU0000002", "22G1", CargoStatus.Registered, cargoBookingId: booking.Id, weightKg: 7499.750m),
            Container.Register("MSKU0000003", "22G1", CargoStatus.Registered, cargoBookingId: booking.Id),
        ]);

        var export = await CreateQuery(dbContext).GetAsync(booking.Id, new ExportCargoContainersRequest(), CancellationToken.None);

        Assert.Equal(20000.000m, export.KnownWeightKg);
        Assert.Equal(2, export.KnownWeightCount);
        Assert.Equal(1, export.UnknownWeightCount);
        var legacy = export.Rows[2];
        Assert.Null(legacy.WeightKg);
        Assert.Null(legacy.PortOfLoadingCode);
        Assert.Null(legacy.PortOfDischargeCode);
        Assert.Null(legacy.VesselId);
    }

    [Fact]
    public async Task GetAsync_returns_a_valid_empty_export_when_nothing_matches()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0007");
        await AddContainersAsync(dbContext, booking.Id, [Container.Register("MSKU0000001", "22G1", CargoStatus.Registered, cargoBookingId: booking.Id, weightKg: 100m)]);

        var export = await CreateQuery(dbContext).GetAsync(
            booking.Id,
            new ExportCargoContainersRequest { Status = "Unloaded" },
            CancellationToken.None);

        Assert.Empty(export.Rows);
        Assert.Equal(0, export.MatchingCount);
        Assert.Equal(1, export.BookingContainerCount);
        Assert.Equal(0m, export.KnownWeightKg);
        Assert.Equal(0, export.KnownWeightCount);
        Assert.Equal(0, export.UnknownWeightCount);
    }

    [Fact]
    public async Task GetAsync_never_exports_containers_of_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0008");
        var otherBooking = await AddBookingAsync(dbContext, "BK-EXPORT-0009");
        await AddContainersAsync(dbContext, booking.Id, [Container.Register("MSKU0000001", "22G1", CargoStatus.Registered, cargoBookingId: booking.Id, weightKg: 100m)]);
        await AddContainersAsync(dbContext, otherBooking.Id, [Container.Register("MSKU0000002", "22G1", CargoStatus.Registered, cargoBookingId: otherBooking.Id, weightKg: 900m)]);

        var export = await CreateQuery(dbContext).GetAsync(
            booking.Id,
            new ExportCargoContainersRequest { Search = "MSKU" },
            CancellationToken.None);

        Assert.Equal("MSKU0000001", Assert.Single(export.Rows).ContainerNumber);
        Assert.Equal(1, export.BookingContainerCount);
        Assert.Equal(100m, export.KnownWeightKg);
    }

    [Fact]
    public async Task GetAsync_exports_exactly_the_row_limit()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0010");
        await AddNumberedContainersAsync(dbContext, booking.Id, GetCargoManifestExportQuery.MaximumRows);

        var export = await CreateQuery(dbContext).GetAsync(booking.Id, new ExportCargoContainersRequest(), CancellationToken.None);

        Assert.Equal(GetCargoManifestExportQuery.MaximumRows, export.Rows.Count);
        Assert.Equal(GetCargoManifestExportQuery.MaximumRows, export.MatchingCount);
    }

    [Fact]
    public async Task GetAsync_rejects_one_row_over_the_limit_instead_of_truncating()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0011");
        await AddNumberedContainersAsync(dbContext, booking.Id, GetCargoManifestExportQuery.MaximumRows + 1);

        var exception = await Assert.ThrowsAsync<CargoManifestExportLimitExceededException>(
            () => CreateQuery(dbContext).GetAsync(booking.Id, new ExportCargoContainersRequest(), CancellationToken.None));

        Assert.Equal(GetCargoManifestExportQuery.MaximumRows, exception.Limit);
        Assert.Equal(
            "More than 10,000 containers match. Narrow the filters to export or print at most 10,000 containers.",
            exception.Message);
    }

    [Fact]
    public async Task GetAsync_allows_a_narrowed_filter_on_a_booking_above_the_limit()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0012");
        await AddNumberedContainersAsync(dbContext, booking.Id, GetCargoManifestExportQuery.MaximumRows + 1);

        var export = await CreateQuery(dbContext).GetAsync(
            booking.Id,
            new ExportCargoContainersRequest { Search = "MSKU0000001" },
            CancellationToken.None);

        Assert.Equal("MSKU0000001", Assert.Single(export.Rows).ContainerNumber);
        Assert.Equal(GetCargoManifestExportQuery.MaximumRows + 1, export.BookingContainerCount);
    }

    [Theory]
    [InlineData("Lost", null, null, "status")]
    [InlineData(null, "99ZZ", null, "containerType")]
    [InlineData(null, null, "NL-RT", "portOfLoadingCode")]
    public async Task GetAsync_rejects_an_invalid_filter(
        string? status,
        string? containerType,
        string? portOfLoadingCode,
        string expectedField)
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0013");

        var exception = await Assert.ThrowsAsync<GetCargoContainersValidationException>(
            () => CreateQuery(dbContext).GetAsync(
                booking.Id,
                new ExportCargoContainersRequest { Status = status, ContainerType = containerType, PortOfLoadingCode = portOfLoadingCode },
                CancellationToken.None));

        Assert.Equal([expectedField], exception.Errors.Keys);
    }

    [Fact]
    public async Task GetAsync_rejects_a_search_longer_than_a_container_number()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0014");

        var exception = await Assert.ThrowsAsync<GetCargoContainersValidationException>(
            () => CreateQuery(dbContext).GetAsync(
                booking.Id,
                new ExportCargoContainersRequest { Search = new string('M', 21) },
                CancellationToken.None));

        Assert.Equal(["search"], exception.Errors.Keys);
    }

    [Fact]
    public async Task GetAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var unknownBookingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => CreateQuery(dbContext).GetAsync(unknownBookingId, new ExportCargoContainersRequest(), CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
    }

    [Fact]
    public async Task GetAsync_never_changes_booking_or_container_state()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0015");
        await AddContainersAsync(dbContext, booking.Id, [Container.Register("MSKU0000001", "22G1", CargoStatus.Loaded, cargoBookingId: booking.Id)]);
        dbContext.ChangeTracker.Clear();

        await CreateQuery(dbContext).GetAsync(booking.Id, new ExportCargoContainersRequest(), CancellationToken.None);

        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Equal(CargoStatus.Loaded, (await dbContext.Set<Container>().SingleAsync()).Status);
    }

    [Fact]
    public async Task GetAsync_honours_cancellation()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXPORT-0016");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateQuery(dbContext).GetAsync(booking.Id, new ExportCargoContainersRequest(), cancellation.Token));
    }

    private static GetCargoManifestExportQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetCargoManifestExportQuery(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static async Task<CargoBooking> AddBookingAsync(HarborOpsDbContext dbContext, string bookingNumber)
    {
        var booking = CargoBooking.Register(bookingNumber, VoyageId, "Steel coils", "General Cargo", 5000.000m, 10, isHazardous: false);

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return booking;
    }

    private static async Task AddContainersAsync(HarborOpsDbContext dbContext, Guid bookingId, IEnumerable<Container> containers)
    {
        var added = containers.ToList();
        Assert.All(added, container => Assert.Equal(bookingId, container.CargoBookingId));

        await dbContext.Set<Container>().AddRangeAsync(added, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static Task AddNumberedContainersAsync(HarborOpsDbContext dbContext, Guid bookingId, int count)
    {
        return AddContainersAsync(dbContext, bookingId, Enumerable.Range(1, count).Select(ordinal => Container.Register(
            $"MSKU{ordinal:D7}", "22G1", CargoStatus.Registered, cargoBookingId: bookingId, weightKg: 1000m)));
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
