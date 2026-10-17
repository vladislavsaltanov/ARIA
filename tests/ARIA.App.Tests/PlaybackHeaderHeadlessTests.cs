namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Aria.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;

[Collection("headless")]
public sealed class PlaybackHeaderHeadlessTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(),
        "/audio/header-test.flac",
        "header-test",
        TimeSpan.FromSeconds(90),
        new TrackDefaults());

    private readonly HeadlessUnitTestSession _session;

    public PlaybackHeaderHeadlessTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task MainWindow_Composes_PlaybackHeader_AndDragOnWaveSeeks()
    {
        await _session.Dispatch(() =>
        {
            var window = new Views.MainWindow(null);
            window.Show();
            var header = window.FindControl<Views.PlaybackHeader>("PlaybackHeader");
            Assert.NotNull(header);
            Assert.NotNull(header.FindControl<TextBlock>("ShowClockBig"));
            Assert.NotNull(header.FindControl<TextBlock>("TimerSubBig"));
            Assert.NotNull(header.FindControl<Border>("WaveformStrip"));
            Assert.NotNull(header.FindControl<Canvas>("WaveformCanvas"));
            Assert.NotNull(header.FindControl<Rectangle>("WaveCursor"));
            Assert.NotNull(header.FindControl<Button>("StartClockButton"));
            Assert.NotNull(header.FindControl<Button>("PauseClockButton"));
            Assert.NotNull(header.FindControl<Button>("ResetClockButton"));
            window.Close();

            using var lane = new TestLane(TestTrack.Id, status: TransportStatus.Stopped);
            var dragHeader = lane.Header;
            var dragWindow = lane.Window;
            var controller = lane.Controller;
            var strip = dragHeader.FindControl<Border>("WaveformStrip");
            Assert.NotNull(strip);
            var cursor = dragHeader.FindControl<Rectangle>("WaveCursor");
            Assert.NotNull(cursor);
            Assert.True(cursor.IsVisible);
            var canvas = dragHeader.FindControl<Canvas>("WaveformCanvas");
            Assert.NotNull(canvas);
            Assert.True(canvas.Children.Count > 0);
            Assert.Equal(canvas.Bounds.Width / 3, Canvas.GetLeft(cursor), 1);
            Assert.Same(canvas, cursor.Parent);
            var cursorAt = cursor.TranslatePoint(new Point(0, 0), canvas);
            Assert.NotNull(cursorAt);
            Assert.Equal(canvas.Bounds.Width / 3, cursorAt.Value.X, 0);

            var width = strip.Bounds.Width;
            var at = strip.TranslatePoint(new Point(width * 0.25, 28), dragWindow);
            Assert.NotNull(at);
            dragWindow.MouseDown(at.Value, MouseButton.Left);
            dragWindow.MouseUp(at.Value, MouseButton.Left);

            var command = Assert.IsType<SeekTo>(Assert.Single(controller.Received));
            Assert.Equal(22.5, command.FilePosition.TotalSeconds, 3);

            dragWindow.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Waveform_Draws_FromViewModelCurrentTrack_WhenTransportStopped()
    {
        await _session.Dispatch(() =>
        {
            using var lane = new TestLane(TestTrack.Id, status: TransportStatus.Stopped, peaks: 100, bindMonitor: false);
            var canvas = lane.Header.FindControl<Canvas>("WaveformCanvas");
            Assert.NotNull(canvas);
            Assert.True(canvas.Children.Count > 0);
            return 0;
        }, CancellationToken.None);
    }

}