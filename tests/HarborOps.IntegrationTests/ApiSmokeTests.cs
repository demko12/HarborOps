using System.Net;

namespace HarborOps.IntegrationTests;

public sealed class ApiSmokeTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public ApiSmokeTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync("/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
