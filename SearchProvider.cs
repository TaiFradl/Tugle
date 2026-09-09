using System.Net;

namespace Tugle;

internal static class SearchProvider
{
    public static readonly string[] Names = ["Google", "DuckDuckGo", "Bing"];
    public static string Normalize(string? provider) => Names.Contains(provider) ? provider! : "Google";

    public static string BuildUrl(string provider, string query) => (Normalize(provider) switch
    {
        "DuckDuckGo" => "https://duckduckgo.com/?q=",
        "Bing" => "https://www.bing.com/search?q=",
        _ => "https://www.google.com/search?q="
    }) + Uri.EscapeDataString(query);

    public static string? GetQuery(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;
        var host = uri.IdnHost;
        var known = ((host is "google.com" or "www.google.com" or "bing.com" or "www.bing.com") && uri.AbsolutePath == "/search") ||
            ((host is "duckduckgo.com" or "www.duckduckgo.com" or "html.duckduckgo.com") && uri.AbsolutePath is "/" or "/html/");
        if (!known) return null;
        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && WebUtility.UrlDecode(pair[0]) == "q") return WebUtility.UrlDecode(pair[1]);
        }
        return null;
    }
}
