using System.Diagnostics;
using Orbit.Engine;
using Plugin.Maui.Audio;

namespace ProseForge.Game;

public partial class MainPage : ContentPage
{
    private readonly GuildGameState game;
    private readonly GuildStateStore store;
    private readonly IGameSceneManager sceneManager;
    private readonly IAudioManager audioManager;
    private readonly IDispatcherTimer timer;
    private IAudioPlayer? audioPlayer;
    private long lastTick;
    private int saveTicks;
    private bool recruitExpanded;

    private void OnRecruitTabClicked(object? sender, EventArgs e)
    {
        recruitExpanded = !recruitExpanded;
        RecruitPanel.IsVisible = recruitExpanded;
        RecruitTabButton.Text = recruitExpanded ? "✕ Close recruitment · swipe down" : "Recruit your guild";
        RecruitTabButton.BackgroundColor = Color.FromArgb(recruitExpanded ? "#A7824F" : "#34403C");
        RecruitTabButton.TextColor = Color.FromArgb(recruitExpanded ? "#101A1B" : "#F7EEDC");
    }

    private void OnRecruitSwipeDown(object? sender, SwipedEventArgs e)
    {
        if (recruitExpanded) OnRecruitTabClicked(sender, EventArgs.Empty);
    }

    public MainPage(GuildGameState game, GuildStateStore store,
        IGameSceneManager sceneManager, IAudioManager audioManager)
    {
        InitializeComponent();
        this.game = game;
        this.store = store;
        this.sceneManager = sceneManager;
        this.audioManager = audioManager;
        sceneManager.LoadScene<GuildHallScene>(GameView);
        GameView.StartInteraction += OnGameViewStartInteraction;
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += OnTick;
        Refresh();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        lastTick = Stopwatch.GetTimestamp();
        timer.Start();
        if (!game.Paused) sceneManager.Start();
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
        game.PauseForBackground();
        store.Save(game);
        audioPlayer?.Pause();
        Refresh();
    }

    public void Foreground()
    {
        lastTick = Stopwatch.GetTimestamp();
        timer.Start();
        if (!game.Paused) sceneManager.Start();
        Refresh();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(lastTick, now).TotalSeconds;
        lastTick = now;
        if (!game.CanAct) return;
        game.Tick(elapsed);
        if (++saveTicks >= 10)
        {
            store.Save(game);
            saveTicks = 0;
        }
        Refresh();
        GameView.Invalidate();
    }

    private void OnGameViewStartInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || !GuildHallScene.IsWorkbenchHit(
            e.Touches[0], (float)GameView.Width, (float)GameView.Height))
            return;
        Collect();
    }

    private void Collect() => Perform(game.Collect, "Fresh manuscripts on your desk. Sell now, or make something richer.");
    private void OnCollectClicked(object? sender, EventArgs e) => Collect();

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        if (Perform(game.Open, "Brother Pip dips his quill. Your first story is underway."))
            _ = PlayVoiceAsync("monk-welcome-v1.wav");
    }

    private void OnPauseClicked(object? sender, EventArgs e)
    {
        if (!Perform(game.TogglePause, game.Paused ? "The candles are still." : "The guild stirs back to life."))
            return;
        if (game.Paused) sceneManager.Pause();
        else sceneManager.Start();
    }

    private void OnSellPlainClicked(object? sender, EventArgs e) => Sell(0);
    private void OnSellArtClicked(object? sender, EventArgs e) => Sell(1);
    private void OnSellBardClicked(object? sender, EventArgs e)
    {
        var count = game.Stock[2];
        Perform(game.SellAllPerformed, $"The town square takes all {count} performances. +{count * GuildGameState.Prices[2]} coins.");
    }
    private void Sell(int kind) => Perform(() => game.Sell(kind), $"A reader takes a story home. +{GuildGameState.Prices[kind]} coins.");

    private void OnQueueArtClicked(object? sender, EventArgs e) =>
        Perform(() => game.Queue(0), "A manuscript arrives at the illustration desk.");
    private void OnQueueBardClicked(object? sender, EventArgs e) =>
        Perform(() => game.Queue(1), "An illustrated book is on its way to the town square.");

    private void OnHireMonkClicked(object? sender, EventArgs e) => Hire(0, "Another monk pulls up a chair.", "monk-welcome-v1.wav");
    private void OnHireArtistClicked(object? sender, EventArgs e) => Hire(1, "The illuminator opens a box of pigments.", "artist-welcome-v1.wav");
    private void OnHireBardClicked(object? sender, EventArgs e) => Hire(2, "The troubadour tunes a lute.", "bard-welcome-v1.wav");
    private void Hire(int role, string message, string voice)
    {
        if (Perform(() => game.Hire(role), message))
            _ = PlayVoiceAsync(voice);
    }

    private bool Perform(Func<bool> action, string message)
    {
        if (!action()) return false;
        MessageLabel.Text = message;
        store.Save(game);
        lastTick = Stopwatch.GetTimestamp();
        Refresh();
        GameView.Invalidate();
        return true;
    }

    private async Task PlayVoiceAsync(string asset)
    {
        if (!VoiceSwitch.IsToggled) return;
        try
        {
            audioPlayer?.Dispose();
            var stream = await FileSystem.OpenAppPackageFileAsync(asset);
            audioPlayer = audioManager.CreatePlayer(stream);
            audioPlayer.Play();
            AudioStatusLabel.Text = $"Playing {asset.Split('-')[0]} greeting";
        }
        catch (Exception)
        {
            AudioStatusLabel.Text = "Voice unavailable. Your guild still works silently.";
        }
    }

    private void OnVoiceToggled(object? sender, ToggledEventArgs e)
    {
        if (!e.Value) audioPlayer?.Pause();
        AudioStatusLabel.Text = e.Value ? "Voices ready" : "Voices off";
    }

    private void OnMonkVoiceTapped(object? sender, TappedEventArgs e) => ReplayVoice(0, "monk-welcome-v1.wav");
    private void OnArtistVoiceTapped(object? sender, TappedEventArgs e) => ReplayVoice(1, "artist-welcome-v1.wav");
    private void OnBardVoiceTapped(object? sender, TappedEventArgs e) => ReplayVoice(2, "bard-welcome-v1.wav");

    private void ReplayVoice(int role, string asset)
    {
        if (game.Workers[role] == 0) return;
        MessageLabel.Text = VoiceSwitch.IsToggled ? "A guild member speaks." : "Turn voices on in Recruit to hear them.";
        _ = PlayVoiceAsync(asset);
    }

    private void Refresh()
    {
        var active = game.CanAct;
        CoinsLabel.Text = $"{game.Coins} coins";
        SoldLabel.Text = $"{game.Sold} books sold";
        OpenButton.IsVisible = !game.Started;
        PauseButton.IsEnabled = game.Started;
        PauseButton.Text = game.Paused ? "Resume" : "Pause";
        CollectButton.Text = game.Ready > 0 ? $"Collect {game.Ready}" : "Quill at work…";
        CollectButton.IsEnabled = active && game.Ready > 0;
        MonkProgress.Progress = game.Progress[0] / GuildGameState.WorkSeconds[0];
        ArtistProgress.Progress = game.Progress[1] / GuildGameState.WorkSeconds[1];
        BardProgress.Progress = game.Progress[2] / GuildGameState.WorkSeconds[2];
        PlainLabel.Text = $"Manuscripts: {game.Stock[0]}";
        IllustratedLabel.Text = $"Illustrated: {game.Stock[1]}";
        PerformedLabel.Text = $"Performed: {game.Stock[2]}";
        SellPlainButton.IsEnabled = active && game.Stock[0] > 0;
        SellArtButton.IsEnabled = active && game.Stock[1] > 0;
        SellBardButton.IsEnabled = active && game.Stock[2] > 0;
        SellBardButton.Text = "Sell all";
        QueueArtButton.IsEnabled = active && game.Stock[0] > 0 && game.Workers[1] > 0;
        QueueBardButton.IsEnabled = active && game.Stock[1] > 0 && game.Workers[2] > 0;
        QueueArtButton.Text = $"Illustrate {Math.Min(game.Stock[0], game.Workers[1])} →";
        QueueBardButton.Text = $"Perform {Math.Min(game.Stock[1], game.Workers[2])} →";
        MonkLabel.Text = $"♫ {game.Workers[0]} monk{(game.Workers[0] == 1 ? "" : "s")} · writing";
        ArtistLabel.Text = game.Workers[1] == 0 ? "Illuminator · empty workshop" : $"♫ {game.Workers[1]} illuminator{(game.Workers[1] == 1 ? "" : "s")} · illustrating";
        BardLabel.Text = game.Workers[2] == 0 ? "Troubadour · empty alcove" : $"♫ {game.Workers[2]} troubadour{(game.Workers[2] == 1 ? "" : "s")} · performing";
        ArtistActor.IsVisible = game.Workers[1] > 0;
        BardActor.IsVisible = game.Workers[2] > 0;
        SetHire(HireMonkButton, 0);
        SetHire(HireArtistButton, 1);
        SetHire(HireBardButton, 2);
        GoalLabel.Text = !game.Started ? "Open your guild. Brother Pip has a story to tell."
            : game.Paused ? "The guild is paused. Resume when you are ready."
            : game.Sold == 0 ? "Collect a manuscript and sell it for your first 5 coins."
            : game.Workers[1] == 0 ? "Save 30 coins for an illuminator, or hire another monk for 25."
            : game.Workers[2] == 0 ? "Illustrate a manuscript and save 60 coins for a troubadour."
            : game.CompletedChain ? "Three crafts, one growing guild. Sell another story or grow your team."
            : game.Stock[2] == 0 ? "Send an illustrated book to your troubadour."
            : "Three crafts, one growing guild. Sell the performance or grow your team.";
    }

    private void SetHire(Button button, int role)
    {
        var cost = game.HireCost(role);
        button.IsEnabled = game.CanAct && game.Workers[role] < int.MaxValue && game.Coins >= cost;
        button.Text = $"{new[] { "Monk", "Illuminator", "Troubadour" }[role]}\n{cost} coins";
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null)
        {
            timer.Stop();
            GameView.StartInteraction -= OnGameViewStartInteraction;
            audioPlayer?.Dispose();
        }
        base.OnHandlerChanging(args);
    }
}
