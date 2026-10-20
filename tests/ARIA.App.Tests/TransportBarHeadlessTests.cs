namespace Aria.App.Tests;

using Aria.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

[Collection("headless")]
public sealed class TransportBarHeadlessTests
{
    private readonly HeadlessUnitTestSession _session;

    public TransportBarHeadlessTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task MainWindow_Composes_TransportBar_WithoutHelpOverlay()
    {
        await _session.Dispatch(() =>
        {
            var window = new Views.MainWindow(null);
            window.Show();

            var bar = window.FindControl<Views.TransportBar>("TransportBar");
            Assert.NotNull(bar);
            Assert.NotNull(bar.FindControl<Avalonia.Controls.Button>("PlayPauseButton"));
            Assert.NotNull(bar.FindControl<Avalonia.Controls.Button>("StopButton"));
            Assert.NotNull(bar.FindControl<Avalonia.Controls.Button>("NextButton"));
            Assert.NotNull(bar.FindControl<Avalonia.Controls.Button>("MuteButton"));
            Assert.NotNull(bar.FindControl<Avalonia.Controls.Button>("PanicButton"));
            Assert.NotNull(window.FindControl<Views.RailProjects>("RailProjects"));
            Assert.Null(window.FindControl<Avalonia.Controls.Control>("LibrarySection"));
            Assert.NotNull(window.FindControl<Views.ProjectCenter>("ProjectCenter"));
            var center = window.FindControl<Views.ProjectCenter>("ProjectCenter");
            Assert.NotNull(center);
            var playlist = center.FindControl<Avalonia.Controls.Panel>("PlaylistArea");
            Assert.NotNull(playlist);
            Assert.NotNull(playlist.ContextMenu);
            Assert.NotEmpty(playlist.ContextMenu!.Items);
            Assert.NotNull(window.FindControl<Views.QueueColumn>("QueueColumn"));
            Assert.Null(window.FindControl<Avalonia.Controls.Border>("HelpOverlay"));
            Assert.Null(window.FindControl<Avalonia.Controls.Grid>("HotkeyTable"));

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TransportBar_Binds_ToViewModel()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new Core.Runtime.CommandBus(new ShowControllerStub(), Core.Runtime.BusMode.Inline);
            using var viewModel = new ViewModels.TransportViewModel(bus);
            var window = new MainWindow(null) { DataContext = viewModel };
            window.Show();

            var bar = window.FindControl<TransportBar>("TransportBar");
            Assert.NotNull(bar);
            Assert.Same(viewModel, bar.DataContext);

            var left = bar.FindControl<ProgressBar>("LevelLeftBar");
            var right = bar.FindControl<ProgressBar>("LevelRightBar");
            Assert.NotNull(left);
            Assert.NotNull(right);
            viewModel.LevelLeft = 0.8;
            viewModel.LevelRight = 0.3;
            Assert.Equal(0.8, left.Value);
            Assert.Equal(0.3, right.Value);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TransportBar_MetersFit_Inside_BarHeight()
    {
        await _session.Dispatch(() =>
        {
            var window = new Views.MainWindow(null);
            window.Show();
            window.UpdateLayout();

            var bar = window.FindControl<Views.TransportBar>("TransportBar");
            Assert.NotNull(bar);
            var value = bar.FindControl<TextBlock>("LufsValue");
            var left = bar.FindControl<ProgressBar>("LevelLeftBar");
            var right = bar.FindControl<ProgressBar>("LevelRightBar");
            Assert.NotNull(value);
            Assert.NotNull(left);
            Assert.NotNull(right);
            Assert.True(left.IsVisible);
            Assert.True(right.IsVisible);
            Assert.True(value.IsVisible);

            var meters = new Control[] { value, left, right };
            foreach (var control in meters)
            {
                var bottom = control.TranslatePoint(new Point(0, control.Bounds.Height), bar)!.Value.Y;
                Assert.True(bottom <= bar.Bounds.Height - 2,
                    $"{control.Name} bottom {bottom:F1} exceeds bar height {bar.Bounds.Height:F1}");
            }

            foreach (var label in new[] { bar.FindControl<TextBlock>("LevelLeftLabel")!, bar.FindControl<TextBlock>("LevelRightLabel")! })
            {
                Assert.True(label.Bounds.Height >= label.DesiredSize.Height - 1,
                    $"{label.Name} clipped: arranged {label.Bounds.Height:F1} < desired {label.DesiredSize.Height:F1}");
            }

            var barRight = left.TranslatePoint(new Point(left.Bounds.Width, 0), bar)!.Value.X;
            var valueLeft = value.TranslatePoint(new Point(0, 0), bar)!.Value.X;
            Assert.True(valueLeft < barRight, "LUFS value should sit left of the meters column");

            var lufsBar = bar.FindControl<ProgressBar>("LufsBar");
            Assert.NotNull(lufsBar);
            var labelLeft = bar.FindControl<TextBlock>("LevelLeftLabel")!;
            var labelRight = bar.FindControl<TextBlock>("LevelRightLabel")!;
            var bars = new[] { lufsBar!, left, right };
            foreach (var meter in bars)
            {
                var meterLeft = meter.TranslatePoint(new Point(0, 0), bar)!.Value.X;
                var meterWidth = meter.Bounds.Width;
                Assert.True(meterWidth <= 88.5, $"{meter.Name} width {meterWidth:F1} exceeds the 88px meters column");
                Assert.True(meterLeft >= barRight - 0.5 || meterLeft >= value.TranslatePoint(new Point(value.Bounds.Width, 0), bar)!.Value.X - 0.5,
                    $"{meter.Name} overlaps the value/label column");
            }
            Assert.True(labelLeft.TranslatePoint(new Point(labelLeft.Bounds.Width, 0), bar)!.Value.X <= left.TranslatePoint(new Point(0, 0), bar)!.Value.X + 0.5,
                "L label overlaps the left meter");
            Assert.True(labelRight.TranslatePoint(new Point(labelRight.Bounds.Width, 0), bar)!.Value.X <= right.TranslatePoint(new Point(0, 0), bar)!.Value.X + 0.5,
                "R label overlaps the right meter");

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private sealed class ShowControllerStub : Core.Runtime.IShowHandler
    {
        private readonly Core.State.ShowSnapshot _snapshot = new(
            0,
            new Core.State.ShowState([], null, false, new Core.State.ShowClockState(TimeSpan.Zero, false), [], Core.Model.TrackDigest.Empty, Core.Model.EndAction.Advance),
            0,
            new Core.State.TransportState(Core.State.TransportStatus.Stopped, null, null, []),
            0,
            new Core.State.QueueState([]),
            0,
            new Core.State.MixerState(0, false, TimeSpan.FromMilliseconds(100), Core.Model.Smoothing.Default));

        public Action<Core.State.StateEvent>? Emitted { get; set; }

        public void Handle(Core.Commands.ClientId client, long seq, Core.Commands.Command command)
        {
        }

        public Core.State.ShowSnapshot Snapshot() => _snapshot;
    }
}
