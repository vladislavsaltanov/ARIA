namespace Aria.Remote.Tests;

using System.Net;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;

public sealed class AudioPreviewCodecTests : IAsyncLifetime
{
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;

    public async Task InitializeAsync()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("secret", TestPorts.Next()));
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _bus.Dispose();
    }

    private TestClient Connected()
    {
        var client = new TestClient();
        client.ConnectAsync(_host.WebsocketEndpoint, "secret").GetAwaiter().GetResult();
        return client;
    }

    private static string EqJson(double gainDb = 0)
    {
        var bands = string.Join(",", AudioEq.DefaultFrequencies.Select(f =>
            "{\"freq_hz\":" + f + ",\"gain_db\":" + gainDb + ",\"q\":1}"));
        return "{\"bands\":[" + bands + "]}";
    }

    private static string GlobalJson(string eq) =>
        "{\"pan\":0.1,\"mono\":false,\"hpf_hz\":80,\"eq\":" + eq
        + ",\"limiter\":{\"enabled\":true,\"threshold_db\":-1,\"release_ms\":100}}";

    [Fact]
    public void SetGlobalAudio_Parses()
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_global_audio\","
            + "\"pan\":0.1,\"mono\":false,\"hpf_hz\":80,\"eq\":" + EqJson(2)
            + ",\"limiter\":{\"enabled\":true,\"threshold_db\":-1,\"release_ms\":100}}}";

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<SetGlobalAudio>(command);
        Assert.Equal(0.1, parsed.Value.Pan);
        Assert.False(parsed.Value.Mono);
        Assert.Equal(80, parsed.Value.HpfHz);
        Assert.Equal(7, parsed.Value.Eq.Bands.Length);
        Assert.Equal(2, parsed.Value.Eq.Bands[3].GainDb);
        Assert.True(parsed.Value.Limiter.Enabled);
    }

    [Fact]
    public void SetTrackAudio_Parses()
    {
        var id = Guid.NewGuid();
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_track_audio\","
            + "\"track\":\"" + id + "\",\"gain_db\":-3,\"pan\":-0.5,\"eq\":" + EqJson() + "}}";

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<SetTrackAudio>(command);
        Assert.Equal(new TrackId(id), parsed.Track);
        Assert.Equal(-3, parsed.Audio.GainDb);
        Assert.Equal(-0.5, parsed.Audio.Pan);
    }

    [Fact]
    public void SetEntryAudio_WithValue_Parses()
    {
        var id = Guid.NewGuid();
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_entry_audio\","
            + "\"entry\":\"" + id + "\",\"audio\":{\"gain_db\":-3,\"pan\":0,\"eq\":" + EqJson() + "}}}";

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<SetEntryAudio>(command);
        Assert.Equal(new EntryId(id), parsed.Entry);
        Assert.NotNull(parsed.Audio);
        Assert.Equal(-3, parsed.Audio!.GainDb);
    }

    [Fact]
    public void SetEntryAudio_Null_Clears()
    {
        var id = Guid.NewGuid();
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_entry_audio\","
            + "\"entry\":\"" + id + "\",\"audio\":null}}";

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<SetEntryAudio>(command);
        Assert.Null(parsed.Audio);
    }

    [Fact]
    public void StartPreviewTrack_Parses()
    {
        var id = Guid.NewGuid();
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"start_preview_track\",\"track\":\"" + id + "\"}}";

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        Assert.Equal(new StartPreviewTrack(new TrackId(id)), command);
    }

    [Fact]
    public void StopPreview_Parses()
    {
        Assert.True(CommandCodec.TryParse("{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"stop_preview\"}}", out _, out _, out var command));
        Assert.IsType<StopPreview>(command);
    }

    [Fact]
    public void SetPreviewGain_Parses()
    {
        Assert.True(CommandCodec.TryParse("{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_preview_gain\",\"gain_db\":-6}}", out _, out _, out var command));
        Assert.Equal(new SetPreviewGain(-6), command);
    }

    [Fact]
    public void SetPreviewMuted_Parses()
    {
        Assert.True(CommandCodec.TryParse("{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_preview_muted\",\"muted\":true}}", out _, out _, out var command));
        Assert.Equal(new SetPreviewMuted(true), command);
    }

    [Fact]
    public void SetGlobalAudio_WrongBandCount_Throws()
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_global_audio\","
            + "\"pan\":0,\"mono\":false,\"hpf_hz\":0,\"eq\":{\"bands\":[]}"
            + ",\"limiter\":{\"enabled\":true,\"threshold_db\":-1,\"release_ms\":100}}}";

        Assert.Throws<FormatException>(() => CommandCodec.TryParse(json, out _, out _, out _));
    }

    [Fact]
    public void SetTrackAudio_GainOutOfRange_Throws()
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_track_audio\","
            + "\"track\":\"" + Guid.NewGuid() + "\",\"gain_db\":99,\"pan\":0,\"eq\":" + EqJson() + "}}";

        Assert.Throws<FormatException>(() => CommandCodec.TryParse(json, out _, out _, out _));
    }

    [Fact]
    public async Task SetPreviewMuted_Roundtrip_Ack()
    {
        var engine = new CapturingEngine();
        var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()));
        await host.StartAsync();
        try
        {
            var client = new TestClient();
            await client.ConnectAsync(host.WebsocketEndpoint, "secret");
            using (client)
            {
                await client.SendAsync("{\"client\":\"pult-1\",\"seq\":1,\"command\":{\"type\":\"set_preview_muted\",\"muted\":true}}");

                var ack = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
                Assert.Equal(1, ack.GetProperty("seq").GetInt64());
            }
            Assert.True(engine.PreviewMuted);
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public void SetClickSettings_Parses()
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_click_settings\",\"session\":3,\"bpm\":120,\"beats_per_bar\":4,\"gain_db\":-6,\"offset_ms\":25}}";

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        Assert.Equal(new SetClickSettings(new PreviewSessionHandle(3), new ClickSettings(120, 4, -6, 25)), command);
    }

    [Fact]
    public void SetClickMuted_Parses()
    {
        Assert.True(CommandCodec.TryParse("{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_click_muted\",\"session\":3,\"muted\":true}}", out _, out _, out var command));
        Assert.Equal(new SetClickMuted(new PreviewSessionHandle(3), true), command);
    }

    [Fact]
    public void SetClickSettings_BadBpm_Throws()
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_click_settings\",\"session\":3,\"bpm\":0,\"beats_per_bar\":4,\"gain_db\":0,\"offset_ms\":0}}";

        Assert.Throws<FormatException>(() => CommandCodec.TryParse(json, out _, out _, out _));
    }

    [Fact]
    public void SetClickSettings_BadBeats_Throws()
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_click_settings\",\"session\":3,\"bpm\":120,\"beats_per_bar\":0,\"gain_db\":0,\"offset_ms\":0}}";

        Assert.Throws<FormatException>(() => CommandCodec.TryParse(json, out _, out _, out _));
    }

    [Fact]
    public async Task SetClickSettings_Roundtrip_Ack()
    {
        var engine = new CapturingEngine();
        var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()));
        await host.StartAsync();
        try
        {
            var client = new TestClient();
            await client.ConnectAsync(host.WebsocketEndpoint, "secret");
            using (client)
            {
                await client.SendAsync("{\"client\":\"pult-1\",\"seq\":7,\"command\":{\"type\":\"set_click_settings\",\"session\":3,\"bpm\":120,\"beats_per_bar\":4,\"gain_db\":-6,\"offset_ms\":25}}");

                var ack = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
                Assert.Equal(7, ack.GetProperty("seq").GetInt64());
            }
            Assert.Equal(new PreviewSessionHandle(3), engine.ClickSession);
            Assert.Equal(new ClickSettings(120, 4, -6, 25), engine.Click);
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public async Task SetClickSettings_BadGain_Rejected()
    {
        var engine = new CapturingEngine();
        var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()));
        await host.StartAsync();
        try
        {
            var client = new TestClient();
            await client.ConnectAsync(host.WebsocketEndpoint, "secret");
            using (client)
            {
                await client.SendAsync("{\"client\":\"pult-1\",\"seq\":7,\"command\":{\"type\":\"set_click_settings\",\"session\":3,\"bpm\":120,\"beats_per_bar\":4,\"gain_db\":99,\"offset_ms\":0}}");

                var rejected = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "rejected", TimeSpan.FromSeconds(5));
                Assert.Equal("gain-out-of-range", rejected.GetProperty("reason").GetString());
            }
            Assert.Null(engine.Click);
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public async Task SetClickMuted_Roundtrip_Ack()
    {
        var engine = new CapturingEngine();
        var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()));
        await host.StartAsync();
        try
        {
            var client = new TestClient();
            await client.ConnectAsync(host.WebsocketEndpoint, "secret");
            using (client)
            {
                await client.SendAsync("{\"client\":\"pult-1\",\"seq\":7,\"command\":{\"type\":\"set_click_muted\",\"session\":3,\"muted\":false}}");

                var ack = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
                Assert.Equal(7, ack.GetProperty("seq").GetInt64());
            }
            Assert.Equal(new PreviewSessionHandle(3), engine.ClickMutedSession);
            Assert.False(engine.ClickMutedFlag);
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public void SetTrackBpm_Parses()
    {
        var id = Guid.NewGuid();
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_track_bpm\",\"track\":\"" + id + "\",\"bpm\":128.5}}";

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        Assert.Equal(new SetTrackBpm(new TrackId(id), 128.5), command);
    }

    [Fact]
    public void SetTrackBpm_Null_Clears()
    {
        var id = Guid.NewGuid();
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_track_bpm\",\"track\":\"" + id + "\",\"bpm\":null}}";

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        Assert.Equal(new SetTrackBpm(new TrackId(id), null), command);
    }

    [Fact]
    public void StartSessionTrack_Parses()
    {
        var id = Guid.NewGuid();
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"start_session_track\",\"session\":7,\"track\":\"" + id + "\"}}";

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        Assert.Equal(new StartSessionTrack(new PreviewSessionHandle(7), new TrackId(id)), command);
    }

    private sealed class CapturingEngine : IAudioEngine
    {
        public bool PreviewMuted { get; private set; }

        public PreviewSessionHandle ClickSession { get; private set; }

        public ClickSettings? Click { get; private set; }

        public PreviewSessionHandle ClickMutedSession { get; private set; }

        public bool ClickMutedFlag { get; private set; }

        public event Action<StreamEvent>? Events { add { } remove { } }

        public StreamHandle StartStream(TrackSource source, StreamOptions options) => new(1);

        public void Transport(StreamHandle handle, TransportCommand command)
        {
        }

        public void SetMix(StreamHandle handle, MixParameters mix)
        {
        }

        public void Seek(StreamHandle handle, TimeSpan position)
        {
        }

        public void SetMasterGain(double gainDb)
        {
        }

        public void SetSmoothing(Smoothing smoothing)
        {
        }

        public void Panic(PanicSpec spec)
        {
        }

        public void DisposeStream(StreamHandle handle)
        {
        }

        public void SetPreviewMuted(bool muted) => PreviewMuted = muted;

        public void SetClick(PreviewSessionHandle session, ClickSettings settings)
        {
            ClickSession = session;
            Click = settings;
        }

        public void SetClickMuted(PreviewSessionHandle session, bool muted)
        {
            ClickMutedSession = session;
            ClickMutedFlag = muted;
        }
    }

    [Fact]
    public async Task Preview_WithoutToken_Returns401()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(new Uri(_host.HttpEndpoint, "preview"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken token)
    {
        var result = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(result.AsMemory(offset), token);
            Assert.True(read > 0, "stream ended before expected bytes arrived");
            offset += read;
        }
        return result;
    }

    [Fact]
    public async Task Preview_WithData_StreamsWavPcm()
    {
        var tap = new PreviewTap(4800, 2);
        var frames = 2400;
        var source = new float[frames * 2];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = 0.5f;
        }
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()), previewTap: tap);
        await host.StartAsync();
        try
        {
            using var http = new HttpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(
                new Uri(host.HttpEndpoint, "preview?token=secret"),
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("audio/x-wav", response.Content.Headers.ContentType!.MediaType);
            Assert.Null(response.Content.Headers.ContentLength);
            var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var header = await ReadExactlyAsync(stream, 44, cts.Token);
            Assert.Equal((byte)'R', header[0]);
            Assert.Equal((byte)'I', header[1]);
            Assert.Equal((byte)'F', header[2]);
            Assert.Equal((byte)'F', header[3]);
            tap.Publish(source);
            var audio = await ReadExactlyAsync(stream, frames * 2 * 2, cts.Token);
            Assert.Equal(0x4000, audio[0] | (audio[1] << 8));
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

}
