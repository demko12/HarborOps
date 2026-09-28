using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class GetCargoContainersQueryTests
{
    private static readonly Guid VoyageId = Guid.Parse("7f6e5d4c-3b2a-4190-8f7e-6d5c4b3a2190");
    private static readonly Guid VesselId = Guid.Parse("3d2c1b0a-9988-4776-a554-4332211ffeed");

    [Fact]
    public async Task GetContainersAsync_returns_every_container_field()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0001");
        var container = await AddContainerAsync(
            dbContext,
            booking.Id,
            "MSKU1234567",
            containerType: "40HC",
            status: CargoStatus.Loaded,
            vesselId: VesselId);
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(booking.Id, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(container.Id, item.ContainerId);
        Assert.Equal("MSKU1234567", item.ContainerNumber);
        Assert.Equal("40HC", item.ContainerType);
        Assert.Equal(nameof(CargoStatus.Loaded), item.Status);
        Assert.Equal(booking.Id, item.CargoBookingId);
        Assert.Equal(VesselId, item.VesselId);
    }

    [Fact]
    public async Task GetContainersAsync_leaves_an_unassigned_vessel_null()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0002");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(booking.Id, CancellationToken.None);

        Assert.Null(Assert.Single(response.Items).VesselId);
    }

    [Fact]
    public async Task GetContainersAsync_orders_by_container_number()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0003");
        await AddContainerAsync(dbContext, booking.Id, "TGHU7654321");
        await AddContainerAsync(dbContext, booking.Id, "CSQU3054383");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(booking.Id, CancellationToken.None);

        Assert.Equal(
            ["CSQU3054383", "MSKU1234567", "TGHU7654321"],
            response.Items.Select(item => item.ContainerNumber));
    }

    [Fact]
    public async Task GetContainersAsync_breaks_a_number_tie_by_id()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0004");
        var first = await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        var second = await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(booking.Id, CancellationToken.None);

        var expected = new[] { first.Id, second.Id }.OrderBy(id => id);
        Assert.Equal(expected, response.Items.Select(item => item.ContainerId));
    }

    [Fact]
    public async Task GetContainersAsync_ignores_containers_of_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0005");
        var otherBooking = await AddBookingAsync(dbContext, "BK-MANIFEST-0006");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await AddContainerAsync(dbContext, otherBooking.Id, "TGHU7654321");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(booking.Id, CancellationToken.None);

        Assert.Equal("MSKU1234567", Assert.Single(response.Items).ContainerNumber);
    }

    [Fact]
    public async Task GetContainersAsync_returns_an_empty_manifest()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0007");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(booking.Id, CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetContainersAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetCargoContainersQuery(dbContext);
        var unknownBookingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => query.GetContainersAsync(unknownBookingId, CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
    }

    [Fact]
    public async Task GetContainersAsync_finds_a_container_beyond_the_first_page()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0101");
        await AddNumberedContainersAsync(dbContext, booking.Id, "MSKU", 20);
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Search = "0000019" },
            CancellationToken.None);

        Assert.Equal("MSKU0000019", Assert.Single(response.Items).ContainerNumber);
        Assert.Equal(1, response.MatchingCount);
        Assert.Equal(20, response.TotalCount);
        Assert.Equal(1, response.PageNumber);
    }

    [Fact]
    public async Task GetContainersAsync_matches_the_search_anywhere_in_the_number_ignoring_case_and_whitespace()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0102");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await AddContainerAsync(dbContext, booking.Id, "tghu7123400");
        await AddContainerAsync(dbContext, booking.Id, "CSQU3054383");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Search = "  u71234 " },
            CancellationToken.None);

        Assert.Equal("tghu7123400", Assert.Single(response.Items).ContainerNumber);
    }

    [Fact]
    public async Task GetContainersAsync_filters_by_an_exact_status_ignoring_case()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0103");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", status: CargoStatus.Loaded);
        await AddContainerAsync(dbContext, booking.Id, "TGHU7654321");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Status = " loaded " },
            CancellationToken.None);

        Assert.Equal("MSKU1234567", Assert.Single(response.Items).ContainerNumber);
    }

    [Fact]
    public async Task GetContainersAsync_filters_by_a_normalized_container_type()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0104");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", containerType: "42R1");
        await AddContainerAsync(dbContext, booking.Id, "TGHU7654321", containerType: "22r1");
        await AddContainerAsync(dbContext, booking.Id, "CSQU3054383", containerType: "22G1");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { ContainerType = " 22R1" },
            CancellationToken.None);

        Assert.Equal("TGHU7654321", Assert.Single(response.Items).ContainerNumber);
    }

    [Fact]
    public async Task GetContainersAsync_filters_by_port_of_loading_and_hides_legacy_rows_without_one()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0105");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", portOfLoadingCode: "NLRTM");
        await AddContainerAsync(dbContext, booking.Id, "TGHU7654321", portOfLoadingCode: "DEHAM");
        await AddContainerAsync(dbContext, booking.Id, "CSQU3054383", portOfLoadingCode: null);
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { PortOfLoadingCode = "nlrtm " },
            CancellationToken.None);

        Assert.Equal("MSKU1234567", Assert.Single(response.Items).ContainerNumber);
    }

    [Fact]
    public async Task GetContainersAsync_filters_by_port_of_discharge_and_hides_legacy_rows_without_one()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0106");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", portOfDischargeCode: "USNYC");
        await AddContainerAsync(dbContext, booking.Id, "TGHU7654321", portOfDischargeCode: "SGSIN");
        await AddContainerAsync(dbContext, booking.Id, "CSQU3054383", portOfDischargeCode: null);
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { PortOfDischargeCode = "SGSIN" },
            CancellationToken.None);

        Assert.Equal("TGHU7654321", Assert.Single(response.Items).ContainerNumber);
    }

    [Fact]
    public async Task GetContainersAsync_keeps_legacy_rows_without_ports_when_no_port_filter_is_set()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0107");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", status: CargoStatus.Loaded, portOfLoadingCode: null, portOfDischargeCode: null, weightKg: null);
        await AddContainerAsync(dbContext, booking.Id, "TGHU7654321", status: CargoStatus.Loaded);
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Status = "Loaded", PortOfLoadingCode = " ", PortOfDischargeCode = "" },
            CancellationToken.None);

        Assert.Equal(["MSKU1234567", "TGHU7654321"], response.Items.Select(item => item.ContainerNumber));
        Assert.Null(response.Items[0].PortOfLoadingCode);
        Assert.Null(response.Items[0].WeightKg);
    }

    [Fact]
    public async Task GetContainersAsync_combines_every_supplied_filter()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0108");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1000001", "42R1", CargoStatus.Loaded, portOfLoadingCode: "NLRTM", portOfDischargeCode: "USNYC");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1000002", "42R1", CargoStatus.Loaded, portOfLoadingCode: "NLRTM", portOfDischargeCode: "SGSIN");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1000003", "42R1", CargoStatus.Registered, portOfLoadingCode: "NLRTM", portOfDischargeCode: "USNYC");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1000004", "22G1", CargoStatus.Loaded, portOfLoadingCode: "NLRTM", portOfDischargeCode: "USNYC");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1000005", "42R1", CargoStatus.Loaded, portOfLoadingCode: "DEHAM", portOfDischargeCode: "USNYC");
        await AddContainerAsync(dbContext, booking.Id, "TGHU1000006", "42R1", CargoStatus.Loaded, portOfLoadingCode: "NLRTM", portOfDischargeCode: "USNYC");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest
            {
                Search = "msku",
                Status = "Loaded",
                ContainerType = "42R1",
                PortOfLoadingCode = "NLRTM",
                PortOfDischargeCode = "USNYC",
            },
            CancellationToken.None);

        Assert.Equal("MSKU1000001", Assert.Single(response.Items).ContainerNumber);
        Assert.Equal(1, response.MatchingCount);
        Assert.Equal(6, response.TotalCount);
        Assert.Equal(5, response.LoadedCount);
    }

    [Fact]
    public async Task GetContainersAsync_pages_the_matching_rows_while_totals_describe_the_whole_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0109");
        await AddNumberedContainersAsync(dbContext, booking.Id, "MSKU", 17, CargoStatus.Loaded);
        await AddNumberedContainersAsync(dbContext, booking.Id, "TGHU", 5);
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Search = "MSKU", PageNumber = 2 },
            CancellationToken.None);

        Assert.Equal(["MSKU0000016", "MSKU0000017"], response.Items.Select(item => item.ContainerNumber));
        Assert.Equal(17, response.MatchingCount);
        Assert.Equal(22, response.TotalCount);
        Assert.Equal(17, response.LoadedCount);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(15, response.PageSize);
    }

    [Fact]
    public async Task GetContainersAsync_returns_an_empty_page_beyond_the_last_matching_page()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0110");
        await AddNumberedContainersAsync(dbContext, booking.Id, "MSKU", 3);
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Search = "MSKU", PageNumber = 2 },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(3, response.MatchingCount);
        Assert.Equal(2, response.PageNumber);
    }

    [Fact]
    public async Task GetContainersAsync_reports_no_matches_without_changing_the_whole_booking_counts()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0111");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", status: CargoStatus.Loaded);
        await AddContainerAsync(dbContext, booking.Id, "TGHU7654321");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Search = "ZZZZ" },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.MatchingCount);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(1, response.LoadedCount);
    }

    [Fact]
    public async Task GetContainersAsync_without_filters_matches_the_whole_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0112");
        await AddNumberedContainersAsync(dbContext, booking.Id, "MSKU", 16);
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Search = "   ", Status = "", ContainerType = null },
            CancellationToken.None);

        Assert.Equal(15, response.Items.Count);
        Assert.Equal(16, response.MatchingCount);
        Assert.Equal(16, response.TotalCount);
    }

    [Fact]
    public async Task GetContainersAsync_never_returns_a_matching_container_of_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0113");
        var otherBooking = await AddBookingAsync(dbContext, "BK-MANIFEST-0114");
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await AddContainerAsync(dbContext, otherBooking.Id, "MSKU7654321");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Search = "MSKU" },
            CancellationToken.None);

        Assert.Equal("MSKU1234567", Assert.Single(response.Items).ContainerNumber);
        Assert.Equal(1, response.MatchingCount);
        Assert.Equal(1, response.TotalCount);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task GetContainersAsync_rejects_an_invalid_parameter(
        GetCargoContainersRequest request,
        string expectedField)
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0115");
        var query = new GetCargoContainersQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetCargoContainersValidationException>(
            () => query.GetContainersAsync(booking.Id, request, CancellationToken.None));

        Assert.Equal([expectedField], exception.Errors.Keys);
    }

    public static TheoryData<GetCargoContainersRequest, string> InvalidRequests() => new()
    {
        { new GetCargoContainersRequest { Search = new string('M', 21) }, GetCargoContainersValidator.SearchField },
        { new GetCargoContainersRequest { Status = "Lost" }, GetCargoContainersValidator.StatusField },
        { new GetCargoContainersRequest { Status = "2" }, GetCargoContainersValidator.StatusField },
        { new GetCargoContainersRequest { ContainerType = "99ZZ" }, GetCargoContainersValidator.ContainerTypeField },
        { new GetCargoContainersRequest { PortOfLoadingCode = "NLRT" }, GetCargoContainersValidator.PortOfLoadingCodeField },
        { new GetCargoContainersRequest { PortOfLoadingCode = "NL-RT" }, GetCargoContainersValidator.PortOfLoadingCodeField },
        { new GetCargoContainersRequest { PortOfDischargeCode = "USNYCX" }, GetCargoContainersValidator.PortOfDischargeCodeField },
        { new GetCargoContainersRequest { PageNumber = 0 }, GetCargoContainersValidator.PageNumberField },
        { new GetCargoContainersRequest { PageNumber = GetCargoContainersValidator.MaximumPageNumber + 1 }, GetCargoContainersValidator.PageNumberField },
        { new GetCargoContainersRequest { PageNumber = int.MaxValue }, GetCargoContainersValidator.PageNumberField },
        { new GetCargoContainersRequest { PageSize = 0 }, GetCargoContainersValidator.PageSizeField },
        { new GetCargoContainersRequest { PageSize = 16 }, GetCargoContainersValidator.PageSizeField },
    };

    [Fact]
    public async Task GetContainersAsync_accepts_a_search_of_the_maximum_container_number_length()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0116");
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { Search = $"  {new string('M', 20)}  " },
            CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetContainersAsync_accepts_the_largest_page_number_without_overflowing()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0118");
        await AddNumberedContainersAsync(dbContext, booking.Id, "MSKU", 2);
        var query = new GetCargoContainersQuery(dbContext);

        var response = await query.GetContainersAsync(
            booking.Id,
            new GetCargoContainersRequest { PageNumber = GetCargoContainersValidator.MaximumPageNumber },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(2, response.MatchingCount);
    }

    [Fact]
    public async Task GetContainersAsync_validates_before_looking_up_the_booking()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetCargoContainersQuery(dbContext);

        await Assert.ThrowsAsync<GetCargoContainersValidationException>(
            () => query.GetContainersAsync(
                Guid.NewGuid(),
                new GetCargoContainersRequest { Status = "Lost" },
                CancellationToken.None));
    }

    [Fact]
    public async Task GetContainersAsync_honours_cancellation()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-MANIFEST-0117");
        var query = new GetCargoContainersQuery(dbContext);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => query.GetContainersAsync(
                booking.Id,
                new GetCargoContainersRequest { Search = "MSKU" },
                cancellation.Token));
    }

    private static async Task AddNumberedContainersAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        string ownerCode,
        int count,
        CargoStatus status = CargoStatus.Registered)
    {
        for (var ordinal = 1; ordinal <= count; ordinal++)
        {
            await AddContainerAsync(dbContext, cargoBookingId, $"{ownerCode}{ordinal:D7}", status: status);
        }
    }

    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber)
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            "Steel coils",
            "General Cargo",
            5000.000m,
            10,
            isHazardous: false);

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return booking;
    }

    private static async Task<Container> AddContainerAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        string containerNumber,
        string containerType = "20GP",
        CargoStatus status = CargoStatus.Registered,
        Guid? vesselId = null,
        string? portOfLoadingCode = "NLRTM",
        string? portOfDischargeCode = "USNYC",
        decimal? weightKg = 12500m)
    {
        var container = Container.Register(
            containerNumber,
            containerType,
            status,
            vesselId,
            cargoBookingId,
            portOfLoadingCode,
            portOfDischargeCode,
            weightKg);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
