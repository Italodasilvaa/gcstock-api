using GCStock.Api.Cors;

namespace GCStock.IntegrationTests;

public class CorsPolicyExtensionsTests
{
    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("https://localhost")]
    [InlineData("https://valee.com.br")]
    [InlineData("https://app.valee.com.br")]
    [InlineData("https://sub.domain.valee.com.br")]
    public void Allows_Localhost_And_Valee_Domains(string origin)
    {
        Assert.True(CorsPolicyExtensions.IsOriginAllowed(origin));
    }

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("https://valee.com.br.evil.com")]
    [InlineData("not-a-uri")]
    public void Rejects_Unknown_Origins(string origin)
    {
        Assert.False(CorsPolicyExtensions.IsOriginAllowed(origin));
    }
}
