using System.Globalization;

namespace ProseForge.Game;

// The earned unlock: every delivery in every theme counts toward one total, and at 100 the
// game opens "How this was made". The total lives in the save slots under its own key, so a theme
// switch or a fresh game never takes it away, and the celebration is marked once it has played.
public sealed class EarnedUnlock(ISaveSlots slots)
{
    public const long Needed = 100;
    public const string TotalKey = "unlock.deliveries.v1";
    public const string ShownKey = "unlock.shown.v1";

    // Read from the slots once, then kept here: the game asks every frame, and only this class
    // writes these keys.
    private long? total;
    private bool? shown;

    public long Total => total ??=
        long.TryParse(slots.Get(TotalKey), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    public bool Unlocked => Total >= Needed;

    // Unlocked, and the player hasn't seen the celebration yet. Kept apart from the count so a
    // player who closes the app at the 100th delivery still gets it on the next start.
    public bool Due => Unlocked && !(shown ??= slots.Get(ShownKey) is not null);

    public void Add(long deliveries)
    {
        if (deliveries <= 0) return;
        var current = Total;
        Write(deliveries > long.MaxValue - current ? long.MaxValue : current + deliveries);
    }

    // The first start with the unlock in the game: what the player had already delivered counts.
    // Only then; after that the total keeps itself.
    public void StartFrom(IEnumerable<long> delivered)
    {
        if (slots.Get(TotalKey) is not null) return;
        Write(0);
        foreach (var n in delivered) Add(n);
    }

    public void MarkShown()
    {
        slots.Set(ShownKey, "1");
        shown = true;
    }

    private void Write(long value)
    {
        slots.Set(TotalKey, value.ToString(CultureInfo.InvariantCulture));
        total = value;
    }
}
