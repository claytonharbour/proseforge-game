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

        // A power-up that runs out partway through boosts only that part: the tick is
        // split where the first running power-up ends, and the rest is worked without it.
        while (seconds > 0)
        {
            var slice = seconds;
            foreach (var left in state.PowerUpsLeft)
                if (left > 0 && left < slice) slice = left;
            state = Step(game, state, slice);
            seconds -= slice;
        }
        return state;
    }

    private static EngineState Step(Gameplay game, EngineState state, double seconds)
    {
        for (var i = 0; i < game.Stations.Count; i++)
            state = state.With(i, Work(game, state, i, seconds));

        state = PayOut(game, PassAlong(game, state)) with { PlayedSeconds = state.PlayedSeconds + seconds };
        return state.PowerUpsLeft.IsDefaultOrEmpty ? state
            : state with { PowerUpsLeft = [.. state.PowerUpsLeft.Select(left => Math.Max(0, left - seconds))] };
    }

    // ---- Power-ups and levels ----

    // A station as the boosts make it right now: every level reached and every power-up still
    // running. The rules read a station's numbers only through here, so a boost can't be missed.
    public static Station Effective(Gameplay game, EngineState state, int station)
    {
        var s = game.Stations[station];
        double speed = 1, priceFactor = 1;
        int batchAdd = 0, priceAdd = 0;
        foreach (var boost in ActiveBoosts(game, state))
        {
            if (!boost.AppliesTo(s.Id)) continue;
            switch (boost.Kind)
            {
                case BoostKind.Speed: speed *= boost.Factor; break;
                case BoostKind.Batch: batchAdd += boost.Add; break;
                case BoostKind.Price: priceFactor *= boost.Factor; priceAdd += boost.Add; break;
            }
        }
        return s with
        {
            SecondsPerJob = s.SecondsPerJob / speed,
            BatchSize = Math.Max(1, s.BatchSize + batchAdd),
            SellPrice = s.SellPrice is { } price ? (int)Math.Round(price * priceFactor) + priceAdd : null,
        };
    }

    public static IEnumerable<Boost> ActiveBoosts(Gameplay game, EngineState state)
    {
        foreach (var level in game.AllLevels)
            if (state.Delivered >= level.Delivered) yield return level.Reward;
        for (var i = 0; i < game.AllPowerUps.Count; i++)
            if (PowerUpLeft(state, i) > 0) yield return game.AllPowerUps[i].Effect;
    }

    // Levels come from what has been delivered, so they need no state of their own and can't be lost.
    public static int LevelsReached(Gameplay game, EngineState state) =>
        game.AllLevels.Count(level => state.Delivered >= level.Delivered);

    public static double PowerUpLeft(EngineState state, int powerUp) =>
        powerUp >= 0 && powerUp < state.PowerUpsLeft.Length ? state.PowerUpsLeft[powerUp] : 0;

    // A power-up unlocks once enough has been delivered, and like levels it can't be lost.
    public static bool Unlocked(Gameplay game, EngineState state, int powerUp) =>
        powerUp >= 0 && powerUp < game.AllPowerUps.Count && state.Delivered >= game.AllPowerUps[powerUp].UnlockAt;

    // Seconds left in an unlocked power-up's visit, or 0 while it's away (or locked). One with no
    // rhythm (Every = 0) is always there. Power-up i of n arrives i/n of the way through its cycle,
    // so a world's three don't turn up together.
    public static double VisitLeft(Gameplay game, EngineState state, int powerUp)
    {
        if (!Unlocked(game, state, powerUp)) return 0;
        var p = game.AllPowerUps[powerUp];
        if (p.Every <= 0) return double.PositiveInfinity;
        var offset = p.Every * powerUp / game.AllPowerUps.Count;
        var into = (state.PlayedSeconds - offset) % p.Every;
        if (into < 0) into += p.Every;
        return into < p.Stays ? p.Stays - into : 0;
    }

    public static bool Visiting(Gameplay game, EngineState state, int powerUp) => VisitLeft(game, state, powerUp) > 0;

    // Buy a power-up while it's visiting. Buying one that's still running restarts its clock; it never
    // stacks. One that's away or still locked can't be bought.
    public static EngineState? StartPowerUp(Gameplay game, EngineState state, int powerUp)
    {
        if (!state.CanAct || powerUp < 0 || powerUp >= game.AllPowerUps.Count || !Visiting(game, state, powerUp)) return null;
        var p = game.AllPowerUps[powerUp];
        if (state.Coins < p.Cost) return null;
        var left = state.PowerUpsLeft.Length == game.AllPowerUps.Count
            ? state.PowerUpsLeft
            : [.. game.AllPowerUps.Select((_, i) => PowerUpLeft(state, i))];
        return state with { Coins = state.Coins - p.Cost, PowerUpsLeft = left.SetItem(powerUp, p.Seconds) };
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
            || Effective(game, state, station).SellPrice is not { } price || count <= 0) return null;
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

    // What a station takes in at once. A batch boost adds to what it makes, not to what it needs.
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
        var station = Effective(game, state, index);
        // A batch boost adds finished items per job; what a job takes in stays the station's own batch
        // (otherwise a queue that was enough before the boost stalls under it).
        var takes = game.Stations[index].BatchSize;
        var extra = station.BatchSize - takes;
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
            : Math.Min(capacity, waiting / takes);

        if (Jobs() == 0) return s with { Progress = 0 };

        // Progress is kept in the station's own job time, and a speed boost makes it build up faster,
        // so a part-done job is the same share done whether or not a boost is running.
        var job = game.Stations[index].SecondsPerJob;
        progress += seconds * (job / station.SecondsPerJob);
        while (progress >= job && Jobs() > 0)
        {
            progress -= job;
            var jobs = Jobs();
            var room = (long)station.OutputCap - Held();
            int made;
            if (isSource)
                made = (int)Math.Min(jobs * (1L + extra), room);
            else
            {
                // Only what fits under the cap is made, and only the input for that is used:
                // each whole job takes its batch and makes batch + boost; a part job squeezed in under the
                // cap uses no more input than it makes. Unboosted, that's the old rule: made == used.
                var perJob = takes + extra;
                var whole = (int)Math.Min(jobs, room / perJob);
                made = whole * perJob;
                var used = whole * takes;
                if (whole < jobs && room - made > 0)
                {
                    var part = (int)(room - made);
                    made += part;
                    used += Math.Min(part, takes);
                }
                waiting -= used;
            }
            if (station.CollectByHand) produced += made; else stock += made;
        }
        // A step that runs out of work starts its next job from scratch. (A full source keeps its
        // part-done job for one more tick, exactly as the guild always has.)
        if (!isSource && Jobs() == 0) progress = 0;
        // Under a big boost a full source could bank more than a job; it holds one finished job at most,
        // which is also what a save allows (EngineSaves.Restore).
        if (isSource && Jobs() == 0) progress = Math.Min(progress, job);
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
            Effective(game, state, last).SellPrice ?? 0);
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
