namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetPortCallClearanceQuery
{
    Task<PortCallClearanceResponse> GetAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken);
}
