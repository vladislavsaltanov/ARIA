namespace Aria.Remote.Tests;

using Aria.Audio;

internal static class PcmTestFiles
{
    public static string WriteWav(string directory, string name, int frames)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        var dataSize = frames * 2 * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(48000);
        writer.Write(48000 * 2 * 2);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        for (var frame = 0; frame < frames; frame++)
        {
            var left = (short)Math.Round(30000 * Math.Sin(frame * 0.001234) * Math.Cos(frame * 0.0003));
            writer.Write(left);
            writer.Write((short)-left);
        }
        return path;
    }

    public static short[] Reference(string path, long from, int frames)
    {
        var source = new MiniaudioSourceFactory(48000, 2).Open(path, TimeSpan.Zero, null)!;
        using (source as IDisposable)
        {
            source.Seek(from);
            var floats = new float[frames * 2];
            var total = 0;
            while (total < frames)
            {
                var read = source.ReadFrames(floats.AsSpan(total * 2));
                if (read <= 0)
                {
                    break;
                }
                total += read;
            }
            var samples = new short[total * 2];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = (short)Math.Clamp(Math.Round(floats[index] * 32767.0), short.MinValue, short.MaxValue);
            }
            return samples;
        }
    }
}
