using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Orbit.Engine;
using ProseForge.Game.Themes;
using ProseForge.Game.ViewModels;

namespace ProseForge.Game;

// The page only does what a view model can't: run the frame timer, lay out one row and one hire
// button per station, and drive the Orbit scene. Everything else is bound.
public partial class MainPage : ContentPage
{
    private readonly GameViewModel model;
    private readonly ThemeHost themes;
    private readonly BookCatalogService catalogService;
    private readonly IGameSceneManager sceneManager;
    private readonly EarnedUnlock unlock;
    private readonly IDispatcherTimer timer;

    public MainPage(GameViewModel model, ThemeHost themes, BookCatalogService catalogService, IGameSceneManager sceneManager,
        EarnedUnlock unlock)
    {
        InitializeComponent();
        this.model = model;
        this.unlock = unlock;
        this.themes = themes;
        this.catalogService = catalogService;
        this.sceneManager = sceneManager;
        BindingContext = model;
        NavigationPage.SetHasNavigationBar(this, false);
        LayOutStations(model.Stations.Count);
        PowerUpColumns.ColumnDefinitions = [.. model.PowerUps.Select(_ => new ColumnDefinition(GridLength.Star))];
        ThemeButton.IsVisible = themes.Choices.Count > 1;
        sceneManager.LoadScene<GuildHallScene>(GameView);
        model.PropertyChanged += OnModelChanged;
        model.Unlocked += OnUnlocked;
        model.LevelReached += OnLevelReached;
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += (_, _) => model.Frame();
    }

    // A theme decides how many stations there are; the grids follow.
    private void LayOutStations(int count)
    {
        StationRows.RowDefinitions = [.. Enumerable.Range(0, count).Select(_ => new RowDefinition(GridLength.Star))];
        HireColumns.ColumnDefinitions = [.. Enumerable.Range(0, count).Select(_ => new ColumnDefinition(GridLength.Star))];
    }

    // A level reached: a small shake, and the dock's count (now carrying the level's name)
    // gives a short bounce. Reduce Motion gets just the message.
    private async void OnLevelReached()
    {
        if (Motion.Reduced) return;
        foreach (var x in new[] { -6.0, 6, -3, 0 })
            await Root.TranslateToAsync(x, 0, 40);
        await DeliveredLabel.ScaleToAsync(1.25, 160, Easing.CubicOut);
        await DeliveredLabel.ScaleToAsync(1, 260, Easing.SpringOut);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameViewModel.Paused)) return;
        if (model.Paused) sceneManager.Pause(); else sceneManager.Start();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        model.Resume();
        timer.Start();
        if (!model.Paused) sceneManager.Start();
    }

    protected override void OnDisappearing()
    {
        PauseForBackground();
        base.OnDisappearing();
    }

    public void PauseForBackground()
    {
        timer.Stop();
        sceneManager.Pause();
        model.PauseForBackground();
    }

    public void Foreground()
    {
        model.Resume();
        timer.Start();
        if (!model.Paused) sceneManager.Start();
    }

    private async void OnAboutClicked(object? sender, EventArgs e) => await OpenAbout(highlight: false);

    private Task OpenAbout(bool highlight) => Navigation.PushAsync(new AboutPage(catalogService, unlock, highlight));

    // The earned unlock. Big on purpose: nobody should miss it. Reduce Motion gets the same
    // card without the shake, the flash or the flight into the About button.
    private async void OnUnlocked()
    {
        var calm = Motion.Reduced;
        UnlockCard.Shadow = calm ? null : new Shadow
        {
            Brush = new SolidColorBrush(Color.FromArgb(model.Theme.Palette.Accent)), Radius = 48, Opacity = 0.95f,
            Offset = new Point(0, 0),
        };
        UnlockCard.Opacity = 0;
        UnlockCard.Scale = calm ? 1 : 0.3;
        UnlockCard.TranslationX = UnlockCard.TranslationY = 0;
        UnlockOverlay.IsVisible = true;
        if (calm)
        {
            await UnlockCard.FadeToAsync(1, 200);
            return;
        }

        UnlockFlash.IsVisible = true;
        async Task Flash()
        {
            for (var i = 0; i < 3; i++)
            {
                await UnlockFlash.FadeToAsync(0.6, 110);
                await UnlockFlash.FadeToAsync(0, 190);
            }
        }
        var flash = Flash();
        foreach (var x in new[] { -16.0, 16, -12, 12, -8, 8, -4, 0 })
            await Root.TranslateToAsync(x, 0, 45);
        await Task.WhenAll(flash, UnlockCard.FadeToAsync(1, 180), UnlockCard.ScaleToAsync(1, 420, Easing.SpringOut));
        UnlockFlash.IsVisible = false;
        // A slow glow while the card waits for a tap.
        for (var i = 0; i < 3 && UnlockOverlay.IsVisible; i++)
        {
            await UnlockCard.ScaleToAsync(1.04, 420, Easing.SinInOut);
            await UnlockCard.ScaleToAsync(1, 420, Easing.SinInOut);
        }
    }

    private async void OnUnlockShow(object? sender, EventArgs e)
    {
        await CloseUnlock();
        await OpenAbout(highlight: true);
    }

    private async void OnUnlockLater(object? sender, EventArgs e)
    {
        await CloseUnlock();
        if (Motion.Reduced) return;
        // Point at where it went.
        for (var i = 0; i < 3; i++)
        {
            await AboutButton.ScaleToAsync(1.35, 180, Easing.CubicOut);
            await AboutButton.ScaleToAsync(1, 180, Easing.CubicIn);
        }
    }

    // The card flies into the About button and is gone: that's where the room now lives.
    private async Task CloseUnlock()
    {
        if (!Motion.Reduced)
        {
            var from = Centre(UnlockCard);
            var to = Centre(AboutButton);
            await Task.WhenAll(
                UnlockCard.TranslateToAsync(to.X - from.X, to.Y - from.Y, 380, Easing.CubicIn),
                UnlockCard.ScaleToAsync(0.05, 380, Easing.CubicIn),
                UnlockCard.FadeToAsync(0.2, 380));
        }
        UnlockOverlay.IsVisible = false;
    }

    // Where a view's middle is on the page, without its current translation.
    private static Point Centre(VisualElement view)
    {
        double x = view.Width / 2, y = view.Height / 2;
        for (Element? e = view; e is VisualElement v && e is not Page; e = e.Parent)
        {
            x += v.X;
            y += v.Y;
        }
        return new Point(x, y);
    }

    // The picker shows every bundled theme as a card; choosing one swaps the whole game in place.
    private async void OnThemeClicked(object? sender, EventArgs e)
    {
        model.OpenPicker();
        PickerFrame.Stroke = Gilded(model.Theme.Palette);
        ThemeCards.Children.Clear();
        foreach (var choice in themes.Choices) ThemeCards.Children.Add(Card(choice));
        PickerFrame.Opacity = 0;
        PickerFrame.Scale = 0.94;
        ThemePicker.IsVisible = true;
        await Task.WhenAll(PickerFrame.FadeToAsync(1, 160), PickerFrame.ScaleToAsync(1, 160, Easing.CubicOut));
    }

    private void OnThemePickerClose(object? sender, EventArgs e)
    {
        ThemePicker.IsVisible = false;
        model.ClosePicker();
    }

    private async void Pick(string id)
    {
        ThemePicker.IsVisible = false;
        if (id == model.Theme.Id)
            model.ClosePicker();
        else if (themes.Switch(id) is { } problem)
        {
            model.ClosePicker();
            await DisplayAlertAsync("That theme won't load", problem, "OK");
        }
    }

    // A frame in a theme's own metals: its accent, darkening to its panel edge and back, which
    // reads as gold leaf on the guild's palette.
    private static LinearGradientBrush Gilded(Themes.Palette p) => new(
        [new GradientStop(Color.FromArgb(p.Accent), 0f), new GradientStop(Color.FromArgb(p.PanelStroke), 0.5f),
         new GradientStop(Color.FromArgb(p.Accent), 1f)],
        new Point(0, 0), new Point(1, 1));

    private View Card(Themes.ThemeChoice choice)
    {
        var p = choice.Palette;
        var current = choice.Id == model.Theme.Id;
        var grid = new Grid { HeightRequest = 96 };
        if (choice.Background != Themes.ThemePacks.MissingArt)
            grid.Add(new Image { Source = Themes.AppThemes.Image(choice.Background), Aspect = Aspect.AspectFill, Opacity = 0.7 });
        // A band of the theme's own panel colour keeps the title readable over any picture.
        grid.Add(new BoxView
        {
            VerticalOptions = LayoutOptions.End, HeightRequest = 48,
            Background = new LinearGradientBrush(
                [new GradientStop(Colors.Transparent, 0f), new GradientStop(Color.FromArgb(p.Panel), 0.6f)],
                new Point(0, 0), new Point(0, 1)),
        });
        grid.Add(new Label
        {
            Text = choice.Title, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(p.Text),
            VerticalOptions = LayoutOptions.End, Margin = new Thickness(14, 0, 14, 10),
        });
        if (current)
            grid.Add(new Border
            {
                HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Margin = 10,
                Padding = new Thickness(10, 3), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 },
                BackgroundColor = Color.FromArgb(p.Accent),
                Content = new Label { Text = "Playing now", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(p.OnAccent) },
            });

        var card = new Border
        {
            Content = grid, Padding = 0, StrokeThickness = current ? 5 : 3, Stroke = Gilded(p),
            StrokeShape = new RoundRectangle { CornerRadius = 16 }, BackgroundColor = Color.FromArgb(p.Background),
        };
        card.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Pick(choice.Id)) });
        SemanticProperties.SetDescription(card, current ? $"{choice.Title}, playing now" : $"Switch to {choice.Title}");
        return card;
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null)
        {
            timer.Stop();
            model.PropertyChanged -= OnModelChanged;
            model.Unlocked -= OnUnlocked;
        }
        base.OnHandlerChanging(args);
    }
}
