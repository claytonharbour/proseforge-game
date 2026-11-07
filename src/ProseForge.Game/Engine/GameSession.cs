namespace ProseForge.Game.Engine;

// The one place a running game's time is counted. Every move first credits the work done
// since the session last looked at the clock, so a tap never costs the player the time since the
// last frame, and an active player earns exactly what an idle one does.
public sealed class GameSession
{
    private readonly TimeProvider? clock;
    private long lastLook;

    // With no clock, time moves only when Tick is called: deterministic, for tests and replays.
    public GameSession(Gameplay gameplay, EngineState? state = null, TimeProvider? clock = null)
    {
        Gameplay = gameplay;
        State = state ?? EngineState.New(gameplay);
        this.clock = clock;
        lastLook = clock?.GetTimestamp() ?? 0;
    }

    public Gameplay Gameplay { get; }
    public EngineState State { get; private set; }

    public void Tick(double seconds) => State = GameEngine.Tick(Gameplay, State, seconds);

    // Credit the time since we last looked, then remember now. Paused time is skipped, not owed.
    public void CatchUp()
    {
        if (clock is null) return;
        var now = clock.GetTimestamp();
        Tick(clock.GetElapsedTime(lastLook, now).TotalSeconds);
        lastLook = now;
    }

    // Apply a move after catching up. Returns false, changing nothing, when the move isn't allowed.
    public bool Act(Func<Gameplay, EngineState, EngineState?> move)
    {
        CatchUp();
        if (move(Gameplay, State) is not { } next) return false;
        State = next;
        return true;
    }

    // Coming back from the background: the time away is not production time.
    public void Resume() => lastLook = clock?.GetTimestamp() ?? 0;
}
