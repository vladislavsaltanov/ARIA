namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.Services;
using Aria.Core.Model;
using Aria.Persistence;
using Avalonia.Headless;

[Collection("headless")]
public sealed class WaveformThumbsTests
{
    private readonly HeadlessUnitTestSession _session;

    public WaveformThumbsTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

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
            var id = TrackId.New();
            var store = MemoryWaveformStore.Seeded(id, 250);
            var thumbs = new WaveformThumbs(store);

            var first = thumbs.For(id);

            Assert.NotNull(first);
            Assert.True(first.Bounds.Width > 0 && first.Bounds.Height > 0);
            Assert.Same(first, thumbs.For(id));
            return 0;
        }, CancellationToken.None);
    }

}
