using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class RegisterMedicalExaminationServiceTests
{
    [Fact]
    public async Task RegisterAsync_saves_the_examination_and_returns_its_id()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        var service = new RegisterMedicalExaminationService(dbContext);
        var request = CreateValidRequest();

        var response = await service.RegisterAsync(member.Id, request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);

        var persisted = await dbContext.Set<MedicalExamination>()
            .SingleAsync(exam => exam.Id == response.Id, CancellationToken.None);
        Assert.Equal(member.Id, persisted.CrewMemberId);
        Assert.Equal("Periodic", persisted.ExamType);
        Assert.Equal("Clinic A", persisted.Clinic);
        Assert.Equal(request.ExamDate, persisted.ExamDate);
        Assert.Equal(request.ExpiryDate, persisted.ExpiryDate);
        Assert.Equal(CrewFitnessResults.Fit, persisted.FitnessResult);
    }

    [Fact]
    public async Task RegisterAsync_rejects_an_unknown_crew_member()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterMedicalExaminationService(dbContext);
        var request = CreateValidRequest();

        await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => service.RegisterAsync(Guid.NewGuid(), request, CancellationToken.None));

        Assert.Empty(await dbContext.Set<MedicalExamination>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_rejects_invalid_requests_before_saving()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        var service = new RegisterMedicalExaminationService(dbContext);
        var request = CreateValidRequest() with { FitnessResult = "Unknown" };

        await Assert.ThrowsAsync<RegisterMedicalExaminationValidationException>(
            () => service.RegisterAsync(member.Id, request, CancellationToken.None));

        Assert.Empty(await dbContext.Set<MedicalExamination>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_saves_an_already_expired_historical_examination_without_being_blocked()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        var service = new RegisterMedicalExaminationService(dbContext);
        var request = CreateValidRequest() with
        {
            ExamDate = new DateOnly(2015, 1, 1),
            ExpiryDate = new DateOnly(2016, 1, 1),
        };

        var response = await service.RegisterAsync(member.Id, request, CancellationToken.None);

        var persisted = await dbContext.Set<MedicalExamination>()
            .SingleAsync(exam => exam.Id == response.Id, CancellationToken.None);
        Assert.Equal(new DateOnly(2015, 1, 1), persisted.ExamDate);
        Assert.Equal(new DateOnly(2016, 1, 1), persisted.ExpiryDate);
    }

    private static RegisterMedicalExaminationRequest CreateValidRequest()
    {
        return new RegisterMedicalExaminationRequest
        {
            ExamType = "Periodic",
            Clinic = "Clinic A",
            ExamDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2027, 1, 1),
            FitnessResult = CrewFitnessResults.Fit,
        };
    }

    private static async Task<CrewMember> SeedMemberAsync(HarborOpsDbContext dbContext)
    {
        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);

        var member = CrewMember.Create(
            position.Id,
            "Ana Petrovic",
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

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
