using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class CrewMemberTests
{
    [Fact]
    public void UpdateProfile_updates_every_mutable_field()
    {
        var crewMember = CreateCrewMember();
        var newBaseRankId = Guid.NewGuid();

        crewMember.UpdateProfile(
            "Marko",
            "Marković",
            new DateOnly(1988, 3, 2),
            "Serbia",
            "P9999999",
            new DateOnly(2031, 1, 1),
            "CDC9999999",
            new DateOnly(2021, 6, 1),
            newBaseRankId);

        Assert.Equal("Marko", crewMember.FirstName);
        Assert.Equal("Marković", crewMember.LastName);
        Assert.Equal(new DateOnly(1988, 3, 2), crewMember.DateOfBirth);
        Assert.Equal("Serbia", crewMember.Nationality);
        Assert.Equal("P9999999", crewMember.PassportNumber);
        Assert.Equal(new DateOnly(2031, 1, 1), crewMember.PassportExpiry);
        Assert.Equal("CDC9999999", crewMember.CdcNumber);
        Assert.Equal(new DateOnly(2021, 6, 1), crewMember.HireDate);
        Assert.Equal(newBaseRankId, crewMember.BaseRankId);
    }

    [Fact]
    public void UpdateProfile_leaves_the_imo_seafarer_id_untouched()
    {
        var crewMember = CreateCrewMember();
        var originalImoSeafarerId = crewMember.ImoSeafarerId;

        crewMember.UpdateProfile(
            "Marko",
            "Marković",
            new DateOnly(1988, 3, 2),
            "Serbia",
            "P9999999",
            new DateOnly(2031, 1, 1),
            "CDC9999999",
            new DateOnly(2021, 6, 1),
            Guid.NewGuid());

        Assert.Equal(originalImoSeafarerId, crewMember.ImoSeafarerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void UpdateProfile_rejects_a_blank_first_name(string firstName)
    {
        var crewMember = CreateCrewMember();

        Assert.Throws<ArgumentException>(() => crewMember.UpdateProfile(
            firstName, "Marković", new DateOnly(1988, 3, 2), "Serbia",
            "P9999999", new DateOnly(2031, 1, 1), "CDC9999999", new DateOnly(2021, 6, 1), Guid.NewGuid()));
    }

    [Fact]
    public void UpdateProfile_rejects_an_empty_base_rank_id()
    {
        var crewMember = CreateCrewMember();

        Assert.Throws<ArgumentException>(() => crewMember.UpdateProfile(
            "Marko", "Marković", new DateOnly(1988, 3, 2), "Serbia",
            "P9999999", new DateOnly(2031, 1, 1), "CDC9999999", new DateOnly(2021, 6, 1), Guid.Empty));
    }

    [Fact]
    public void ChangeEmploymentStatus_sets_the_new_status()
    {
        var crewMember = CreateCrewMember();

        crewMember.ChangeEmploymentStatus("OnLeave");

        Assert.Equal("OnLeave", crewMember.EmploymentStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ChangeEmploymentStatus_rejects_a_blank_status(string status)
    {
        var crewMember = CreateCrewMember();

        Assert.Throws<ArgumentException>(() => crewMember.ChangeEmploymentStatus(status));
    }

    private static CrewMember CreateCrewMember() =>
        CrewMember.Create(
            Guid.NewGuid(),
            "Ivan",
            "Petrović",
            new DateOnly(1990, 5, 14),
            "Montenegro",
            $"IMO{Guid.NewGuid():N}"[..10],
            "P1234567",
            new DateOnly(2028, 1, 1),
            "CDC987654",
            new DateOnly(2024, 1, 15),
            "Active");
}
