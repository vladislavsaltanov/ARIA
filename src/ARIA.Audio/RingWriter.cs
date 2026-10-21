namespace Aria.Audio;

using System.Diagnostics;

internal delegate int FrameWriter(ReadOnlySpan<float> chunk, int frames);

internal static class RingWriter
{
    public static int Write(ReadOnlySpan<float> samples, int channels, int blockSizeFrames, TimeSpan timeout, Func<int> space, FrameWriter write)
    {
        var total = 0;
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (total < samples.Length)
        {
            var remaining = samples.Length - total;
            var acceptedFrames = Math.Min(Math.Min(remaining / channels, space()), blockSizeFrames);
            if (acceptedFrames <= 0)
            {
                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    return total;
                }
                Thread.Sleep(1);
                continue;
            }
            var writtenFrames = write(samples.Slice(total, acceptedFrames * channels), acceptedFrames);
            if (writtenFrames <= 0)
            {
                return total;
            }
            total += writtenFrames * channels;
        }
        return total;
    }
}
