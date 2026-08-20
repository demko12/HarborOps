namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentFileResponse(
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedBy,
    DateTime UploadedAtUtc);
