using ProseForge.Game;

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
