using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Eluna.App.Services;

public sealed record WebResult(string Title, string Url, string Snippet, string Provider);
public sealed record WebSearchResult(string Query, List<WebResult> Results, List<string> Warnings);
public sealed record WebDocument(string Title, string Url, string Text, DateTimeOffset RetrievedUtc, string? Published = null, List<WebResult>? Links = null);

public sealed class WebSearchService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Dictionary<string, WebDocument> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset Time, WebSearchResult Result)> _searchCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _pages = new(3, 3);
    private const int MaxBytes = 2 * 1024 * 1024;
    public WebSearchService(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = false,
            ConnectCallback = ConnectPublicAsync
        })
        { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Eluna/0.1.0 (+https://github.com/SUIREN-KazutoHashimoto/Eluna)");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) { address = address.MapToIPv4(); }
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) { return false; }
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 || bytes[0] == 169 && bytes[1] == 254 ||
                bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168 ||
                bytes[0] == 100 && bytes[1] is >= 64 and <= 127 || bytes[0] == 198 && bytes[1] is 18 or 19 ||
                bytes[0] == 192 && bytes[1] == 0 || bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
                bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }
        return (bytes[0] & 0xE0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a))) { throw new HttpRequestException("公開ネットワーク以外の接続先は取得できません。"); }
        Exception? lastError = null;
        foreach (IPAddress address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token); return new NetworkStream(socket, true); }
            catch (Exception ex) when (ex is SocketException or IOException) { lastError = ex; socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("ページの接続先に到達できませんでした。", lastError);
    }

    private static Uri PublicUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 ||
            uri.Port is not (80 or 443) || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? address) && !IsPublicAddress(address))
        {
            throw new InvalidDataException("公開HTTP / HTTPSページだけを取得できます。");
        }
        return uri;
    }

    private async Task<(string Content, Uri Url, string MediaType)> ReadAsync(string url, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        CancellationToken requestToken = timeout.Token;
        Uri uri = PublicUri(url);
        for (int redirects = 0; redirects < 6; redirects++)
        {
            using HttpResponseMessage response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is Uri location)
            {
                uri = PublicUri(new Uri(uri, location).AbsoluteUri); continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes) { throw new InvalidDataException("ページが大きすぎます（上限2 MB）。"); }
            await using Stream source = await response.Content.ReadAsStreamAsync(requestToken);
            using var output = new MemoryStream();
            byte[] buffer = new byte[32 * 1024];
            int count;
            while ((count = await source.ReadAsync(buffer, requestToken)) > 0)
            {
                if (output.Length + count > MaxBytes) { throw new InvalidDataException("ページが大きすぎます（上限2 MB）。"); }
                output.Write(buffer, 0, count);
            }
            string? charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
            if (charset is null)
            {
                string prefix = Encoding.ASCII.GetString(output.ToArray(), 0, (int)Math.Min(output.Length, 4096));
                Match declared = Regex.Match(prefix, @"charset\s*=\s*[""']?(?<name>[A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
                charset = declared.Success ? declared.Groups["name"].Value : "utf-8";
            }
            Encoding encoding;
            try { encoding = Encoding.GetEncoding(charset); } catch (ArgumentException) { encoding = Encoding.UTF8; }
            return (encoding.GetString(output.ToArray()), uri, response.Content.Headers.ContentType?.MediaType ?? "");
        }
        throw new HttpRequestException("ページの転送が多すぎます。");
    }

    public Task<WebSearchResult> SearchAsync(string query, int limit, CancellationToken token) => Task.Run(() => SearchCoreAsync(query, limit, token), token);
    private async Task<WebSearchResult> SearchCoreAsync(string query, int limit, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query)) { return new(query, [], []); }
        token.ThrowIfCancellationRequested();
        string cacheKey = query.Trim() + "/" + limit;
        lock (_searchCache) { if (_searchCache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromMinutes(5)) { return cached.Result with { Results = [.. cached.Result.Results], Warnings = [.. cached.Result.Warnings] }; } }
        string encoded = Uri.EscapeDataString(query.Trim());
        async Task<(List<WebResult> Results, string? Warning)> Provider(string name, string url, Func<string, List<WebResult>> parse)
        {
            try { var page = await ReadAsync(url, token); return (parse(page.Content), null); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return ([], name + ": タイムアウトしました"); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or XmlException or RegexMatchTimeoutException)
            {
                string detail = ex switch { InvalidDataException => ex.Message, HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "検索先の取得制限", HttpRequestException => "通信に失敗しました", _ => "検索結果を読み取れませんでした" };
                return ([], name + ": " + detail);
            }
        }
        var results = await Task.WhenAll(
            Provider("Bing", "https://www.bing.com/search?q=" + encoded + "&format=rss", ParseBing),
            Provider("DuckDuckGo", "https://lite.duckduckgo.com/lite/?q=" + encoded, ParseDuckDuckGo),
            Provider("Wikipedia", "https://ja.wikipedia.org/w/api.php?action=query&list=search&srsearch=" + encoded + "&format=json", ParseWikipedia));
        var distinct = new Dictionary<string, WebResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in results)
        {
            foreach (WebResult result in provider.Results)
            {
                try { string key = Canonical(PublicUri(result.Url)); distinct.TryAdd(key, result); } catch (InvalidDataException) { }
            }
        }
        // Interleave providers so one provider cannot occupy the entire result budget.
        List<WebResult> ordered = distinct.Values.GroupBy(r => r.Provider).SelectMany(g => g.Select((r, index) => (r, index)))
            .OrderBy(x => x.index).Select(x => x.r).Take(Math.Clamp(limit, 1, 50)).ToList();
        var combined = new WebSearchResult(query, ordered, results.Where(r => r.Warning is not null).Select(r => r.Warning!).ToList());
        if (ordered.Count > 0)
        {
            lock (_searchCache) { if (_searchCache.Count >= 64) { _searchCache.Remove(_searchCache.MinBy(p => p.Value.Time).Key); } _searchCache[cacheKey] = (DateTimeOffset.UtcNow, combined); }
        }
        return combined;
    }

    internal static List<WebResult> ParseBing(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        XDocument document = XDocument.Load(reader);
        return document.Descendants("item").Select(item => new WebResult(item.Element("title")?.Value ?? "", item.Element("link")?.Value ?? "",
            PlainText(item.Element("description")?.Value ?? ""), "Bing")).Where(r => r.Url.Length > 0).ToList();
    }

    internal static List<WebResult> ParseDuckDuckGo(string html)
    {
        if (html.Contains("anomaly-modal", StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("検索先がアクセス確認を要求しました。"); }
        var results = new List<WebResult>();
        foreach (Match anchor in Regex.Matches(html, @"<a\b(?<attrs>[^>]*)>(?<text>[\s\S]*?)</a>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500)))
        {
            string classes = Attribute(anchor.Groups["attrs"].Value, "class");
            if (!classes.Contains("result-link", StringComparison.OrdinalIgnoreCase) && !classes.Contains("result__a", StringComparison.OrdinalIgnoreCase)) { continue; }
            string url = Attribute(anchor.Groups["attrs"].Value, "href");
            if (url.StartsWith("//", StringComparison.Ordinal)) { url = "https:" + url; }
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
            {
                Match redirected = Regex.Match(uri.Query, @"(?:^\?|&)uddg=([^&]+)");
                if (redirected.Success) { url = Uri.UnescapeDataString(redirected.Groups[1].Value); }
            }
            results.Add(new(PlainText(anchor.Groups["text"].Value), url, "", "DuckDuckGo"));
        }
        return results;
    }

    internal static List<WebResult> ParseWikipedia(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out JsonElement query) || !query.TryGetProperty("search", out JsonElement results)) { return []; }
        return results.EnumerateArray().Select(item => new WebResult(item.GetProperty("title").GetString() ?? "",
            "https://ja.wikipedia.org/?curid=" + item.GetProperty("pageid").GetInt64(), PlainText(item.GetProperty("snippet").GetString() ?? ""), "Wikipedia")).ToList();
    }

    internal static string Attribute(string attributes, string name)
    {
        string pattern = @"(?:^|\s)" + Regex.Escape(name) + @"\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))";
        return WebUtility.HtmlDecode(Regex.Match(attributes, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)).Groups["value"].Value);
    }

    internal static string PlainText(string html)
    {
        string value = Regex.Replace(html, @"<(script|style|noscript|svg)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        // Remove page-local citation markers before exposing the page as model knowledge.
        // Preserve mathematical superscripts and ordinary bracketed data.
        value = Regex.Replace(value, @"<(sup|a)\b(?<attrs>[^>]*)>[\s\S]*?</\1>", match =>
        {
            string attrs = match.Groups["attrs"].Value;
            return Attribute(attrs, "class").Split(' ').Contains("reference", StringComparer.OrdinalIgnoreCase) ||
                Attribute(attrs, "id").StartsWith("cite_ref", StringComparison.OrdinalIgnoreCase) ||
                Attribute(attrs, "href").StartsWith("#cite_note", StringComparison.OrdinalIgnoreCase) ? "" : match.Value;
        }, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        value = Regex.Replace(value, @"</?(?:p|div|h[1-6]|li|tr|section|article|br)\b[^>]*>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        value = Regex.Replace(value, @"<[^>]+>", "", RegexOptions.None, TimeSpan.FromMilliseconds(500));
        value = WebUtility.HtmlDecode(value);
        value = Regex.Replace(value, @"[^\S\n]+", " ");
        return Regex.Replace(value, @"\n\s*\n+", "\n\n").Trim();
    }

    internal static string Canonical(Uri uri)
    {
        var builder = new UriBuilder(uri) { Fragment = "" };
        builder.Query = string.Join('&', uri.Query.TrimStart('?').Split('&').Where(q => !q.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)));
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    public Task<WebDocument> FetchAsync(WebResult result, CancellationToken token) => Task.Run(() => FetchCoreAsync(result, token), token);
    private async Task<WebDocument> FetchCoreAsync(WebResult result, CancellationToken token)
    {
        string key = Canonical(PublicUri(result.Url));
        token.ThrowIfCancellationRequested();
        lock (_cache) { if (_cache.TryGetValue(key, out WebDocument? cached) && DateTimeOffset.UtcNow - cached.RetrievedUtc < TimeSpan.FromMinutes(30)) { return cached; } }
        await _pages.WaitAsync(token);
        try
        {
            var page = await ReadAsync(result.Url, token);
            if (page.MediaType.Length > 0 && page.MediaType is not ("text/html" or "application/xhtml+xml" or "text/plain" or "text/xml" or "application/xml"))
            {
                throw new InvalidDataException("このページ形式の本文取得には対応していません。");
            }
            string html = page.Content;
            Match main = Regex.Match(html, @"<article\b[^>]*>(?<body>[\s\S]*?)</article>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
            if (!main.Success) { main = Regex.Match(html, @"<main\b[^>]*>(?<body>[\s\S]*?)</main>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500)); }
            string text = page.MediaType == "text/plain" ? html.Trim() : PlainText(main.Success ? main.Groups["body"].Value : html);
            if (text.Length < 40) { throw new InvalidDataException("本文を取得できませんでした。"); }
            if (text.Length > 60000) { text = text[..60000]; }
            string title = PlainText(Regex.Match(html, @"<title\b[^>]*>(?<title>[\s\S]*?)</title>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500)).Groups["title"].Value);
            string? published = null;
            foreach (Match meta in Regex.Matches(html, @"<meta\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)))
            {
                string attributes = meta.Groups["attributes"].Value;
                string name = Attribute(attributes, "property") + Attribute(attributes, "name");
                if (name is "article:published_time" or "date" or "datePublished" or "pubdate") { published = Attribute(attributes, "content"); break; }
            }
            var document = new WebDocument(title.Length > 0 ? title : result.Title, page.Url.AbsoluteUri, text, DateTimeOffset.UtcNow, published,
                page.MediaType == "text/plain" ? [] : ExtractLinks(html, page.Url));
            lock (_cache) { if (_cache.Count >= 100) { _cache.Remove(_cache.MinBy(p => p.Value.RetrievedUtc).Key); } _cache[key] = document; }
            return document;
        }
        finally { _pages.Release(); }
    }
    internal static List<WebResult> ExtractLinks(string html, Uri page)
    {
        var links = new List<WebResult>();
        void Add(string href, string title, string snippet)
        {
            if (href.Length == 0 || href.StartsWith('#') || !Uri.TryCreate(page, WebUtility.HtmlDecode(href), out Uri? uri)) { return; }
            try { uri = PublicUri(uri.AbsoluteUri); } catch (InvalidDataException) { return; }
            string key = Canonical(uri);
            if (key == Canonical(page) || links.Any(l => Canonical(new Uri(l.Url)) == key)) { return; }
            links.Add(new(title.Length > 0 ? title : uri.AbsoluteUri, uri.AbsoluteUri, snippet, "link"));
        }
        Match parent = Regex.Match(html, @"forked\s+from[\s\S]{0,1000}?<a\b(?<attributes>[^>]*)>(?<text>[\s\S]*?)</a>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(300));
        if (parent.Success) { Add(Attribute(parent.Groups["attributes"].Value, "href"), PlainText(parent.Groups["text"].Value), "ページが参照元として示したリポジトリ。原典の候補です。"); }
        Match article = Regex.Match(html, @"<article\b[^>]*>(?<body>[\s\S]*?)</article>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(300));
        string body = article.Success ? article.Groups["body"].Value : html;
        foreach (Match link in Regex.Matches(body, @"<a\b(?<attributes>[^>]*)>(?<text>[\s\S]*?)</a>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500)).Take(1000))
        {
            if (links.Count >= 80) { break; }
            string title = PlainText(link.Groups["text"].Value); if (title.Length is 0 or > 200) { continue; }
            Add(Attribute(link.Groups["attributes"].Value, "href"), title, "閲覧したページの参照リンク。");
        }
        return links;
    }
    public void Dispose() { if (_ownsHttp) { _http.Dispose(); } _pages.Dispose(); }
}
