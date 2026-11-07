using System.Text.Json;
using System.Text.Json.Serialization;
using ProseForge.Game.Engine;

namespace ProseForge.Game.Themes;

// Theme packs are read with source-generated JSON, not reflection, so iOS release builds (trimmed
// and compiled ahead of time) can still read them. A missing required field or a null where the
// record doesn't allow one is an error that names the field, not a silent null.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ThemeFile))]
[JsonSerializable(typeof(Gameplay))]
[JsonSerializable(typeof(ThemeIndex))]
public sealed partial class ThemeJson : JsonSerializerContext;
