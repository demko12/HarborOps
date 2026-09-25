namespace HarborOps.Modules.Documents.Contracts;

public interface IGetDocumentRequirementsQuery
{
    Task<GetDocumentRequirementsResponse> GetAsync(
        GetDocumentRequirementsRequest request,
        CancellationToken cancellationToken);

    Task<DocumentRequirementDetailsResponse> GetByIdAsync(
        Guid requirementId,
        CancellationToken cancellationToken);
}
