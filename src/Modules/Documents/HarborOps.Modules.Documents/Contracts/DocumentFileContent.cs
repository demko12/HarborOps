namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentFileContent(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);
