using ProseForge.Game.Engine;

namespace ProseForge.Game.Themes;

// What a theme looks and sounds like, once loaded: names, words, colours, pictures and voices. The rules
// come from its Gameplay; everything a player reads or hears comes from here. Templates
// fill {n}, {price}, {total} and {cost} from the game.
public sealed record Theme(
    string Id,
    string Title,
    Gameplay Gameplay,
    Palette Palette,
    string Background,
    IReadOnlyList<StationLook> Stations,
    ThemeStrings Strings,
    string License = "",
    IReadOnlyList<PowerUpLook>? PowerUps = null,
    IReadOnlyList<LevelLook>? Levels = null)
{
    public PowerUpLook? PowerUpLookFor(string id) => PowerUps?.FirstOrDefault(p => p.Id == id);
    public LevelLook? LevelLookFor(string id) => Levels?.FirstOrDefault(l => l.Id == id);
}

// theme.json as written in a pack: the same as Theme, but the gameplay is a file name and every
// picture and sound is a path inside the pack folder.
public sealed record ThemeFile(
    string Id,
    string Title,
    string License,
    string Gameplay,
    string Background,
    Palette Palette,
    IReadOnlyList<StationLook> Stations,
    ThemeStrings Strings,
    IReadOnlyList<PowerUpLook>? PowerUps = null,   // one per power-up in gameplay.json, by id
    IReadOnlyList<LevelLook>? Levels = null);      // one per level in gameplay.json, by id

// How a power-up looks and reads. The rules (cost, seconds, effect) are in gameplay.json.
public sealed record PowerUpLook(
    string Id,                   // the power-up's id in gameplay.json
    string Label,                // "Hot Oven"
    string Message,              // said when it starts: "The oven roars. Two extra loaves a batch."
    string? Icon = null);        // a square PNG; a drawn stand-in shows until there is one

// How a level looks and sounds. What it rewards is in gameplay.json.
public sealed record LevelLook(
    string Id,
    string Label,                // "Head Baker"; shown beside the delivered count once reached
    string Message,              // said when it's reached: "Head Baker! The counter sells for a coin more."
    string? Badge = null,        // a square PNG; a drawn stand-in shows until there is one
    string? Voice = null);       // played when it's reached

public sealed record StationLook(
    string Worker,               // "monk"
    string Workers,              // "monks"
    string Activity,             // "writing"
    string Empty,                // shown before anyone is hired here
    string StockLabel,           // "Manuscripts: {n}"; {waiting} is the queue in front of this step, {tools} its shared tool
    string HireLabel,            // "Monk"
    string HireMessage,
    string? Actor = null,        // a picture shown once someone works here
    string? Voice = null,        // played on hire, and when the worker is tapped
    string? CollectLabel = null, // "Collect {n}"
    string? IdleLabel = null,    // shown while nothing is ready to collect
    string? CollectMessage = null,
    string? SendLabel = null,    // on the receiving station: "Illustrate {n} →"
    string? SendMessage = null,
    bool SellsAll = false,       // one button sells everything finished here
    string? SellMessage = null,  // "A reader takes a story home. +{price} coins."
    string? ToolLabel = null,    // on a station that needs a shared tool: "+1 tape · {cost}"; {n} is how many there are
    string? ToolMessage = null,  // "Another roll of tape. Two gifts can be wrapped at once."
    string? VoiceId = null,      // the TTS voice that recorded Voice: "bf_alice"
    string? Speaker = null,      // who the Actor picture shows: "woman" or "man"
    string? VoiceGender = null); // only for a voice whose id doesn't say it (not Kokoro): "woman" or "man"

// Colour roles, not colours: every XAML colour is a DynamicResource named after one of these.
public sealed record Palette(
    string Background,
    string Header,
    string Panel,
    string PanelStroke,
    string Accent,
    string OnAccent,
    string Text,
    string MutedText,
    string Button,
    string ButtonActive,
    string Dock,
    string DockStroke);

public sealed record ThemeStrings(
    string OpenButton,
    string OpenMessage,
    string? OpenVoice,
    string Welcome,
    string Coins,                // "{n} coins"
    string Delivered,            // "{n} books sold"
    string Pause,
    string Resume,
    string PausedMessage,
    string ResumedMessage,
    string WorkerStatus,         // "♫ {n} {who} · {activity}"
    string SellOne,              // "Sell +{price}"
    string SellAll,              // "Sell all"
    string HireButton,           // "{label}\n{cost} coins"
    string Recruit,
    string RecruitClose,
    string VoicesReady,
    string VoicesOff,
    string VoicePlaying,         // "Playing {name} greeting"
    string VoiceUnavailable,
    string WorkerSpeaks,
    string VoicesOffHint,
    string GoalStart,
    string GoalPaused,
    string GoalFirstSale,
    IReadOnlyList<string> GoalHire, // per station after the first: what to save for next
    string GoalSendToLast,
    string GoalSellLast,
    string GoalChainDone,
    // Power-up words, with defaults so packs written before power-ups still load. The button shows the
    // name over the price or the time left (three share the dock, so one line is too narrow);
    // the whole sentence is what a screen reader says.
    string PowerUpButton = "{label} · {cost}",   // said while it can be bought
    string PowerUpRunning = "{label} · {left}s",   // said while it runs; {left} is whole seconds
    string PowerUpCost = "{cost}",   // the button's second line while it can be bought
    string PowerUpLeft = "{left}s");  // the button's second line while it runs
