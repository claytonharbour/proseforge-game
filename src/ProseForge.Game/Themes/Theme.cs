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
    string License = "");

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
    ThemeStrings Strings);

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
    string? ToolMessage = null); // "Another roll of tape. Two gifts can be wrapped at once."

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
    string GoalChainDone);
