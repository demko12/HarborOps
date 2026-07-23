using System.Runtime.InteropServices;

namespace HarborOps.UnitTests;

public sealed class FoundationSmokeTests
{
    [Fact]
    public void Tests_run_on_dotnet_10()
    {
        Assert.Contains(".NET 10.", RuntimeInformation.FrameworkDescription);
    }
}
