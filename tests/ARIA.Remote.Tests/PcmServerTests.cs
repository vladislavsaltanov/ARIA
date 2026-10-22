namespace Aria.Remote.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Aria.Remote.Monitor;

public sealed class PcmServerTests : IAsyncLifetime
{
    private const int FileFrames = 144000;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-pcm-{Guid.NewGuid():N}");
    private readonly List<Track> _tracks = [];
    private readonly HttpClient _http = new();
    private readonly PlaybackMonitor _bindings = new();
    private readonly MixReportRing _ring = new();
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;
    private RemoteCredentials _pair = null!;

    public async Task InitializeAsync()
    {
        var store = new RemoteCredentialsStore(Path.Combine(_directory, "remote-auth.json"));
        _pair = store.Load();
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        var feed = new MonitorFeed(_ring, new FakeStamps(), new MiniaudioSourceFactory(48000, 2), new TrackListSources(() => [.. _tracks]), _bindings, () => 0.0);
        _host = new RemoteHost(_bus, new RemoteOptions("unused", TestPorts.Next(), store), monitorFeed: feed);
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        _bus.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Track AddTrack(string name, string? directory = null)
    {
        var path = PcmTestFiles.WriteWav(directory ?? _directory, name, FileFrames);
        var track = new Track(TrackId.New(), path, name, TimeSpan.FromSeconds(3), new TrackDefaults());
        _tracks.Add(track);
        return track;
    }

    private async Task<string> TokenAsync(string password)
    {
        var response = await _http.PostAsJsonAsync(new Uri(_host.HttpEndpoint, "auth"), new { identifier = _pair.Identifier, password });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<HttpResponseMessage> GetPcmAsync(string token, string query) =>
        await _http.GetAsync(new Uri(_host.HttpEndpoint, $"monitor/pcm?token={token}&{query}"));

    private static async Task<short[]> SamplesAsync(HttpResponseMessage response) =>
        MemoryMarshal.Cast<byte, short>(await response.Content.ReadAsByteArrayAsync()).ToArray();

    [Fact]
    public async Task Pcm_FrameExact_MatchesSourceFactory()
    {
        var track = AddTrack("song.wav");
        var token = await TokenAsync(_pair.Password);

        var response = await GetPcmAsync(token, $"track={track.Id.Value}&from=50000&frames=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(PcmTestFiles.Reference(track.FilePath, 50000, 1000), await SamplesAsync(response));
        Assert.Equal("1000", response.Headers.GetValues("X-Aria-Frames").Single());
    }

    [Fact]
    public async Task Pcm_ChunkBoundary_Seamless()
    {
        var track = AddTrack("song.wav");
        var token = await TokenAsync(_pair.Password);

        var response = await GetPcmAsync(token, $"track={track.Id.Value}&from=47990&frames=20");

        Assert.Equal(PcmTestFiles.Reference(track.FilePath, 47990, 20), await SamplesAsync(response));
    }

    [Fact]
    public async Task Pcm_AtEof_ShortAndTotalHeader()
    {
        var track = AddTrack("song.wav");
        var token = await TokenAsync(_pair.Password);

        var response = await GetPcmAsync(token, $"track={track.Id.Value}&from=143000&frames=5000");

        Assert.Equal("1000", response.Headers.GetValues("X-Aria-Frames").Single());
        Assert.Equal("144000", response.Headers.GetValues("X-Aria-Total-Frames").Single());
        Assert.Equal(2000, (await SamplesAsync(response)).Length);
    }

    [Theory]
    [InlineData("from=0&frames=10")]
    [InlineData("track=not-a-guid&from=0&frames=10")]
    [InlineData("track={0}&from=-1&frames=10")]
    [InlineData("track={0}&from=0&frames=0")]
    [InlineData("track={0}&from=0&frames=240001")]
    public async Task Pcm_BadParams_400(string query)
    {
        var track = AddTrack("song.wav");
        var token = await TokenAsync(_pair.Password);

        var response = await GetPcmAsync(token, string.Format(query, track.Id.Value));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pcm_WithoutToken_401()
    {
        var response = await _http.GetAsync(new Uri(_host.HttpEndpoint, $"monitor/pcm?track={Guid.NewGuid()}&from=0&frames=10"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pcm_UnknownTrack_404()
    {
        var token = await TokenAsync(_pair.Password);

        var response = await GetPcmAsync(token, $"track={Guid.NewGuid()}&from=0&frames=10");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Pcm_ListenRole_ForeignTrack_403()
    {
        var track = AddTrack("song.wav");
        var token = await TokenAsync(_pair.ListenPassword);

        var response = await GetPcmAsync(token, $"track={track.Id.Value}&from=0&frames=10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pcm_ListenRole_SeenTrack_Ok()
    {
        var track = AddTrack("song.wav");
        _bindings.Bind(new StreamHandle(1), new DeckContent(null, track.Id, "song", null, EndAction.Stop, track.Duration, TimeSpan.Zero));
        var token = await TokenAsync(_pair.ListenPassword);
        var deadline = Environment.TickCount64 + 5000;
        while (!_host.AllowedMonitorTracks.Contains(track.Id))
        {
            _ring.Publish(_ring.LatestSequence * 512, 0, [new VoiceReport { Tag = 1, Active = true, FadeTo = 1, FadeProgress = 1 }], false, false);
            Assert.True(Environment.TickCount64 < deadline, "track never allowed");
            await Task.Delay(20);
        }

        var response = await GetPcmAsync(token, $"track={track.Id.Value}&from=0&frames=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Pcm_Undecodable_422()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "broken.wav");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);
        var track = new Track(TrackId.New(), path, "broken", TimeSpan.FromSeconds(3), new TrackDefaults());
        _tracks.Add(track);
        var token = await TokenAsync(_pair.Password);

        var response = await GetPcmAsync(token, $"track={track.Id.Value}&from=0&frames=10");

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [Fact]
    public async Task Pcm_CyrillicPath_Decodes()
    {
        var track = AddTrack("Тест подложка.wav", Path.Combine(_directory, "Мониторинг"));
        var token = await TokenAsync(_pair.Password);

        var response = await GetPcmAsync(token, $"track={track.Id.Value}&from=1000&frames=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PcmTestFiles.Reference(track.FilePath, 1000, 100), await SamplesAsync(response));
    }

    [Fact]
    public async Task Cache_EvictsOverBudget()
    {
        var track = AddTrack("song.wav");
        using var cache = new PcmCache(new MiniaudioSourceFactory(48000, 2), new TrackListSources(() => [.. _tracks]), budgetBytes: 2L * PcmCache.ChunkFrames * 2 * 2);

        await cache.ReadAsync(track.Id, 0, 10, CancellationToken.None);
        await cache.ReadAsync(track.Id, 48000, 10, CancellationToken.None);
        await cache.ReadAsync(track.Id, 96000, 10, CancellationToken.None);
        Assert.Equal(3, cache.DecodedChunks);

        await cache.ReadAsync(track.Id, 96010, 10, CancellationToken.None);
        Assert.Equal(3, cache.DecodedChunks);
        await cache.ReadAsync(track.Id, 5, 10, CancellationToken.None);
        Assert.Equal(4, cache.DecodedChunks);
    }
}
