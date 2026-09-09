using HarborOps.Api.Composition.Voyages;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Notifications.Application;
using HarborOps.Modules.Notifications.Domain;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarborOps.UnitTests.Voyages;

public sealed class VoyageEstimateRevisionServiceTests
{
    [Fact]
    public async Task Apply_commits_the_revision_and_creates_one_operational_alert()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(120);
        var preview = await scenario.PreviewAsync(delay);
        var service = CreateService(scenario, dbContext);

        var result = await service.ApplyAsync(
            scenario.Voyage.Id,
            delay.Id,
            VoyageDelayImpactScenario.ActorId,
            VoyageDelayImpactScenario.ToApplyRequest(preview),
            CancellationToken.None);

        Assert.True(result.AlertCreated);
        Assert.Equal(3, result.Changes.Count);

        var alert = Assert.Single(await dbContext.Set<OperationAlert>().AsNoTracking().ToListAsync());

        Assert.Equal(result.RevisionBatchId, alert.SourceId);
        Assert.Equal(VoyageEstimateRevisionService.AlertCapability, alert.Capability);
        Assert.Equal(VoyageEstimateRevisionService.AlertSourceType, alert.SourceType);
        Assert.Equal(VoyageEstimateRevisionService.AlertSeverity, alert.Severity);
        Assert.Single(await dbContext.Set<VoyageEstimateRevisionBatch>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Apply_keeps_the_revision_when_the_alert_cannot_be_created()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(120);
        var preview = await scenario.PreviewAsync(delay);
        var service = CreateService(scenario, CreateUnavailableNotificationService());

        var result = await service.ApplyAsync(
            scenario.Voyage.Id,
            delay.Id,
            VoyageDelayImpactScenario.ActorId,
            VoyageDelayImpactScenario.ToApplyRequest(preview),
            CancellationToken.None);

        Assert.False(result.AlertCreated);

        var batch = Assert.Single(await dbContext.Set<VoyageEstimateRevisionBatch>()
            .AsNoTracking()
            .ToListAsync());

        Assert.Equal(result.RevisionBatchId, batch.Id);
        Assert.Equal(3, await dbContext.Set<VoyageEstimateRevisionItem>().CountAsync());
    }

    [Fact]
    public async Task Apply_does_not_swallow_a_voyages_conflict()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);
        var preview = await scenario.PreviewAsync(delay);
        var service = CreateService(scenario, dbContext);

        await service.ApplyAsync(
            scenario.Voyage.Id,
            delay.Id,
            VoyageDelayImpactScenario.ActorId,
            VoyageDelayImpactScenario.ToApplyRequest(preview),
            CancellationToken.None);

        await Assert.ThrowsAsync<DelayImpactAlreadyAppliedException>(
            () => service.ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                VoyageDelayImpactScenario.ToApplyRequest(preview),
                CancellationToken.None));

        Assert.Single(await dbContext.Set<OperationAlert>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Retry_creates_the_missing_alert_for_a_committed_revision()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(90);
        var preview = await scenario.PreviewAsync(delay);

        var failing = CreateService(scenario, CreateUnavailableNotificationService());
        var applied = await failing.ApplyAsync(
            scenario.Voyage.Id,
            delay.Id,
            VoyageDelayImpactScenario.ActorId,
            VoyageDelayImpactScenario.ToApplyRequest(preview),
            CancellationToken.None);

        Assert.False(applied.AlertCreated);

        var result = await CreateService(scenario, dbContext).RetryAlertAsync(
            scenario.Voyage.Id,
            applied.RevisionBatchId,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.AlertCreated);
        Assert.Equal(applied.RevisionBatchId, result.RevisionBatchId);
        Assert.Single(await dbContext.Set<OperationAlert>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Retry_is_idempotent_and_never_duplicates_the_alert()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(45);
        var preview = await scenario.PreviewAsync(delay);
        var service = CreateService(scenario, dbContext);

        var applied = await service.ApplyAsync(
            scenario.Voyage.Id,
            delay.Id,
            VoyageDelayImpactScenario.ActorId,
            VoyageDelayImpactScenario.ToApplyRequest(preview),
            CancellationToken.None);

        var first = await service.RetryAlertAsync(
            scenario.Voyage.Id,
            applied.RevisionBatchId,
            CancellationToken.None);
        var second = await service.RetryAlertAsync(
            scenario.Voyage.Id,
            applied.RevisionBatchId,
            CancellationToken.None);

        Assert.True(first!.AlertCreated);
        Assert.True(second!.AlertCreated);
        Assert.Single(await dbContext.Set<OperationAlert>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Retry_returns_nothing_for_an_unknown_revision()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);

        var result = await CreateService(scenario, dbContext).RetryAlertAsync(
            scenario.Voyage.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(await dbContext.Set<OperationAlert>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Get_reports_the_alert_state_of_every_revision()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);

        var alerted = await scenario.AddDelayAsync(30, scenario.Stops[2].Id);
        var alertedPreview = await scenario.PreviewAsync(alerted);
        var alertedRevision = await CreateService(scenario, dbContext).ApplyAsync(
            scenario.Voyage.Id,
            alerted.Id,
            VoyageDelayImpactScenario.ActorId,
            VoyageDelayImpactScenario.ToApplyRequest(alertedPreview),
            CancellationToken.None);

        scenario.Advance(TimeSpan.FromMinutes(5));

        var silent = await scenario.AddDelayAsync(15, scenario.Stops[2].Id);
        var silentPreview = await scenario.PreviewAsync(silent);
        var silentRevision = await CreateService(scenario, CreateUnavailableNotificationService())
            .ApplyAsync(
                scenario.Voyage.Id,
                silent.Id,
                VoyageDelayImpactScenario.ActorId,
                VoyageDelayImpactScenario.ToApplyRequest(silentPreview),
                CancellationToken.None);

        var response = await CreateService(scenario, dbContext).GetAsync(
            scenario.Voyage.Id,
            CancellationToken.None);

        Assert.Equal(3, response.Stops.Count);
        Assert.Equal(
            [silentRevision.RevisionBatchId, alertedRevision.RevisionBatchId],
            response.Revisions.Select(revision => revision.RevisionBatchId));
        Assert.False(response.Revisions[0].AlertCreated);
        Assert.True(response.Revisions[1].AlertCreated);
    }

    [Fact]
    public async Task Get_rejects_an_unknown_voyage()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);

        await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => CreateService(scenario, dbContext).GetAsync(
                Guid.NewGuid(),
                CancellationToken.None));
    }

    private static VoyageEstimateRevisionService CreateService(
        VoyageDelayImpactScenario scenario,
        HarborOpsDbContext notificationDbContext)
    {
        return CreateService(
            scenario,
            new NotificationService(notificationDbContext, scenario.CreateTimeProvider()));
    }

    private static VoyageEstimateRevisionService CreateService(
        VoyageDelayImpactScenario scenario,
        NotificationService notificationService)
    {
        return new VoyageEstimateRevisionService(
            scenario.CreateApplyService(),
            new GetVoyageEstimateRevisionsQuery(scenario.DbContext),
            notificationService,
            NullLogger<VoyageEstimateRevisionService>.Instance);
    }

    private static NotificationService CreateUnavailableNotificationService()
    {
        var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        dbContext.Dispose();

        return new NotificationService(dbContext, TimeProvider.System);
    }
}
