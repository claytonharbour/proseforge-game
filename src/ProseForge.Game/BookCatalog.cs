using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProseForge.Game;

// Amazon's own "Write a review" page for one store, built from the ASIN in a live listing.
public sealed record ReviewLink(string Store, string Url);

public sealed record ListedBook(string Title, string? Tagline, string? Slug, string? StoreUrl,
    IReadOnlyList<ReviewLink>? Reviews = null, string? CoverUrl = null)
{
    public bool HasStoreUrl => StoreUrl is not null;
    public bool HasCover => CoverUrl is not null;

    public string? ReviewUrl => ReviewUrlFor(RegionInfo.CurrentRegion.TwoLetterISORegionName);
    public bool HasReviewUrl => ReviewUrl is not null;

    // One button, not one per store: Canada prefers amazon.ca, everywhere else amazon.com, and
    // either falls back to the other store when its own has no listing.
    public string? ReviewUrlFor(string? region)
    {
        if (Reviews is not { Count: > 0 }) return null;
        var preferred = string.Equals(region, "CA", StringComparison.OrdinalIgnoreCase) ? "amazon_ca" : "amazon";
        return (Reviews.FirstOrDefault(review => review.Store == preferred) ?? Reviews[0]).Url;
    }
}

public sealed record BookCatalog(IReadOnlyList<ListedBook> Books)
{
    // Set this only after the game-development book has a real public listing.
    public const string? GameBookSlug = null;

    public ListedBook? FeaturedBook => FindFeatured(GameBookSlug);

    public ListedBook? FindFeatured(string? slug) => string.IsNullOrWhiteSpace(slug) ? null :
        Books.FirstOrDefault(book => string.Equals(book.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static BookCatalog Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("books", out var books) ||
            books.ValueKind != JsonValueKind.Array)
            throw new JsonException("The author listings response has no books array.");

        var parsed = new List<ListedBook>();
        foreach (var item in books.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var title = ReadString(item, "title");
            if (string.IsNullOrWhiteSpace(title)) continue;
            var url = item.TryGetProperty("listings", out var listings) &&
                      listings.ValueKind == JsonValueKind.Array
                ? listings.EnumerateArray().Select(listing => ReadString(listing, "url"))
                    .FirstOrDefault(IsSafeStoreUrl)
                : null;
            var cover = ReadString(item, "coverUrl");
            parsed.Add(new ListedBook(title.Trim(), ReadString(item, "tagline"),
                ReadString(item, "slug"), url, ReviewLinks(item), IsSafeStoreUrl(cover) ? cover : null));
        }

        return new BookCatalog(parsed);
    }

    // Mirrors the website's bookReviews.ts: one link per store, from the first live edition in
    // ebook, paperback, hardcover, audiobook order whose URL carries an ASIN on that store's host.
    private static readonly (string Store, string Host)[] ReviewStores =
        [("amazon", "www.amazon.com"), ("amazon_ca", "www.amazon.ca")];
    private static readonly string[] FormatOrder = ["ebook", "paperback", "hardcover", "audiobook"];
    private static readonly Regex AsinPath = new(@"^/(?:dp|gp/product)/([A-Z0-9]{10})(?:/|$)");

    private static IReadOnlyList<ReviewLink> ReviewLinks(JsonElement book)
    {
        if (!book.TryGetProperty("listings", out var listings) || listings.ValueKind != JsonValueKind.Array)
            return [];
        var links = new List<ReviewLink>();
        foreach (var (store, host) in ReviewStores)
        {
            var asin = listings.EnumerateArray()
                .Where(listing => ReadString(listing, "store") == store && ReadString(listing, "status") == "live")
                .OrderBy(listing => FormatRank(ReadString(listing, "format")))
                .Select(listing => AsinFrom(ReadString(listing, "url"), host))
                .FirstOrDefault(found => found is not null);
            if (asin is not null)
                links.Add(new ReviewLink(store, $"https://{host}/review/create-review?asin={asin}"));
        }
        return links;
    }

    private static int FormatRank(string? format)
    {
        var rank = Array.IndexOf(FormatOrder, format);
        return rank < 0 ? FormatOrder.Length : rank;
    }

    // Only a URL on that store's own Amazon host gives an ASIN; anything else gives nothing.
    internal static string? AsinFrom(string? url, string host)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.Host != host && uri.Host != host["www.".Length..]) return null;
        var match = AsinPath.Match(uri.AbsolutePath);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool IsSafeStoreUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
