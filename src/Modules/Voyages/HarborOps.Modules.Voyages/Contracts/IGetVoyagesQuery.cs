namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetVoyagesQuery
{
    Task<GetVoyagesResponse> GetVoyagesAsync(
        GetVoyagesRequest request,
        CancellationToken cancellationToken);
}
