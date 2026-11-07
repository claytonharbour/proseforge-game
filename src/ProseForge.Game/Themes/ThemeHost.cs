using ProseForge.Game.Engine;
using ProseForge.Game.ViewModels;

namespace ProseForge.Game.Themes;

// One theme as the picker shows it: its name, its own colours and its background art.
public sealed record ThemeChoice(string Id, string Title, Palette Palette, string Background);

// Owns the live theme on the phone and switches it without a restart: save the game being
// left, load the new pack and its own game, repaint, remember the choice, and hand the app a new
// view model to put on screen. A pack that won't load changes nothing.
public sealed class ThemeHost
{
    private const string ChoiceKey = "theme.current";
#if DEBUG
    private const bool IncludeDrafts = true;    // draft packs are for testing on our own phones
#else
    private const bool IncludeDrafts = false;
#endif
    private readonly SaveStore saves;
    private readonly ThemeResources resources;
    private readonly VoicePlayer voices;
    private readonly TimeProvider clock;
    private readonly ThemeIndex index;
    private IReadOnlyList<ThemeChoice>? choices;

    public ThemeHost(SaveStore saves, ThemeResources resources, VoicePlayer voices, TimeProvider clock)
    {
        this.saves = saves;
        this.resources = resources;
        this.voices = voices;
        this.clock = clock;
        var problems = new List<ThemeProblem>();
        index = ThemeSwitch.LoadIndex(AppThemes.Open, problems) ?? throw new InvalidOperationException(
            $"The bundled theme list didn't load: {string.Join("; ", problems)}");

        // Start in the last theme played. If it no longer loads, the default must: a build whose own
        // default pack is broken shouldn't run.
        var plan = Prepare(ThemeSwitch.Startup(index, IncludeDrafts, Preferences.Default.Get<string?>(ChoiceKey, null)));
        if (!plan.Ready) plan = Prepare(index.Default);
        Current = Build(plan.Ready ? plan : throw new InvalidOperationException(
            $"The default theme \"{index.Default}\" didn't load: {string.Join("; ", plan.Problems)}"));
    }

    public GameViewModel Current { get; private set; }

    // The colours go into the app's resources, which only exist once the App has been built, so the
    // App calls this at start-up; after that, every switch repaints by itself.
    public void ApplyPalette() => resources.Apply(Current.Theme.Palette);

    // Raised after a switch, with the view model for the new theme.
    public event Action<GameViewModel>? Switched;

    // Every bundled theme by title, for the picker. A pack that doesn't load isn't offered.
    public IReadOnlyList<ThemeChoice> Choices => choices ??=
        [.. index.Offered(IncludeDrafts).Select(id => AppThemes.Load(id).Theme)
            .OfType<Theme>()
            .Select(t => new ThemeChoice(t.Id, t.Title, t.Palette, t.Background))];

    // Switch to another theme. Returns null on success, or why the switch was refused.
    public string? Switch(string id)
    {
        if (id == Current.Theme.Id) return null;
        var plan = Prepare(id);
        if (!plan.Ready)
            return string.Join(" ", plan.Problems.Where(p => p.Level == ProblemLevel.Error).Select(p => p.Message));

        Current.Leave();   // saves the game being left, running or paused as the player had it
        Current = Build(plan);
        ApplyPalette();
        Preferences.Default.Set(ChoiceKey, id);
        Switched?.Invoke(Current);
        return null;
    }

    private ThemeSwitchPlan Prepare(string id) => ThemeSwitch.Prepare(index, IncludeDrafts, id, AppThemes.Open, saves.Load);

    private GameViewModel Build(ThemeSwitchPlan plan) =>
        new(new GameSession(plan.Theme!.Gameplay, plan.State, clock), saves, voices, plan.Theme);
}
