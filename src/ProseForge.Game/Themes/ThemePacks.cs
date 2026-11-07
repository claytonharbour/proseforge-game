using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using ProseForge.Game.Engine;

namespace ProseForge.Game.Themes;

public enum ProblemLevel { Error, Warning }

public sealed record ThemeProblem(ProblemLevel Level, string Message)
{
    public override string ToString() => $"{Level}: {Message}";
}

public sealed record ThemeLoad(Theme? Theme, IReadOnlyList<ThemeProblem> Problems)
{
    public bool Loaded => Theme is not null;
}

// Reads and checks a theme pack from any file source: the app package on a phone, or a folder on
// disk in the tests and in `make theme-check`. An error stops the pack loading and says what to
// fix; missing art or sound is only a warning, and a placeholder stands in so the game still runs.
public static partial class ThemePacks
{
    public const string Root = "themes";
    public const string MissingArt = "themes/_shared/missing-art.png";

    // The look every pack is held to (themes/README.md). The header fits this many characters of
    // title on one line; the background fills a portrait phone; characters stand in a 172-point
    // square above their station's bar.
    public const int TitleMaxLength = 18;
    public const double BackgroundMinAspect = 9.0 / 16, BackgroundMaxAspect = 2.0 / 3;
    public const int BackgroundMinWidth = 1024, ActorMinSide = 512;

    public static ThemeLoad Load(string id, Func<string, Stream?> open)
    {
        var problems = new List<ThemeProblem>();
        var folder = $"{Root}/{id}";
        var file = Read(open, $"{folder}/theme.json", ThemeJson.Default.ThemeFile, problems);
        var gameplayPath = file is null ? null : Inside(folder, file.Gameplay, problems);
        var gameplay = gameplayPath is null ? null : Read(open, gameplayPath, ThemeJson.Default.Gameplay, problems);
        if (file is null || gameplay is null) return new(null, problems);

        Check(id, file, gameplay, problems);
        if (problems.Exists(p => p.Level == ProblemLevel.Error)) return new(null, problems);

        string? Asset(string? path, bool picture, Action<string, int, int>? checkSize = null)
        {
            if (path is null) return null;
            var full = Inside(folder, path, problems);
            if (full is null) return null;
            if (Exists(open, full))
            {
                if (!picture) return full;
                if (PngSize(open, full) is not var (w, h))
                {
                    problems.Add(new(ProblemLevel.Error, $"{path} isn't a PNG. Pictures in a pack are PNG files."));
                    return null;
                }
                checkSize?.Invoke(path, w, h);
                return full;
            }
            problems.Add(new(ProblemLevel.Warning, picture
                ? $"{path} is missing; a placeholder picture stands in."
                : $"{path} is missing; that voice stays silent."));
            return picture ? MissingArt : null;
        }

        var theme = new Theme(
            file.Id, file.Title, gameplay, file.Palette, Asset(file.Background, picture: true, CheckBackground) ?? MissingArt,
            [.. file.Stations.Select(look => look with
            {
                Actor = Asset(look.Actor, picture: true, CheckActor),
                Voice = Asset(look.Voice, picture: false),
            })],
            file.Strings with { OpenVoice = Asset(file.Strings.OpenVoice, picture: false) },
            file.License);
        return problems.Exists(p => p.Level == ProblemLevel.Error) ? new(null, problems) : new(theme, problems);

        void CheckBackground(string path, int w, int h)
        {
            var aspect = (double)w / h;
            if (aspect < BackgroundMinAspect - 0.01 || aspect > BackgroundMaxAspect + 0.01)
                problems.Add(new(ProblemLevel.Error,
                    $"{path} is {w}×{h}. The background must be portrait, between 9:16 and 2:3 (1080×1920 is ideal)."));
            else if (w < BackgroundMinWidth)
                problems.Add(new(ProblemLevel.Warning, $"{path} is {w}×{h}; it will look soft on big phones. 1080×1920 is ideal."));
        }

        void CheckActor(string path, int w, int h)
        {
            if (Math.Min(w, h) < ActorMinSide)
                problems.Add(new(ProblemLevel.Warning, $"{path} is {w}×{h}; characters should be at least {ActorMinSide} on the short side."));
        }
    }

    // Width and height from a PNG's header, or null if the file isn't a PNG.
    internal static (int Width, int Height)? PngSize(Func<string, Stream?> open, string path)
    {
        using var stream = open(path);
        if (stream is null) return null;
        Span<byte> head = stackalloc byte[24];
        if (stream.ReadAtLeast(head, 24, throwOnEndOfStream: false) < 24) return null;
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (!head[..8].SequenceEqual(signature) || !head[12..16].SequenceEqual("IHDR"u8)) return null;
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[16..20]),
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[20..24]));
    }

    private static T? Read<T>(Func<string, Stream?> open, string path, JsonTypeInfo<T> type, List<ThemeProblem> problems)
        where T : class
    {
        using var stream = open(path);
        if (stream is null)
        {
            problems.Add(new(ProblemLevel.Error, $"{path} not found."));
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize(stream, type)
                ?? throw new JsonException("the file is empty");
        }
        catch (JsonException e)
        {
            problems.Add(new(ProblemLevel.Error, $"{path}: {e.Message}"));
            return null;
        }
    }

    // A pack can only reach files inside its own folder; a path that points anywhere else is
    // reported and never opened.
    private static string? Inside(string folder, string path, List<ThemeProblem> problems)
    {
        if (path.StartsWith('/') || path.Contains(':') || path.Split('/', '\\').Contains(".."))
        {
            problems.Add(new(ProblemLevel.Error, $"\"{path}\" points outside the pack. Use a path inside the theme's folder."));
            return null;
        }
        return $"{folder}/{path}";
    }

    private static bool Exists(Func<string, Stream?> open, string path)
    {
        using var stream = open(path);
        return stream is not null;
    }

    private static void Check(string folderId, ThemeFile theme, Gameplay game, List<ThemeProblem> problems)
    {
        void Error(string message) => problems.Add(new(ProblemLevel.Error, message));
        void Warn(string message) => problems.Add(new(ProblemLevel.Warning, message));

        if (theme.Id != folderId)
            Error($"theme.json says id \"{theme.Id}\" but the pack is in the folder \"{folderId}\". They must match.");
        if (game.Id != theme.Id)
            Error($"gameplay id \"{game.Id}\" must match the theme id \"{theme.Id}\": the save slot is named after it.");
        if (theme.Title.Length > TitleMaxLength)
            Error($"title \"{theme.Title}\" is {theme.Title.Length} characters; the header fits {TitleMaxLength} on one line.");
        if (string.IsNullOrWhiteSpace(theme.License))
            Error("license is empty. Say who owns this pack and how it may be reused.");

        CheckGameplay(game, Error);

        if (theme.Stations.Count != game.Stations.Count)
            Error($"theme.json describes {theme.Stations.Count} stations but gameplay.json has {game.Stations.Count}.");
        else
            for (var i = 0; i < game.Stations.Count; i++)
            {
                var rules = game.Stations[i];
                var look = theme.Stations[i];
                var name = $"Station {i + 1} (\"{rules.Id}\")";
                if (rules.CollectByHand && look.CollectLabel is null)
                    Error($"{name} is collected by hand, so it needs a collectLabel.");
                if (i > 0 && game.Stations[i - 1].Handoff == Handoff.ByHand && look.SendLabel is null)
                    Error($"{name} receives work by hand, so it needs a sendLabel.");
                if (rules.NeedsTool is not null && look.ToolLabel is null)
                    Error($"{name} shares the tool \"{rules.NeedsTool}\", so it needs a toolLabel for the button that buys more.");
                if (game.Payout == PayoutRule.AnyStage && rules.SellPrice is not null && look.SellMessage is null)
                    Warn($"{name} can be sold, but has no sellMessage to say so.");
            }

        if (theme.Strings.GoalHire.Count < game.Stations.Count - 1)
            Warn($"strings.goalHire has {theme.Strings.GoalHire.Count} hints for {game.Stations.Count - 1} stations to staff; the rest get none.");

        foreach (var (role, colour) in Colours(theme.Palette))
            if (!ColourPattern().IsMatch(colour))
                Error($"palette.{role} \"{colour}\" isn't a colour. Use #RRGGBB, or #AARRGGBB for transparency.");
    }

    private static void CheckGameplay(Gameplay game, Action<string> error)
    {
        if (game.Stations.Count == 0)
        {
            error("gameplay.json has no stations.");
            return;
        }
        foreach (var dup in game.Stations.GroupBy(s => s.Id).Where(g => g.Count() > 1))
            error($"Two stations share the id \"{dup.Key}\". Each station needs its own id; saves are keyed by it.");
        foreach (var dup in game.AllTools.GroupBy(t => t.Id).Where(g => g.Count() > 1))
            error($"Two tools share the id \"{dup.Key}\".");

        foreach (var s in game.Stations)
        {
            if (s.SecondsPerJob <= 0) error($"Station \"{s.Id}\": secondsPerJob must be more than 0.");
            if (s.HireCost < 0) error($"Station \"{s.Id}\": hireCost can't be negative.");
            if (s.StartingWorkers < 0) error($"Station \"{s.Id}\": startingWorkers can't be negative.");
            if (s.OutputCap < 1) error($"Station \"{s.Id}\": outputCap must be at least 1.");
            if (s.BatchSize < 1) error($"Station \"{s.Id}\": batchSize must be at least 1.");
            if (s.NeedsTool is { } tool && game.ToolIndexOf(tool) < 0)
                error($"Station \"{s.Id}\" needs the tool \"{tool}\", but no tool has that id.");
        }
        foreach (var t in game.AllTools)
            if (t.Starting < 0 || t.UpgradeCost < 0)
                error($"Tool \"{t.Id}\": starting and upgradeCost can't be negative.");

        if (game.Stations[0].StartingWorkers < 1)
            error("The first station starts with nobody working, and a new game has no coins to hire anyone.");
        // With nothing for sale before the end, a new game earns its first coin only if every step
        // already has someone working it; otherwise there's never money to hire the missing one.
        if (game.Payout == PayoutRule.FinalStageOnly)
            foreach (var s in game.Stations.Where(s => s.StartingWorkers < 1))
                error($"Station \"{s.Id}\" starts with nobody working, and only the last step pays, so a new game can never earn the coins to hire there.");
        if (game.Payout == PayoutRule.FinalStageOnly && (game.Stations[^1].SellPrice ?? 0) <= 0)
            error("Only the last step pays (FinalStageOnly), but the last station has no sellPrice.");
        if (game.Payout == PayoutRule.AnyStage && !game.Stations.Any(s => s.SellPrice > 0))
            error("Nothing can be sold: give at least one station a sellPrice.");
        if (game.Goal is { Delivered: <= 0 })
            error("goal.delivered must be more than 0.");
    }

    private static IEnumerable<(string Role, string Colour)> Colours(Palette p) =>
    [
        ("background", p.Background), ("header", p.Header), ("panel", p.Panel), ("panelStroke", p.PanelStroke),
        ("accent", p.Accent), ("onAccent", p.OnAccent), ("text", p.Text), ("mutedText", p.MutedText),
        ("button", p.Button), ("buttonActive", p.ButtonActive), ("dock", p.Dock), ("dockStroke", p.DockStroke),
    ];

    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex ColourPattern();
}
