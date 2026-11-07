using System.Globalization;

namespace ProseForge.Game.Themes;

public static class ThemeText
{
    // Fill {name} placeholders. Unknown placeholders stay visible, so a typo in a theme shows up
    // on screen instead of silently vanishing.
    public static string Fill(string template, params (string Name, object Value)[] values)
    {
        foreach (var (name, value) in values)
            template = template.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.CurrentCulture));
        return template;
    }
}
