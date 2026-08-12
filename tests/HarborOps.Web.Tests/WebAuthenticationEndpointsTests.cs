using HarborOps.Web;

namespace HarborOps.Web.Tests;

public sealed class WebAuthenticationEndpointsTests
{
    [Theory]
    [InlineData("/administration", "/administration")]
    [InlineData("/fleet?page=2", "/fleet?page=2")]
    [InlineData("https://attacker.example", "/")]
    [InlineData("//attacker.example", "/")]
    [InlineData("administration", "/")]
    public void SafeReturnUrl_rejects_external_and_non_local_urls(string supplied, string expected)
    {
        Assert.Equal(expected, WebAuthenticationEndpoints.SafeReturnUrl(supplied));
    }
}
