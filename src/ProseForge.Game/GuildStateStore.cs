using System.Text.Json;

namespace ProseForge.Game;

public sealed class GuildStateStore
{
    private const string Key = "guild-save.v1";

    public GuildGameState Load()
    {
        try
        {
            var json = Preferences.Default.Get(Key, string.Empty);
            return string.IsNullOrEmpty(json)
                ? new GuildGameState()
                : GuildGameState.FromSnapshot(JsonSerializer.Deserialize<GuildSnapshot>(json));
        }
        catch (JsonException)
        {
            return new GuildGameState();
        }
    }

    public void Save(GuildGameState game) =>
        Preferences.Default.Set(Key, JsonSerializer.Serialize(game.Snapshot()));
}
