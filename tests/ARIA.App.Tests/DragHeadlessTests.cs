namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.ViewModels;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;

[Collection("headless")]
public sealed class DragHeadlessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-drag-headless-{Guid.NewGuid():N}");
    private readonly HeadlessUnitTestSession _session;

    public DragHeadlessTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReorderWithinProject_ShowsInsertionLine_AndMovesEntry()
    {
        var wav1 = TestWav.Write(_directory, "reorder-a.wav");
        var wav2 = TestWav.Write(_directory, "reorder-b.wav");
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        await host.StartAsync();
        Assert.Equal(2, (await host.ImportTracksAsync([wav1, wav2])).Added);
        host.Submit(new CreateProject("ReorderTarget"));
        var tracks = await TracksEventuallyAsync(host, 2);
        var projectId = await ProjectEventuallyAsync(host);
        host.Submit(new AddEntry(projectId, tracks[0].Id, 0));
        Assert.Single(await EntriesEventuallyAsync(host, 1));
        host.Submit(new AddEntry(projectId, tracks[1].Id, 1));
        var initial = await EntriesEventuallyAsync(host, 2);
        Assert.Equal(2, initial.Length);
        Assert.Equal(tracks[0].Id, initial[0].TrackId);
        Assert.Equal(tracks[1].Id, initial[1].TrackId);
        Window? window = null;
        await _session.Dispatch(() =>
        {
            var sync = SynchronizationContext.Current;
            var projects = new ProjectsViewModel(host.Bus, () => host.Library!.Load().Tracks, sync: sync);
            var queue = new QueueViewModel(host.Bus, sync);
            window = new Views.MainWindow(null, projectsViewModel: projects, queueViewModel: queue);
            window.Show();
            window.UpdateLayout();
            return 0;
        }, CancellationToken.None);

        await _session.Dispatch(() =>
        {
            window!.UpdateLayout();
            var entryList = window.FindControl<Views.ProjectCenter>("ProjectCenter")!.EntryListBox;
            var first = Assert.IsType<ListBoxItem>(entryList.ItemsPanelRoot!.Children[0]);
            var second = Assert.IsType<ListBoxItem>(entryList.ItemsPanelRoot!.Children[1]);
            var from = first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height / 2), window);
            var bottom = second.TranslatePoint(new Point(second.Bounds.Width / 2, second.Bounds.Height - 4), window);
            var top = second.TranslatePoint(new Point(second.Bounds.Width / 2, 4), window);
            Assert.NotNull(from);
            Assert.NotNull(bottom);
            Assert.NotNull(top);
            window.MouseDown(from.Value, MouseButton.Left);
            window.MouseMove(bottom.Value);
            Assert.Contains("dropAfter", second.Classes);
            window.MouseMove(top.Value);
            Assert.Contains("dropBefore", second.Classes);
            Assert.DoesNotContain("dropAfter", second.Classes);
            window.MouseMove(bottom.Value);
            window.MouseUp(bottom.Value, MouseButton.Left);
            Assert.DoesNotContain("dropBefore", second.Classes);
            Assert.DoesNotContain("dropAfter", second.Classes);
            return 0;
        }, CancellationToken.None);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        ImmutableArray<ProjectEntry> entries = default;
        while (DateTime.UtcNow < deadline)
        {
            entries = host.Bus.Snapshot().Show.Projects[0].Entries;
            if (entries.Length == 2 && entries[0].TrackId == tracks[1].Id)
            {
                break;
            }
            await Task.Delay(25);
        }
        Assert.Equal(tracks[1].Id, entries[0].TrackId);
        Assert.Equal(tracks[0].Id, entries[1].TrackId);
        await CloseAsync(window!);
    }

    [Fact]
    public async Task InterruptedDrag_ResetClearsInsertionLine_AndBlocksMove()
    {
        var wav1 = TestWav.Write(_directory, "stuck-a.wav");
        var wav2 = TestWav.Write(_directory, "stuck-b.wav");
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        await host.StartAsync();
        Assert.Equal(2, (await host.ImportTracksAsync([wav1, wav2])).Added);
        host.Submit(new CreateProject("StuckTarget"));
        var tracks = await TracksEventuallyAsync(host, 2);
        var projectId = await ProjectEventuallyAsync(host);
        host.Submit(new AddEntry(projectId, tracks[0].Id, 0));
        host.Submit(new AddEntry(projectId, tracks[1].Id, 1));
        Assert.Equal(2, (await EntriesEventuallyAsync(host, 2)).Length);
        Views.MainWindow? window = null;
        await _session.Dispatch(() =>
        {
            var sync = SynchronizationContext.Current;
            var projects = new ProjectsViewModel(host.Bus, () => host.Library!.Load().Tracks, sync: sync);
            var queue = new QueueViewModel(host.Bus, sync);
            window = new Views.MainWindow(null, projectsViewModel: projects, queueViewModel: queue);
            window.Show();
            window.UpdateLayout();
            return 0;
        }, CancellationToken.None);

        await _session.Dispatch(() =>
        {
            window!.UpdateLayout();
            var entryList = window.FindControl<Views.ProjectCenter>("ProjectCenter")!.EntryListBox;
            var first = Assert.IsType<ListBoxItem>(entryList.ItemsPanelRoot!.Children[0]);
            var second = Assert.IsType<ListBoxItem>(entryList.ItemsPanelRoot!.Children[1]);
            var from = first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height / 2), window);
            var bottom = second.TranslatePoint(new Point(second.Bounds.Width / 2, second.Bounds.Height - 4), window);
            Assert.NotNull(from);
            Assert.NotNull(bottom);
            window.MouseDown(from.Value, MouseButton.Left);
            window.MouseMove(bottom.Value);
            Assert.Contains("dropAfter", second.Classes);
            window.ResetDrag();
            window.MouseUp(bottom.Value, MouseButton.Left);
            Assert.DoesNotContain("dropBefore", second.Classes);
            Assert.DoesNotContain("dropAfter", second.Classes);
            return 0;
        }, CancellationToken.None);

        var entries = host.Bus.Snapshot().Show.Projects[0].Entries;
        Assert.Equal(tracks[0].Id, entries[0].TrackId);
        Assert.Equal(tracks[1].Id, entries[1].TrackId);
        await CloseAsync(window!);
    }

    private static async Task<ImmutableArray<Track>> TracksEventuallyAsync(AppHost host, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var tracks = host.Library!.Load().Tracks;
        while (DateTime.UtcNow < deadline && tracks.Length < count)
        {
            await Task.Delay(25);
            tracks = host.Library!.Load().Tracks;
        }
        return tracks;
    }

    private static async Task<ProjectId> ProjectEventuallyAsync(AppHost host)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (host.Bus.Snapshot().Show.Projects.Length > 0)
            {
                return host.Bus.Snapshot().Show.Projects[0].Id;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("project never appeared");
    }

    private static async Task<ImmutableArray<ProjectEntry>> EntriesEventuallyAsync(AppHost host, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var entries = host.Bus.Snapshot().Show.Projects.Length > 0
            ? host.Bus.Snapshot().Show.Projects[0].Entries
            : default;
        while (DateTime.UtcNow < deadline && entries.Length < count)
        {
            await Task.Delay(25);
            entries = host.Bus.Snapshot().Show.Projects.Length > 0
                ? host.Bus.Snapshot().Show.Projects[0].Entries
                : default;
        }
        return entries;
    }

    private async Task CloseAsync(Window window)
    {
        await _session.Dispatch(() =>
        {
            window.Close();
            return 0;
        }, CancellationToken.None);
    }
}
