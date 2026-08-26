namespace HarborOps.Modules.Crew.Contracts;

public interface IGetMedicalExaminationsQuery
{
    Task<GetMedicalExaminationsResponse> GetAsync(
        GetMedicalExaminationsRequest request,
        CancellationToken cancellationToken);
}
