using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace XRBrowser.Services;

public enum AdBlockResourceType
{
    Document,
    Stylesheet,
    Image,
    Media,
    Font,
    Script,
    Xhr,
    WebSocket,
    Ping,
    Other
}

/// <summary>
/// Lightweight AdGuard filter-list engine for WebView2.
/// It implements host/path network rules and basic cosmetic filtering. Scriptlets,
/// redirects, CSP and other extended AdGuard actions are intentionally not executed.
/// </summary>
public sealed class AdBlockService
{
    private static readonly string[] FilterUrls =
    {
        "https://filters.adtidy.org/extension/ublock/filters/2.txt",
        "https://filters.adtidy.org/extension/ublock/filters/224.txt"
    };

    private static readonly string[] FilterFiles =
    {
        "adguard-base.txt",
        "adguard-chinese.txt"
    };

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromDays(7);
    private static readonly HttpClient Http = CreateHttpClient();

    internal static readonly string[] BaselineCosmetic =
    {
        "ins.adsbygoogle",
        ".adsbygoogle",
        ".ad-container",
        ".ad-wrapper",
        ".ad-slot",
        ".ad-unit",
        ".ad-banner",
        ".ad-placeholder",
        ".advert",
        ".advertisement",
        "[id^=\"div-gpt-ad\"]",
        "[id^=\"google_ads\"]",
        "[id^=\"ad_\"]",
        "[class^=\"ad-\"]",
        "[class*=\" ad-\"]",
        "iframe[src*=\"doubleclick.net\"]",
        "iframe[src*=\"googlesyndication.com\"]",
        "iframe[src*=\"amazon-adsystem.com\"]"
    };

    private readonly SettingsService _settings;
    private AdBlockEngine _engine;
    private int _blockedCount;

    public AdBlockService(SettingsService settings)
    {
        _settings = settings;
        _engine = AdBlockEngine.LoadFromDiskOrFallback();
        Status = _engine.IsFallback ? "内置规则（正在下载 AdGuard 列表）" : "AdGuard Base + 中文过滤列表";
        _ = Task.Run(RefreshFiltersAsync);
    }

    public bool Enabled => _settings.Current.AdBlockEnabled;
    public int BlockedCount => Volatile.Read(ref _blockedCount);
    public string Status { get; private set; } = "";
    public int RuleCount => _engine.RuleCount;

    public bool ShouldBlock(Uri requestUri, Uri? documentUri, AdBlockResourceType type)
    {
        if (!Enabled) return false;
        if (requestUri.Scheme is not ("http" or "https")) return false;
        if (_engine.IsBlocked(requestUri, documentUri, type))
        {
            Interlocked.Increment(ref _blockedCount);
            return true;
        }
        return false;
    }

    public string? GetInjectionScript(string? pageUrl)
    {
        if (!Enabled) return null;
        var host = UrlHelper.GetHost(pageUrl);
        if (string.IsNullOrWhiteSpace(host) || host == UrlHelper.InternalHost) return null;
        var css = _engine.GetCosmeticCss(host);
        if (string.IsNullOrWhiteSpace(css)) return null;
        return BuildInjectionScript(css);
    }

    public static string RemovalScript =>
        "(function(){try{var e=document.getElementById('xr-adguard-style');if(e)e.remove();}catch(x){}})();";

    public void ResetCounter() => Interlocked.Exchange(ref _blockedCount, 0);

    private async Task RefreshFiltersAsync()
    {
        try
        {
            var needsUpdate = FilterFiles.Any(file =>
            {
                var path = Path.Combine(AppPaths.Filters, file);
                return !File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > UpdateInterval;
            });
            if (!needsUpdate) return;

            Directory.CreateDirectory(AppPaths.Filters);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var downloaded = false;
            for (var i = 0; i < FilterUrls.Length; i++)
            {
                var path = Path.Combine(AppPaths.Filters, FilterFiles[i]);
                if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <= UpdateInterval)
                    continue;

                try
                {
                    var text = await Http.GetStringAsync(FilterUrls[i], timeout.Token).ConfigureAwait(false);
                    if (text.Length > 1000)
                    {
                        await File.WriteAllTextAsync(path, text, timeout.Token).ConfigureAwait(false);
                        downloaded = true;
                    }
                }
                catch (Exception ex)
                {
                    AppPaths.Log($"AdGuard filter download failed ({FilterFiles[i]}): {ex.Message}");
                }
            }

            if (!downloaded) return;
            var engine = AdBlockEngine.LoadFromDiskOrFallback();
            _engine = engine;
            Status = engine.IsFallback ? "内置规则" : "AdGuard Base + 中文过滤列表";
            AppPaths.Log($"AdGuard filters loaded: {engine.RuleCount} network rules, {engine.CosmeticCount} cosmetic rules");
        }
        catch (Exception ex)
        {
            AppPaths.Log("AdGuard filter update failed: " + ex.Message);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 XRBrowser/1.0 AdGuardFilterLoader");
        return client;
    }

    private static string BuildInjectionScript(string css)
    {
        var json = JsonSerializer.Serialize(css);
        return "(function(){try{var css=" + json + ";" +
               "function apply(){var id='xr-adguard-style';var s=document.getElementById(id);" +
               "if(!s){s=document.createElement('style');s.id=id;(document.head||document.documentElement).appendChild(s);}" +
               "s.textContent=css;}" +
               "if(document.documentElement){apply();}" +
               "document.addEventListener('DOMContentLoaded',apply,{once:true});}catch(e){}})();";
    }
}

internal sealed class AdBlockEngine
{
    private readonly Dictionary<string, List<Rule>> _blockRules;
    private readonly Dictionary<string, List<Rule>> _allowRules;
    private readonly List<string> _blockPrefixes;
    private readonly List<string> _allowPrefixes;
    private readonly List<string> _genericCosmetic;
    private readonly Dictionary<string, List<string>> _cosmeticByDomain;

    private AdBlockEngine(
        Dictionary<string, List<Rule>> blockRules,
        Dictionary<string, List<Rule>> allowRules,
        List<string> blockPrefixes,
        List<string> allowPrefixes,
        List<string> genericCosmetic,
        Dictionary<string, List<string>> cosmeticByDomain,
        int ruleCount,
        int cosmeticCount,
        bool isFallback)
    {
        _blockRules = blockRules;
        _allowRules = allowRules;
        _blockPrefixes = blockPrefixes;
        _allowPrefixes = allowPrefixes;
        _genericCosmetic = genericCosmetic;
        _cosmeticByDomain = cosmeticByDomain;
        RuleCount = ruleCount;
        CosmeticCount = cosmeticCount;
        IsFallback = isFallback;
    }

    public int RuleCount { get; }
    public int CosmeticCount { get; }
    public bool IsFallback { get; }

    public bool IsBlocked(Uri requestUri, Uri? documentUri, AdBlockResourceType type)
    {
        var requestHost = NormalizeHost(requestUri.IdnHost);
        var documentHost = NormalizeHost(documentUri?.IdnHost);
        var thirdParty = documentHost.Length > 0 && GetSite(requestHost) != GetSite(documentHost);
        var url = requestUri.AbsoluteUri;

        foreach (var suffix in HostSuffixes(requestHost))
        {
            if (_allowRules.TryGetValue(suffix, out var rules) && rules.Any(r => r.Matches(requestUri, type, documentHost, thirdParty)))
                return false;
        }
        if (_allowPrefixes.Any(p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        foreach (var suffix in HostSuffixes(requestHost))
        {
            if (_blockRules.TryGetValue(suffix, out var rules) && rules.Any(r => r.Matches(requestUri, type, documentHost, thirdParty)))
                return true;
        }
        return _blockPrefixes.Any(p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    public string? GetCosmeticCss(string host)
    {
        var selectors = new List<string>(_genericCosmetic);
        foreach (var suffix in HostSuffixes(NormalizeHost(host)))
        {
            if (_cosmeticByDomain.TryGetValue(suffix, out var list))
                selectors.AddRange(list);
        }
        if (selectors.Count == 0) return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var builder = new StringBuilder();
        foreach (var selector in selectors)
        {
            if (selector.Length == 0 || !seen.Add(selector)) continue;
            builder.Append(selector).Append("{display:none!important}\n");
            if (builder.Length > 220_000) break;
        }
        return builder.Length == 0 ? null : builder.ToString();
    }

    public static AdBlockEngine LoadFromDiskOrFallback()
    {
        try
        {
            var files = new[]
            {
                Path.Combine(AppPaths.Filters, "adguard-base.txt"),
                Path.Combine(AppPaths.Filters, "adguard-chinese.txt")
            };
            var existing = files.Where(File.Exists).ToArray();
            if (existing.Length == 0) return BuildFallback();
            return Parse(existing.Select(File.ReadAllText), isFallback: false);
        }
        catch (Exception ex)
        {
            AppPaths.Log("AdGuard filter load failed: " + ex.Message);
            return BuildFallback();
        }
    }

    private static AdBlockEngine BuildFallback()
    {
        string[] rules =
        {
            "||doubleclick.net^",
            "||googlesyndication.com^",
            "||googleadservices.com^",
            "||adservice.google.com^",
            "||pagead2.googlesyndication.com^",
            "||amazon-adsystem.com^",
            "||adnxs.com^",
            "||criteo.net^",
            "||criteo.com^",
            "||taboola.com^",
            "||outbrain.com^",
            "||scorecardresearch.com^",
            "||quantserve.com^",
            "||zedo.com^",
            "||popads.net^",
            "||propellerads.com^",
            "||adsterra.com^",
            "||media.net^",
            "||moatads.com^",
            "||adsrvr.org^",
            "||casalemedia.com^",
            "||contextweb.com^",
            "||pubmatic.com^",
            "||rubiconproject.com^",
            "||smartadserver.com^",
            "||yieldmo.com^",
            "||adform.net^",
            "||ads.yahoo.com^",
            "||analytics.yahoo.com^",
            "||bing.com/th/id/ads^"
        };
        return Parse(new[] { string.Join('\n', rules), string.Join('\n', AdBlockService.BaselineCosmetic.Select(s => "##" + s)) }, isFallback: true);
    }

    private static AdBlockEngine Parse(IEnumerable<string> filterTexts, bool isFallback)
    {
        var builder = new Builder();
        foreach (var text in filterTexts)
        {
            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } line)
                builder.Add(line);
        }
        return builder.Build(isFallback);
    }

    private sealed class Rule
    {
        public string? Path { get; init; }
        public AdBlockResourceType? Type { get; init; }
        public bool ThirdPartyOnly { get; init; }
        public bool FirstPartyOnly { get; init; }
        public string[] IncludeDomains { get; init; } = Array.Empty<string>();
        public string[] ExcludeDomains { get; init; } = Array.Empty<string>();

        public bool Matches(Uri request, AdBlockResourceType type, string documentHost, bool thirdParty)
        {
            if (Type is { } required && required != type) return false;
            if (ThirdPartyOnly && !thirdParty) return false;
            if (FirstPartyOnly && thirdParty) return false;

            if (IncludeDomains.Length > 0 &&
                !IncludeDomains.Any(d => DomainMatches(documentHost, d)))
                return false;
            if (ExcludeDomains.Any(d => DomainMatches(documentHost, d)))
                return false;

            if (!string.IsNullOrEmpty(Path))
            {
                var pathAndQuery = request.PathAndQuery;
                if (!pathAndQuery.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
    }

    private sealed class Builder
    {
        private readonly Dictionary<string, List<Rule>> _block = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Rule>> _allow = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _blockPrefixes = new();
        private readonly List<string> _allowPrefixes = new();
        private readonly List<string> _genericCosmetic = new();
        private readonly Dictionary<string, List<string>> _cosmetic = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _cosmeticSeen = new(StringComparer.OrdinalIgnoreCase);
        private int _networkCount;

        public Builder()
        {
            foreach (var selector in AdBlockService.BaselineCosmetic)
            {
                if (_cosmeticSeen.Add(selector))
                    _genericCosmetic.Add(selector);
            }
        }

        public void Add(string rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('!') || line.StartsWith('[')) return;
            if (line.Contains("#@#") || line.Contains("#?#") || line.Contains("#$#") || line.Contains("#%#")) return;

            var cosmeticIndex = line.IndexOf("##", StringComparison.Ordinal);
            if (cosmeticIndex >= 0)
            {
                AddCosmetic(line[..cosmeticIndex], line[(cosmeticIndex + 2)..]);
                return;
            }

            var isException = line.StartsWith("@@", StringComparison.Ordinal);
            if (isException) line = line[2..];

            var optionIndex = line.IndexOf('$');
            var pattern = optionIndex >= 0 ? line[..optionIndex] : line;
            var optionText = optionIndex >= 0 ? line[(optionIndex + 1)..] : "";
            if (!TryParseOptions(optionText, out var type, out var thirdPartyOnly, out var firstPartyOnly,
                    out var includes, out var excludes, out var supported))
                return;
            if (!supported) return;

            if (pattern.StartsWith("||", StringComparison.Ordinal))
            {
                var rest = pattern[2..];
                var end = rest.IndexOfAny(new[] { '^', '/' });
                var host = NormalizeHost(end >= 0 ? rest[..end] : rest);
                if (host.Length == 0 || host.Contains('*') || host.Contains('\\')) return;

                string? path = null;
                if (end >= 0)
                {
                    var tail = rest[end..];
                    if (tail.StartsWith('^')) tail = tail[1..];
                    if (tail.StartsWith('/'))
                    {
                        if (tail.Contains('*')) return;
                        path = tail;
                    }
                    else if (tail.Length > 0)
                    {
                        return;
                    }
                }

                var rule = new Rule
                {
                    Path = path,
                    Type = type,
                    ThirdPartyOnly = thirdPartyOnly,
                    FirstPartyOnly = firstPartyOnly,
                    IncludeDomains = includes,
                    ExcludeDomains = excludes
                };
                var target = isException ? _allow : _block;
                if (!target.TryGetValue(host, out var list))
                    target[host] = list = new List<Rule>(1);
                list.Add(rule);
                _networkCount++;
                return;
            }

            if (pattern.StartsWith('|'))
            {
                if (type is not null || thirdPartyOnly || firstPartyOnly || includes.Length > 0 || excludes.Length > 0)
                    return;
                var prefix = pattern[1..];
                if (prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var clean = prefix.Replace("^", "/");
                    (isException ? _allowPrefixes : _blockPrefixes).Add(clean);
                    _networkCount++;
                }
            }
        }

        private void AddCosmetic(string domainPart, string selector)
        {
            selector = selector.Trim();
            if (selector.Length == 0 || selector.Length > 500) return;
            if (selector.Contains(":contains(", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains(":has-text(", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains(":matches-css(", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains(":style(", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains(":remove(", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains(":upward(", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains(":xpath(", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains(":watch-attr(", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains("+js(", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrWhiteSpace(domainPart))
            {
                if (_genericCosmetic.Count < 2500 && _cosmeticSeen.Add(selector))
                    _genericCosmetic.Add(selector);
                return;
            }

            foreach (var rawDomain in domainPart.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var domain = rawDomain.Trim();
                if (domain.StartsWith('~') || domain.Contains('*') || domain.Length == 0) continue;
                domain = NormalizeHost(domain);
                if (domain.Length == 0) continue;
                var key = domain + "\n" + selector;
                if (!_cosmeticSeen.Add(key)) continue;
                if (!_cosmetic.TryGetValue(domain, out var list))
                    _cosmetic[domain] = list = new List<string>();
                if (list.Count < 250) list.Add(selector);
            }
        }

        public AdBlockEngine Build(bool isFallback)
        {
            var cosmeticCount = _genericCosmetic.Count + _cosmetic.Values.Sum(v => v.Count);
            return new AdBlockEngine(_block, _allow, _blockPrefixes, _allowPrefixes,
                _genericCosmetic, _cosmetic, _networkCount, cosmeticCount, isFallback);
        }

        private static bool TryParseOptions(
            string text,
            out AdBlockResourceType? type,
            out bool thirdPartyOnly,
            out bool firstPartyOnly,
            out string[] includes,
            out string[] excludes,
            out bool supported)
        {
            type = null;
            thirdPartyOnly = false;
            firstPartyOnly = false;
            includes = Array.Empty<string>();
            excludes = Array.Empty<string>();
            supported = true;
            if (string.IsNullOrWhiteSpace(text)) return true;

            var includeList = new List<string>();
            var excludeList = new List<string>();
            foreach (var raw in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var option = raw.Trim();
                if (option.Length == 0) continue;
                if (option.StartsWith("domain=", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var domain in option[7..].Split('|', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var value = domain.Trim();
                        if (value.StartsWith('~')) excludeList.Add(NormalizeHost(value[1..]));
                        else includeList.Add(NormalizeHost(value));
                    }
                    continue;
                }

                var negated = option.StartsWith('~');
                var name = (negated ? option[1..] : option).ToLowerInvariant();
                if (negated)
                {
                    if (name == "third-party") firstPartyOnly = true;
                    else if (name == "first-party") thirdPartyOnly = true;
                    else if (name is "script" or "image" or "stylesheet" or "xmlhttprequest" or "font" or "media" or "subdocument" or "websocket" or "ping" or "object" or "other")
                    {
                        // Negated resource types would require "all except this type"; skip the rule
                        // rather than risk over-blocking.
                        supported = false;
                    }
                    continue;
                }

                switch (name)
                {
                    case "third-party": thirdPartyOnly = true; break;
                    case "first-party": firstPartyOnly = true; break;
                    case "script": type = AdBlockResourceType.Script; break;
                    case "image": type = AdBlockResourceType.Image; break;
                    case "stylesheet": type = AdBlockResourceType.Stylesheet; break;
                    case "xmlhttprequest":
                    case "fetch": type = AdBlockResourceType.Xhr; break;
                    case "font": type = AdBlockResourceType.Font; break;
                    case "media": type = AdBlockResourceType.Media; break;
                    case "websocket": type = AdBlockResourceType.WebSocket; break;
                    case "ping": type = AdBlockResourceType.Ping; break;
                    case "object":
                    case "other": type = AdBlockResourceType.Other; break;
                    case "subdocument": type = AdBlockResourceType.Document; break;
                    case "all":
                    case "important":
                    case "match-case":
                        break;
                    case "document":
                    case "popup":
                    case "csp":
                    case "redirect":
                    case "redirect-rule":
                    case "removeparam":
                    case "cookie":
                    case "header":
                    case "replace":
                    case "empty":
                    case "mp4":
                    case "webrtc":
                    case "permissions":
                    case "urltransform":
                    case "ipaddress":
                    case "method":
                    case "to":
                    case "denyallow":
                    case "client":
                    case "referrerpolicy":
                    case "elemhide":
                    case "generichide":
                    case "specifichide":
                    case "genericblock":
                    case "badfilter":
                        supported = false;
                        break;
                    default:
                        supported = false;
                        break;
                }
            }
            includes = includeList.Where(d => d.Length > 0).Distinct().ToArray();
            excludes = excludeList.Where(d => d.Length > 0).Distinct().ToArray();
            return true;
        }
    }

    private static IEnumerable<string> HostSuffixes(string host)
    {
        var start = 0;
        while (start < host.Length)
        {
            yield return host[start..];
            var dot = host.IndexOf('.', start);
            if (dot < 0) yield break;
            start = dot + 1;
        }
    }

    private static bool DomainMatches(string host, string domain)
        => host.Length > 0 && (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                               host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    private static string GetSite(string host)
    {
        if (string.IsNullOrEmpty(host)) return "";
        var parts = host.Split('.');
        if (parts.Length <= 2) return host;
        return parts[^2] + "." + parts[^1];
    }

    private static string NormalizeHost(string? host)
        => (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
}
