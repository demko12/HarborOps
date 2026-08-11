using HarborOps.Web.Api;
using Microsoft.Extensions.Configuration;

namespace HarborOps.Web.Tests.Api;

public sealed class ApiConfigurationTests
{
    [Fact]
    public void GetBaseAddress_returns_normalized_configured_url()
    {
        var configuration = CreateConfiguration("http://localhost:5204");

        var result = ApiConfiguration.GetBaseAddress(configuration);

        Assert.Equal(new Uri("http://localhost:5204/"), result);
    }

    [Fact]
    public void GetBaseAddress_rejects_missing_url()
    {
        var configuration = CreateConfiguration(null);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ApiConfiguration.GetBaseAddress(configuration));

        Assert.Contains(ApiConfiguration.BaseUrlKey, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("ftp://harborops.test/")]
    public void GetBaseAddress_rejects_non_http_absolute_url(string configuredUrl)
    {
        var configuration = CreateConfiguration(configuredUrl);

        Assert.Throws<InvalidOperationException>(
            () => ApiConfiguration.GetBaseAddress(configuration));
    }

    private static IConfiguration CreateConfiguration(string? baseUrl)
    {
        var values = new Dictionary<string, string?>
        {
            [ApiConfiguration.BaseUrlKey] = baseUrl,
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
