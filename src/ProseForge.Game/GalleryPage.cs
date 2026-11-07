using ProseForge.Game.Presentation;
using ProseForge.Game.Themes;

namespace ProseForge.Game;

// "How this was made": the art gallery read from gallery/ in the app package. One card per
// panel: what the picture had to do, the pictures behind it in a row you can swipe, what changed,
// and the team's room messages about it, copied in as bubbles. A tap shows a picture full size.
// Theme colours only; no links open.
public sealed class GalleryPage : ContentPage
{
    public GalleryPage(Gallery gallery)
    {
        Title = gallery.Title;
        SetDynamicResource(BackgroundColorProperty, "Background");
        var list = new VerticalStackLayout { Spacing = 14, Padding = new Thickness(16, 12, 16, 32), MaximumWidthRequest = 600 };

        list.Add(Text(gallery.Title, 22, "Accent", bold: true));
        list.Add(Text(gallery.Intro, 13, "Text"));
        foreach (var panel in gallery.Panels) list.Add(Card(panel));

        Content = new ScrollView { Content = list };
    }

    private View Card(GalleryPanel panel)
    {
        var body = new VerticalStackLayout { Spacing = 8 };
        body.Add(Text(panel.Kind.ToUpperInvariant(), 10, "MutedText"));
        var title = Text(panel.Title, 17, "Accent", bold: true);
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level2);
        body.Add(title);
        body.Add(Text(panel.Story, 13, "Text"));

        var row = new HorizontalStackLayout { Spacing = 10 };
        foreach (var picture in panel.Pictures) row.Add(Thumbnail(picture));
        body.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = row });

        body.Add(Text(panel.Note, 12, "MutedText"));
        if (panel.AllRoom.Count > 0) body.Add(Room(panel.AllRoom));

        var card = new Border
        {
            Padding = new Thickness(12, 12),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = body,
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "Panel");
        card.SetDynamicResource(Border.StrokeProperty, "PanelStroke");
        return card;
    }

    // The room messages behind a panel, closed until asked for: some are long, and the card reads
    // without them.
    private View Room(IReadOnlyList<GalleryBubble> bubbles)
    {
        var label = $"From the room · {bubbles.Count} message{(bubbles.Count == 1 ? "" : "s")}";
        var list = new VerticalStackLayout { Spacing = 8, IsVisible = false };
        foreach (var bubble in bubbles) list.Add(Bubble(bubble));
        var toggle = new Button { Text = label + " ▾", FontSize = 12, HeightRequest = 34, CornerRadius = 17, Padding = new Thickness(12, 0) };
        toggle.SetDynamicResource(Button.BackgroundColorProperty, "Button");
        toggle.SetDynamicResource(Button.TextColorProperty, "Accent");
        toggle.Clicked += (_, _) =>
        {
            list.IsVisible = !list.IsVisible;
            toggle.Text = label + (list.IsVisible ? " ▴" : " ▾");
        };
        return new VerticalStackLayout { Spacing = 8, Children = { toggle, list } };
    }

    private View Bubble(GalleryBubble bubble)
    {
        var body = new VerticalStackLayout { Spacing = 6 };
        var header = new HorizontalStackLayout { Spacing = 6 };
        header.Add(Text(bubble.Author, 13, "Accent", bold: true));
        header.Add(Text(bubble.Time, 11, "MutedText"));
        body.Add(header);
        foreach (var part in bubble.Parts)
        {
            if (part.Text is { } words)
                body.Add(Text(words, 12, "Text"));
            else if (part.Picture is { } file)
            {
                var picture = new GalleryPicture(file, $"{bubble.Author}, {bubble.Time}", $"Picture posted by {bubble.Author}");
                var button = new ImageButton
                {
                    Source = AppThemes.Image(Gallery.PathOf(file)),
                    Aspect = Aspect.AspectFit,
                    HeightRequest = 200,
                    HorizontalOptions = LayoutOptions.Start,
                    BackgroundColor = Colors.Transparent,
                    Padding = 0,
                    Command = new Command(async () => await Navigation.PushAsync(new PicturePage(picture))),
                };
                SemanticProperties.SetDescription(button, picture.Alt);
                SemanticProperties.SetHint(button, "Shows the picture full size");
                body.Add(button);
            }
        }
        if (bubble.Reactions is { Count: > 0 } reactions)
            body.Add(Text(string.Join("   ", reactions), 12, "MutedText"));
        var card = new Border
        {
            Padding = new Thickness(10, 8),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = body,
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "Background");
        card.SetDynamicResource(Border.StrokeProperty, "PanelStroke");
        SemanticProperties.SetDescription(card, $"{bubble.Author}, {bubble.Time}");
        return card;
    }

    // An image button, not a tapped image, so a screen reader announces it as something to press (Sten's review).
    private View Thumbnail(GalleryPicture picture)
    {
        var button = new ImageButton
        {
            Source = AppThemes.Image(Gallery.PathOf(picture)),
            Aspect = Aspect.AspectFit,
            HeightRequest = 220,
            WidthRequest = 170,
            BackgroundColor = Colors.Transparent,
            Padding = 0,
            Command = new Command(async () => await Navigation.PushAsync(new PicturePage(picture))),
        };
        SemanticProperties.SetDescription(button, picture.Alt);
        SemanticProperties.SetHint(button, "Shows the picture full size");
        var caption = Text(picture.Caption, 11, "MutedText");
        caption.WidthRequest = 170;
        return new VerticalStackLayout { Spacing = 4, Children = { button, caption } };
    }

    private static Label Text(string text, double size, string colour, bool bold = false)
    {
        var label = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
        label.SetDynamicResource(Label.TextColorProperty, colour);
        return label;
    }

    // One picture as large as the screen allows, with its caption. A tap anywhere goes back.
    private sealed class PicturePage : ContentPage
    {
        public PicturePage(GalleryPicture picture)
        {
            SetDynamicResource(BackgroundColorProperty, "Background");
            var image = new Image { Source = AppThemes.Image(Gallery.PathOf(picture)), Aspect = Aspect.AspectFit };
            SemanticProperties.SetDescription(image, picture.Alt);
            var caption = Text(picture.Caption, 13, "Text");
            caption.HorizontalTextAlignment = TextAlignment.Center;
            var grid = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)], Padding = new Thickness(12), RowSpacing = 8 };
            grid.Add(image, 0, 0);
            grid.Add(caption, 0, 1);
            grid.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PopAsync()) });
            Content = grid;
        }
    }
}
