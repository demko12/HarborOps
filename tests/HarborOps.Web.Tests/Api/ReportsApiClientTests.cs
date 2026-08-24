using System.Net;
using System.Text;
using HarborOps.Web.Api.Reports;

namespace HarborOps.Web.Tests.Api;

public sealed class ReportsApiClientTests
{
    private const string DocumentsExpiryPageJson = """
        {"asOf":"2026-08-15","items":[{"documentId":"10000000-0000-0000-0000-000000000001",
        "vesselId":"20000000-0000-0000-0000-000000000002","documentTypeId":1,"documentTypeName":"Safety Certificate",
        "number":"CERT-001","issueDate":"2026-01-01","expiryDate":"2026-12-31","issuingAuthority":"Flag State",
        "status":"Active","daysUntilExpiry":120,"isExpired":false}],"pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private const string ComplianceExposurePageJson = """
        {"asOf":"2026-08-15","items":[{"itemType":"Certificate","vesselId":"20000000-0000-0000-0000-000000000002",
        "reference":"CERT-001","description":"Safety Certificate","relevantDate":"2026-09-01",
        "status":"Expiring","severity":null}],"pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private const string MaintenanceBacklogPageJson = """
        {"asOf":"2026-08-15","items":[{"workOrderId":"10000000-0000-0000-0000-000000000001",
        "vesselId":"20000000-0000-0000-0000-000000000002","title":"Replace impeller","priority":"Critical",
        "status":"Open","dueUtc":"2026-08-10T00:00:00Z","isOverdue":true,"assignedEngineerId":null,
        "createdUtc":"2026-07-01T00:00:00Z","completedUtc":null}],"pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    // ---- GetDocumentsExpiryAsync ----

    [Fact]
    public async Task GetDocumentsExpiryAsync_sends_all_filters_and_paging_to_the_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, DocumentsExpiryPageJson);
        });

        var vesselId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var query = new DocumentsExpiryReportQuery(
            new DateOnly(2026, 8, 15), vesselId, 7, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            "Active", 2, 50);

        var result = await CreateClient(httpClient).GetDocumentsExpiryAsync(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/reports/documents-expiry", captured.RequestUri!.AbsolutePath);
        Assert.Contains("asOf=2026-08-15", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains($"vesselId={vesselId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("documentTypeId=7", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("from=2026-01-01", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-12-31", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("status=Active", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDocumentsExpiryAsync_omits_unset_optional_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, DocumentsExpiryPageJson);
        });

        var query = new DocumentsExpiryReportQuery(new DateOnly(2026, 8, 15), null, null, null, null, null, 1, 25);

        await CreateClient(httpClient).GetDocumentsExpiryAsync(query, CancellationToken.None);

        Assert.DoesNotContain("vesselId=", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("documentTypeId=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("from=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("to=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("status=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDocumentsExpiryAsync_reads_a_page_without_failing()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, DocumentsExpiryPageJson));

        var result = await CreateClient(httpClient).GetDocumentsExpiryAsync(
            new DocumentsExpiryReportQuery(new DateOnly(2026, 8, 15), null, null, null, null, null, 1, 25),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 8, 15), result.Value!.AsOf);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal("CERT-001", item.Number);
        Assert.Equal(120, item.DaysUntilExpiry);
    }

    [Fact]
    public async Task GetDocumentsExpiryAsync_surfaces_validation_errors_from_problem_details()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"asOf":["The asOf date is required."]}}"""));

        var result = await CreateClient(httpClient).GetDocumentsExpiryAsync(
            new DocumentsExpiryReportQuery(new DateOnly(2026, 8, 15), null, null, null, null, null, 1, 25),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal("The asOf date is required.", Assert.Single(result.Failure.Errors["asOf"]));
    }

    [Fact]
    public async Task GetDocumentsExpiryAsync_surfaces_forbidden_with_a_fallback_title_when_no_body_is_returned()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var result = await CreateClient(httpClient).GetDocumentsExpiryAsync(
            new DocumentsExpiryReportQuery(new DateOnly(2026, 8, 15), null, null, null, null, null, 1, 25),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsForbidden);
        Assert.Equal("Report access denied", result.Failure.Title);
    }

    [Fact]
    public async Task GetDocumentsExpiryAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).GetDocumentsExpiryAsync(
            new DocumentsExpiryReportQuery(new DateOnly(2026, 8, 15), null, null, null, null, null, 1, 25),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetDocumentsExpiryAsync_returns_a_retryable_failure_on_timeout()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new TaskCanceledException("Timed out."));

        var result = await CreateClient(httpClient).GetDocumentsExpiryAsync(
            new DocumentsExpiryReportQuery(new DateOnly(2026, 8, 15), null, null, null, null, null, 1, 25),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetDocumentsExpiryAsync_propagates_explicit_cancellation()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, DocumentsExpiryPageJson));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(httpClient).GetDocumentsExpiryAsync(
                new DocumentsExpiryReportQuery(new DateOnly(2026, 8, 15), null, null, null, null, null, 1, 25),
                cts.Token));
    }

    // ---- ExportDocumentsExpiryAsync ----

    [Fact]
    public async Task ExportDocumentsExpiryAsync_hits_the_export_path_without_paging_params()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("csv", Encoding.UTF8, "text/csv"),
            });
        });

        var query = new DocumentsExpiryReportQuery(
            new DateOnly(2026, 8, 15), null, null, null, null, "Active", 3, 999);

        using var response = await CreateClient(httpClient).ExportDocumentsExpiryAsync(query, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/reports/documents-expiry/export", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("asOf=2026-08-15", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("status=Active", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("pageNumber=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("pageSize=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    // ---- GetComplianceExposureAsync / ExportComplianceExposureAsync ----

    [Fact]
    public async Task GetComplianceExposureAsync_sends_vessel_and_category_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, ComplianceExposurePageJson);
        });

        var vesselId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var query = new ComplianceExposureReportQuery(new DateOnly(2026, 8, 15), vesselId, "Insurance", 1, 25);

        var result = await CreateClient(httpClient).GetComplianceExposureAsync(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/reports/compliance-exposure", captured!.RequestUri!.AbsolutePath);
        Assert.Contains($"vesselId={vesselId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("category=Insurance", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task ExportComplianceExposureAsync_hits_the_export_path()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("csv", Encoding.UTF8, "text/csv"),
            });
        });

        var query = new ComplianceExposureReportQuery(new DateOnly(2026, 8, 15), null, "Detention", 1, 25);

        using var response = await CreateClient(httpClient)
            .ExportComplianceExposureAsync(query, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/reports/compliance-exposure/export", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("category=Detention", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("pageNumber=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    // ---- GetMaintenanceBacklogAsync / ExportMaintenanceBacklogAsync ----

    [Fact]
    public async Task GetMaintenanceBacklogAsync_sends_priority_status_overdue_and_due_range()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, MaintenanceBacklogPageJson);
        });

        var query = new MaintenanceBacklogReportQuery(
            new DateOnly(2026, 8, 15), null, "Critical", "Open", true,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 1, 25);

        var result = await CreateClient(httpClient).GetMaintenanceBacklogAsync(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/reports/maintenance-backlog", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("priority=Critical", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("status=Open", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("overdue=true", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("dueFrom=2026-08-01", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("dueTo=2026-08-31", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.True(Assert.Single(result.Value!.Items).IsOverdue);
    }

    [Fact]
    public async Task GetMaintenanceBacklogAsync_omits_overdue_when_unset()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, MaintenanceBacklogPageJson);
        });

        var query = new MaintenanceBacklogReportQuery(
            new DateOnly(2026, 8, 15), null, null, null, null, null, null, 1, 25);

        await CreateClient(httpClient).GetMaintenanceBacklogAsync(query, CancellationToken.None);

        Assert.DoesNotContain("overdue=", captured!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportMaintenanceBacklogAsync_hits_the_export_path_without_paging_params()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("csv", Encoding.UTF8, "text/csv"),
            });
        });

        var query = new MaintenanceBacklogReportQuery(
            new DateOnly(2026, 8, 15), null, "Critical", null, false, null, null, 1, 25);

        using var response = await CreateClient(httpClient)
            .ExportMaintenanceBacklogAsync(query, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/reports/maintenance-backlog/export", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("priority=Critical", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("overdue=false", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("pageNumber=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("pageSize=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    private static ReportsApiClient CreateClient(HttpClient httpClient) => new(httpClient);

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
