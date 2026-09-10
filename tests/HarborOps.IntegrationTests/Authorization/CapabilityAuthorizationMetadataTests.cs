using System.Net;
using System.Text.RegularExpressions;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Authorization;

public sealed class CapabilityAuthorizationMetadataTests : IntegrationTestBase
{
    private static readonly (string RoutePrefix, string Capability)[] CapabilityRoutes =
    [
        ("/api/fleet", "fleet"),
        ("/api/crew", "crew"),
        ("/api/voyages", "voyages"),
        ("/api/cargo", "cargo"),
        ("/api/maintenance", "maintenance"),
        ("/api/compliance", "compliance"),
        ("/api/documents", "documents"),
        ("/api/reports", "reports"),
    ];

    private static readonly Regex RouteParameterPattern = new(@"\{[^}]+\}", RegexOptions.Compiled);

    private readonly HarborOpsApiFactory _application;

    public CapabilityAuthorizationMetadataTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Every_business_controller_endpoint_enforces_its_matching_capability_policy()
    {
        using var anonymousClient = _application.CreateClient();
        using var unrelatedPermissionClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Read);
        var permittedClients = await CreatePermittedClientsAsync();
        var endpoints = _application.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .Select(endpoint => new
            {
                Endpoint = endpoint,
                Path = "/" + endpoint.RoutePattern.RawText!.TrimStart('~', '/'),
            })
            .Select(item => new
            {
                item.Endpoint,
                Capability = CapabilityRoutes
                    .Where(entry => item.Path.StartsWith(entry.RoutePrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => (string?)entry.Capability)
                    .SingleOrDefault(),
            })
            .Where(item => item.Capability is not null)
            .ToArray();

        Assert.NotEmpty(endpoints);

        try
        {
            foreach (var item in endpoints)
            {
                using var anonymousResponse = await anonymousClient.SendAsync(CreateRequest(item.Endpoint));
                using var unrelatedPermissionResponse = await unrelatedPermissionClient.SendAsync(CreateRequest(item.Endpoint));
                using var permittedResponse = await permittedClients[item.Capability!].SendAsync(CreateRequest(item.Endpoint));

                Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, unrelatedPermissionResponse.StatusCode);
                Assert.True(
                    permittedResponse.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden,
                    $"{item.Endpoint.DisplayName} rejected its {item.Capability} capability client with {permittedResponse.StatusCode}.");
            }
        }
        finally
        {
            foreach (var client in permittedClients.Values)
            {
                client.Dispose();
            }
        }
    }

    private async Task<Dictionary<string, HttpClient>> CreatePermittedClientsAsync()
    {
        return new Dictionary<string, HttpClient>(StringComparer.Ordinal)
        {
            ["fleet"] = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read, Permissions.Fleet.Manage),
            ["crew"] = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read, Permissions.Crew.Manage),
            ["voyages"] = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Voyages.Read, Permissions.Voyages.Manage),
            ["cargo"] = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Read, Permissions.Cargo.Manage),
            ["maintenance"] = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read, Permissions.Maintenance.Manage),
            ["compliance"] = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read, Permissions.Compliance.Manage),
            // Documents.Dispose is added on top of Read/Manage because two Documents
            // endpoints (disposition approve/reject) opt out of the blanket per-route
            // read/manage split via their own [Authorize] attribute and require this more
            // specific capability instead - see CapabilityAuthorizationConvention's
            // IAuthorizeData opt-out check.
            ["documents"] = await _application.CreateAuthenticatedClientWithPermissionsAsync(
                Permissions.Documents.Read, Permissions.Documents.Manage, Permissions.Documents.Dispose),
            ["reports"] = await _application.CreateAuthenticatedClientWithPermissionsAsync(
                Permissions.Reports.Read,
                Permissions.Reports.Manage,
                Permissions.Compliance.Read,
                Permissions.Documents.Read,
                Permissions.Maintenance.Read),
        };
    }

    private static HttpRequestMessage CreateRequest(RouteEndpoint endpoint)
    {
        var httpMethod = endpoint.Metadata
            .OfType<IHttpMethodMetadata>()
            .SelectMany(metadata => metadata.HttpMethods)
            .First();
        var path = RouteParameterPattern.Replace(
            endpoint.RoutePattern.RawText!,
            match => match.Value.Contains(":guid", StringComparison.OrdinalIgnoreCase)
                ? Guid.NewGuid().ToString()
                : match.Value.Contains(":int", StringComparison.OrdinalIgnoreCase)
                    ? "1"
                    : "test");

        return new HttpRequestMessage(new HttpMethod(httpMethod), "/" + path.TrimStart('~', '/'));
    }
}
