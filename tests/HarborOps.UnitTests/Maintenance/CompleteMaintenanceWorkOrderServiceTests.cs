using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CompleteMaintenanceWorkOrderServiceTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.UtcNow.AddMinutes(10);

    [Fact]
    public async Task CompleteWorkOrderAsync_completes_open_work_order_and_creates_record()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        var response = await service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None);

        var persistedWorkOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleAsync(candidate => candidate.Id == workOrder.Id);
        var persistedRecord = await dbContext
            .Set<MaintenanceRecord>()
            .SingleAsync(candidate => candidate.Id == response.MaintenanceRecordId);

        Assert.Equal(workOrder.Id, response.WorkOrderId);
        Assert.Equal(WorkOrderStatus.Completed, persistedWorkOrder.Status);
        Assert.Equal(request.CompletedUtc, persistedWorkOrder.CompletedUtc);
        Assert.Equal(request.CompletedUtc, response.CompletedUtc);
        Assert.Equal(workOrder.Id, persistedRecord.WorkOrderId);
        Assert.Equal(request.TechnicianId, persistedRecord.TechnicianId);
        Assert.Equal(DateOnly.FromDateTime(request.CompletedUtc!.Value), persistedRecord.CompletionDate);
        Assert.Equal("Filter replaced successfully.", persistedRecord.Notes);
        Assert.Equal(request.Cost, persistedRecord.Cost);
        Assert.Equal(request.LaborHours, persistedRecord.LaborHours);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_completes_in_progress_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        workOrder.Start();
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        var response = await service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None);

        var persistedWorkOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleAsync(candidate => candidate.Id == workOrder.Id);

        Assert.Equal(WorkOrderStatus.Completed, persistedWorkOrder.Status);
        Assert.NotEqual(Guid.Empty, response.MaintenanceRecordId);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_invalid_input()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = new CompleteMaintenanceWorkOrderRequest
        {
            TechnicianId = Guid.Empty,
            CompletedUtc = FixedNow.UtcDateTime.AddDays(1),
            Cost = -10m,
            LaborHours = 0m,
        };

        var exception = await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderValidationException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.Contains(CompleteMaintenanceWorkOrderValidator.TechnicianIdField, exception.Errors);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CompletedUtcField, exception.Errors);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CostField, exception.Errors);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.LaborHoursField, exception.Errors);
        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync());
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_completed_utc_before_created_utc()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest() with { CompletedUtc = workOrder.CreatedUtc.AddSeconds(-1) };

        var exception = await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderValidationException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CompletedUtcField, exception.Errors);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_non_utc_completed_utc()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest() with
        {
            CompletedUtc = DateTime.SpecifyKind(FixedNow.UtcDateTime, DateTimeKind.Local),
        };

        var exception = await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderValidationException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CompletedUtcField, exception.Errors);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_completed_utc_in_the_future()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest() with { CompletedUtc = FixedNow.UtcDateTime.AddDays(1) };

        var exception = await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderValidationException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CompletedUtcField, exception.Errors);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_already_completed_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        workOrder.Complete(FixedNow.UtcDateTime.AddHours(-1));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderConflictException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync());
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_canceled_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        workOrder.Cancel();
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderConflictException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync());
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_throws_not_found_for_unknown_work_order()
    {
        await using var dbContext = CreateDbContext();
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => service.CompleteWorkOrderAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    private static CompleteMaintenanceWorkOrderRequest CreateValidRequest()
    {
        return new CompleteMaintenanceWorkOrderRequest
        {
            TechnicianId = Guid.NewGuid(),
            CompletedUtc = FixedNow.UtcDateTime,
            Notes = " Filter replaced successfully. ",
            Cost = 150.50m,
            LaborHours = 2.5m,
        };
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
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
