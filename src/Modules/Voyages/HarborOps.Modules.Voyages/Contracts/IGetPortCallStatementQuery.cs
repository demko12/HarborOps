namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetPortCallStatementQuery
{
    Task<PortCallStatementResponse> GetAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken);

    Task<PortCallStatementResponse> GetPrintAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken);
}
