using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProseForge.Game;

// Switches for things one build ships and another doesn't. They're read from features.json
// in the app package. A missing, unreadable or unknown value is OFF, so a store build that never
// mentions a feature never shows it. The store game keeps the art gallery off until the player
// earns it (EarnedUnlock); "gallery": true shows it from the start, for checking it by hand.
public sealed record FeatureFlags(bool Gallery)
{
    public const string Path = "features.json";
    public static readonly FeatureFlags Off = new(false);

    public static FeatureFlags Load(Func<string, Stream?> open)
    {
        try
        {
            using var stream = open(Path);
            return stream is null ? Off : JsonSerializer.Deserialize(stream, FeatureJson.Default.FeatureFlags) ?? Off;
        }
        catch (Exception)
        {
            return Off;   // unreadable for any reason, broken JSON or a failed read, is off
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FeatureFlags))]
internal sealed partial class FeatureJson : JsonSerializerContext;
