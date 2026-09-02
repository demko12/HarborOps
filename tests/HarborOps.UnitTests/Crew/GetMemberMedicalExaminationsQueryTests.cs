using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HarborOps.UnitTests.Crew;

public sealed class GetMemberMedicalExaminationsQueryTests
{
    private static readonly DateTimeOffset Today = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_throws_when_crew_member_not_found()
    {
        await using var dbContext = CreateDbContext();
        var query = CreateQuery(dbContext);

        var exception = await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => query.GetAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.NotEqual(Guid.Empty, exception.CrewMemberId);
    }

    [Fact]
    public async Task GetAsync_returns_empty_history_and_null_current_fitness_when_no_exams_exist()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Null(response.CurrentFitnessResult);
    }

    [Fact]
    public async Task GetAsync_orders_newest_examination_first()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        var older = await SeedExamAsync(dbContext, member.Id, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var newer = await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 3, 1), new DateOnly(2027, 3, 1));

        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(newer.Id, response.Items[0].MedicalExaminationId);
        Assert.Equal(older.Id, response.Items[1].MedicalExaminationId);
    }

    [Fact]
    public async Task GetAsync_derives_current_fitness_from_the_latest_non_future_examination()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        await SeedExamAsync(dbContext, member.Id, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), CrewFitnessResults.Unfit);
        await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 5, 1), new DateOnly(2027, 5, 1), CrewFitnessResults.FitWithRestrictions);

        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Equal(CrewFitnessResults.FitWithRestrictions, response.CurrentFitnessResult);
    }

    [Fact]
    public async Task GetAsync_excludes_future_dated_exams_from_current_fitness_but_still_lists_them()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 5, 1), new DateOnly(2027, 5, 1), CrewFitnessResults.Fit);
        var future = await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 12, 1), new DateOnly(2027, 12, 1), CrewFitnessResults.Unfit);

        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Equal(CrewFitnessResults.Fit, response.CurrentFitnessResult);
        Assert.Contains(response.Items, item => item.MedicalExaminationId == future.Id);
    }

    [Fact]
    public async Task GetAsync_projects_is_expired_and_is_expiring_soon_per_row()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        await SeedExamAsync(dbContext, member.Id, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1));
        await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 20));

        var query = CreateQuery(dbContext, expiringSoonWithinDays: 30);

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.True(response.Items.Single(item => item.ExpiryDate == new DateOnly(2025, 1, 1)).IsExpired);
        Assert.True(response.Items.Single(item => item.ExpiryDate == new DateOnly(2026, 6, 20)).IsExpiringSoon);
    }

    private static GetMemberMedicalExaminationsQuery CreateQuery(HarborOpsDbContext dbContext, int expiringSoonWithinDays = 30)
    {
        return new GetMemberMedicalExaminationsQuery(
            dbContext,
            new FixedTimeProvider(Today),
            Options.Create(new MedicalExaminationOptions { ExpiringSoonWithinDays = expiringSoonWithinDays }));
    }

    private static async Task<CrewMember> SeedMemberAsync(HarborOpsDbContext dbContext)
    {
        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);

        var member = CrewMember.Create(
            position.Id,
            "Ana",
            "Petrovic",
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

        return member;
    }

    private static async Task<MedicalExamination> SeedExamAsync(
        HarborOpsDbContext dbContext,
        Guid crewMemberId,
        DateOnly examDate,
        DateOnly expiryDate,
        string fitnessResult = CrewFitnessResults.Fit)
    {
        var exam = MedicalExamination.Create(crewMemberId, "Periodic", "Clinic A", examDate, expiryDate, fitnessResult);
        await dbContext.Set<MedicalExamination>().AddAsync(exam);
        await dbContext.SaveChangesAsync();

        return exam;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
