namespace HarborOps.Modules.Documents.Domain;

public sealed class Document
{
    internal const int NumberMaxLength = 100;
    internal const int IssuingAuthorityMaxLength = 200;
    internal const int StatusMaxLength = 50;
    internal const int FileNameMaxLength = 260;
    internal const int FileUrlMaxLength = 2048;

    public int Id { get; private set; }

    public Guid VesselId { get; private set; }

    public int DocumentTypeId { get; private set; }

    public DocumentType DocumentType { get; private set; } = null!;

    public int? UploadedBy { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public DateOnly IssueDate { get; private set; }

    public DateOnly ExpiryDate { get; private set; }

    public string IssuingAuthority { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public string? FileName { get; private set; }

    public string? FileUrl { get; private set; }

    public DateTime? UploadedAtUtc { get; private set; }

    private Document()
    {
    }

    public Document(
        Guid vesselId,
        int documentTypeId,
        string number,
        DateOnly issueDate,
        DateOnly expiryDate,
        string issuingAuthority,
        string status)
    {
        VesselId = vesselId;
        DocumentTypeId = documentTypeId;
        Number = number;
        IssueDate = issueDate;
        ExpiryDate = expiryDate;
        IssuingAuthority = issuingAuthority;
        Status = status;
    }

    public void AttachFile(string fileName, string fileUrl, int uploadedBy, DateTime uploadedAtUtc)
    {
        FileName = fileName;
        FileUrl = fileUrl;
        UploadedBy = uploadedBy;
        UploadedAtUtc = uploadedAtUtc;
    }
}
