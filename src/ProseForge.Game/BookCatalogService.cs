using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProseForge.Game;

public sealed record BookCatalogResult(BookCatalog Catalog, bool IsCached);

// The author's books for the About page. The listings API is slow (about 1.3 s, no caching
// headers), so the app fetches it in the background and About shows the
// last good copy at once: from memory, else from disk, refreshing only when it's stale.
public sealed class BookCatalogService
{
    private const string FeedUrl = "https://app.proseforge.ai/api/v1/author/clayton/listings";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
    private const int CoverDownloads = 3;

    private readonly HttpClient client;
    private readonly string cachePath;
    private readonly string etagPath;
    private readonly string coverDirectory;
    private readonly SemaphoreSlim refreshing = new(1, 1);
    private BookCatalogResult? latest;
    private DateTimeOffset? fetchedAt;

    public BookCatalogService(HttpClient client, string cacheDirectory)
    {
        this.client = client;
        cachePath = Path.Combine(cacheDirectory, "author-listings.json");
        etagPath = Path.Combine(cacheDirectory, "author-listings.etag");
        coverDirectory = Path.Combine(cacheDirectory, "covers");
    }

    // Raised after a refresh brings in a newer list, or a cover finishes downloading.
    public event Action? Updated;

    // Whether a list fetched at this time should be refreshed now.
    public static bool IsStale(DateTimeOffset? fetched, DateTimeOffset now) =>
        fetched is not { } at || now - at >= StaleAfter;

    public bool NeedsRefresh => IsStale(fetchedAt, DateTimeOffset.UtcNow);

    // Whatever we have without touching the network: memory, else the copy on disk. Null if neither.
    public async Task<BookCatalogResult?> GetCachedAsync(CancellationToken cancellationToken = default)
    {
        if (latest is not null) return latest;
        var cached = await ReadCacheAsync(cancellationToken);
        if (cached is null) return null;
        fetchedAt ??= File.GetLastWriteTimeUtc(cachePath);
        return latest = new BookCatalogResult(cached, true);
    }

    // Fetch from the server (one fetch at a time), then download any covers not yet on disk.
    public async Task<BookCatalogResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshing.WaitAsync(cancellationToken);
        try
        {
            latest = await LoadAsync(cancellationToken);
            if (!latest.IsCached) fetchedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            refreshing.Release();
        }
        Updated?.Invoke();
        _ = DownloadCoversAsync(latest.Catalog, cancellationToken);
        return latest;
    }

    public async Task<BookCatalogResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        var cached = await ReadCacheAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);
            if (cached is not null && File.Exists(etagPath))
            {
                var etag = await File.ReadAllTextAsync(etagPath, cancellationToken);
                if (EntityTagHeaderValue.TryParse(etag, out var parsedTag))
                    request.Headers.IfNoneMatch.Add(parsedTag);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
                return new BookCatalogResult(cached, false);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var catalog = BookCatalog.Parse(json); // Validate before replacing a good cache.
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var temporaryPath = cachePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, cachePath, true);
            if (response.Headers.ETag is { } tag)
                await File.WriteAllTextAsync(etagPath, tag.ToString(), cancellationToken);
            else if (File.Exists(etagPath))
                File.Delete(etagPath);
            return new BookCatalogResult(catalog, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return new BookCatalogResult(cached ?? new BookCatalog([]), true);
        }
    }

    // A cover's file on disk once downloaded, else null (show the URL meanwhile).
    public string? CoverFile(string? url)
    {
        if (url is null) return null;
        var path = Path.Combine(coverDirectory, CoverFileName(url));
        return File.Exists(path) ? path : null;
    }

    // One file per cover URL: a hash of the URL, so a changed cover is a new file and no URL can
    // reach outside the cover folder.
    public static string CoverFileName(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32].ToLowerInvariant();
        var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        return hash + (extension is ".png" or ".jpg" or ".jpeg" or ".webp" ? extension : ".img");
    }

    private async Task DownloadCoversAsync(BookCatalog catalog, CancellationToken cancellationToken)
    {
        var missing = catalog.Books.Select(book => book.CoverUrl).OfType<string>().Distinct()
            .Where(url => CoverFile(url) is null).ToList();
        if (missing.Count == 0) return;
        Directory.CreateDirectory(coverDirectory);
        using var gate = new SemaphoreSlim(CoverDownloads);
        var any = false;
        await Task.WhenAll(missing.Select(async url =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var bytes = await client.GetByteArrayAsync(url, cancellationToken);
                var path = Path.Combine(coverDirectory, CoverFileName(url));
                await File.WriteAllBytesAsync(path + ".tmp", bytes, cancellationToken);
                File.Move(path + ".tmp", path, true);
                any = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                // A cover that won't download just keeps showing from its URL.
            }
            finally
            {
                gate.Release();
            }
        }));
        if (any) Updated?.Invoke();
    }

    private async Task<BookCatalog?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cachePath)) return null;
            return BookCatalog.Parse(await File.ReadAllTextAsync(cachePath, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
