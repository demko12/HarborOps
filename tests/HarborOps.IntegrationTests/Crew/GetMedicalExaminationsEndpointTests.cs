using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class GetMedicalExaminationsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetMedicalExaminationsEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Get_returns_an_empty_page_when_no_examinations_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync("/api/crew/medical-examinations");
        var page = await response.Content.ReadFromJsonAsync<GetMedicalExaminationsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, page!.TotalCount);
    }

    [Fact]
    public async Task Get_returns_registered_examinations_with_crew_member_details()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var (memberId, _) = await SeedExaminationAsync();

        using var response = await client.GetAsync("/api/crew/medical-examinations");
        var page = await response.Content.ReadFromJsonAsync<GetMedicalExaminationsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(page!.Items);
        Assert.Equal(memberId, item.CrewMemberId);
        Assert.Equal("Marko Petrovic", item.CrewMemberName);
        Assert.Equal("Active", item.EmploymentStatus);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_unsupported_fitness_result()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync("/api/crew/medical-examinations?fitnessResult=Unknown");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(GetMedicalExaminationsValidator.FitnessResultField, problem!.Errors);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync("/api/crew/medical-examinations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.GetAsync("/api/crew/medical-examinations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<(Guid MemberId, Guid ExaminationId)> SeedExaminationAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO900001", "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");
        var exam = MedicalExamination.Create(
            member.Id, "Periodic", "Clinic A", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), CrewFitnessResults.Fit);

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.Set<MedicalExamination>().AddAsync(exam);
        await dbContext.SaveChangesAsync();

        return (member.Id, exam.Id);
    }
}
