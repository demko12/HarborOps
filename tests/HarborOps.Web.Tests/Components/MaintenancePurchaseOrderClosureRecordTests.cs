using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class MaintenancePurchaseOrderClosureRecordTests : BunitContext
{
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string ActorUserId = "a2b3c4d5-0000-0000-0000-00000000002a";
    private const string ClosurePath = $"/api/maintenance/purchase-orders/{OrderId}/closure";

    private readonly List<Uri> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = _ => Json(HttpStatusCode.OK, Closure());

    [Fact]
    public void The_closure_shows_when_by_whom_and_why_the_order_was_closed()
    {
        var record = RenderRecord();

        var summary = record.Find(".closure-record .procurement-details-summary");
        var actor = summary.QuerySelectorAll("dd")[1];

        Assert.Contains("2026-10-01 08:00", summary.TextContent, StringComparison.Ordinal);
        Assert.Equal("a2b3c4d5", actor.TextContent.Trim());
        Assert.Equal(ActorUserId, actor.GetAttribute("title"));
        Assert.Equal("Supplier discontinued the part", record.Find(".closure-record-reason").TextContent.Trim());
        Assert.Contains("6 unit(s) received", summary.TextContent, StringComparison.Ordinal);
        Assert.Contains("6 unit(s) closed without delivery", summary.TextContent, StringComparison.Ordinal);
        Assert.Equal(ClosurePath, requests.Single().AbsolutePath);
    }

    [Fact]
    public void Each_line_shows_the_quantities_recorded_at_closing()
    {
        var record = RenderRecord();

        var rows = record.FindAll(".closure-record tbody tr");
        var filterCells = rows[0].QuerySelectorAll("td.numeric").Select(cell => cell.TextContent.Trim());

        Assert.Equal(2, rows.Count);
        Assert.Contains("PN-1", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Equal(["10", "4", "1", "6 pcs"], filterCells);
        Assert.Contains("0 pcs", rows[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        var attempts = 0;
        respond = _ => ++attempts == 1
            ? Json(HttpStatusCode.ServiceUnavailable, """{"title":"Service unavailable."}""")
            : Json(HttpStatusCode.OK, Closure());

        var record = RenderRecord(waitForRecord: false);

        record.WaitForAssertion(() => Assert.NotEmpty(record.FindAll(".api-error-state")));

        record.Find(".api-error-state button").Click();

        record.WaitForAssertion(() => Assert.Equal(2, record.FindAll(".closure-record tbody tr").Count));
    }

    [Fact]
    public void The_same_order_does_not_reload()
    {
        var record = RenderRecord();

        record.Render(parameters => parameters.Add(component => component.PurchaseOrderId, Guid.Parse(OrderId)));

        Assert.Single(requests);
    }

    private IRenderedComponent<PurchaseOrderClosureRecord> RenderRecord(bool waitForRecord = true)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(httpClient));

        var record = Render<PurchaseOrderClosureRecord>(parameters => parameters
            .Add(component => component.PurchaseOrderId, Guid.Parse(OrderId)));

        if (waitForRecord)
        {
            record.WaitForAssertion(() => Assert.Empty(record.FindAll(".loading-state")));
        }

        return record;
    }

    private static string Closure()
    {
        return $$"""
            {"closureId":"d0000000-0000-0000-0000-00000000000d","purchaseOrderId":"{{OrderId}}",
            "clientRequestId":"a1000000-0000-0000-0000-00000000001a","reason":"Supplier discontinued the part",
            "closedByUserId":"{{ActorUserId}}","closedUtc":"2026-10-01T08:00:00Z",
            "orderStatusBefore":"PartiallyReceived","orderStatusAfter":"ClosedShort",
            "totalQuantityReceived":6,"totalQuantityClosed":6,
            "lines":[
            {"closureLineId":"e1000000-0000-0000-0000-00000000001e","purchaseOrderLineId":"f0000000-0000-0000-0000-00000000000f",
            "inventoryItemId":"c0000000-0000-0000-0000-00000000000c","name":"Fuel filter","partNumber":"PN-1",
            "unitAbbreviation":"pcs","quantityOrdered":10,"receivedQuantity":4,"returnedQuantity":1,"closedQuantity":6},
            {"closureLineId":"e2000000-0000-0000-0000-00000000002e","purchaseOrderLineId":"f1000000-0000-0000-0000-00000000001f",
            "inventoryItemId":"e0000000-0000-0000-0000-00000000000e","name":"Impeller","partNumber":"PN-2",
            "unitAbbreviation":"pcs","quantityOrdered":2,"receivedQuantity":2,"returnedQuantity":0,"closedQuantity":0}]}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }
}
