namespace HarborOps.Modules.Compliance.Contracts;

public interface IGetComplianceCertificateDetailQuery
{
    Task<GetComplianceCertificateDetailResponse?> GetAsync(
        Guid certificateId,
        CancellationToken cancellationToken);
}
