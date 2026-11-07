namespace ProseForge.Game.Engine;

// A game is a production line described as data. The engine runs whatever line it is given,
// so the guild and Santa's workshop differ in numbers and rules, not in code.
public sealed record Gameplay(
    string Id,
    IReadOnlyList<Station> Stations,   // in order: the first makes, each later one transforms
    PayoutRule Payout,
    IReadOnlyList<SharedTool>? Tools = null,
    Goal? Goal = null)
{
    public IReadOnlyList<SharedTool> AllTools => Tools ?? [];

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
        PayoutRule.AnyStage);
}
