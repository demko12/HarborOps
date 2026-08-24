using System.Net;
using System.Net.Http.Json;
using HarborOps.Api.Composition.Reports.DocumentsExpiry;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Documents.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Reports;

public sealed class DocumentsExpiryReportEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public DocumentsExpiryReportEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_composed_page_with_as_of_and_days_until_expiry()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        var seeded = await SeedDocumentAsync(vesselId, asOf.AddDays(5));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/documents-expiry?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<DocumentsExpiryReportPage>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(asOf, page.AsOf);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);
        Assert.Equal(seeded.Id, item.DocumentId);
        Assert.Equal("Safety Management Certificate", item.DocumentTypeName);
        Assert.Equal(5, item.DaysUntilExpiry);
        Assert.False(item.IsExpired);
    }

    [Fact]
    public async Task Get_returns_empty_page_when_nothing_matches()
    {
        var asOf = new DateOnly(2026, 8, 15);
        await SeedDocumentAsync(Guid.NewGuid(), asOf.AddYears(5));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/documents-expiry?asOf={asOf:yyyy-MM-dd}&vesselId={Guid.NewGuid()}",
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<DocumentsExpiryReportPage>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Get_returns_validation_problem_when_as_of_is_missing()
    {
        using var client = await CreateReportClientAsync();

        using var response = await client.GetAsync("/api/reports/documents-expiry", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(DocumentsExpiryReportValidator.AsOfField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_page_size_above_maximum()
    {
        var asOf = new DateOnly(2026, 8, 15);
        using var client = await CreateReportClientAsync();

        using var response = await client.GetAsync(
            $"/api/reports/documents-expiry?asOf={asOf:yyyy-MM-dd}&pageSize={DocumentsExpiryReportValidator.MaxPageSize + 1}",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(DocumentsExpiryReportValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_unauthorized_for_an_anonymous_request()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            "/api/reports/documents-expiry?asOf=2026-08-15",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_reports_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Documents.Read);

        using var response = await client.GetAsync(
            "/api/reports/documents-expiry?asOf=2026-08-15",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_documents_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Reports.Read);

        using var response = await client.GetAsync(
            "/api/reports/documents-expiry?asOf=2026-08-15",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_returns_csv_with_headers_matching_json_rows()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        var seeded = await SeedDocumentAsync(vesselId, asOf.AddDays(5));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/documents-expiry/export?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(
            $"documents-expiry-{asOf:yyyy-MM-dd}.csv",
            response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var lines = csv.Replace("﻿", string.Empty).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            "DocumentId,VesselId,DocumentTypeId,DocumentTypeName,Number,IssueDate,ExpiryDate,IssuingAuthority,Status,DaysUntilExpiry,IsExpired",
            lines[0]);
        Assert.Contains(seeded.Id.ToString(), lines[1]);
        Assert.Contains("Safety Management Certificate", lines[1]);
    }

    [Fact]
    public async Task Export_escapes_values_containing_commas_and_quotes()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        await SeedDocumentAsync(vesselId, asOf.AddDays(5), issuingAuthority: "Flag State, \"Registry\" Office");

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/documents-expiry/export?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Contains("\"Flag State, \"\"Registry\"\" Office\"", csv);
    }

    [Fact]
    public async Task Export_guards_against_formula_injection_in_leading_characters()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        await SeedDocumentAsync(vesselId, asOf.AddDays(5), number: "=SUM(A1:A9)");

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/documents-expiry/export?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Contains("'=SUM(A1:A9)", csv);
        Assert.DoesNotContain(",=SUM(A1:A9)", csv);
    }

    [Fact]
    public async Task Export_does_not_apply_the_formula_injection_guard_to_a_negative_days_until_expiry()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        await SeedDocumentAsync(vesselId, asOf.AddDays(-5));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/documents-expiry/export?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Contains(",-5,True", csv);
        Assert.DoesNotContain(",'-5,", csv);
    }

    [Fact]
    public async Task Export_returns_validation_problem_when_as_of_is_missing()
    {
        using var client = await CreateReportClientAsync();

        using var response = await client.GetAsync("/api/reports/documents-expiry/export", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(DocumentsExpiryReportValidator.AsOfField, problem.Errors);
    }

    private Task<HttpClient> CreateReportClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Reports.Read,
            Permissions.Documents.Read);
    }

    private async Task<Document> SeedDocumentAsync(
        Guid vesselId,
        DateOnly expiryDate,
        string number = "CERT-1001",
        string issuingAuthority = "Flag State Authority")
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var documentType = new DocumentType(
            "Safety Management Certificate",
            60,
            "Flag State Authority",
            requiresSurvey: true);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        var document = new Document(
            vesselId,
            documentType.Id,
            number,
            expiryDate.AddYears(-1),
            expiryDate,
            issuingAuthority,
            "Active");
        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document;
    }
}
