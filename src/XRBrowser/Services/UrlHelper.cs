using System.Net;
using System.Text.RegularExpressions;
using XRBrowser.Models;

namespace XRBrowser.Services;

public static class UrlHelper
{
    public const string InternalHost = "xr.internal";
    public const string BackgroundHost = "xr.background";
    public const string BackgroundOrigin = "https://xr.background";
    public const string InternalOrigin = "https://xr.internal";
    public const string NewTabUrl = "https://xr.internal/newtab.html";
    public const string AppPageUrl = "https://xr.internal/app.html";

    public static readonly IReadOnlyDictionary<string, (string Name, string Template)> SearchEngines =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = ("Google", "https://www.google.com/search?q={0}"),
            ["Bing"] = ("Bing", "https://www.bing.com/search?q={0}"),
            ["Baidu"] = ("百度", "https://www.baidu.com/s?wd={0}"),
            ["DuckDuckGo"] = ("DuckDuckGo", "https://duckduckgo.com/?q={0}"),
            ["Yandex"] = ("Yandex", "https://yandex.com/search/?text={0}")
        };

    private static readonly Regex SchemeRegex = new(@"^[a-zA-Z][a-zA-Z0-9+.-]*:", RegexOptions.Compiled);
    private static readonly Regex HttpSchemeRegex = new(@"^(?<scheme>https?):[\\/]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MissingHttpColonRegex = new(@"^(?<scheme>https?)[\\/]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AsciiTldRegex = new(@"^[A-Za-z]{2,}$", RegexOptions.Compiled);
    private static readonly Regex Ipv4Regex = new(@"^\d{1,3}(\.\d{1,3}){3}$", RegexOptions.Compiled);
    private static readonly Regex HostLikeRegex = new(@"^(localhost|(\d{1,3}\.){3}\d{1,3}|\[[0-9a-fA-F:]+\])(:\d+)?([/?#].*)?$", RegexOptions.Compiled);
    private static readonly Regex DomainRegex = new(@"^[\p{L}\p{N}](?:[\p{L}\p{N}\-]*[\p{L}\p{N}])?(?:\.[\p{L}\p{N}](?:[\p{L}\p{N}\-]*[\p{L}\p{N}])?)+(:\d+)?([/?#].*)?$", RegexOptions.Compiled);

    public static string GetSearchUrl(string query, AppSettings settings)
    {
        var template = settings.SearchEngine.Equals("Custom", StringComparison.OrdinalIgnoreCase)
            ? settings.CustomSearchUrl
            : SearchEngines.TryGetValue(settings.SearchEngine, out var engine)
                ? engine.Template
                : SearchEngines["Bing"].Template;
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{0}"))
            template = SearchEngines["Bing"].Template;
        return string.Format(template, Uri.EscapeDataString(query));
    }

    public static string GetSearchEngineName(AppSettings settings)
        => settings.SearchEngine.Equals("Custom", StringComparison.OrdinalIgnoreCase)
            ? "自定义"
            : SearchEngines.TryGetValue(settings.SearchEngine, out var e) ? e.Name : "Bing";

    /// <summary>
    /// Fixes common IME/typing mistakes in URL-like input without changing normal search queries.
    /// Examples: bing。com, https:\\example.com, https：／／example。com.
    /// </summary>
    private static string NormalizeUrlTypos(string input)
    {
        var mapped = MapFullWidthPunctuation(input);

        var schemeMatch = Regex.Match(mapped, @"^(?<scheme>[A-Za-z][A-Za-z0-9+.-]*):");
        var hasScheme = schemeMatch.Success;
        var isHttp = hasScheme &&
                     (schemeMatch.Groups["scheme"].Value.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                      schemeMatch.Groups["scheme"].Value.Equals("https", StringComparison.OrdinalIgnoreCase));
        var missingColon = MissingHttpColonRegex.IsMatch(mapped);

        if (hasScheme && !isHttp)
            return input; // Do not touch xr:, file:, mailto:, custom schemes, etc.

        if (!isHttp && !missingColon && !LooksLikeDomainCandidate(mapped))
            return input;

        if (!isHttp && !missingColon)
            return mapped.Replace('\\', '/');

        mapped = mapped.Replace('\\', '/');
        if (MissingHttpColonRegex.IsMatch(mapped))
            mapped = MissingHttpColonRegex.Replace(mapped, "${scheme}://");
        mapped = HttpSchemeRegex.Replace(mapped, "${scheme}://");
        return mapped;
    }

    private static bool LooksLikeDomainCandidate(string input)
    {
        if (input.Any(char.IsWhiteSpace)) return false;

        var host = input;
        var pathIndex = host.IndexOfAny(new[] { '/', '?', '#' });
        if (pathIndex >= 0) host = host[..pathIndex];

        var portIndex = host.LastIndexOf(':');
        if (portIndex >= 0) host = host[..portIndex];

        if (Ipv4Regex.IsMatch(host)) return true;

        var dot = host.LastIndexOf('.');
        if (dot <= 0 || dot == host.Length - 1) return false;
        return AsciiTldRegex.IsMatch(host[(dot + 1)..]);
    }

    private static string MapFullWidthPunctuation(string input)
    {
        char[]? buffer = null;
        for (var i = 0; i < input.Length; i++)
        {
            var replacement = input[i] switch
            {
                '。' or '｡' or '．' => '.',
                '：' => ':',
                '／' => '/',
                '＼' => '\\',
                '？' => '?',
                '＃' => '#',
                '＆' => '&',
                '＝' => '=',
                '％' => '%',
                '＠' => '@',
                '＿' => '_',
                '－' => '-',
                '～' => '~',
                _ => input[i]
            };
            if (replacement == input[i]) continue;
            buffer ??= input.ToCharArray();
            buffer[i] = replacement;
        }
        return buffer is null ? input : new string(buffer);
    }

    public static bool TryNormalizeNavigation(string input, AppSettings settings, out string target, out bool isSearch)
    {
        target = "";
        isSearch = false;
        input = (input ?? "").Trim();
        if (input.Length == 0) return false;
        input = NormalizeUrlTypos(input);

        if (IsInternalInput(input))
        {
            target = ToInternalUrl(input);
            return true;
        }

        if (input.StartsWith("xr://", StringComparison.OrdinalIgnoreCase))
        {
            target = ToInternalUrl(input);
            return true;
        }

        if (SchemeRegex.IsMatch(input))
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme is "http" or "https" or "file" or "about" or "data" or "edge")
                {
                    target = input;
                    return true;
                }

                // Unknown schemes are handed to the OS by the window, not searched.
                target = input;
                return true;
            }
        }

        if (!input.Contains(' ') && (HostLikeRegex.IsMatch(input) || DomainRegex.IsMatch(input)))
        {
            var scheme = input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                         HostLikeRegex.IsMatch(input)
                ? "http://"
                : "https://";
            target = scheme + input;
            return true;
        }

        isSearch = true;
        target = GetSearchUrl(input, settings);
        return true;
    }

    public static bool IsInternalInput(string input)
        => input.StartsWith("xr://", StringComparison.OrdinalIgnoreCase)
           || input.StartsWith(InternalOrigin, StringComparison.OrdinalIgnoreCase);

    public static string ToInternalUrl(string canonical)
    {
        if (canonical.StartsWith(InternalOrigin, StringComparison.OrdinalIgnoreCase))
            return canonical;

        var page = canonical.StartsWith("xr://", StringComparison.OrdinalIgnoreCase)
            ? canonical[5..].Trim('/')
            : canonical.Trim('/');
        var hashIndex = page.IndexOf('#');
        var fragment = "";
        if (hashIndex >= 0)
        {
            fragment = page[hashIndex..];
            page = page[..hashIndex];
        }

        page = page.ToLowerInvariant();
        if (page is "" or "newtab" or "new-tab") return NewTabUrl;
        if (page.StartsWith("settings/")) return AppPageUrl + "#settings/" + page["settings/".Length..];
        return AppPageUrl + "#" + (page.Length == 0 ? "settings" : page) + fragment;
    }

    public static bool IsNewTabUrl(string? url)
        => url is not null &&
           (url.StartsWith(NewTabUrl, StringComparison.OrdinalIgnoreCase) ||
            url.Equals("xr://newtab", StringComparison.OrdinalIgnoreCase));

    public static bool IsInternalUrl(string? url)
        => url is not null && url.StartsWith(InternalOrigin, StringComparison.OrdinalIgnoreCase);

    public static bool IsInternalAppUrl(string? url)
        => url is not null && url.StartsWith(AppPageUrl, StringComparison.OrdinalIgnoreCase);

    public static string GetInternalPageName(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "newtab";
        if (IsNewTabUrl(url)) return "newtab";
        var idx = url.IndexOf('#');
        if (idx < 0) return "settings";
        return url[(idx + 1)..].Split('/')[0];
    }

    public static string ToCanonicalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "xr://newtab";
        if (IsNewTabUrl(url)) return "xr://newtab";
        if (url.StartsWith(AppPageUrl, StringComparison.OrdinalIgnoreCase))
        {
            var page = GetInternalPageName(url);
            return "xr://" + (page.Length == 0 ? "settings" : page);
        }
        return url;
    }

    public static string GetOmniboxText(string? url, bool focused)
    {
        if (string.IsNullOrWhiteSpace(url) || IsNewTabUrl(url)) return "";
        if (IsInternalUrl(url)) return ToCanonicalUrl(url);
        if (focused) return url;

        try
        {
            var uri = new Uri(url);
            var text = uri.Host + uri.PathAndQuery + uri.Fragment;
            if (uri.IsDefaultPort == false && !text.Contains(':'))
                text = uri.Host + ":" + uri.Port + uri.PathAndQuery + uri.Fragment;
            return Uri.UnescapeDataString(text.TrimEnd('/'));
        }
        catch
        {
            return url;
        }
    }

    public static string GetFullDisplayUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (IsNewTabUrl(url)) return "";
        if (IsInternalUrl(url)) return ToCanonicalUrl(url);
        return url;
    }

    public static string GetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (IsInternalUrl(url)) return "XR Browser";
        try { return new Uri(url).Host; } catch { return ""; }
    }

    public static string GetSiteLetter(string? url)
    {
        var host = GetHost(url);
        return host.Length == 0 ? "?" : host.TrimStart('w', 'w', 'w', '.').ToUpperInvariant()[..1];
    }

    public static SecurityState GetSecurityState(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || IsInternalUrl(url)) return SecurityState.Internal;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return SecurityState.Secure;
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return SecurityState.Local;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var host = GetHost(url);
            if (host is "localhost" or "127.0.0.1" or "::1") return SecurityState.Local;
            return SecurityState.Insecure;
        }
        return SecurityState.Unknown;
    }

    public static bool IsSupportedBrowserScheme(string scheme)
        => scheme is "http" or "https" or "file" or "about" or "data" or "edge" or "chrome" or "xr";

    public static string GetDownloadFileName(string path)
        => Path.GetFileName(path);
}

public enum SecurityState
{
    Internal,
    Secure,
    Insecure,
    Local,
    Unknown,
    Dangerous
}
