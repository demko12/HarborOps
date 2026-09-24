using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class StocktakeEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public StocktakeEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_201_with_baseline_lines()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Fuel filter", "FF-001", quantity: 12.5m);

        using var response = await client.PostAsJsonAsync(
            StocktakesUri(),
            new { inventoryItemIds = new[] { item.Id }, notes = "Engine room locker" },
            CancellationToken.None);

        var details = await response.Content.ReadFromJsonAsync<StocktakeDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal($"/api/maintenance/stocktakes/{details.StocktakeId}", response.Headers.Location?.OriginalString);
        Assert.Equal("Draft", details.Status);
        Assert.NotEqual(Guid.Empty, details.ConcurrencyToken);

        var line = Assert.Single(details.Lines);

        Assert.Equal(12.5m, line.BaselineQuantity);
        Assert.Equal("pcs", line.UnitAbbreviation);
        Assert.Null(line.CountedQuantity);
    }

    [Fact]
    public async Task Post_returns_400_for_duplicate_items()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Fuel filter", "FF-001", quantity: 1m);

        using var response = await client.PostAsJsonAsync(
            StocktakesUri(),
            new { inventoryItemIds = new[] { item.Id, item.Id } },
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CreateStocktakeValidator.InventoryItemIdsField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_item()
    {
        using var client = await CreateManagerClientAsync();

        using var response = await client.PostAsJsonAsync(
            StocktakesUri(),
            new { inventoryItemIds = new[] { Guid.NewGuid() } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Complete_count_is_posted_once_with_traceable_adjustments()
    {
        using var client = await CreateManagerClientAsync();
        var surplus = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        var shortage = await SeedItemAsync("Gasket", "GSK-001", quantity: 10.5m);
        var draft = await CreateStocktakeAsync(client, surplus.Id, shortage.Id);
        await CountAsync(
            client,
            draft,
            (surplus.Id, 12m, "Found in spare locker"),
            (shortage.Id, 10.125m, "Damaged"));
        var preview = await PreviewAsync(client, draft.StocktakeId);

        Assert.True(preview.CanPost);
        Assert.Equal(10m, await StoredQuantityAsync(surplus.Id));

        using var postResponse = await PostStocktakeAsync(client, preview);

        var posted = await postResponse.Content.ReadFromJsonAsync<PostStocktakeResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        Assert.NotNull(posted);
        Assert.Equal("Posted", posted.Status);
        Assert.Equal(2, posted.Adjustments.Count);

        var gasketAdjustment = posted.Adjustments.Single(adjustment => adjustment.InventoryItemId == shortage.Id);

        Assert.Equal(10.5m, gasketAdjustment.QuantityBefore);
        Assert.Equal(10.125m, gasketAdjustment.CountedQuantity);
        Assert.Equal(-0.375m, gasketAdjustment.QuantityDelta);
        Assert.Equal("Damaged", gasketAdjustment.Reason);
        Assert.Equal(draft.StocktakeId, gasketAdjustment.StocktakeId);
        Assert.Equal(draft.CreatedByUserId, gasketAdjustment.ActorUserId);
        Assert.Equal(12m, await StoredQuantityAsync(surplus.Id));
        Assert.Equal(10.125m, await StoredQuantityAsync(shortage.Id));

        using var repeatResponse = await PostStocktakeAsync(client, preview);

        var repeatProblem = await repeatResponse.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, repeatResponse.StatusCode);
        Assert.Equal("Stocktake has already been posted.", repeatProblem?.Title);
        Assert.Equal(12m, await StoredQuantityAsync(surplus.Id));
        Assert.Equal(2, await StoredAdjustmentCountAsync());

        var history = await GetDetailsAsync(client, draft.StocktakeId);

        Assert.Equal("Posted", history.Status);
        Assert.Equal(2, history.Adjustments.Count);
    }

    [Fact]
    public async Task Put_counts_returns_400_when_a_variance_has_no_reason()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        var draft = await CreateStocktakeAsync(client, item.Id);

        using var response = await PutCountsAsync(client, draft, (item.Id, 9m, null));

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("lines[0].reason", problem.Errors);
    }

    [Fact]
    public async Task Put_counts_returns_400_for_a_negative_or_imprecise_count()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        var other = await SeedItemAsync("Gasket", "GSK-001", quantity: 10m);
        var draft = await CreateStocktakeAsync(client, item.Id, other.Id);

        using var response = await PutCountsAsync(client, draft, (item.Id, -1m, "Bad"), (other.Id, 1.2345m, "Bad"));

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("lines[0].countedQuantity", problem.Errors);
        Assert.Contains("lines[1].countedQuantity", problem.Errors);
    }

    [Fact]
    public async Task Put_counts_returns_409_for_a_stale_concurrency_token()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        var draft = await CreateStocktakeAsync(client, item.Id);
        await CountAsync(client, draft, (item.Id, 10m, null));

        using var response = await PutCountsAsync(client, draft, (item.Id, 9m, "Recount"));

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Stocktake was modified concurrently.", problem?.Title);
    }

    [Fact]
    public async Task Post_stocktake_returns_409_for_a_count_below_reservations_and_changes_nothing()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        var workOrder = await SeedWorkOrderAsync();

        using (var reserveResponse = await client.PutAsJsonAsync(
            new Uri($"/api/maintenance/work-orders/{workOrder.Id}/part-reservations", UriKind.Relative),
            new { reservations = new[] { new { inventoryItemId = item.Id, quantity = 6m } } },
            CancellationToken.None))
        {
            reserveResponse.EnsureSuccessStatusCode();
        }

        var draft = await CreateStocktakeAsync(client, item.Id);
        await CountAsync(client, draft, (item.Id, 5m, "Missing"));
        var preview = await PreviewAsync(client, draft.StocktakeId);

        var blocker = Assert.Single(preview.Blockers);

        Assert.False(preview.CanPost);
        Assert.Equal(StocktakeBlockerCodes.BelowReserved, blocker.Code);
        Assert.Equal([workOrder.Id], blocker.ReservingWorkOrderIds);

        using var response = await PostStocktakeAsync(client, preview);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Stocktake has posting blockers.", problem?.Title);
        Assert.Equal(10m, await StoredQuantityAsync(item.Id));
        Assert.Equal(0, await StoredAdjustmentCountAsync());
    }

    [Fact]
    public async Task Post_stocktake_returns_409_after_usage_and_succeeds_after_refresh_and_recount()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        var workOrder = await SeedWorkOrderAsync();
        var draft = await CreateStocktakeAsync(client, item.Id);
        var counted = await CountAsync(client, draft, (item.Id, 8m, "Missing"));
        var stalePreview = await PreviewAsync(client, draft.StocktakeId);

        using (var usageResponse = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/work-orders/{workOrder.Id}/parts/{item.Id}/usage", UriKind.Relative),
            new { quantityUsed = 1m },
            CancellationToken.None))
        {
            usageResponse.EnsureSuccessStatusCode();
        }

        using var staleResponse = await PostStocktakeAsync(client, stalePreview);

        var staleProblem = await staleResponse.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("Stocktake posting preview is out of date.", staleProblem?.Title);
        Assert.Equal(9m, await StoredQuantityAsync(item.Id));

        using var refreshResponse = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/stocktakes/{draft.StocktakeId}/refresh", UriKind.Relative),
            new { expectedConcurrencyToken = counted.ConcurrencyToken, inventoryItemIds = new[] { item.Id } },
            CancellationToken.None);

        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<StocktakeDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.NotNull(refreshed);

        var refreshedLine = Assert.Single(refreshed.Lines);

        Assert.Equal(9m, refreshedLine.BaselineQuantity);
        Assert.Null(refreshedLine.CountedQuantity);

        var recounted = await CountAsync(client, refreshed, (item.Id, 7m, "Missing"));
        var preview = await PreviewAsync(client, recounted.StocktakeId);

        using var postResponse = await PostStocktakeAsync(client, preview);

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        Assert.Equal(7m, await StoredQuantityAsync(item.Id));
    }

    [Fact]
    public async Task Get_preview_never_changes_inventory()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        var draft = await CreateStocktakeAsync(client, item.Id);
        await CountAsync(client, draft, (item.Id, 1m, "Missing"));

        await PreviewAsync(client, draft.StocktakeId);
        await PreviewAsync(client, draft.StocktakeId);

        Assert.Equal(10m, await StoredQuantityAsync(item.Id));
        Assert.Equal(0, await StoredAdjustmentCountAsync());
    }

    [Fact]
    public async Task Post_cancel_requires_a_reason_and_makes_the_draft_immutable()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        var draft = await CreateStocktakeAsync(client, item.Id);

        using (var missingReasonResponse = await client.PostAsJsonAsync(
            CancelUri(draft.StocktakeId),
            new { expectedConcurrencyToken = draft.ConcurrencyToken, reason = " " },
            CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missingReasonResponse.StatusCode);
        }

        using var cancelResponse = await client.PostAsJsonAsync(
            CancelUri(draft.StocktakeId),
            new { expectedConcurrencyToken = draft.ConcurrencyToken, reason = "Counted the wrong locker" },
            CancellationToken.None);

        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<StocktakeDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.NotNull(cancelled);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Counted the wrong locker", cancelled.CancellationReason);

        using var editResponse = await PutCountsAsync(client, cancelled, (item.Id, 1m, "Late"));
        using var previewResponse = await client.GetAsync(PreviewUri(draft.StocktakeId), CancellationToken.None);

        var editProblem = await editResponse.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);
        Assert.Equal("Stocktake can no longer be changed.", editProblem?.Title);
        Assert.Equal(HttpStatusCode.Conflict, previewResponse.StatusCode);
        Assert.Equal(10m, await StoredQuantityAsync(item.Id));
    }

    [Fact]
    public async Task Get_filters_by_status_and_pages()
    {
        using var client = await CreateManagerClientAsync();
        var item = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        await CreateStocktakeAsync(client, item.Id);
        await CreateStocktakeAsync(client, item.Id);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/stocktakes?status=Draft&pageNumber=1&pageSize=1", UriKind.Relative),
            CancellationToken.None);

        var page = await response.Content.ReadFromJsonAsync<GetStocktakesResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(1, page.PageSize);
    }

    [Fact]
    public async Task Get_returns_400_for_an_oversized_page()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/stocktakes?pageSize=101", UriKind.Relative),
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetStocktakesValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_details_returns_404_for_an_unknown_stocktake()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/stocktakes/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Read_permission_can_view_and_preview_but_not_change()
    {
        using var manager = await CreateManagerClientAsync();
        using var reader = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var item = await SeedItemAsync("Impeller", "IMP-001", quantity: 10m);
        var draft = await CreateStocktakeAsync(manager, item.Id);
        await CountAsync(manager, draft, (item.Id, 10m, null));

        using var listResponse = await reader.GetAsync(StocktakesUri(), CancellationToken.None);

        var details = await GetDetailsAsync(reader, draft.StocktakeId);
        var preview = await PreviewAsync(reader, draft.StocktakeId);

        using var createResponse = await reader.PostAsJsonAsync(
            StocktakesUri(),
            new { inventoryItemIds = new[] { item.Id } },
            CancellationToken.None);
        using var countsResponse = await PutCountsAsync(reader, details, (item.Id, 9m, "Missing"));
        using var refreshResponse = await reader.PostAsJsonAsync(
            new Uri($"/api/maintenance/stocktakes/{draft.StocktakeId}/refresh", UriKind.Relative),
            new { expectedConcurrencyToken = details.ConcurrencyToken, inventoryItemIds = new[] { item.Id } },
            CancellationToken.None);
        using var postResponse = await PostStocktakeAsync(reader, preview);
        using var cancelResponse = await reader.PostAsJsonAsync(
            CancelUri(draft.StocktakeId),
            new { expectedConcurrencyToken = details.ConcurrencyToken, reason = "No" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, countsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refreshResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
        Assert.Equal(10m, await StoredQuantityAsync(item.Id));
    }

    [Fact]
    public async Task Get_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(StocktakesUri(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            StocktakesUri(),
            new { inventoryItemIds = new[] { Guid.NewGuid() } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_403_without_a_maintenance_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(StocktakesUri(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private Task<HttpClient> CreateManagerClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read,
            Permissions.Maintenance.Manage);
    }

    private static Uri StocktakesUri()
    {
        return new Uri("/api/maintenance/stocktakes", UriKind.Relative);
    }

    private static Uri PreviewUri(Guid stocktakeId)
    {
        return new Uri($"/api/maintenance/stocktakes/{stocktakeId}/preview", UriKind.Relative);
    }

    private static Uri CancelUri(Guid stocktakeId)
    {
        return new Uri($"/api/maintenance/stocktakes/{stocktakeId}/cancel", UriKind.Relative);
    }

    private static async Task<StocktakeDetails> CreateStocktakeAsync(HttpClient client, params Guid[] inventoryItemIds)
    {
        using var response = await client.PostAsJsonAsync(
            StocktakesUri(),
            new { inventoryItemIds },
            CancellationToken.None);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<StocktakeDetails>(CancellationToken.None))!;
    }

    private static Task<HttpResponseMessage> PutCountsAsync(
        HttpClient client,
        StocktakeDetails stocktake,
        params (Guid InventoryItemId, decimal? Counted, string? Reason)[] counts)
    {
        return client.PutAsJsonAsync(
            new Uri($"/api/maintenance/stocktakes/{stocktake.StocktakeId}/counts", UriKind.Relative),
            new
            {
                expectedConcurrencyToken = stocktake.ConcurrencyToken,
                lines = counts.Select(count => new
                {
                    inventoryItemId = count.InventoryItemId,
                    countedQuantity = count.Counted,
                    reason = count.Reason,
                }),
            },
            CancellationToken.None);
    }

    private static async Task<StocktakeDetails> CountAsync(
        HttpClient client,
        StocktakeDetails stocktake,
        params (Guid InventoryItemId, decimal? Counted, string? Reason)[] counts)
    {
        using var response = await PutCountsAsync(client, stocktake, counts);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<StocktakeDetails>(CancellationToken.None))!;
    }

    private static async Task<StocktakePostingPreview> PreviewAsync(HttpClient client, Guid stocktakeId)
    {
        using var response = await client.GetAsync(PreviewUri(stocktakeId), CancellationToken.None);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<StocktakePostingPreview>(CancellationToken.None))!;
    }

    private static Task<HttpResponseMessage> PostStocktakeAsync(HttpClient client, StocktakePostingPreview preview)
    {
        return client.PostAsJsonAsync(
            new Uri($"/api/maintenance/stocktakes/{preview.StocktakeId}/post", UriKind.Relative),
            new { expectedConcurrencyToken = preview.ConcurrencyToken, previewToken = preview.PreviewToken },
            CancellationToken.None);
    }

    private static async Task<StocktakeDetails> GetDetailsAsync(HttpClient client, Guid stocktakeId)
    {
        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/stocktakes/{stocktakeId}", UriKind.Relative),
            CancellationToken.None);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<StocktakeDetails>(CancellationToken.None))!;
    }

    private async Task<InventoryItem> SeedItemAsync(string name, string partNumber, decimal quantity)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            unit.Id,
            Guid.NewGuid(),
            name,
            partNumber,
            quantity,
            minimumQuantity: 2m,
            unitCost: 150m);

        await dbContext.Set<UnitOfMeasure>().AddAsync(unit, CancellationToken.None);
        await dbContext.Set<InventoryItem>().AddAsync(item, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return item;
    }

    private async Task<MaintenanceWorkOrder> SeedWorkOrderAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Overhaul cooling pump",
            null,
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7));

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return workOrder;
    }

    private async Task<decimal> StoredQuantityAsync(Guid inventoryItemId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => item.Id == inventoryItemId)
            .Select(item => item.Quantity)
            .SingleAsync(CancellationToken.None);
    }

    private async Task<int> StoredAdjustmentCountAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<InventoryAdjustment>().CountAsync(CancellationToken.None);
    }
}
