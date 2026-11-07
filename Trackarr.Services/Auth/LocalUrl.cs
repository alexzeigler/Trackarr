namespace Trackarr.Services.Auth;

public static class LocalUrl
{
    /// <summary>
    ///     True when <paramref name="url" /> is a path on this site ("/queue", "/trackarr/missing?page=2"). Anything
    ///     that could leave the site is rejected: full URLs, "//host" and "/\host" (which browsers treat as another
    ///     host), and control characters. Use it before redirecting to a return URL from the query string.
    /// </summary>
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/') return false;
        if (url.Length > 1 && url[1] is '/' or '\\') return false;
        return !url.Any(char.IsControl);
    }
}
