namespace ProseForge.Game.Themes;

// Every colour in the XAML is a DynamicResource named after a palette role, so swapping this one
// dictionary repaints the whole app in place, with no page rebuilt.
public sealed class ThemeResources
{
    private ResourceDictionary? active;

    public void Apply(Palette palette)
    {
        var colours = new ResourceDictionary
        {
            [nameof(Palette.Background)] = Color.FromArgb(palette.Background),
            [nameof(Palette.Header)] = Color.FromArgb(palette.Header),
            [nameof(Palette.Panel)] = Color.FromArgb(palette.Panel),
            [nameof(Palette.PanelStroke)] = Color.FromArgb(palette.PanelStroke),
            [nameof(Palette.Accent)] = Color.FromArgb(palette.Accent),
            [nameof(Palette.OnAccent)] = Color.FromArgb(palette.OnAccent),
            [nameof(Palette.Text)] = Color.FromArgb(palette.Text),
            [nameof(Palette.MutedText)] = Color.FromArgb(palette.MutedText),
            [nameof(Palette.Button)] = Color.FromArgb(palette.Button),
            [nameof(Palette.ButtonActive)] = Color.FromArgb(palette.ButtonActive),
            [nameof(Palette.Dock)] = Color.FromArgb(palette.Dock),
            [nameof(Palette.DockStroke)] = Color.FromArgb(palette.DockStroke),
        };
        var merged = Application.Current!.Resources.MergedDictionaries;
        if (active is not null) merged.Remove(active);
        merged.Add(active = colours);
    }
}
