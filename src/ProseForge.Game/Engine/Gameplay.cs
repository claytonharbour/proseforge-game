namespace ProseForge.Game.Engine;

// A game is a production line described as data. The engine runs whatever line it is given,
// so the guild and Santa's workshop differ in numbers and rules, not in code.
public sealed record Gameplay(
    string Id,
    IReadOnlyList<Station> Stations,   // in order: the first makes, each later one transforms
    PayoutRule Payout,
    IReadOnlyList<SharedTool>? Tools = null,
    Goal? Goal = null,
    IReadOnlyList<PowerUp>? PowerUps = null,   // bought with coins, for a while
    IReadOnlyList<LevelUp>? Levels = null)     // earned for good by delivering enough, in order
{
    public IReadOnlyList<SharedTool> AllTools => Tools ?? [];
    public IReadOnlyList<PowerUp> AllPowerUps => PowerUps ?? [];
    public IReadOnlyList<LevelUp> AllLevels => Levels ?? [];

    public int IndexOf(string stationId)
    {
        for (var i = 0; i < Stations.Count; i++)
            if (Stations[i].Id == stationId) return i;
        return -1;
    }

    public int ToolIndexOf(string toolId)
    {
        for (var i = 0; i < AllTools.Count; i++)
            if (AllTools[i].Id == toolId) return i;
        return -1;
    }
}

public sealed record Station(
    string Id,
    double SecondsPerJob,
    int HireCost,
    int StartingWorkers = 0,
    int? SellPrice = null,          // what one finished item earns, if it can be sold here
    int OutputCap = int.MaxValue,   // finished work waiting to be collected stops at this
    bool CollectByHand = false,     // the player taps to gather finished work (the guild's desk)
    Handoff Handoff = Handoff.ByHand,
    int BatchSize = 1,              // a delivery leaves with a full load, not one item at a time
    string? NeedsTool = null);      // the step can't go faster than this shared tool allows

// How finished work reaches the next station.
public enum Handoff
{
    ByHand,     // the player sends a batch on ("Illustrate 3 →")
    Automatic,  // it joins the next station's queue as soon as it's done
}

// Where a line earns its money.
public enum PayoutRule
{
    AnyStage,        // sell at any step, or push on for more (the guild)
    FinalStageOnly,  // nothing is worth anything until the last step finishes it (the workshop)
}

// Something every worker at a station has to share: twenty elves, one roll of good tape.
public sealed record SharedTool(string Id, int Starting, int UpgradeCost);

public sealed record Goal(long Delivered);

// What a boost changes at a station. Speed divides the seconds a job takes; Batch adds to
// what each job handles; Price multiplies, then adds to, what one item sells for.
public enum BoostKind { Speed, Batch, Price }

// A boost to one station, or to every station when Station is "*".
public sealed record Boost(BoostKind Kind, string Station, double Factor = 1, int Add = 0)
{
    public const string Everywhere = "*";
    public bool AppliesTo(string stationId) => Station == Everywhere || Station == stationId;
}

// Bought with coins; runs for Seconds of play, then wears off. Buying it again restarts the clock.
// It unlocks once UnlockAt items have been delivered, and then visits: it can be bought for Stays
// seconds out of every Every, offset so a world's power-ups don't all arrive together.
// Every = 0 means it's always there once unlocked, which is how packs written before visits behave.
public sealed record PowerUp(string Id, int Cost, double Seconds, Boost Effect,
    long UnlockAt = 0, double Every = 0, double Stays = 0);

// Reached once Delivered items have been delivered, and kept for good.
public sealed record LevelUp(string Id, long Delivered, Boost Reward);

public static class Gameplays
{
    // One More Story's guild, with the numbers the game has always used.
    public static readonly Gameplay Guild = new(
        "guild",
        [
            new Station("write", SecondsPerJob: 4, HireCost: 25, StartingWorkers: 1, SellPrice: 5,
                OutputCap: 20, CollectByHand: true),
            new Station("illustrate", SecondsPerJob: 6, HireCost: 30, SellPrice: 12),
            new Station("perform", SecondsPerJob: 8, HireCost: 60, SellPrice: 24),
        ],
        PayoutRule.AnyStage,
        // themes/guild/gameplay.json says the same; a test holds them equal.
        PowerUps:
        [
            new PowerUp("midnight-oil", Cost: 40, Seconds: 30, new Boost(BoostKind.Speed, Boost.Everywhere, Factor: 2), UnlockAt: 0, Every: 45, Stays: 15),
            new PowerUp("second-quill", Cost: 50, Seconds: 30, new Boost(BoostKind.Batch, "write", Add: 1), UnlockAt: 25, Every: 60, Stays: 15),
            new PowerUp("feast-day", Cost: 60, Seconds: 30, new Boost(BoostKind.Price, Boost.Everywhere, Factor: 2), UnlockAt: 100, Every: 90, Stays: 15),
        ],
        Levels:
        [
            new LevelUp("master-scribe", Delivered: 25, new Boost(BoostKind.Speed, "write", Factor: 1.25)),
            new LevelUp("guild-archivist", Delivered: 100, new Boost(BoostKind.Price, "illustrate", Add: 3)),
            new LevelUp("master-storykeeper", Delivered: 250, new Boost(BoostKind.Price, "perform", Add: 6)),
        ]);
}
