using System.Windows.Input;
using ProseForge.Game.Engine;
using ProseForge.Game.Presentation;
using ProseForge.Game.Themes;

namespace ProseForge.Game.ViewModels;

// One row of the production line, on screen. What the row shows is decided by StationRow, a plain
// class the tests can reach; this class only copies its answer onto bindable properties and
// turns taps into engine moves.
public sealed class StationViewModel : Observable
{
    private readonly GameViewModel parent;
    private StationRow row;

    public StationViewModel(GameViewModel parent, int index)
    {
        this.parent = parent;
        Index = index;
        PrimaryCommand = new Command(Primary);
        SellCommand = new Command(Sell);
        HireCommand = new Command(Hire);
        SpeakCommand = new Command(() => parent.Replay(Index));
        ActorImage = AppThemes.Image(Look.Actor);
        row = Compute();
    }

    public int Index { get; }
    private StationLook Look => parent.Theme.Stations[Index];

    public ICommand PrimaryCommand { get; }
    public ICommand SellCommand { get; }
    public ICommand HireCommand { get; }
    public ICommand SpeakCommand { get; }
    public ImageSource? ActorImage { get; }

    public string Status => row.Status;
    public string Stock => row.Stock;
    public double Progress => row.Progress;
    public bool ActorVisible => row.ActorVisible;
    public bool PrimaryVisible => row.PrimaryVisible;
    public string PrimaryText => row.PrimaryText;
    public bool PrimaryEnabled => row.PrimaryEnabled;
    public bool SellVisible => row.SellVisible;
    public string SellText => row.SellText;
    public bool SellEnabled => row.SellEnabled;
    public string HireText => row.HireText;
    public bool HireEnabled => row.HireEnabled;

    private StationRow Compute() => StationRow.For(parent.Rules, parent.State, parent.Theme, Index, ActorImage is not null);

    private bool boosted;
    // True while a level or a running power-up is boosting this station: the row glows.
    public bool Boosted { get => boosted; private set => Set(ref boosted, value); }

    // Recompute, and tell the screen about exactly the values that changed.
    internal void Refresh()
    {
        Boosted = Boosts.Boosted(parent.Rules, parent.State, Index);
        var before = row;
        row = Compute();
        if (row == before) return;
        void Changed<T>(T a, T b, string name) { if (!EqualityComparer<T>.Default.Equals(a, b)) OnPropertyChanged(name); }
        Changed(before.Status, row.Status, nameof(Status));
        Changed(before.Stock, row.Stock, nameof(Stock));
        Changed(before.Progress, row.Progress, nameof(Progress));
        Changed(before.ActorVisible, row.ActorVisible, nameof(ActorVisible));
        Changed(before.PrimaryVisible, row.PrimaryVisible, nameof(PrimaryVisible));
        Changed(before.PrimaryText, row.PrimaryText, nameof(PrimaryText));
        Changed(before.PrimaryEnabled, row.PrimaryEnabled, nameof(PrimaryEnabled));
        Changed(before.SellVisible, row.SellVisible, nameof(SellVisible));
        Changed(before.SellText, row.SellText, nameof(SellText));
        Changed(before.SellEnabled, row.SellEnabled, nameof(SellEnabled));
        Changed(before.HireText, row.HireText, nameof(HireText));
        Changed(before.HireEnabled, row.HireEnabled, nameof(HireEnabled));
    }

    private void Primary()
    {
        switch (row.Primary)
        {
            case RowAction.Collect:
                parent.Perform((g, s) => GameEngine.Collect(g, s, Index), Look.CollectMessage ?? "");
                break;
            case RowAction.Send:
                parent.Perform((g, s) => GameEngine.Send(g, s, Index - 1), Look.SendMessage ?? "");
                break;
            case RowAction.BuyTool when parent.Rules.Stations[Index].NeedsTool is { } tool:
                parent.Perform((g, s) => GameEngine.Upgrade(g, s, g.ToolIndexOf(tool)), Look.ToolMessage ?? "");
                break;
        }
    }

    private void Sell()
    {
        var price = parent.Rules.Stations[Index].SellPrice ?? 0;
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
