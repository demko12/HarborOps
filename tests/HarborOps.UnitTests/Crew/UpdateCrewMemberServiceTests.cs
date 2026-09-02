using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class UpdateCrewMemberServiceTests
{
    [Fact]
    public async Task UpdateAsync_updates_mutable_fields_and_persists_them()
    {
        await using var dbContext = CreateDbContext();
        var (member, _) = await SeedMemberAsync(dbContext);
        var newRankId = await SeedPositionAsync(dbContext, "Chief Engineer");
        var service = new UpdateCrewMemberService(dbContext);
        var request = CreateValidRequest(newRankId) with { FirstName = "Marko", LastName = "Marković" };

        await service.UpdateAsync(member.Id, request, CancellationToken.None);

        var updated = await dbContext.Set<CrewMember>().SingleAsync(m => m.Id == member.Id, CancellationToken.None);
        Assert.Equal("Marko", updated.FirstName);
        Assert.Equal("Marković", updated.LastName);
        Assert.Equal(newRankId, updated.BaseRankId);
        Assert.Equal(new DateOnly(1988, 3, 2), updated.DateOfBirth);
        Assert.Equal("Serbia", updated.Nationality);
        Assert.Equal("P9999999", updated.PassportNumber);
        Assert.Equal(new DateOnly(2031, 1, 1), updated.PassportExpiry);
        Assert.Equal("CDC9999999", updated.CdcNumber);
        Assert.Equal(new DateOnly(2021, 6, 1), updated.HireDate);
    }

    [Fact]
    public async Task UpdateAsync_leaves_the_imo_seafarer_id_untouched()
    {
        await using var dbContext = CreateDbContext();
        var (member, _) = await SeedMemberAsync(dbContext);
        var originalImoSeafarerId = member.ImoSeafarerId;
        var service = new UpdateCrewMemberService(dbContext);
        var request = CreateValidRequest(member.BaseRankId);

        await service.UpdateAsync(member.Id, request, CancellationToken.None);

        var updated = await dbContext.Set<CrewMember>().SingleAsync(m => m.Id == member.Id, CancellationToken.None);
        Assert.Equal(originalImoSeafarerId, updated.ImoSeafarerId);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_unknown_crew_member()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedPositionAsync(dbContext, "Bosun");
        var service = new UpdateCrewMemberService(dbContext);
        var request = CreateValidRequest(rankId);

        await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => service.UpdateAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_unknown_base_rank()
    {
        await using var dbContext = CreateDbContext();
        var (member, _) = await SeedMemberAsync(dbContext);
        var service = new UpdateCrewMemberService(dbContext);
        var request = CreateValidRequest(Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<CrewPositionNotFoundException>(
            () => service.UpdateAsync(member.Id, request, CancellationToken.None));

        Assert.Equal(request.BaseRankId, exception.BaseRankId);
    }

    [Fact]
    public async Task UpdateAsync_rejects_invalid_requests_before_saving()
    {
        await using var dbContext = CreateDbContext();
        var (member, _) = await SeedMemberAsync(dbContext);
        var service = new UpdateCrewMemberService(dbContext);
        var request = CreateValidRequest(member.BaseRankId) with { FirstName = " " };

        await Assert.ThrowsAsync<UpdateCrewMemberValidationException>(
            () => service.UpdateAsync(member.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_treats_an_all_unchanged_request_as_a_no_op()
    {
        await using var dbContext = CreateDbContext();
        var (member, rankId) = await SeedMemberAsync(dbContext);
        var service = new UpdateCrewMemberService(dbContext);
        var request = new UpdateCrewMemberRequest
        {
            FirstName = member.FirstName,
            LastName = member.LastName,
            DateOfBirth = member.DateOfBirth,
            Nationality = member.Nationality,
            PassportNumber = member.PassportNumber,
            PassportExpiry = member.PassportExpiry,
            CdcNumber = member.CdcNumber,
            HireDate = member.HireDate,
            BaseRankId = rankId,
        };

        var response = await service.UpdateAsync(member.Id, request, CancellationToken.None);

        Assert.Equal(member.Id, response.Id);
        var unchanged = await dbContext.Set<CrewMember>().SingleAsync(m => m.Id == member.Id, CancellationToken.None);
        Assert.Equal(member.FirstName, unchanged.FirstName);
        Assert.Equal(member.LastName, unchanged.LastName);
    }

    private static UpdateCrewMemberRequest CreateValidRequest(Guid baseRankId)
    {
        return new UpdateCrewMemberRequest
        {
            FirstName = "Ivan",
            LastName = "Petrović",
            DateOfBirth = new DateOnly(1988, 3, 2),
            Nationality = "Serbia",
            PassportNumber = "P9999999",
            PassportExpiry = new DateOnly(2031, 1, 1),
            CdcNumber = "CDC9999999",
            HireDate = new DateOnly(2021, 6, 1),
            BaseRankId = baseRankId,
        };
    }

    private static async Task<Guid> SeedPositionAsync(HarborOpsDbContext dbContext, string rankName)
    {
        var position = CrewPosition.Create(rankName, "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position.Id;
    }

    private static async Task<(CrewMember Member, Guid RankId)> SeedMemberAsync(HarborOpsDbContext dbContext)
    {
        var rankId = await SeedPositionAsync(dbContext, "Bosun");

        var member = CrewMember.Create(
            rankId,
            "Ana",
            "Petrović",
            new DateOnly(1990, 1, 1),
            "Montenegro",
            $"IMO{Guid.NewGuid():N}"[..10],
            "P1234567",
            new DateOnly(2030, 1, 1),
            "CDC1234567",
            new DateOnly(2020, 1, 1),
            "Active");

        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return (member, rankId);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
