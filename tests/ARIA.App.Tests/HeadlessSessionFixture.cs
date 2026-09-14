namespace Aria.App.Tests;

using Avalonia.Headless;

public sealed class HeadlessSessionFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => Session.Dispose();
}
