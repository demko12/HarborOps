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
    private const string DocumentId = "10000000-0000-0000-0000-000000000001";

    private readonly List<string> requestedPaths = [];
    private readonly List<Uri> requestedUris = [];

    public DocumentsWorkspaceTabsTests()
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
                $"/api/documents/{DocumentId}" => Json(DocumentDetails),
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
    }

    [Fact]
    public void The_workspace_offers_expiring_documents_requirements_readiness_and_retention()
    {
        var page = RenderWorkspace<Documents>("/documents");

        Assert.Equal(
            ["Expiring documents", "Requirements", "Readiness", "Retention & disposition"],
            page.FindAll(".documents-tabs .workspace-tab").Select(tab => tab.TextContent));
        Assert.Equal(
            ["/documents", "/documents/requirements", "/documents/readiness", "/documents/retention"],
            page.FindAll(".documents-tabs .workspace-tab").Select(tab => tab.GetAttribute("href")));
    }

    [Fact]
    public void The_documents_page_shows_only_the_expiring_documents()
    {
        var page = RenderWorkspace<Documents>("/documents");

        Assert.NotNull(page.Find(".documents-register"));
        Assert.Empty(page.FindAll(".requirements-panel"));
        Assert.Empty(page.FindAll(".readiness-panel"));
        Assert.Empty(page.FindAll(".documents-retention-card"));
        Assert.Equal("Expiring documents", page.Find(".documents-tabs .workspace-tab.active").TextContent);
    }

    [Fact]
    public void The_retention_page_loads_the_queue_in_a_card_rather_than_a_dialog()
    {
        var page = RenderWorkspace<DocumentsRetention>("/documents/retention");

        page.WaitForAssertion(() => Assert.Contains(requestedPaths, path => path == "/api/documents/retention-queue"));
        Assert.NotNull(page.Find(".documents-retention-card"));
        Assert.Empty(page.FindAll(".documents-modal-backdrop"));
        Assert.Empty(page.FindAll(".documents-register"));
        Assert.Equal("Retention & disposition", page.Find(".documents-tabs .workspace-tab.active").TextContent);
    }

    [Fact]
    public void A_readiness_link_opens_the_matrix_for_the_linked_vessel()
    {
        var page = RenderWorkspace<DocumentsReadiness>($"/documents/readiness?vesselId={VesselId}");

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedUris, uri => uri.AbsolutePath == "/api/documents/readiness"
                && uri.Query.Contains($"vesselId={VesselId}", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Aurora Borealis", page.Find("#readiness-vessel").GetAttribute("value"));
        });
        Assert.Equal("Readiness", page.Find(".documents-tabs .workspace-tab.active").TextContent);
        Assert.Empty(page.FindAll(".documents-register"));
    }

    [Fact]
    public void The_requirements_page_shows_the_requirement_configuration()
    {
        var page = RenderWorkspace<DocumentsRequirements>("/documents/requirements");

        page.WaitForAssertion(() => Assert.Contains("Safety Management Certificate", page.Find(".requirements-panel").TextContent, StringComparison.Ordinal));
        Assert.Equal("Requirements", page.Find(".documents-tabs .workspace-tab.active").TextContent);
        Assert.Empty(page.FindAll(".documents-register"));
        Assert.Contains(requestedPaths, path => path == "/api/documents/requirements");
    }

    [Fact]
    public void The_readiness_page_shows_the_readiness_matrix()
    {
        var page = RenderWorkspace<DocumentsReadiness>("/documents/readiness");

        page.WaitForAssertion(() => Assert.Contains("Missing", page.Find(".readiness-panel").TextContent, StringComparison.Ordinal));
        Assert.Contains(requestedPaths, path => path == "/api/documents/readiness");
    }

    [Fact]
    public void Registering_a_missing_type_opens_the_register_form_with_the_vessel_and_type_prefilled()
    {
        var page = RenderWorkspace<DocumentsReadiness>("/documents/readiness");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".readiness-documents .readiness-row-action")));

        page.Find(".readiness-documents .readiness-row-action").Click();

        page.WaitForAssertion(() => Assert.Equal("7", page.Find("#doc-type").GetAttribute("value")));
        Assert.Equal("Aurora Borealis", page.Find("#documents-form-title").ParentElement!.ParentElement!.QuerySelector(".vessel-picker-input")!.GetAttribute("value"));
        Assert.Contains("Register document", page.Find("#documents-form-title").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_a_document_from_readiness_shows_the_document_dialog_on_the_readiness_page()
    {
        var page = RenderWorkspace<DocumentsReadiness>("/documents/readiness");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".readiness-document-link")));

        page.Find(".readiness-document-link").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".documents-file-form-card .documents-review-status")));
        Assert.NotNull(page.Find(".readiness-panel"));
        Assert.Contains(requestedPaths, path => path == $"/api/documents/{DocumentId}");
    }

    [Fact]
    public void Every_documents_page_and_dialog_shares_the_documents_styles()
    {
        Assert.True(RenderWorkspace<Documents>("/documents").Find(".documents-page").HasAttribute("b-documents"));
        Assert.True(RenderWorkspace<DocumentsRequirements>("/documents/requirements").Find(".documents-page").HasAttribute("b-documents"));
        Assert.True(RenderWorkspace<DocumentsRetention>("/documents/retention").Find(".documents-page").HasAttribute("b-documents"));

        var readiness = RenderWorkspace<DocumentsReadiness>("/documents/readiness");
        Assert.True(readiness.Find(".documents-page").HasAttribute("b-documents"));

        readiness.WaitForAssertion(() => Assert.NotEmpty(readiness.FindAll(".readiness-document-link")));
        readiness.Find(".readiness-document-link").Click();
        readiness.WaitForAssertion(() => Assert.True(readiness.Find(".documents-file-form-card").HasAttribute("b-documents")));
        readiness.Find(".documents-file-close-button").Click();

        readiness.Find(".readiness-documents .readiness-row-action").Click();
        readiness.WaitForAssertion(() => Assert.True(readiness.Find(".documents-form-card").HasAttribute("b-documents")));
    }

    private IRenderedComponent<TPage> RenderWorkspace<TPage>(string address)
        where TPage : IComponent
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);

        var page = Render<TPage>();
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
        "summary":{"configuredCount":2,"satisfiedCount":1,"unsatisfiedCount":1},
        "items":[{"requirementId":"11111111-1111-1111-1111-111111111111","vesselId":"{{VesselId}}","documentTypeId":7,
        "documentTypeName":"Safety Management Certificate","minimumRemainingValidityDays":0,"instruction":null,
        "requiredValidUntil":"2026-09-24","status":"Unsatisfied","blockers":["Missing"],"selectedDocumentId":null,"candidates":[]},
        {"requirementId":"22222222-2222-2222-2222-222222222222","vesselId":"{{VesselId}}","documentTypeId":8,
        "documentTypeName":"Load Line Certificate","minimumRemainingValidityDays":0,"instruction":null,
        "requiredValidUntil":"2026-09-24","status":"Satisfied","blockers":[],"selectedDocumentId":"{{DocumentId}}",
        "candidates":[{"documentId":"{{DocumentId}}","number":"CERT-001","status":"Active","reviewStatus":"Approved","hasFile":true,"retentionState":"Active","issueDate":"2026-01-01","expiryDate":"2028-01-01","qualifies":true,"blockers":[]}]}],
        "pageNumber":1,"pageSize":25,"totalCount":2}
        """;

    private static string DocumentDetails =>
        $$"""
        {"id":"{{DocumentId}}","vesselId":"{{VesselId}}","documentTypeId":8,"documentTypeName":"Load Line Certificate",
        "number":"CERT-001","issueDate":"2026-01-01","expiryDate":"2028-01-01","issuingAuthority":"Flag State",
        "status":"Active","reviewStatus":"Approved","hasFile":false,"fileName":null,"contentType":null,
        "sizeBytes":null,"uploadedBy":null,"uploadedAtUtc":null}
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
