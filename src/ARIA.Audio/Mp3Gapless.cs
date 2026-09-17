namespace Aria.Audio;

public sealed record Mp3GapInfo(int DelaySamples, int PaddingSamples, int SampleRate)
{
    public TimeSpan Delay() => TimeSpan.FromSeconds(DelaySamples / (double)SampleRate);

    public TimeSpan Padding() => TimeSpan.FromSeconds(PaddingSamples / (double)SampleRate);
}

public static class Mp3Gapless
{
    private const int ProbeBytes = 8192;
    private const int MaxShift = 100_000;

    public static bool TryRead(string filePath, out Mp3GapInfo info)
    {
        info = null!;
        if (!string.Equals(Path.GetExtension(filePath), ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        byte[] head;
        try
        {
            using var stream = File.OpenRead(filePath);
            head = new byte[Math.Min(ProbeBytes, (int)Math.Min(stream.Length, ProbeBytes))];
            var read = stream.Read(head, 0, head.Length);
            if (read < head.Length)
            {
                head = head[..read];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return TryRead(head, out info);
    }

    internal static bool TryRead(byte[] data, out Mp3GapInfo info)
    {
        info = null!;
        var offset = 0;
        if (data.Length > 10 && data[0] == 'I' && data[1] == 'D' && data[2] == '3')
        {
            offset = 10 + ((data[6] & 0x7F) << 21 | (data[7] & 0x7F) << 14 | (data[8] & 0x7F) << 7 | (data[9] & 0x7F));
        }
        for (; offset + 4 < data.Length && offset < ProbeBytes; offset++)
        {
            if (data[offset] == 0xFF && (data[offset + 1] & 0xE0) == 0xE0)
            {
                return TryReadFrame(data, offset, out info);
            }
        }
        return false;
    }

    private static bool TryReadFrame(byte[] data, int offset, out Mp3GapInfo info)
    {
        info = null!;
        if (offset + 4 > data.Length)
        {
            return false;
        }
        var version = (data[offset + 1] >> 3) & 0x3;
        var layer = (data[offset + 1] >> 1) & 0x3;
        var rateIndex = (data[offset + 2] >> 2) & 0x3;
        if (layer != 1 || rateIndex == 3 || version == 1)
        {
            return false;
        }
        var rate = version switch
        {
            3 => new[] { 44100, 48000, 32000 }[rateIndex],
            2 => new[] { 22050, 24000, 16000 }[rateIndex],
            _ => new[] { 11025, 12000, 8000 }[rateIndex],
        };
        var mono = ((data[offset + 3] >> 6) & 0x3) == 3;
        var side = version == 3 ? (mono ? 17 : 32) : (mono ? 9 : 17);
        var xing = offset + 4 + side;
        if (xing + 8 > data.Length || !IsMagic(data, xing, "Xing") && !IsMagic(data, xing, "Info"))
        {
            return false;
        }
        var flags = (data[xing + 4] << 24) | (data[xing + 5] << 16) | (data[xing + 6] << 8) | data[xing + 7];
        var fields = 0;
        if ((flags & 1) != 0)
        {
            fields += 4;
        }
        if ((flags & 2) != 0)
        {
            fields += 4;
        }
        if ((flags & 4) != 0)
        {
            fields += 100;
        }
        if ((flags & 8) != 0)
        {
            fields += 4;
        }
        var lame = xing + 8 + fields;
        if (lame + 24 > data.Length || !IsMagic(data, lame, "LAME"))
        {
            return false;
        }
        var delay = (data[lame + 21] << 4) | (data[lame + 22] >> 4);
        var padding = ((data[lame + 22] & 0xF) << 8) | data[lame + 23];
        if (delay < 0 || padding < 0 || delay + padding > MaxShift)
        {
            return false;
        }
        info = new Mp3GapInfo(delay, padding, rate);
        return true;
    }

    private static bool IsMagic(byte[] data, int offset, string magic) =>
        data[offset] == magic[0] && data[offset + 1] == magic[1] && data[offset + 2] == magic[2] && data[offset + 3] == magic[3];
}
