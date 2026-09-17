namespace Girus.Api.Cors;

public static class CorsPolicyExtensions
{
    public const string PolicyName = "GirusCorsPolicy";

    public static bool IsOriginAllowed(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;

        return host is "localhost" or "127.0.0.1"
            || host.Equals("valee.com.br", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".valee.com.br", StringComparison.OrdinalIgnoreCase);
    }
}
