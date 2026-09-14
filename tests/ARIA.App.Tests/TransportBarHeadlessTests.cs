namespace Aria.App.Tests;

using Aria.App.Views;
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
    public async Task MainWindow_Composes_TransportBar_AndHelpOverlay()
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
            Assert.NotNull(window.FindControl<Views.RailPlaylists>("RailPlaylists"));
            Assert.Null(window.FindControl<Avalonia.Controls.Control>("LibrarySection"));
            Assert.NotNull(window.FindControl<Views.PlaylistCenter>("PlaylistCenter"));
            var center = window.FindControl<Views.PlaylistCenter>("PlaylistCenter");
            Assert.NotNull(center);
            Assert.NotNull(center.FindControl<Avalonia.Controls.Button>("ImportPlaylistButton"));
            Assert.NotNull(center.FindControl<Avalonia.Controls.Button>("ExportPlaylistButton"));
            Assert.NotNull(window.FindControl<Views.QueueColumn>("QueueColumn"));
            Assert.NotNull(window.FindControl<Avalonia.Controls.Border>("HelpOverlay"));
            Assert.NotNull(window.FindControl<Avalonia.Controls.Grid>("HotkeyTable"));

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

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private sealed class ShowControllerStub : Core.Runtime.IShowHandler
    {
        private readonly Core.State.ShowSnapshot _snapshot = new(
            0,
            new Core.State.ShowState([], null, false, new Core.State.ShowClockState(TimeSpan.Zero, false), [], Core.Model.TrackDigest.Empty),
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
