using System.Collections.ObjectModel;
using ProseForge.Game.Presentation;
using ProseForge.Game.Themes;
using ProseForge.Game.ViewModels;

namespace ProseForge.Game;

// One book in the About list. Its cover shows from the downloaded file once there is one, from the
// URL until then, and not at all in compact mode.
public sealed class BookCard(ListedBook book, ImageSource? cover, bool showCover) : Observable
{
    private bool showCover = showCover && cover is not null;

    public ListedBook Book { get; } = book;
    public ImageSource? Cover { get; } = cover;
    public string CoverDescription => $"Cover of {Book.Title}";

    public bool ShowCover
    {
        get => showCover;
        set
        {
            if (Set(ref showCover, value && Cover is not null)) OnPropertyChanged(nameof(TaglineLines));
        }
    }

    // A card with its cover has room for more of the tagline.
    public int TaglineLines => showCover ? 4 : 2;
}

public partial class AboutPage : ContentPage
{
    private const string CompactKey = "about.compact";

    // The book about making this game has no public listing yet, so its card is written here.
    public const string GameBookTitle = "A Claude Agent and a Codex Agent Walk Into a Room...";
    public const string GameBookDescription =
        "A practical guide to vendor and model selection, told through building this game: a human, " +
        "a team of Claude and Codex agents, and who got each job, and why.";

    public const string AuthorBooksUrl = "https://claytonharbour.com/books";

    private readonly BookCatalogService catalogService;
    private string? featuredUrl;
    private bool compact;

    public ObservableCollection<BookCard> Books { get; } = [];

    public bool Compact
    {
        get => compact;
        set
        {
            if (compact == value) return;
            compact = value;
            Preferences.Default.Set(CompactKey, value);
            foreach (var card in Books) card.ShowCover = !value;
            OnPropertyChanged();
        }
    }

    public AboutPage(BookCatalogService catalogService, EarnedUnlock unlock, bool highlight = false)
    {
        InitializeComponent();
        this.catalogService = catalogService;
        compact = Preferences.Default.Get(CompactKey, false);   // covers shown unless the player chose compact
        BindingContext = this;
        // The art gallery: earned in the store game by 100 deliveries across every theme, or
        // switched on from the start by a build's features.json. A build without gallery/ (the public
        // copy) has nothing to show, so the button stays hidden.
        gallery = FeatureFlags.Load(AppThemes.Open).Gallery || unlock.Unlocked ? Gallery.Load(AppThemes.Open, []) : null;
        HowMadeButton.IsVisible = gallery is not null;
        this.highlight = highlight && gallery is not null;
    }

    private bool highlight;

    private readonly Gallery? gallery;

    private async void OnHowMadeClicked(object? sender, EventArgs e)
    {
        if (gallery is not null) await Navigation.PushAsync(new GalleryPage(gallery));
    }

    // Show what we already have at once; refresh in the background only if it's missing or stale.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (highlight) ShowUnlocked();
        catalogService.Updated += OnCatalogUpdated;
        var cached = await catalogService.GetCachedAsync();
        if (cached is not null) Show(cached);
        if (cached is null || catalogService.NeedsRefresh)
        {
            if (cached is null) SourceLabel.Text = "Loading books…";
            _ = catalogService.RefreshAsync();
        }
    }

    protected override void OnDisappearing()
    {
        catalogService.Updated -= OnCatalogUpdated;
        base.OnDisappearing();
    }

    // Arriving from the unlock: the button the player just earned is the brightest thing here.
    private async void ShowUnlocked()
    {
        highlight = false;
        HowMadeButton.SetDynamicResource(BackgroundColorProperty, "Accent");
        HowMadeButton.SetDynamicResource(Button.TextColorProperty, "OnAccent");
        HowMadeButton.Text = "Unlocked: how this game was made";
        if (Motion.Reduced) return;
        for (var i = 0; i < 3; i++)
        {
            await HowMadeButton.ScaleToAsync(1.08, 220, Easing.SinInOut);
            await HowMadeButton.ScaleToAsync(1, 220, Easing.SinInOut);
        }
    }

    private void OnCatalogUpdated() => MainThread.BeginInvokeOnMainThread(async () =>
    {
        if (await catalogService.GetCachedAsync() is { } result) Show(result);
    });

    private void Show(BookCatalogResult result)
    {
        Books.Clear();
        foreach (var book in result.Catalog.Books)
            Books.Add(new BookCard(book, CoverFor(book), !Compact));

        SourceLabel.Text = result.IsCached
            ? Books.Count == 0 ? "Books unavailable while offline." : $"{Books.Count} books · saved copy"
            : $"{Books.Count} books · updated from ProseForge";

        var featured = result.Catalog.FeaturedBook;
        featuredUrl = featured?.Slug is { } slug
            ? $"https://app.proseforge.ai/@clayton/{Uri.EscapeDataString(slug)}/read"
            : null;
        FeaturedLink.IsVisible = featuredUrl is not null;
        FeaturedStatus.IsVisible = featuredUrl is null;
    }

    private ImageSource? CoverFor(ListedBook book) =>
        catalogService.CoverFile(book.CoverUrl) is { } file ? ImageSource.FromFile(file)
        : book.CoverUrl is { } url ? new UriImageSource { Uri = new Uri(url), CacheValidity = TimeSpan.FromDays(7) }
        : null;

    private async void OnLicencesClicked(object? sender, EventArgs e) => await Navigation.PushAsync(new LicencesPage());

    private async void OnAuthorTapped(object? sender, TappedEventArgs e) =>
        await Launcher.Default.OpenAsync(AuthorBooksUrl);

    private async void OnFeaturedClicked(object? sender, EventArgs e)
    {
        if (featuredUrl is not null) await Launcher.Default.OpenAsync(featuredUrl);
    }

    private async void OnBookClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string url })
            await Launcher.Default.OpenAsync(url);
    }
}
