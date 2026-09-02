using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class GetMemberMedicalExaminationsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetMemberMedicalExaminationsEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Get_returns_empty_history_and_null_current_fitness_for_a_member_without_exams()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync();

        using var response = await client.GetAsync($"/api/crew/members/{memberId}/medical-examinations");
        var result = await response.Content.ReadFromJsonAsync<GetMemberMedicalExaminationsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(result!.Items);
        Assert.Null(result.CurrentFitnessResult);
    }

    [Fact]
    public async Task Get_returns_history_and_current_fitness_for_a_member_with_exams()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var (memberId, examId) = await SeedMemberWithExamAsync();

        using var response = await client.GetAsync($"/api/crew/members/{memberId}/medical-examinations");
        var result = await response.Content.ReadFromJsonAsync<GetMemberMedicalExaminationsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(examId, Assert.Single(result!.Items).MedicalExaminationId);
        Assert.Equal(CrewFitnessResults.Fit, result.CurrentFitnessResult);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_crew_member()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync($"/api/crew/members/{Guid.NewGuid()}/medical-examinations");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync($"/api/crew/members/{Guid.NewGuid()}/medical-examinations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.GetAsync($"/api/crew/members/{Guid.NewGuid()}/medical-examinations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedMemberAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko", "Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO900002", "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return member.Id;
    }

    private async Task<(Guid MemberId, Guid ExaminationId)> SeedMemberWithExamAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko", "Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO900003", "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var exam = MedicalExamination.Create(
            member.Id, "Periodic", "Clinic A", today.AddDays(-30), today.AddYears(1), CrewFitnessResults.Fit);

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.Set<MedicalExamination>().AddAsync(exam);
        await dbContext.SaveChangesAsync();

        return (member.Id, exam.Id);
    }
}
