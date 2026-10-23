namespace Aria.Audio;

public sealed class MixBlockReport
{
    public long Sequence;
    public long OutputFrame;
    public int SinkGeneration;
    public bool Overflow;
    public bool TestClick;
    public int Count;

    public VoiceReport[] Voices { get; } = new VoiceReport[MixerBus.MaxReportVoices];
}

public sealed class MixReportRing
{
    public const int Capacity = 64;

    private readonly Slot[] _slots = new Slot[Capacity];
    private long _latest;

    public MixReportRing()
    {
        for (var index = 0; index < Capacity; index++)
        {
            _slots[index] = new Slot();
        }
    }

    public long LatestSequence => Volatile.Read(ref _latest);

    public bool TryRead(long sequence, MixBlockReport target)
    {
        if (sequence <= 0 || sequence > LatestSequence)
        {
            return false;
        }
        var slot = _slots[sequence % Capacity];
        var before = Volatile.Read(ref slot.Seq);
        if (before != 2 * sequence + 2)
        {
            return false;
        }
        target.Sequence = sequence;
        target.OutputFrame = slot.OutputFrame;
        target.SinkGeneration = slot.SinkGeneration;
        target.Overflow = slot.Overflow;
        target.TestClick = slot.TestClick;
        var count = Math.Clamp(slot.Count, 0, MixerBus.MaxReportVoices);
        slot.Voices.AsSpan(0, count).CopyTo(target.Voices);
        target.Count = count;
        Interlocked.MemoryBarrier();
        return Volatile.Read(ref slot.Seq) == before;
    }

    internal void Publish(long outputFrame, int sinkGeneration, ReadOnlySpan<VoiceReport> voices, bool overflow, bool testClick)
    {
        var sequence = _latest + 1;
        var slot = _slots[sequence % Capacity];
        Interlocked.Exchange(ref slot.Seq, 2 * sequence + 1);
        slot.OutputFrame = outputFrame;
        slot.SinkGeneration = sinkGeneration;
        slot.Overflow = overflow;
        slot.TestClick = testClick;
        var count = Math.Min(voices.Length, MixerBus.MaxReportVoices);
        voices.Slice(0, count).CopyTo(slot.Voices);
        slot.Count = count;
        Volatile.Write(ref slot.Seq, 2 * sequence + 2);
        Volatile.Write(ref _latest, sequence);
    }

    private sealed class Slot
    {
        public long Seq;
        public long OutputFrame;
        public int SinkGeneration;
        public bool Overflow;
        public bool TestClick;
        public int Count;
        public readonly VoiceReport[] Voices = new VoiceReport[MixerBus.MaxReportVoices];
    }
}
