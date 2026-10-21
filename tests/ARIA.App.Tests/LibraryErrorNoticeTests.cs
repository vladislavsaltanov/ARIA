namespace Aria.App.Tests;

using Aria.App;
using Avalonia.Controls;
using Avalonia.Headless;

[Collection("headless")]
public sealed class LibraryErrorNoticeTests
{
    private readonly HeadlessUnitTestSession _session;

    public LibraryErrorNoticeTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task LibraryError_IsNonModal_AndDoesNotStealFocusFromTheShow()
    {
        await _session.Dispatch(() =>
        {
            var owner = new Window { Width = 800, Height = 600 };
            owner.Show();

            var notice = App.ShowLibraryErrorNotice(owner, "disk full");

            Assert.True(notice.IsVisible);
            Assert.False(notice.ShowActivated);
            Assert.True(owner.IsEnabled);
            Assert.Same(owner, notice.Owner);

            notice.Close();
            owner.Close();
            return 0;
        }, CancellationToken.None);
    }
}
