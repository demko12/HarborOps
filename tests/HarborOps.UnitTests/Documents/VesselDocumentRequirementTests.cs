using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class VesselDocumentRequirementTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_starts_enabled_at_revision_one()
    {
        var actorId = Guid.NewGuid();

        var requirement = VesselDocumentRequirement.Create(Guid.NewGuid(), 3, 30, "Keep a copy on board.", actorId, CreatedAtUtc);

        Assert.True(requirement.IsEnabled);
        Assert.Equal(1, requirement.Revision);
        Assert.Equal(30, requirement.MinimumRemainingValidityDays);
        Assert.Equal(actorId, requirement.CreatedByUserId);
        Assert.Equal(actorId, requirement.UpdatedByUserId);
        Assert.NotEqual(Guid.Empty, requirement.ConcurrencyToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(365)]
    public void Create_accepts_the_validity_buffer_bounds(int days)
    {
        var requirement = VesselDocumentRequirement.Create(Guid.NewGuid(), 3, days, null, Guid.NewGuid(), CreatedAtUtc);

        Assert.Equal(days, requirement.MinimumRemainingValidityDays);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void Create_rejects_a_validity_buffer_outside_the_bounds(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VesselDocumentRequirement.Create(Guid.NewGuid(), 3, days, null, Guid.NewGuid(), CreatedAtUtc));
    }

    [Fact]
    public void Create_rejects_an_instruction_longer_than_the_limit()
    {
        var instruction = new string('x', VesselDocumentRequirement.InstructionMaxLength + 1);

        Assert.Throws<ArgumentException>(
            () => VesselDocumentRequirement.Create(Guid.NewGuid(), 3, 0, instruction, Guid.NewGuid(), CreatedAtUtc));
    }

    [Fact]
    public void Update_changes_settings_and_advances_revision_and_version()
    {
        var requirement = VesselDocumentRequirement.Create(Guid.NewGuid(), 3, 0, null, Guid.NewGuid(), CreatedAtUtc);
        var previousVersion = requirement.ConcurrencyToken;
        var actorId = Guid.NewGuid();
        var updatedAtUtc = CreatedAtUtc.AddHours(1);

        requirement.Update(false, 60, "Renew early.", actorId, updatedAtUtc);

        Assert.False(requirement.IsEnabled);
        Assert.Equal(60, requirement.MinimumRemainingValidityDays);
        Assert.Equal("Renew early.", requirement.Instruction);
        Assert.Equal(2, requirement.Revision);
        Assert.Equal(actorId, requirement.UpdatedByUserId);
        Assert.Equal(updatedAtUtc, requirement.UpdatedAtUtc);
        Assert.NotEqual(previousVersion, requirement.ConcurrencyToken);
    }

    [Fact]
    public void Update_can_enable_a_disabled_requirement()
    {
        var requirement = VesselDocumentRequirement.Create(Guid.NewGuid(), 3, 0, null, Guid.NewGuid(), CreatedAtUtc);
        requirement.Disable(Guid.NewGuid(), CreatedAtUtc);

        requirement.Update(true, 0, null, Guid.NewGuid(), CreatedAtUtc);

        Assert.True(requirement.IsEnabled);
        Assert.Equal(3, requirement.Revision);
    }

    [Fact]
    public void Disable_rejects_an_already_disabled_requirement()
    {
        var requirement = VesselDocumentRequirement.Create(Guid.NewGuid(), 3, 0, null, Guid.NewGuid(), CreatedAtUtc);
        requirement.Disable(Guid.NewGuid(), CreatedAtUtc);

        Assert.Throws<InvalidOperationException>(() => requirement.Disable(Guid.NewGuid(), CreatedAtUtc));
    }

    [Fact]
    public void History_records_the_resulting_state_of_each_revision()
    {
        var requirement = VesselDocumentRequirement.Create(Guid.NewGuid(), 3, 10, null, Guid.NewGuid(), CreatedAtUtc);
        var actorId = Guid.NewGuid();
        requirement.Disable(actorId, CreatedAtUtc.AddDays(1));

        var entry = VesselDocumentRequirementHistory.RecordDisabled(requirement, "Vessel sold.");

        Assert.Equal(requirement.Id, entry.RequirementId);
        Assert.Equal(2, entry.Revision);
        Assert.Equal(VesselDocumentRequirementHistory.DisabledEvent, entry.EventType);
        Assert.Equal("Vessel sold.", entry.Reason);
        Assert.False(entry.IsEnabled);
        Assert.Equal(10, entry.MinimumRemainingValidityDays);
        Assert.Equal(actorId, entry.ActorUserId);
        Assert.Equal(CreatedAtUtc.AddDays(1), entry.OccurredAtUtc);
    }

    [Fact]
    public void History_requires_a_reason_for_every_event()
    {
        var requirement = VesselDocumentRequirement.Create(Guid.NewGuid(), 3, 10, null, Guid.NewGuid(), CreatedAtUtc);

        Assert.Throws<ArgumentException>(() => VesselDocumentRequirementHistory.RecordCreated(requirement, " "));
        Assert.Throws<ArgumentException>(() => VesselDocumentRequirementHistory.RecordUpdated(requirement, " "));
        Assert.Throws<ArgumentException>(() => VesselDocumentRequirementHistory.RecordDisabled(requirement, string.Empty));
    }
}
