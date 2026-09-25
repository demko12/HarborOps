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

public sealed class SupplierReturnEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public SupplierReturnEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_201_with_line_allowances_for_the_order_supplier()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 12m, quantityOrdered: 10m);

        using var response = await client.PostAsJsonAsync(
            SupplierReturnsUri(),
            new
            {
                purchaseOrderId = seed.PurchaseOrderId,
                notes = "RMA 1042",
                lines = new[] { new { purchaseOrderLineId = seed.LineId, quantity = 2.5m, reason = "Wrong size" } },
            },
            CancellationToken.None);

        var details = await response.Content.ReadFromJsonAsync<SupplierReturnDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(
            $"/api/maintenance/supplier-returns/{details.SupplierReturnId}",
            response.Headers.Location?.OriginalString);
        Assert.Equal("Draft", details.Status);
        Assert.Equal(seed.SupplierId, details.SupplierId);
        Assert.Equal("Baltic Marine Parts", details.SupplierName);
        Assert.NotEqual(Guid.Empty, details.CreatedByUserId);

        var line = Assert.Single(details.Lines);

        Assert.Equal(10m, line.ReceivedQuantity);
        Assert.Equal(0m, line.PreviouslyReturnedQuantity);
        Assert.Equal(10m, line.RemainingReturnableQuantity);
        Assert.Equal(12m, line.OnHandQuantity);
        Assert.Equal(0m, line.ReservedQuantity);
        Assert.Equal("pcs", line.UnitAbbreviation);
        Assert.Equal(12m, await StoredQuantityAsync(seed.ItemId));
    }

    [Fact]
    public async Task Post_returns_400_for_duplicate_zero_and_imprecise_lines()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 10m);

        using var response = await client.PostAsJsonAsync(
            SupplierReturnsUri(),
            new
            {
                purchaseOrderId = seed.PurchaseOrderId,
                lines = new object[]
                {
                    new { purchaseOrderLineId = seed.LineId, quantity = 0m, reason = "Damaged" },
                    new { purchaseOrderLineId = seed.LineId, quantity = 1.0005m, reason = "" },
                },
            },
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("lines[0].quantity", problem.Errors.Keys);
        Assert.Contains("lines[1].purchaseOrderLineId", problem.Errors.Keys);
        Assert.Contains("lines[1].quantity", problem.Errors.Keys);
        Assert.Contains("lines[1].reason", problem.Errors.Keys);
        Assert.Equal(0, await StoredReturnCountAsync());
    }

    [Fact]
    public async Task Post_returns_400_for_a_line_from_another_order_or_above_the_received_quantity()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 50m, quantityOrdered: 5m);
        var other = await SeedReceivedOrderAsync(quantityOnHand: 50m, quantityOrdered: 5m);

        using var response = await client.PostAsJsonAsync(
            SupplierReturnsUri(),
            new
            {
                purchaseOrderId = seed.PurchaseOrderId,
                lines = new[]
                {
                    new { purchaseOrderLineId = seed.LineId, quantity = 5.001m, reason = "Damaged" },
                    new { purchaseOrderLineId = other.LineId, quantity = 1m, reason = "Damaged" },
                },
            },
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("lines[0].quantity", problem.Errors.Keys);
        Assert.Contains("lines[1].purchaseOrderLineId", problem.Errors.Keys);
    }

    [Fact]
    public async Task Post_returns_409_for_an_order_that_was_not_received()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m, PurchaseOrderStatus.Ordered);

        using var response = await CreateReturnAsync(client, seed, 1m);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Purchase order has not been received.", problem?.Title);
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_order()
    {
        using var client = await CreateManagerClientAsync();

        using var response = await client.PostAsJsonAsync(
            SupplierReturnsUri(),
            new
            {
                purchaseOrderId = Guid.NewGuid(),
                lines = new[] { new { purchaseOrderLineId = Guid.NewGuid(), quantity = 1m, reason = "Damaged" } },
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_partial_return_posts_once_and_the_retry_returns_the_same_result()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 8m);
        var draft = await CreateDraftAsync(client, seed, 3.125m);
        var preview = await PreviewAsync(client, draft.SupplierReturnId);

        Assert.True(preview.CanPost);
        Assert.Equal(6.875m, Assert.Single(preview.Items).QuantityAfter);
        Assert.Equal(10m, await StoredQuantityAsync(seed.ItemId));

        using var postResponse = await PostReturnAsync(client, preview);

        var posted = await postResponse.Content.ReadFromJsonAsync<PostSupplierReturnResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        Assert.NotNull(posted);
        Assert.Equal("Posted", posted.Status);
        Assert.False(posted.AlreadyPosted);

        var movement = Assert.Single(posted.Movements);

        Assert.Equal(10m, movement.QuantityBefore);
        Assert.Equal(6.875m, movement.QuantityAfter);
        Assert.Equal(-3.125m, movement.QuantityDelta);
        Assert.Equal(draft.SupplierReturnId, movement.SupplierReturnId);
        Assert.Equal(Assert.Single(draft.Lines).SupplierReturnLineId, movement.SupplierReturnLineId);
        Assert.Equal(draft.CreatedByUserId, movement.ActorUserId);
        Assert.Equal(6.875m, await StoredQuantityAsync(seed.ItemId));

        using var retryResponse = await PostReturnAsync(client, preview);

        var retry = await retryResponse.Content.ReadFromJsonAsync<PostSupplierReturnResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.NotNull(retry);
        Assert.True(retry.AlreadyPosted);
        Assert.Equal(movement.MovementId, Assert.Single(retry.Movements).MovementId);
        Assert.Equal(6.875m, await StoredQuantityAsync(seed.ItemId));
        Assert.Equal(1, await StoredMovementCountAsync());

        var history = await GetDetailsAsync(client, draft.SupplierReturnId);

        Assert.Equal("Posted", history.Status);
        Assert.Equal("Received", history.PurchaseOrderStatus);
        Assert.Empty(history.Blockers);
        Assert.Equal(4.875m, Assert.Single(history.Lines).RemainingReturnableQuantity);
        Assert.Single(history.Movements);
        Assert.Equal(8m, await StoredOrderedQuantityAsync(seed.LineId));
    }

    [Fact]
    public async Task A_posted_return_is_immutable()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 8m);
        var draft = await CreateDraftAsync(client, seed, 1m);
        using (var postResponse = await PostReturnAsync(client, await PreviewAsync(client, draft.SupplierReturnId)))
        {
            postResponse.EnsureSuccessStatusCode();
        }

        var posted = await GetDetailsAsync(client, draft.SupplierReturnId);

        using var updateResponse = await UpdateAsync(client, posted, seed.LineId, 2m);
        using var cancelResponse = await CancelAsync(client, posted, "Changed my mind");
        using var previewResponse = await client.GetAsync(PreviewUri(posted.SupplierReturnId), CancellationToken.None);

        var problem = await updateResponse.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
        Assert.Equal("Supplier return can no longer be changed.", problem?.Title);
        Assert.Equal(HttpStatusCode.Conflict, cancelResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, previewResponse.StatusCode);
        Assert.Equal(9m, await StoredQuantityAsync(seed.ItemId));
    }

    [Fact]
    public async Task Post_return_returns_409_for_reserved_stock_and_changes_nothing()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 10m);
        var workOrder = await SeedWorkOrderAsync();
        await ReserveAsync(client, workOrder.Id, seed.ItemId, 8m);
        var draft = await CreateDraftAsync(client, seed, 3m);
        var preview = await PreviewAsync(client, draft.SupplierReturnId);

        var blocker = Assert.Single(preview.Blockers);

        Assert.False(preview.CanPost);
        Assert.Equal(SupplierReturnBlockerCodes.ReservedStock, blocker.Code);
        Assert.Equal([workOrder.Id], blocker.ReservingWorkOrderIds);
        Assert.Equal(2m, Assert.Single(preview.Items).AvailableQuantity);

        using var response = await PostReturnAsync(client, preview);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Supplier return has posting blockers.", problem?.Title);
        Assert.Equal(10m, await StoredQuantityAsync(seed.ItemId));
        Assert.Equal(0, await StoredMovementCountAsync());
        Assert.Equal(8m, await OutstandingReservedAsync(seed.ItemId));

        var details = await GetDetailsAsync(client, draft.SupplierReturnId);

        Assert.Equal("Draft", details.Status);
        Assert.Equal(SupplierReturnBlockerCodes.ReservedStock, Assert.Single(details.Blockers).Code);
    }

    [Fact]
    public async Task Post_return_returns_409_after_part_usage_and_succeeds_with_a_new_preview()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 10m);
        var workOrder = await SeedWorkOrderAsync();
        var draft = await CreateDraftAsync(client, seed, 2m);
        var stalePreview = await PreviewAsync(client, draft.SupplierReturnId);

        using (var usageResponse = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/work-orders/{workOrder.Id}/parts/{seed.ItemId}/usage", UriKind.Relative),
            new { quantityUsed = 1m },
            CancellationToken.None))
        {
            usageResponse.EnsureSuccessStatusCode();
        }

        using var staleResponse = await PostReturnAsync(client, stalePreview);

        var staleProblem = await staleResponse.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("Supplier return posting preview is out of date.", staleProblem?.Title);
        Assert.Equal(9m, await StoredQuantityAsync(seed.ItemId));

        using var postResponse = await PostReturnAsync(client, await PreviewAsync(client, draft.SupplierReturnId));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        Assert.Equal(7m, await StoredQuantityAsync(seed.ItemId));
    }

    [Fact]
    public async Task Cumulative_returns_cannot_exceed_the_received_quantity()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 20m, quantityOrdered: 5m);
        var first = await CreateDraftAsync(client, seed, 3m);
        var second = await CreateDraftAsync(client, seed, 3m);
        var secondStalePreview = await PreviewAsync(client, second.SupplierReturnId);

        using (var firstPost = await PostReturnAsync(client, await PreviewAsync(client, first.SupplierReturnId)))
        {
            firstPost.EnsureSuccessStatusCode();
        }

        using var staleResponse = await PostReturnAsync(client, secondStalePreview);
        var blockedPreview = await PreviewAsync(client, second.SupplierReturnId);
        using var blockedResponse = await PostReturnAsync(client, blockedPreview);

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(SupplierReturnBlockerCodes.ExceedsReturnable, Assert.Single(blockedPreview.Blockers).Code);
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);

        // Reducing the draft to what is still returnable lets it post.
        var current = await GetDetailsAsync(client, second.SupplierReturnId);
        using (var updateResponse = await UpdateAsync(client, current, seed.LineId, 2m))
        {
            updateResponse.EnsureSuccessStatusCode();
        }

        using var finalResponse = await PostReturnAsync(client, await PreviewAsync(client, second.SupplierReturnId));

        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        Assert.Equal(15m, await StoredQuantityAsync(seed.ItemId));
        Assert.Equal(0m, Assert.Single((await GetDetailsAsync(client, second.SupplierReturnId)).Lines)
            .RemainingReturnableQuantity);
    }

    [Fact]
    public async Task Put_returns_409_for_a_stale_concurrency_token()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m);
        var draft = await CreateDraftAsync(client, seed, 1m);

        using (var firstUpdate = await UpdateAsync(client, draft, seed.LineId, 2m))
        {
            firstUpdate.EnsureSuccessStatusCode();
        }

        using var staleUpdate = await UpdateAsync(client, draft, seed.LineId, 3m);

        var problem = await staleUpdate.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        Assert.Equal("Supplier return was modified concurrently.", problem?.Title);
        Assert.Equal(2m, Assert.Single((await GetDetailsAsync(client, draft.SupplierReturnId)).Lines).Quantity);
    }

    [Fact]
    public async Task Post_cancel_requires_a_reason_and_releases_nothing()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m);
        var draft = await CreateDraftAsync(client, seed, 5m);

        using var missingReason = await CancelAsync(client, draft, " ");
        using var cancelResponse = await CancelAsync(client, draft, "Supplier issued a credit instead");

        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<SupplierReturnDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.NotNull(cancelled);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Supplier issued a credit instead", cancelled.CancellationReason);
        Assert.Equal(10m, await StoredQuantityAsync(seed.ItemId));

        // A cancelled return does not consume the allowance, so the full quantity is still returnable.
        var replacement = await CreateDraftAsync(client, seed, 5m);

        Assert.Equal(5m, Assert.Single(replacement.Lines).RemainingReturnableQuantity);
    }

    [Fact]
    public async Task Get_filters_by_purchase_order_and_status_and_pages()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 50m, quantityOrdered: 20m);
        var other = await SeedReceivedOrderAsync(quantityOnHand: 50m, quantityOrdered: 20m);
        await CreateDraftAsync(client, seed, 1m);
        await CreateDraftAsync(client, seed, 1m);
        var posted = await CreateDraftAsync(client, seed, 1m);
        await CreateDraftAsync(client, other, 1m);
        using (var postResponse = await PostReturnAsync(client, await PreviewAsync(client, posted.SupplierReturnId)))
        {
            postResponse.EnsureSuccessStatusCode();
        }

        var byOrder = await GetListAsync(client, $"?purchaseOrderId={seed.PurchaseOrderId}&pageSize=2&pageNumber=1");
        var postedOnly = await GetListAsync(client, $"?purchaseOrderId={seed.PurchaseOrderId}&status=Posted");

        Assert.Equal(3, byOrder.TotalCount);
        Assert.Equal(2, byOrder.Items.Count);
        Assert.All(byOrder.Items, row => Assert.Equal(seed.PurchaseOrderId, row.PurchaseOrderId));
        Assert.Equal([posted.SupplierReturnId], postedOnly.Items.Select(row => row.SupplierReturnId));
    }

    [Fact]
    public async Task Get_returns_400_for_an_unknown_status_or_oversized_page()
    {
        using var client = await CreateManagerClientAsync();

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/supplier-returns?status=Archived&pageSize=101", UriKind.Relative),
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetSupplierReturnsValidator.StatusField, problem.Errors.Keys);
        Assert.Contains(GetSupplierReturnsValidator.PageSizeField, problem.Errors.Keys);
    }

    [Fact]
    public async Task Get_details_returns_404_for_an_unknown_return()
    {
        using var client = await CreateManagerClientAsync();

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/supplier-returns/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Read_permission_can_view_and_preview_but_not_change()
    {
        using var manager = await CreateManagerClientAsync();
        var seed = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m);
        var draft = await CreateDraftAsync(manager, seed, 1m);
        var preview = await PreviewAsync(manager, draft.SupplierReturnId);

        using var reader = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var listResponse = await reader.GetAsync(SupplierReturnsUri(), CancellationToken.None);
        using var detailsResponse = await reader.GetAsync(
            new Uri($"/api/maintenance/supplier-returns/{draft.SupplierReturnId}", UriKind.Relative),
            CancellationToken.None);
        using var previewResponse = await reader.GetAsync(PreviewUri(draft.SupplierReturnId), CancellationToken.None);
        using var createResponse = await CreateReturnAsync(reader, seed, 1m);
        using var updateResponse = await UpdateAsync(reader, draft, seed.LineId, 2m);
        using var postResponse = await PostReturnAsync(reader, preview);
        using var cancelResponse = await CancelAsync(reader, draft, "Duplicate");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
        Assert.Equal(10m, await StoredQuantityAsync(seed.ItemId));
    }

    [Fact]
    public async Task Get_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(SupplierReturnsUri(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            SupplierReturnsUri(),
            new { purchaseOrderId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_403_without_a_maintenance_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(SupplierReturnsUri(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private Task<HttpClient> CreateManagerClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read,
            Permissions.Maintenance.Manage);
    }

    private static Uri SupplierReturnsUri()
    {
        return new Uri("/api/maintenance/supplier-returns", UriKind.Relative);
    }

    private static Uri PreviewUri(Guid supplierReturnId)
    {
        return new Uri($"/api/maintenance/supplier-returns/{supplierReturnId}/preview", UriKind.Relative);
    }

    private static Task<HttpResponseMessage> CreateReturnAsync(HttpClient client, OrderSeed seed, decimal quantity)
    {
        return client.PostAsJsonAsync(
            SupplierReturnsUri(),
            new
            {
                purchaseOrderId = seed.PurchaseOrderId,
                lines = new[] { new { purchaseOrderLineId = seed.LineId, quantity, reason = "Damaged on arrival" } },
            },
            CancellationToken.None);
    }

    private static async Task<SupplierReturnDetails> CreateDraftAsync(HttpClient client, OrderSeed seed, decimal quantity)
    {
        using var response = await CreateReturnAsync(client, seed, quantity);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SupplierReturnDetails>(CancellationToken.None))!;
    }

    private static Task<HttpResponseMessage> UpdateAsync(
        HttpClient client,
        SupplierReturnDetails supplierReturn,
        Guid purchaseOrderLineId,
        decimal quantity)
    {
        return client.PutAsJsonAsync(
            new Uri($"/api/maintenance/supplier-returns/{supplierReturn.SupplierReturnId}", UriKind.Relative),
            new
            {
                expectedConcurrencyToken = supplierReturn.ConcurrencyToken,
                lines = new[] { new { purchaseOrderLineId, quantity, reason = "Damaged on arrival" } },
            },
            CancellationToken.None);
    }

    private static Task<HttpResponseMessage> CancelAsync(
        HttpClient client,
        SupplierReturnDetails supplierReturn,
        string reason)
    {
        return client.PostAsJsonAsync(
            new Uri($"/api/maintenance/supplier-returns/{supplierReturn.SupplierReturnId}/cancel", UriKind.Relative),
            new { expectedConcurrencyToken = supplierReturn.ConcurrencyToken, reason },
            CancellationToken.None);
    }

    private static async Task<SupplierReturnPostingPreview> PreviewAsync(HttpClient client, Guid supplierReturnId)
    {
        using var response = await client.GetAsync(PreviewUri(supplierReturnId), CancellationToken.None);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SupplierReturnPostingPreview>(CancellationToken.None))!;
    }

    private static Task<HttpResponseMessage> PostReturnAsync(HttpClient client, SupplierReturnPostingPreview preview)
    {
        return client.PostAsJsonAsync(
            new Uri($"/api/maintenance/supplier-returns/{preview.SupplierReturnId}/post", UriKind.Relative),
            new { expectedConcurrencyToken = preview.ConcurrencyToken, previewToken = preview.PreviewToken },
            CancellationToken.None);
    }

    private static async Task<SupplierReturnDetails> GetDetailsAsync(HttpClient client, Guid supplierReturnId)
    {
        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/supplier-returns/{supplierReturnId}", UriKind.Relative),
            CancellationToken.None);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SupplierReturnDetails>(CancellationToken.None))!;
    }

    private static async Task<GetSupplierReturnsResponse> GetListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/supplier-returns{query}", UriKind.Relative),
            CancellationToken.None);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<GetSupplierReturnsResponse>(CancellationToken.None))!;
    }

    private static async Task ReserveAsync(HttpClient client, Guid workOrderId, Guid inventoryItemId, decimal quantity)
    {
        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/maintenance/work-orders/{workOrderId}/part-reservations", UriKind.Relative),
            new { reservations = new[] { new { inventoryItemId, quantity } } },
            CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    private Task<OrderSeed> SeedReceivedOrderAsync(decimal quantityOnHand, decimal quantityOrdered)
    {
        return SeedOrderAsync(quantityOnHand, quantityOrdered, PurchaseOrderStatus.Received);
    }

    private async Task<OrderSeed> SeedOrderAsync(
        decimal quantityOnHand,
        decimal quantityOrdered,
        PurchaseOrderStatus status)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var supplier = Supplier.Register("Baltic Marine Parts", null, null, null, null);
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            unit.Id,
            supplier.Id,
            "Cooling pump impeller",
            $"IMP-{Guid.NewGuid():N}",
            quantityOnHand,
            minimumQuantity: 2m,
            unitCost: 150m);
        var order = PurchaseOrder.Create(supplier.Id, new DateOnly(2026, 9, 1), null);
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);

        if (status == PurchaseOrderStatus.Received)
        {
            order.ChangeStatusTo(PurchaseOrderStatus.Received);
        }

        var line = PurchaseOrderLine.Create(order.Id, item.Id, quantityOrdered, 150m);

        await dbContext.AddRangeAsync(supplier, unit, item, order, line);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return new OrderSeed(supplier.Id, item.Id, order.Id, line.Id);
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

    private async Task<decimal> StoredOrderedQuantityAsync(Guid purchaseOrderLineId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.Id == purchaseOrderLineId)
            .Select(line => line.QuantityOrdered)
            .SingleAsync(CancellationToken.None);
    }

    private async Task<decimal> OutstandingReservedAsync(Guid inventoryItemId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .Where(reservation => reservation.InventoryItemId == inventoryItemId)
            .SumAsync(reservation => reservation.QuantityReserved - reservation.QuantityConsumed, CancellationToken.None);
    }

    private async Task<int> StoredReturnCountAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<SupplierReturn>().CountAsync(CancellationToken.None);
    }

    private async Task<int> StoredMovementCountAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<InventoryAdjustment>()
            .CountAsync(adjustment => adjustment.SupplierReturnId != null, CancellationToken.None);
    }

    private sealed record OrderSeed(Guid SupplierId, Guid ItemId, Guid PurchaseOrderId, Guid LineId);
}
