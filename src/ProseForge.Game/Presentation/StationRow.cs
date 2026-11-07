using ProseForge.Game.Engine;
using ProseForge.Game.Themes;

namespace ProseForge.Game.Presentation;

// What one station's row shows, worked out from the rules, the state and the theme's words, with
// no screen types in sight, so a test can ask "what does the wrapping row offer now?".
// StationViewModel copies these values onto the screen; it decides nothing itself.
public enum RowAction { None, Collect, Send, BuyTool }

public sealed record StationRow(
    string Status,
    string Stock,
    double Progress,
    bool ActorVisible,
    RowAction Primary,
    string PrimaryText,
    bool PrimaryEnabled,
    bool SellVisible,
    string SellText,
    bool SellEnabled,
    string HireText,
    bool HireEnabled)
{
    public bool PrimaryVisible => Primary != RowAction.None;

    public static StationRow For(Gameplay game, EngineState state, Theme theme, int index, bool hasActor)
    {
        var rules = game.Stations[index];
        var look = theme.Stations[index];
        var text = theme.Strings;
        var me = state.Stations[index];
        var active = state.CanAct;

        var status = me.Workers == 0 ? look.Empty : ThemeText.Fill(text.WorkerStatus,
            ("n", me.Workers), ("who", me.Workers == 1 ? look.Worker : look.Workers), ("activity", look.Activity));
        var tool = rules.NeedsTool is { } id ? game.ToolIndexOf(id) : -1;
        var stock = ThemeText.Fill(look.StockLabel, ("n", me.Stock), ("waiting", me.Waiting),
            ("tools", tool >= 0 ? state.Tools[tool] : 0));

        // One primary button per row, chosen by the rules: a desk you collect from, a step you send
        // work to by hand, or a shared tool you can buy more of.
        var receivesByHand = index > 0 && game.Stations[index - 1].Handoff == Handoff.ByHand && look.SendLabel is not null;
        var (action, label, enabled) = rules.CollectByHand
            ? (RowAction.Collect,
               me.Ready > 0 ? ThemeText.Fill(look.CollectLabel ?? "{n}", ("n", me.Ready)) : look.IdleLabel ?? "",
               active && me.Ready > 0)
            : receivesByHand ? SendButton(game, state, look, index, active)
            : tool >= 0 ? ToolButton(game, state, look, tool, active)
            : (RowAction.None, "", false);

        var cost = GameEngine.HireCost(game, state, index);
        return new StationRow(
            status, stock, me.Progress / rules.SecondsPerJob, hasActor && me.Workers > 0,
            action, label, enabled,
            SellVisible: game.Payout == PayoutRule.AnyStage && rules.SellPrice is not null,
            SellText: look.SellsAll ? text.SellAll : ThemeText.Fill(text.SellOne, ("price", rules.SellPrice ?? 0)),
            SellEnabled: active && me.Stock > 0,
            HireText: ThemeText.Fill(text.HireButton, ("label", look.HireLabel), ("cost", cost)),
            HireEnabled: active && me.Workers < int.MaxValue && state.Coins >= cost);
    }

    private static (RowAction, string, bool) SendButton(Gameplay game, EngineState state, StationLook look, int index, bool active)
    {
        var batch = Math.Min(state.Stations[index - 1].Stock, GameEngine.SendSize(game, state, index));
        return (RowAction.Send, ThemeText.Fill(look.SendLabel!, ("n", batch)), active && batch > 0);
    }

    private static (RowAction, string, bool) ToolButton(Gameplay game, EngineState state, StationLook look, int tool, bool active)
    {
        var cost = GameEngine.UpgradeCost(game, state, tool);
        return (RowAction.BuyTool, ThemeText.Fill(look.ToolLabel ?? "+1 {cost}", ("n", state.Tools[tool]), ("cost", cost)),
            active && state.Coins >= cost);
    }
}
