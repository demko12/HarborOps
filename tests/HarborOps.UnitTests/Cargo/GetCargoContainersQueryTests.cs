using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
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
        Guid? vesselId = null)
    {
        var container = Container.Register(
            containerNumber,
            containerType,
            status,
            vesselId,
            cargoBookingId);

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
