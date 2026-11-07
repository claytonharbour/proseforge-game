using System.Collections.ObjectModel;
using System.Windows.Input;
using ProseForge.Game.Engine;
using ProseForge.Game.Themes;

namespace ProseForge.Game.ViewModels;

// The main screen, as data the XAML binds to. It knows the session (the rules and the clock) and
// the theme (the words and pictures); it holds no game rules of its own.
public sealed class GameViewModel : Observable
{
    private readonly SaveStore store;
    private readonly VoicePlayer voices;
    private readonly PickerPause picker = new();
    private bool left;
    private string coins = "", goal = "", message = "", delivered = "", pauseText = "", recruitText = "";
    private string audioStatus = "";
    private bool started, showOpen, paused, recruitOpen, voicesOn = true;
    private int saveTicks;

    public GameViewModel(GameSession session, SaveStore store, VoicePlayer voices, Theme theme)
    {
        Session = session;
        this.store = store;
        this.voices = voices;
        Theme = theme;
        BackgroundImage = AppThemes.Image(theme.Background);
        Stations = [.. session.Gameplay.Stations.Select((_, i) => new StationViewModel(this, i))];
        OpenCommand = new Command(Open);
        PauseCommand = new Command(TogglePause);
        RecruitCommand = new Command(() => RecruitOpen = !RecruitOpen);
        message = Theme.Strings.Welcome;
        audioStatus = Theme.Strings.VoicesReady;
        Refresh();
    }

    public Theme Theme { get; }
    public ObservableCollection<StationViewModel> Stations { get; }
    internal GameSession Session { get; }
    internal Gameplay Rules => Session.Gameplay;
    internal EngineState State => Session.State;

    public ICommand OpenCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand RecruitCommand { get; }

    public string Title => Theme.Title;
    public ImageSource? BackgroundImage { get; }
    public string OpenText => Theme.Strings.OpenButton;
    public string Coins { get => coins; private set => Set(ref coins, value); }
    public string Goal { get => goal; private set => Set(ref goal, value); }
    public string Message { get => message; private set => Set(ref message, value); }
    public string Delivered { get => delivered; private set => Set(ref delivered, value); }
    public string PauseText { get => pauseText; private set => Set(ref pauseText, value); }
    public bool Started { get => started; private set => Set(ref started, value); }
    public bool ShowOpen { get => showOpen; private set => Set(ref showOpen, value); }
    public bool Paused { get => paused; private set => Set(ref paused, value); }
    public string AudioStatus { get => audioStatus; private set => Set(ref audioStatus, value); }
    public string RecruitText { get => recruitText; private set => Set(ref recruitText, value); }

    public bool RecruitOpen
    {
        get => recruitOpen;
        set { if (Set(ref recruitOpen, value)) Refresh(); }
    }

    public bool VoicesOn
    {
        get => voicesOn;
        set
        {
            if (!Set(ref voicesOn, value)) return;
            if (!value) voices.Pause();
            AudioStatus = value ? Theme.Strings.VoicesReady : Theme.Strings.VoicesOff;
        }
    }

    // Called by the page's frame timer: credit the time, save now and then, redraw.
    public void Frame()
    {
        Session.CatchUp();
        if (!State.CanAct) return;
        if (++saveTicks >= 10)
        {
            store.Save(Session);
            saveTicks = 0;
        }
        Refresh();
    }

    public void PauseForBackground()
    {
        if (left) return;   // this theme was switched away from and saved as the player had it
        Session.Act((_, s) => GameEngine.PauseForBackground(s));
        store.Save(Session);
        voices.Pause();
        Refresh();
    }

    // The theme picker holds the game still while it's open, and Cancel lets it run again.
    public void OpenPicker()
    {
        picker.Open(Session);
        Refresh();
    }

    public void ClosePicker()
    {
        picker.Close(Session);
        Refresh();
    }

    // Switching to another theme: put this game back as the player had it, save it, go quiet.
    public void Leave()
    {
        picker.Close(Session);
        left = true;
        voices.Pause();
        store.Save(Session);
    }

    public void Resume()
    {
        Session.Resume();
        Refresh();
    }

    // Every tap goes through here: apply the move, say what happened, save, redraw.
    internal bool Perform(Func<Gameplay, EngineState, EngineState?> move, string said,
        string? voice = null, string speaker = "")
    {
        if (!Session.Act(move)) return false;
        Message = said;
        store.Save(Session);
        Refresh();
        if (voice is not null) _ = SpeakAsync(voice, speaker);
        return true;
    }

    internal void Replay(int station)
    {
        if (State.Stations[station].Workers == 0 || Theme.Stations[station].Voice is not { } voice) return;
        Message = VoicesOn ? Theme.Strings.WorkerSpeaks : Theme.Strings.VoicesOffHint;
        _ = SpeakAsync(voice, Theme.Stations[station].Worker);
    }

    private void Open() => Perform((_, s) => GameEngine.Open(s), Theme.Strings.OpenMessage,
        Theme.Strings.OpenVoice, Theme.Stations[0].Worker);

    private void TogglePause()
    {
        // The message describes where the guild ends up, not where it was.
        var pausing = !State.Paused;
        Perform((_, s) => GameEngine.TogglePause(s),
            pausing ? Theme.Strings.PausedMessage : Theme.Strings.ResumedMessage);
    }

    private async Task SpeakAsync(string asset, string speaker)
    {
        if (!VoicesOn) return;
        AudioStatus = await voices.PlayAsync(asset)
            ? ThemeText.Fill(Theme.Strings.VoicePlaying, ("name", speaker))
            : Theme.Strings.VoiceUnavailable;
    }

    public void Refresh()
    {
        var s = State;
        var text = Theme.Strings;
        Coins = ThemeText.Fill(text.Coins, ("n", s.Coins));
        Delivered = ThemeText.Fill(text.Delivered, ("n", s.Delivered));
        Started = s.Started;
        ShowOpen = !s.Started;
        Paused = s.Paused;
        PauseText = s.Paused ? text.Resume : text.Pause;
        RecruitText = RecruitOpen ? text.RecruitClose : text.Recruit;
        Goal = NextGoal();
        foreach (var station in Stations) station.Refresh();
    }

    // What to aim for next: open, unpause, make a first sale, staff each empty station in turn,
    // then keep the last step busy.
    private string NextGoal()
    {
        var s = State;
        var text = Theme.Strings;
        var game = Rules;
        if (!s.Started) return text.GoalStart;
        if (s.Paused) return text.GoalPaused;
        if (s.Delivered == 0) return text.GoalFirstSale;
        for (var i = 1; i < game.Stations.Count; i++)
            if (s.Stations[i].Workers == 0 && i - 1 < text.GoalHire.Count)
                return ThemeText.Fill(text.GoalHire[i - 1],
                    ("cost", GameEngine.HireCost(game, s, i)), ("firstCost", GameEngine.HireCost(game, s, 0)));
        if (s.FinishedChain) return text.GoalChainDone;
        return s.Stations[^1].Stock == 0 ? text.GoalSendToLast : text.GoalSellLast;
    }
}
