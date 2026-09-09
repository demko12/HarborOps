using HarborOps.Modules.Notifications.Application;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Api.Composition.Voyages;

public sealed class VoyageEstimateRevisionService(
    ApplyDelayImpactService applyDelayImpactService,
    IGetVoyageEstimateRevisionsQuery estimateRevisionsQuery,
    NotificationService notificationService,
    ILogger<VoyageEstimateRevisionService> logger)
{
    public const string AlertCapability = "voyages";
    public const string AlertSourceType = "VoyageEstimateRevision";
    public const string AlertSeverity = "warning";

    public async Task<VoyageEstimateRevisionResult> ApplyAsync(
        Guid voyageId,
        Guid delayId,
        Guid actingUserId,
        ApplyDelayImpactRequest request,
        CancellationToken cancellationToken)
    {
        var revision = await applyDelayImpactService.ApplyAsync(
            voyageId,
            delayId,
            actingUserId,
            request,
            cancellationToken);

        var alertCreated = await TryCreateAlertAsync(
            revision.RevisionBatchId,
            revision.RevisedAtUtc,
            cancellationToken);

        return new VoyageEstimateRevisionResult(
            revision.RevisionBatchId,
            revision.DelayId,
            revision.Reason,
            revision.RevisedByUserId,
            revision.RevisedAtUtc,
            alertCreated,
            revision.Changes);
    }

    public async Task<VoyageEstimateRevisionsResult> GetAsync(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        var revisions = await estimateRevisionsQuery.GetEstimateRevisionsAsync(
            voyageId,
            cancellationToken);

        var alerted = await ReadAlertedRevisionsAsync(
            revisions.Revisions.Select(revision => revision.RevisionBatchId).ToList(),
            cancellationToken);

        var results = revisions.Revisions
            .Select(revision => new VoyageEstimateRevisionResult(
                revision.RevisionBatchId,
                revision.DelayId,
                revision.Reason,
                revision.RevisedByUserId,
                revision.RevisedAtUtc,
                alerted.Contains(revision.RevisionBatchId),
                revision.Changes))
            .ToList();

        return new VoyageEstimateRevisionsResult(revisions.VoyageId, revisions.Stops, results);
    }

    public async Task<EstimateRevisionAlertResult?> RetryAlertAsync(
        Guid voyageId,
        Guid revisionBatchId,
        CancellationToken cancellationToken)
    {
        var revisions = await estimateRevisionsQuery.GetEstimateRevisionsAsync(
            voyageId,
            cancellationToken);

        var revision = revisions.Revisions
            .FirstOrDefault(candidate => candidate.RevisionBatchId == revisionBatchId);

        if (revision is null)
        {
            return null;
        }

        return new EstimateRevisionAlertResult(
            revisionBatchId,
            await TryCreateAlertAsync(revisionBatchId, revision.RevisedAtUtc, cancellationToken));
    }

    private async Task<bool> TryCreateAlertAsync(
        Guid revisionBatchId,
        DateTime revisedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return await notificationService.GenerateOperationAlertAsync(
                AlertCapability,
                AlertSourceType,
                revisionBatchId,
                DateOnly.FromDateTime(revisedAtUtc),
                AlertSeverity,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The operational alert for estimate revision {RevisionBatchId} could not be created.",
                revisionBatchId);

            return false;
        }
    }

    private async Task<IReadOnlySet<Guid>> ReadAlertedRevisionsAsync(
        IReadOnlyCollection<Guid> revisionBatchIds,
        CancellationToken cancellationToken)
    {
        try
        {
            return await notificationService.FindAlertedSourcesAsync(
                revisionBatchIds,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The operational alert state of the estimate revisions could not be read.");

            return new HashSet<Guid>();
        }
    }
}
