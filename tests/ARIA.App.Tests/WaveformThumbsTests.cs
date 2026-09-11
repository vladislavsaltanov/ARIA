namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.Services;
using Aria.Core.Model;
using Aria.Persistence;
using Avalonia.Headless;

[Collection("headless")]
public sealed class WaveformThumbsTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public void MissingPeaks_ReturnsNull()
    {
        var thumbs = new WaveformThumbs(new MemoryWaveformStore());

        Assert.Null(thumbs.For(TrackId.New()));
    }

    [Fact]
    public async Task Peaks_BuildNonEmptyGeometry_AndMemoize()
    {
        await _session.Dispatch(() =>
        {
            var store = new MemoryWaveformStore();
            var id = TrackId.New();
            var points = ImmutableArray.CreateBuilder<PeakPoint>();
            for (var i = 0; i < 250; i++)
            {
                points.Add(new PeakPoint(-0.5f, 0.5f));
            }
            store.Save(new WaveformPeaks(id, 25, 48000, points.ToImmutable()));
            var thumbs = new WaveformThumbs(store);

            var first = thumbs.For(id);

            Assert.NotNull(first);
            Assert.True(first.Bounds.Width > 0 && first.Bounds.Height > 0);
            Assert.Same(first, thumbs.For(id));
            return 0;
        }, CancellationToken.None);
    }

    private sealed class MemoryWaveformStore : IWaveformStore
    {
        private readonly Dictionary<TrackId, WaveformPeaks> _peaks = [];

        public void Save(WaveformPeaks peaks) => _peaks[peaks.TrackId] = peaks;

        public WaveformPeaks? Load(TrackId trackId) => _peaks.GetValueOrDefault(trackId);

        public void Dispose()
        {
        }
    }
}
