namespace HarborOps.Modules.Documents.Application;

public sealed class DocumentFileValidationOptions
{
    public const string SectionName = "Documents:FileValidation";

    public long MaxSizeBytes { get; set; } = 26_214_400; // 25 MB

    public IReadOnlyCollection<string> AllowedExtensions { get; set; } =
        new[] { ".pdf", ".jpg", ".jpeg", ".png", ".docx", ".xlsx" };
}
