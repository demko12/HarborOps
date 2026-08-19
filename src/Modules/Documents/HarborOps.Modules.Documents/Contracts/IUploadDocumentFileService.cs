namespace HarborOps.Modules.Documents.Contracts;

public interface IUploadDocumentFileService
{
    Task<DocumentFileResponse> UploadAsync(
        UploadDocumentFileRequest request,
        CancellationToken cancellationToken);
}
