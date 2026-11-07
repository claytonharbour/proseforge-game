using ProseForge.Game;
using ProseForge.Game.Engine;
using ProseForge.Game.Presentation;
using ProseForge.Game.Themes;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var game = new GuildGameState();
Check(!game.Collect() && !game.Hire(0), "Closed guild accepted an action");
Check(game.Open() && !game.Open(), "Guild opened twice");
Check(!game.Sell(3) && !game.Queue(-1) && !game.Hire(3), "Invalid role accepted");

for (var i = 0; i < 6; i++)
{
    game.Tick(1); game.Tick(1); game.Tick(1); game.Tick(1);
    Check(game.Ready == 1 && game.Collect() && !game.Collect(), "Manuscript collection failed");
    Check(game.Sell(0), "Plain manuscript sale failed");
}
Check(game.Coins == 30 && game.Sold == 6, "First-six sale accounting failed");
Check(game.Hire(1) && game.Coins == 0, "Illuminator recruitment failed");
Check(!game.Hire(2) && !game.Hire(1), "Unaffordable recruitment accepted");

game.Tick(1); game.Tick(1); game.Tick(1); game.Tick(1);
Check(game.Collect() && game.Queue(0), "Illustration queue failed");
Check(!game.Queue(0) && !game.Sell(1), "Pending book was sold twice");
for (var i = 0; i < 6; i++) game.Tick(1);
Check(game.Queues[0] == 0 && game.Stock[1] == 1, "Illustration did not complete");
Check(game.Sell(1) && game.Coins == 12, "Illustrated sale failed");

var snapshot = game.Snapshot();
game.PauseForBackground();
game.Tick(100);
Check(game.Snapshot().Progress.SequenceEqual(snapshot.Progress), "Background advanced production");
var resumed = GuildGameState.FromSnapshot(game.Snapshot());
Check(resumed.Coins == 12 && resumed.Sold == 7 && resumed.Paused, "Save/reload lost state");
Check(!resumed.Sell(0) && resumed.TogglePause(), "Paused action or resume failed");

for (var i = 0; i < 10; i++)
{
    for (var t = 0; t < 4; t++) resumed.Tick(1);
    Check(resumed.Collect() && resumed.Sell(0), "Progression sale failed");
}
Check(resumed.Coins == 62 && resumed.Hire(2), "Troubadour recruitment failed");
for (var t = 0; t < 4; t++) resumed.Tick(1);
Check(resumed.Collect() && resumed.Queue(0), "Second illustration queue failed");
for (var t = 0; t < 6; t++) resumed.Tick(1);
Check(resumed.Queue(1) && resumed.Queues[1] == 1, "Performance queue failed");
for (var t = 0; t < 8; t++) resumed.Tick(1);
Check(resumed.Stock[2] == 1 && resumed.Sell(2) && resumed.Coins == 26, "Full three-craft sale failed");
Check(resumed.CompletedChain && GuildGameState.FromSnapshot(resumed.Snapshot()).CompletedChain,
    "Completed guild milestone was not saved");

var before = resumed.Ready;
resumed.Tick(4000);
Check(resumed.Ready <= before + 2, "Unbounded catch-up");
Check(GuildGameState.FromSnapshot(null).Workers[0] == 1, "Missing save did not recover");
var invalid = resumed.Snapshot() with { Coins = -1 };
Check(GuildGameState.FromSnapshot(invalid).Coins == 0, "Invalid save did not recover");
var capped = new GuildGameState();
capped.Open();
for (var t = 0; t < 100; t++) capped.Tick(1);
Check(capped.Ready == GuildGameState.ReadyLimit, "Ready manuscript capacity failed");
Check(!capped.Hire(0), "Unaffordable second monk accepted");

var growing = GuildGameState.FromSnapshot(new GuildGameState().Snapshot() with { Started = true, Coins = 1000 });
Check(growing.HireCost(0) == 25 && growing.HireCost(1) == 30 && growing.HireCost(2) == 60,
    "Initial recruitment prices changed");
Check(growing.Hire(0) && growing.HireCost(0) == 38 && growing.Hire(0)
    && growing.HireCost(0) == 50 && growing.Hire(0) && growing.Workers[0] == 4,
    "Monks stopped growing or prices did not rise");
Check(growing.Hire(1) && growing.HireCost(1) == 45 && growing.Hire(1)
    && growing.HireCost(1) == 60 && growing.Hire(1) && growing.Workers[1] == 3,
    "Illuminators stopped growing or prices did not rise");
Check(growing.Hire(2) && growing.HireCost(2) == 90 && growing.Hire(2)
    && growing.HireCost(2) == 120 && growing.Hire(2) && growing.Workers[2] == 3,
    "Troubadours stopped growing or prices did not rise");
var growingReloaded = GuildGameState.FromSnapshot(growing.Snapshot());
Check(growingReloaded.Workers.SequenceEqual(growing.Workers)
    && growingReloaded.HireCost(1) == growing.HireCost(1),
    "Larger guild or its next hire price was lost on reload");
var batch = GuildGameState.FromSnapshot(growing.Snapshot() with
{
    Stock = [40, 25, 0], Queues = [0, 0], Workers = [4, 20, 20]
});
Check(batch.Queue(1) && batch.Stock[1] == 5 && batch.Queues[1] == 20,
    "Twenty troubadours did not queue twenty illustrated books");
for (var t = 0; t < 8; t++) batch.Tick(1);
Check(batch.Queues[1] == 0 && batch.Stock[2] == 20,
    "Twenty troubadours did not finish the batch together");
var coinsBeforeSale = batch.Coins;
var soldBeforeSale = batch.Sold;
Check(batch.SellAllPerformed() && batch.Stock[2] == 0 && batch.Coins == coinsBeforeSale + 480
    && batch.Sold == soldBeforeSale + 20 && batch.CompletedChain,
    "Sell all did not credit every finished performance");
Check(batch.Queue(0) && batch.Stock[0] == 20 && batch.Queues[0] == 20,
    "Twenty illuminators did not queue twenty manuscripts");
Check(batch.Queue(1) && batch.Stock[1] == 0 && batch.Queues[1] == 5,
    "Small remainder did not queue only available books");
Console.WriteLine("PASS: three-craft journey, rising recruitment costs, batched work, saves, and bounded catch-up");

// ---- The data-driven engine ----
static string Saved(Gameplay g, EngineState s) =>
    System.Text.Json.JsonSerializer.Serialize(EngineSaves.Capture(g, s));

// Same state and same elapsed time give the same result, every time.
var first = new GameSession(Gameplays.Guild);
var second = new GameSession(Gameplays.Guild);
foreach (var session in new[] { first, second })
{
    session.Act((_, st) => GameEngine.Open(st));
    foreach (var dt in new[] { 0.3, 1.0, 0.7, 2.5, 0.05, 1.0, 1.0 }) session.Tick(dt);
}
Check(Saved(Gameplays.Guild, first.State) == Saved(Gameplays.Guild, second.State), "Engine is not deterministic");

// an active player earns exactly what an idle one does over the same time.
var idleClock = new ManualClock();
var busyClock = new ManualClock();
GuildGameState Ready(ManualClock clock) =>
    GuildGameState.FromSnapshot(new GuildGameState().Snapshot() with { Started = true, Stock = [100, 0, 0] }, clock);
var idle = Ready(idleClock);
var busy = Ready(busyClock);
// The busy player sells 20, under the guild's first level (25 sold): a level speeds the writing
// up by design, which is the rule working, not a tap costing time. The other frames still interleave
// the clock exactly as before.
for (var frame = 0; frame < 41; frame++)
{
    idleClock.Advance(0.100); idle.Session.CatchUp();
    busyClock.Advance(0.090);
    if (frame < 20) Check(busy.Sell(0), "Busy player's sale was refused");
    busyClock.Advance(0.010); busy.Session.CatchUp();
}
Check(idle.Ready == 1 && busy.Ready == idle.Ready && Math.Abs(busy.Progress[0] - idle.Progress[0]) < 1e-9,
    "Taps cost the player production time");

// Paused time is skipped, not owed: unpausing never delivers a burst of work.
var pausedClock = new ManualClock();
var paused = Ready(pausedClock);
paused.TogglePause();
pausedClock.Advance(30);
paused.TogglePause();
pausedClock.Advance(0.5);
paused.Session.CatchUp();
Check(paused.Ready == 0 && Math.Abs(paused.Progress[0] - 0.5) < 1e-9, "Paused time was credited");

// A workshop line: pay only on delivery, a shared roll of tape, sleighs that leave full.
var workshop = new Gameplay("workshop-test",
    [
        new Station("make", 1, 10, StartingWorkers: 20, Handoff: Handoff.Automatic),
        new Station("wrap", 1, 10, StartingWorkers: 20, Handoff: Handoff.Automatic, NeedsTool: "tape"),
        new Station("deliver", 1, 10, StartingWorkers: 1, SellPrice: 3, BatchSize: 5),
    ],
    PayoutRule.FinalStageOnly,
    [new SharedTool("tape", Starting: 1, UpgradeCost: 10)],
    new Goal(10));
var line = new GameSession(workshop);
line.Act((_, st) => GameEngine.Open(st));
for (var t = 0; t < 4; t++) line.Tick(1);
Check(line.State.Stations[1].Waiting > 40 && line.State.Delivered == 0,
    "Boxes did not pile up at wrapping");
Check(GameEngine.Capacity(workshop, line.State, 1) == 1,
    "Twenty wrappers with one roll of tape wrapped more than one box at a time");
Check(!line.Act((g, st) => GameEngine.Sell(g, st, 1)), "A wrapped present was sold before delivery");
var beforeHire = line.State with { Coins = 1000 };
var moreWrappers = GameEngine.Hire(workshop, beforeHire, 1)!;
Check(GameEngine.Capacity(workshop, moreWrappers, 1) == 1, "More wrappers beat the tape bottleneck");
var moreTape = GameEngine.Upgrade(workshop, beforeHire, 0)!;
Check(GameEngine.Capacity(workshop, moreTape, 1) == 2 && moreTape.Coins == 990,
    "More tape did not widen the wrapping step");
Check(GameEngine.UpgradeCost(workshop, moreTape, 0) == 15, "Tape price did not rise like hires do");

var sleigh = new GameSession(workshop, EngineState.New(workshop) with { Started = true });
// Only the sleigh works here: no makers or wrappers feeding it more boxes mid-test.
var loaded = sleigh.State.With(0, sleigh.State.Stations[0] with { Workers = 0 })
    .With(1, sleigh.State.Stations[1] with { Workers = 0 })
    .With(2, sleigh.State.Stations[2] with { Waiting = 4 });
loaded = GameEngine.Tick(workshop, loaded, 1);
Check(loaded.Stations[2].Waiting == 4 && loaded.Delivered == 0, "A sleigh left before it was full");
loaded = loaded.With(2, loaded.Stations[2] with { Waiting = 5 });
loaded = GameEngine.Tick(workshop, loaded, 1);
Check(loaded.Stations[2].Waiting == 0 && loaded.Delivered == 5 && loaded.Coins == 15 && loaded.FinishedChain,
    "A full sleigh was not delivered and paid on arrival");
Check(!loaded.GoalReached(workshop) && (loaded with { Delivered = 10 }).GoalReached(workshop),
    "Practice-night goal miscounted");

// Saves: one slot per theme, keyed by id, and nonsense never loads.
Check(EngineSaves.SlotKey(Gameplays.Guild) == "save.guild.v2", "Guild save slot moved");
var roundTrip = EngineSaves.Restore(workshop, EngineSaves.Capture(workshop, moreTape))!;
Check(Saved(workshop, roundTrip) == Saved(workshop, moreTape), "Engine save round trip lost state");
Check(EngineSaves.Restore(Gameplays.Guild, EngineSaves.Capture(workshop, moreTape)) is null,
    "A workshop save loaded into the guild");
var corrupt = EngineSaves.Capture(workshop, moreTape);
corrupt = corrupt with { Stations = [.. corrupt.Stations.Select(st => st with { Progress = double.NaN })] };
Check(EngineSaves.Restore(workshop, corrupt) is null, "A corrupt save loaded");
var grown = workshop with { Stations = [.. workshop.Stations, new Station("cocoa", 2, 5)] };
var kept = EngineSaves.Restore(grown, EngineSaves.Capture(workshop, moreTape) with { Gameplay = grown.Id })!;
Check(kept.Stations[1].Waiting == moreTape.Stations[1].Waiting && kept.Stations[3].Workers == 0 && kept.Tools[0] == 2,
    "Adding a station to a theme lost the player's progress");

// The one-time v1 migration carries every field across.
var v1 = growing.Snapshot();
var migrated = EngineSaves.Capture(Gameplays.Guild, GuildGameState.FromSnapshot(v1).Session.State);
Check(migrated.Coins == v1.Coins && migrated.Delivered == v1.Sold
    && migrated.Stations.Select(st => st.Workers).SequenceEqual(v1.Workers)
    && migrated.Stations[0].Ready == v1.Ready && migrated.Stations[1].Waiting == v1.Queues[0],
    "v1 guild save did not migrate intact");
Console.WriteLine("PASS: data-driven engine: determinism, honest clock, tape bottleneck, full sleighs, pay on delivery, per-theme saves");

// ---- Theme packs: the real guild pack loads clean; each broken pack says what's wrong ----
static string RepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "themes"))) return dir.FullName;
    throw new Exception("Couldn't find the repository's themes/ folder");
}
var repo = RepoRoot();
Stream? FromDisk(string path)
{
    var full = Path.Combine(repo, path);
    return File.Exists(full) ? File.OpenRead(full) : null;
}

// Positive control first: the shipped guild pack loads with no problems at all.
var guildLoad = ThemePacks.Load("guild", FromDisk);
Check(guildLoad.Loaded && guildLoad.Problems.Count == 0,
    "Guild pack didn't load clean: " + string.Join("; ", guildLoad.Problems));
var guildTheme = guildLoad.Theme!;
string GameplayJson(Gameplay g) => System.Text.Json.JsonSerializer.Serialize(g, ThemeJson.Default.Gameplay);
Check(GameplayJson(guildTheme.Gameplay) == GameplayJson(Gameplays.Guild),
    "themes/guild/gameplay.json and Gameplays.Guild disagree; the guild's rules must have one meaning");
Check(guildTheme.Background == "themes/guild/art/guild-hall.png" && guildTheme.Stations[1].Actor == "themes/guild/art/illuminator.png"
    && guildTheme.Strings.OpenVoice == "themes/guild/audio/monk-welcome.wav",
    "Pack paths didn't resolve inside the pack");
Check(ThemeText.Fill("Sell +{price}", ("price", 12)) == "Sell +12"
    && ThemeText.Fill("Sell +{prise}", ("price", 12)) == "Sell +{prise}",
    "Theme templates don't fill, or a typo vanished");
Check(ThemeText.Fill(guildTheme.Strings.GoalHire[0], ("cost", 30), ("firstCost", 25))
    == "Save 30 coins for an illuminator, or hire another monk for 25.", "Guild's first hiring goal changed");
Check(guildTheme.Strings.HireButton == "{label}\n{cost} coins", "JSON line break in the hire button was lost");

// Broken packs: the guild's own files with one thing changed, served from memory.
var guildFiles = new Dictionary<string, string>
{
    ["themes/guild/theme.json"] = File.ReadAllText(Path.Combine(repo, "themes/guild/theme.json")),
    ["themes/guild/gameplay.json"] = File.ReadAllText(Path.Combine(repo, "themes/guild/gameplay.json")),
};
var opened = new List<string>();
ThemeLoad Broken(string file, string find, string replace)
{
    var files = new Dictionary<string, string>(guildFiles);
    Check(files[file].Contains(find), $"Test setup: '{find}' isn't in {file}");
    files[file] = files[file].Replace(find, replace);
    opened.Clear();
    return ThemePacks.Load("guild", path =>
    {
        opened.Add(path);
        if (files.TryGetValue(path, out var text)) return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return FromDisk(path);
    });
}
bool Says(ThemeLoad load, ProblemLevel level, string text) =>
    load.Problems.Any(p => p.Level == level && p.Message.Contains(text));

var noChange = Broken("themes/guild/theme.json", "\"title\"", "\"title\"");
Check(noChange.Loaded && noChange.Problems.Count == 0, "The in-memory control pack didn't load clean");

var missingArt = Broken("themes/guild/theme.json", "art/illuminator.png", "art/nobody-drew-this.png");
Check(missingArt.Loaded && missingArt.Theme!.Stations[1].Actor == ThemePacks.MissingArt
    && Says(missingArt, ProblemLevel.Warning, "art/nobody-drew-this.png is missing"),
    "Missing art didn't fall back to the placeholder with a warning");
var missingVoice = Broken("themes/guild/theme.json", "audio/bard-welcome.wav", "audio/silence.wav");
Check(missingVoice.Loaded && missingVoice.Theme!.Stations[2].Voice is null, "A missing voice wasn't left silent");

Check(Says(Broken("themes/guild/theme.json", "\"accent\": \"#EDB96B\"", "\"accent\": \"gold\""), ProblemLevel.Error, "palette.accent \"gold\""),
    "A bad colour wasn't named");
Check(Says(Broken("themes/guild/gameplay.json", "\"id\": \"guild\"", "\"id\": \"monastery\""), ProblemLevel.Error, "must match the theme id"),
    "A gameplay id that would share a save slot wasn't caught");
var escaped = Broken("themes/guild/theme.json", "\"gameplay\": \"gameplay.json\"", "\"gameplay\": \"../../.env\"");
Check(!escaped.Loaded && Says(escaped, ProblemLevel.Error, "points outside the pack") && !opened.Any(p => p.Contains("..")),
    "A path outside the pack was opened");
Check(Says(Broken("themes/guild/theme.json", "\"worker\": \"monk\", ", ""), ProblemLevel.Error, "worker"),
    "A missing required field wasn't named");
Check(Says(Broken("themes/guild/gameplay.json", "\"sellPrice\": 12 }", "\"sellPrice\": 12, \"needsTool\": \"glue\" }"), ProblemLevel.Error, "tool \"glue\""),
    "A station needing a tool nobody has wasn't caught");
Check(Says(Broken("themes/guild/gameplay.json", "\"startingWorkers\": 1,", "\"startingWorkers\": 0,"), ProblemLevel.Error, "nobody working"),
    "A line nobody can start wasn't caught");
Check(Says(Broken("themes/guild/theme.json", "\"collectLabel\": \"Collect {n}\", ", ""), ProblemLevel.Error, "needs a collectLabel"),
    "A collect-by-hand station without its button label wasn't caught");
Check(Says(Broken("themes/guild/gameplay.json", "\"stations\": [", "\"stations\": [ oops"), ProblemLevel.Error, "themes/guild/gameplay.json"),
    "Broken JSON didn't name its file");
// The look every pack is held to: picture sizes read from the PNG header, and a one-line title.
static byte[] Png(int w, int h)
{
    var b = new byte[24];
    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }.CopyTo(b, 0);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h);
    return b;
}
ThemeLoad WithPicture(string path, byte[] bytes) => ThemePacks.Load("guild", p =>
    p == path ? new MemoryStream(bytes) : FromDisk(p));
Check(WithPicture("themes/guild/art/guild-hall.png", Png(1080, 1920)) is { Loaded: true, Problems.Count: 0 },
    "An ideal 1080×1920 background wasn't accepted cleanly");
Check(Says(WithPicture("themes/guild/art/guild-hall.png", Png(1920, 1080)), ProblemLevel.Error, "must be portrait"),
    "A landscape background was accepted");
Check(Says(WithPicture("themes/guild/art/guild-hall.png", Png(1000, 1000)), ProblemLevel.Error, "must be portrait"),
    "A square background was accepted");
Check(Says(WithPicture("themes/guild/art/guild-hall.png", Png(720, 1280)), ProblemLevel.Warning, "look soft"),
    "A small background wasn't flagged");
Check(Says(WithPicture("themes/guild/art/guild-hall.png", "not a picture"u8.ToArray()), ProblemLevel.Error, "isn't a PNG"),
    "A file that isn't a PNG was accepted as the background");
Check(Says(WithPicture("themes/guild/art/illuminator.png", Png(256, 256)), ProblemLevel.Warning, "at least 512"),
    "A tiny character picture wasn't flagged");
Check(Says(Broken("themes/guild/theme.json", "\"title\": \"ONE MORE STORY\"", "\"title\": \"ONE MORE STORY: THE EXTENDED EDITION\""),
    ProblemLevel.Error, "fits 18"), "A title too long for the header was accepted");

// A voice must match the person in the picture: a man's voice on a woman's picture is refused.
Check(ThemePacks.VoiceGender("af_heart") == "woman" && ThemePacks.VoiceGender("bf_emma") == "woman"
    && ThemePacks.VoiceGender("am_michael") == "man" && ThemePacks.VoiceGender("bm_george") == "man"
    && ThemePacks.VoiceGender("en-US-Jenny") is null && ThemePacks.VoiceGender("af") is null,
    "Kokoro voice ids weren't read as the right gender");
const string Illuminator = "\"voiceId\": \"bf_emma\", \"speaker\": \"woman\"";
Check(Says(Broken("themes/guild/theme.json", Illuminator, "\"voiceId\": \"bf_emma\", \"speaker\": \"man\""),
    ProblemLevel.Error, "recorded with bf_emma, a woman's voice"), "A woman's voice on a picture of a man was accepted");
Check(Says(Broken("themes/guild/theme.json", Illuminator, "\"voiceId\": \"bm_george\", \"speaker\": \"woman\""),
    ProblemLevel.Error, "recorded with bm_george, a man's voice"), "A man's voice on a picture of a woman was accepted");
// Leaving the fields out must fail too, or a pack could skip the check (theme-check passes on warnings).
Check(Broken("themes/guild/theme.json", Illuminator, "\"speaker\": \"woman\"") is var noId
    && !noId.Loaded && Says(noId, ProblemLevel.Error, "no voiceId"), "A voice without its voiceId still loaded");
Check(Broken("themes/guild/theme.json", Illuminator, "\"voiceId\": \"bf_emma\"") is var noSpeaker
    && !noSpeaker.Loaded && Says(noSpeaker, ProblemLevel.Error, "no speaker"), "A voice without its speaker still loaded");
Check(Says(Broken("themes/guild/theme.json", Illuminator, "\"voiceId\": \"bf_emma\", \"speaker\": \"girl\""),
    ProblemLevel.Error, "Use \"woman\" or \"man\""), "A speaker that isn't woman or man was accepted");
// A voice whose name doesn't give its gender (a greeting you recorded yourself) must say it.
Check(Broken("themes/guild/theme.json", Illuminator, "\"voiceId\": \"my-recording\", \"speaker\": \"woman\"") is var unread
    && !unread.Loaded && Says(unread, ProblemLevel.Error, "Add \"voiceGender\""), "A voice the check can't read still loaded");
Check(Broken("themes/guild/theme.json", Illuminator, "\"voiceId\": \"my-recording\", \"voiceGender\": \"woman\", \"speaker\": \"woman\"")
    is { Loaded: true, Problems.Count: 0 }, "A recorded voice with a matching voiceGender didn't load clean");
Check(Says(Broken("themes/guild/theme.json", Illuminator, "\"voiceId\": \"my-recording\", \"voiceGender\": \"man\", \"speaker\": \"woman\""),
    ProblemLevel.Error, "recorded with my-recording, a man's voice"), "A recorded voice of the wrong gender was accepted");
// Pictures load as PNG (the masters) or WebP (what release builds ship): the size comes from
// either header, for each of WebP's three kinds. Anything else isn't a picture.
Stream? OpenRepo(string path) => File.Exists(Path.Combine(repo, path)) ? File.OpenRead(Path.Combine(repo, path)) : null;
foreach (var (file, w, h) in new[]
{
    ("tests/ProseForge.Game.Rules.Tests/fixtures/webp/lossy-37x21.webp", 37, 21),      // "VP8 "
    ("tests/ProseForge.Game.Rules.Tests/fixtures/webp/lossless-37x21.webp", 37, 21),   // VP8L
    ("tests/ProseForge.Game.Rules.Tests/fixtures/webp/alpha-19x43.webp", 19, 43),      // VP8X
    ("themes/santa/art/powerup.png", 512, 512),
})
    Check(ThemePacks.PictureSize(OpenRepo, file) == (w, h), $"{file} didn't read as {w}×{h}: {ThemePacks.PictureSize(OpenRepo, file)}");
Check(ThemePacks.PictureSize(OpenRepo, "themes/santa/theme.json") is null && ThemePacks.PictureSize(OpenRepo, "no/such.png") is null
    && ThemePacks.PictureSize(_ => new MemoryStream("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()), "short.webp") is null,
    "A file that isn't a picture, a missing one, or a cut-off WebP was read as a picture");
Console.WriteLine("PASS: theme packs: the guild loads clean, missing art has a placeholder, and each broken pack names its fault");

// ---- Live theme switch: pick from the index, each theme keeps its own game, a bad pack changes nothing ----
var indexProblems = new List<ThemeProblem>();
var index = ThemeSwitch.LoadIndex(FromDisk, indexProblems)!;
Check(index is not null && indexProblems.Count == 0, "themes/index.json didn't load: " + string.Join("; ", indexProblems));
// Since 1.1 every pack ships in the store build, so the real index
// has no drafts. The draft rule itself is still checked, on a small index of its own.
var packFolders = Directory.GetDirectories(Path.Combine(repo, "themes")).Select(Path.GetFileName)
    .Where(n => !n!.StartsWith('_')).OrderBy(n => n).ToList();
Check(index.Offered(includeDrafts: false).OrderBy(n => n).SequenceEqual(packFolders) && index.Default == "guild",
    "The store build doesn't offer every pack in themes/: " + string.Join(", ", index.Offered(includeDrafts: false)));
var draftIndex = ThemeSwitch.LoadIndex(path => path == ThemeSwitch.IndexPath
    ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{ "default": "guild", "themes": ["guild"], "drafts": ["santa"] }""")) : null, [])!;
Check(draftIndex.Offered(includeDrafts: false).SequenceEqual(["guild"]) && draftIndex.Offered(includeDrafts: true).Contains("santa"),
    "A store build would offer a draft pack, or a debug build can't reach it");
Check(ThemeSwitch.Startup(draftIndex, false, "santa") == "guild" && ThemeSwitch.Startup(draftIndex, true, "santa") == "santa"
    && ThemeSwitch.Startup(draftIndex, true, "gone") == "guild" && ThemeSwitch.Startup(draftIndex, true, null) == "guild",
    "Launch didn't fall back to the default for a theme this build doesn't offer");

ThemeIndex? IndexFrom(string json, List<ThemeProblem> problems) =>
    ThemeSwitch.LoadIndex(path => path == ThemeSwitch.IndexPath ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)) : null, problems);
var bad = new List<ThemeProblem>();
Check(IndexFrom("""{ "default": "guild", "themes": ["guild", "../secrets"] }""", bad) is null && bad.Any(p => p.Message.Contains("../secrets")),
    "An index entry that escapes themes/ was accepted");
bad.Clear();
Check(IndexFrom("""{ "default": "santa", "themes": ["guild"], "drafts": ["santa"] }""", bad) is null && bad.Any(p => p.Message.Contains("default")),
    "A default that store builds don't offer was accepted");
bad.Clear();
Check(IndexFrom("""{ "default": "guild", "themes": ["guild", "guild"] }""", bad) is null && bad.Any(p => p.Message.Contains("twice")),
    "A pack listed twice was accepted");

// The phone's save store, in memory: one slot per theme, as EngineSaves names them.
var slots = new Dictionary<string, string>();
string SaveJsonOf(GameSession s) => System.Text.Json.JsonSerializer.Serialize(EngineSaves.Capture(s.Gameplay, s.State));
void Save(GameSession s) => slots[EngineSaves.SlotKey(s.Gameplay)] = SaveJsonOf(s);
EngineState? Restore(Gameplay g) => slots.TryGetValue(EngineSaves.SlotKey(g), out var json)
    ? EngineSaves.Restore(g, System.Text.Json.JsonSerializer.Deserialize<EngineSnapshot>(json)) : null;

var toGuild = ThemeSwitch.Prepare(index, true, "guild", FromDisk, Restore);
var guildGame = new GameSession(toGuild.Theme!.Gameplay, toGuild.State);
guildGame.Act((_, st) => GameEngine.Open(st));
for (var t = 0; t < 8; t++) guildGame.Tick(1);
guildGame.Act((g, st) => GameEngine.Collect(g, st, 0));
guildGame.Act((g, st) => GameEngine.Sell(g, st, 0));
Save(guildGame);
var guildSaved = slots["save.guild.v2"];

var toSanta = ThemeSwitch.Prepare(index, true, "santa", FromDisk, Restore);
Check(toSanta.Ready && toSanta.State!.Coins == 0 && !toSanta.State.Started && toSanta.State.Stations.Length == 3,
    "Santa didn't start a new game of its own");
var santaGame = new GameSession(toSanta.Theme!.Gameplay, toSanta.State);
santaGame.Act((_, st) => GameEngine.Open(st));
for (var t = 0; t < 30; t++) santaGame.Tick(1);
Save(santaGame);
var back = ThemeSwitch.Prepare(index, true, "guild", FromDisk, Restore);
Check(slots["save.guild.v2"] == guildSaved && SaveJsonOf(new GameSession(back.Theme!.Gameplay, back.State)) == guildSaved,
    "Switching Guild → Santa → Guild didn't bring back the same guild game");
Check(!ThemeSwitch.Prepare(draftIndex, false, "santa", FromDisk, Restore).Ready, "A store build switched to a draft pack");
Check(!ThemeSwitch.Prepare(index, true, "nowhere", FromDisk, Restore).Ready, "A theme that isn't in the index loaded");

// The order of a live switch: check the new pack first; only then leave (which saves) the
// current game, build the new one, repaint and remember. A refused switch must touch nothing.
var steps = new List<string>();
(string Game, string? Refusal) SwitchFrom(string current, string to) => ThemeSwitch.Run(current, current, to,
    prepare: id => { steps.Add($"check {id}"); return ThemeSwitch.Prepare(index, true, id, FromDisk, Restore); },
    build: plan => { steps.Add($"build {plan.Theme!.Id}"); return plan.Theme.Id; },
    leave: game => steps.Add($"leave {game}"),
    paint: game => steps.Add($"paint {game}"),
    remember: id => steps.Add($"remember {id}"));
var toSantaLive = SwitchFrom("guild", "santa");
Check(toSantaLive == ("santa", null)
    && steps.SequenceEqual(["check santa", "leave guild", "build santa", "paint santa", "remember santa"]),
    "A live switch ran out of order: " + string.Join(", ", steps));
steps.Clear();
var refusedLive = SwitchFrom("guild", "nowhere");
Check(refusedLive.Game == "guild" && !string.IsNullOrEmpty(refusedLive.Refusal) && steps.SequenceEqual(["check nowhere"]),
    "A refused switch left, saved or repainted something: " + string.Join(", ", steps));
steps.Clear();
Check(SwitchFrom("guild", "guild") == ("guild", null) && steps.Count == 0, "Switching to the theme in play did something");

// A broken pack is refused before anything changes: no save written or read.
var santaTheme = File.ReadAllText(Path.Combine(repo, "themes/santa/theme.json"));
var restoresAsked = 0;
var brokenSanta = ThemeSwitch.Prepare(index, true, "santa", path => path == "themes/santa/theme.json"
        ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(santaTheme.Replace("\"accent\": \"#F2D27A\"", "\"accent\": \"red\"")))
        : FromDisk(path),
    g => { restoresAsked++; return Restore(g); });
Check(santaTheme.Contains("\"accent\": \"#F2D27A\"") && !brokenSanta.Ready && restoresAsked == 0
    && brokenSanta.Problems.Any(p => p.Message.Contains("palette.accent")) && slots["save.guild.v2"] == guildSaved,
    "A broken pack wasn't refused cleanly");

// The Santa pack plays the tape rule: boxes pile up at wrapping, more
// wrappers don't help, another roll of tape does.
var santa = toSanta.Theme!.Gameplay;
Check(santaGame.State.Stations[1].Waiting > 0 && santaGame.State.Delivered > 0,
    "Santa's line didn't pile boxes at wrapping and still deliver");
var rich = santaGame.State with { Coins = 1000 };
Check(GameEngine.Capacity(santa, rich, 1) == 1 && GameEngine.Capacity(santa, GameEngine.Hire(santa, rich, 1)!, 1) == 1
    && GameEngine.Capacity(santa, GameEngine.Upgrade(santa, rich, 0)!, 1) == 2,
    "In the Santa pack, a wrapper beat the tape, or tape didn't widen wrapping");
var santaGameplay = File.ReadAllText(Path.Combine(repo, "themes/santa/gameplay.json"));
var stuck = ThemePacks.Load("santa", path => path == "themes/santa/gameplay.json"
    ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(santaGameplay.Replace("\"startingWorkers\": 2", "\"startingWorkers\": 0")))
    : FromDisk(path));
Check(santaGameplay.Contains("\"startingWorkers\": 2") && !stuck.Loaded && stuck.Problems.Any(p => p.Message.Contains("never earn")),
    "A pay-on-delivery line with an empty step, which can never earn a coin, wasn't caught");
var noToolButton = ThemePacks.Load("santa", path => path == "themes/santa/theme.json"
    ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(santaTheme.Replace("\"toolLabel\"", "\"unusedLabel\"")))
    : FromDisk(path));
Check(!noToolButton.Loaded, "A tool station without a toolLabel loaded (unknown fields should fail too)");
// The picker holds the game still, and Cancel puts it back as the player had it.
var running = new GameSession(santa);
running.Act((_, st) => GameEngine.Open(st));
var hold = new PickerPause();
hold.Open(running);
var heldState = running.State;
running.Tick(5);
Check(running.State.Paused && running.State == heldState, "The game kept working behind the theme picker");
hold.Close(running);
Check(running.State.CanAct, "Cancel didn't let a running game run again");
running.Act((_, st) => GameEngine.TogglePause(st));
hold.Open(running);
hold.Close(running);
Check(running.State.Paused, "Cancel undid a pause the player made themselves");
var notOpen = new GameSession(santa);
hold.Open(notOpen);
hold.Close(notOpen);
Check(!notOpen.State.Started && !notOpen.State.Paused, "The picker changed a workshop that wasn't open yet");
// The layout model is what the art guide is drawn from, so it must match a real phone. Measured on
// the dev OnePlus (1080×2412 at 480 dpi: a 360×720-point page under the status bar), 2026-10-30,
// from the accessibility tree with the game paused. Each box: top, bottom in points.
var phone = ScreenLayout.For(360, 720, 3);
bool Near(double a, double b) => Math.Abs(a - b) < 0.5;
bool Rows(ScreenLayout.Box box, double top, double bottom) => Near(box.Top, top) && Near(box.Bottom, bottom);
Check(Rows(phone.Header, 0, 92)
    && Rows(phone.Stations[0].Band, 96, 263.3) && Rows(phone.Stations[0].Bar, 203.3, 263.3)
    && Rows(phone.Stations[1].Band, 268.3, 435.7) && Rows(phone.Stations[1].Bar, 375.7, 435.7)
    && Rows(phone.Stations[2].Band, 440.7, 608) && Rows(phone.Stations[2].Bar, 548, 608)
    && Near(phone.Dock.Top, 608)
    && Near(phone.Stations[1].Actor.Left, 154) && Near(phone.Stations[1].Actor.Right, 326) && Rows(phone.Stations[1].Actor, 268.3, 435.7),
    "ScreenLayout no longer matches the measured phone; re-measure it, then fix the model or MainPage.xaml");
// A 1080×1920 background on that phone: scaled ×0.375 to the page height, so 22.5 points (60 pixels)
// are cropped from each side and 960 of the 1080 pixels show.
var visibleArt = ScreenLayout.ToPicture(new ScreenLayout.Box(0, 0, 360, 720), 360, 720, 1080, 1920);
Check(Math.Abs(visibleArt.Left - 60) < 0.5 && Math.Abs(visibleArt.Width - 960) < 0.5 && Math.Abs(visibleArt.Height - 1920) < 0.5,
    "AspectFill crop isn't what the phone shows");
// a desktop window opens phone-shaped and centred, never taller than the screen allows, and never
// below a size that keeps every bar on screen with a strip of art above it.
var laptop = ScreenLayout.DesktopWindow(1470, 956);     // a 13-inch MacBook Air's screen, in points
Check(Near(laptop.Height, 860) && Near(laptop.Width, Math.Round(860 * ScreenLayout.DesktopPage))
    && Near(laptop.X, (1470 - laptop.Width) / 2) && Near(laptop.Y, (956 - 860) / 2.0),
    $"A laptop window isn't phone-shaped and centred: {laptop}");
var big = ScreenLayout.DesktopWindow(2560, 1440);
Check(Near(big.Height, ScreenLayout.DesktopHeight), $"A big screen's window isn't the standard height: {big}");
var cramped = ScreenLayout.DesktopWindow(800, 480);
Check(Near(cramped.Height, cramped.MinHeight) && cramped.Width >= ScreenLayout.MinWidth && cramped.X >= 0 && cramped.Y >= 0,
    $"A tiny screen's window went below the minimum or off screen: {cramped}");
var smallest = ScreenLayout.For(laptop.MinWidth, laptop.MinHeight, 3);
Check(smallest.Stations.All(st => st.Band.Height >= ScreenLayout.MinBand - 0.01 && st.Bar.Bottom <= smallest.Dock.Top + 0.01),
    "At the minimum window size a station loses its bar or the art above it");
Console.WriteLine("PASS: live theme switch: index checked, drafts debug-only, each theme keeps its own game, broken packs change nothing, picker pauses, tape plays");

// ---- Testable screen logic: what each row offers, the goal line, and saves without a phone ----
var guildRules = guildTheme.Gameplay;
EngineState Opened(Gameplay g) => GameEngine.Open(EngineState.New(g))!;
var guildOpen = Opened(guildRules);
var desk = ProseForge.Game.Presentation.StationRow.For(guildRules, guildOpen, guildTheme, 0, hasActor: true);
Check(desk.Primary == ProseForge.Game.Presentation.RowAction.Collect && desk.PrimaryText == "Quill at work…" && !desk.PrimaryEnabled
    && desk.SellVisible && desk.SellText == "Sell +5" && !desk.SellEnabled && desk.ActorVisible,
    "The guild's desk row didn't offer an idle Collect and a Sell +5");
var readyDesk = guildOpen.With(0, guildOpen.Stations[0] with { Ready = 3 });
var collectNow = ProseForge.Game.Presentation.StationRow.For(guildRules, readyDesk, guildTheme, 0, true);
Check(collectNow.PrimaryText == "Collect 3" && collectNow.PrimaryEnabled, "Ready manuscripts didn't enable Collect 3");
var pausedDesk = ProseForge.Game.Presentation.StationRow.For(guildRules, readyDesk with { Paused = true }, guildTheme, 0, true);
Check(!pausedDesk.PrimaryEnabled && !pausedDesk.HireEnabled, "A paused game still offered buttons");
var emptyIllum = ProseForge.Game.Presentation.StationRow.For(guildRules, guildOpen, guildTheme, 1, true);
Check(emptyIllum.Primary == ProseForge.Game.Presentation.RowAction.Send && emptyIllum.PrimaryText == "Illustrate 0 →" && !emptyIllum.PrimaryEnabled
    && emptyIllum.Status == "Illuminator · empty workshop" && !emptyIllum.ActorVisible,
    "An unstaffed illuminator row wasn't an empty, disabled send");
var staffed = guildOpen.With(0, guildOpen.Stations[0] with { Stock = 5 }).With(1, guildOpen.Stations[1] with { Workers = 1 }) with { Coins = 100 };
var illum = ProseForge.Game.Presentation.StationRow.For(guildRules, staffed, guildTheme, 1, true);
Check(illum.PrimaryText == "Illustrate 1 →" && illum.PrimaryEnabled && illum.ActorVisible && illum.HireEnabled
    && illum.HireText == "Illuminator\n45 coins",
    "A staffed illuminator didn't offer one manuscript to send, or the next hire price is wrong");
var santaTheme2 = toSanta.Theme!;
var santaOpen = Opened(santaTheme2.Gameplay) with { Coins = 14 };
var wrapRow = ProseForge.Game.Presentation.StationRow.For(santaTheme2.Gameplay, santaOpen, santaTheme2, 1, true);
Check(wrapRow.Primary == ProseForge.Game.Presentation.RowAction.BuyTool && wrapRow.PrimaryText == "+1 tape · ♥ 15" && !wrapRow.PrimaryEnabled
    && wrapRow.Stock.Contains("rolls of tape: 1") && !wrapRow.SellVisible,
    "Santa's wrapping row didn't offer tape at 15, or showed a sell button on a pay-on-delivery line");
Check(ProseForge.Game.Presentation.StationRow.For(santaTheme2.Gameplay, santaOpen with { Coins = 15 }, santaTheme2, 1, true).PrimaryEnabled,
    "Fifteen hearts didn't enable the tape button");
Check(ProseForge.Game.Presentation.StationRow.For(santaTheme2.Gameplay, santaOpen, santaTheme2, 0, true).Primary == ProseForge.Game.Presentation.RowAction.None,
    "An automatic station grew a button");

string Goal(EngineState st) => ProseForge.Game.Presentation.GoalText.Next(guildRules, st, guildTheme.Strings);
var gs = guildTheme.Strings;
Check(Goal(EngineState.New(guildRules)) == gs.GoalStart && Goal(guildOpen with { Paused = true }) == gs.GoalPaused
    && Goal(guildOpen) == gs.GoalFirstSale, "The opening goals are out of order");
var sold = guildOpen with { Delivered = 1 };
Check(Goal(sold) == "Save 30 coins for an illuminator, or hire another monk for 25.", "The first hiring goal is wrong");
var withIllum = sold.With(1, sold.Stations[1] with { Workers = 1 });
Check(Goal(withIllum) == "Illustrate a manuscript and save 60 coins for a troubadour.", "The second hiring goal is wrong");
var allStaffed = withIllum.With(2, withIllum.Stations[2] with { Workers = 1 });
Check(Goal(allStaffed) == gs.GoalSendToLast && Goal(allStaffed.With(2, allStaffed.Stations[2] with { Workers = 1, Stock = 2 })) == gs.GoalSellLast
    && Goal(allStaffed with { FinishedChain = true }) == gs.GoalChainDone, "The late-game goals are wrong");

var memSlots = new MemorySlots();
var saves = new SaveStore(memSlots);
Check(saves.Load(guildRules) is null, "An empty phone produced a saved game");
var oldGame = new GuildGameState();
oldGame.Open(); for (var i = 0; i < 4; i++) oldGame.Tick(1); oldGame.Collect(); oldGame.Sell(0);
var v1Json = System.Text.Json.JsonSerializer.Serialize(oldGame.Snapshot(), SaveJson.Default.GuildSnapshot);
memSlots.Set("guild-save.v1", v1Json);
var migrated2 = saves.Load(guildRules);
Check(migrated2 is { Coins: 5, Delivered: 1, Started: true } && memSlots.Get("guild-save.v1") == v1Json && memSlots.Get("save.guild.v2") is null,
    "A v1 save didn't migrate on load, or the v1 save was changed");
var santaSession = new GameSession(santaTheme2.Gameplay, santaOpen);
saves.Save(santaSession);
saves.Save(new GameSession(guildRules, migrated2));
Check(memSlots.Get("save.santa.v2") is not null && saves.Load(guildRules) is { Coins: 5 } && saves.Load(santaTheme2.Gameplay) is { Coins: 14 },
    "Two themes' saves weren't kept in their own memSlots");
memSlots.Set("save.guild.v2", "{not json");
Check(saves.Load(guildRules) is { Coins: 5 }, "A corrupt v2 save didn't fall back to the v1 save");
// Readable colours: theme-check warns when a text pair is under 4.5:1. Santa's first cranberry
// palette put dark red text on an evergreen active button (2.65:1), spotted on the phone.
Check(Math.Abs(ThemePacks.Contrast("#000000", "#FFFFFF") - 21) < 0.01 && Math.Abs(ThemePacks.Contrast("#3A0E10", "#2F6B45") - 2.65) < 0.01,
    "The contrast formula doesn't match WCAG");
var santaJson = File.ReadAllText(Path.Combine(repo, "themes/santa/theme.json"));
var oldGreen = ThemePacks.Load("santa", path => path == "themes/santa/theme.json"
    ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(santaJson.Replace("\"buttonActive\": \"#E8C66A\"", "\"buttonActive\": \"#2F6B45\"")))
    : FromDisk(path));
Check(santaJson.Contains("\"buttonActive\": \"#E8C66A\"") && oldGreen.Loaded
    && oldGreen.Problems.Any(p => p.Level == ProblemLevel.Warning && p.Message.Contains("buttonActive") && p.Message.Contains("2.6:1")),
    "Dark red on evergreen wasn't flagged as hard to read");
foreach (var id in index.Offered(includeDrafts: true))
{
    var pack = ThemePacks.Load(id, FromDisk);
    Check(!pack.Problems.Any(p => p.Message.Contains("hard to read")), $"Pack {id} has a hard-to-read colour pair: "
        + string.Join("; ", pack.Problems.Where(p => p.Message.Contains("hard to read"))));
}
// Santa pays only for finished deliveries (FinalStageOnly), so no row offers Sell, even the delivery
// row that has a price. Without this, dropping the payout rule from SellVisible passed every test
// (a reviewer deleted the payout check on purpose, and no test noticed).
var santaPack = toSanta.Theme!;
var santaRows = Enumerable.Range(0, santaPack.Gameplay.Stations.Count)
    .Select(i => ProseForge.Game.Presentation.StationRow.For(santaPack.Gameplay, Opened(santaPack.Gameplay), santaPack, i, true)).ToList();
Check(santaPack.Gameplay.Payout == PayoutRule.FinalStageOnly && santaPack.Gameplay.Stations[^1].SellPrice is not null
    && santaRows.All(r => !r.SellVisible), "A final-stage-only theme showed a Sell button");
string[] shortCounts = [.. new long[] { 0, 2509, 9999, 10_000, 12_499, 99_999, 100_000, 999_999, 1_250_000, 3_000_000_000 }
    .Select(ProseForge.Game.Presentation.Count.Short)];
Check(string.Join(" ", shortCounts) == "0 2509 9999 10k 12.4k 99.9k 100k 999k 1.2M 3B",
    $"The header count didn't shorten as expected: {string.Join(" ", shortCounts)}");
Console.WriteLine("PASS: screen logic: every row's buttons from the rules, the goal line in order, and saves (v1 migration, one slot per theme) without a phone");

// ---- The art gallery: what the 100-delivery unlock opens, checked without a phone ----
Stream? Text(string s) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(s));
Gallery? ParseGallery(string json, List<string> problems) => Gallery.Parse(Text(json)!, problems);
const string GoodPanel = """{"title":"T","kind":"K","story":"S","note":"N","pictures":[{"file":"images/a.webp","caption":"C","alt":"A"}]}""";
string GalleryWith(string panel) => $$"""{"format":"proseforge-gallery/1","title":"How","intro":"I","panels":[{{panel}}]}""";
var galleryBad = new List<string>();
var goodGallery = ParseGallery(GalleryWith(GoodPanel), galleryBad);
Check(goodGallery is not null && galleryBad.Count == 0 && goodGallery.Panels.Single().Pictures.Single().Alt == "A"
    && Gallery.PathOf(goodGallery.Panels[0].Pictures[0]) == "gallery/images/a.webp",
    "A good gallery didn't load: " + string.Join("; ", galleryBad));
// Refused rather than shown: another format, broken JSON, a field missing, or a picture outside images/.
foreach (var (json, why) in new[]
{
    ("""{"format":"proseforge-gallery/9","title":"How","intro":"I","panels":[]}""", "another format"),
    ("not json", "broken JSON"),
    ("""{"format":"proseforge-gallery/1","title":"How","intro":"I","panels":[]}""", "no panels"),
    (GalleryWith(GoodPanel.Replace("\"note\":\"N\",", "")), "a panel with no note"),
    (GalleryWith(GoodPanel.Replace("\"alt\":\"A\"", "\"alt\":\"\"")), "a picture with no alt text"),
    (GalleryWith(GoodPanel.Replace("images/a.webp", "../../themes/guild/art/guild-hall.png")), "a picture outside its folder"),
    (GalleryWith(GoodPanel.Replace("images/a.webp", "images/../../secret.png")), "a path that climbs out"),
    (GalleryWith(GoodPanel.Replace("[{\"file\"", "[null,{\"file\"")), "a null picture"),
    (GalleryWith("null"), "a null panel"),
    (GalleryWith(GoodPanel.Replace("\"note\":\"N\",", "\"note\":\"N\",\"room\":[{\"author\":\"A\",\"time\":\"t\",\"parts\":[{\"picture\":\"../x.png\"}]}],")), "a room picture outside its folder"),
    (GalleryWith(GoodPanel.Replace("\"note\":\"N\",", "\"note\":\"N\",\"room\":[{\"author\":\"A\",\"time\":\"t\",\"parts\":[{\"text\":\"x\",\"picture\":\"images/a.webp\"}]}],")), "a room part that is words and a picture at once"),
    (GalleryWith(GoodPanel.Replace("\"note\":\"N\",", "\"note\":\"N\",\"room\":[{\"author\":\"\",\"time\":\"t\",\"parts\":[{\"text\":\"x\"}]}],")), "a room message with no author"),
})
{
    galleryBad.Clear();
    Check(ParseGallery(json, galleryBad) is null && galleryBad.Count > 0, $"A gallery with {why} was shown");
}
// A build with no gallery/ shows nothing, and a read that throws doesn't crash the About page.
galleryBad.Clear();
Check(Gallery.Load(_ => null, galleryBad) is null && galleryBad.Count == 0, "A build without gallery/ showed one");
Check(Gallery.Load(_ => throw new IOException("disk gone"), galleryBad) is null && galleryBad.Count == 1, "A failed read wasn't reported");
Check(FeatureFlags.Load(_ => null) == FeatureFlags.Off, "A build without features.json showed a feature");
Check(FeatureFlags.Load(_ => throw new IOException("disk gone")) == FeatureFlags.Off, "A read that throws didn't leave features off");
Check(FeatureFlags.Load(_ => Text("""{"gallery": true}""")).Gallery, "features.json couldn't turn the gallery on");
Check(!FeatureFlags.Load(_ => Text("""{"gallery": false}""")).Gallery && !FeatureFlags.Load(_ => Text("{}")).Gallery
    && !FeatureFlags.Load(_ => Text("{oops")).Gallery && !FeatureFlags.Load(_ => Text("""{"roomViewer": true}""")).Gallery,
    "A feature that wasn't switched on, the old room flag, or a broken file showed the gallery");
Check(!FeatureFlags.Load(path => File.Exists(Path.Combine(repo, "src/ProseForge.Game/Resources/Raw", path))
        ? File.OpenRead(Path.Combine(repo, "src/ProseForge.Game/Resources/Raw", path)) : null).Gallery,
    "The store build's own features.json shows the gallery before it's earned");

// The gallery the game ships: it loads clean, every picture is there and every file is used, and it
// stays small. No link, ticket number or internal address may reach a player. The public copy of the
// repo leaves gallery/ out (its art is reserved), so there only the parser checks above run.
if (!Directory.Exists(Path.Combine(repo, "gallery")))
    Console.WriteLine("PASS: art gallery: parser checks only; this copy has no gallery/ (the public copy leaves it out)");
else
{
var shippedProblems = new List<string>();
var shippedGallery = Gallery.Load(path => File.Exists(Path.Combine(repo, path)) ? File.OpenRead(Path.Combine(repo, path)) : null, shippedProblems);
Check(shippedGallery is not null && shippedProblems.Count == 0, "The shipped gallery didn't load clean: " + string.Join("; ", shippedProblems));
var shownFiles = shippedGallery!.Panels.SelectMany(p => p.Pictures).Select(p => p.File)
    .Concat(shippedGallery.Panels.SelectMany(p => p.AllRoom).SelectMany(b => b.Parts).Select(part => part.Picture).OfType<string>())
    .Select(Path.GetFileName).OfType<string>().ToHashSet();
Check(shippedGallery.Panels.Count(p => p.AllRoom.Count > 0) >= 5, "Most shipped panels should have their room messages behind them");
Check(shippedGallery.Panels.SelectMany(p => p.AllRoom).Any(b => b.Reactions?.Contains("❤️ Clayton") == true), "The hearts from the room didn't come through");
foreach (var file in shownFiles)
    Check(File.Exists(Path.Combine(repo, "gallery/images", file)), $"The gallery shows {file}, which isn't in gallery/images");
var galleryFiles = Directory.GetFiles(Path.Combine(repo, "gallery/images")).Select(Path.GetFileName).ToList();
Check(galleryFiles.All(f => shownFiles.Contains(f!)), "gallery/images has files no panel shows: " + string.Join(", ", galleryFiles.Where(f => !shownFiles.Contains(f!))));
var galleryBytes = Directory.GetFiles(Path.Combine(repo, "gallery"), "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
// The room stories brought their own pictures (Clayton, 2026-11-07), so the cap is 4 MB, not the room's old 3.
Check(galleryBytes < 4_000_000, $"The gallery is {galleryBytes:N0} bytes; keep it under 4 MB");
var galleryText = File.ReadAllText(Path.Combine(repo, Gallery.IndexPath));
// The room messages are copied as written, ticket numbers and all, but never a link: not to the room,
// not to Gitea, and above all not a signed storage URL, which carries an access key.
foreach (var leak in new[] { "http", "www.", "git.proseforge", "proseforge.ai/", "pf-image", "room?msg", "X-Amz", "digitalocean" })
    Check(!galleryText.Contains(leak, StringComparison.OrdinalIgnoreCase), $"The gallery's text contains \"{leak}\", which mustn't reach players");
// Nor our internal numbers (Clayton: "remove the numbers"): a ticket or PR shows as "#…", and commit
// and section ids go. Single digits stay, because Angel's sketches are "#1", "#2" and "#3".
var internalNumber = System.Text.RegularExpressions.Regex.Match(galleryText,
    @"#\d{2,}|\b(?=[0-9a-f]*\d)(?=[0-9a-f]*[a-f])[0-9a-f]{7,40}\b(?!\.webp|-\d)|\b[0-9a-f]{8}-[0-9a-f]{4}-");
Check(!internalNumber.Success, $"The gallery's text still has an internal number: \"{internalNumber.Value}\"");
Console.WriteLine($"PASS: art gallery: {shippedGallery.Panels.Count} panels, {shippedGallery.Panels.Sum(p => p.AllRoom.Count)} room messages, {shownFiles.Count} pictures, {galleryBytes / 1024} KB; refuses bad content, no links, hides until earned or switched on");
}

// The earned unlock: one total across every theme, due once at 100, never taken back.
var unlockSlots = new MemorySlots();
var unlock = new EarnedUnlock(unlockSlots);
Check(unlock.Total == 0 && !unlock.Unlocked && !unlock.Due, "A new player started with deliveries or the unlock");
unlock.Add(99);
Check(!unlock.Unlocked && !unlock.Due, "99 deliveries unlocked the room");
unlock.Add(0); unlock.Add(-5);
Check(unlock.Total == 99, "A zero or negative count changed the total");
unlock.Add(1);
Check(unlock.Total == 100 && unlock.Unlocked && unlock.Due, "The 100th delivery didn't unlock the room");
unlock.MarkShown();
unlock.Add(50);
Check(unlock.Unlocked && !unlock.Due, "The celebration was due again after it had played");
Check(new EarnedUnlock(unlockSlots).Unlocked && !new EarnedUnlock(unlockSlots).Due, "The unlock didn't survive a restart");
unlock.StartFrom([500]);
Check(unlock.Total == 150, "Earlier games were counted again on a later start");

var earlier = new EarnedUnlock(new MemorySlots());
earlier.StartFrom([40, 0, 70]);
Check(earlier.Total == 110 && earlier.Due, "Deliveries from before the unlock existed didn't count across themes");
var nothingYet = new MemorySlots();
new EarnedUnlock(nothingYet).StartFrom([]);
new EarnedUnlock(nothingYet).StartFrom([300]);
Check(new EarnedUnlock(nothingYet).Total == 0, "A second start re-read the games after a first start found none");

var damaged = new MemorySlots();
damaged.Set(EarnedUnlock.TotalKey, "lots");
Check(new EarnedUnlock(damaged).Total == 0, "A damaged total wasn't read as zero");
damaged.Set(EarnedUnlock.TotalKey, "-200");
Check(new EarnedUnlock(damaged).Total == 0, "A negative total was read back");
var huge = new EarnedUnlock(new MemorySlots());
huge.Add(long.MaxValue); huge.Add(long.MaxValue);
Check(huge.Total == long.MaxValue, "The total wrapped around");

// the open-source licences. Every package the app references and every bundled font is in the
// generated notices file, and every licence text a component points at is there to read.
var notices = ProseForge.Game.Presentation.Notices.Parse(File.ReadAllText(Path.Combine(repo, "THIRD_PARTY_NOTICES.md")));
var referenced = System.Text.RegularExpressions.Regex.Matches(
        File.ReadAllText(Path.Combine(repo, "src/ProseForge.Game/ProseForge.Game.csproj")), "<PackageReference Include=\"([^\"]+)\"")
    .Select(m => m.Groups[1].Value).ToList();
Check(referenced.Count >= 3, "control: the csproj's package references weren't read");
foreach (var package in referenced)
    Check(notices.Components.Any(c => c.Name == package), $"{package} is referenced by the app but missing from THIRD_PARTY_NOTICES.md (run make notices)");
foreach (var font in Directory.GetFiles(Path.Combine(repo, "src/ProseForge.Game/Resources/Fonts"), "*.ttf"))
    Check(notices.Components.Any(c => c.Name.Contains(Path.GetFileName(font))), $"{Path.GetFileName(font)} ships but isn't credited in THIRD_PARTY_NOTICES.md");
var textIds = notices.Texts.Select(t => t.Id).ToHashSet();
foreach (var c in notices.Components)
{
    Check(c.TextIds.Count > 0, $"{c.Name} points at no licence text");
    Check(c.TextIds.All(textIds.Contains), $"{c.Name} points at a licence text that isn't in the file");
}
Check(notices.Texts.Any(t => t.Body.Contains("Permission is hereby granted, free of charge")), "the MIT licence text is missing");
Check(File.Exists(Path.Combine(repo, "src/ProseForge.Game/Resources/Fonts/LICENSE.txt")), "the fonts' licence isn't beside them");
Console.WriteLine($"PASS: open-source licences: {notices.Components.Count} components, every package and font credited, {notices.Texts.Count} licence texts");

Console.WriteLine("PASS: earned unlock: 100 deliveries across every theme, earlier games counted once, due once, kept");

const string feed = """
    {"books":[
      {"title":" First book ","tagline":"A little tale","slug":"first","listings":[{"url":"http://unsafe.example/book"},{"url":"https://books.example/book"}]},
      {"title":"One More Story","slug":"one-more-story","listings":[]},
      {"title":"","listings":[]}
    ]}
    """;
var catalog = BookCatalog.Parse(feed);
Check(catalog.Books.Count == 2 && catalog.Books[0].Title == "First book" &&
      catalog.Books[0].StoreUrl == "https://books.example/book" &&
      catalog.FindFeatured("one-more-story")?.Title == "One More Story" && catalog.FeaturedBook is null,
    "Book listing parse/order/feature failed");
Check(BookCatalog.Parse("{\"books\":[]}").FeaturedBook is null, "Unpublished feature was invented");
try { BookCatalog.Parse("{\"wrong\":[]}"); throw new Exception("Malformed feed accepted"); }
catch (System.Text.Json.JsonException) { }

const string reviewFeed = """
    {"books":[
      {"title":"Both stores","listings":[
        {"store":"amazon","format":"paperback","status":"live","url":"https://www.amazon.com/dp/PAPERBACK1"},
        {"store":"amazon","format":"ebook","status":"live","url":"https://amazon.com/gp/product/EBOOK00001/ref=x"},
        {"store":"amazon_ca","format":"ebook","status":"live","url":"https://www.amazon.ca/dp/CAEBOOK001"}]},
      {"title":"US only","listings":[
        {"store":"amazon","format":"ebook","status":"draft","url":"https://www.amazon.com/dp/DRAFTEBOOK"},
        {"store":"amazon","format":"hardcover","status":"live","url":"https://www.amazon.com/dp/HARDCOVER1"}]},
      {"title":"Bad URLs","listings":[
        {"store":"amazon","format":"ebook","status":"live","url":"http://www.amazon.com/dp/INSECURE01"},
        {"store":"amazon","format":"paperback","status":"live","url":"https://www.amazon.ca/dp/WRONGHOST1"},
        {"store":"amazon","format":"hardcover","status":"live","url":"https://evil.example/dp/EVILHOST01"},
        {"store":"amazon_ca","format":"ebook","status":"live","url":"https://www.amazon.ca/s?k=NOTANASIN1"},
        {"store":"amazon_ca","format":"paperback","status":"live","url":"https://www.amazon.ca/dp/short"}]},
      {"title":"Play only","listings":[
        {"store":"google_play","format":"ebook","status":"live","url":"https://play.google.com/store/books/details?id=x"}]}
    ]}
    """;
var reviews = BookCatalog.Parse(reviewFeed).Books;
Check(reviews[0].ReviewUrlFor("US") == "https://www.amazon.com/review/create-review?asin=EBOOK00001",
    "Review link did not prefer the ebook ASIN from a /gp/product URL");
Check(reviews[0].ReviewUrlFor("ca") == "https://www.amazon.ca/review/create-review?asin=CAEBOOK001",
    "Canadian region did not get the amazon.ca review page");
Check(reviews[1].ReviewUrlFor("CA") == "https://www.amazon.com/review/create-review?asin=HARDCOVER1",
    "Canadian region did not fall back to amazon.com, or a draft listing was used");
Check(reviews[2].ReviewUrlFor("US") is null && reviews[2].ReviewUrlFor("CA") is null,
    "An insecure, wrong-host or non-ASIN URL produced a review link");
Check(reviews[3].ReviewUrlFor("US") is null && reviews[3].Reviews is { Count: 0 },
    "A book with no Amazon listing got a review link");
Check(catalog.Books[0].ReviewUrlFor("US") is null, "Listings without a store produced a review link");
Console.WriteLine("PASS: review links follow the website's Amazon rules and device region");

var cacheDirectory = Path.Combine(Path.GetTempPath(), "proseforge-book-catalog-test-" + Guid.NewGuid());
try
{
    var handler = new CatalogHandler(feed);
    var service = new BookCatalogService(new HttpClient(handler), cacheDirectory);
    var fresh = await service.LoadAsync();
    Check(!fresh.IsCached && fresh.Catalog.Books.Count == 2, "Fresh listing load failed");
    handler.Response = "{\"wrong\":[]}";
    var fallback = await service.LoadAsync();
    Check(fallback.IsCached && fallback.Catalog.Books.Count == 2,
        "Invalid response did not retain the last good catalogue");
    handler.Fail = true;
    var offline = await service.LoadAsync();
    Check(offline.IsCached && offline.Catalog.Books.Count == 2,
        "Offline listing fallback failed");
}
finally
{
    if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, true);
}
Console.WriteLine("PASS: public book catalogue parse, feature, safe links, and offline cache");

// The About page: covers only over https, a list refreshed when it's 30 minutes old, cover
// files named so no URL can escape their folder, and covers fetched in the background after a refresh.
const string coverFeed = """
    {"books":[
      {"title":"Safe","coverUrl":"https://cdn.example/a/b/cover.png","listings":[]},
      {"title":"Plain http","coverUrl":"http://cdn.example/cover.png","listings":[]},
      {"title":"None","listings":[]}
    ]}
    """;
var covers = BookCatalog.Parse(coverFeed).Books;
Check(covers[0].CoverUrl == "https://cdn.example/a/b/cover.png" && covers[0].HasCover
    && covers[1].CoverUrl is null && covers[2].CoverUrl is null && !covers[2].HasCover,
    "A cover over plain http was kept, or a safe one dropped");
var now = DateTimeOffset.UtcNow;
Check(BookCatalogService.IsStale(null, now) && !BookCatalogService.IsStale(now.AddMinutes(-10), now)
    && BookCatalogService.IsStale(now.AddMinutes(-31), now),
    "The book list's 30-minute refresh rule is wrong");
var coverName = BookCatalogService.CoverFileName("https://cdn.example/a/../../secret/cover.png?x=1");
Check(coverName.Length == 36 && coverName.EndsWith(".png") && !coverName.Contains('/') && !coverName.Contains("..")
    && coverName == BookCatalogService.CoverFileName("https://cdn.example/a/../../secret/cover.png?x=1")
    && BookCatalogService.CoverFileName("https://cdn.example/cover.exe").EndsWith(".img"),
    "Cover file names aren't stable, safe and typed");

var aboutCache = Path.Combine(Path.GetTempPath(), "proseforge-about-test-" + Guid.NewGuid());
try
{
    var aboutHandler = new CatalogHandler(coverFeed);
    var about = new BookCatalogService(new HttpClient(aboutHandler), aboutCache);
    Check(await about.GetCachedAsync() is null && about.NeedsRefresh, "A list appeared before anything was fetched");
    var coversArrived = new TaskCompletionSource();
    about.Updated += () => { if (about.CoverFile("https://cdn.example/a/b/cover.png") is not null) coversArrived.TrySetResult(); };
    var refreshed = await about.RefreshAsync();
    Check(!refreshed.IsCached && refreshed.Catalog.Books.Count == 3 && !about.NeedsRefresh, "Refresh didn't fetch the list");
    Check(await Task.WhenAny(coversArrived.Task, Task.Delay(5000)) == coversArrived.Task,
        "The cover wasn't downloaded in the background after a refresh");
    var reopened = new BookCatalogService(new HttpClient(aboutHandler), aboutCache);
    aboutHandler.Fail = true;
    Check(await reopened.GetCachedAsync() is { IsCached: true, Catalog.Books.Count: 3 }
        && reopened.CoverFile("https://cdn.example/a/b/cover.png") is not null,
        "A new launch didn't show the saved list and cover without the network");
}
finally
{
    if (Directory.Exists(aboutCache)) Directory.Delete(aboutCache, true);
}
Console.WriteLine("PASS: About books: https covers, 30-minute refresh, safe cover files, background cover download, saved copy offline");

// ---- Power-ups and level-ups: bought boosts that run out, earned boosts that stay ----
// One maker on a two-second job, paid on delivery, so every delivery is easy to count.
var boostLine = new Gameplay("boost-test",
    [new Station("make", 2, 10, StartingWorkers: 1, SellPrice: 5)],
    PayoutRule.FinalStageOnly,
    PowerUps: [new PowerUp("rush", Cost: 10, Seconds: 4, new Boost(BoostKind.Speed, Boost.Everywhere, Factor: 2))],
    Levels:
    [
        new LevelUp("apprentice", Delivered: 3, new Boost(BoostKind.Price, "make", Add: 1)),
        new LevelUp("master", Delivered: 6, new Boost(BoostKind.Speed, "make", Factor: 1.25)),
    ]);
EngineState Running(long coins = 0, long delivered = 0) =>
    EngineState.New(boostLine) with { Started = true, Coins = coins, Delivered = delivered };
EngineState Play(EngineState st, int seconds)
{
    for (var t = 0; t < seconds; t++) st = GameEngine.Tick(boostLine, st, 1);
    return st;
}

// The control: no boosts, one delivery every two seconds.
Check(Play(Running(), 4).Delivered == 2, "The unboosted line didn't deliver one every two seconds");

// A rush costs its coins and doubles the speed for its seconds, then wears off.
var rushed = GameEngine.StartPowerUp(boostLine, Running(coins: 10), 0)!;
Check(rushed.Coins == 0 && GameEngine.PowerUpLeft(rushed, 0) == 4, "Starting a rush didn't cost 10 and run 4 seconds");
var afterRush = Play(rushed, 4);
Check(afterRush.Delivered == 4 && GameEngine.PowerUpLeft(afterRush, 0) == 0,
    "A rush didn't double the deliveries for its four seconds, or didn't run out");
Check(Play(afterRush, 2).Delivered == 5, "The line stayed fast after the rush ran out");

// Buying it again while it runs resets the timer; it never doubles the effect.
var midRush = Play(GameEngine.StartPowerUp(boostLine, Running(coins: 20), 0)!, 3);
Check(GameEngine.PowerUpLeft(midRush, 0) == 1, "Three seconds didn't use three seconds of the rush");
var again = GameEngine.StartPowerUp(boostLine, midRush, 0)!;
Check(again.Coins == midRush.Coins - 10 && GameEngine.PowerUpLeft(again, 0) == 4,
    "Buying a running rush didn't cost 10 and reset its timer to the full 4 seconds");
Check(GameEngine.Effective(boostLine, again, 0).SecondsPerJob == 1, "Two rushes stacked into more than double speed");

// Refused, and nothing half-changes: too poor, paused, closed, or a power-up that doesn't exist.
Check(GameEngine.StartPowerUp(boostLine, Running(coins: 9), 0) is null, "A rush started without enough coins");
Check(GameEngine.StartPowerUp(boostLine, Running(coins: 10) with { Paused = true }, 0) is null, "A rush started while paused");
Check(GameEngine.StartPowerUp(boostLine, EngineState.New(boostLine) with { Coins = 10 }, 0) is null, "A rush started before opening");
Check(GameEngine.StartPowerUp(boostLine, Running(coins: 10), 1) is null && GameEngine.StartPowerUp(boostLine, Running(coins: 10), -1) is null,
    "A power-up that doesn't exist started");

// Paused time doesn't use up a power-up.
var pausedRush = GameEngine.TogglePause(rushed)!;
Check(GameEngine.PowerUpLeft(GameEngine.Tick(boostLine, pausedRush, 1), 0) == 4, "A paused game used up its rush");

// Levels come from what has been delivered and stay: no state of their own to lose.
Check(GameEngine.LevelsReached(boostLine, Running(delivered: 2)) == 0
    && GameEngine.LevelsReached(boostLine, Running(delivered: 3)) == 1
    && GameEngine.LevelsReached(boostLine, Running(delivered: 99)) == 2, "Levels weren't counted from deliveries");
Check(GameEngine.Effective(boostLine, Running(delivered: 2), 0).SellPrice == 5
    && GameEngine.Effective(boostLine, Running(delivered: 3), 0).SellPrice == 6, "The first level's +1 price didn't apply from 3 delivered");
var paidMore = Play(Running(delivered: 3), 2);
Check(paidMore.Coins == 6, "A delivery after the first level wasn't paid the boosted price");

// A level's boost and a power-up combine: 1.25 times 2 is 2.5 times as fast.
var both = GameEngine.StartPowerUp(boostLine, Running(coins: 10, delivered: 6), 0)!;
Check(Math.Abs(GameEngine.Effective(boostLine, both, 0).SecondsPerJob - 2 / 2.5) < 1e-9, "A level and a power-up didn't combine");

// A batch boost adds to what each job handles, on a receiving station and on the first one.
var batchLine = workshop with
{
    Id = "batch-test",
    PowerUps = [new PowerUp("big", 5, 10, new Boost(BoostKind.Batch, "deliver", Add: 2))],
};
var batched = GameEngine.StartPowerUp(batchLine, EngineState.New(batchLine) with { Started = true, Coins = 5 }, 0)!;
Check(GameEngine.Effective(batchLine, batched, 2).BatchSize == 7 && GameEngine.SendSize(batchLine, batched, 2) == 5,
    "A batch boost changed what a sleigh takes in, or not what it delivers");

// From review: a batch boost adds finished items per job; what a job takes in doesn't
// change, so a queue that was enough before the boost is still enough. A full sleigh (5 boxes)
// delivers 5 without the boost and 7 with it, and never stalls waiting for 7.
EngineState FullSleigh(EngineState st) => st.With(2, st.Stations[2] with { Waiting = 5 });
var plainSleigh = GameEngine.Tick(batchLine, FullSleigh(EngineState.New(batchLine) with { Started = true }), 1);
var boostedSleigh = GameEngine.Tick(batchLine, FullSleigh(batched), 1);
Check(plainSleigh.Delivered == 5 && boostedSleigh.Delivered == 7 && boostedSleigh.Stations[2].Waiting == 0,
    "A batch boost stalled a ready sleigh, or didn't add two to its delivery");

// From a second review: a capped receiving station under a batch boost must not eat input it
// can't turn into output. Batch 5, 10 waiting, 2 workers, room for 6, +2: it makes 6 (the cap) from
// one batch of 5, so 5 stay waiting. The unboosted control keeps the old rule: 6 made from 6.
var cappedLine = new Gameplay("capped-test",
    [
        new Station("make", 1, 10, StartingWorkers: 1),
        new Station("pack", 1, 10, StartingWorkers: 2, SellPrice: 1, OutputCap: 6, BatchSize: 5),
    ],
    PayoutRule.AnyStage,
    PowerUps: [new PowerUp("big", 1, 10, new Boost(BoostKind.Batch, "pack", Add: 2))]);
EngineState Queued(EngineState st) => st.With(1, st.Stations[1] with { Waiting = 10 });
var cappedPlain = GameEngine.Tick(cappedLine, Queued(EngineState.New(cappedLine) with { Started = true }), 1);
Check(cappedPlain.Stations[1].Stock == 6 && cappedPlain.Stations[1].Waiting == 4,
    "The control changed: an unboosted capped station should make 6 from 6 and leave 4 waiting");
var cappedBoost = GameEngine.Tick(cappedLine,
    Queued(GameEngine.StartPowerUp(cappedLine, EngineState.New(cappedLine) with { Started = true, Coins = 1 }, 0)!), 1);
Check(cappedBoost.Stations[1].Stock == 6 && cappedBoost.Stations[1].Waiting == 5,
    $"A capped station under a batch boost lost input: stock {cappedBoost.Stations[1].Stock}, waiting {cappedBoost.Stations[1].Waiting} (want 6 and 5)");

// From review: a power-up that runs out partway through a tick boosts only that part.
// A two-second job with half a second of double speed left, then one more half second at normal
// speed: 0.5 x 2 + 0.5 = 1.5 of the 2 seconds' work, so nothing is delivered yet.
var lastHalf = GameEngine.StartPowerUp(boostLine, Running(coins: 10), 0)!;
lastHalf = lastHalf with { PowerUpsLeft = lastHalf.PowerUpsLeft.SetItem(0, 0.5) };
var split = GameEngine.Tick(boostLine, lastHalf, 1);
Check(split.Delivered == 0 && Math.Abs(split.Stations[0].Progress - 1.5) < 1e-9 && GameEngine.PowerUpLeft(split, 0) == 0,
    "A tick that outlasted the power-up was boosted for all of it");

// Saves keep a running power-up; a save from before power-ups loads with none running.
var savedRush = EngineSaves.Restore(boostLine, EngineSaves.Capture(boostLine, Play(rushed, 1)))!;
Check(GameEngine.PowerUpLeft(savedRush, 0) == 3, "A running power-up wasn't saved");
var oldSave = EngineSaves.Capture(boostLine, Running(coins: 7)) with { PowerUps = null };
var fromOld = EngineSaves.Restore(boostLine, oldSave);
Check(fromOld is not null && fromOld.Coins == 7 && GameEngine.PowerUpLeft(fromOld, 0) == 0,
    "A save from before power-ups didn't load");
Check(EngineSaves.Restore(boostLine, oldSave with { PowerUps = new Dictionary<string, double> { ["rush"] = -1 } }) is null
    && EngineSaves.Restore(boostLine, oldSave with { PowerUps = new Dictionary<string, double> { ["rush"] = 99 } }) is null,
    "A save with impossible power-up time loaded");

// The theme check names a bad entry. Positive control: the guild's own gameplay with one good power-up.
const string goodPowerUp = "\"powerUps\": [ { \"id\": \"rush\", \"cost\": 40, \"seconds\": 30, \"effect\": { \"kind\": \"Speed\", \"station\": \"*\", \"factor\": 2 } } ], \"stations\": [";
// The rules alone aren't enough: the screen needs a label and a message from theme.json.
Check(Says(Broken("themes/guild/gameplay.json", "\"stations\": [", goodPowerUp), ProblemLevel.Error, "no powerUps entry"),
    "A power-up with no words for the screen wasn't caught");
ThemeLoad WithBoth(string gameplay, string look)
{
    var files = new Dictionary<string, string>(guildFiles);
    files["themes/guild/gameplay.json"] = files["themes/guild/gameplay.json"].Replace("\"stations\": [", gameplay);
    files["themes/guild/theme.json"] = files["themes/guild/theme.json"].Replace("\"strings\": {", look);
    return ThemePacks.Load("guild", path =>
        files.TryGetValue(path, out var text) ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)) : FromDisk(path));
}
const string rushLook = "\"powerUps\": [ { \"id\": \"rush\", \"label\": \"Midnight Oil\", \"message\": \"Candles lit.\" } ], \"strings\": {";
var withPowerUp = WithBoth(goodPowerUp, rushLook);
Check(withPowerUp.Loaded && withPowerUp.Problems.Count == 0 && withPowerUp.Theme!.Gameplay.AllPowerUps.Count == 1
    && withPowerUp.Theme.PowerUpLookFor("rush")?.Label == "Midnight Oil",
    "A good power-up with its words didn't load clean: " + string.Join("; ", withPowerUp.Problems));
Check(Says(WithBoth("\"stations\": [", rushLook), ProblemLevel.Warning, "no power-up with that id"),
    "Words for a power-up the rules don't have weren't flagged");
ThemeLoad BadPowerUp(string find, string replace) =>
    Broken("themes/guild/gameplay.json", "\"stations\": [", goodPowerUp.Replace(find, replace));
Check(Says(BadPowerUp("\"station\": \"*\"", "\"station\": \"kitchen\""), ProblemLevel.Error, "\"kitchen\""),
    "A boost naming a station that doesn't exist wasn't caught");
Check(Says(BadPowerUp("\"cost\": 40", "\"cost\": 0"), ProblemLevel.Error, "cost"), "A free power-up wasn't caught");
Check(Says(BadPowerUp("\"seconds\": 30", "\"seconds\": 0"), ProblemLevel.Error, "seconds"), "A power-up that never runs wasn't caught");
Check(Says(BadPowerUp("\"factor\": 2", "\"factor\": 0"), ProblemLevel.Error, "factor")
    && Says(BadPowerUp("\"factor\": 2", "\"factor\": 0.5"), ProblemLevel.Error, "factor"),
    "A speed boost of zero, or a slow-down, wasn't caught");
Check(Says(Broken("themes/guild/gameplay.json", "\"stations\": [",
        "\"levels\": [ { \"id\": \"b\", \"delivered\": 50, \"reward\": { \"kind\": \"Price\", \"station\": \"write\", \"add\": 1 } }, "
        + "{ \"id\": \"a\", \"delivered\": 20, \"reward\": { \"kind\": \"Price\", \"station\": \"write\", \"add\": 1 } } ], \"stations\": ["),
        ProblemLevel.Error, "order"),
    "Levels out of order weren't caught");

// The screen's half, without a phone: the dock button, the level in the dock, the glow.
var bakeryLoad = ThemePacks.Load("bakery", FromDisk);
Check(bakeryLoad.Loaded && !bakeryLoad.Problems.Any(p => p.Level == ProblemLevel.Error)
    && bakeryLoad.Theme!.Gameplay.AllPowerUps.Count == 3 && bakeryLoad.Theme.Gameplay.AllLevels.Count == 3,
    "The Bakery's power-up and levels didn't load: " + string.Join("; ", bakeryLoad.Problems));
var bakery = bakeryLoad.Theme!;
var bakeryRules = bakery.Gameplay;
// The oven is the Bakery's Big Batch: the second of its three buttons. It unlocks at 25 delivered and,
// as the second of three on a 60-second cycle, visits from 20 seconds of play to 35.
var shop = EngineState.New(bakeryRules) with { Started = true, Delivered = 25, PlayedSeconds = 20 };
var ovenButton = PowerUpButton.For(bakeryRules, shop with { Coins = 59 }, bakery, 1);
Check(ovenButton.Visible && ovenButton.Text == "Hot Oven · 60" && !ovenButton.Enabled && !ovenButton.Running, "The Hot Oven button wasn't shown, priced, and too dear at 59");
var heating = GameEngine.StartPowerUp(bakeryRules, shop with { Coins = 60 }, 1)!;
var hot = PowerUpButton.For(bakeryRules, GameEngine.Tick(bakeryRules, heating, 0.5), bakery, 1);
Check(hot.Running && hot.Text == "Hot Oven · 45s" && Math.Abs(hot.LeftFraction - 44.5 / 45) < 1e-9,
    "The running Hot Oven didn't count its seconds down");
Check(Boosts.Boosted(bakeryRules, heating, 1) && !Boosts.Boosted(bakeryRules, heating, 0) && !Boosts.Boosted(bakeryRules, shop, 1),
    "Only the bake station should glow while the oven is hot");
Check(Boosts.CurrentLevel(bakeryRules, shop with { Delivered = 0 }, bakery) is null
    && Boosts.CurrentLevel(bakeryRules, shop with { Delivered = 25 }, bakery)?.Label == "Head Baker"
    && Boosts.CurrentLevel(bakeryRules, shop with { Delivered = 250 }, bakery)?.Label == "Master Baker",
    "The level shown in the dock wasn't the highest reached");
// Every shipped pack has a power-up now, so take one away to get a pack without.
var santaWithPowerUps = ThemePacks.Load("santa", FromDisk).Theme!;
var plainPack = santaWithPowerUps with { Gameplay = santaWithPowerUps.Gameplay with { PowerUps = null }, PowerUps = null };
Check(santaWithPowerUps.Gameplay.AllPowerUps.Count > 0 && plainPack.Gameplay.AllPowerUps.Count == 0
    && PowerUpButton.For(plainPack.Gameplay, EngineState.New(plainPack.Gameplay), plainPack, 0) == PowerUpButton.None
    && PowerUpButton.All(plainPack.Gameplay, EngineState.New(plainPack.Gameplay), plainPack).Count == 0,
    "A pack without power-ups showed the button");
// The guild is the second pack with the full set, art included.
Check(guildTheme.Gameplay.AllPowerUps.Count == 3 && guildTheme.Gameplay.AllLevels.Count == 3
    && guildTheme.PowerUpLookFor("midnight-oil")?.Icon == "themes/guild/art/powerup.png"
    && guildTheme.LevelLookFor("master-storykeeper")?.Badge == "themes/guild/art/levels/level-3.png"
    && bakery.PowerUpLookFor("hot-oven")?.Icon == "themes/bakery/art/powerup.png",
    "The guild's or bakery's power-up art didn't resolve inside the pack");
// A 1.3 save, made when the Hot Oven was the Bakery's only power-up, loads in 1.4 with the oven's
// time on its new (second) button: saves keep power-up timers by id, not by place.
var bakery13 = bakeryRules with { PowerUps = [bakeryRules.AllPowerUps.Single(p => p.Id == "hot-oven")] };
var oldOven = GameEngine.Tick(bakery13, GameEngine.StartPowerUp(bakery13, EngineState.New(bakery13) with { Started = true, Coins = 60, Delivered = 25 }, 0)!, 1);   // one tick counts at most a second
var oldSaveJson = System.Text.Json.JsonSerializer.Serialize(EngineSaves.Capture(bakery13, oldOven));
var oldSaveLoaded = EngineSaves.Restore(bakeryRules, System.Text.Json.JsonSerializer.Deserialize<EngineSnapshot>(oldSaveJson));
Check(oldSaveLoaded is not null && GameEngine.PowerUpLeft(oldSaveLoaded, 1) == 44 && GameEngine.PowerUpLeft(oldSaveLoaded, 0) == 0 && GameEngine.PowerUpLeft(oldSaveLoaded, 2) == 0
    && PowerUpButton.For(bakeryRules, oldSaveLoaded, bakery, 1).Text == "Hot Oven · 44s",
    $"A 1.3 save's running Hot Oven didn't land on its new button: {(oldSaveLoaded is null ? "refused" : string.Join(", ", Enumerable.Range(0, 3).Select(i => GameEngine.PowerUpLeft(oldSaveLoaded, i))))}");
// Three power-ups a world, each a different job: a Rush (Speed), a Big Batch (Batch) and a
// Payday (Price), every one with words on the screen, and the Rush the cheapest and the Payday the dearest.
foreach (var id in packFolders)
{
    var pack = ThemePacks.Load(id!, FromDisk).Theme!;
    var ups = pack.Gameplay.AllPowerUps;
    var rush = ups.FirstOrDefault(p => p.Effect.Kind == BoostKind.Speed);
    var payday = ups.FirstOrDefault(p => p.Effect.Kind == BoostKind.Price);
    Check(ups.Count == 3 && ups.Select(p => p.Effect.Kind).Distinct().Count() == 3 && ups.All(p => pack.PowerUpLookFor(p.Id) is not null)
        && rush is not null && payday is not null && ups.All(p => p.Cost >= rush.Cost && p.Cost <= payday.Cost),
        $"{id} doesn't have a Rush, a Big Batch and a Payday, priced cheapest to dearest: "
        + string.Join(", ", ups.Select(p => $"{p.Id} {p.Effect.Kind} {p.Cost}")));
}
// Each button keeps its own clock, so two run together. The Rush visits from 45 seconds of play and the
// Payday from 60: buy the Rush, and it's still running when the Payday arrives. The Big Batch is
// away then (it visits 20 to 35 seconds into each minute), so its place on the dock is empty.
var guildShop = EngineState.New(Gameplays.Guild) with { Started = true, Coins = 200, Delivered = 100, PlayedSeconds = 50 };
var rushOnly = GameEngine.Tick(Gameplays.Guild, GameEngine.StartPowerUp(Gameplays.Guild, guildShop, 0)!, 1);
var rushAndFeast = GameEngine.Tick(Gameplays.Guild,
    GameEngine.StartPowerUp(Gameplays.Guild, rushOnly with { PlayedSeconds = 60 }, 2)!, 0.5);
var dock = PowerUpButton.All(Gameplays.Guild, rushAndFeast, guildTheme);
Check(dock.Count == 3 && dock[0].Running && dock[2].Running && !dock[1].Visible
    && dock[0].Text == "Midnight Oil · 29s" && dock[2].Text == "Feast Day · 30s"
    // On the button the name sits over the price, or over the seconds left.
    && dock[0].Name == "Midnight Oil" && dock[0].Detail == "29s" && dock[0].Ticking,
    "The dock's buttons didn't each keep their own clock: " + string.Join(" | ", dock.Select(b => b.Visible ? b.Text : "(away)")));
var quillVisit = PowerUpButton.For(Gameplays.Guild, guildShop with { PlayedSeconds = 20 }, guildTheme, 1);
Check(quillVisit.Visible && quillVisit.Enabled && !quillVisit.Running && quillVisit.Ticking
    && quillVisit.Text == "Second Quill · 50" && quillVisit.Detail == "50" && Math.Abs(quillVisit.LeftFraction - 1) < 1e-9,
    "A visiting power-up didn't show its price, with its ring full at the start of the visit");
// Stacked, a Payday and a Rush give both: the scribe's page sells for double and is written twice as fast.
// (With 100 delivered, the first level's quarter-faster writing applies too: 4s / 2 / 1.25 = 1.6s.)
var stacked = GameEngine.Effective(Gameplays.Guild, rushAndFeast, 0);
Check(stacked.SellPrice == 10 && Math.Abs(stacked.SecondsPerJob - 1.6) < 1e-9,
    $"A Payday stacked on a Rush didn't give both: price {stacked.SellPrice}, {stacked.SecondsPerJob}s a page");
// The dock has room for three, so a pack with a fourth is refused.
Check(Says(Broken("themes/guild/gameplay.json", "{ \"id\": \"feast-day\", \"cost\": 60, \"seconds\": 30, \"unlockAt\": 100, \"every\": 90, \"stays\": 15, \"effect\": { \"kind\": \"Price\", \"station\": \"*\", \"factor\": 2 } }",
        "{ \"id\": \"feast-day\", \"cost\": 60, \"seconds\": 30, \"effect\": { \"kind\": \"Price\", \"station\": \"*\", \"factor\": 2 } }, { \"id\": \"fourth\", \"cost\": 70, \"seconds\": 30, \"effect\": { \"kind\": \"Speed\", \"station\": \"*\", \"factor\": 2 } }"), ProblemLevel.Error, "at most 3"),
    "A fourth power-up, which the dock can't show, wasn't caught");
// ---- Power-ups unlock with progress and visit for a while ----
var g = Gameplays.Guild;
var visitShop = EngineState.New(g) with { Started = true, Coins = 500 };
// Unlocks: the Big Batch at the first level (25 delivered), the Payday at the second (100). Inside its
// window but still locked, it neither shows nor sells.
var quillWindow = visitShop with { PlayedSeconds = 20 };
Check(!GameEngine.Unlocked(g, quillWindow with { Delivered = 24 }, 1) && GameEngine.Unlocked(g, quillWindow with { Delivered = 25 }, 1)
    && GameEngine.StartPowerUp(g, quillWindow with { Delivered = 24 }, 1) is null
    && !PowerUpButton.For(g, quillWindow with { Delivered = 24 }, guildTheme, 1).Visible
    && GameEngine.StartPowerUp(g, quillWindow with { Delivered = 25 }, 1) is not null,
    "The Big Batch unlocked before 25 deliveries, or not at 25");
Check(GameEngine.StartPowerUp(g, visitShop with { Delivered = 99, PlayedSeconds = 60 }, 2) is null
    && GameEngine.StartPowerUp(g, visitShop with { Delivered = 100, PlayedSeconds = 60 }, 2) is not null,
    "The Payday unlocked before 100 deliveries, or not at 100");
// Visits: the Rush is there for 15 seconds of every 45, from the start; then it's away and can't be bought.
Check(Math.Abs(GameEngine.VisitLeft(g, visitShop with { PlayedSeconds = 5 }, 0) - 10) < 1e-9
    && GameEngine.Visiting(g, visitShop with { PlayedSeconds = 14.9 }, 0) && !GameEngine.Visiting(g, visitShop with { PlayedSeconds = 15 }, 0)
    && GameEngine.StartPowerUp(g, visitShop with { PlayedSeconds = 30 }, 0) is null
    && !PowerUpButton.For(g, visitShop with { PlayedSeconds = 30 }, guildTheme, 0).Visible
    && GameEngine.Visiting(g, visitShop with { PlayedSeconds = 45 }, 0),
    "The Rush's visits weren't 15 seconds of every 45");
// Bought late in its visit, a power-up runs its full time after the window closes, and stays on the dock.
var lateRush = GameEngine.StartPowerUp(g, visitShop with { PlayedSeconds = 14 }, 0)!;
for (var i = 0; i < 5; i++) lateRush = GameEngine.Tick(g, lateRush, 1);
var lateButton = PowerUpButton.For(g, lateRush, guildTheme, 0);
Check(!GameEngine.Visiting(g, lateRush, 0) && GameEngine.PowerUpLeft(lateRush, 0) is var stillLeft && Math.Abs(stillLeft - 25) < 1e-9
    && lateButton.Visible && lateButton.Running && !lateButton.Enabled && Math.Abs(lateRush.PlayedSeconds - 19) < 1e-9,
    "A power-up stopped, or vanished from the dock, when its visit ended");
// Paused time doesn't count toward the visit clock.
Check(GameEngine.Tick(g, visitShop with { Paused = true, PlayedSeconds = 7 }, 1).PlayedSeconds == 7,
    "Paused time moved the visit clock");
// Saves keep the visit clock; an older save without one starts at 0; a broken one is refused.
var clocked = visitShop with { PlayedSeconds = 123.5 };
Check(EngineSaves.Restore(g, EngineSaves.Capture(g, clocked))?.PlayedSeconds == 123.5
    && EngineSaves.Restore(g, EngineSaves.Capture(g, clocked) with { PlayedSeconds = 0 })?.PlayedSeconds == 0
    && EngineSaves.Restore(g, EngineSaves.Capture(g, clocked) with { PlayedSeconds = -1 }) is null
    && EngineSaves.Restore(g, EngineSaves.Capture(g, clocked) with { PlayedSeconds = double.NaN }) is null,
    "The visit clock wasn't saved, or a broken one was loaded");
var preVisitSave = System.Text.Json.JsonSerializer.Deserialize<EngineSnapshot>(
    System.Text.Json.JsonSerializer.Serialize(EngineSaves.Capture(g, clocked)).Replace(",\"PlayedSeconds\":123.5", ""))!;
Check(preVisitSave.PlayedSeconds == 0 && EngineSaves.Restore(g, preVisitSave) is not null, "A save from before visits didn't load at 0");
// The theme check refuses visits that make no sense.
Check(Says(BadPowerUp("\"seconds\": 30", "\"seconds\": 30, \"every\": 20, \"stays\": 30"), ProblemLevel.Error, "stays")
    && Says(BadPowerUp("\"seconds\": 30", "\"seconds\": 30, \"stays\": 10"), ProblemLevel.Error, "stays only means")
    && Says(BadPowerUp("\"seconds\": 30", "\"seconds\": 30, \"unlockAt\": -1"), ProblemLevel.Error, "unlockAt")
    && Says(BadPowerUp("\"seconds\": 30", "\"seconds\": 30, \"every\": 4000, \"stays\": 10"), ProblemLevel.Error, "every"),
    "A visit that outlasts its cycle, stays without every, a negative unlock, or a huge cycle wasn't caught");
// Every shipped world: the Rush from the start, the Big Batch at its first level, the Payday at its second,
// each visiting for 15 seconds on its own rhythm.
foreach (var id in packFolders)
{
    var pack = ThemePacks.Load(id!, FromDisk).Theme!.Gameplay;
    var ups = pack.AllPowerUps;
    Check(ups[0].UnlockAt == 0 && ups[1].UnlockAt == pack.AllLevels[0].Delivered && ups[2].UnlockAt == pack.AllLevels[1].Delivered
        && ups.All(u => u.Every > 0 && u.Stays == 15) && ups.Select(u => u.Every).Distinct().Count() == 3,
        $"{id}'s power-ups don't unlock and visit as designed: " + string.Join(", ", ups.Select(u => $"{u.Id} {u.UnlockAt}/{u.Every}/{u.Stays}")));
}
Console.WriteLine("PASS: power-ups and levels: a rush that wears off and resets, refusals, paused time, levels from deliveries, combined boosts, batch, saves, and the theme check");

// ---- The voice line: who is speaking, and only while they are ----
var voiceWords = guildTheme.Strings;
var voiceLine = new VoiceLine(voiceWords);
var monkClip = voiceLine.Begin()!.Value;
Check(voiceLine.Started(monkClip, "Brother Ink", played: true) && voiceLine.Text == ThemeText.Fill(voiceWords.VoicePlaying, ("name", "Brother Ink")),
    "A voice that loaded didn't name its speaker");
voiceLine.Ended(monkClip);
Check(voiceLine.Text == voiceWords.VoicesReady, "The speaker's name stayed after the voice stopped: " + voiceLine.Text);
// Switched off while a clip is still loading: it must not play, and its name must not come back.
var lateClip = voiceLine.Begin()!.Value;
voiceLine.Switch(false);
Check(!voiceLine.Started(lateClip, "Brother Ink", played: true) && voiceLine.Text == voiceWords.VoicesOff && voiceLine.Begin() is null,
    "A voice that finished loading after voices were switched off still played or named itself: " + voiceLine.Text);
voiceLine.Ended(lateClip);
Check(voiceLine.Text == voiceWords.VoicesOff, "An old clip ending wrote over \"voices off\"");
// Two taps close together: the newer clip wins, and the older one ending can't clear its name.
voiceLine.Switch(true);
var olderClip = voiceLine.Begin()!.Value;
var newerClip = voiceLine.Begin()!.Value;
Check(!voiceLine.IsCurrent(olderClip) && voiceLine.IsCurrent(newerClip) && !voiceLine.Started(olderClip, "Monk", played: true)
    && voiceLine.Started(newerClip, "Bard", played: true), "An older clip wasn't dropped for a newer one");
voiceLine.Ended(olderClip);
Check(voiceLine.Text == ThemeText.Fill(voiceWords.VoicePlaying, ("name", "Bard")), "An older clip ending cleared the newer speaker's name");
var brokenClip = voiceLine.Begin()!.Value;
Check(!voiceLine.Started(brokenClip, "Bard", played: false) && voiceLine.Text == voiceWords.VoiceUnavailable, "A voice that couldn't load wasn't reported");
Console.WriteLine("PASS: the voice line names a speaker only while they speak, and the switch always wins");

sealed class CatalogHandler(string response) : HttpMessageHandler
{
    public string Response { get; set; } = response;
    public bool Fail { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (Fail) throw new HttpRequestException("Offline");
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(Response)
        });
    }
}

// A clock that moves only when the test says so.
sealed class ManualClock : TimeProvider
{
    private long ticks;
    public void Advance(double seconds) => ticks += (long)Math.Round(seconds * TimestampFrequency);
    public override long GetTimestamp() => ticks;
    public override long TimestampFrequency => 1_000_000;
}

sealed class MemorySlots : ISaveSlots
{
    private readonly Dictionary<string, string> values = [];
    public string? Get(string key) => values.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, string value) => values[key] = value;
}
