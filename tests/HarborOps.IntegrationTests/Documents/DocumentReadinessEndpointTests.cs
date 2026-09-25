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

public sealed class DocumentReadinessEndpointTests : IntegrationTestBase
{
    private const string ReadinessPath = "/api/documents/readiness";

    private readonly HarborOpsApiFactory _application;

    public DocumentReadinessEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(ReadinessPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_documents_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.GetAsync(ReadinessPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_users_get_readiness_and_counts_without_unapproved_documents()
    {
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();
        await SeedRequirementAsync(vessel.Id, documentType.Id);
        await SeedDraftDocumentAsync(vessel.Id, documentType.Id);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync($"{ReadinessPath}?vesselId={vessel.Id}", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentReadinessResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(result!.Items);
        Assert.Equal(DocumentReadinessRules.NotReadyVesselStatus, result.VesselStatus);
        Assert.Equal(new DocumentReadinessSummary(1, 0, 1), result.Summary);
        Assert.Equal([DocumentReadinessRules.NotApproved, DocumentReadinessRules.MissingFile], item.Blockers);
        Assert.Empty(item.Candidates);
    }

    [Fact]
    public async Task Managers_see_the_unapproved_candidate()
    {
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();
        await SeedRequirementAsync(vessel.Id, documentType.Id);
        var draft = await SeedDraftDocumentAsync(vessel.Id, documentType.Id);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Documents.Read,
            Permissions.Documents.Manage);

        using var response = await client.GetAsync($"{ReadinessPath}?vesselId={vessel.Id}", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentReadinessResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(draft.Id, Assert.Single(Assert.Single(result!.Items).Candidates).DocumentId);
    }

    [Fact]
    public async Task Recipients_are_listed_for_managers_and_hidden_from_read_only_users()
    {
        var vessel = await SeedVesselAsync();
        var documentType = await SeedDocumentTypeAsync();
        await SeedRequirementAsync(vessel.Id, documentType.Id);
        await SeedDistributedDocumentAsync(vessel.Id, documentType.Id);
        using var readOnly = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);
        using var manager = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Documents.Read,
            Permissions.Documents.Manage);

        var readOnlyResult = await readOnly.GetFromJsonAsync<GetDocumentReadinessResponse>(
            $"{ReadinessPath}?vesselId={vessel.Id}", CancellationToken.None);
        var managerResult = await manager.GetFromJsonAsync<GetDocumentReadinessResponse>(
            $"{ReadinessPath}?vesselId={vessel.Id}", CancellationToken.None);

        var readOnlyDistribution = Assert.Single(readOnlyResult!.Items).Distribution!;
        var managerDistribution = Assert.Single(managerResult!.Items).Distribution!;
        Assert.Equal(1, readOnlyDistribution.PendingCount);
        Assert.Null(readOnlyDistribution.Recipients);
        Assert.Single(managerDistribution.Recipients!);
    }

    [Fact]
    public async Task A_vessel_without_requirements_is_not_configured()
    {
        var vessel = await SeedVesselAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync($"{ReadinessPath}?vesselId={vessel.Id}", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentReadinessResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DocumentReadinessRules.NotConfiguredVesselStatus, result!.VesselStatus);
    }

    [Fact]
    public async Task An_unknown_vessel_returns_404()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync($"{ReadinessPath}?vesselId={Guid.NewGuid()}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_filters_return_400()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync($"{ReadinessPath}?status=Ready&pageSize=500", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(GetDocumentReadinessQuery.StatusField, problem!.Errors);
        Assert.Contains(GetDocumentReadinessQuery.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task The_vessel_overview_lists_fleet_vessels_including_vessels_without_requirements()
    {
        var configured = await SeedVesselAsync("Adriatic Star", "1000002");
        var unconfigured = await SeedVesselAsync("Boka Pearl", "1000003");
        var documentType = await SeedDocumentTypeAsync();
        await SeedRequirementAsync(configured.Id, documentType.Id);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync($"{ReadinessPath}/vessels", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetVesselReadinessOverviewResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, result!.TotalCount);
        Assert.Equal(
            [
                new VesselReadinessOverviewItem(configured.Id, "Adriatic Star", DocumentReadinessRules.NotReadyVesselStatus, new DocumentReadinessSummary(1, 0, 1)),
                new VesselReadinessOverviewItem(unconfigured.Id, "Boka Pearl", DocumentReadinessRules.NotConfiguredVesselStatus, new DocumentReadinessSummary(0, 0, 0)),
            ],
            result.Items);
    }

    [Fact]
    public async Task The_vessel_overview_requires_the_documents_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.GetAsync($"{ReadinessPath}/vessels", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_vessel_overview_rejects_invalid_filters_with_400()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);
        var tooFar = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(400);

        using var response = await client.GetAsync(
            $"{ReadinessPath}/vessels?requiredThrough={tooFar:yyyy-MM-dd}&pageSize=500",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(GetDocumentReadinessQuery.RequiredThroughField, problem!.Errors);
        Assert.Contains(GetDocumentReadinessQuery.PageSizeField, problem.Errors);
    }

    private async Task<Vessel> SeedVesselAsync(string name = "North Star", string imoNumber = "1000001")
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");
        var vessel = Vessel.Register(Guid.NewGuid(), vesselType.Id, name, imoNumber, "Malta", "Active");

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

    private async Task SeedRequirementAsync(Guid vesselId, int documentTypeId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var requirement = VesselDocumentRequirement.Create(vesselId, documentTypeId, 0, null, Guid.NewGuid(), DateTime.UtcNow);

        await dbContext.Set<VesselDocumentRequirement>().AddAsync(requirement);
        await dbContext.Set<VesselDocumentRequirementHistory>()
            .AddAsync(VesselDocumentRequirementHistory.RecordCreated(requirement, "Company requirement."));
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedDistributedDocumentAsync(Guid vesselId, int documentTypeId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var document = new Document(
            vesselId,
            documentTypeId,
            "SMC-002",
            today.AddYears(-1),
            today.AddYears(2),
            "Flag State Authority",
            Document.ActiveStatus);
        document.AttachFile("certificate.pdf", "application/pdf", $"key-{Guid.NewGuid():N}", 1024, Guid.NewGuid(), DateTime.UtcNow);
        document.SubmitForReview(Guid.NewGuid(), DateTimeOffset.UtcNow);
        document.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var issue = new DocumentDistributionIssue(document, "Safety Management Certificate", today.AddDays(7), null, Guid.NewGuid(), DateTime.UtcNow);

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.Set<DocumentDistributionIssue>().AddAsync(issue);
        await dbContext.Set<DocumentDistributionRecipient>().AddAsync(new DocumentDistributionRecipient(issue.Id, Guid.NewGuid()));
        await dbContext.SaveChangesAsync();
    }

    private async Task<Document> SeedDraftDocumentAsync(Guid vesselId, int documentTypeId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var document = new Document(
            vesselId,
            documentTypeId,
            "SMC-001",
            today.AddYears(-1),
            today.AddYears(2),
            "Flag State Authority",
            Document.ActiveStatus);

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document;
    }
}
