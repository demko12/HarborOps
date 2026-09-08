using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetVoyageEstimateRevisionsQuery(DbContext dbContext)
    : IGetVoyageEstimateRevisionsQuery
{
    public async Task<VoyageEstimateRevisionsResponse> GetEstimateRevisionsAsync(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        var voyageExists = await dbContext.Set<Voyage>()
            .AsNoTracking()
            .AnyAsync(voyage => voyage.Id == voyageId, cancellationToken);

        if (!voyageExists)
        {
            throw new VoyageNotFoundException(voyageId);
        }

        var stops = await (
            from stop in dbContext.Set<VoyageStop>().AsNoTracking()
            join port in dbContext.Set<Port>().AsNoTracking()
                on stop.PortId equals port.Id
            where stop.VoyageId == voyageId
            orderby stop.StopOrder, stop.Id
            select new VoyageEstimateStopItem(
                stop.Id,
                stop.StopOrder,
                port.Id,
                port.Name,
                stop.Status,
                stop.Eta,
                stop.Etd,
                stop.RevisedEta,
                stop.RevisedEtd,
                stop.Ata,
                stop.Atd))
            .ToListAsync(cancellationToken);

        var changes = await (
            from item in dbContext.Set<VoyageEstimateRevisionItem>().AsNoTracking()
            join batch in dbContext.Set<VoyageEstimateRevisionBatch>().AsNoTracking()
                on item.RevisionBatchId equals batch.Id
            join stop in dbContext.Set<VoyageStop>().AsNoTracking()
                on item.VoyageStopId equals stop.Id
            join port in dbContext.Set<Port>().AsNoTracking()
                on stop.PortId equals port.Id
            where batch.VoyageId == voyageId
            orderby batch.RevisedAtUtc descending, batch.Id descending, stop.StopOrder, stop.Id
            select new RevisionRow(
                batch.Id,
                batch.DelayId,
                batch.Reason,
                batch.RevisedByUserId,
                batch.RevisedAtUtc,
                new EstimateRevisionStopChangeItem(
                    stop.Id,
                    stop.StopOrder,
                    port.Name,
                    item.PreviousEta,
                    item.PreviousEtd,
                    item.NewEta,
                    item.NewEtd)))
            .ToListAsync(cancellationToken);

        var revisions = changes
            .GroupBy(row => row.RevisionBatchId)
            .Select(group => new VoyageEstimateRevisionBatchItem(
                group.Key,
                group.First().DelayId,
                group.First().Reason,
                group.First().RevisedByUserId,
                group.First().RevisedAtUtc,
                group.Select(row => row.Change).ToList()))
            .ToList();

        return new VoyageEstimateRevisionsResponse(voyageId, stops, revisions);
    }

    private sealed record RevisionRow(
        Guid RevisionBatchId,
        Guid DelayId,
        string Reason,
        Guid RevisedByUserId,
        DateTime RevisedAtUtc,
        EstimateRevisionStopChangeItem Change);
}
