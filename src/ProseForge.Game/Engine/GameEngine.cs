namespace ProseForge.Game.Engine;

// The rules of every theme, as pure functions. Each move returns the next state, or null when
// the move isn't allowed right now, so a refused tap can never half-change the game.
public static class GameEngine
{
    // A long gap is credited as at most one second of work, so a frozen screen can't flood the line.
    public const double MaxTickSeconds = 1;

    public static EngineState Tick(Gameplay game, EngineState state, double seconds)
    {
        if (!state.CanAct || !double.IsFinite(seconds) || seconds <= 0) return state;
        seconds = Math.Min(seconds, MaxTickSeconds);

        for (var i = 0; i < game.Stations.Count; i++)
            state = state.With(i, Work(game, state, i, seconds));

        return PayOut(game, PassAlong(game, state));
    }

    // How many jobs a station can work at once: its workers, unless a shared tool runs out first.
    // Twenty elves and one roll of good tape still wrap one box at a time.
    public static int Capacity(Gameplay game, EngineState state, int station)
    {
        var workers = state.Stations[station].Workers;
        var tool = game.Stations[station].NeedsTool is { } id ? game.ToolIndexOf(id) : -1;
        return tool < 0 ? workers : Math.Min(workers, state.Tools[tool]);
    }

    public static EngineState? Open(EngineState state) =>
        state.Started ? null : state with { Started = true };

    public static EngineState? TogglePause(EngineState state) =>
        state.Started ? state with { Paused = !state.Paused } : null;

    public static EngineState PauseForBackground(EngineState state) =>
        state.Started ? state with { Paused = true } : state;

    public static EngineState? Collect(Gameplay game, EngineState state, int station)
    {
        if (!state.CanAct || !Valid(game, station) || !game.Stations[station].CollectByHand) return null;
        var s = state.Stations[station];
        return s.Ready == 0 ? null : state.With(station, s with { Stock = s.Stock + s.Ready, Ready = 0 });
    }

    public static EngineState? Sell(Gameplay game, EngineState state, int station, int count = 1)
    {
        if (!state.CanAct || !Valid(game, station) || game.Payout != PayoutRule.AnyStage
            || game.Stations[station].SellPrice is not { } price || count <= 0) return null;
        var s = state.Stations[station];
        if (s.Stock < count) return null;
        return Credit(game, state.With(station, s with { Stock = s.Stock - count }), station, count, price);
    }

    public static EngineState? SellAll(Gameplay game, EngineState state, int station) =>
        Valid(game, station) ? Sell(game, state, station, state.Stations[station].Stock) : null;

    // Send finished work on by hand: as much as the next station can take in one go.
    public static EngineState? Send(Gameplay game, EngineState state, int station)
    {
        if (!state.CanAct || !Valid(game, station) || station == game.Stations.Count - 1
            || game.Stations[station].Handoff != Handoff.ByHand) return null;
        var batch = Math.Min(state.Stations[station].Stock, SendSize(game, state, station + 1));
        if (batch == 0) return null;
        var sender = state.Stations[station];
        var receiver = state.Stations[station + 1];
        return state.With(station, sender with { Stock = sender.Stock - batch })
                    .With(station + 1, receiver with { Waiting = receiver.Waiting + batch });
    }

    public static int SendSize(Gameplay game, EngineState state, int station) =>
        Capacity(game, state, station) * game.Stations[station].BatchSize;

    public static EngineState? Hire(Gameplay game, EngineState state, int station)
    {
        if (!state.CanAct || !Valid(game, station)) return null;
        var s = state.Stations[station];
        var cost = HireCost(game, state, station);
        if (s.Workers == int.MaxValue || state.Coins < cost) return null;
        return state.With(station, s with { Workers = s.Workers + 1 }) with { Coins = state.Coins - cost };
    }

    public static EngineState? Upgrade(Gameplay game, EngineState state, int tool)
    {
        if (!state.CanAct || tool < 0 || tool >= game.AllTools.Count) return null;
        var cost = UpgradeCost(game, state, tool);
        if (state.Tools[tool] == int.MaxValue || state.Coins < cost) return null;
        return state with { Tools = state.Tools.SetItem(tool, state.Tools[tool] + 1), Coins = state.Coins - cost };
    }

    // The first hire costs the station's base price; each later hire costs another half of it.
    public static int HireCost(Gameplay game, EngineState state, int station) =>
        RisingPrice(game.Stations[station].HireCost,
            state.Stations[station].Workers - game.Stations[station].StartingWorkers);

    public static int UpgradeCost(Gameplay game, EngineState state, int tool) =>
        RisingPrice(game.AllTools[tool].UpgradeCost, state.Tools[tool] - game.AllTools[tool].Starting);

    private static int RisingPrice(int basePrice, int previous)
    {
        var doubled = (long)basePrice * (2L + Math.Max(0, previous));
        return (int)Math.Min(int.MaxValue, (doubled + 1) / 2);
    }

    private static StationState Work(Gameplay game, EngineState state, int index, double seconds)
    {
        var station = game.Stations[index];
        var s = state.Stations[index];
        var capacity = Capacity(game, state, index);
        var isSource = index == 0;
        var produced = s.Ready;   // finished work lands in Ready when it's collected by hand,
        var stock = s.Stock;      // otherwise straight in Stock
        var waiting = s.Waiting;
        var progress = s.Progress;

        int Held() => station.CollectByHand ? produced : stock;
        int Jobs() => Held() >= station.OutputCap ? 0
            : isSource ? capacity
            : Math.Min(capacity, waiting / station.BatchSize);

        if (Jobs() == 0) return s with { Progress = 0 };

        progress += seconds;
        while (progress >= station.SecondsPerJob && Jobs() > 0)
        {
            progress -= station.SecondsPerJob;
            var made = isSource ? Jobs() : Jobs() * station.BatchSize;
            made = (int)Math.Min(made, (long)station.OutputCap - Held());
            if (!isSource) waiting -= made;
            if (station.CollectByHand) produced += made; else stock += made;
        }
        // A step that runs out of work starts its next job from scratch. (A full source keeps its
        // part-done job for one more tick, exactly as the guild always has.)
        if (!isSource && Jobs() == 0) progress = 0;
        return new StationState(s.Workers, produced, stock, waiting, progress);
    }

    // Stations that hand work on automatically pass along everything they finished this tick.
    private static EngineState PassAlong(Gameplay game, EngineState state)
    {
        for (var i = 0; i < game.Stations.Count - 1; i++)
        {
            var sender = state.Stations[i];
            if (game.Stations[i].Handoff != Handoff.Automatic || sender.Stock == 0) continue;
            var receiver = state.Stations[i + 1];
            state = state.With(i, sender with { Stock = 0 })
                         .With(i + 1, receiver with { Waiting = receiver.Waiting + sender.Stock });
        }
        return state;
    }

    // When only the last step pays, whatever it finished is delivered and earned at once.
    private static EngineState PayOut(Gameplay game, EngineState state)
    {
        var last = game.Stations.Count - 1;
        var done = state.Stations[last];
        if (game.Payout != PayoutRule.FinalStageOnly || done.Stock == 0) return state;
        return Credit(game, state.With(last, done with { Stock = 0 }), last, done.Stock,
            game.Stations[last].SellPrice ?? 0);
    }

    private static EngineState Credit(Gameplay game, EngineState state, int station, int count, int price) =>
        state with
        {
            Coins = state.Coins + (long)count * price,
            Delivered = state.Delivered + count,
            FinishedChain = state.FinishedChain || station == game.Stations.Count - 1,
        };

    private static bool Valid(Gameplay game, int station) => station >= 0 && station < game.Stations.Count;
}
