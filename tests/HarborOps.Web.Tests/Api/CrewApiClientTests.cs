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

    [Fact]
    public async Task UpdateProfileAsync_puts_to_the_member_endpoint()
    {
        var crewMemberId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, $$"""{"id":"{{crewMemberId}}"}""");
        });

        var result = await new CrewApiClient(httpClient).UpdateProfileAsync(
            crewMemberId,
            new UpdateCrewMemberRequest
            {
                FirstName = "Marko",
                LastName = "Marković",
                DateOfBirth = new DateOnly(1990, 5, 14),
                Nationality = "Montenegro",
                PassportNumber = "P1234567",
                PassportExpiry = new DateOnly(2028, 1, 1),
                CdcNumber = "CDC987654",
                HireDate = new DateOnly(2024, 1, 15),
                BaseRankId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Equal($"/api/crew/members/{crewMemberId}", captured.RequestUri!.AbsolutePath);
        Assert.Equal(crewMemberId, result.Value!.Id);
    }

    [Fact]
    public async Task UpdateProfileAsync_returns_validation_failure_with_field_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"firstName":["First name is required."]}}"""));

        var result = await new CrewApiClient(httpClient).UpdateProfileAsync(
            Guid.NewGuid(), new UpdateCrewMemberRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("firstName", result.Failure.Errors!.Keys);
    }

    [Fact]
    public async Task ChangeEmploymentStatusAsync_posts_to_the_employment_status_changes_endpoint()
    {
        var crewMemberId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, $$"""
                {"id":"{{Guid.NewGuid()}}","crewMemberId":"{{crewMemberId}}",
                "previousStatus":"Active","newStatus":"OnLeave","changedByUserId":"{{Guid.NewGuid()}}",
                "notes":"Medical leave","changedAtUtc":"2026-08-01T10:00:00Z"}
                """);
        });

        var result = await new CrewApiClient(httpClient).ChangeEmploymentStatusAsync(
            crewMemberId,
            new ChangeCrewMemberEmploymentStatusRequest { Status = "OnLeave", Notes = "Medical leave" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/crew/members/{crewMemberId}/employment-status-changes", captured.RequestUri!.AbsolutePath);
        Assert.Equal("OnLeave", result.Value!.NewStatus);
    }

    [Fact]
    public async Task ChangeEmploymentStatusAsync_returns_conflict_failure_for_an_unchanged_status()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)));

        var result = await new CrewApiClient(httpClient).ChangeEmploymentStatusAsync(
            Guid.NewGuid(),
            new ChangeCrewMemberEmploymentStatusRequest { Status = "Active" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task GetEmploymentStatusHistoryAsync_sends_request_to_the_member_endpoint()
    {
        var crewMemberId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, $$"""
                {"items":[{"id":"{{Guid.NewGuid()}}","status":"OnLeave","changedByUserId":"{{Guid.NewGuid()}}",
                "notes":null,"changedAtUtc":"2026-08-01T10:00:00Z"}]}
                """);
        });

        var result = await new CrewApiClient(httpClient).GetEmploymentStatusHistoryAsync(crewMemberId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/api/crew/members/{crewMemberId}/employment-status-history", captured!.RequestUri!.AbsolutePath);
        Assert.Single(result.Value!.Items);
        Assert.Equal("OnLeave", result.Value.Items[0].Status);
    }

    [Fact]
    public async Task GetEmploymentStatusHistoryAsync_reports_not_found_for_an_unknown_member()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await new CrewApiClient(httpClient).GetEmploymentStatusHistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task GetPositionsAsync_sends_search_department_and_paging_to_the_positions_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":2,"pageSize":10,"totalCount":0}""");
        });

        var result = await new CrewApiClient(httpClient).GetPositionsAsync(
            "Bos", 10, CancellationToken.None, "Deck", 2);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/crew/positions", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("search=Bos", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("department=Deck", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=10", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPositionsAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new CrewApiClient(httpClient).GetPositionsAsync(null, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
    }

    [Fact]
    public async Task RegisterPositionAsync_posts_to_the_positions_endpoint()
    {
        var positionId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, $$"""{"id":"{{positionId}}"}""");
        });

        var result = await new CrewApiClient(httpClient).RegisterPositionAsync(
            new RegisterCrewPositionRequest { RankName = "Bosun", Department = "Deck", SeniorityLevel = "SeniorRating" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/crew/positions", captured.RequestUri!.AbsolutePath);
        Assert.Equal(positionId, result.Value!.Id);
    }

    [Fact]
    public async Task RegisterPositionAsync_returns_conflict_failure_for_a_duplicate_position()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)));

        var result = await new CrewApiClient(httpClient).RegisterPositionAsync(
            new RegisterCrewPositionRequest { RankName = "Bosun", Department = "Deck", SeniorityLevel = "SeniorRating" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task UpdatePositionAsync_puts_to_the_position_endpoint()
    {
        var positionId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, $$"""{"id":"{{positionId}}"}""");
        });

        var result = await new CrewApiClient(httpClient).UpdatePositionAsync(
            positionId,
            new UpdateCrewPositionRequest { RankName = "Chief Bosun", Department = "Engine", SeniorityLevel = "Officer" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Equal($"/api/crew/positions/{positionId}", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdatePositionAsync_returns_not_found_failure_for_an_unknown_position()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await new CrewApiClient(httpClient).UpdatePositionAsync(
            Guid.NewGuid(),
            new UpdateCrewPositionRequest { RankName = "Bosun", Department = "Deck", SeniorityLevel = "SeniorRating" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task GetAvailabilityAsync_sends_filters_and_paging_to_the_availability_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0}""");
        });
        var memberId = Guid.NewGuid();

        var result = await new CrewApiClient(httpClient).GetAvailabilityAsync(
            memberId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "AnnualLeave", "Requested", 1, 25, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/crew/availability", captured!.RequestUri!.AbsolutePath);
        Assert.Contains($"crewMemberId={memberId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("from=2026-08-01", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("type=AnnualLeave", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("status=Requested", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberAvailabilityAsync_and_conflicts_use_their_endpoints()
    {
        var paths = new List<string>();
        using var httpClient = CreateHttpClient((request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("/conflicts", StringComparison.Ordinal)
                ? """{"conflicts":[]}"""
                : """{"items":[]}""");
        });
        var memberId = Guid.NewGuid();
        var client = new CrewApiClient(httpClient);

        await client.GetMemberAvailabilityAsync(memberId, CancellationToken.None);
        await client.GetAvailabilityConflictsAsync(memberId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), CancellationToken.None);

        Assert.Contains($"/api/crew/members/{memberId}/availability", paths);
        Assert.Contains("/api/crew/availability/conflicts", paths);
    }

    [Fact]
    public async Task Availability_write_methods_use_the_actual_endpoints()
    {
        var calls = new List<(HttpMethod Method, string Path)>();
        using var httpClient = CreateHttpClient((request, _) =>
        {
            calls.Add((request.Method, request.RequestUri!.AbsolutePath));
            var status = request.RequestUri!.AbsolutePath.EndsWith("/availability-requests", StringComparison.Ordinal)
                ? HttpStatusCode.Created
                : HttpStatusCode.OK;
            return Json(status, $$"""{"id":"{{Guid.NewGuid()}}","crewMemberId":"{{Guid.NewGuid()}}","previousStatus":"Requested","newStatus":"Approved","actorUserId":"{{Guid.NewGuid()}}","reason":null,"occurredAtUtc":"2026-09-10T00:00:00Z"}""");
        });
        var client = new CrewApiClient(httpClient);
        var memberId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await client.RequestAvailabilityAsync(memberId, new CreateCrewAvailabilityRequestRequest { Type = "AnnualLeave" }, CancellationToken.None);
        await client.ApproveAvailabilityRequestAsync(requestId, CancellationToken.None);
        await client.RejectAvailabilityRequestAsync(requestId, new RejectCrewAvailabilityRequestRequest { Reason = "x" }, CancellationToken.None);
        await client.CancelAvailabilityRequestAsync(requestId, new CancelCrewAvailabilityRequestRequest { Reason = "x" }, CancellationToken.None);

        Assert.Contains((HttpMethod.Post, $"/api/crew/members/{memberId}/availability-requests"), calls);
        Assert.Contains((HttpMethod.Post, $"/api/crew/availability-requests/{requestId}/approve"), calls);
        Assert.Contains((HttpMethod.Post, $"/api/crew/availability-requests/{requestId}/reject"), calls);
        Assert.Contains((HttpMethod.Post, $"/api/crew/availability-requests/{requestId}/cancel"), calls);
    }

    [Fact]
    public async Task Availability_write_methods_map_conflict_validation_and_not_found()
    {
        using var conflict = CreateHttpClient((_, _) => Json(HttpStatusCode.Conflict, """{"title":"Conflict","detail":"Overlaps."}"""));
        var conflictResult = await new CrewApiClient(conflict).ApproveAvailabilityRequestAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.True(conflictResult.Failure!.IsConflict);

        using var validation = CreateHttpClient((_, _) => Json(HttpStatusCode.BadRequest, """{"title":"Invalid","errors":{"reason":["Reason is required."]}}"""));
        var validationResult = await new CrewApiClient(validation).RejectAvailabilityRequestAsync(
            Guid.NewGuid(), new RejectCrewAvailabilityRequestRequest(), CancellationToken.None);
        Assert.True(validationResult.Failure!.IsValidation);
        Assert.Equal("Reason is required.", Assert.Single(validationResult.Failure.Errors!["reason"]));

        using var notFound = CreateHttpClient((_, _) => Json(HttpStatusCode.NotFound, """{"title":"Not found"}"""));
        var notFoundResult = await new CrewApiClient(notFound).GetMemberAvailabilityAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.True(notFoundResult.Failure!.IsNotFound);
    }

    [Fact]
    public async Task RenewCertificateAsync_posts_to_the_certificate_renew_endpoint()
    {
        var certificateId = Guid.NewGuid();
        var predecessorId = Guid.NewGuid();
        var crewMemberId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, $$"""
                {"id":"{{certificateId}}","predecessorCertificateId":"{{predecessorId}}","crewMemberId":"{{crewMemberId}}",
                "type":"STCW","status":"Valid","concurrencyToken":"{{Guid.NewGuid()}}"}
                """);
        });

        var result = await new CrewApiClient(httpClient).RenewCertificateAsync(
            predecessorId,
            new RenewCrewCertificateRequest { Name = "Basic Safety", LicenseNumber = "LIC-2" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/api/crew/certificates/{predecessorId}/renew", captured!.RequestUri!.AbsolutePath);
        Assert.Equal(certificateId, result.Value!.Id);
        Assert.Equal(predecessorId, result.Value.PredecessorCertificateId);
    }

    [Fact]
    public async Task RenewCertificateAsync_reports_conflict_for_a_duplicate_renewal()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"This certificate already has a renewal successor."}"""));

        var result = await new CrewApiClient(httpClient).RenewCertificateAsync(
            Guid.NewGuid(), new RenewCrewCertificateRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task RevokeCertificateAsync_posts_to_the_certificate_revoke_endpoint()
    {
        var certificateId = Guid.NewGuid();
        var vesselId = Guid.NewGuid();
        var rankId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, $$"""
                {"id":"{{certificateId}}","revokedAtUtc":"2026-09-28T10:00:00Z","concurrencyToken":"{{Guid.NewGuid()}}",
                "affectedActiveAssignments":[{"assignmentId":"{{assignmentId}}","vesselId":"{{vesselId}}","rankId":"{{rankId}}"}]}
                """);
        });

        var result = await new CrewApiClient(httpClient).RevokeCertificateAsync(
            certificateId, new RevokeCrewCertificateRequest { Reason = "Issued in error" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/api/crew/certificates/{certificateId}/revoke", captured!.RequestUri!.AbsolutePath);
        var affected = Assert.Single(result.Value!.AffectedActiveAssignments);
        Assert.Equal(assignmentId, affected.AssignmentId);
        Assert.Equal(vesselId, affected.VesselId);
        Assert.Equal(rankId, affected.RankId);
    }

    [Fact]
    public async Task RevokeCertificateAsync_reports_a_validation_failure_for_a_missing_reason()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"reason":["Reason is required."]}}"""));

        var result = await new CrewApiClient(httpClient).RevokeCertificateAsync(
            Guid.NewGuid(), new RevokeCrewCertificateRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal("Reason is required.", Assert.Single(result.Failure.Errors!["reason"]));
    }

    [Fact]
    public async Task GetCertificateHistoryAsync_sends_request_to_the_certificate_history_endpoint()
    {
        var certificateId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, $$"""
                {"certificateId":"{{certificateId}}","chainCertificateIds":["{{certificateId}}"],"history":[]}
                """);
        });

        var result = await new CrewApiClient(httpClient).GetCertificateHistoryAsync(certificateId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/api/crew/certificates/{certificateId}/history", captured!.RequestUri!.AbsolutePath);
        Assert.Equal(certificateId, result.Value!.CertificateId);
    }

    [Fact]
    public async Task GetCertificateHistoryAsync_reports_not_found_for_an_unknown_certificate()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.NotFound, """{"title":"Not found"}"""));

        var result = await new CrewApiClient(httpClient).GetCertificateHistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
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
