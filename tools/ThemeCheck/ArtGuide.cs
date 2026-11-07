using System.Globalization;
using System.Text;
using ProseForge.Game.Themes;
using static ProseForge.Game.Themes.ScreenLayout;

// The picture artists generate a theme's background against: where the header, bars and dock cover it,
// what tall phones crop, and the clear window on each floor where that floor's character must go.
// Every box comes from ScreenLayout, for the shortest and the tallest phone the game supports.
public static class ArtGuide
{
    public const int Width = 1080, Height = 1920;

    // overlay: no paper or grid, so the guide can be laid over a picture to check it (make art-check).
    public static string Svg(int stations, bool overlay = false)
    {
        const double pageWidth = 360;
        var phones = new[] { pageWidth / WidestPage, pageWidth / TallestPage }
            .Select(h => (H: h, Screen: For(pageWidth, h, stations))).ToArray();
        Box Pic((double H, Screen Screen) phone, Box page) => ToPicture(page, pageWidth, phone.H, Width, Height);
        Box Union(IEnumerable<Box> boxes) => Hull(boxes, Math.Min, Math.Max);
        Box Common(IEnumerable<Box> boxes) => Hull(boxes, Math.Max, Math.Min);

        var visible = Common(phones.Select(p => Pic(p, new Box(0, 0, pageWidth, p.H))));
        var svg = new StringBuilder();
        svg.Append($"""<svg xmlns="http://www.w3.org/2000/svg" width="{Width}" height="{Height}" viewBox="0 0 {Width} {Height}" font-family="Helvetica, Arial, sans-serif">""");
        if (!overlay) svg.Append($"""<rect width="{Width}" height="{Height}" fill="#f4f1ea"/>""");
        for (var t = 1; t < 10 && !overlay; t++)
            svg.Append($"""<line x1="0" x2="{Width}" y1="{F(Height * t / 10.0)}" y2="{F(Height * t / 10.0)}" stroke="#d8d2c4" stroke-width="2"/><text x="8" y="{F(Height * t / 10.0 - 8)}" font-size="22" fill="#a39c8c">{t * 10}%</text>""");

        Rect(svg, new Box(0, 0, visible.Left, Height), "#9a9a9a", 0.55, "cropped on tall phones", vertical: true);
        Rect(svg, new Box(visible.Right, 0, Width - visible.Right, Height), "#9a9a9a", 0.55, "cropped on tall phones", vertical: true);
        Rect(svg, Union(phones.Select(p => Pic(p, p.Screen.Header))), "#c0392b", 0.6, "HEADER covers this");
        Rect(svg, Union(phones.Select(p => Pic(p, p.Screen.Dock))), "#c0392b", 0.6, "BOTTOM PANEL covers this");
        for (var i = 0; i < stations; i++)
        {
            var open = Common(phones.Select(p => Pic(p, p.Screen.Stations[i].Open)).Append(visible));
            Rect(svg, open, "#27ae60", 0.35, $"FLOOR {i + 1}: always visible. Put this floor's character here");
            Rect(svg, Union(phones.Select(p => Pic(p, p.Screen.Stations[i].Bar))), "#c0392b", 0.6, $"station {i + 1} bar covers this");
            var actor = Union(phones.Select(p => Pic(p, p.Screen.Stations[i].Actor)));
            svg.Append($"""<rect x="{F(actor.Left)}" y="{F(actor.Top)}" width="{F(actor.Width)}" height="{F(actor.Height)}" fill="none" stroke="#2c5aa0" stroke-width="5" stroke-dasharray="18 10"/>""");
            svg.Append($"""<text x="{F(actor.Left + 10)}" y="{F(actor.Bottom - 12)}" font-size="22" fill="#2c5aa0">cut-out character</text>""");
        }
        svg.Append($"""<text x="{Width / 2}" y="{Height - 20}" font-size="24" text-anchor="middle" fill="#555">One More Story art guide · {stations} stations · 1080×1920 · phones 9:16 to 9:20 · themes/README.md</text>""");
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static Box Hull(IEnumerable<Box> boxes, Func<double, double, double> low, Func<double, double, double> high)
    {
        var list = boxes.ToList();
        var left = list.Select(b => b.Left).Aggregate(low);
        var top = list.Select(b => b.Top).Aggregate(low);
        var right = list.Select(b => b.Right).Aggregate(high);
        var bottom = list.Select(b => b.Bottom).Aggregate(high);
        return new Box(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static void Rect(StringBuilder svg, Box b, string colour, double opacity, string label, bool vertical = false)
    {
        if (b.Width <= 0 || b.Height <= 0) return;
        svg.Append($"""<rect x="{F(b.Left)}" y="{F(b.Top)}" width="{F(b.Width)}" height="{F(b.Height)}" fill="{colour}" fill-opacity="{F(opacity)}"/>""");
        if (vertical)
            svg.Append($"""<text transform="translate({F(b.Left + b.Width / 2 + 9)},{F(Height / 2.0)}) rotate(-90)" font-size="24" text-anchor="middle" fill="#fff">{label}</text>""");
        else
            svg.Append($"""<text x="{F(b.Left + b.Width / 2)}" y="{F(b.Top + b.Height / 2 + 10)}" font-size="28" font-weight="bold" text-anchor="middle" fill="#fff">{label}</text>""");
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
