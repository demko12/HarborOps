using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

public sealed class DocumentRequirementsEndpointTests : IntegrationTestBase
{
    private const string RequirementsPath = "/api/documents/requirements";

    private readonly HarborOpsApiFactory _application;

    public DocumentRequirementsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            RequirementsPath, CreateRequest(Guid.NewGuid(), 1), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();

        using var response = await client.PostAsJsonAsync(
            RequirementsPath, CreateRequest(vessel.Id, documentType.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_201_and_validates_the_vessel_through_the_fleet_lookup()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();

        using var response = await client.PostAsJsonAsync(
            RequirementsPath, CreateRequest(vessel.Id, documentType.Id), CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<DocumentRequirementListItem>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal($"{RequirementsPath}/{result!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(vessel.Id, result.VesselId);
        Assert.Equal(1, result.Revision);
    }

    [Fact]
    public async Task Post_returns_404_when_the_vessel_does_not_exist_in_fleet()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);
        var documentType = await SeedDocumentTypeAsync();

        using var response = await client.PostAsJsonAsync(
            RequirementsPath, CreateRequest(Guid.NewGuid(), documentType.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_400_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);

        using var response = await client.PostAsJsonAsync(
            RequirementsPath,
            new CreateDocumentRequirementRequest { MinimumRemainingValidityDays = 366 },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(DocumentRequirementValidator.VesselIdField, problem!.Errors);
        Assert.Contains(DocumentRequirementValidator.MinimumRemainingValidityDaysField, problem.Errors);
        Assert.Contains(DocumentRequirementValidator.ReasonField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_409_for_a_duplicate_vessel_and_type()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();
        using var first = await client.PostAsJsonAsync(
            RequirementsPath, CreateRequest(vessel.Id, documentType.Id), CancellationToken.None);

        using var second = await client.PostAsJsonAsync(
            RequirementsPath, CreateRequest(vessel.Id, documentType.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Get_list_and_details_require_only_the_read_permission()
    {
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();
        var requirement = await SeedRequirementAsync(vessel.Id, documentType.Id);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var listResponse = await client.GetAsync($"{RequirementsPath}?vesselId={vessel.Id}", CancellationToken.None);
        var list = await listResponse.Content.ReadFromJsonAsync<GetDocumentRequirementsResponse>(CancellationToken.None);
        using var detailsResponse = await client.GetAsync($"{RequirementsPath}/{requirement.Id}", CancellationToken.None);
        var details = await detailsResponse.Content.ReadFromJsonAsync<DocumentRequirementDetailsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Single(list!.Items);
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        Assert.Single(details!.History);
    }

    [Fact]
    public async Task Put_with_a_stale_version_returns_409_and_the_current_version_succeeds()
    {
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();
        var requirement = await SeedRequirementAsync(vessel.Id, documentType.Id);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);

        using var stale = await client.PutAsJsonAsync(
            $"{RequirementsPath}/{requirement.Id}",
            UpdateRequest(Guid.NewGuid()),
            CancellationToken.None);
        using var current = await client.PutAsJsonAsync(
            $"{RequirementsPath}/{requirement.Id}",
            UpdateRequest(requirement.ConcurrencyToken),
            CancellationToken.None);
        using var repeated = await client.PutAsJsonAsync(
            $"{RequirementsPath}/{requirement.Id}",
            UpdateRequest(requirement.ConcurrencyToken),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
    }

    [Fact]
    public async Task Put_without_manage_permission_returns_403()
    {
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();
        var requirement = await SeedRequirementAsync(vessel.Id, documentType.Id);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.PutAsJsonAsync(
            $"{RequirementsPath}/{requirement.Id}",
            UpdateRequest(requirement.ConcurrencyToken),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_disable_disables_the_requirement_and_a_second_disable_returns_409()
    {
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();
        var requirement = await SeedRequirementAsync(vessel.Id, documentType.Id);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);

        using var response = await client.PostAsJsonAsync(
            $"{RequirementsPath}/{requirement.Id}/disable",
            new DisableDocumentRequirementRequest { Reason = "Vessel sold.", Version = requirement.ConcurrencyToken },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<DocumentRequirementListItem>(CancellationToken.None);

        using var repeated = await client.PostAsJsonAsync(
            $"{RequirementsPath}/{requirement.Id}/disable",
            new DisableDocumentRequirementRequest { Reason = "Again.", Version = result!.Version },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result.IsEnabled);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
    }

    [Fact]
    public async Task Details_history_names_the_actor_by_email_through_the_identity_lookup()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Documents.Read,
            Permissions.Documents.Manage);
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();
        using var created = await client.PostAsJsonAsync(
            RequirementsPath, CreateRequest(vessel.Id, documentType.Id), CancellationToken.None);
        var requirement = await created.Content.ReadFromJsonAsync<DocumentRequirementListItem>(CancellationToken.None);

        var details = await client.GetFromJsonAsync<DocumentRequirementDetailsResponse>(
            $"{RequirementsPath}/{requirement!.Id}", CancellationToken.None);

        var entry = Assert.Single(details!.History);
        Assert.Equal(requirement.UpdatedByUserId, entry.ActorUserId);
        Assert.False(string.IsNullOrWhiteSpace(entry.ActorEmail));
    }

    [Fact]
    public async Task Get_details_returns_404_for_an_unknown_requirement()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync($"{RequirementsPath}/{Guid.NewGuid()}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static CreateDocumentRequirementRequest CreateRequest(Guid vesselId, int documentTypeId) => new()
    {
        VesselId = vesselId,
        DocumentTypeId = documentTypeId,
        MinimumRemainingValidityDays = 30,
        Reason = "Company requirement.",
    };

    private static UpdateDocumentRequirementRequest UpdateRequest(Guid version) => new()
    {
        IsEnabled = true,
        MinimumRemainingValidityDays = 60,
        Reason = "Charter requires 60 days.",
        Version = version,
    };

    private async Task<Vessel> SeedVesselAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");
        var vessel = Vessel.Register(Guid.NewGuid(), vesselType.Id, "North Star", "1000001", "Malta", "Active");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private async Task<DocumentType> SeedDocumentTypeAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var documentType = new DocumentType("Safety Management Certificate", 365, "Flag State Authority", requiresSurvey: true);

        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType;
    }

    private async Task<VesselDocumentRequirement> SeedRequirementAsync(Guid vesselId, int documentTypeId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var requirement = VesselDocumentRequirement.Create(vesselId, documentTypeId, 0, null, Guid.NewGuid(), DateTime.UtcNow);

        await dbContext.Set<VesselDocumentRequirement>().AddAsync(requirement);
        await dbContext.Set<VesselDocumentRequirementHistory>()
            .AddAsync(VesselDocumentRequirementHistory.RecordCreated(requirement, "Company requirement."));
        await dbContext.SaveChangesAsync();

        return requirement;
    }
}
