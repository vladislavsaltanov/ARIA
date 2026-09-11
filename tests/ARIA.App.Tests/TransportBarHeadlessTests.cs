namespace Aria.App.Tests;

using Aria.App.Views;
using Avalonia.Controls;
using Avalonia.Headless;

public sealed class TransportBarHeadlessTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public void MainWindow_Composes_TransportBar_AndHelpOverlay()
    {
        _session.Dispatch(() =>
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
            Assert.NotNull(window.FindControl<Views.LibrarySection>("LibrarySection"));
            Assert.NotNull(window.FindControl<Views.RailPlaylists>("RailPlaylists"));
            Assert.NotNull(window.FindControl<Views.PlaylistCenter>("PlaylistCenter"));
            Assert.NotNull(window.FindControl<Views.QueueColumn>("QueueColumn"));
            Assert.NotNull(window.FindControl<Avalonia.Controls.Border>("HelpOverlay"));
            Assert.NotNull(window.FindControl<Avalonia.Controls.Grid>("HotkeyTable"));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void TransportBar_Binds_ToViewModel()
    {
        _session.Dispatch(() =>
        {
            using var bus = new Core.Runtime.CommandBus(new ShowControllerStub(), Core.Runtime.BusMode.Inline);
            using var viewModel = new ViewModels.TransportViewModel(bus);
            var window = new MainWindow(null) { DataContext = viewModel };
            window.Show();

            var bar = window.FindControl<TransportBar>("TransportBar");
            Assert.NotNull(bar);
            Assert.Same(viewModel, bar.DataContext);

            window.Close();
        }, CancellationToken.None);
    }

    private sealed class ShowControllerStub : Core.Runtime.IShowHandler
    {
        private readonly Core.State.ShowSnapshot _snapshot = new(
            0,
            new Core.State.ShowState([], null, false, new Core.State.ShowClockState(TimeSpan.Zero, false)),
            0,
            new Core.State.TransportState(Core.State.TransportStatus.Stopped, null, null, []),
            0,
            new Core.State.QueueState([]),
            0,
            new Core.State.MixerState(0, false, TimeSpan.FromMilliseconds(100)));

        public Action<Core.State.StateEvent>? Emitted { get; set; }

        public void Handle(Core.Commands.ClientId client, long seq, Core.Commands.Command command)
        {
        }

        public Core.State.ShowSnapshot Snapshot() => _snapshot;
    }
}
