namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Core.Commands;
using Aria.Core.Model;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

[Collection("headless")]
public sealed class TrackAudioViewHeadlessTests
{
    private readonly HeadlessUnitTestSession _session;

    public TrackAudioViewHeadlessTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task GridInputs_BindBothWays()
    {
        await _session.Dispatch(() =>
        {
            var submitted = new List<Command>();
            var editor = new TrackAudioVm(submitted.Add, TrackId.New(), initialGrid: new BeatGrid(100, 4, TimeSpan.Zero), trackDuration: TimeSpan.FromMinutes(3));
            var view = new TrackAudioView { DataContext = editor };
            var window = new Window { Content = view, Width = 480, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var bpm = view.FindControl<NumericUpDown>("GridBpm")!;
            var beats = view.FindControl<NumericUpDown>("GridBeats")!;
            var firstBeat = view.FindControl<NumericUpDown>("GridFirstBeat")!;
            Assert.Equal(100m, bpm.Value);
            Assert.Equal(4m, beats.Value);

            bpm.Value = 128.5m;
            beats.Value = 3m;
            firstBeat.Value = 250m;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new BeatGrid(128.5, 3, TimeSpan.FromMilliseconds(250)), Assert.IsType<SetTrackGrid>(submitted[^1]).Grid);
            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackingSearch_SelectionReachesViewModel()
    {
        await _session.Dispatch(() =>
        {
            var calls = new List<TrackId?>();
            var guide = new BackingOption(TrackId.New(), "Гайд");
            var editor = new TrackAudioVm(_ => { }, TrackId.New(), entryId: EntryId.New(), backingOptions: () => [guide], setBacking: calls.Add);
            var view = new TrackAudioView { DataContext = editor };
            var window = new Window { Content = view, Width = 480, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var search = view.FindControl<AutoCompleteBox>("BackingSearch")!;
            Assert.True(search.IsEffectivelyVisible);
            search.SelectedItem = guide;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([guide.Id], calls);
            Assert.Equal("Гайд", editor.BackingName);
            window.Close();
            return 0;
        }, CancellationToken.None);
    }
}
