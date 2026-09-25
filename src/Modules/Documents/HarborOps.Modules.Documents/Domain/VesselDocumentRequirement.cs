namespace HarborOps.Modules.Documents.Domain;

internal sealed class VesselDocumentRequirement
{
    internal const int MinimumRemainingValidityDaysLimit = 365;
    internal const int InstructionMaxLength = 2000;

    private VesselDocumentRequirement()
    {
    }

    private VesselDocumentRequirement(
        Guid id,
        Guid vesselId,
        int documentTypeId,
        int minimumRemainingValidityDays,
        string? instruction,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        Id = id;
        VesselId = vesselId;
        DocumentTypeId = documentTypeId;
        IsEnabled = true;
        MinimumRemainingValidityDays = minimumRemainingValidityDays;
        Instruction = instruction;
        Revision = 1;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedByUserId = createdByUserId;
        UpdatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid VesselId { get; private set; }
    public int DocumentTypeId { get; private set; }
    public bool IsEnabled { get; private set; }
    public int MinimumRemainingValidityDays { get; private set; }
    public string? Instruction { get; private set; }
    public int Revision { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    internal static VesselDocumentRequirement Create(
        Guid vesselId,
        int documentTypeId,
        int minimumRemainingValidityDays,
        string? instruction,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        EnsureValidSettings(minimumRemainingValidityDays, instruction);

        return new VesselDocumentRequirement(
            Guid.NewGuid(),
            vesselId,
            documentTypeId,
            minimumRemainingValidityDays,
            instruction,
            createdByUserId,
            createdAtUtc);
    }

    internal void Update(
        bool isEnabled,
        int minimumRemainingValidityDays,
        string? instruction,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        EnsureValidSettings(minimumRemainingValidityDays, instruction);

        IsEnabled = isEnabled;
        MinimumRemainingValidityDays = minimumRemainingValidityDays;
        Instruction = instruction;
        Touch(actorUserId, occurredAtUtc);
    }

    internal void Disable(Guid actorUserId, DateTime occurredAtUtc)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("The document requirement is already disabled.");
        }

        IsEnabled = false;
        Touch(actorUserId, occurredAtUtc);
    }

    private void Touch(Guid actorUserId, DateTime occurredAtUtc)
    {
        Revision++;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static void EnsureValidSettings(int minimumRemainingValidityDays, string? instruction)
    {
        if (minimumRemainingValidityDays is < 0 or > MinimumRemainingValidityDaysLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumRemainingValidityDays),
                minimumRemainingValidityDays,
                $"Minimum remaining validity must be between 0 and {MinimumRemainingValidityDaysLimit} days.");
        }

        if (instruction is { Length: > InstructionMaxLength })
        {
            throw new ArgumentException(
                $"Instruction must not exceed {InstructionMaxLength} characters.", nameof(instruction));
        }
    }
}
