namespace HarborOps.Modules.Crew.Domain;

internal sealed class VesselManningPlan
{
    private VesselManningPlan()
    {
    }

    private VesselManningPlan(
        Guid id,
        Guid vesselId,
        DateOnly effectiveStartDate,
        DateOnly effectiveEndDate,
        Guid createdByUserId,
        DateTime createdAtUtc,
        Guid concurrencyToken)
    {
        Id = id;
        VesselId = vesselId;
        EffectiveStartDate = effectiveStartDate;
        EffectiveEndDate = effectiveEndDate;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = concurrencyToken;
    }

    public Guid Id { get; private set; }
    public Guid VesselId { get; private set; }
    public DateOnly EffectiveStartDate { get; private set; }
    public DateOnly EffectiveEndDate { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    internal static VesselManningPlan Create(
        Guid vesselId,
        DateOnly effectiveStartDate,
        DateOnly effectiveEndDate,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        if (effectiveEndDate < effectiveStartDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveEndDate), effectiveEndDate, "Effective end date must be on or after the start date.");
        }

        return new VesselManningPlan(
            Guid.NewGuid(),
            vesselId,
            effectiveStartDate,
            effectiveEndDate,
            createdByUserId,
            createdAtUtc,
            Guid.NewGuid());
    }

    internal void EndOn(DateOnly newEndDate)
    {
        if (newEndDate < EffectiveStartDate || newEndDate > EffectiveEndDate)
        {
            throw new InvalidVesselManningPlanEndDateException(Id, newEndDate, EffectiveStartDate, EffectiveEndDate);
        }

        EffectiveEndDate = newEndDate;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void Touch()
    {
        ConcurrencyToken = Guid.NewGuid();
    }
}
