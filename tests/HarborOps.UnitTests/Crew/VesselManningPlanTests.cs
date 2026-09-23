using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class VesselManningPlanTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_sets_fields_and_a_fresh_concurrency_token()
    {
        var vesselId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var plan = VesselManningPlan.Create(
            vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), createdBy, CreatedAtUtc);

        Assert.Equal(vesselId, plan.VesselId);
        Assert.Equal(new DateOnly(2026, 1, 1), plan.EffectiveStartDate);
        Assert.Equal(new DateOnly(2026, 3, 31), plan.EffectiveEndDate);
        Assert.Equal(createdBy, plan.CreatedByUserId);
        Assert.NotEqual(Guid.Empty, plan.ConcurrencyToken);
    }

    [Fact]
    public void Create_rejects_an_end_date_before_the_start_date()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VesselManningPlan.Create(
            Guid.NewGuid(), new DateOnly(2026, 3, 31), new DateOnly(2026, 1, 1), Guid.NewGuid(), CreatedAtUtc));
    }

    [Fact]
    public void EndOn_shortens_the_effective_end_date_and_bumps_the_concurrency_token()
    {
        var plan = VesselManningPlan.Create(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), Guid.NewGuid(), CreatedAtUtc);
        var previousToken = plan.ConcurrencyToken;

        plan.EndOn(new DateOnly(2026, 2, 15));

        Assert.Equal(new DateOnly(2026, 2, 15), plan.EffectiveEndDate);
        Assert.NotEqual(previousToken, plan.ConcurrencyToken);
    }

    [Fact]
    public void EndOn_rejects_a_date_before_the_effective_start_date()
    {
        var plan = VesselManningPlan.Create(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), Guid.NewGuid(), CreatedAtUtc);

        Assert.Throws<InvalidVesselManningPlanEndDateException>(() => plan.EndOn(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void EndOn_rejects_a_date_after_the_current_end_date()
    {
        var plan = VesselManningPlan.Create(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), Guid.NewGuid(), CreatedAtUtc);

        Assert.Throws<InvalidVesselManningPlanEndDateException>(() => plan.EndOn(new DateOnly(2026, 4, 1)));
    }

    [Fact]
    public void Touch_bumps_the_concurrency_token_without_changing_dates()
    {
        var plan = VesselManningPlan.Create(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), Guid.NewGuid(), CreatedAtUtc);
        var previousToken = plan.ConcurrencyToken;

        plan.Touch();

        Assert.NotEqual(previousToken, plan.ConcurrencyToken);
        Assert.Equal(new DateOnly(2026, 3, 31), plan.EffectiveEndDate);
    }
}
