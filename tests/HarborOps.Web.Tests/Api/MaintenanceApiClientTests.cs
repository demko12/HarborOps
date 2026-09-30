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

    private const string WorkOrderDetailsJson = """
        {"workOrderId":"10000000-0000-0000-0000-000000000001",
        "vesselId":"20000000-0000-0000-0000-000000000002","dryDockId":null,
        "assignedEngineerId":"80000000-0000-0000-0000-000000000008",
        "title":"Replace main engine filter","description":"Routine","priority":"High","status":"Open",
        "dueUtc":"2026-12-01T10:00:00Z","createdUtc":"2026-08-01T08:00:00Z","completedUtc":null,
        "isOverdue":true,"allowedActions":["Start","Cancel","ReplaceEquipment"],
        "equipment":[{"equipmentId":"30000000-0000-0000-0000-000000000003",
        "vesselId":"20000000-0000-0000-0000-000000000002","name":"Main engine","category":"Propulsion",
        "manufacturer":"MAN","serialNumber":"SN-1"}],
        "partUsages":[{"partId":"50000000-0000-0000-0000-000000000005","name":"Filter",
        "partNumber":"FILTER-001","quantityUsed":2.5,"remainingQuantity":7.5}],
        "completion":{"maintenanceRecordId":"60000000-0000-0000-0000-000000000006",
        "technicianId":"70000000-0000-0000-0000-000000000007","completionDate":"2026-08-20",
        "notes":"Done","cost":1250.50,"laborHours":6.5}}
        """;

    private const string StocktakeId = "c0000000-0000-0000-0000-00000000000c";
    private const string StocktakeItemId = "d0000000-0000-0000-0000-00000000000d";
    private const string StocktakeWorkOrderId = "e0000000-0000-0000-0000-00000000000e";
    private const string StocktakeConcurrencyToken = "f0000000-0000-0000-0000-00000000000f";

    private const string StocktakesPageJson = $$"""
        {"items":[{"stocktakeId":"{{StocktakeId}}","status":"Draft","lineCount":4,
        "countedLineCount":3,"varianceLineCount":1,"createdByUserId":"{{StocktakeItemId}}",
        "createdUtc":"2026-09-23T08:00:00Z","postedUtc":null,"cancelledUtc":null,
        "concurrencyToken":"{{StocktakeConcurrencyToken}}"}],
        "pageNumber":2,"pageSize":50,"totalCount":1}
        """;

    private const string StocktakeDetailsJson = $$"""
        {"stocktakeId":"{{StocktakeId}}","status":"Draft","notes":"Engine room locker",
        "concurrencyToken":"{{StocktakeConcurrencyToken}}","createdByUserId":"{{StocktakeItemId}}",
        "createdUtc":"2026-09-23T08:00:00Z","lastModifiedByUserId":null,"lastModifiedUtc":null,
        "postedByUserId":null,"postedUtc":null,"cancelledByUserId":null,"cancelledUtc":null,
        "cancellationReason":null,
        "lines":[{"inventoryItemId":"{{StocktakeItemId}}","name":"Main engine filter",
        "partNumber":"FILTER-001","unitAbbreviation":"pcs","baselineQuantity":10,
        "baselineCapturedUtc":"2026-09-23T08:00:00Z","countedQuantity":8,"variance":-2,
        "varianceReason":"Missing","countedByUserId":"{{StocktakeItemId}}",
        "countedUtc":"2026-09-23T08:30:00Z"}],
        "adjustments":[]}
        """;

    private const string StocktakePreviewJson = $$"""
        {"stocktakeId":"{{StocktakeId}}","concurrencyToken":"{{StocktakeConcurrencyToken}}",
        "previewToken":"TOKEN","canPost":false,
        "lines":[{"inventoryItemId":"{{StocktakeItemId}}","name":"Main engine filter",
        "partNumber":"FILTER-001","baselineQuantity":10,"currentQuantity":12,
        "countedQuantity":5,"variance":-5,"outstandingReservedQuantity":6,
        "baselineIsStale":true}],
        "blockers":[{"inventoryItemId":"{{StocktakeItemId}}","code":"BelowReserved",
        "message":"Counted 5.000 is below the outstanding reserved quantity 6.000.",
        "reservingWorkOrderIds":["{{StocktakeWorkOrderId}}"]}],
        "generatedUtc":"2026-09-23T08:00:00Z"}
        """;

    private const string PostStocktakeResultJson = $$"""
        {"stocktakeId":"{{StocktakeId}}","status":"Posted",
        "concurrencyToken":"{{StocktakeConcurrencyToken}}","postedByUserId":"{{StocktakeItemId}}",
        "postedUtc":"2026-09-23T09:00:00Z",
        "adjustments":[{"adjustmentId":"{{StocktakeWorkOrderId}}",
        "inventoryItemId":"{{StocktakeItemId}}","stocktakeId":"{{StocktakeId}}",
        "quantityBefore":10,"countedQuantity":8,"quantityDelta":-2,"reason":"Missing",
        "actorUserId":"{{StocktakeItemId}}","adjustedUtc":"2026-09-23T09:00:00Z"}]}
        """;

    private static string WorkOrderTransitionJson(string status, string allowedActions) => $$"""
        {"workOrderId":"10000000-0000-0000-0000-000000000001","status":"{{status}}",
        "allowedActions":{{allowedActions}},"completedUtc":null,"transitionedUtc":"2026-08-02T09:00:00Z"}
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

    [Fact]
    public async Task GetStocktakesAsync_sends_the_status_filter_and_paging()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, StocktakesPageJson);
        });

        var result = await CreateClient(httpClient).GetStocktakesAsync(
            new StocktakeFilter("Draft", 2, 50),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/maintenance/stocktakes", captured.RequestUri!.AbsolutePath);
        Assert.Contains("status=Draft", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", captured.RequestUri.Query, StringComparison.Ordinal);

        var row = Assert.Single(result.Value!.Items);

        Assert.Equal("Draft", row.Status);
        Assert.Equal(4, row.LineCount);
        Assert.Equal(3, row.CountedLineCount);
        Assert.Equal(1, row.VarianceLineCount);
        Assert.Equal(Guid.Parse(StocktakeConcurrencyToken), row.ConcurrencyToken);
    }

    [Fact]
    public async Task GetStocktakesAsync_omits_an_unset_status()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, StocktakesPageJson);
        });

        await CreateClient(httpClient).GetStocktakesAsync(
            new StocktakeFilter(null, 1, 25),
            CancellationToken.None);

        Assert.DoesNotContain("status=", captured!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStocktakeAsync_reads_the_lines_with_their_baseline()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, StocktakeDetailsJson);
        });

        var result = await CreateClient(httpClient).GetStocktakeAsync(
            Guid.Parse(StocktakeId),
            CancellationToken.None);

        Assert.Equal($"/api/maintenance/stocktakes/{StocktakeId}", captured!.RequestUri!.AbsolutePath);

        var line = Assert.Single(result.Value!.Lines);

        Assert.Equal("Main engine filter", line.Name);
        Assert.Equal("pcs", line.UnitAbbreviation);
        Assert.Equal(10m, line.BaselineQuantity);
        Assert.Equal(8m, line.CountedQuantity);
        Assert.Equal(-2m, line.Variance);
        Assert.Equal("Missing", line.VarianceReason);
    }

    [Fact]
    public async Task GetStocktakeAsync_reports_an_unknown_stocktake()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Stocktake not found.","detail":"No such stocktake."}"""));

        var result = await CreateClient(httpClient).GetStocktakeAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal((int)HttpStatusCode.NotFound, result.Failure!.StatusCode);
        Assert.Equal("Stocktake not found.", result.Failure.Title);
    }

    [Fact]
    public async Task CreateStocktakeAsync_posts_the_items_and_reads_the_created_draft()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(HttpStatusCode.Created, StocktakeDetailsJson);
        });

        var result = await CreateClient(httpClient).CreateStocktakeAsync(
            new CreateStocktakeRequest
            {
                InventoryItemIds = [Guid.Parse(StocktakeItemId)],
                Notes = "Engine room locker",
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/maintenance/stocktakes", captured.RequestUri!.AbsolutePath);
        Assert.Contains(StocktakeItemId, body!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Engine room locker", body, StringComparison.Ordinal);
        Assert.Equal("Draft", result.Value!.Status);
    }

    [Fact]
    public async Task CreateStocktakeAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """
            {"title":"One or more validation errors occurred.","status":400,
            "errors":{"inventoryItemIds":["Inventory item ids must be distinct."]}}
            """));

        var result = await CreateClient(httpClient).CreateStocktakeAsync(
            new CreateStocktakeRequest { InventoryItemIds = [Guid.NewGuid()] },
            CancellationToken.None);

        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("inventoryItemIds", result.Failure.Errors.Keys);
    }

    [Fact]
    public async Task UpdateStocktakeCountsAsync_puts_the_counts_with_the_expected_token()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(HttpStatusCode.OK, StocktakeDetailsJson);
        });

        var result = await CreateClient(httpClient).UpdateStocktakeCountsAsync(
            Guid.Parse(StocktakeId),
            new UpdateStocktakeCountsRequest
            {
                ExpectedConcurrencyToken = Guid.Parse(StocktakeConcurrencyToken),
                Lines =
                [
                    new StocktakeCountEntry
                    {
                        InventoryItemId = Guid.Parse(StocktakeItemId),
                        CountedQuantity = 8m,
                        Reason = "Missing",
                    },
                ],
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Equal($"/api/maintenance/stocktakes/{StocktakeId}/counts", captured.RequestUri!.AbsolutePath);
        Assert.Contains(StocktakeConcurrencyToken, body!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"countedQuantity\":8", body, StringComparison.Ordinal);
        Assert.Contains("Missing", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateStocktakeCountsAsync_reports_a_concurrent_change_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """
            {"title":"Stocktake was modified concurrently.",
            "detail":"The stocktake was changed by another request. Reload and try again."}
            """));

        var result = await CreateClient(httpClient).UpdateStocktakeCountsAsync(
            Guid.NewGuid(),
            new UpdateStocktakeCountsRequest { ExpectedConcurrencyToken = Guid.NewGuid(), Lines = [] },
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Stocktake was modified concurrently.", result.Failure.Title);
    }

    [Fact]
    public async Task RefreshStocktakeLinesAsync_posts_the_items_to_recount()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(HttpStatusCode.OK, StocktakeDetailsJson);
        });

        await CreateClient(httpClient).RefreshStocktakeLinesAsync(
            Guid.Parse(StocktakeId),
            new RefreshStocktakeLinesRequest
            {
                ExpectedConcurrencyToken = Guid.Parse(StocktakeConcurrencyToken),
                InventoryItemIds = [Guid.Parse(StocktakeItemId)],
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/maintenance/stocktakes/{StocktakeId}/refresh", captured.RequestUri!.AbsolutePath);
        Assert.Contains(StocktakeItemId, body!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStocktakePreviewAsync_reads_the_token_lines_and_blockers()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, StocktakePreviewJson);
        });

        var result = await CreateClient(httpClient).GetStocktakePreviewAsync(
            Guid.Parse(StocktakeId),
            CancellationToken.None);

        Assert.Equal($"/api/maintenance/stocktakes/{StocktakeId}/preview", captured!.RequestUri!.AbsolutePath);

        var preview = result.Value!;

        Assert.False(preview.CanPost);
        Assert.Equal("TOKEN", preview.PreviewToken);
        Assert.Equal(Guid.Parse(StocktakeConcurrencyToken), preview.ConcurrencyToken);

        var line = Assert.Single(preview.Lines);

        Assert.Equal(6m, line.OutstandingReservedQuantity);
        Assert.True(line.BaselineIsStale);

        var blocker = Assert.Single(preview.Blockers);

        Assert.Equal("BelowReserved", blocker.Code);
        Assert.Equal(Guid.Parse(StocktakeWorkOrderId), Assert.Single(blocker.ReservingWorkOrderIds));
    }

    [Fact]
    public async Task PostStocktakeAsync_sends_the_preview_token_and_reads_the_adjustments()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(HttpStatusCode.OK, PostStocktakeResultJson);
        });

        var result = await CreateClient(httpClient).PostStocktakeAsync(
            Guid.Parse(StocktakeId),
            new PostStocktakeRequest
            {
                ExpectedConcurrencyToken = Guid.Parse(StocktakeConcurrencyToken),
                PreviewToken = "TOKEN",
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/maintenance/stocktakes/{StocktakeId}/post", captured.RequestUri!.AbsolutePath);
        Assert.Contains("TOKEN", body!, StringComparison.Ordinal);

        var adjustment = Assert.Single(result.Value!.Adjustments);

        Assert.Equal("Posted", result.Value.Status);
        Assert.Equal(10m, adjustment.QuantityBefore);
        Assert.Equal(8m, adjustment.CountedQuantity);
        Assert.Equal(-2m, adjustment.QuantityDelta);
        Assert.Equal("Missing", adjustment.Reason);
    }

    [Fact]
    public async Task PostStocktakeAsync_reports_a_stale_preview_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """
            {"title":"Stocktake posting preview is out of date.",
            "detail":"Inventory changed after the posting preview."}
            """));

        var result = await CreateClient(httpClient).PostStocktakeAsync(
            Guid.NewGuid(),
            new PostStocktakeRequest { ExpectedConcurrencyToken = Guid.NewGuid(), PreviewToken = "TOKEN" },
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Stocktake posting preview is out of date.", result.Failure.Title);
    }

    [Fact]
    public async Task CancelStocktakeAsync_posts_the_reason()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(HttpStatusCode.OK, StocktakeDetailsJson);
        });

        await CreateClient(httpClient).CancelStocktakeAsync(
            Guid.Parse(StocktakeId),
            new CancelStocktakeRequest
            {
                ExpectedConcurrencyToken = Guid.Parse(StocktakeConcurrencyToken),
                Reason = "Counted the wrong locker",
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/maintenance/stocktakes/{StocktakeId}/cancel", captured.RequestUri!.AbsolutePath);
        Assert.Contains("Counted the wrong locker", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stocktake_call_surfaces_a_forbidden_response()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, "{}"));

        var result = await CreateClient(httpClient).GetStocktakesAsync(
            new StocktakeFilter(null, 1, 25),
            CancellationToken.None);

        Assert.True(result.Failure!.IsForbidden);
    }

    [Fact]
    public async Task The_stocktake_calls_report_an_unavailable_api()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));
        var client = CreateClient(httpClient);
        var stocktakeId = Guid.Parse(StocktakeId);

        var list = await client.GetStocktakesAsync(new StocktakeFilter(null, 1, 25), CancellationToken.None);
        var details = await client.GetStocktakeAsync(stocktakeId, CancellationToken.None);
        var create = await client.CreateStocktakeAsync(new CreateStocktakeRequest(), CancellationToken.None);
        var counts = await client.UpdateStocktakeCountsAsync(
            stocktakeId,
            new UpdateStocktakeCountsRequest(),
            CancellationToken.None);
        var refresh = await client.RefreshStocktakeLinesAsync(
            stocktakeId,
            new RefreshStocktakeLinesRequest(),
            CancellationToken.None);
        var preview = await client.GetStocktakePreviewAsync(stocktakeId, CancellationToken.None);
        var post = await client.PostStocktakeAsync(
            stocktakeId,
            new PostStocktakeRequest(),
            CancellationToken.None);
        var cancel = await client.CancelStocktakeAsync(
            stocktakeId,
            new CancelStocktakeRequest(),
            CancellationToken.None);

        Assert.Equal(0, list.Failure!.StatusCode);
        Assert.Equal(0, details.Failure!.StatusCode);
        Assert.Equal(0, create.Failure!.StatusCode);
        Assert.Equal(0, counts.Failure!.StatusCode);
        Assert.Equal(0, refresh.Failure!.StatusCode);
        Assert.Equal(0, preview.Failure!.StatusCode);
        Assert.Equal(0, post.Failure!.StatusCode);
        Assert.Equal(0, cancel.Failure!.StatusCode);
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

    private const string PurchaseOrdersPageJson = """
        {"items":[{"purchaseOrderId":"b0000000-0000-0000-0000-00000000000b",
        "supplierId":"a0000000-0000-0000-0000-00000000000a","supplierName":"Baltic Marine Parts",
        "orderDate":"2026-08-20","expectedDelivery":"2026-09-01","status":"Ordered",
        "lineCount":2,"totalCost":302.00,"isOverdue":false,"daysUntilDelivery":6}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    [Fact]
    public async Task GetSuppliersAsync_escapes_the_search_term()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0}""");
        });

        var result = await CreateClient(httpClient).GetSuppliersAsync(
            new SupplierFilter("Baltic & Co", 1, 25),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/maintenance/suppliers", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("search=Baltic%20%26%20Co", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_sends_every_filter_and_the_paging()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PurchaseOrdersPageJson);
        });

        var supplierId = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
        var filter = new PurchaseOrderFilter(
            supplierId,
            "Ordered",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            true,
            2,
            50);

        var result = await CreateClient(httpClient).GetPurchaseOrdersAsync(filter, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Baltic Marine Parts", result.Value!.Items[0].SupplierName);
        Assert.Equal("/api/maintenance/purchase-orders", captured!.RequestUri!.AbsolutePath);

        var query = captured.RequestUri.Query;
        Assert.Contains($"supplierId={supplierId}", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status=Ordered", query, StringComparison.Ordinal);
        Assert.Contains("orderDateFrom=2026-08-01", query, StringComparison.Ordinal);
        Assert.Contains("orderDateTo=2026-08-31", query, StringComparison.Ordinal);
        Assert.Contains("expectedDeliveryFrom=2026-09-01", query, StringComparison.Ordinal);
        Assert.Contains("expectedDeliveryTo=2026-09-30", query, StringComparison.Ordinal);
        Assert.Contains("overdue=true", query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_omits_unset_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PurchaseOrdersPageJson);
        });

        await CreateClient(httpClient).GetPurchaseOrdersAsync(
            new PurchaseOrderFilter(null, null, null, null, null, null, null, 1, 25),
            CancellationToken.None);

        var query = captured!.RequestUri!.Query;

        Assert.DoesNotContain("supplierId", query, StringComparison.Ordinal);
        Assert.DoesNotContain("status", query, StringComparison.Ordinal);
        Assert.DoesNotContain("overdue", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_posts_the_lines_and_reads_the_created_order()
    {
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(
                HttpStatusCode.Created,
                """
                {"purchaseOrderId":"b0000000-0000-0000-0000-00000000000b","status":"Draft",
                "lineCount":1,"totalCost":102.00}
                """);
        });

        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = Guid.Parse("a0000000-0000-0000-0000-00000000000a"),
            OrderDate = new DateOnly(2026, 8, 20),
            ExpectedDelivery = new DateOnly(2026, 9, 1),
            Lines =
            [
                new CreatePurchaseOrderLineRequest
                {
                    InventoryItemId = Guid.Parse("c0000000-0000-0000-0000-00000000000c"),
                    QuantityOrdered = 4m,
                    UnitCost = 25.50m,
                },
            ],
        };

        var result = await CreateClient(httpClient).CreatePurchaseOrderAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Draft", result.Value!.Status);
        Assert.Equal(102.00m, result.Value.TotalCost);
        Assert.Contains("\"orderDate\":\"2026-08-20\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"quantityOrdered\":4", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_maps_a_missing_item_to_a_not_found_failure()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Inventory item not found.","detail":"Inventory items were not found."}"""));

        var result = await CreateClient(httpClient).CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
        Assert.Equal("Inventory item not found.", result.Failure.Title);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_posts_the_confirmation_to_the_receive_action()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(
                HttpStatusCode.OK,
                """
                {"purchaseOrderId":"b0000000-0000-0000-0000-00000000000b","previousStatus":"Ordered",
                "status":"Received","allowedTransitions":[],
                "stockAdjustments":[{"inventoryItemId":"c0000000-0000-0000-0000-00000000000c",
                "quantityReceived":4,"quantityOnHand":14}],
                "receipt":{"receiptId":"d0000000-0000-0000-0000-00000000000d",
                "purchaseOrderId":"b0000000-0000-0000-0000-00000000000b",
                "clientRequestId":"a1000000-0000-0000-0000-00000000001a","receivedUtc":"2026-09-01T06:00:00Z",
                "deliveryReference":"DN-9","notes":null,"recordedByUserId":"a2000000-0000-0000-0000-00000000002a",
                "recordedUtc":"2026-09-01T08:00:00Z","orderStatusBefore":"Ordered","orderStatusAfter":"Received",
                "totalQuantity":4,"lines":[],"movements":[]},
                "alreadyPosted":false}
                """);
        });

        var purchaseOrderId = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
        var clientRequestId = Guid.Parse("a1000000-0000-0000-0000-00000000001a");

        var result = await CreateClient(httpClient).ReceivePurchaseOrderAsync(
            purchaseOrderId,
            new ReceiveRemainingPurchaseOrderRequest
            {
                ClientRequestId = clientRequestId,
                ExpectedConcurrencyToken = Guid.Parse("a3000000-0000-0000-0000-00000000003a"),
                PreviewToken = "ABC123",
                ReceivedUtc = new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc),
                DeliveryReference = "DN-9",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/maintenance/purchase-orders/{purchaseOrderId}/receive", captured.RequestUri!.AbsolutePath);
        Assert.Contains($"\"clientRequestId\":\"{clientRequestId}\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"previewToken\":\"ABC123\"", body, StringComparison.Ordinal);
        Assert.Contains("\"receivedUtc\":\"2026-09-01T06:00:00Z\"", body, StringComparison.Ordinal);
        Assert.Contains("\"deliveryReference\":\"DN-9\"", body, StringComparison.Ordinal);
        Assert.Equal("Received", result.Value!.Status);
        Assert.Equal(14m, Assert.Single(result.Value.StockAdjustments).QuantityOnHand);
        Assert.Empty(result.Value.AllowedTransitions);
        Assert.Equal("DN-9", result.Value.Receipt!.DeliveryReference);
        Assert.False(result.Value.AlreadyPosted);
    }

    [Fact]
    public async Task A_blocked_receipt_is_reported_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """
            {"title":"Purchase order receipt has posting blockers.",
            "detail":"Purchase order 'b0000000' is Received; only ordered or partially received orders can be received."}
            """));

        var result = await CreateClient(httpClient).ReceivePurchaseOrderAsync(
            Guid.NewGuid(),
            new ReceiveRemainingPurchaseOrderRequest(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Purchase order receipt has posting blockers.", result.Failure.Title);
        Assert.Contains("only ordered or partially received", result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkPurchaseOrderOrderedAsync_and_CancelPurchaseOrderAsync_use_their_own_actions()
    {
        var paths = new List<string>();
        using var httpClient = CreateHttpClient((request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);

            return Json(
                HttpStatusCode.OK,
                """
                {"purchaseOrderId":"b0000000-0000-0000-0000-00000000000b","previousStatus":"Draft",
                "status":"Ordered","allowedTransitions":["Received","Cancelled"],"stockAdjustments":[]}
                """);
        });

        var client = CreateClient(httpClient);
        var purchaseOrderId = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

        await client.MarkPurchaseOrderOrderedAsync(purchaseOrderId, CancellationToken.None);
        await client.CancelPurchaseOrderAsync(purchaseOrderId, CancellationToken.None);

        Assert.Equal(
            [
                $"/api/maintenance/purchase-orders/{purchaseOrderId}/order",
                $"/api/maintenance/purchase-orders/{purchaseOrderId}/cancel",
            ],
            paths);
    }

    [Fact]
    public async Task GetWorkOrderDetailsAsync_reads_the_scope_usage_and_completion()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, WorkOrderDetailsJson);
        });

        var result = await CreateClient(httpClient).GetWorkOrderDetailsAsync(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal(
            "/api/maintenance/work-orders/10000000-0000-0000-0000-000000000001",
            captured.RequestUri!.AbsolutePath);
        Assert.Equal("Replace main engine filter", result.Value!.Title);
        Assert.True(result.Value.IsOverdue);
        Assert.Equal(["Start", "Cancel", "ReplaceEquipment"], result.Value.AllowedActions);
        Assert.Equal("Main engine", Assert.Single(result.Value.Equipment).Name);
        Assert.Equal(2.5m, Assert.Single(result.Value.PartUsages).QuantityUsed);
        Assert.Equal(new DateOnly(2026, 8, 20), result.Value.Completion!.CompletionDate);
    }

    [Fact]
    public async Task GetWorkOrderDetailsAsync_surfaces_not_found()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Maintenance work order not found.","detail":"Gone."}"""));

        var result = await CreateClient(httpClient).GetWorkOrderDetailsAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task ReplaceWorkOrderEquipmentAsync_puts_the_selected_identifiers()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(
                HttpStatusCode.OK,
                """
                {"workOrderId":"10000000-0000-0000-0000-000000000001","status":"Open",
                "allowedActions":["Start","Cancel","ReplaceEquipment"],
                "equipment":[{"equipmentId":"30000000-0000-0000-0000-000000000003",
                "vesselId":"20000000-0000-0000-0000-000000000002","name":"Main engine",
                "category":"Propulsion","manufacturer":null,"serialNumber":null}],
                "updatedUtc":"2026-08-02T09:00:00Z"}
                """);
        });

        var equipmentId = Guid.Parse("30000000-0000-0000-0000-000000000003");

        var result = await CreateClient(httpClient).ReplaceWorkOrderEquipmentAsync(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            new ReplaceWorkOrderEquipmentWebRequest([equipmentId]),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Equal(
            "/api/maintenance/work-orders/10000000-0000-0000-0000-000000000001/equipment",
            captured.RequestUri!.AbsolutePath);
        Assert.Contains(equipmentId.ToString(), body!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Main engine", Assert.Single(result.Value!.Equipment).Name);
    }

    [Fact]
    public async Task ReplaceWorkOrderEquipmentAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """
            {"title":"One or more validation errors occurred.",
            "errors":{"equipmentIds":["Must not contain duplicates."]}}
            """));

        var result = await CreateClient(httpClient).ReplaceWorkOrderEquipmentAsync(
            Guid.NewGuid(),
            new ReplaceWorkOrderEquipmentWebRequest([Guid.NewGuid()]),
            CancellationToken.None);

        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("equipmentIds", result.Failure.Errors);
    }

    [Fact]
    public async Task ReplaceWorkOrderEquipmentAsync_surfaces_a_vessel_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """
            {"title":"Equipment does not belong to the work order vessel.",
            "detail":"Other vessel."}
            """));

        var result = await CreateClient(httpClient).ReplaceWorkOrderEquipmentAsync(
            Guid.NewGuid(),
            new ReplaceWorkOrderEquipmentWebRequest([Guid.NewGuid()]),
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Other vessel.", result.Failure.Message);
    }

    [Fact]
    public async Task StartWorkOrderAsync_posts_to_the_start_action()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, WorkOrderTransitionJson(
                "InProgress",
                """["Cancel","ReplaceEquipment"]"""));
        });

        var result = await CreateClient(httpClient).StartWorkOrderAsync(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            "/api/maintenance/work-orders/10000000-0000-0000-0000-000000000001/start",
            captured.RequestUri!.AbsolutePath);
        Assert.Equal("InProgress", result.Value!.Status);
        Assert.Equal(["Cancel", "ReplaceEquipment"], result.Value.AllowedActions);
    }

    [Fact]
    public async Task CancelWorkOrderAsync_posts_to_the_cancel_action()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, WorkOrderTransitionJson("Canceled", "[]"));
        });

        var result = await CreateClient(httpClient).CancelWorkOrderAsync(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            CancellationToken.None);

        Assert.Equal(
            "/api/maintenance/work-orders/10000000-0000-0000-0000-000000000001/cancel",
            captured!.RequestUri!.AbsolutePath);
        Assert.Equal("Canceled", result.Value!.Status);
        Assert.Empty(result.Value.AllowedActions);
    }

    [Fact]
    public async Task A_rejected_work_order_transition_is_reported_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """
            {"title":"Maintenance work order status transition is not allowed.",
            "detail":"Already started."}
            """));

        var result = await CreateClient(httpClient).StartWorkOrderAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Already started.", result.Failure.Message);
    }

    [Fact]
    public async Task A_work_order_transition_surfaces_a_forbidden_response()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, "{}"));

        var result = await CreateClient(httpClient).CancelWorkOrderAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsForbidden);
    }

    [Fact]
    public async Task The_work_order_lifecycle_calls_report_an_unavailable_api()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));
        var client = CreateClient(httpClient);
        var workOrderId = Guid.NewGuid();

        var details = await client.GetWorkOrderDetailsAsync(workOrderId, CancellationToken.None);
        var equipment = await client.ReplaceWorkOrderEquipmentAsync(
            workOrderId,
            new ReplaceWorkOrderEquipmentWebRequest([]),
            CancellationToken.None);
        var start = await client.StartWorkOrderAsync(workOrderId, CancellationToken.None);

        Assert.Equal(0, details.Failure!.StatusCode);
        Assert.Equal(0, equipment.Failure!.StatusCode);
        Assert.Equal(0, start.Failure!.StatusCode);
    }
}
