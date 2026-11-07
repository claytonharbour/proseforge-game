namespace ProseForge.Game;

// The phone's save slots: MAUI Preferences, one string per key. Kept out of SaveStore.cs so
// the save rules compile into the tests without MAUI.
public sealed class PreferenceSlots : ISaveSlots
{
    public string? Get(string key)
    {
        var value = Preferences.Default.Get(key, string.Empty);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public void Set(string key, string value) => Preferences.Default.Set(key, value);
}
