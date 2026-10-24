using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace XRBrowser.Services;

public sealed class FaviconService
{
    private const int MaxFaviconBytes = 2_000_000;
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly Regex LinkTagRegex = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RelRegex = new(@"\brel\s*=\s*(?:[""'](?<v>[^""']+)[""']|(?<v>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HrefRegex = new(@"\bhref\s*=\s*(?:[""'](?<v>[^""']+)[""']|(?<v>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _webInflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _networkInflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _networkLimit = new(4, 4);

    /// <summary>
    /// Reads the favicon that Chromium already downloaded for the current page.
    /// The PNG bytes are also persisted so bookmarks keep their icon after restart.
    /// </summary>
    public async Task<ImageSource?> GetAsync(CoreWebView2? core, string? pageUrl)
    {
        if (core is null || string.IsNullOrWhiteSpace(pageUrl)) return null;
        if (!TryGetAuthority(pageUrl, out var authority)) return null;

        if (_cache.TryGetValue(authority, out var cached) && cached is not null)
            return cached;

        try
        {
            var task = _webInflight.GetOrAdd(authority, _ => FetchFromWebViewAsync(core, authority));
            var result = await task;
            if (result is not null) return result;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Favicon from WebView failed for {authority}: {ex.Message}");
        }

        return LoadFromDisk(authority);
    }

    /// <summary>
    /// Resolves a favicon for a saved bookmark, using the in-memory/disk cache first
    /// and then, if necessary, the site's own /favicon.ico or &lt;link rel="icon"&gt;.
    /// </summary>
    public async Task<ImageSource?> GetForBookmarkAsync(string? pageUrl)
    {
        if (!TryGetAuthority(pageUrl, out var authority)) return null;

        if (_cache.TryGetValue(authority, out var cached)) return cached;
        var disk = LoadFromDisk(authority);
        if (disk is not null)
        {
            _cache[authority] = disk;
            return disk;
        }

        try
        {
            var task = _networkInflight.GetOrAdd(authority, _ => FetchFromWebAsync(authority));
            return await task;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Favicon download failed for {authority}: {ex.Message}");
            _networkInflight.TryRemove(authority, out _);
            _cache.TryAdd(authority, null);
            return null;
        }
    }

    private async Task<ImageSource?> FetchFromWebViewAsync(CoreWebView2 core, string authority)
    {
        try
        {
            await using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream is null) return null;
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();
            if (bytes.Length is 0 or > MaxFaviconBytes) return null;

            var image = Decode(bytes);
            if (image is null) return null;

            _cache[authority] = image;
            SaveToDisk(authority, bytes);
            return image;
        }
        finally
        {
            _webInflight.TryRemove(authority, out _);
        }
    }

    private async Task<ImageSource?> FetchFromWebAsync(string authority)
    {
        await _networkLimit.WaitAsync().ConfigureAwait(false);
        try
        {
            var scheme = authoritiesStartingWithLocalHost(authority) ? "http" : "https";
            var pageUri = new Uri($"{scheme}://{authority}/");

            var bytes = await TryDownloadAsync(new Uri(pageUri, "favicon.ico")).ConfigureAwait(false)
                        ?? await TryDownloadAsync(new Uri(pageUri, "apple-touch-icon.png")).ConfigureAwait(false);

            if (bytes is null)
            {
                var html = await TryDownloadHtmlAsync(pageUri).ConfigureAwait(false);
                var iconUri = html is null ? null : FindIconUri(html, pageUri);
                if (iconUri is not null)
                    bytes = await TryDownloadAsync(iconUri).ConfigureAwait(false);
            }

            if (bytes is null)
            {
                _cache.TryAdd(authority, null);
                return null;
            }

            var image = Decode(bytes);
            if (image is null)
            {
                _cache.TryAdd(authority, null);
                return null;
            }

            _cache[authority] = image;
            SaveToDisk(authority, bytes);
            return image;
        }
        finally
        {
            _networkLimit.Release();
            _networkInflight.TryRemove(authority, out _);
        }
    }

    private static async Task<byte[]?> TryDownloadAsync(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https")) return null;
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > MaxFaviconBytes) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return bytes.Length is > 0 and <= MaxFaviconBytes ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryDownloadHtmlAsync(Uri uri)
    {
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > 1_500_000) return null;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return null;
            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return html.Length > 1_500_000 ? html[..1_500_000] : html;
        }
        catch
        {
            return null;
        }
    }

    private static Uri? FindIconUri(string html, Uri pageUri)
    {
        foreach (Match tagMatch in LinkTagRegex.Matches(html))
        {
            var tag = tagMatch.Value;
            var rel = RelRegex.Match(tag);
            if (!rel.Success) continue;
            var relValue = rel.Groups["v"].Value;
            if (!relValue.Contains("icon", StringComparison.OrdinalIgnoreCase)) continue;

            var href = HrefRegex.Match(tag);
            if (!href.Success) continue;
            var value = WebUtility.HtmlDecode(href.Groups["v"].Value);
            if (Uri.TryCreate(pageUri, value, out var iconUri) &&
                iconUri.Scheme is "http" or "https")
            {
                return iconUri;
            }
        }
        return null;
    }

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var memory = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = memory;
            image.DecodePixelWidth = 32;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string CachePath(string authority)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(authority))).ToLowerInvariant();
        return Path.Combine(AppPaths.Favicons, hash[..24] + ".png");
    }

    private static void SaveToDisk(string authority, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Favicons);
            File.WriteAllBytes(CachePath(authority), bytes);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Favicon cache write failed: " + ex.Message);
        }
    }

    private static ImageSource? LoadFromDisk(string authority)
    {
        try
        {
            var path = CachePath(authority);
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            return bytes.Length is > 0 and <= MaxFaviconBytes ? Decode(bytes) : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetAuthority(string? pageUrl, out string authority)
    {
        authority = "";
        if (string.IsNullOrWhiteSpace(pageUrl)) return false;
        if (UrlHelper.IsInternalUrl(pageUrl) ||
            pageUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            pageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        authority = uri.Authority.ToLowerInvariant();
        return authority.Length > 0;
    }

    private static bool authoritiesStartingWithLocalHost(string authority)
        => authority.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
           authority.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
           authority.StartsWith("[::1]", StringComparison.OrdinalIgnoreCase);

    public void Invalidate(string? pageUrl)
    {
        if (TryGetAuthority(pageUrl, out var authority))
            _cache.TryRemove(authority, out _);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36 XRBrowser/1.0");
        return client;
    }
}
