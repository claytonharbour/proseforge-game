using System.Text.RegularExpressions;

namespace ProseForge.Game.Presentation;

// The app's open-source licences page, read from THIRD_PARTY_NOTICES.md, which
// tools/notices/notices.py generates from the packages the app ships. Parsed here, not in the page,
// so the tests can check the bundled file lists every package and every text it points at.
public sealed record NoticeComponent(string Name, string Version, string Licence, string Copyright, IReadOnlyList<string> TextIds);

public sealed record NoticeText(string Id, string Title, string Body);

public sealed partial record Notices(IReadOnlyList<NoticeComponent> Components, IReadOnlyList<NoticeText> Texts)
{
    public const string FileName = "THIRD_PARTY_NOTICES.md";

    public static Notices Parse(string markdown)
    {
        var components = new List<NoticeComponent>();
        var texts = new List<NoticeText>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (Row().Match(lines[i]) is { Success: true } row && row.Groups[2].Value != "Version" && !row.Groups[2].Value.StartsWith("---"))
            {
                var name = Link().Replace(row.Groups[1].Value.Trim(), "$1");
                var ids = TextRef().Matches(row.Groups[5].Value).Select(m => m.Groups[1].Value).ToList();
                components.Add(new(name, row.Groups[2].Value.Trim(), row.Groups[3].Value.Trim(), row.Groups[4].Value.Trim(), ids));
            }
            else if (TextHeading().Match(lines[i]) is { Success: true } heading && i + 2 < lines.Length && lines[i + 2] == "```text")
            {
                var body = new List<string>();
                for (i += 3; i < lines.Length && lines[i] != "```"; i++) body.Add(lines[i]);
                var title = body.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? heading.Groups[1].Value;
                texts.Add(new(heading.Groups[1].Value, title, string.Join('\n', body)));
            }
        }
        return new(components, texts);
    }

    [GeneratedRegex(@"^\| (.+?) \| (.+?) \| (.+?) \| (.*?) \| (.*) \|$")]
    private static partial Regex Row();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\(#text-([0-9a-f]+)\)")]
    private static partial Regex TextRef();

    [GeneratedRegex(@"^### Text ([0-9a-f]+)$")]
    private static partial Regex TextHeading();
}
