using ProseForge.Game.Engine;
using ProseForge.Game.Themes;

namespace ProseForge.Game.Presentation;

// One of the dock's power-up buttons as data: what it says, whether it can be pressed, and how much
// of its time is left for the ring. Each power-up has its own button and clock; a power-up
// the theme has no words for isn't shown.
// Text is the whole sentence a screen reader says; Name and Detail are the two lines on the button.
// Ticking is what the ring shows: the power-up's own clock while it runs, or the time left to buy it
// while it's visiting for a while.
public sealed record PowerUpButton(bool Visible, string Text, bool Enabled, bool Running, double LeftFraction, string? Icon,
    string Name = "", string Detail = "", bool Ticking = false)
{
    public static readonly PowerUpButton None = new(false, "", false, false, 0, null);

    // The dock holds at most this many; ThemePacks refuses a pack with more.
    public const int MaxOnDock = 3;

    public static IReadOnlyList<PowerUpButton> All(Gameplay game, EngineState state, Theme theme) =>
        [.. Enumerable.Range(0, Math.Min(game.AllPowerUps.Count, MaxOnDock)).Select(i => For(game, state, theme, i))];

    public static PowerUpButton For(Gameplay game, EngineState state, Theme theme, int index)
    {
        if (index < 0 || index >= game.AllPowerUps.Count || theme.PowerUpLookFor(game.AllPowerUps[index].Id) is not { } look) return None;
        var rules = game.AllPowerUps[index];
        var left = GameEngine.PowerUpLeft(state, index);
        var running = left > 0;
        // A power-up shows only while it's visiting (it can be bought) or running. Locked, or away
        // between visits, its place on the dock stays empty.
        var visit = GameEngine.VisitLeft(game, state, index);
        if (!running && visit <= 0) return None;
        var timedVisit = visit > 0 && double.IsFinite(visit);
        var seconds = (int)Math.Ceiling(left);
        var text = running
            ? ThemeText.Fill(theme.Strings.PowerUpRunning, ("label", look.Label), ("left", seconds))
            : ThemeText.Fill(theme.Strings.PowerUpButton, ("label", look.Label), ("cost", rules.Cost));
        var detail = running
            ? ThemeText.Fill(theme.Strings.PowerUpLeft, ("left", seconds))
            : ThemeText.Fill(theme.Strings.PowerUpCost, ("cost", rules.Cost));
        // Pressing it while it runs restarts the clock, so it can be bought whenever it's visiting and paid for.
        var fraction = running ? Math.Clamp(left / rules.Seconds, 0, 1)
            : timedVisit ? Math.Clamp(visit / rules.Stays, 0, 1) : 0;
        return new(true, text, state.CanAct && state.Coins >= rules.Cost && visit > 0, running,
            fraction, look.Icon, look.Label, detail, Ticking: running || timedVisit);
    }
}

public static class Boosts
{
    // The highest level reached, as the theme names it; null before the first.
    public static LevelLook? CurrentLevel(Gameplay game, EngineState state, Theme theme)
    {
        var reached = GameEngine.LevelsReached(game, state);
        return reached == 0 ? null : theme.LevelLookFor(game.AllLevels[reached - 1].Id);
    }

    // A station glows while a boost is working on it: a level reached or a power-up running.
    public static bool Boosted(Gameplay game, EngineState state, int station) =>
        GameEngine.ActiveBoosts(game, state).Any(b => b.AppliesTo(game.Stations[station].Id));
}
