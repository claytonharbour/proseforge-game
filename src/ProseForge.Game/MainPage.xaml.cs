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
    private readonly IDispatcherTimer timer;

    public MainPage(GameViewModel model, ThemeHost themes, BookCatalogService catalogService, IGameSceneManager sceneManager)
    {
        InitializeComponent();
        this.model = model;
        this.themes = themes;
        this.catalogService = catalogService;
        this.sceneManager = sceneManager;
        BindingContext = model;
        NavigationPage.SetHasNavigationBar(this, false);
        LayOutStations(model.Stations.Count);
        ThemeButton.IsVisible = themes.Choices.Count > 1;
        sceneManager.LoadScene<GuildHallScene>(GameView);
        model.PropertyChanged += OnModelChanged;
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

    private async void OnAboutClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new AboutPage(catalogService));

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
        }
        base.OnHandlerChanging(args);
    }
}
