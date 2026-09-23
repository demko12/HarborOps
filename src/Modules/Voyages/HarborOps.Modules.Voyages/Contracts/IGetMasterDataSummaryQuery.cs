namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetMasterDataSummaryQuery
{
    Task<MasterDataSummaryResponse> GetAsync(CancellationToken cancellationToken);
}
