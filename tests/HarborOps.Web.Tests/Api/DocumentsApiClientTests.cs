using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarborOps.Web.Api.Documents;

namespace HarborOps.Web.Tests.Api;

public sealed class DocumentsApiClientTests
{
    private const string ExpiringPageJson = """
        {"items":[{"documentId":"10000000-0000-0000-0000-000000000001",
        "vesselId":"20000000-0000-0000-0000-000000000002","number":"CERT-001",
        "issueDate":"2026-01-01","expiryDate":"2026-12-31","issuingAuthority":"Flag State",
        "status":"Active","documentTypeName":"Safety Certificate","daysUntilExpiry":120}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private const string DocumentTypesPageJson = """
        {"items":[{"documentTypeId":1,"name":"Safety Certificate",
        "mandatoryValidityPeriodDays":365,"issuingAuthority":"Flag State","requiresSurvey":true}],
        "pageSize":100,"totalCount":1}
        """;

    // ---- GetExpiringDocumentsAsync ----

    [Fact]
    public async Task GetExpiringDocumentsAsync_sends_all_filters_and_paging_to_the_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, ExpiringPageJson);
        });

        var vesselId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var query = new GetExpiringDocumentsQuery(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), vesselId, 2, 50);

        var result = await CreateClient(httpClient).GetExpiringDocumentsAsync(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/documents/expiring", captured.RequestUri!.AbsolutePath);
        Assert.Contains("from=2026-01-01", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-12-31", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains($"vesselId={vesselId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetExpiringDocumentsAsync_omits_unset_dates_and_other_optional_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, ExpiringPageJson);
        });

        var query = new GetExpiringDocumentsQuery(null, null, null, null, null);

        await CreateClient(httpClient).GetExpiringDocumentsAsync(query, CancellationToken.None);

        Assert.DoesNotContain("from=", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("to=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("vesselId=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("pageNumber=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("pageSize=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetExpiringDocumentsAsync_sends_only_from_when_to_is_unset()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, ExpiringPageJson);
        });

        await CreateClient(httpClient).GetExpiringDocumentsAsync(
            new GetExpiringDocumentsQuery(new DateOnly(2026, 1, 1), null, null, 1, 25),
            CancellationToken.None);

        Assert.Contains("from=2026-01-01", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("to=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetExpiringDocumentsAsync_sends_only_to_when_from_is_unset()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, ExpiringPageJson);
        });

        await CreateClient(httpClient).GetExpiringDocumentsAsync(
            new GetExpiringDocumentsQuery(null, new DateOnly(2026, 12, 31), null, 1, 25),
            CancellationToken.None);

        Assert.DoesNotContain("from=", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-12-31", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetExpiringDocumentsAsync_reads_a_page_without_failing()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, ExpiringPageJson));

        var result = await CreateClient(httpClient).GetExpiringDocumentsAsync(
            new GetExpiringDocumentsQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, 1, 25),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(Guid.Parse("10000000-0000-0000-0000-000000000001"), item.DocumentId);
        Assert.Equal(Guid.Parse("20000000-0000-0000-0000-000000000002"), item.VesselId);
        Assert.Equal("CERT-001", item.Number);
        Assert.Equal("Safety Certificate", item.DocumentTypeName);
        Assert.Equal(120, item.DaysUntilExpiry);
    }

    [Fact]
    public async Task GetExpiringDocumentsAsync_surfaces_validation_errors_from_problem_details()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"to":["\"To\" must not be earlier than \"From\"."]}}"""));

        var result = await CreateClient(httpClient).GetExpiringDocumentsAsync(
            new GetExpiringDocumentsQuery(new DateOnly(2026, 1, 1), new DateOnly(2025, 1, 1), null, 1, 25),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal(
            "\"To\" must not be earlier than \"From\".",
            Assert.Single(result.Failure.Errors["to"]));
    }

    [Fact]
    public async Task GetExpiringDocumentsAsync_surfaces_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Conflict.","errors":{}}"""));

        var result = await CreateClient(httpClient).GetExpiringDocumentsAsync(
            new GetExpiringDocumentsQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, 1, 25),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task GetExpiringDocumentsAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).GetExpiringDocumentsAsync(
            new GetExpiringDocumentsQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, 1, 25),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetExpiringDocumentsAsync_returns_a_retryable_failure_on_timeout()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new TaskCanceledException("Timed out."));

        var result = await CreateClient(httpClient).GetExpiringDocumentsAsync(
            new GetExpiringDocumentsQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, 1, 25),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetExpiringDocumentsAsync_propagates_explicit_cancellation()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, ExpiringPageJson));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(httpClient).GetExpiringDocumentsAsync(
                new GetExpiringDocumentsQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, 1, 25),
                cts.Token));
    }

    // ---- RegisterDocumentAsync ----

    [Fact]
    public async Task RegisterDocumentAsync_returns_the_created_id()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Created,
            """{"id":"30000000-0000-0000-0000-000000000003"}"""));

        var result = await CreateClient(httpClient).RegisterDocumentAsync(
            new RegisterDocumentRequest
            {
                VesselId = Guid.NewGuid(),
                DocumentTypeId = 1,
                Number = "CERT-001",
                IssueDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2026, 12, 31),
                IssuingAuthority = "Flag State",
                Status = "Active",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Guid.Parse("30000000-0000-0000-0000-000000000003"), result.Value!.Id);
    }

    [Fact]
    public async Task RegisterDocumentAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"number":["Document number is required."]}}"""));

        var result = await CreateClient(httpClient).RegisterDocumentAsync(
            new RegisterDocumentRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("number", result.Failure.Errors.Keys);
    }

    [Fact]
    public async Task RegisterDocumentAsync_surfaces_conflict_for_a_duplicate_number()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"A document with this number already exists for this vessel.","errors":{"number":["A document with number 'CERT-001' already exists for vessel 'x'."]}}"""));

        var result = await CreateClient(httpClient).RegisterDocumentAsync(
            new RegisterDocumentRequest
            {
                VesselId = Guid.NewGuid(),
                DocumentTypeId = 1,
                Number = "CERT-001",
                IssueDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2026, 12, 31),
                IssuingAuthority = "Flag State",
                Status = "Active",
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Contains("number", result.Failure.Errors.Keys);
    }

    [Fact]
    public async Task RegisterDocumentAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).RegisterDocumentAsync(
            new RegisterDocumentRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task RegisterDocumentAsync_returns_a_retryable_failure_on_timeout()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new TaskCanceledException("Timed out."));

        var result = await CreateClient(httpClient).RegisterDocumentAsync(
            new RegisterDocumentRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task RegisterDocumentAsync_propagates_explicit_cancellation()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Created, """{"id":"30000000-0000-0000-0000-000000000003"}"""));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(httpClient).RegisterDocumentAsync(new RegisterDocumentRequest(), cts.Token));
    }

    // ---- GetDocumentTypesAsync ----

    [Fact]
    public async Task GetDocumentTypesAsync_sends_search_and_page_size_to_the_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, DocumentTypesPageJson);
        });

        await CreateClient(httpClient).GetDocumentTypesAsync(
            new GetDocumentTypesQuery("safety", 100), CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/documents/types", captured.RequestUri!.AbsolutePath);
        Assert.Contains("search=safety", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=100", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDocumentTypesAsync_omits_unset_optional_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, DocumentTypesPageJson);
        });

        await CreateClient(httpClient).GetDocumentTypesAsync(
            new GetDocumentTypesQuery(null, null), CancellationToken.None);

        Assert.Equal("/api/documents/types", captured!.RequestUri!.AbsolutePath);
        Assert.Equal(string.Empty, captured.RequestUri.Query);
    }

    [Fact]
    public async Task GetDocumentTypesAsync_reads_a_page_without_failing()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, DocumentTypesPageJson));

        var result = await CreateClient(httpClient).GetDocumentTypesAsync(
            new GetDocumentTypesQuery(null, 100), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(1, item.DocumentTypeId);
        Assert.Equal("Safety Certificate", item.Name);
        Assert.Equal(365, item.MandatoryValidityPeriodDays);
        Assert.True(item.RequiresSurvey);
    }

    [Fact]
    public async Task GetDocumentTypesAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"pageSize":["Page size must not exceed 100."]}}"""));

        var result = await CreateClient(httpClient).GetDocumentTypesAsync(
            new GetDocumentTypesQuery(null, 500), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal(
            "Page size must not exceed 100.",
            Assert.Single(result.Failure.Errors["pageSize"]));
    }

    [Fact]
    public async Task GetDocumentTypesAsync_surfaces_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Conflict.","errors":{}}"""));

        var result = await CreateClient(httpClient).GetDocumentTypesAsync(
            new GetDocumentTypesQuery(null, 100), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task GetDocumentTypesAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).GetDocumentTypesAsync(
            new GetDocumentTypesQuery(null, 100), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetDocumentTypesAsync_returns_a_retryable_failure_on_timeout()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new TaskCanceledException("Timed out."));

        var result = await CreateClient(httpClient).GetDocumentTypesAsync(
            new GetDocumentTypesQuery(null, 100), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetDocumentTypesAsync_propagates_explicit_cancellation()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, DocumentTypesPageJson));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(httpClient).GetDocumentTypesAsync(new GetDocumentTypesQuery(null, 100), cts.Token));
    }

    // ---- RegisterDocumentTypeAsync ----

    [Fact]
    public async Task RegisterDocumentTypeAsync_returns_the_created_id()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Created,
            """{"id":42}"""));

        var result = await CreateClient(httpClient).RegisterDocumentTypeAsync(
            new RegisterDocumentTypeRequest
            {
                Name = "Safety Certificate",
                MandatoryValidityPeriodDays = 365,
                IssuingAuthority = "Flag State",
                RequiresSurvey = true,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value!.Id);
    }

    [Fact]
    public async Task RegisterDocumentTypeAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"name":["Name is required."]}}"""));

        var result = await CreateClient(httpClient).RegisterDocumentTypeAsync(
            new RegisterDocumentTypeRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal(
            "Name is required.",
            Assert.Single(result.Failure.Errors["name"]));
    }

    [Fact]
    public async Task RegisterDocumentTypeAsync_surfaces_conflict_for_a_duplicate_name()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"A document type with this name already exists.","errors":{"name":["A document type with name 'Safety Certificate' already exists."]}}"""));

        var result = await CreateClient(httpClient).RegisterDocumentTypeAsync(
            new RegisterDocumentTypeRequest
            {
                Name = "Safety Certificate",
                MandatoryValidityPeriodDays = 365,
                IssuingAuthority = "Flag State",
                RequiresSurvey = true,
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Contains("name", result.Failure.Errors.Keys);
    }

    [Fact]
    public async Task RegisterDocumentTypeAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).RegisterDocumentTypeAsync(
            new RegisterDocumentTypeRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task RegisterDocumentTypeAsync_returns_a_retryable_failure_on_timeout()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new TaskCanceledException("Timed out."));

        var result = await CreateClient(httpClient).RegisterDocumentTypeAsync(
            new RegisterDocumentTypeRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task RegisterDocumentTypeAsync_propagates_explicit_cancellation()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Created, """{"id":42}"""));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(httpClient).RegisterDocumentTypeAsync(new RegisterDocumentTypeRequest(), cts.Token));
    }

    private static DocumentsApiClient CreateClient(HttpClient httpClient) => new(httpClient);

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
