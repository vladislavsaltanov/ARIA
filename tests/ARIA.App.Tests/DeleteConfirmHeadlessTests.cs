namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Avalonia.Controls;
using Avalonia.Headless;

[Collection("headless")]
public sealed class DeleteConfirmHeadlessTests : IDisposable
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/confirm.flac", "confirm", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task ProjectCenter_BindsDeleteConfirm()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
            var center = new ProjectCenter { DataContext = vm };
            var window = new Window { Content = center, Width = 1200, Height = 800 };
            window.Show();

            Assert.NotNull(vm.ConfirmDeleteProject);

            center.DataContext = null;

            Assert.Null(vm.ConfirmDeleteProject);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }
}
