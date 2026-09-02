using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class RegisterMedicalExaminationEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterMedicalExaminationEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Post_registers_and_persists_a_medical_examination()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO900011");
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/medical-examinations", request);
        var result = await response.Content.ReadFromJsonAsync<RegisterMedicalExaminationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(
            $"/api/crew/members/{memberId}/medical-examinations/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var exam = await dbContext.Set<MedicalExamination>()
            .SingleAsync(candidate => candidate.Id == result.Id);

        Assert.Equal(memberId, exam.CrewMemberId);
        Assert.Equal(request.ExamType, exam.ExamType);
        Assert.Equal(request.Clinic, exam.Clinic);
        Assert.Equal(request.FitnessResult, exam.FitnessResult);
    }

    [Fact]
    public async Task Post_registers_an_already_expired_historical_examination()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO900012");
        var request = CreateValidRequest() with
        {
            ExamDate = new DateOnly(2015, 1, 1),
            ExpiryDate = new DateOnly(2016, 1, 1),
        };

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/medical-examinations", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_unsupported_fitness_result()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO900013");
        var request = CreateValidRequest() with { FitnessResult = "Unknown" };

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/medical-examinations", request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(RegisterMedicalExaminationValidator.FitnessResultField, problem!.Errors);
    }

    [Fact]
    public async Task Post_returns_validation_problem_when_expiry_date_precedes_exam_date()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO900014");
        var request = CreateValidRequest() with
        {
            ExamDate = new DateOnly(2026, 6, 1),
            ExpiryDate = new DateOnly(2026, 5, 1),
        };

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/medical-examinations", request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(RegisterMedicalExaminationValidator.ExpiryDateField, problem!.Errors);
    }

    [Fact]
    public async Task Post_returns_not_found_for_an_unknown_crew_member()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{Guid.NewGuid()}/medical-examinations", request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, problem!.Status);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{Guid.NewGuid()}/medical-examinations", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_read_only_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync("IMO900015");
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/medical-examinations", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    private async Task<Guid> SeedMemberAsync(string imoSeafarerId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko", "Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            imoSeafarerId, "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return member.Id;
    }
}
