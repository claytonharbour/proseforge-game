using ProseForge.Game.Engine;
using ProseForge.Game.Themes;

namespace ProseForge.Game.Presentation;

// The line under the title that says what to aim for next. Moved out of GameViewModel so each
// step of the opening can be checked without a phone: open, unpause, first sale, staff each empty
// station in turn, then keep the last step busy.
public static class GoalText
{
    public static string Next(Gameplay game, EngineState state, ThemeStrings text)
    {
        if (!state.Started) return text.GoalStart;
        if (state.Paused) return text.GoalPaused;
        if (state.Delivered == 0) return text.GoalFirstSale;
        for (var i = 1; i < game.Stations.Count; i++)
            if (state.Stations[i].Workers == 0 && i - 1 < text.GoalHire.Count)
                return ThemeText.Fill(text.GoalHire[i - 1],
                    ("cost", GameEngine.HireCost(game, state, i)), ("firstCost", GameEngine.HireCost(game, state, 0)));
        if (state.FinishedChain) return text.GoalChainDone;
        return state.Stations[^1].Stock == 0 ? text.GoalSendToLast : text.GoalSellLast;
    }
}
