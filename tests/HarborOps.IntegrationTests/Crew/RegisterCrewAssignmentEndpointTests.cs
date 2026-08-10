using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;

namespace HarborOps.IntegrationTests.Crew;

public sealed class RegisterCrewAssignmentEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterCrewAssignmentEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/assignments",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_authentication_reaches_normal_validation_behavior()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = new RegisterCrewAssignmentRequest
        {
            CrewMemberId = null,
            VesselId = null,
            RankId = null,
            SignOnPortId = null,
            SignOnDate = null,
            PlannedSignOffDate = null,
        };

        using var response = await client.PostAsJsonAsync(
            "/api/crew/assignments",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterCrewAssignmentValidator.CrewMemberIdField, problem.Errors);
        Assert.Contains(RegisterCrewAssignmentValidator.VesselIdField, problem.Errors);
        Assert.Contains(RegisterCrewAssignmentValidator.RankIdField, problem.Errors);
        Assert.Contains(RegisterCrewAssignmentValidator.SignOnDateField, problem.Errors);
    }

    private static RegisterCrewAssignmentRequest CreateValidRequest()
    {
        return new RegisterCrewAssignmentRequest
        {
            CrewMemberId = Guid.NewGuid(),
            VesselId = Guid.NewGuid(),
            RankId = Guid.NewGuid(),
            SignOnPortId = null,
            SignOnDate = new DateOnly(2026, 1, 1),
            PlannedSignOffDate = new DateOnly(2026, 6, 1),
        };
    }
}

