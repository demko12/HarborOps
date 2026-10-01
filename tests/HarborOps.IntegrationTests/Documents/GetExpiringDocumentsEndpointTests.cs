using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

public sealed class GetExpiringDocumentsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetExpiringDocumentsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_correctly_shaped_page_with_document_type_name()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var vesselId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow);
        var expiryDate = today.AddDays(5);
        var seeded = await SeedDocumentAsync(vesselId, expiryDate);
        var from = today;
        var to = today.AddDays(10);

        using var response = await client.GetAsync(
            new Uri(
                $"/api/documents/expiring?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
                UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetExpiringDocumentsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(GetExpiringDocumentsValidator.DefaultPageSize, page.PageSize);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);

        Assert.Equal(seeded.Id, item.DocumentId);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal(expiryDate, item.ExpiryDate);
        Assert.Equal("Safety Management Certificate", item.DocumentTypeName);
        Assert.Equal(5, item.DaysUntilExpiry);
    }

    [Fact]
    public async Task Summary_counts_the_filtered_documents_and_returns_twelve_months()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var vesselId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow);
        await SeedDocumentAsync(vesselId, today.AddDays(-3));
        await SeedDocumentAsync(vesselId, today.AddDays(5));

        using var response = await client.GetAsync(
            new Uri($"/api/documents/expiring/summary?vesselId={vesselId}", UriKind.Relative),
            CancellationToken.None);
        var summary = await response.Content.ReadFromJsonAsync<GetExpiringDocumentsSummaryResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(summary);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.ExpiredCount);
        Assert.Equal(1, summary.DueWithin30DaysCount);
        Assert.Equal(12, summary.Months.Count);
        Assert.Equal(1, summary.Months.Sum(month => month.Count));
    }

    [Fact]
    public async Task Get_returns_empty_items_when_nothing_in_range()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var today = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow);
        await SeedDocumentAsync(Guid.NewGuid(), today.AddYears(5));

        using var response = await client.GetAsync(
            new Uri(
                $"/api/documents/expiring?from={today:yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}",
                UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetExpiringDocumentsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 367)]
    public async Task Get_returns_validation_problem_for_invalid_date_range(int fromOffsetDays, int toOffsetDays)
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var today = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow);
        var from = today.AddDays(fromOffsetDays);
        var to = today.AddDays(toOffsetDays);

        using var response = await client.GetAsync(
            new Uri(
                $"/api/documents/expiring?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
                UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetExpiringDocumentsValidator.ToField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_page_size_above_maximum()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var today = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow);

        using var response = await client.GetAsync(
            new Uri(
                $"/api/documents/expiring?from={today:yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}&pageSize=1000",
                UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetExpiringDocumentsValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_empty_vessel_id()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var today = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow);

        using var response = await client.GetAsync(
            new Uri(
                $"/api/documents/expiring?from={today:yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}" +
                "&vesselId=00000000-0000-0000-0000-000000000000",
                UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetExpiringDocumentsValidator.VesselIdField, problem.Errors);
    }

    private async Task<Document> SeedDocumentAsync(Guid vesselId, DateOnly expiryDate)
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
            "CERT-1001",
            expiryDate.AddYears(-1),
            expiryDate,
            "Flag State Authority",
            "Active");
        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document;
    }
}
