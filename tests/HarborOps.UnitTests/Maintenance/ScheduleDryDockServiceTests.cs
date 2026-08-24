using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ScheduleDryDockServiceTests
{
    private static readonly DateOnly StartDate = new(2026, 5, 4);

    [Fact]
    public async Task ScheduleAsync_persists_a_planned_dry_dock_with_trimmed_tasks()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var portId = Guid.NewGuid();
        var service = new ScheduleDryDockService(dbContext);

        var response = await service.ScheduleAsync(
            new ScheduleDryDockRequest
            {
                VesselId = vesselId,
                PortId = portId,
                StartDate = StartDate,
                PlannedTasks = "  Hull blasting and coating  ",
            },
            CancellationToken.None);

        var stored = await dbContext.Set<DryDock>().SingleAsync();

        Assert.Equal(stored.Id, response.Id);
        Assert.Equal(vesselId, stored.VesselId);
        Assert.Equal(portId, stored.PortId);
        Assert.Equal(StartDate, stored.StartDate);
        Assert.Equal("Hull blasting and coating", stored.PlannedTasks);
        Assert.Equal(DryDockStatus.Planned, stored.Status);
        Assert.Null(stored.EndDate);
        Assert.Null(stored.ActualCost);
    }

    [Fact]
    public async Task ScheduleAsync_accepts_a_vessel_and_port_it_cannot_verify()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleDryDockService(dbContext);

        var response = await service.ScheduleAsync(ValidRequest(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
    }

    [Theory]
    [InlineData(ScheduleDryDockValidator.VesselIdField)]
    [InlineData(ScheduleDryDockValidator.PortIdField)]
    [InlineData(ScheduleDryDockValidator.StartDateField)]
    [InlineData(ScheduleDryDockValidator.PlannedTasksField)]
    public async Task ScheduleAsync_rejects_a_request_missing_a_required_value(string field)
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleDryDockService(dbContext);

        var request = field switch
        {
            ScheduleDryDockValidator.VesselIdField => ValidRequest() with { VesselId = null },
            ScheduleDryDockValidator.PortIdField => ValidRequest() with { PortId = null },
            ScheduleDryDockValidator.StartDateField => ValidRequest() with { StartDate = null },
            _ => ValidRequest() with { PlannedTasks = "   " },
        };

        var exception = await Assert.ThrowsAsync<ScheduleDryDockValidationException>(
            () => service.ScheduleAsync(request, CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(field));
        Assert.Empty(dbContext.Set<DryDock>());
    }

    [Theory]
    [InlineData(ScheduleDryDockValidator.VesselIdField)]
    [InlineData(ScheduleDryDockValidator.PortIdField)]
    public async Task ScheduleAsync_rejects_an_empty_identifier(string field)
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleDryDockService(dbContext);

        var request = field == ScheduleDryDockValidator.VesselIdField
            ? ValidRequest() with { VesselId = Guid.Empty }
            : ValidRequest() with { PortId = Guid.Empty };

        var exception = await Assert.ThrowsAsync<ScheduleDryDockValidationException>(
            () => service.ScheduleAsync(request, CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(field));
    }

    [Fact]
    public async Task ScheduleAsync_rejects_planned_tasks_beyond_the_stored_length()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleDryDockService(dbContext);

        var exception = await Assert.ThrowsAsync<ScheduleDryDockValidationException>(
            () => service.ScheduleAsync(
                ValidRequest() with
                {
                    PlannedTasks = new string('t', DryDock.PlannedTasksMaxLength + 1),
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ScheduleDryDockValidator.PlannedTasksField));
        Assert.Empty(dbContext.Set<DryDock>());
    }

    [Fact]
    public async Task ScheduleAsync_reports_every_invalid_field_at_once()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleDryDockService(dbContext);

        var exception = await Assert.ThrowsAsync<ScheduleDryDockValidationException>(
            () => service.ScheduleAsync(new ScheduleDryDockRequest(), CancellationToken.None));

        Assert.Equal(4, exception.Errors.Count);
    }

    private static ScheduleDryDockRequest ValidRequest()
    {
        return new ScheduleDryDockRequest
        {
            VesselId = Guid.NewGuid(),
            PortId = Guid.NewGuid(),
            StartDate = StartDate,
            PlannedTasks = "Propeller shaft survey",
        };
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
