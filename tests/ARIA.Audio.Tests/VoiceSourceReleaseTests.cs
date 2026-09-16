namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class VoiceSourceReleaseTests : IDisposable
{
    private const int SampleRate = 8000;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "aria-release-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void StoppedVoice_ReleasesSource_AllowingOsDelete()
    {
        var path = TestWav.WriteSine(_directory, "release.wav", SampleRate, 1, 1.0, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        using var bus = new MixerBus(1, SampleRate, 512);
        var output = new float[512];
        var inner = factory.Open(path, TimeSpan.Zero, null);
        Assert.NotNull(inner);
        var probe = new ReleaseProbe(inner!);
        var handle = bus.AddVoice(new VoiceConfig(probe, 0.0, null, null, ImmutableArray<MarkerSpec>.Empty, TimeSpan.Zero, null));

        bus.Render(output);
        Assert.True(bus.Peak > 0f, "voice never rendered audio");

        bus.Transport(handle, TransportCommand.Stop);
        bus.Render(output);

        Assert.True(probe.Released, "stopped voice did not release its source");
        File.Delete(path);
    }

    [Fact]
    public void RemovedVoice_ReleasesSource()
    {
        var path = TestWav.WriteSine(_directory, "removed.wav", SampleRate, 1, 1.0, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        using var bus = new MixerBus(1, SampleRate, 512);
        var output = new float[512];
        var inner = factory.Open(path, TimeSpan.Zero, null);
        Assert.NotNull(inner);
        var probe = new ReleaseProbe(inner!);
        var handle = bus.AddVoice(new VoiceConfig(probe, 0.0, null, null, ImmutableArray<MarkerSpec>.Empty, TimeSpan.Zero, null));

        bus.Render(output);
        bus.RemoveVoice(handle);
        bus.Render(output);

        Assert.True(probe.Released, "removed voice did not release its source");
    }

    [Fact]
    public void DisposedBus_ReleasesActiveVoiceSource()
    {
        var path = TestWav.WriteSine(_directory, "disposed.wav", SampleRate, 1, 1.0, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        var bus = new MixerBus(1, SampleRate, 512);
        var output = new float[512];
        var inner = factory.Open(path, TimeSpan.Zero, null);
        Assert.NotNull(inner);
        var probe = new ReleaseProbe(inner!);
        bus.AddVoice(new VoiceConfig(probe, 0.0, null, null, ImmutableArray<MarkerSpec>.Empty, TimeSpan.Zero, null));

        bus.Render(output);
        bus.Dispose();

        Assert.True(probe.Released, "disposed bus did not release its voice source");
    }

    private sealed class ReleaseProbe(ISampleSource inner) : ISampleSource, IDisposable
    {
        public bool Released { get; private set; }

        public int Channels => inner.Channels;

        public int SampleRate => inner.SampleRate;

        public int ReadFrames(Span<float> destination) => inner.ReadFrames(destination);

        public void Seek(long frameIndex) => inner.Seek(frameIndex);

        public void Dispose()
        {
            Released = true;
            (inner as IDisposable)?.Dispose();
        }
    }
}
