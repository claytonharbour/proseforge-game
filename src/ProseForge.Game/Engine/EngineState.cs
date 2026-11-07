using System.Collections.Immutable;

namespace ProseForge.Game.Engine;

// Everything about a game in progress, as a value. The engine never changes a state;
// it returns a new one, so the same state and the same elapsed time always give the same result.
public sealed record EngineState(
    bool Started,
    bool Paused,
    long Coins,
    long Delivered,
    bool FinishedChain,
    ImmutableArray<StationState> Stations,
    ImmutableArray<int> Tools)
{
    // Seconds each power-up still has to run, in the gameplay's order; 0 when it isn't running.
    public ImmutableArray<double> PowerUpsLeft { get; init; } = [];

    // Seconds of play so far (paused time doesn't count): the clock power-up visits run on.
    public double PlayedSeconds { get; init; }

    public bool CanAct => Started && !Paused;

    public static EngineState New(Gameplay gameplay) => new(
        Started: false, Paused: false, Coins: 0, Delivered: 0, FinishedChain: false,
        [.. gameplay.Stations.Select(s => new StationState(s.StartingWorkers, 0, 0, 0, 0))],
        [.. gameplay.AllTools.Select(t => t.Starting)])
    {
        PowerUpsLeft = [.. gameplay.AllPowerUps.Select(_ => 0.0)],
    };

    public bool GoalReached(Gameplay gameplay) =>
        gameplay.Goal is { } goal && Delivered >= goal.Delivered;

    internal EngineState With(int station, StationState value) =>
        this with { Stations = Stations.SetItem(station, value) };
}

public sealed record StationState(
    int Workers,
    int Ready,       // finished, waiting to be collected by hand
    int Stock,       // finished and collected: can be sold or sent on
    int Waiting,     // sent here from the station before, not yet worked
    double Progress);
