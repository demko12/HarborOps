using System.Net;
using System.Text;
using HarborOps.Web.Api;

namespace HarborOps.Web.Tests.Api;

public sealed class CrewApiClientTests
{
    [Fact]
    public async Task GetMembersAsync_sends_search_status_and_paging_to_crew_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":2,"pageSize":10,"totalCount":0}""");
        });

        var result = await new CrewApiClient(httpClient).GetMembersAsync("Ana Novak", "Active", 2, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/crew/members", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("search=Ana%20Novak", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("employmentStatus=Active", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMembersAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new CrewApiClient(httpClient).GetMembersAsync(null, null, 1, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetRotationsAsync_sends_filters_and_paging_to_crew_rotations_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":2,"pageSize":10,"totalCount":0}""");
        });
        var vesselId = Guid.NewGuid();

        var result = await new CrewApiClient(httpClient).GetRotationsAsync(
            vesselId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), true, 2, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/crew/rotations", captured!.RequestUri!.AbsolutePath);
        Assert.Contains($"vesselId={vesselId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("from=2026-07-01", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-07-31", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("overdueOnly=true", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRotationAsync_sends_request_to_the_assignment_rotation_endpoint()
    {
        var assignmentId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, $$"""
                {"rotationId":"{{Guid.NewGuid()}}","assignmentId":"{{assignmentId}}","vesselId":"{{Guid.NewGuid()}}",
                "crewMemberId":"{{Guid.NewGuid()}}","crewMemberName":"Marko","rankId":"{{Guid.NewGuid()}}","rankName":"AB",
                "relievingCrewMemberId":null,"relievingCrewMemberName":null,"rotationPattern":"2-2",
                "plannedReliefDate":"2026-07-01","isOverdue":false}
                """);
        });

        var result = await new CrewApiClient(httpClient).GetRotationAsync(assignmentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/api/crew/assignments/{assignmentId}/rotation", captured!.RequestUri!.AbsolutePath);
        Assert.Equal(assignmentId, result.Value!.AssignmentId);
    }

    [Fact]
    public async Task GetRotationAsync_reports_not_found_when_no_rotation_is_planned()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await new CrewApiClient(httpClient).GetRotationAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task PlanRotationAsync_posts_to_the_assignment_rotation_endpoint()
    {
        var assignmentId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, $$"""{"id":"{{Guid.NewGuid()}}"}""");
        });

        var result = await new CrewApiClient(httpClient).PlanRotationAsync(
            assignmentId,
            new PlanCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/crew/assignments/{assignmentId}/rotation", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdateRotationAsync_puts_to_the_assignment_rotation_endpoint()
    {
        var assignmentId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, $$"""{"id":"{{Guid.NewGuid()}}"}""");
        });

        var result = await new CrewApiClient(httpClient).UpdateRotationAsync(
            assignmentId,
            new UpdateCrewRotationRequest { RotationPattern = "4-4", PlannedReliefDate = new DateOnly(2026, 8, 1) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Equal($"/api/crew/assignments/{assignmentId}/rotation", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ExecuteRotationAsync_posts_to_the_execute_endpoint_without_a_body()
    {
        var assignmentId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, $$"""
                {"assignmentId":"{{assignmentId}}","actualSignOffDate":"2026-07-01",
                "replacementAssignmentId":"{{Guid.NewGuid()}}","replacementSignOnDate":"2026-07-01",
                "replacementPlannedSignOffDate":"2026-12-31"}
                """);
        });

        var result = await new CrewApiClient(httpClient).ExecuteRotationAsync(assignmentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/crew/assignments/{assignmentId}/rotation/execute", captured.RequestUri!.AbsolutePath);
        Assert.Null(captured.Content);
    }

    [Fact]
    public async Task ExecuteRotationAsync_returns_conflict_failure_when_not_executable()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)));

        var result = await new CrewApiClient(httpClient).ExecuteRotationAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task GetMedicalExaminationsAsync_sends_filters_and_paging_to_the_medical_examinations_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":2,"pageSize":10,"totalCount":0}""");
        });

        var result = await new CrewApiClient(httpClient).GetMedicalExaminationsAsync(
            "Ana", "Fit", true, 15, 2, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/crew/medical-examinations", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("search=Ana", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("fitnessResult=Fit", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("expired=true", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("expiringWithinDays=15", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMedicalExaminationsAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new CrewApiClient(httpClient).GetMedicalExaminationsAsync(
            null, null, null, null, 1, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
    }

    [Fact]
    public async Task GetMemberMedicalExaminationsAsync_sends_request_to_the_member_endpoint()
    {
        var crewMemberId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"currentFitnessResult":"Fit"}""");
        });

        var result = await new CrewApiClient(httpClient).GetMemberMedicalExaminationsAsync(crewMemberId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/api/crew/members/{crewMemberId}/medical-examinations", captured!.RequestUri!.AbsolutePath);
        Assert.Equal("Fit", result.Value!.CurrentFitnessResult);
    }

    [Fact]
    public async Task GetMemberMedicalExaminationsAsync_reports_not_found_for_an_unknown_member()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await new CrewApiClient(httpClient).GetMemberMedicalExaminationsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task RegisterMedicalExaminationAsync_posts_to_the_member_endpoint()
    {
        var crewMemberId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, $$"""{"id":"{{Guid.NewGuid()}}"}""");
        });

        var result = await new CrewApiClient(httpClient).RegisterMedicalExaminationAsync(
            crewMemberId,
            new RegisterMedicalExaminationRequest
            {
                ExamType = "Periodic",
                Clinic = "Clinic A",
                ExamDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2027, 1, 1),
                FitnessResult = "Fit",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/crew/members/{crewMemberId}/medical-examinations", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RegisterMedicalExaminationAsync_returns_validation_failure_with_field_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"fitnessResult":["Fitness result is not supported."]}}"""));

        var result = await new CrewApiClient(httpClient).RegisterMedicalExaminationAsync(
            Guid.NewGuid(),
            new RegisterMedicalExaminationRequest(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("fitnessResult", result.Failure.Errors!.Keys);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
