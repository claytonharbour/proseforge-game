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
                if (PictureSize(open, full) is not var (w, h))
                {
                    problems.Add(new(ProblemLevel.Error, $"{path} isn't a PNG or a WebP. Pictures in a pack are PNG files (release builds ship them as WebP)."));
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
            file.License,
            file.PowerUps?.Select(look => look with { Icon = OptionalPicture(look.Icon) }).ToList(),
            file.Levels?.Select(look => look with
            {
                Badge = OptionalPicture(look.Badge),
                Voice = Asset(look.Voice, picture: false),
            }).ToList());
        return problems.Exists(p => p.Level == ProblemLevel.Error) ? new(null, problems) : new(theme, problems);

        // Power-up icons and level badges are optional: without one the screen draws a stand-in,
        // so a missing file is left null rather than replaced by the placeholder picture.
        string? OptionalPicture(string? path)
        {
            var resolved = Asset(path, picture: true);
            return resolved == MissingArt ? null : resolved;
        }

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

    // Width and height from a PNG's or a WebP's header, or null if the file is neither. The masters
    // in the repo are PNG; release builds ship WebP copies made by tools/optimize.
    internal static (int Width, int Height)? PictureSize(Func<string, Stream?> open, string path)
    {
        using var stream = open(path);
        if (stream is null) return null;
        Span<byte> head = stackalloc byte[30];
        var read = stream.ReadAtLeast(head, 30, throwOnEndOfStream: false);
        return read >= 24 && PngSize(head[..read]) is { } png ? png : read >= 30 ? WebPSize(head) : null;
    }

    private static (int Width, int Height)? PngSize(ReadOnlySpan<byte> head)
    {
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (!head[..8].SequenceEqual(signature) || !head[12..16].SequenceEqual("IHDR"u8)) return null;
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[16..20]),
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[20..24]));
    }

    // A WebP is a RIFF file whose first chunk says how to read the size: VP8X (extended, used
    // when there's transparency), VP8L (lossless) or "VP8 " (lossy).
    private static (int Width, int Height)? WebPSize(ReadOnlySpan<byte> head)
    {
        if (!head[..4].SequenceEqual("RIFF"u8) || !head[8..12].SequenceEqual("WEBP"u8)) return null;
        var chunk = head[12..16];
        if (chunk.SequenceEqual("VP8X"u8))
            return (1 + (head[24] | head[25] << 8 | head[26] << 16), 1 + (head[27] | head[28] << 8 | head[29] << 16));
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (head[20] != 0x2F) return null;
            var bits = (uint)(head[21] | head[22] << 8 | head[23] << 16 | head[24] << 24);
            return (1 + (int)(bits & 0x3FFF), 1 + (int)((bits >> 14) & 0x3FFF));
        }
        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (head[23] != 0x9D || head[24] != 0x01 || head[25] != 0x2A) return null;
            return ((head[26] | head[27] << 8) & 0x3FFF, (head[28] | head[29] << 8) & 0x3FFF);
        }
        return null;
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
                CheckVoice(name, look, Error);
            }

        // Every power-up and level in the rules needs words for the screen.
        foreach (var p in game.AllPowerUps)
            if (theme.PowerUps?.Any(l => l.Id == p.Id) != true)
                Error($"gameplay.json has the power-up \"{p.Id}\", but theme.json has no powerUps entry for it (label and message).");
        foreach (var l in game.AllLevels)
            if (theme.Levels?.Any(look => look.Id == l.Id) != true)
                Error($"gameplay.json has the level \"{l.Id}\", but theme.json has no levels entry for it (label and message).");
        foreach (var look in theme.PowerUps ?? [])
            if (!game.AllPowerUps.Any(p => p.Id == look.Id))
                Warn($"theme.json describes the power-up \"{look.Id}\", but gameplay.json has no power-up with that id.");
        foreach (var look in theme.Levels ?? [])
            if (!game.AllLevels.Any(l => l.Id == look.Id))
                Warn($"theme.json describes the level \"{look.Id}\", but gameplay.json has no level with that id.");

        if (theme.Strings.GoalHire.Count < game.Stations.Count - 1)
            Warn($"strings.goalHire has {theme.Strings.GoalHire.Count} hints for {game.Stations.Count - 1} stations to staff; the rest get none.");

        foreach (var (role, colour) in Colours(theme.Palette))
            if (!ColourPattern().IsMatch(colour))
                Error($"palette.{role} \"{colour}\" isn't a colour. Use #RRGGBB, or #AARRGGBB for transparency.");
        if (Colours(theme.Palette).All(c => ColourPattern().IsMatch(c.Colour)))
            foreach (var (text, back, where) in TextPairs(theme.Palette))
                if (Contrast(text, back) < MinContrast)
                    Warn($"{where} is hard to read: contrast {Contrast(text, back):0.0}:1, and text needs at least {MinContrast}:1 (WCAG AA). Change one of the two colours.");
    }

    // A greeting in a man's voice on a picture of a woman, or the other way round, is easy to make
    // when the voices are chosen before the art exists, and nothing else notices it. The pack
    // records which voice recorded each greeting and who each picture shows; this compares them.
    // Every part is an error, not a warning: theme-check passes on warnings, so a warning here would
    // let a pack skip the check by leaving the fields out. A voice whose id doesn't say its gender
    // (anything but Kokoro, such as a greeting you recorded yourself) declares it in voiceGender.
    private static void CheckVoice(string name, StationLook look, Action<string> error)
    {
        if (look.Voice is null) return;
        static bool Person(string? value) => value is "woman" or "man";
        if (!Person(look.Speaker))
        {
            error(look.Speaker is null
                ? $"{name} has a voice but no speaker. Add \"speaker\": \"woman\" or \"man\": who the actor picture shows."
                : $"{name} has speaker \"{look.Speaker}\". Use \"woman\" or \"man\": who the actor picture shows.");
            return;
        }
        if (look.VoiceId is null)
        {
            error($"{name} has a voice but no voiceId. Add the voice that recorded it, so it can be checked against the picture.");
            return;
        }
        if (look.VoiceGender is not null && !Person(look.VoiceGender))
        {
            error($"{name} has voiceGender \"{look.VoiceGender}\". Use \"woman\" or \"man\".");
            return;
        }
        if ((VoiceGender(look.VoiceId) ?? look.VoiceGender) is not { } voice)
        {
            error($"{name}'s voiceId \"{look.VoiceId}\" isn't a Kokoro voice (af_, bf_, am_ or bm_), so its gender can't be read from the name. Add \"voiceGender\": \"woman\" or \"man\".");
            return;
        }
        if (voice != look.Speaker)
            error($"{name}'s picture shows a {look.Speaker}, but its greeting was recorded with {look.VoiceId}, a {voice}'s voice. Re-record the line with a {look.Speaker}'s voice, or correct speaker if the picture is what changed.");
    }

    // Kokoro voice ids carry the voice's gender in their second letter: af_/bf_ female, am_/bm_ male.
    public static string? VoiceGender(string voiceId) =>
        voiceId.Length > 3 && voiceId[2] == '_' && voiceId[0] is 'a' or 'b'
            ? voiceId[1] switch { 'f' => "woman", 'm' => "man", _ => null }
            : null;

    // Text needs at least this much contrast with what is behind it (WCAG AA, normal text).
    public const double MinContrast = 4.5;

    // Every place the screen puts one palette colour on another (MainPage.xaml). Transparency is
    // ignored: a panel's colour is judged as if solid, since the picture behind it varies.
    private static IEnumerable<(string Text, string Back, string Where)> TextPairs(Palette p) =>
    [
        (p.Text, p.Header, "palette.text on palette.header (title, goal)"),
        (p.MutedText, p.Header, "palette.mutedText on palette.header (the message line)"),
        (p.Accent, p.Panel, "palette.accent on palette.panel (station status)"),
        (p.Text, p.Panel, "palette.text on palette.panel (station stock)"),
        (p.OnAccent, p.Accent, "palette.onAccent on palette.accent (main buttons)"),
        (p.Text, p.Button, "palette.text on palette.button (other buttons)"),
        (p.Background, p.ButtonActive, "palette.background on palette.buttonActive (an open panel's button)"),
        (p.MutedText, p.Dock, "palette.mutedText on palette.dock (delivered count)"),
    ];

    // WCAG contrast ratio of two #RRGGBB / #AARRGGBB colours, alpha ignored.
    public static double Contrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            var rgb = hex.TrimStart('#')[^6..];
            double Channel(int i)
            {
                var c = Convert.ToInt32(rgb.Substring(i, 2), 16) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
        }
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
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
        CheckBoosts(game, error);
    }

    // Power-ups and levels: each names a real station (or "*" for all), with numbers that do
    // something, and levels come in the order they're reached.
    private static void CheckBoosts(Gameplay game, Action<string> error)
    {
        foreach (var dup in game.AllPowerUps.GroupBy(p => p.Id).Where(g => g.Count() > 1))
            error($"Two power-ups share the id \"{dup.Key}\". Saves are keyed by it.");
        // The dock has room for three buttons (PowerUpButton.MaxOnDock); a fourth would never show.
        if (game.AllPowerUps.Count > 3)
            error($"{game.AllPowerUps.Count} power-ups: the dock shows at most 3, so the rest could never be bought.");
        foreach (var dup in game.AllLevels.GroupBy(l => l.Id).Where(g => g.Count() > 1))
            error($"Two levels share the id \"{dup.Key}\".");

        void CheckBoost(string owner, Boost b)
        {
            if (b.Station != Boost.Everywhere && game.IndexOf(b.Station) < 0)
                error($"{owner} boosts the station \"{b.Station}\", but no station has that id. Use a station id, or \"*\" for every station.");
            switch (b.Kind)
            {
                // Only faster: a slow-down would let a save hold more progress than a job takes.
                case BoostKind.Speed when b.Factor <= 1 || b.Factor > 10:
                    error($"{owner}: a Speed factor must be more than 1 and at most 10 (2 means twice as fast).");
                    break;
                case BoostKind.Batch when b.Add < 1 || b.Add > 100:
                    error($"{owner}: a Batch boost needs an add from 1 to 100 (extra items per job).");
                    break;
                case BoostKind.Price when b.Factor <= 0 || b.Factor > 10 || b.Add < 0 || (b.Factor == 1 && b.Add == 0):
                    error($"{owner}: a Price boost needs a factor above 0 (at most 10) and an add of 0 or more, and must change the price.");
                    break;
            }
        }

        foreach (var p in game.AllPowerUps)
        {
            if (p.Cost <= 0) error($"Power-up \"{p.Id}\": cost must be more than 0, or it could run forever for free.");
            if (p.Seconds <= 0 || p.Seconds > 600) error($"Power-up \"{p.Id}\": seconds must be more than 0 and at most 600.");
            // Visits: unlockAt is a delivered count; every and stays are seconds, and a visit
            // can't outlast its own cycle. every 0 means always there, and then stays means nothing.
            if (p.UnlockAt < 0) error($"Power-up \"{p.Id}\": unlockAt must be 0 or more (items delivered).");
            if (p.Every < 0 || p.Every > 3600) error($"Power-up \"{p.Id}\": every must be from 0 (always there) to 3600 seconds.");
            else if (p.Every > 0 && (p.Stays <= 0 || p.Stays > p.Every))
                error($"Power-up \"{p.Id}\": stays must be more than 0 and no more than every ({p.Every} seconds), or it would never leave.");
            else if (p.Every == 0 && p.Stays != 0)
                error($"Power-up \"{p.Id}\": stays only means something with every; set every, or leave both out.");
            CheckBoost($"Power-up \"{p.Id}\"", p.Effect);
        }
        for (var i = 0; i < game.AllLevels.Count; i++)
        {
            var l = game.AllLevels[i];
            if (l.Delivered <= 0) error($"Level \"{l.Id}\": delivered must be more than 0.");
            if (i > 0 && l.Delivered <= game.AllLevels[i - 1].Delivered)
                error($"Level \"{l.Id}\" comes after \"{game.AllLevels[i - 1].Id}\" but needs no more deliveries. List levels in the order they're reached.");
            CheckBoost($"Level \"{l.Id}\"", l.Reward);
        }
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
