using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ProseForge.Game.Engine;

namespace ProseForge.Game;

// One save per theme, in that theme's own slot (EngineSaves.SlotKey), so switching themes never
// touches another theme's game.
public sealed class SaveStore
{
    private const string V1Key = "guild-save.v1";

    // The v2 slot wins. A guild player with only the old save is migrated on first load; the v1 save
    // is left untouched, so nothing is lost if the migration ever has to be looked at again. Null
    // means there is no game to resume in this theme.
    public EngineState? Load(Gameplay gameplay)
    {
        if (Read(EngineSaves.SlotKey(gameplay), SaveJson.Default.EngineSnapshot) is { } saved
            && EngineSaves.Restore(gameplay, saved) is { } state)
            return state;
        if (gameplay.Id == Gameplays.Guild.Id && Read(V1Key, SaveJson.Default.GuildSnapshot) is { } v1)
            return GuildGameState.FromSnapshot(v1).Session.State;
        return null;
    }

    public void Save(GameSession session) =>
        Preferences.Default.Set(EngineSaves.SlotKey(session.Gameplay), JsonSerializer.Serialize(
            EngineSaves.Capture(session.Gameplay, session.State), SaveJson.Default.EngineSnapshot));

    private static T? Read<T>(string key, JsonTypeInfo<T> type) where T : class
    {
        try
        {
            var json = Preferences.Default.Get(key, string.Empty);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// Saves keep .NET's default property names (PascalCase), the shape every existing save on a phone
// was written in. Source-generated, like the theme packs, so trimmed release builds can read them.
[JsonSerializable(typeof(EngineSnapshot))]
[JsonSerializable(typeof(GuildSnapshot))]
internal sealed partial class SaveJson : JsonSerializerContext;
