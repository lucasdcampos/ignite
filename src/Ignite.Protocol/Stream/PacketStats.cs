namespace Ignite.Protocol.Stream;

/// <summary>Counts received and lost packets from 16-bit sequence numbers for congestion reports.</summary>
public sealed class PacketStats
{
    private readonly Lock _lock = new();
    private int? _last;
    private int _received;
    private int _lost;

    public void Record(ushort sequence)
    {
        lock (_lock)
        {
            _received++;
            if (_last is { } last)
            {
                var gap = unchecked((short)(sequence - (ushort)last)) - 1;
                if (gap < 0)
                    return;
                _lost += gap;
            }

            _last = sequence;
        }
    }

    /// <summary>Returns the counts since the previous call.</summary>
    public (int Received, int Lost) TakeSnapshot()
    {
        lock (_lock)
        {
            var snapshot = (_received, _lost);
            (_received, _lost) = (0, 0);
            return snapshot;
        }
    }

    /// <summary>Caps the loss ratio as described in docs/protocol/14-congestion.md.</summary>
    public static (ushort Received, ushort Lost) Cap(int received, int lost, double maxLoss)
    {
        var total = received + lost;
        if (total > 0 && (double)lost / total > maxLoss)
        {
            lost = (int)(total * maxLoss);
            received = total - lost;
        }

        return ((ushort)Math.Min(received, ushort.MaxValue), (ushort)Math.Min(lost, ushort.MaxValue));
    }
}
