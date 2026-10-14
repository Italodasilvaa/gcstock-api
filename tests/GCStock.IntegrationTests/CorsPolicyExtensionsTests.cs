using GCStock.Api.Cors;

namespace GCStock.IntegrationTests;

public class CorsPolicyExtensionsTests
{
    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("https://localhost")]
    [InlineData("https://iwsolutions.cloud")]
    [InlineData("https://app.iwsolutions.cloud")]
    [InlineData("https://sub.domain.iwsolutions.cloud")]
    public void Allows_Localhost_And_IwSolutions_Domains(string origin)
    {
        Assert.True(CorsPolicyExtensions.IsOriginAllowed(origin));
    }

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("https://iwsolutions.cloud.evil.com")]
    [InlineData("not-a-uri")]
    public void Rejects_Unknown_Origins(string origin)
    {
        Assert.False(CorsPolicyExtensions.IsOriginAllowed(origin));
    }
}
