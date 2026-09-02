using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HarborOps.UnitTests.Crew;

public sealed class GetMedicalExaminationsQueryTests
{
    private static readonly DateTimeOffset Today = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_filters_by_search_on_crew_member_name()
    {
        await using var dbContext = CreateDbContext();
        var matching = await SeedMemberAsync(dbContext, "Ana Petrovic");
        var other = await SeedMemberAsync(dbContext, "Marko Ivanovic");
        await SeedExamAsync(dbContext, matching.Id, new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1));
        await SeedExamAsync(dbContext, other.Id, new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1));

        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(
            new GetMedicalExaminationsRequest { Search = "Ana" }, CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(matching.Id, Assert.Single(response.Items).CrewMemberId);
    }

    [Fact]
    public async Task GetAsync_filters_by_fitness_result()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, "Ana Petrovic");
        await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1), CrewFitnessResults.Fit);
        await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 5, 1), new DateOnly(2027, 5, 1), CrewFitnessResults.Unfit);

        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(
            new GetMedicalExaminationsRequest { FitnessResult = CrewFitnessResults.Unfit }, CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(CrewFitnessResults.Unfit, Assert.Single(response.Items).FitnessResult);
    }

    [Fact]
    public async Task GetAsync_filters_by_expired()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, "Ana Petrovic");
        var expired = await SeedExamAsync(dbContext, member.Id, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1));
        var valid = await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));

        var query = CreateQuery(dbContext);

        var expiredResponse = await query.GetAsync(
            new GetMedicalExaminationsRequest { Expired = true }, CancellationToken.None);
        var validResponse = await query.GetAsync(
            new GetMedicalExaminationsRequest { Expired = false }, CancellationToken.None);

        Assert.Equal(expired.Id, Assert.Single(expiredResponse.Items).MedicalExaminationId);
        Assert.Equal(valid.Id, Assert.Single(validResponse.Items).MedicalExaminationId);
    }

    [Fact]
    public async Task GetAsync_filters_by_expiring_within_days_independently_of_the_configured_default()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, "Ana Petrovic");
        var soon = await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 20));
        var later = await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1));

        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(
            new GetMedicalExaminationsRequest { ExpiringWithinDays = 10 }, CancellationToken.None);

        Assert.Equal(soon.Id, Assert.Single(response.Items).MedicalExaminationId);
        Assert.NotEqual(later.Id, response.Items[0].MedicalExaminationId);
    }

    [Fact]
    public async Task GetAsync_computes_is_expiring_soon_from_the_configured_default_not_the_request_window()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, "Ana Petrovic");
        await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 20));

        var query = CreateQuery(dbContext, expiringSoonWithinDays: 30);

        var response = await query.GetAsync(
            new GetMedicalExaminationsRequest { ExpiringWithinDays = 400 }, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.True(item.IsExpiringSoon);
        Assert.False(item.IsExpired);
    }

    [Fact]
    public async Task GetAsync_orders_expired_first_then_expiry_date_then_member_name_then_id()
    {
        await using var dbContext = CreateDbContext();
        var zoran = await SeedMemberAsync(dbContext, "Zoran Ivanovic");
        var ana = await SeedMemberAsync(dbContext, "Ana Petrovic");

        var validLater = await SeedExamAsync(dbContext, zoran.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1));
        var validSooner = await SeedExamAsync(dbContext, ana.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1));
        var expired = await SeedExamAsync(dbContext, zoran.Id, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1));

        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(new GetMedicalExaminationsRequest(), CancellationToken.None);

        Assert.Equal(3, response.Items.Count);
        Assert.Equal(expired.Id, response.Items[0].MedicalExaminationId);
        Assert.Equal(validSooner.Id, response.Items[1].MedicalExaminationId);
        Assert.Equal(validLater.Id, response.Items[2].MedicalExaminationId);
    }

    [Fact]
    public async Task GetAsync_returns_the_requested_page()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, "Ana Petrovic");

        for (var index = 1; index <= 5; index++)
        {
            await SeedExamAsync(dbContext, member.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 7, index));
        }

        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(
            new GetMedicalExaminationsRequest { PageNumber = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task GetAsync_returns_empty_when_nothing_matches()
    {
        await using var dbContext = CreateDbContext();
        var query = CreateQuery(dbContext);

        var response = await query.GetAsync(new GetMedicalExaminationsRequest(), CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetAsync_rejects_unsupported_fitness_result()
    {
        await using var dbContext = CreateDbContext();
        var query = CreateQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetMedicalExaminationsValidationException>(
            () => query.GetAsync(
                new GetMedicalExaminationsRequest { FitnessResult = "Unknown" }, CancellationToken.None));

        Assert.Contains(GetMedicalExaminationsValidator.FitnessResultField, exception.Errors);
    }

    [Fact]
    public async Task GetAsync_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = CreateQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetMedicalExaminationsValidationException>(
            () => query.GetAsync(
                new GetMedicalExaminationsRequest { PageSize = GetMedicalExaminationsValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(GetMedicalExaminationsValidator.PageSizeField, exception.Errors);
    }

    private static GetMedicalExaminationsQuery CreateQuery(HarborOpsDbContext dbContext, int expiringSoonWithinDays = 30)
    {
        return new GetMedicalExaminationsQuery(
            dbContext,
            new FixedTimeProvider(Today),
            Options.Create(new MedicalExaminationOptions { ExpiringSoonWithinDays = expiringSoonWithinDays }));
    }

    private static async Task<CrewMember> SeedMemberAsync(HarborOpsDbContext dbContext, string name)
    {
        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);

        var nameParts = name.Split(' ', 2);
        var member = CrewMember.Create(
            position.Id,
            nameParts[0],
            nameParts.Length > 1 ? nameParts[1] : string.Empty,
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
