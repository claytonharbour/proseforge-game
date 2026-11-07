using System.Windows.Input;
using ProseForge.Game.Engine;
using ProseForge.Game.Themes;

namespace ProseForge.Game.ViewModels;

// One row of the production line. Which buttons it shows comes from the station's rules: a desk
// you collect from, a step you send work to by hand, a step you can sell from, or none of these.
public sealed class StationViewModel : Observable
{
    private readonly GameViewModel parent;
    private string status = "", stock = "", primaryText = "", sellText = "", hireText = "";
    private bool primaryEnabled, sellEnabled, hireEnabled, actorVisible;
    private double progress;

    public StationViewModel(GameViewModel parent, int index)
    {
        this.parent = parent;
        Index = index;
        PrimaryCommand = new Command(Primary);
        SellCommand = new Command(Sell);
        HireCommand = new Command(Hire);
        SpeakCommand = new Command(() => parent.Replay(Index));
        ActorImage = AppThemes.Image(Look.Actor);
    }

    public int Index { get; }
    private Gameplay Game => parent.Rules;
    private Station Rules => Game.Stations[Index];
    private StationLook Look => parent.Theme.Stations[Index];
    private bool CollectsByHand => Rules.CollectByHand;
    private bool ReceivesByHand => Index > 0 && Game.Stations[Index - 1].Handoff == Handoff.ByHand
        && Look.SendLabel is not null;
    // A step that shares a tool (Santa's tape) gets a button that buys one more of it.
    private int Tool => Rules.NeedsTool is { } id ? Game.ToolIndexOf(id) : -1;

    public ICommand PrimaryCommand { get; }
    public ICommand SellCommand { get; }
    public ICommand HireCommand { get; }
    public ICommand SpeakCommand { get; }

    public ImageSource? ActorImage { get; }
    public bool PrimaryVisible => CollectsByHand || ReceivesByHand || Tool >= 0;
    public bool SellVisible => Game.Payout == PayoutRule.AnyStage && Rules.SellPrice is not null;

    public string Status { get => status; private set => Set(ref status, value); }
    public string Stock { get => stock; private set => Set(ref stock, value); }
    public string PrimaryText { get => primaryText; private set => Set(ref primaryText, value); }
    public bool PrimaryEnabled { get => primaryEnabled; private set => Set(ref primaryEnabled, value); }
    public string SellText { get => sellText; private set => Set(ref sellText, value); }
    public bool SellEnabled { get => sellEnabled; private set => Set(ref sellEnabled, value); }
    public string HireText { get => hireText; private set => Set(ref hireText, value); }
    public bool HireEnabled { get => hireEnabled; private set => Set(ref hireEnabled, value); }
    public bool ActorVisible { get => actorVisible; private set => Set(ref actorVisible, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }

    internal void Refresh()
    {
        var state = parent.State;
        var me = state.Stations[Index];
        var text = parent.Theme.Strings;
        var active = state.CanAct;

        Status = me.Workers == 0 ? Look.Empty : ThemeText.Fill(text.WorkerStatus,
            ("n", me.Workers), ("who", me.Workers == 1 ? Look.Worker : Look.Workers), ("activity", Look.Activity));
        Stock = ThemeText.Fill(Look.StockLabel, ("n", me.Stock), ("waiting", me.Waiting),
            ("tools", Tool >= 0 ? state.Tools[Tool] : 0));
        Progress = me.Progress / Rules.SecondsPerJob;
        ActorVisible = ActorImage is not null && me.Workers > 0;

        if (CollectsByHand)
        {
            PrimaryText = me.Ready > 0 ? ThemeText.Fill(Look.CollectLabel ?? "{n}", ("n", me.Ready)) : Look.IdleLabel ?? "";
            PrimaryEnabled = active && me.Ready > 0;
        }
        else if (ReceivesByHand)
        {
            var batch = Math.Min(state.Stations[Index - 1].Stock, GameEngine.SendSize(Game, state, Index));
            PrimaryText = ThemeText.Fill(Look.SendLabel!, ("n", batch));
            PrimaryEnabled = active && batch > 0;
        }
        else if (Tool >= 0)
        {
            var toolCost = GameEngine.UpgradeCost(Game, state, Tool);
            PrimaryText = ThemeText.Fill(Look.ToolLabel ?? "+1 {cost}", ("n", state.Tools[Tool]), ("cost", toolCost));
            PrimaryEnabled = active && state.Coins >= toolCost;
        }

        SellText = Look.SellsAll ? text.SellAll : ThemeText.Fill(text.SellOne, ("price", Rules.SellPrice ?? 0));
        SellEnabled = active && me.Stock > 0;

        var cost = GameEngine.HireCost(Game, state, Index);
        HireText = ThemeText.Fill(text.HireButton, ("label", Look.HireLabel), ("cost", cost));
        HireEnabled = active && me.Workers < int.MaxValue && state.Coins >= cost;
    }

    private void Primary()
    {
        if (CollectsByHand)
            parent.Perform((g, s) => GameEngine.Collect(g, s, Index), Look.CollectMessage ?? "");
        else if (ReceivesByHand)
            parent.Perform((g, s) => GameEngine.Send(g, s, Index - 1), Look.SendMessage ?? "");
        else if (Tool >= 0)
            parent.Perform((g, s) => GameEngine.Upgrade(g, s, Tool), Look.ToolMessage ?? "");
    }

    private void Sell()
    {
        var price = Rules.SellPrice ?? 0;
        if (Look.SellsAll)
        {
            var count = parent.State.Stations[Index].Stock;
            parent.Perform((g, s) => GameEngine.SellAll(g, s, Index),
                ThemeText.Fill(Look.SellMessage ?? "", ("n", count), ("total", (long)count * price)));
        }
        else
            parent.Perform((g, s) => GameEngine.Sell(g, s, Index),
                ThemeText.Fill(Look.SellMessage ?? "", ("price", price)));
    }

    private void Hire() =>
        parent.Perform((g, s) => GameEngine.Hire(g, s, Index), Look.HireMessage, Look.Voice, Look.Worker);
}
