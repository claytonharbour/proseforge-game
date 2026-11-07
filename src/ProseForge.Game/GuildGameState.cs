using ProseForge.Game.Engine;

namespace ProseForge.Game;

// The guild as the page has always seen it, now a thin view over the data-driven engine.
// The rules live in GameEngine and Gameplays.Guild; this class only speaks the guild's language
// (monks, Ready, Queues) and reads and writes the original v1 save shape.
public sealed class GuildGameState
{
    private static Gameplay Guild => Gameplays.Guild;

    public static int ReadyLimit => Guild.Stations[0].OutputCap;
    public static readonly int[] Prices = [.. Gameplays.Guild.Stations.Select(s => s.SellPrice ?? 0)];
    public static readonly int[] HireCosts = [.. Gameplays.Guild.Stations.Select(s => s.HireCost)];
    public static readonly double[] WorkSeconds = [.. Gameplays.Guild.Stations.Select(s => s.SecondsPerJob)];

    public GuildGameState(EngineState? state = null, TimeProvider? clock = null) =>
        Session = new GameSession(Guild, state, clock);

    public GameSession Session { get; }
    private EngineState State => Session.State;

    public bool Started => State.Started;
    public bool Paused => State.Paused;
    public int Coins => ClampToInt(State.Coins);
    public int Sold => ClampToInt(State.Delivered);
    public bool CompletedChain => State.FinishedChain;
    public int[] Workers => [.. State.Stations.Select(s => s.Workers)];
    public double[] Progress => [.. State.Stations.Select(s => s.Progress)];
    public int Ready => State.Stations[0].Ready;
    public int[] Stock => [.. State.Stations.Select(s => s.Stock)];
    public int[] Queues => [State.Stations[1].Waiting, State.Stations[2].Waiting];
    public bool CanAct => State.CanAct;

    public bool Open() => Session.Act((_, s) => GameEngine.Open(s));
    public bool TogglePause() => Session.Act((_, s) => GameEngine.TogglePause(s));
    public void PauseForBackground() => Session.Act((_, s) => GameEngine.PauseForBackground(s));
    public void Tick(double seconds) => Session.Tick(seconds);
    public bool Collect() => Session.Act((g, s) => GameEngine.Collect(g, s, 0));
    public bool Sell(int kind) => Session.Act((g, s) => GameEngine.Sell(g, s, kind));
    public bool SellAllPerformed() => Session.Act((g, s) => GameEngine.SellAll(g, s, 2));
    public bool Hire(int role) => Session.Act((g, s) => GameEngine.Hire(g, s, role));

    // The guild only ever sends manuscripts to illustration, and illustrated books to performance.
    public bool Queue(int kind) => kind is 0 or 1 && Session.Act((g, s) => GameEngine.Send(g, s, kind));

    public int HireCost(int role)
    {
        if (role is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(role));
        return GameEngine.HireCost(Guild, State, role);
    }

    public GuildSnapshot Snapshot() => new(1, Started, Paused, Coins, Sold,
        Workers, Progress, Ready, Stock, Queues, CompletedChain);

    // Reads the original v1 save. This is also the one-time migration: the store writes it back
    // as a v2 engine save, and never writes v1 again.
    public static GuildGameState FromSnapshot(GuildSnapshot? saved, TimeProvider? clock = null)
    {
        if (saved is null || saved.Version != 1 || saved.Workers is not { Length: 3 }
            || saved.Progress is not { Length: 3 } || saved.Stock is not { Length: 3 }
            || saved.Queues is not { Length: 2 } || saved.Workers[0] < 1
            || saved.Workers.Skip(1).Any(n => n < 0)
            || saved.Progress.Any(n => !double.IsFinite(n) || n < 0 || n >= 8)
            || saved.Stock.Any(n => n < 0) || saved.Queues.Any(n => n < 0)
            || saved.Coins < 0 || saved.Sold < 0 || saved.Ready is < 0 or > 20)
            return new GuildGameState(clock: clock);

        var waiting = new[] { 0, saved.Queues[0], saved.Queues[1] };
        var state = EngineState.New(Guild) with
        {
            Started = saved.Started, Paused = saved.Paused, Coins = saved.Coins,
            Delivered = saved.Sold, FinishedChain = saved.CompletedChain,
            Stations = [.. Enumerable.Range(0, 3).Select(i => new StationState(
                saved.Workers[i], i == 0 ? saved.Ready : 0, saved.Stock[i], waiting[i], saved.Progress[i]))],
        };
        return new GuildGameState(state, clock);
    }

    private static int ClampToInt(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}

public sealed record GuildSnapshot(int Version, bool Started, bool Paused, int Coins, int Sold,
    int[] Workers, double[] Progress, int Ready, int[] Stock, int[] Queues, bool CompletedChain = false);
