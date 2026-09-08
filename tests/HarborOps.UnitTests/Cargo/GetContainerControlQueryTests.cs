using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class GetContainerControlQueryTests
{
    private static readonly Guid ActorUserId =
        Guid.Parse("8b7a6f5e-4d3c-4b2a-9f0e-1d8c7b6a5f4e");

    private static readonly DateTime SealedAtUtc =
        new(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime InspectedAtUtc =
        new(2026, 9, 7, 9, 15, 0, DateTimeKind.Utc);

    private static readonly DateTime ReleasedAtUtc =
        new(2026, 9, 7, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAsync_reports_a_pending_container_without_a_control_record()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);

        var response = await new GetContainerControlQuery(dbContext)
            .GetAsync(container.Id, CancellationToken.None);

        Assert.Equal(container.Id, response.Control.ContainerId);
        Assert.Equal("MSKU1234567", response.Control.ContainerNumber);
        Assert.Null(response.Control.SealNumber);
        Assert.Equal(nameof(ContainerSealStatus.None), response.Control.SealStatus);
        Assert.Equal(nameof(ContainerControlState.Pending), response.Control.State);
        Assert.False(response.Control.CanRelease);
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealNotIntactReason,
            response.Control.ReleaseBlockedReason);
        Assert.Empty(response.History);
    }

    [Fact]
    public async Task GetAsync_reports_the_current_state_and_the_latest_inspection()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedControlledContainerAsync(dbContext);

        var response = await new GetContainerControlQuery(dbContext)
            .GetAsync(container.Id, CancellationToken.None);

        Assert.Equal("ML458821", response.Control.SealNumber);
        Assert.Equal(nameof(ContainerSealStatus.Intact), response.Control.SealStatus);
        Assert.Equal(nameof(ContainerControlState.Released), response.Control.State);
        Assert.Equal(
            nameof(ContainerInspectionResult.Passed),
            response.Control.LatestInspectionResult);
        Assert.Equal("Doors aligned", response.Control.LatestInspectionNotes);
        Assert.Equal(InspectedAtUtc, response.Control.LatestInspectedAtUtc);
        Assert.Equal(ActorUserId, response.Control.LatestInspectedByUserId);
        Assert.Equal(ReleasedAtUtc, response.Control.StateChangedAtUtc);
    }

    [Fact]
    public async Task GetAsync_returns_the_history_in_chronological_order()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedControlledContainerAsync(dbContext);

        var response = await new GetContainerControlQuery(dbContext)
            .GetAsync(container.Id, CancellationToken.None);

        Assert.Equal(
            [
                nameof(ContainerControlAction.SealAssigned),
                nameof(ContainerControlAction.InspectionRecorded),
                nameof(ContainerControlAction.Released),
            ],
            response.History.Select(entry => entry.Action));
        Assert.Equal(
            [SealedAtUtc, InspectedAtUtc, ReleasedAtUtc],
            response.History.Select(entry => entry.OccurredUtc));
    }

    [Fact]
    public async Task GetAsync_reports_a_reefer_container()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext, containerType: "40RH");

        var response = await new GetContainerControlQuery(dbContext)
            .GetAsync(container.Id, CancellationToken.None);

        Assert.True(response.Control.IsReefer);
    }

    [Fact]
    public async Task GetAsync_rejects_an_unknown_container()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<ContainerNotFoundException>(
            () => new GetContainerControlQuery(dbContext)
                .GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ignores_the_history_of_other_containers()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedControlledContainerAsync(dbContext);
        await SeedControlledContainerAsync(dbContext, "MSKU7654321", "ML999000");

        var response = await new GetContainerControlQuery(dbContext)
            .GetAsync(container.Id, CancellationToken.None);

        Assert.Equal(3, response.History.Count);
        Assert.DoesNotContain("ML999000", response.History.Select(entry => entry.SealNumber));
    }

    private static async Task<Container> SeedContainerAsync(
        HarborOpsDbContext dbContext,
        string containerNumber = "MSKU1234567",
        string containerType = "40HC")
    {
        var container = Container.Register(containerNumber, containerType, CargoStatus.Registered);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static async Task<Container> SeedControlledContainerAsync(
        HarborOpsDbContext dbContext,
        string containerNumber = "MSKU1234567",
        string sealNumber = "ML458821")
    {
        var container = await SeedContainerAsync(dbContext, containerNumber);
        var control = ContainerControl.Start(container.Id);
        var sealEntry = control.AssignSeal(sealNumber, ActorUserId, SealedAtUtc);
        var inspectionEntry = control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Doors aligned",
            null,
            isReefer: false,
            ActorUserId,
            InspectedAtUtc);
        var releaseEntry = control.Release(ActorUserId, ReleasedAtUtc, null);

        await dbContext.Set<ContainerControl>().AddAsync(control, CancellationToken.None);
        await dbContext.Set<ContainerControlEvent>()
            .AddRangeAsync([sealEntry, .. inspectionEntry, releaseEntry], CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}");

        return new HarborOpsDbContext(builder.Options);
    }
}
