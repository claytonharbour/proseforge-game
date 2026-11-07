using ProseForge.Game.Presentation;
using ProseForge.Game.Themes;

namespace ProseForge.Game;

// "Open-source licences": every component the app ships and the full text of each licence,
// from the bundled THIRD_PARTY_NOTICES.md. Reached from the bottom of About, after the books.
public sealed class LicencesPage : ContentPage
{
    public LicencesPage()
    {
        Title = "Open-source licences";
        SetDynamicResource(BackgroundColorProperty, "Background");
        Notices notices;
        try
        {
            using var stream = AppThemes.Open(Notices.FileName);
            notices = stream is null ? new([], []) : Notices.Parse(new StreamReader(stream).ReadToEnd());
        }
        catch (Exception)
        {
            notices = new([], []);
        }

        var items = new List<View>
        {
            Text("One More Story is built on the open-source software and fonts below, each used under its own licence. The full licence texts follow the list.", 12, "MutedText"),
        };
        if (notices.Components.Count == 0)
            items.Add(Text("The licence list couldn't be read in this build.", 12, "Text"));
        foreach (var c in notices.Components)
        {
            var row = new VerticalStackLayout { Spacing = 1 };
            row.Add(Text($"{c.Name} {c.Version}", 13, "Text", bold: true));
            row.Add(Text(c.Copyright.Length > 0 ? $"{c.Licence} · {c.Copyright}" : c.Licence, 11, "MutedText"));
            items.Add(row);
        }
        foreach (var t in notices.Texts)
        {
            var body = new VerticalStackLayout { Spacing = 4, Margin = new Thickness(0, 10, 0, 0) };
            body.Add(Text(t.Title, 14, "Accent", bold: true));
            var text = Text(t.Body, 10, "Text");
            text.FontFamily = DeviceInfo.Platform == DevicePlatform.iOS ? "Menlo" : "monospace";
            body.Add(text);
            items.Add(body);
        }

        Content = new CollectionView
        {
            ItemsSource = items,
            ItemTemplate = new DataTemplate(() =>
            {
                var holder = new ContentView { Padding = new Thickness(16, 6) };
                holder.SetBinding(ContentView.ContentProperty, ".");
                return holder;
            }),
            MaximumWidthRequest = 640,
        };
    }

    private static Label Text(string text, double size, string colour, bool bold = false)
    {
        var label = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
        label.SetDynamicResource(Label.TextColorProperty, colour);
        return label;
    }
}
