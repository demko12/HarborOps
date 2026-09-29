namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewCertificateHistoryQuery
{
    Task<GetCrewCertificateHistoryResponse> GetAsync(Guid certificateId, CancellationToken cancellationToken);
}
