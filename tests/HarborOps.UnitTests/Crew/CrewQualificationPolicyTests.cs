using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class CrewQualificationPolicyTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_sets_fields_and_a_fresh_concurrency_token()
    {
        var crewPositionId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var policy = CrewQualificationPolicy.Create(crewPositionId, enabled: true, createdBy, CreatedAtUtc);

        Assert.Equal(crewPositionId, policy.CrewPositionId);
        Assert.True(policy.Enabled);
        Assert.Equal(createdBy, policy.CreatedByUserId);
        Assert.NotEqual(Guid.Empty, policy.ConcurrencyToken);
    }

    [Fact]
    public void Update_changes_enabled_and_bumps_the_concurrency_token()
    {
        var policy = CrewQualificationPolicy.Create(Guid.NewGuid(), enabled: true, Guid.NewGuid(), CreatedAtUtc);
        var previousToken = policy.ConcurrencyToken;

        policy.Update(false);

        Assert.False(policy.Enabled);
        Assert.NotEqual(previousToken, policy.ConcurrencyToken);
    }

    [Fact]
    public void Touch_bumps_the_concurrency_token_without_changing_enabled()
    {
        var policy = CrewQualificationPolicy.Create(Guid.NewGuid(), enabled: true, Guid.NewGuid(), CreatedAtUtc);
        var previousToken = policy.ConcurrencyToken;

        policy.Touch();

        Assert.True(policy.Enabled);
        Assert.NotEqual(previousToken, policy.ConcurrencyToken);
    }
}
