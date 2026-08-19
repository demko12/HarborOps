using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Identity.Contracts;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class InsurancePoliciesEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory application;

    public InsurancePoliciesEndpointTests(HarborOpsApiFactory application)
        : base(application) => this.application = application;

    [Fact]
    public async Task Post_registers_a_policy_and_get_returns_its_expiry_classification()
    {
        using var client = await application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Compliance.Manage,
            Permissions.Compliance.Read);
        var vesselId = Guid.NewGuid();
        var request = CreateRequest(vesselId, "Harbor Mutual", "HM-2027-001");

        using var post = await client.PostAsJsonAsync("/api/compliance/insurance-policies", request, CancellationToken.None);
        using var get = await client.GetAsync(
            $"/api/compliance/insurance-policies?vesselId={vesselId}&pageNumber=1&pageSize=25",
            CancellationToken.None);
        var response = await get.Content.ReadFromJsonAsync<GetInsurancePoliciesResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.NotNull(response);
        var policy = Assert.Single(response.Items);
        Assert.Equal("Harbor Mutual", policy.Insurer);
        Assert.Equal("Active", policy.ExpiryClassification);
    }

    [Fact]
    public async Task Post_returns_conflict_for_a_duplicate_insurer_and_policy_number()
    {
        using var client = await application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var first = CreateRequest(Guid.NewGuid(), "Ocean Mutual", "OM-DUP-001");
        var duplicate = first with { VesselId = Guid.NewGuid() };
        using var initial = await client.PostAsJsonAsync("/api/compliance/insurance-policies", first, CancellationToken.None);
        using var conflict = await client.PostAsJsonAsync("/api/compliance/insurance-policies", duplicate, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, initial.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Read_permission_cannot_register_and_manage_permission_includes_read_access()
    {
        using var readClient = await application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var manageClient = await application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var forbiddenPost = await readClient.PostAsJsonAsync(
            "/api/compliance/insurance-policies",
            CreateRequest(Guid.NewGuid(), "Coastal Mutual", "CM-001"),
            CancellationToken.None);
        using var permittedGet = await manageClient.GetAsync(
            "/api/compliance/insurance-policies?pageNumber=1&pageSize=25",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, forbiddenPost.StatusCode);
        Assert.Equal(HttpStatusCode.OK, permittedGet.StatusCode);
    }

    private static RegisterInsurancePolicyRequest CreateRequest(Guid vesselId, string insurer, string number) =>
        new(vesselId, insurer, number, "P&I", new DateOnly(2027, 8, 19));
}
