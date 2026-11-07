namespace ProseForge.Game;

// Pure game rules shared by the app and deterministic tests.
public sealed class GuildGameState
{
    public const int ReadyLimit = 20;
    public static readonly int[] Prices = [5, 12, 24];
    public static readonly int[] HireCosts = [25, 30, 60];
    public static readonly double[] WorkSeconds = [4, 6, 8];

    public bool Started { get; private set; }
    public bool Paused { get; private set; }
    public int Coins { get; private set; }
    public int Sold { get; private set; }
    public bool CompletedChain { get; private set; }
    public int[] Workers { get; } = [1, 0, 0];
    public double[] Progress { get; } = [0, 0, 0];
    public int Ready { get; private set; }
    public int[] Stock { get; } = [0, 0, 0];
    public int[] Queues { get; } = [0, 0];

    public bool Open()
    {
        if (Started) return false;
        Started = true;
        return true;
    }

    public bool TogglePause()
    {
        if (!Started) return false;
        Paused = !Paused;
        return true;
    }

    public void PauseForBackground()
    {
        if (Started) Paused = true;
    }

    public void Tick(double seconds)
    {
        if (!Started || Paused || !double.IsFinite(seconds) || seconds <= 0) return;
        seconds = Math.Min(seconds, 1);

        if (Ready < ReadyLimit)
        {
            Progress[0] += seconds;
            while (Progress[0] >= WorkSeconds[0] && Ready < ReadyLimit)
            {
                Progress[0] -= WorkSeconds[0];
                Ready = Math.Min(ReadyLimit, Ready + Workers[0]);
            }
        }
        else Progress[0] = 0;

        for (var role = 1; role < 3; role++)
        {
            if (Workers[role] == 0 || Queues[role - 1] == 0)
            {
                Progress[role] = 0;
                continue;
            }
            Progress[role] += seconds;
            while (Progress[role] >= WorkSeconds[role] && Queues[role - 1] > 0)
            {
                Progress[role] -= WorkSeconds[role];
                var completed = Math.Min(Workers[role], Queues[role - 1]);
                Queues[role - 1] -= completed;
                Stock[role] += completed;
            }
            if (Queues[role - 1] == 0) Progress[role] = 0;
        }
    }

    public bool Collect()
    {
        if (!CanAct || Ready == 0) return false;
        Stock[0] += Ready;
        Ready = 0;
        return true;
    }

    public bool Sell(int kind)
    {
        if (!CanAct || !ValidKind(kind) || Stock[kind] == 0) return false;
        Stock[kind]--;
        Coins += Prices[kind];
        Sold++;
        if (kind == 2) CompletedChain = true;
        return true;
    }

    public bool SellAllPerformed()
    {
        if (!CanAct || Stock[2] == 0) return false;
        var count = Stock[2];
        Stock[2] = 0;
        Coins += count * Prices[2];
        Sold += count;
        CompletedChain = true;
        return true;
    }

    public bool Queue(int kind)
    {
        if (!CanAct || kind is < 0 or > 1 || Stock[kind] == 0 || Workers[kind + 1] == 0) return false;
        var batch = Math.Min(Stock[kind], Workers[kind + 1]);
        Stock[kind] -= batch;
        Queues[kind] += batch;
        return true;
    }

    public bool Hire(int role)
    {
        if (!CanAct || !ValidKind(role) || Workers[role] == int.MaxValue) return false;
        var cost = HireCost(role);
        if (Coins < cost) return false;
        Coins -= cost;
        Workers[role]++;
        return true;
    }

    // The first additional monk costs 25; the first illuminator and troubadour
    // cost 30 and 60. Each later hire costs another half of that role's base.
    public int HireCost(int role)
    {
        if (!ValidKind(role)) throw new ArgumentOutOfRangeException(nameof(role));
        var previousHires = Workers[role] - (role == 0 ? 1 : 0);
        var doubledCost = (long)HireCosts[role] * (2L + previousHires);
        return (int)Math.Min(int.MaxValue, (doubledCost + 1) / 2);
    }

    public bool CanAct => Started && !Paused;

    public GuildSnapshot Snapshot() => new(1, Started, Paused, Coins, Sold,
        [.. Workers], [.. Progress], Ready, [.. Stock], [.. Queues], CompletedChain);

    public static GuildGameState FromSnapshot(GuildSnapshot? saved)
    {
        if (saved is null || saved.Version != 1 || saved.Workers is not { Length: 3 }
            || saved.Progress is not { Length: 3 } || saved.Stock is not { Length: 3 }
            || saved.Queues is not { Length: 2 } || saved.Workers[0] < 1
            || saved.Workers.Skip(1).Any(n => n < 0)
            || saved.Progress.Any(n => !double.IsFinite(n) || n < 0 || n >= 8)
            || saved.Stock.Any(n => n < 0) || saved.Queues.Any(n => n < 0)
            || saved.Coins < 0 || saved.Sold < 0 || saved.Ready is < 0 or > ReadyLimit)
            return new GuildGameState();

        var state = new GuildGameState
        {
            Started = saved.Started, Paused = saved.Paused,
            Coins = saved.Coins, Sold = saved.Sold, Ready = saved.Ready,
            CompletedChain = saved.CompletedChain
        };
        saved.Workers.CopyTo(state.Workers, 0);
        saved.Progress.CopyTo(state.Progress, 0);
        saved.Stock.CopyTo(state.Stock, 0);
        saved.Queues.CopyTo(state.Queues, 0);
        return state;
    }

    private static bool ValidKind(int kind) => kind is >= 0 and < 3;
}

public sealed record GuildSnapshot(int Version, bool Started, bool Paused, int Coins, int Sold,
    int[] Workers, double[] Progress, int Ready, int[] Stock, int[] Queues, bool CompletedChain = false);
