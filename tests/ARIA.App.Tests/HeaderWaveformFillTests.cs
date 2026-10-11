namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;
using Aria.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;

[Collection("headless")]
public sealed class HeaderWaveformFillTests
{
    private readonly HeadlessUnitTestSession _session;

    public HeaderWaveformFillTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task AttachedPeaks_RenderFilledWaveGeometry()
    {
        await _session.Dispatch(() =>
        {
            var trackId = TrackId.New();
            var store = MemoryWaveformStore.Seeded(trackId, 500);
            var monitor = new PlaybackMonitor();
            var deck = new DeckContent(null, trackId, "test", null, EndAction.Pause, TimeSpan.FromSeconds(90), TimeSpan.Zero);
            monitor.Bind(new StreamHandle(1), deck);
            monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(30));

            var header = new Views.PlaybackHeader();
            using var host = new HeaderWindow(header);
            header.Attach(monitor, store);
            host.UpdateLayout();

            var canvas = header.FindControl<Canvas>("WaveformCanvas");
            Assert.NotNull(canvas);
            var paths = canvas.Children.OfType<Path>().ToList();
            Assert.Equal(2, paths.Count);
            var width = canvas.Bounds.Width;
            var height = canvas.Bounds.Height;
            Assert.True(width > 0 && height > 0);
            var center = height / 2.0;
            var inside = new Point(width * 0.75, center * 0.9);
            foreach (var path in paths)
            {
                var geometry = Assert.IsAssignableFrom<Geometry>(path.Data);
                Assert.True(geometry.FillContains(inside));
            }
            return 0;
        }, CancellationToken.None);
    }

}
