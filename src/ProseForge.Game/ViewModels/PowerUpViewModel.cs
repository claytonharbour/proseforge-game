using System.Windows.Input;
using ProseForge.Game.Engine;
using ProseForge.Game.Presentation;
using ProseForge.Game.Themes;

namespace ProseForge.Game.ViewModels;

// One power-up button on the dock. What it shows is decided by PowerUpButton, a plain record the
// tests can reach; this class only copies its answer onto bindable properties and turns a tap into
// the engine move. Each button keeps its own clock, so two can run at once.
public sealed class PowerUpViewModel : Observable
{
    private readonly GameViewModel parent;
    private PowerUpButton button;
    private string? iconPath;
    private ImageSource? icon;

    public PowerUpViewModel(GameViewModel parent, int index)
    {
        this.parent = parent;
        Index = index;
        Command = new Command(Start);
        button = Compute();
        iconPath = button.Icon;
        icon = AppThemes.Image(iconPath);
    }

    public int Index { get; }
    public ICommand Command { get; }

    public bool Visible => button.Visible;
    public string Text => button.Text;
    public string Name => button.Name;
    public string Detail => button.Detail;
    public bool Enabled => button.Enabled;
    public bool Running => button.Running;
    public bool Ticking => button.Ticking;
    public double Left => button.LeftFraction;
    // The pack's icon inside the ring; without one the drawn ring and the name stand in.
    public ImageSource? Icon { get => icon; private set => Set(ref icon, value); }
    public bool HasIcon => Icon is not null;
    public Color RingColour => Color.FromArgb(parent.Theme.Palette.OnAccent);

    private PowerUpButton Compute() => PowerUpButton.For(parent.Rules, parent.State, parent.Theme, Index);

    private void Start()
    {
        if (parent.Theme.PowerUpLookFor(parent.Rules.AllPowerUps[Index].Id) is not { } look) return;
        parent.Perform((g, s) => GameEngine.StartPowerUp(g, s, Index), look.Message);
    }

    // Recompute, and tell the screen about exactly the values that changed.
    internal void Refresh()
    {
        var before = button;
        button = Compute();
        if (button.Icon != iconPath)
        {
            iconPath = button.Icon;
            Icon = AppThemes.Image(iconPath);
            OnPropertyChanged(nameof(HasIcon));
        }
        if (button == before) return;
        void Changed<T>(T a, T b, string name) { if (!EqualityComparer<T>.Default.Equals(a, b)) OnPropertyChanged(name); }
        Changed(before.Visible, button.Visible, nameof(Visible));
        Changed(before.Text, button.Text, nameof(Text));
        Changed(before.Name, button.Name, nameof(Name));
        Changed(before.Detail, button.Detail, nameof(Detail));
        Changed(before.Enabled, button.Enabled, nameof(Enabled));
        Changed(before.Running, button.Running, nameof(Running));
        Changed(before.Ticking, button.Ticking, nameof(Ticking));
        Changed(before.LeftFraction, button.LeftFraction, nameof(Left));
    }
}
