using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProseForge.Game.ViewModels;

// The whole of data binding's plumbing: set a value, and tell the screen if it changed.
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    // For a value worked out from others: tell the screen it changed too.
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
