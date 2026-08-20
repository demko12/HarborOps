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

    private const string InventoryPageJson = """
        {"items":[{"inventoryItemId":"80000000-0000-0000-0000-000000000008",
        "name":"Main engine filter","partNumber":"FILTER-001","quantity":7.5,
        "minimumQuantity":2,"unitCost":150,
        "categoryId":"90000000-0000-0000-0000-000000000009",
        "unitId":"a0000000-0000-0000-0000-00000000000a",
        "supplierId":"b0000000-0000-0000-0000-00000000000b"}],
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

    [Fact]
    public async Task GetInventoryItemsAsync_sends_search_and_low_stock_filter()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, InventoryPageJson);
        });

        var filter = new InventoryItemFilter("Fuel", true, 2, 50);

        var result = await CreateClient(httpClient).GetInventoryItemsAsync(filter, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/maintenance/inventory-items", captured.RequestUri!.AbsolutePath);
        Assert.Contains("search=Fuel", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("lowStock=true", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_omits_unset_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, InventoryPageJson);
        });

        await CreateClient(httpClient).GetInventoryItemsAsync(
            new InventoryItemFilter(null, null, 1, 25),
            CancellationToken.None);

        Assert.DoesNotContain("search=", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("lowStock=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_returns_the_page()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, InventoryPageJson));

        var result = await CreateClient(httpClient).GetInventoryItemsAsync(
            new InventoryItemFilter(null, null, 1, 25),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Main engine filter", item.Name);
        Assert.Equal(7.5m, item.Quantity);
    }

    [Fact]
    public async Task GetPartUsagesAsync_returns_the_usage_items()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.OK,
            """
            {"items":[{"partId":"80000000-0000-0000-0000-000000000008",
            "name":"Main engine filter","partNumber":"FILTER-001",
            "quantityUsed":2.5,"remainingQuantity":7.5}]}
            """));

        var result = await CreateClient(httpClient).GetPartUsagesAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(2.5m, item.QuantityUsed);
        Assert.Equal(7.5m, item.RemainingQuantity);
    }

    [Fact]
    public async Task GetPartUsagesAsync_surfaces_not_found_for_an_unknown_work_order()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Maintenance work order not found."}"""));

        var result = await CreateClient(httpClient).GetPartUsagesAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task RecordPartUsageAsync_returns_the_result()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(
                HttpStatusCode.Created,
                """
                {"workOrderId":"20000000-0000-0000-0000-000000000001",
                "partId":"80000000-0000-0000-0000-000000000008",
                "quantityUsed":2.5,"remainingQuantity":7.5}
                """);
        });

        var workOrderId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var partId = Guid.Parse("80000000-0000-0000-0000-000000000008");

        var result = await CreateClient(httpClient).RecordPartUsageAsync(
            workOrderId, partId, new RecordPartUsageRequest { QuantityUsed = 2.5m }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7.5m, result.Value!.RemainingQuantity);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            $"/api/maintenance/work-orders/{workOrderId}/parts/{partId}/usage",
            captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RecordPartUsageAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"quantityUsed":["QuantityUsed must be greater than zero."]}}"""));

        var result = await CreateClient(httpClient).RecordPartUsageAsync(
            Guid.NewGuid(), Guid.NewGuid(), new RecordPartUsageRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("quantityUsed", result.Failure.Errors.Keys);
    }

    [Fact]
    public async Task RecordPartUsageAsync_surfaces_conflict_for_insufficient_stock()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Inventory item usage could not be recorded.","detail":"Insufficient stock available."}"""));

        var result = await CreateClient(httpClient).RecordPartUsageAsync(
            Guid.NewGuid(), Guid.NewGuid(), new RecordPartUsageRequest { QuantityUsed = 999m }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    private const string EquipmentPageJson = """
        {"items":[{"equipmentId":"c0000000-0000-0000-0000-00000000000c",
        "vesselId":"30000000-0000-0000-0000-000000000002","name":"Main engine",
        "category":"Propulsion","manufacturer":"Wartsila","serialNumber":"SN-1001"}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private const string SchedulesPageJson = """
        {"items":[{"scheduleId":"d0000000-0000-0000-0000-00000000000d",
        "vesselId":"30000000-0000-0000-0000-000000000002","maintenanceType":"Quarterly inspection",
        "frequency":"Quarterly","nextDueDate":"2026-09-30","nextDueHours":null,
        "isOverdue":false,"daysUntilDue":12,"equipmentCount":2}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private const string ScheduleDetailsJson = """
        {"scheduleId":"d0000000-0000-0000-0000-00000000000d",
        "vesselId":"30000000-0000-0000-0000-000000000002","maintenanceType":"Quarterly inspection",
        "frequency":"Quarterly","nextDueDate":"2026-09-30","nextDueHours":null,
        "isOverdue":false,"daysUntilDue":12,
        "equipment":[{"equipmentId":"c0000000-0000-0000-0000-00000000000c",
        "vesselId":"30000000-0000-0000-0000-000000000002","name":"Main engine",
        "category":"Propulsion","manufacturer":null,"serialNumber":null}]}
        """;

    [Fact]
    public async Task GetEquipmentAsync_sends_all_filters_and_paging_to_the_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, EquipmentPageJson);
        });

        var vesselId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var filter = new EquipmentFilter(vesselId, "Propulsion", "Wartsila", "SN-1001", 2, 50);

        var result = await CreateClient(httpClient).GetEquipmentAsync(filter, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Main engine", Assert.Single(result.Value!.Items).Name);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/maintenance/equipment", captured.RequestUri!.AbsolutePath);
        Assert.Contains($"vesselId={vesselId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("category=Propulsion", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("manufacturer=Wartsila", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("search=SN-1001", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEquipmentAsync_omits_unset_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, EquipmentPageJson);
        });

        var filter = new EquipmentFilter(null, null, null, null, 1, 25);

        await CreateClient(httpClient).GetEquipmentAsync(filter, CancellationToken.None);

        Assert.DoesNotContain("vesselId=", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("category=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("manufacturer=", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("search=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEquipmentAsync_surfaces_a_forbidden_response()
    {
        using var httpClient = CreateHttpClient((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var result = await CreateClient(httpClient).GetEquipmentAsync(
            new EquipmentFilter(null, null, null, null, 1, 25), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsForbidden);
    }

    [Fact]
    public async Task GetEquipmentAsync_reports_the_api_as_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("boom"));

        var result = await CreateClient(httpClient).GetEquipmentAsync(
            new EquipmentFilter(null, null, null, null, 1, 25), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
    }

    [Fact]
    public async Task GetEquipmentAsync_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = CreateHttpClient((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();

            return Json(HttpStatusCode.OK, EquipmentPageJson);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(httpClient).GetEquipmentAsync(
                new EquipmentFilter(null, null, null, null, 1, 25), cancellation.Token));
    }

    [Fact]
    public async Task RegisterEquipmentAsync_posts_the_request_and_returns_the_new_id()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, """{"id":"c0000000-0000-0000-0000-00000000000c"}""");
        });

        var result = await CreateClient(httpClient).RegisterEquipmentAsync(
            new RegisterEquipmentRequest
            {
                VesselId = Guid.Parse("30000000-0000-0000-0000-000000000002"),
                Name = "Main engine",
                Category = "Propulsion",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Guid.Parse("c0000000-0000-0000-0000-00000000000c"), result.Value!.Id);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/maintenance/equipment", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RegisterEquipmentAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"name":["Name is required."]}}"""));

        var result = await CreateClient(httpClient).RegisterEquipmentAsync(
            new RegisterEquipmentRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("name", result.Failure.Errors.Keys);
    }

    [Fact]
    public async Task GetSchedulesAsync_sends_all_filters_including_the_due_window()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, SchedulesPageJson);
        });

        var vesselId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var equipmentId = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
        var filter = new MaintenanceScheduleFilter(
            vesselId,
            equipmentId,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 10, 1),
            true,
            2,
            50);

        var result = await CreateClient(httpClient).GetSchedulesAsync(filter, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 30), Assert.Single(result.Value!.Items).NextDueDate);
        Assert.Equal("/api/maintenance/schedules", captured!.RequestUri!.AbsolutePath);
        Assert.Contains($"vesselId={vesselId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains($"equipmentId={equipmentId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("dueFrom=2026-09-01", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("dueTo=2026-10-01", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("overdue=true", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetScheduleDetailsAsync_returns_the_schedule_with_its_equipment()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, ScheduleDetailsJson);
        });

        var scheduleId = Guid.Parse("d0000000-0000-0000-0000-00000000000d");

        var result = await CreateClient(httpClient).GetScheduleDetailsAsync(scheduleId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Main engine", Assert.Single(result.Value!.Equipment).Name);
        Assert.Equal($"/api/maintenance/schedules/{scheduleId}", captured!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetScheduleDetailsAsync_surfaces_not_found()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Maintenance schedule not found.","detail":"No schedule with that id."}"""));

        var result = await CreateClient(httpClient).GetScheduleDetailsAsync(
            Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task RegisterScheduleAsync_surfaces_a_vessel_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Maintenance schedule equipment spans more than one vessel."}"""));

        var result = await CreateClient(httpClient).RegisterScheduleAsync(
            new RegisterMaintenanceScheduleRequest
            {
                MaintenanceType = "Inspection",
                Frequency = "Monthly",
                NextDueDate = new DateOnly(2026, 9, 30),
                EquipmentIds = [Guid.NewGuid(), Guid.NewGuid()],
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task RegisterScheduleAsync_posts_the_request_and_returns_the_new_id()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, """{"id":"d0000000-0000-0000-0000-00000000000d"}""");
        });

        var result = await CreateClient(httpClient).RegisterScheduleAsync(
            new RegisterMaintenanceScheduleRequest
            {
                MaintenanceType = "Inspection",
                Frequency = "Monthly",
                NextDueDate = new DateOnly(2026, 9, 30),
                EquipmentIds = [Guid.NewGuid()],
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Guid.Parse("d0000000-0000-0000-0000-00000000000d"), result.Value!.Id);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/maintenance/schedules", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdateScheduleNextDueAsync_posts_to_the_next_due_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(
                HttpStatusCode.OK,
                """
                {"scheduleId":"d0000000-0000-0000-0000-00000000000d","frequency":"Monthly",
                "nextDueDate":"2026-09-18","nextDueHours":null}
                """);
        });

        var scheduleId = Guid.Parse("d0000000-0000-0000-0000-00000000000d");

        var result = await CreateClient(httpClient).UpdateScheduleNextDueAsync(
            scheduleId,
            new UpdateScheduleNextDueRequest { CompletedOn = new DateOnly(2026, 8, 18) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 18), result.Value!.NextDueDate);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/maintenance/schedules/{scheduleId}/next-due", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdateScheduleNextDueAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """
            {"title":"One or more validation errors occurred.",
            "errors":{"nextDueHours":["Next due hours is required for a running-hours schedule."]}}
            """));

        var result = await CreateClient(httpClient).UpdateScheduleNextDueAsync(
            Guid.NewGuid(),
            new UpdateScheduleNextDueRequest { CompletedOn = new DateOnly(2026, 8, 18) },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("nextDueHours", result.Failure.Errors.Keys);
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
