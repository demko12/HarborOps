using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class CrewQualificationRequirementTests
{
    [Fact]
    public void Create_sets_fields()
    {
        var policyId = Guid.NewGuid();

        var requirement = CrewQualificationRequirement.Create(policyId, "STCW-II/1");

        Assert.Equal(policyId, requirement.PolicyId);
        Assert.Equal("STCW-II/1", requirement.RequiredType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_an_empty_required_type(string? requiredType)
    {
        Assert.ThrowsAny<ArgumentException>(() => CrewQualificationRequirement.Create(Guid.NewGuid(), requiredType!));
    }
}
