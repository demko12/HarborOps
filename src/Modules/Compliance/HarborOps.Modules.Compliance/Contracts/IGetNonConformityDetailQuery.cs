namespace HarborOps.Modules.Compliance.Contracts;

public interface IGetNonConformityDetailQuery
{
    Task<GetNonConformityDetailResponse?> GetAsync(
        int nonConformityId,
        CancellationToken cancellationToken);
}
