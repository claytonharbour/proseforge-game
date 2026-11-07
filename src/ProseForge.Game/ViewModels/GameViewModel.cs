using System.Collections.ObjectModel;
using System.Windows.Input;
using ProseForge.Game.Engine;
using ProseForge.Game.Presentation;
using ProseForge.Game.Themes;

namespace ProseForge.Game.ViewModels;

// The main screen, as data the XAML binds to. It knows the session (the rules and the clock) and
// the theme (the words and pictures); it holds no game rules of its own.
public sealed class GameViewModel : Observable
{
    private readonly SaveStore store;
    private readonly EarnedUnlock unlock;
    private readonly VoicePlayer voices;
    private readonly PickerPause picker = new();
    private bool left;
    private string coins = "", goal = "", message = "", delivered = "", pauseText = "", recruitText = "";
    private readonly VoiceLine voiceLine;
    private bool started, showOpen, paused, recruitOpen;
    private int saveTicks;
    private long counted;   // this game's deliveries already added to the unlock total
    private int levels;     // levels reached when last drawn, so a new one is announced once

    public GameViewModel(GameSession session, SaveStore store, EarnedUnlock unlock, VoicePlayer voices, Theme theme)
    {
        Session = session;
        this.store = store;
        this.unlock = unlock;
        counted = session.State.Delivered;   // a resumed game's earlier deliveries are already in the total
        levels = GameEngine.LevelsReached(session.Gameplay, session.State);   // a resumed game's levels aren't news
        this.voices = voices;
        Theme = theme;
        BackgroundImage = AppThemes.Image(theme.Background);
        Stations = [.. session.Gameplay.Stations.Select((_, i) => new StationViewModel(this, i))];
        PowerUps = [.. PowerUpButton.All(session.Gameplay, session.State, theme).Select((_, i) => new PowerUpViewModel(this, i))];
        OpenCommand = new Command(Open);
        PauseCommand = new Command(TogglePause);
        RecruitCommand = new Command(() => RecruitOpen = !RecruitOpen);
        message = Theme.Strings.Welcome;
        voiceLine = new VoiceLine(Theme.Strings);
        Refresh();
    }

    public Theme Theme { get; }
    public ObservableCollection<StationViewModel> Stations { get; }
    // The dock's power-ups, one button each, in the order gameplay.json lists them.
    public ObservableCollection<PowerUpViewModel> PowerUps { get; }
    public bool HasPowerUps => PowerUps.Count > 0;
    // The open hire panel takes the power-up row's place, so the dock is no taller with it open and
    // never covers the bottom station's worker count.
    public bool ShowPowerUps => HasPowerUps && !RecruitOpen;
    internal GameSession Session { get; }
    internal Gameplay Rules => Session.Gameplay;
    internal EngineState State => Session.State;

    public ICommand OpenCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand RecruitCommand { get; }

    public string Title => Theme.Title;

    // The reached level's badge beside the count, when the pack has one.
    public ImageSource? LevelBadge { get => levelBadge; private set => Set(ref levelBadge, value); }
    public bool HasLevelBadge => LevelBadge is not null;
    private ImageSource? levelBadge;
    private string? levelBadgePath;

    // Raised when a new level is reached, after its message is shown; the page celebrates.
    public event Action? LevelReached;
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
    public string AudioStatus => voiceLine.Text;
    public string RecruitText { get => recruitText; private set => Set(ref recruitText, value); }

    public bool RecruitOpen
    {
        get => recruitOpen;
        set
        {
            if (!Set(ref recruitOpen, value)) return;
            OnPropertyChanged(nameof(ShowPowerUps));
            Refresh();
        }
    }

    public bool VoicesOn
    {
        get => voiceLine.On;
        set
        {
            if (value == voiceLine.On) return;
            voiceLine.Switch(value);
            if (!value) voices.Pause();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AudioStatus));
        }
    }

    // Raised once, the first time the deliveries across every theme reach EarnedUnlock.Needed.
    public event Action? Unlocked;

    // Called by the page's frame timer: credit the time, save now and then, redraw.
    public void Frame()
    {
        Session.CatchUp();
        // Checked here rather than in Refresh, so it only fires once a page is listening.
        CountDeliveries();
        if (unlock.Due && Unlocked is not null)
        {
            unlock.MarkShown();
            Unlocked();
        }
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

    private void AnnounceLevel()
    {
        var reached = GameEngine.LevelsReached(Rules, State);
        if (reached <= levels) { levels = reached; return; }
        levels = reached;
        if (Boosts.CurrentLevel(Rules, State, Theme) is not { } look) return;
        Message = look.Message;
        if (look.Voice is not null) _ = SpeakAsync(look.Voice, look.Label);
        LevelReached?.Invoke();
    }

    private void TogglePause()
    {
        // The message describes where the guild ends up, not where it was.
        var pausing = !State.Paused;
        Perform((_, s) => GameEngine.TogglePause(s),
            pausing ? Theme.Strings.PausedMessage : Theme.Strings.ResumedMessage);
    }

    private async Task SpeakAsync(string asset, string speaker)
    {
        if (voiceLine.Begin() is not { } clip) return;
        var loaded = await voices.LoadAsync(asset, () => voiceLine.IsCurrent(clip), () =>
        {
            voiceLine.Ended(clip);
            OnPropertyChanged(nameof(AudioStatus));
        });
        if (voiceLine.Started(clip, speaker, loaded)) voices.Play();
        OnPropertyChanged(nameof(AudioStatus));
    }

    private void CountDeliveries()
    {
        var delivered = State.Delivered;
        if (delivered > counted) unlock.Add(delivered - counted);
        counted = delivered;
    }

    public void Refresh()
    {
        CountDeliveries();
        var s = State;
        var text = Theme.Strings;
        Coins = ThemeText.Fill(text.Coins, ("n", Count.Short(s.Coins)));
        // The highest level reached sits beside the count in the dock. Beside the title it was
        // cut off: the header row has no room once the coins and buttons are in it (dev phone, 2026-11-05).
        var deliveredText = ThemeText.Fill(text.Delivered, ("n", s.Delivered));
        var level = Boosts.CurrentLevel(Rules, s, Theme);
        Delivered = level is not null ? $"{deliveredText} · {level.Label}" : deliveredText;
        if (level?.Badge != levelBadgePath)
        {
            levelBadgePath = level?.Badge;
            LevelBadge = AppThemes.Image(levelBadgePath);
            OnPropertyChanged(nameof(HasLevelBadge));
        }
        Started = s.Started;
        ShowOpen = !s.Started;
        Paused = s.Paused;
        PauseText = s.Paused ? text.Resume : text.Pause;
        RecruitText = RecruitOpen ? text.RecruitClose : text.Recruit;
        Goal = GoalText.Next(Rules, State, Theme.Strings);
        AnnounceLevel();
        foreach (var powerUp in PowerUps) powerUp.Refresh();
        foreach (var station in Stations) station.Refresh();
    }
}
