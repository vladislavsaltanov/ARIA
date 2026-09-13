namespace Aria.App.Tests;

using Aria.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

[Collection("headless")]
public sealed class TransportBarLayoutTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task ProgressStrip_StaysAbove_TrackNameRow()
    {
        var stripHeight = 0.0;
        var stripBottom = double.MaxValue;
        var nameTop = double.MinValue;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            window.Measure(new Size(1440, 900));
            window.Arrange(new Rect(0, 0, 1440, 900));
            var bar = window.FindControl<TransportBar>("TransportBar");
            Assert.NotNull(bar);
            var grid = bar.GetVisualDescendants().OfType<Grid>()
                .First(g => g.RowDefinitions.Count == 3 && g.Parent is Border);
            var strip = grid.Children.OfType<ProgressBar>().First(p => Grid.GetRow(p) == 0);
            var names = grid.Children.OfType<StackPanel>().First(p => Grid.GetRow(p) == 1);
            var stripRect = ToBarRect(strip, bar);
            var namesRect = ToBarRect(names, bar);
            stripHeight = stripRect.Height;
            stripBottom = stripRect.Bottom;
            nameTop = namesRect.Top;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.True(stripHeight > 0);
        Assert.True(stripBottom <= nameTop);
    }

    private static Rect ToBarRect(Control control, Control bar) =>
        new(control.TranslatePoint(new Point(0, 0), bar) ?? new Point(0, 0), control.Bounds.Size);
}
