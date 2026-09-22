namespace Aria.Audio.Tests;

using Aria.Core.Playback;

public sealed class SessionMixerTests : IDisposable
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int BlockFrames = 256;

    private readonly PreviewTap _mainTap = new(SampleRate * 2, Channels);
    private readonly SessionMixer _mixer = new(new SyntheticSourceFactory(SampleRate, Channels), SampleRate, Channels, BlockFrames);

    public void Dispose() => _mixer.Dispose();

    private SessionPumpContext LiveContext() => new(
        _mainTap.Head,
        true,
        BitConverter.DoubleToInt64Bits(1.0),
        0);

    private static void FillSine(float[] buffer, int channels, ref double phase)
    {
        for (var i = 0; i < buffer.Length / channels; i++)
        {
            var sample = (float)(0.5 * Math.Sin(phase));
            phase += 2.0 * Math.PI * 440.0 / SampleRate;
            for (var c = 0; c < channels; c++)
            {
                buffer[i * channels + c] = sample;
            }
        }
    }

    [Fact]
    public void Open_ListsWithDefaultName()
    {
        var session = _mixer.Open(null, _mainTap.Subscribe(), -1);

        var profile = Assert.Single(_mixer.List());
        Assert.Equal(session, profile.Session);
        Assert.Equal($"Session {session.Value}", profile.Name);
        Assert.Equal(0.0, profile.BackingGainDb);
        Assert.True(profile.ClickMuted);
    }

    [Fact]
    public void Rename_AndGains_ReflectInList()
    {
        var session = _mixer.Open("  Drums  ", _mainTap.Subscribe(), -1);

        _mixer.Rename(session, "Keys");
        _mixer.SetBackingGain(session, -6.0);
        _mixer.SetClickGain(session, -12.0);

        var profile = Assert.Single(_mixer.List());
        Assert.Equal("Keys", profile.Name);
        Assert.Equal(-6.0, profile.BackingGainDb, 3);
        Assert.Equal(-12.0, profile.ClickGainDb);
    }

    [Fact]
    public void Close_RemovesSession()
    {
        var session = _mixer.Open("Temp", _mainTap.Subscribe(), -1);

        _mixer.Close(session);

        Assert.Empty(_mixer.List());
        Assert.Null(_mixer.Tap(session));
    }

    [Fact]
    public void StartTrack_UnknownSession_ReturnsNull()
    {
        Assert.Null(_mixer.StartTrack(new PreviewSessionHandle(999), new TrackSource("/audio/sine.flac", TimeSpan.Zero, null)));
    }

    [Fact]
    public void StartTrack_BrokenFile_ReturnsFault()
    {
        var session = _mixer.Open(null, _mainTap.Subscribe(), -1);

        Assert.Equal(SourceOpenFault.Undecodable, _mixer.StartTrack(session, new TrackSource("/audio/broken.flac", TimeSpan.Zero, null)));
    }

    [Fact]
    public void AuditionEnded_RejoinsMainMix_Deterministically()
    {
        var session = _mixer.Open(null, _mainTap.Subscribe(), -1);
        Assert.Null(_mixer.StartTrack(session, new TrackSource("/audio/finite.flac", TimeSpan.Zero, null)));
        var tap = _mixer.Tap(session);
        Assert.NotNull(tap);
        var reader = tap.Subscribe();
        var block = new float[BlockFrames * Channels];
        var phase = 0.0;
        var buffer = new float[BlockFrames * Channels];

        for (var i = 0; i < 130; i++)
        {
            FillSine(block, Channels, ref phase);
            _mainTap.Publish(block);
            _mixer.Pump(LiveContext());
        }

        Assert.True(reader.Read(buffer) > 0, "session stalled after audition ended");
    }

    [Fact]
    public void Pump_AllocatesNothing_InSteadyState()
    {
        var session = _mixer.Open(null, _mainTap.Subscribe(), -1);
        _mixer.StartTrack(session, new TrackSource("/audio/sine.flac", TimeSpan.Zero, null));
        var block = new float[BlockFrames * Channels];
        var phase = 0.0;
        for (var i = 0; i < 50; i++)
        {
            FillSine(block, Channels, ref phase);
            _mainTap.Publish(block);
            _mixer.Pump(LiveContext());
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            FillSine(block, Channels, ref phase);
            _mainTap.Publish(block);
            _mixer.Pump(LiveContext());
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }
}
