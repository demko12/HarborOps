namespace HarborOps.Modules.Documents.Contracts;

public interface IReplaceDocumentFileService
{
    Task<DocumentFileResponse> ReplaceAsync(
        UploadDocumentFileRequest request,
        CancellationToken cancellationToken);
}
