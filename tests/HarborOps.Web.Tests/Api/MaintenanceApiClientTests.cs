using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Tests.Api;

public sealed class MaintenanceApiClientTests
{
    private const string PageJson = """
        {"items":[{"workOrderId":"20000000-0000-0000-0000-000000000001",
        "vesselId":"30000000-0000-0000-0000-000000000002","title":"Replace filter",
        "description":"Routine replacement","priority":"High","status":"Open",
        "dueUtc":"2026-09-15T08:00:00Z","assignedEngineerId":null,
        "createdUtc":"2026-08-01T08:00:00Z","completedUtc":null}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    [Fact]
    public async Task GetWorkOrdersAsync_sends_all_filters_and_paging_to_the_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PageJson);
        });

        var vesselId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var filter = new MaintenanceWorkOrderFilter(vesselId, "Open", "High", true, 2, 50);

        var result = await CreateClient(httpClient).GetWorkOrdersAsync(filter, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/maintenance/work-orders", captured.RequestUri!.AbsolutePath);
        Assert.Contains($"vesselId={vesselId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("status=Open", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("priority=High", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("overdue=true", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_omits_unset_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PageJson);
        });

        await CreateClient(httpClient).GetWorkOrdersAsync(
            new MaintenanceWorkOrderFilter(null, null, null, null, 1, 25),
            CancellationToken.None);

        Assert.DoesNotContain("vesselId=", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("status=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("priority=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("overdue=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_reads_an_empty_page_without_failing()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.OK,
            """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0}"""));

        var result = await CreateClient(httpClient).GetWorkOrdersAsync(
            new MaintenanceWorkOrderFilter(null, null, null, null, 1, 25),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_surfaces_validation_errors_from_problem_details()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"pageSize":["Page size must not exceed 100."]}}"""));

        var result = await CreateClient(httpClient).GetWorkOrdersAsync(
            new MaintenanceWorkOrderFilter(null, null, null, null, 1, 101),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal(
            "Page size must not exceed 100.",
            Assert.Single(result.Failure.Errors["pageSize"]));
    }

    [Fact]
    public async Task GetWorkOrdersAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).GetWorkOrdersAsync(
            new MaintenanceWorkOrderFilter(null, null, null, null, 1, 25),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_can_be_called_again_after_a_failure()
    {
        var attempts = 0;
        using var httpClient = CreateHttpClient((_, _) =>
        {
            attempts++;
            return attempts == 1
                ? throw new HttpRequestException("Connection failed.")
                : Json(HttpStatusCode.OK, PageJson);
        });
        var client = CreateClient(httpClient);
        var filter = new MaintenanceWorkOrderFilter(null, null, null, null, 1, 25);

        var firstResult = await client.GetWorkOrdersAsync(filter, CancellationToken.None);
        var retryResult = await client.GetWorkOrdersAsync(filter, CancellationToken.None);

        Assert.False(firstResult.IsSuccess);
        Assert.True(retryResult.IsSuccess);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task CreateWorkOrderAsync_returns_the_created_id()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Created,
            """{"id":"40000000-0000-0000-0000-000000000004"}"""));

        var result = await CreateClient(httpClient).CreateWorkOrderAsync(
            new CreateMaintenanceWorkOrderRequest
            {
                VesselId = Guid.NewGuid(),
                Title = "Replace filter",
                Priority = "High",
                DueUtc = DateTime.UtcNow.AddDays(7),
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Guid.Parse("40000000-0000-0000-0000-000000000004"), result.Value!.Id);
    }

    [Fact]
    public async Task CreateWorkOrderAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"title":["Title is required."]}}"""));

        var result = await CreateClient(httpClient).CreateWorkOrderAsync(
            new CreateMaintenanceWorkOrderRequest(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("title", result.Failure.Errors.Keys);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_returns_the_completion_result()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.OK,
            """{"workOrderId":"20000000-0000-0000-0000-000000000001","maintenanceRecordId":"50000000-0000-0000-0000-000000000005","completedUtc":"2026-08-12T09:00:00Z"}"""));

        var result = await CreateClient(httpClient).CompleteWorkOrderAsync(
            Guid.NewGuid(),
            new CompleteMaintenanceWorkOrderRequest
            {
                TechnicianId = Guid.NewGuid(),
                CompletedUtc = DateTime.UtcNow,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Guid.Parse("50000000-0000-0000-0000-000000000005"),
            result.Value!.MaintenanceRecordId);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_surfaces_conflict_for_a_non_completable_work_order()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Maintenance work order cannot be completed from its current status.","detail":"Work order cannot be completed from status 'Completed'."}"""));

        var result = await CreateClient(httpClient).CompleteWorkOrderAsync(
            Guid.NewGuid(),
            new CompleteMaintenanceWorkOrderRequest { TechnicianId = Guid.NewGuid(), CompletedUtc = DateTime.UtcNow },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_surfaces_not_found()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Maintenance work order not found."}"""));

        var result = await CreateClient(httpClient).CompleteWorkOrderAsync(
            Guid.NewGuid(),
            new CompleteMaintenanceWorkOrderRequest { TechnicianId = Guid.NewGuid(), CompletedUtc = DateTime.UtcNow },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    private static MaintenanceApiClient CreateClient(HttpClient httpClient) => new(httpClient);

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
