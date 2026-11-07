namespace ProseForge.Game.Engine;

// A save keyed by station and tool ids, not positions, so a theme that gains a station keeps the
// player's progress in the stations it already had.
public sealed record EngineSnapshot(
    int Version,
    string Gameplay,
    bool Started,
    bool Paused,
    long Coins,
    long Delivered,
    bool FinishedChain,
    IReadOnlyList<StationSnapshot> Stations,
    IReadOnlyDictionary<string, int>? Tools = null,
    IReadOnlyDictionary<string, double>? PowerUps = null,   // seconds left, by id; older saves have none
    double PlayedSeconds = 0);                                // the visit clock; older saves start at 0

public sealed record StationSnapshot(string Id, int Workers, int Ready, int Stock, int Waiting, double Progress);

public static class EngineSaves
{
    public const int Version = 2;

    // Each theme keeps its own slot, so switching away and back returns the player to their game.
    public static string SlotKey(Gameplay gameplay) => $"save.{gameplay.Id}.v{Version}";

    public static EngineSnapshot Capture(Gameplay gameplay, EngineState state) => new(
        Version, gameplay.Id, state.Started, state.Paused, state.Coins, state.Delivered, state.FinishedChain,
        [.. gameplay.Stations.Select((s, i) =>
        {
            var st = state.Stations[i];
            return new StationSnapshot(s.Id, st.Workers, st.Ready, st.Stock, st.Waiting, st.Progress);
        })],
        gameplay.AllTools.Select((t, i) => (t.Id, Level: state.Tools[i])).ToDictionary(x => x.Id, x => x.Level),
        gameplay.AllPowerUps.Select((p, i) => (p.Id, Left: GameEngine.PowerUpLeft(state, i))).ToDictionary(x => x.Id, x => x.Left),
        state.PlayedSeconds);

    // A save that doesn't belong to this game, or that no honest game could have written, starts
    // fresh rather than loading nonsense. Unknown stations are ignored; new ones start empty.
    public static EngineState? Restore(Gameplay gameplay, EngineSnapshot? saved)
    {
        if (saved is null || saved.Version != Version || saved.Gameplay != gameplay.Id
            || saved.Coins < 0 || saved.Delivered < 0 || saved.Stations is null
            || !double.IsFinite(saved.PlayedSeconds) || saved.PlayedSeconds < 0) return null;

        var state = EngineState.New(gameplay) with
        {
            Started = saved.Started, Paused = saved.Paused, Coins = saved.Coins,
            Delivered = saved.Delivered, FinishedChain = saved.FinishedChain, PlayedSeconds = saved.PlayedSeconds,
        };
        foreach (var s in saved.Stations)
        {
            var i = gameplay.IndexOf(s.Id);
            if (i < 0) continue;
            var def = gameplay.Stations[i];
            if (s.Workers < def.StartingWorkers || s.Ready < 0 || s.Ready > def.OutputCap || s.Stock < 0
                || s.Waiting < 0 || !double.IsFinite(s.Progress) || s.Progress < 0
                || s.Progress >= def.SecondsPerJob + GameEngine.MaxTickSeconds)
                return null;
            state = state.With(i, new StationState(s.Workers, s.Ready, s.Stock, s.Waiting, s.Progress));
        }
        foreach (var (id, level) in saved.Tools ?? new Dictionary<string, int>())
        {
            var t = gameplay.ToolIndexOf(id);
            if (t < 0) continue;
            if (level < gameplay.AllTools[t].Starting) return null;
            state = state with { Tools = state.Tools.SetItem(t, level) };
        }
        foreach (var (id, left) in saved.PowerUps ?? new Dictionary<string, double>())
        {
            var p = gameplay.AllPowerUps.ToList().FindIndex(x => x.Id == id);
            if (p < 0) continue;
            if (!double.IsFinite(left) || left < 0 || left > gameplay.AllPowerUps[p].Seconds) return null;
            state = state with { PowerUpsLeft = state.PowerUpsLeft.SetItem(p, left) };
        }
        return state;
    }
}
