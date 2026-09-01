using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class NonConformityWorkflowEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public NonConformityWorkflowEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Register_add_corrective_action_and_close_reaches_a_consistent_final_state()
    {
        var pscId = await SeedPscInspectionAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var registerResponse = await client.PostAsJsonAsync(
            "/api/compliance/non-conformities",
            new RegisterNonConformityRequest
            {
                SurveyId = null,
                PscId = pscId,
                EvidenceDocumentId = Guid.NewGuid(),
                Severity = NonConformity.SeverityMajor,
                CorrectiveActionDeadline = new DateOnly(2026, 12, 1),
            },
            CancellationToken.None);
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterNonConformityResponse>();
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        Assert.NotNull(registered);

        using var actionResponse = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{registered.Id}/corrective-actions",
            new RecordCorrectiveActionRequest("Root cause identified and fixed.", null),
            CancellationToken.None);
        var action = await actionResponse.Content.ReadFromJsonAsync<RecordCorrectiveActionResponse>();
        Assert.Equal(HttpStatusCode.Created, actionResponse.StatusCode);
        Assert.NotNull(action);
        Assert.Equal(NonConformity.ClosureStatusInProgress, action.ClosureStatus);

        using var closeResponse = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{registered.Id}/close",
            new CloseNonConformityRequest("Verified and closed out.", null),
            CancellationToken.None);
        var closed = await closeResponse.Content.ReadFromJsonAsync<CloseNonConformityResponse>();
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        Assert.NotNull(closed);
        Assert.Equal(NonConformity.ClosureStatusClosed, closed.ClosureStatus);

        using var detailResponse = await client.GetAsync($"/api/compliance/non-conformities/{registered.Id}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<GetNonConformityDetailResponse>();

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.NotNull(detail);
        Assert.Equal(NonConformity.ClosureStatusClosed, detail.ClosureStatus);
        Assert.Single(detail.CorrectiveActions);
        Assert.Equal("Verified and closed out.", detail.ClosureNotes);
        Assert.NotNull(detail.ClosedAtUtc);
        Assert.NotNull(detail.ClosedByUserId);
        Assert.False(detail.IsOverdue);
    }

    private async Task<int> SeedPscInspectionAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var inspection = new PscInspection(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), "Port Authority", 1, false);
        await dbContext.AddAsync(inspection);
        await dbContext.SaveChangesAsync();
        return inspection.Id;
    }
}
