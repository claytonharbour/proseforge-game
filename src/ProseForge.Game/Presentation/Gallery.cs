using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProseForge.Game.Presentation;

// "How this was made": the art gallery the game opens at 100 deliveries. Each panel is one
// piece of the game's art with the pictures behind it and a few lines on what changed. It replaces
// the room transcript. Nothing here knows about screens, so the tests check what the page will show.
public sealed record Gallery(string Format, string Title, string Intro, IReadOnlyList<GalleryPanel> Panels)
{
    public const string FormatV1 = "proseforge-gallery/1";
    public const string Folder = "gallery";
    public const string IndexPath = "gallery/index.json";

    // Read and check the gallery. One in another format, with a field missing, or with a picture
    // outside its own images/ folder, is refused rather than shown.
    public static Gallery? Parse(Stream json, List<string> problems)
    {
        Gallery? gallery;
        try
        {
            gallery = JsonSerializer.Deserialize(json, GalleryJson.Default.Gallery);
        }
        catch (JsonException e)
        {
            problems.Add($"The gallery isn't valid JSON: {e.Message}");
            return null;
        }
        if (gallery is null)
        {
            problems.Add("The gallery is empty.");
            return null;
        }
        if (gallery.Format != FormatV1)
        {
            problems.Add($"The gallery's format is \"{gallery.Format}\", not {FormatV1}.");
            return null;
        }
        var before = problems.Count;
        if (string.IsNullOrWhiteSpace(gallery.Title) || gallery.Intro is null) problems.Add("The gallery needs a title and an intro.");
        if (gallery.Panels is null || gallery.Panels.Count == 0) problems.Add("The gallery has no panels.");
        foreach (var (panel, i) in (gallery.Panels ?? []).Select((p, i) => (p, i + 1)))
        {
            if (panel is null || string.IsNullOrWhiteSpace(panel.Title) || panel.Kind is null || panel.Story is null || panel.Note is null)
            {
                problems.Add($"Panel {i} needs a title, a kind, a story and a note.");
                continue;
            }
            if (panel.Pictures is null || panel.Pictures.Count == 0) problems.Add($"Panel {i} ({panel.Title}) has no pictures.");
            foreach (var picture in panel.Pictures ?? [])
            {
                if (picture is null || picture.Caption is null || string.IsNullOrWhiteSpace(picture.Alt) || !InFolder(picture.File))
                    problems.Add($"Panel {i} ({panel.Title}) has a picture outside gallery/images/, or without a caption and alt text.");
            }
            foreach (var bubble in panel.Room ?? [])
            {
                if (bubble is null || string.IsNullOrWhiteSpace(bubble.Author) || bubble.Time is null || bubble.Parts is null || bubble.Parts.Count == 0
                    || bubble.Parts.Any(part => part is null || (part.Text is null) == (part.Picture is null) || (part.Picture is not null && !InFolder(part.Picture))))
                    problems.Add($"Panel {i} ({panel.Title}) has a room message without an author, a time or its words, or a picture outside gallery/images/.");
            }
        }
        return problems.Count == before ? gallery : null;
    }

    // Read gallery/index.json from the app package. Null when the build has no gallery, which is
    // how the public copy (which leaves the folder out) shows nothing.
    public static Gallery? Load(Func<string, Stream?> open, List<string> problems)
    {
        try
        {
            using var stream = open(IndexPath);
            return stream is null ? null : Parse(stream, problems);
        }
        catch (IOException e)
        {
            problems.Add($"The gallery couldn't be read: {e.Message}");
            return null;
        }
    }

    // A picture's path inside the app package.
    public static string PathOf(GalleryPicture picture) => PathOf(picture.File);
    public static string PathOf(string file) => $"{Folder}/{file}";

    private static bool InFolder(string? file) =>
        file is not null && file.StartsWith("images/", StringComparison.Ordinal)
        && !file.Contains("..", StringComparison.Ordinal) && !file.Contains('\\') && file.Length > "images/".Length;
}

// One piece of art. Kind is the small label over the title ("Before and after"); Story says what
// the picture had to do; Note says what changed, or why the panel is here. Room is the team's own
// messages about it, copied in as they were written (Clayton: "copy the room bubble"), never a link.
public sealed record GalleryPanel(string Title, string Kind, string Story, IReadOnlyList<GalleryPicture> Pictures, string Note,
    IReadOnlyList<GalleryBubble>? Room = null)
{
    public IReadOnlyList<GalleryBubble> AllRoom => Room ?? [];
}

// One room message: who wrote it, when (UTC), its words and pictures in order, and its reactions
// as the room showed them ("❤️ Clayton").
public sealed record GalleryBubble(string Author, string Time, IReadOnlyList<GalleryBubblePart> Parts,
    IReadOnlyList<string>? Reactions = null);

// Either words or a picture from gallery/images/, never both.
public sealed record GalleryBubblePart(string? Text = null, string? Picture = null);

// Alt is what a screen reader says for the picture; the caption is what everyone reads under it.
public sealed record GalleryPicture(string File, string Caption, string Alt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Gallery))]
internal sealed partial class GalleryJson : JsonSerializerContext;
