namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetVoyageScheduleChangesQuery
{
    Task<VoyageScheduleResponse> GetScheduleChangesAsync(
        Guid voyageId,
        CancellationToken cancellationToken);
}
