namespace Aria.Audio.Tests;

using System.Runtime.InteropServices;
using Aria.Audio.Native;

public sealed class UnicodePathDecoderTests : IDisposable
{
    private const int SampleRate = 8000;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "aria-unicode-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Open_CyrillicDirAndNumeroName_OpensAndReadsAudio()
    {
        var directory = Path.Combine(_directory, "Музыка", "Фоновая музыка");
        var path = TestWav.WriteSine(directory, "Трек №1.wav", SampleRate, 2, 0.5, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 2);
        var source = factory.Open(path, TimeSpan.Zero, null);
        Assert.NotNull(source);
        using var scope = (IDisposable)source;
        var buffer = new float[1024 * source.Channels];
        Assert.True(source.ReadFrames(buffer) > 0);
        Assert.True(factory.TryOpen(path, TimeSpan.Zero, null, out var reopened, out var fault));
        Assert.NotNull(reopened);
        ((IDisposable)reopened).Dispose();
    }

    [Fact]
    public void DecoderOpen_UsesWideOnlyOnWindows()
    {
        Assert.Equal(OperatingSystem.IsWindows(), AriaShim.DecoderOpenUsesWide);
    }

    [Fact]
    public void DecoderOpen_ExplicitNarrow_OpensCyrillicFile()
    {
        var path = TestWav.WriteSine(_directory, "Маршрут-narrow.wav", SampleRate, 2, 0.5, 440.0, 0.5);
        var opened = AriaShim.DecoderOpen(path, SampleRate, 2, useWide: false, out var decoder);
        Assert.Equal(0, opened);
        Assert.NotEqual(IntPtr.Zero, decoder);
        AriaShim.DecoderClose(decoder);
    }
}
