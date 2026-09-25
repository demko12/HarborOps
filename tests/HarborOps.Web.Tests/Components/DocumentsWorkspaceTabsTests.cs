using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DocumentsWorkspaceTabsTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";

    private readonly List<string> requestedPaths = [];
    private readonly List<Uri> requestedUris = [];

    [Fact]
    public void The_workspace_offers_expiring_documents_requirements_readiness_and_retention()
    {
        var page = RenderWorkspace();

        Assert.Equal(
            ["Expiring documents", "Requirements", "Readiness", "Retention & disposition"],
            page.FindAll("[role=tablist] .workspace-tab").Select(tab => tab.TextContent));
    }

    [Fact]
    public void Expiring_documents_is_the_default_tab()
    {
        var page = RenderWorkspace();

        Assert.NotNull(page.Find(".documents-tabpanel"));
        Assert.Empty(page.FindAll(".requirements-panel"));
        Assert.Empty(page.FindAll(".readiness-panel"));
        Assert.Equal("true", page.FindAll("[role=tablist] .workspace-tab")[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public void Retention_and_disposition_is_a_tab_rather_than_a_dialog()
    {
        var page = RenderWorkspace();

        page.FindAll("[role=tablist] .workspace-tab")[3].Click();

        page.WaitForAssertion(() => Assert.Contains(requestedPaths, path => path == "/api/documents/retention-queue"));
        Assert.Equal("tabpanel", page.Find(".documents-retention-card").GetAttribute("role"));
        Assert.Empty(page.FindAll(".documents-modal-backdrop"));
        Assert.Empty(page.FindAll(".documents-tabpanel"));
    }

    [Fact]
    public void A_readiness_deep_link_opens_the_matrix_for_the_linked_vessel()
    {
        var page = RenderWorkspace($"/documents?tab=readiness&vesselId={VesselId}");

        page.WaitForAssertion(() =>
        {
            Assert.Equal("true", page.FindAll("[role=tablist] .workspace-tab")[2].GetAttribute("aria-selected"));
            Assert.Contains(requestedUris, uri => uri.AbsolutePath == "/api/documents/readiness"
                && uri.Query.Contains($"vesselId={VesselId}", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Aurora Borealis", page.Find("#readiness-vessel").GetAttribute("value"));
        });
        Assert.Empty(page.FindAll(".documents-tabpanel"));
    }

    [Fact]
    public void A_requirements_deep_link_opens_the_configuration_tab()
    {
        var page = RenderWorkspace("/documents?tab=requirements");

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".requirements-panel")));
        Assert.Equal("true", page.FindAll("[role=tablist] .workspace-tab")[1].GetAttribute("aria-selected"));
    }

    [Fact]
    public void A_retention_deep_link_opens_the_retention_tab_and_loads_the_queue()
    {
        var page = RenderWorkspace("/documents?tab=retention");

        page.WaitForAssertion(() => Assert.Contains(requestedPaths, path => path == "/api/documents/retention-queue"));
        Assert.NotNull(page.Find(".documents-retention-card"));
    }

    [Fact]
    public void An_unknown_tab_in_the_link_falls_back_to_expiring_documents()
    {
        var page = RenderWorkspace("/documents?tab=unknown");

        Assert.NotNull(page.Find(".documents-tabpanel"));
        Assert.Equal("true", page.FindAll("[role=tablist] .workspace-tab")[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public void The_requirements_tab_shows_the_requirement_configuration()
    {
        var page = RenderWorkspace();

        page.FindAll("[role=tablist] .workspace-tab")[1].Click();

        page.WaitForAssertion(() => Assert.Contains("Safety Management Certificate", page.Find(".requirements-panel").TextContent, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(".documents-tabpanel"));
        Assert.Contains(requestedPaths, path => path == "/api/documents/requirements");
    }

    [Fact]
    public void The_readiness_tab_shows_the_readiness_matrix()
    {
        var page = RenderWorkspace();

        page.FindAll("[role=tablist] .workspace-tab")[2].Click();

        page.WaitForAssertion(() => Assert.Contains("Missing", page.Find(".readiness-panel").TextContent, StringComparison.Ordinal));
        Assert.Contains(requestedPaths, path => path == "/api/documents/readiness");
    }

    [Fact]
    public void Registering_a_missing_type_opens_the_register_form_with_the_vessel_and_type_prefilled()
    {
        var page = RenderWorkspace();
        page.FindAll("[role=tablist] .workspace-tab")[2].Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".readiness-documents .readiness-row-action")));

        page.Find(".readiness-documents .readiness-row-action").Click();

        page.WaitForAssertion(() => Assert.Equal("7", page.Find("#doc-type").GetAttribute("value")));
        Assert.Equal("Aurora Borealis", page.Find("#documents-form-title").ParentElement!.ParentElement!.QuerySelector(".vessel-picker-input")!.GetAttribute("value"));
        Assert.Contains("Register document", page.Find("#documents-form-title").TextContent, StringComparison.Ordinal);
    }

    private IRenderedComponent<Documents> RenderWorkspace(string? address = null)
    {
        var documentsHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requestedPaths.Add(path);
            requestedUris.Add(request.RequestUri);

            return path switch
            {
                "/api/documents/expiring" => Json(ExpiringPage(
                    request.RequestUri.Query.EndsWith("pageSize=1", StringComparison.Ordinal) ? 3 : 12)),
                "/api/documents/requirements" => Json(RequirementsPage),
                "/api/documents/readiness" => Json(ReadinessPage),
                "/api/documents/types" => Json(TypesPage),
                _ => Json("""{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0}"""),
            };
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            Json($$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new DocumentsApiClient(documentsHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState("documents.read", "documents.manage"));

        if (address is not null)
        {
            Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        }

        var page = Render<Documents>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedPaths));

        return page;
    }

    private static string ExpiringPage(int totalCount) =>
        $$"""{"items":[],"pageNumber":1,"pageSize":25,"totalCount":{{totalCount}}}""";

    private static string TypesPage =>
        """{"items":[{"documentTypeId":7,"name":"Safety Management Certificate","mandatoryValidityPeriodDays":365,"issuingAuthority":"Flag","requiresSurvey":true}],"pageSize":100,"totalCount":1}""";

    private static string RequirementsPage =>
        $$"""
        {"items":[{"id":"11111111-1111-1111-1111-111111111111","vesselId":"{{VesselId}}","documentTypeId":7,
        "documentTypeName":"Safety Management Certificate","isEnabled":true,"minimumRemainingValidityDays":30,
        "instruction":null,"revision":1,"updatedByUserId":"22222222-2222-2222-2222-222222222222",
        "updatedAtUtc":"2026-09-24T09:00:00Z","version":"33333333-3333-3333-3333-333333333333"}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private static string ReadinessPage =>
        $$"""
        {"vesselId":null,"vesselStatus":null,"today":"2026-09-24","requiredThrough":"2026-09-24",
        "summary":{"configuredCount":1,"satisfiedCount":0,"unsatisfiedCount":1},
        "items":[{"requirementId":"11111111-1111-1111-1111-111111111111","vesselId":"{{VesselId}}","documentTypeId":7,
        "documentTypeName":"Safety Management Certificate","minimumRemainingValidityDays":0,"instruction":null,
        "requiredValidUntil":"2026-09-24","status":"Unsatisfied","blockers":["Missing"],"selectedDocumentId":null,"candidates":[]}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private static CurrentUserAuthorizationState CreateAuthorizationState(params string[] permissions)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new CurrentUserAuthorizationState(new HttpContextAccessor { HttpContext = context });
    }

    private static Task<HttpResponseMessage> Json(string json)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request);
        }
    }
}
