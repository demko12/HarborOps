namespace HarborOps.Modules.Documents.Domain;

public sealed class DocumentDistributionIssue
{
    internal const int InstructionMaxLength = 2_000;
    internal const int ReasonMaxLength = 1_000;

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public string DocumentNumberSnapshot { get; private set; } = string.Empty;
    public string DocumentTypeSnapshot { get; private set; } = string.Empty;
    public DateOnly DocumentExpirySnapshot { get; private set; }
    public Guid FileRevisionId { get; private set; }
    public Guid DocumentConcurrencyToken { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string? Instruction { get; private set; }
    public Guid IssuedByUserId { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    private DocumentDistributionIssue() { }

    public DocumentDistributionIssue(Document document, string documentTypeName, DateOnly dueDate, string? instruction, Guid issuerId, DateTime issuedAtUtc)
    {
        Id = Guid.NewGuid();
        DocumentId = document.Id;
        DocumentNumberSnapshot = document.Number;
        DocumentTypeSnapshot = documentTypeName;
        DocumentExpirySnapshot = document.ExpiryDate;
        FileRevisionId = document.FileRevisionId ?? throw new InvalidOperationException("A file revision is required.");
        DocumentConcurrencyToken = document.ConcurrencyToken;
        DueDate = dueDate;
        Instruction = instruction;
        IssuedByUserId = issuerId;
        IssuedAtUtc = issuedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public bool IsCancelled => CancelledAtUtc is not null;

    public void Cancel(Guid actorId, string reason, DateTime nowUtc)
    {
        if (IsCancelled) throw new InvalidOperationException("This distribution issue has already been cancelled.");
        CancelledAtUtc = nowUtc;
        CancelledByUserId = actorId;
        CancellationReason = reason;
        ConcurrencyToken = Guid.NewGuid();
    }
}
