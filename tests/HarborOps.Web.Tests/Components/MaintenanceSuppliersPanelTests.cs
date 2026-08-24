using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class MaintenanceSuppliersPanelTests : BunitContext
{
    private const string SupplierId = "a0000000-0000-0000-0000-00000000000a";

    private readonly List<Uri> requestedUris = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, SuppliersPage(Supplier()));

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void Suppliers_are_listed_with_their_contact_details()
    {
        var panel = RenderPanel();

        var row = panel.Find(".suppliers-table tbody tr");

        Assert.Contains("Baltic Marine Parts", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Ana Ilic", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Net 30", row.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_optional_details_fall_back_to_a_placeholder()
    {
        respond = _ => Json(
            HttpStatusCode.OK,
            SuppliersPage(Supplier(contactPerson: "null", phoneEmail: "null", paymentTerms: "null")));

        var panel = RenderPanel();

        var cells = panel.Find(".suppliers-table tbody tr").QuerySelectorAll("td");

        Assert.Equal(MaintenanceFormat.MissingValue, cells[1].TextContent);
        Assert.Equal(MaintenanceFormat.MissingValue, cells[2].TextContent);
        Assert.Equal(MaintenanceFormat.MissingValue, cells[4].TextContent);
    }

    [Fact]
    public void Searching_sends_the_term_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find("#suppliers-search").Input("Baltic");

        panel.WaitForAssertion(() => Assert.Contains(
            "search=Baltic",
            requestedUris.Last().Query,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Changing_the_page_size_reloads_the_directory_from_the_first_page()
    {
        var panel = RenderPanel();

        panel.Find("#suppliers-page-size").Change("50");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last().Query;

            Assert.Contains("pageSize=50", query, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Resetting_clears_the_search_term()
    {
        var panel = RenderPanel();

        panel.Find("#suppliers-search").Input("Baltic");
        panel.WaitForAssertion(() => Assert.Contains("search=", requestedUris.Last().Query, StringComparison.Ordinal));

        panel.Find(".maintenance-reset").Click();

        panel.WaitForAssertion(() => Assert.DoesNotContain(
            "search=",
            requestedUris.Last().Query,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_register_button_is_hidden_without_the_manage_permission()
    {
        var panel = RenderPanel("maintenance.read");

        Assert.Empty(panel.FindAll(".suppliers-register-toggle"));
    }

    [Fact]
    public void Registering_a_supplier_posts_the_form_and_refreshes_the_list()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"supplierId":"{{SupplierId}}"}""")
            : Json(HttpStatusCode.OK, SuppliersPage(Supplier()));

        var panel = RenderPanel();

        panel.Find(".suppliers-register-toggle").Click();
        panel.Find("#supplier-name").Change("Baltic Marine Parts");
        panel.Find("#supplier-contact").Change("Ana Ilic");
        panel.Find("#supplier-payment-terms").Change("Net 30");
        panel.Find(".suppliers-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Supplier was registered successfully.",
                panel.Find(".suppliers-success").TextContent,
                StringComparison.Ordinal);

            var body = requestBodies.Last();

            Assert.Contains("\"name\":\"Baltic Marine Parts\"", body, StringComparison.Ordinal);
            Assert.Contains("\"contactPerson\":\"Ana Ilic\"", body, StringComparison.Ordinal);
            Assert.Contains("\"paymentTerms\":\"Net 30\"", body, StringComparison.Ordinal);

            Assert.Empty(panel.FindAll(".suppliers-form-card"));
            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == "/api/maintenance/suppliers") >= 3);
        });
    }

    [Fact]
    public void Registering_shows_the_validation_errors_returned_by_the_api()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.",
                "errors":{"name":["Required."]}}
                """)
            : Json(HttpStatusCode.OK, SuppliersPage(Supplier()));

        var panel = RenderPanel();

        panel.Find(".suppliers-register-toggle").Click();
        panel.Find(".suppliers-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var errors = panel.Find(".suppliers-field-errors");

            Assert.Contains("Please correct the following before saving", errors.TextContent, StringComparison.Ordinal);
            Assert.Contains("Required.", errors.TextContent, StringComparison.Ordinal);
            Assert.Equal("Name", panel.Find(".suppliers-field-errors .maintenance-error-field").TextContent);
        });
    }

    [Fact]
    public void Registering_shows_a_non_validation_failure_as_an_inline_error()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Forbidden,
                """{"title":"Maintenance access denied","detail":"Missing the manage permission."}""")
            : Json(HttpStatusCode.OK, SuppliersPage(Supplier()));

        var panel = RenderPanel();

        panel.Find(".suppliers-register-toggle").Click();
        panel.Find(".suppliers-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains(
            "Missing the manage permission.",
            panel.Find(".suppliers-inline-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_register_form_opens_as_a_modal_dialog()
    {
        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".maintenance-modal-backdrop"));

        panel.Find(".suppliers-register-toggle").Click();

        var dialog = panel.Find(".maintenance-modal-backdrop .suppliers-form-card");

        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));

        panel.Find(".maintenance-modal-backdrop").Click();

        Assert.Empty(panel.FindAll(".maintenance-modal-backdrop"));
    }

    [Fact]
    public void The_success_toast_disappears_on_its_own()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"supplierId":"{{SupplierId}}"}""")
            : Json(HttpStatusCode.OK, SuppliersPage(Supplier()));

        var panel = RenderPanel();

        panel.Find(".suppliers-register-toggle").Click();
        panel.Find("#supplier-name").Change("Baltic Marine Parts");
        panel.Find(".suppliers-form").Submit();

        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(".maintenance-toast")));

        panel.WaitForAssertion(
            () => Assert.Empty(panel.FindAll(".maintenance-toast")),
            TimeSpan.FromMilliseconds(SuppliersPanel.SuccessToastMilliseconds + 3000));
    }

    [Fact]
    public void An_empty_directory_shows_the_empty_state()
    {
        respond = _ => Json(HttpStatusCode.OK, SuppliersPage());

        var panel = RenderPanel();

        Assert.Contains("No suppliers match your search", panel.Find(".empty-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_shows_a_retryable_error()
    {
        respond = _ => Json(
            HttpStatusCode.Forbidden,
            """{"title":"Maintenance access denied","detail":"Missing permission."}""");

        var panel = RenderPanel();

        Assert.Contains(
            "Maintenance access denied",
            panel.Find(".api-error-state").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposing_the_panel_cancels_the_in_flight_request()
    {
        var started = new TaskCompletionSource();
        CancellationToken observed = default;

        respondWithToken = (_, token) =>
        {
            observed = token;
            started.TrySetResult();

            return new TaskCompletionSource<HttpResponseMessage>().Task;
        };

        RenderPanel();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private IRenderedComponent<SuppliersPanel> RenderPanel(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requestedUris.Add(request.RequestUri!);

            if (request.Content is not null)
            {
                requestBodies.Add(await request.Content.ReadAsStringAsync(token));
            }

            return respondWithToken is not null
                ? await respondWithToken(request, token)
                : await respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(httpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        var panel = Render<SuppliersPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return panel;
    }

    private static CurrentUserAuthorizationState CreateAuthorizationState(IEnumerable<string> permissions)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new CurrentUserAuthorizationState(new HttpContextAccessor { HttpContext = context });
    }

    private static string Supplier(
        string name = "Baltic Marine Parts",
        string contactPerson = "\"Ana Ilic\"",
        string phoneEmail = "\"sales@baltic.test\"",
        string address = "\"Pier 4\"",
        string paymentTerms = "\"Net 30\"")
    {
        return $$"""
            {"supplierId":"{{SupplierId}}","name":"{{name}}","contactPerson":{{contactPerson}},
            "phoneEmail":{{phoneEmail}},"address":{{address}},"paymentTerms":{{paymentTerms}}}
            """;
    }

    private static string SuppliersPage(params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":1,"pageSize":25,"totalCount":{{items.Length}}}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
