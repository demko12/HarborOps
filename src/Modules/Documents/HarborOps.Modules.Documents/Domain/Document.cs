namespace HarborOps.Modules.Documents.Domain;

public sealed class Document
{
    internal const int NumberMaxLength = 100;
    internal const int IssuingAuthorityMaxLength = 200;
    internal const int StatusMaxLength = 50;
    internal const int FileNameMaxLength = 260;
    internal const int ContentTypeMaxLength = 255;
    internal const int StorageKeyMaxLength = 200;

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public int DocumentTypeId { get; private set; }

    public DocumentType DocumentType { get; private set; } = null!;

    public Guid? UploadedBy { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public DateOnly IssueDate { get; private set; }

    public DateOnly ExpiryDate { get; private set; }

    public string IssuingAuthority { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public string? FileName { get; private set; }

    public string? StorageKey { get; private set; }

    public string? ContentType { get; private set; }

    public long? SizeBytes { get; private set; }

    public DateTime? UploadedAtUtc { get; private set; }

    public bool HasFile => StorageKey is not null;

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
        Id = Guid.NewGuid();
        VesselId = vesselId;
        DocumentTypeId = documentTypeId;
        Number = number;
        IssueDate = issueDate;
        ExpiryDate = expiryDate;
        IssuingAuthority = issuingAuthority;
        Status = status;
    }

    public void AttachFile(
        string fileName,
        string contentType,
        string storageKey,
        long sizeBytes,
        Guid uploadedBy,
        DateTime uploadedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentException("Size in bytes must be greater than zero.", nameof(sizeBytes));
        }

        FileName = fileName;
        ContentType = contentType;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        UploadedBy = uploadedBy;
        UploadedAtUtc = uploadedAtUtc;
    }
}
