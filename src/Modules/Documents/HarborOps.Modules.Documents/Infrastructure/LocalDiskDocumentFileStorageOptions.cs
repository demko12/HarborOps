namespace HarborOps.Modules.Documents.Infrastructure;

public sealed class LocalDiskDocumentFileStorageOptions
{
    public const string SectionName = "Documents:FileStorage";

    public string RootPath { get; set; } = string.Empty;
}
