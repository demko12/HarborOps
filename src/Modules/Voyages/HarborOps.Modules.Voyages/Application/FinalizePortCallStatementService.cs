using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class FinalizePortCallStatementService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task FinalizeAsync(
        Guid voyageId,
        Guid stopId,
        FinalizePortCallStatementRequest request,
        Guid actorUserId,
        string actorName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);

        var errors = PortCallLogValidator.ValidateFinalize(request);
        if (errors.Count > 0)
        {
            throw new PortCallLogValidationException(errors);
        }

        var transaction = await PortCallClearanceGuard.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);

        try
        {
            var (voyageNumber, stop) = await PortCallLog.LoadStopAsync(
                dbContext,
                voyageId,
                stopId,
                tracking: false,
                cancellationToken);

            var statement = await PortCallLog.FindStatementAsync(
                dbContext,
                stopId,
                tracking: true,
                cancellationToken)
                ?? throw new PortCallStatementNotFoundException(stopId);

            statement.EnsureDraft();

            if (statement.ConcurrencyToken != request.Version!.Value)
            {
                throw new PortCallLogConflictException(stopId);
            }

            var sources = await PortCallLog.LoadSourcesAsync(
                dbContext,
                voyageNumber,
                stop,
                tracking: true,
                cancellationToken);

            var blockers = PortCallLog.FindBlockers(sources);
            if (blockers.Count > 0)
            {
                throw new PortCallStatementNotFinalizableException(stopId, blockers);
            }

            var sourceVersion = PortCallLog.ComputeSourceVersion(sources);
            if (!string.Equals(sourceVersion, request.SourceVersion!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new PortCallStatementSourceChangedException(stopId);
            }

            var snapshot = new PortCallStatementSnapshot(
                sources.VoyageNumber,
                stop.StopOrder,
                sources.PortName,
                sources.PortUnLocode,
                sources.PortTimeZone,
                sources.BerthName,
                stop.Ata!.Value,
                stop.Atd!.Value,
                sourceVersion,
                PortCallLog.BuildEntries(sources));

            var entries = statement.Finalize(
                snapshot,
                actorUserId,
                actorName,
                timeProvider.GetUtcNow().UtcDateTime);

            foreach (var activity in sources.Activities)
            {
                activity.Lock();
            }

            await dbContext.Set<PortCallStatementEntry>().AddRangeAsync(entries, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception)
        {
            throw new PortCallLogConflictException(stopId, exception);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
