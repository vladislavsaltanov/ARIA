namespace Aria.App.Tests;

internal static class TestWav
{
    public static string Write(string directory, string fileName, int sampleRate = 8000, int seconds = 1)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var frames = sampleRate * seconds;
        writer.Write("RIFF"u8);
        writer.Write(36 + frames * 2);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(frames * 2);
        for (var i = 0; i < frames; i++)
        {
            var value = Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate);
            writer.Write((short)(value * short.MaxValue));
        }
        return path;
    }
}
