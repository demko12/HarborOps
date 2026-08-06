using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CreateMaintenanceWorkOrderServiceTests
{
    [Fact]
    public async Task Create_persists_work_order_with_open_status_and_normalized_values()
    {
        await using var dbContext = CreateDbContext();
        var service = new CreateMaintenanceWorkOrderService(dbContext);
        var request = CreateValidRequest();
        var startedUtc = DateTime.UtcNow;

        var response = await service.CreateAsync(request, CancellationToken.None);

        var workOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(response.Id, workOrder.Id);
        Assert.Equal(request.VesselId, workOrder.VesselId);
        Assert.Equal("Replace main engine filter", workOrder.Title);
        Assert.Equal("Filter is due for replacement", workOrder.Description);
        Assert.Equal(WorkOrderPriority.High, workOrder.Priority);
        Assert.Equal(WorkOrderStatus.Open, workOrder.Status);
        Assert.InRange(workOrder.CreatedUtc, startedUtc, DateTime.UtcNow);
        Assert.Null(workOrder.DryDockId);
        Assert.Null(workOrder.AssignedEngineerId);
        Assert.Null(workOrder.CompletedUtc);
    }

    [Fact]
    public async Task Create_rejects_empty_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var service = new CreateMaintenanceWorkOrderService(dbContext);
        var request = CreateValidRequest() with { VesselId = Guid.Empty };

        var exception = await Assert.ThrowsAsync<CreateMaintenanceWorkOrderValidationException>(
            () => service.CreateAsync(request, CancellationToken.None));

        Assert.Contains(CreateMaintenanceWorkOrderValidator.VesselIdField, exception.Errors);
    }

    [Fact]
    public async Task Create_rejects_missing_title()
    {
        await using var dbContext = CreateDbContext();
        var service = new CreateMaintenanceWorkOrderService(dbContext);
        var request = CreateValidRequest() with { Title = " " };

        var exception = await Assert.ThrowsAsync<CreateMaintenanceWorkOrderValidationException>(
            () => service.CreateAsync(request, CancellationToken.None));

        Assert.Contains(CreateMaintenanceWorkOrderValidator.TitleField, exception.Errors);
    }

    [Fact]
    public async Task Create_rejects_invalid_priority()
    {
        await using var dbContext = CreateDbContext();
        var service = new CreateMaintenanceWorkOrderService(dbContext);
        var request = CreateValidRequest() with { Priority = "Urgent" };

        var exception = await Assert.ThrowsAsync<CreateMaintenanceWorkOrderValidationException>(
            () => service.CreateAsync(request, CancellationToken.None));

        Assert.Contains(CreateMaintenanceWorkOrderValidator.PriorityField, exception.Errors);
    }

    [Fact]
    public async Task Create_rejects_non_utc_due_utc()
    {
        await using var dbContext = CreateDbContext();
        var service = new CreateMaintenanceWorkOrderService(dbContext);
        var request = CreateValidRequest() with
        {
            DueUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1), DateTimeKind.Local),
        };

        var exception = await Assert.ThrowsAsync<CreateMaintenanceWorkOrderValidationException>(
            () => service.CreateAsync(request, CancellationToken.None));

        Assert.Contains(CreateMaintenanceWorkOrderValidator.DueUtcField, exception.Errors);
    }

    [Fact]
    public async Task Create_rejects_due_utc_not_in_future()
    {
        await using var dbContext = CreateDbContext();
        var service = new CreateMaintenanceWorkOrderService(dbContext);
        var request = CreateValidRequest() with { DueUtc = DateTime.UtcNow.AddDays(-1) };

        var exception = await Assert.ThrowsAsync<CreateMaintenanceWorkOrderValidationException>(
            () => service.CreateAsync(request, CancellationToken.None));

        Assert.Contains(CreateMaintenanceWorkOrderValidator.DueUtcField, exception.Errors);
    }

    private static CreateMaintenanceWorkOrderRequest CreateValidRequest()
    {
        return new CreateMaintenanceWorkOrderRequest
        {
            VesselId = Guid.NewGuid(),
            Title = " Replace main engine filter ",
            Description = " Filter is due for replacement ",
            Priority = "High",
            DueUtc = DateTime.UtcNow.AddDays(7),
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
