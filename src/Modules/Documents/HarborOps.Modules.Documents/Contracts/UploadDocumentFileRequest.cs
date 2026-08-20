namespace HarborOps.Modules.Documents.Contracts;

public sealed record UploadDocumentFileRequest(
    Guid DocumentId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content,
    Guid UploadedBy);
