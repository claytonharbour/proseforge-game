using System.Text.Json;
using System.Text.RegularExpressions;
using ProseForge.Game.Engine;

namespace ProseForge.Game.Themes;

// themes/index.json: the packs this build ships, and the one a new player starts in. Drafts are
// packs still being made (a test fixture, words not yet approved): debug builds offer them, store
// builds never do.
public sealed record ThemeIndex(string Default, IReadOnlyList<string> Themes, IReadOnlyList<string>? Drafts = null)
{
    public IReadOnlyList<string> AllDrafts => Drafts ?? [];

    public IReadOnlyList<string> Offered(bool includeDrafts) =>
        includeDrafts ? [.. Themes, .. AllDrafts] : Themes;
}

// A switch that is ready to happen: the new theme and the game to resume in it. A pack that didn't
// load leaves Theme null, and the caller keeps the current theme.
public sealed record ThemeSwitchPlan(Theme? Theme, EngineState? State, IReadOnlyList<ThemeProblem> Problems)
{
    public bool Ready => Theme is not null;
}

// The part of a live theme switch that needs no phone: which packs exist, which one to start in,
// and what to switch to. The app does the rest (ThemeHost): save, repaint, swap the page.
public static partial class ThemeSwitch
{
    public const string IndexPath = "themes/index.json";

    // Read and check the index. Every id must be a plain folder name, appear once, and include the
    // default; whether each pack loads is ThemeCheck's job, so a phone never loads every pack here.
    public static ThemeIndex? LoadIndex(Func<string, Stream?> open, List<ThemeProblem> problems)
    {
        void Error(string message) => problems.Add(new(ProblemLevel.Error, message));
        ThemeIndex? index;
        using (var stream = open(IndexPath))
        {
            if (stream is null)
            {
                Error($"{IndexPath} not found.");
                return null;
            }
            try
            {
                index = JsonSerializer.Deserialize(stream, ThemeJson.Default.ThemeIndex)
                    ?? throw new JsonException("the file is empty");
            }
            catch (JsonException e)
            {
                Error($"{IndexPath}: {e.Message}");
                return null;
            }
        }

        var all = index.Offered(includeDrafts: true);
        foreach (var id in all.Where(id => !IdPattern().IsMatch(id)))
            Error($"{IndexPath}: \"{id}\" isn't a pack folder name. Use lower-case letters, digits and dashes.");
        foreach (var dup in all.GroupBy(id => id).Where(g => g.Count() > 1))
            Error($"{IndexPath} lists \"{dup.Key}\" twice.");
        if (!index.Themes.Contains(index.Default))
            Error($"{IndexPath}: the default \"{index.Default}\" isn't in the list of themes.");
        return problems.Exists(p => p.Level == ProblemLevel.Error) ? null : index;
    }

    // The theme to open at launch: the player's last choice while this build still ships it,
    // otherwise the default.
    public static string Startup(ThemeIndex index, bool includeDrafts, string? remembered) =>
        remembered is not null && index.Offered(includeDrafts).Contains(remembered) ? remembered : index.Default;

    // Load the pack and the player's game in it (that theme's own save, or a new game). Nothing is
    // changed here, so a refusal costs nothing: the current theme simply stays.
    public static ThemeSwitchPlan Prepare(ThemeIndex index, bool includeDrafts, string id,
        Func<string, Stream?> open, Func<Gameplay, EngineState?> restore)
    {
        if (!index.Offered(includeDrafts).Contains(id))
            return new(null, null, [new(ProblemLevel.Error, $"\"{id}\" isn't one of this build's themes.")]);
        var load = ThemePacks.Load(id, open);
        if (load.Theme is not { } theme) return new(null, null, load.Problems);
        return new(theme, restore(theme.Gameplay) ?? EngineState.New(theme.Gameplay), load.Problems);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();
}
