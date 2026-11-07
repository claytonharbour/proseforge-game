using ProseForge.Game.Themes;

namespace ProseForge.Game.Presentation;

// The line under the voices switch: ready, off, who is speaking, or that a voice couldn't play.
// Each clip gets a number, so news about an old clip (it finished loading, it ended) can't
// overwrite what a newer clip or the switch has said since.
public sealed class VoiceLine(ThemeStrings strings)
{
    private int current;

    public string Text { get; private set; } = strings.VoicesReady;
    public bool On { get; private set; } = true;

    public void Switch(bool on)
    {
        On = on;
        current++;   // a clip still loading or playing is no longer news
        Text = on ? strings.VoicesReady : strings.VoicesOff;
    }

    // A clip is about to load. Returns its number, or null when voices are off.
    public int? Begin() => On ? ++current : null;

    // Still the clip to play: voices are on and nothing newer has begun.
    public bool IsCurrent(int clip) => clip == current && On;

    // The clip finished loading. False means it's stale (voices were switched off, or a newer
    // clip began), so the caller stops it instead of letting it play.
    public bool Started(int clip, string speaker, bool played)
    {
        if (clip != current || !On) return false;
        Text = played ? ThemeText.Fill(strings.VoicePlaying, ("name", speaker)) : strings.VoiceUnavailable;
        return played;
    }

    // The clip finished playing: the name comes off, unless something newer has spoken since.
    public void Ended(int clip)
    {
        if (clip == current && On) Text = strings.VoicesReady;
    }
}
