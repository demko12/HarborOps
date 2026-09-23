using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetPortCallStatementQuery(DbContext dbContext) : IGetPortCallStatementQuery
{
    public async Task<PortCallStatementResponse> GetAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
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
            tracking: false,
            cancellationToken)
            ?? throw new PortCallStatementNotFoundException(stopId);

        return statement.IsFinalized
            ? await FromSnapshotAsync(statement, cancellationToken)
            : await PreviewAsync(statement, voyageNumber, stop, cancellationToken);
    }

    public async Task<PortCallStatementResponse> GetPrintAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        await PortCallLog.LoadStopAsync(dbContext, voyageId, stopId, tracking: false, cancellationToken);

        var statement = await PortCallLog.FindStatementAsync(
            dbContext,
            stopId,
            tracking: false,
            cancellationToken)
            ?? throw new PortCallStatementNotFoundException(stopId);

        if (!statement.IsFinalized)
        {
            throw new PortCallStatementNotFinalizedException(statement.Id);
        }

        return await FromSnapshotAsync(statement, cancellationToken);
    }

    private async Task<PortCallStatementResponse> PreviewAsync(
        PortCallStatement statement,
        string voyageNumber,
        VoyageStop stop,
        CancellationToken cancellationToken)
    {
        var sources = await PortCallLog.LoadSourcesAsync(
            dbContext,
            voyageNumber,
            stop,
            tracking: false,
            cancellationToken);

        var blockers = PortCallLog.FindBlockers(sources);

        return new PortCallStatementResponse(
            statement.Id,
            PortCallStatement.FormatDocumentNumber(sources.VoyageNumber, stop.StopOrder),
            statement.VoyageId,
            statement.VoyageStopId,
            statement.Status,
            statement.ConcurrencyToken,
            PortCallLog.ComputeSourceVersion(sources),
            sources.VoyageNumber,
            stop.StopOrder,
            sources.PortName,
            sources.PortUnLocode,
            sources.PortTimeZone,
            sources.BerthName,
            stop.Ata,
            stop.Atd,
            statement.CreatedByUserId,
            statement.CreatedAtUtc,
            null,
            null,
            null,
            blockers.Count == 0,
            blockers.Select(PortCallLog.ToItem).ToList(),
            PortCallLog.ToEntryItems(PortCallLog.BuildEntries(sources), stop.Ata, stop.Atd));
    }

    private async Task<PortCallStatementResponse> FromSnapshotAsync(
        PortCallStatement statement,
        CancellationToken cancellationToken)
    {
        var entries = await dbContext.Set<PortCallStatementEntry>()
            .AsNoTracking()
            .Where(entry => entry.PortCallStatementId == statement.Id)
            .OrderBy(entry => entry.Position)
            .Select(entry => new PortCallLogEntryItem(
                entry.Position,
                entry.Source,
                entry.SourceId,
                entry.SourceRevision,
                entry.Label,
                entry.OccurredAtUtc,
                entry.EndedAtUtc,
                entry.Notes,
                entry.DurationMinutes,
                false))
            .ToListAsync(cancellationToken);

        return new PortCallStatementResponse(
            statement.Id,
            PortCallStatement.FormatDocumentNumber(statement.VoyageNumber!, statement.StopOrder!.Value),
            statement.VoyageId,
            statement.VoyageStopId,
            statement.Status,
            statement.ConcurrencyToken,
            statement.SourceVersion,
            statement.VoyageNumber!,
            statement.StopOrder!.Value,
            statement.PortName!,
            statement.PortUnLocode,
            statement.PortTimeZone,
            statement.BerthName,
            statement.ActualArrivalUtc,
            statement.ActualDepartureUtc,
            statement.CreatedByUserId,
            statement.CreatedAtUtc,
            statement.FinalizedByUserId,
            statement.FinalizedByName,
            statement.FinalizedAtUtc,
            false,
            [],
            entries);
    }
}
