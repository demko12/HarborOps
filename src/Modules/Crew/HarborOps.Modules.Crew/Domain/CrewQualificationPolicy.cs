namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewQualificationPolicy
{
    private CrewQualificationPolicy()
    {
    }

    private CrewQualificationPolicy(
        Guid id,
        Guid crewPositionId,
        bool enabled,
        Guid createdByUserId,
        DateTime createdAtUtc,
        Guid concurrencyToken)
    {
        Id = id;
        CrewPositionId = crewPositionId;
        Enabled = enabled;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = concurrencyToken;
    }

    public Guid Id { get; private set; }
    public Guid CrewPositionId { get; private set; }
    public bool Enabled { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    internal static CrewQualificationPolicy Create(
        Guid crewPositionId,
        bool enabled,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        return new CrewQualificationPolicy(
            Guid.NewGuid(), crewPositionId, enabled, createdByUserId, createdAtUtc, Guid.NewGuid());
    }

    internal void Update(bool enabled)
    {
        Enabled = enabled;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void Touch()
    {
        ConcurrencyToken = Guid.NewGuid();
    }
}
