namespace Aria.Audio.Tests;

public static class TestWav
{
    private const double TwoPi = Math.PI * 2.0;

    public static string WriteSine(string directory, string name, int sampleRate, int channels, double seconds, double frequency, double amplitude)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        var totalFrames = (int)Math.Round(seconds * sampleRate);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        var dataSize = totalFrames * channels * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        for (var frame = 0; frame < totalFrames; frame++)
        {
            var value = amplitude * Math.Sin(TwoPi * frequency * frame / sampleRate);
            var clamped = Math.Clamp(value, -1.0, 1.0);
            var sample = (short)Math.Round(clamped * 32767.0);
            for (var channel = 0; channel < channels; channel++)
            {
                writer.Write(sample);
            }
        }
        return path;
    }
}
