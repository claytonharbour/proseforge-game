using ProseForge.Game;
using ProseForge.Game.Engine;
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
for (var frame = 0; frame < 41; frame++)
{
    idleClock.Advance(0.100); idle.Session.CatchUp();
    busyClock.Advance(0.090); Check(busy.Sell(0), "Busy player's sale was refused");
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
Console.WriteLine("PASS: theme packs: the guild loads clean, missing art has a placeholder, and each broken pack names its fault");

// ---- Live theme switch: pick from the index, each theme keeps its own game, a bad pack changes nothing ----
var indexProblems = new List<ThemeProblem>();
var index = ThemeSwitch.LoadIndex(FromDisk, indexProblems)!;
Check(index is not null && indexProblems.Count == 0, "themes/index.json didn't load: " + string.Join("; ", indexProblems));
Check(index.Offered(includeDrafts: false).SequenceEqual(["guild"]) && index.Offered(includeDrafts: true).Contains("santa"),
    "A store build would offer a draft pack, or a debug build can't reach it");
Check(ThemeSwitch.Startup(index, false, "santa") == "guild" && ThemeSwitch.Startup(index, true, "santa") == "santa"
    && ThemeSwitch.Startup(index, true, "gone") == "guild" && ThemeSwitch.Startup(index, true, null) == "guild",
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
Check(!ThemeSwitch.Prepare(index, false, "santa", FromDisk, Restore).Ready, "A store build switched to a draft pack");
Check(!ThemeSwitch.Prepare(index, true, "nowhere", FromDisk, Restore).Ready, "A theme that isn't in the index loaded");

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

// The draft Santa pack plays the tape rule Clayton asked to see: boxes pile up at wrapping, more
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
Console.WriteLine("PASS: live theme switch: index checked, drafts debug-only, each theme keeps its own game, broken packs change nothing, picker pauses, tape plays");

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
