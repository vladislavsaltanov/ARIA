namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.Remote;

public sealed class RemoteCredentialCliTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "aria-credcli-tests",
        Guid.NewGuid().ToString("N"));

    private string DataDirectory => _directory;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RemoteCredentialsStore Store() => new(Path.Combine(_directory, "remote-auth.json"));

    [Fact]
    public void Show_GeneratesPair()
    {
        var exit = RemoteCredentialCli.Run([], DataDirectory);

        Assert.Equal(0, exit);
        var credentials = Store().Load();
        Assert.StartsWith("ARIA-", credentials.Identifier);
        Assert.NotEmpty(credentials.Password);
    }

    [Fact]
    public void SetPassword_ChangesPassword_KeepsIdentifier()
    {
        var store = Store();
        var original = store.Load();

        var exit = RemoteCredentialCli.Run(["--remote-set-password", "my-secret"], DataDirectory);

        Assert.Equal(0, exit);
        var changed = store.Load();
        Assert.Equal(original.Identifier, changed.Identifier);
        Assert.Equal("my-secret", changed.Password);
    }

    [Fact]
    public void SetIdentifier_ChangesIdentifier_KeepsPassword()
    {
        var store = Store();
        var original = store.Load();

        var exit = RemoteCredentialCli.Run(["--remote-id", "ARIA-MAIN"], DataDirectory);

        Assert.Equal(0, exit);
        var changed = store.Load();
        Assert.Equal("ARIA-MAIN", changed.Identifier);
        Assert.Equal(original.Password, changed.Password);
    }

    [Fact]
    public void ResetPassword_GeneratesNew_KeepsIdentifier()
    {
        var store = Store();
        var original = store.Load();

        var exit = RemoteCredentialCli.Run(["--remote-reset-password"], DataDirectory);

        Assert.Equal(0, exit);
        var changed = store.Load();
        Assert.Equal(original.Identifier, changed.Identifier);
        Assert.NotEqual(original.Password, changed.Password);
    }

    [Fact]
    public void SetIdentifierAndPassword_Together()
    {
        var exit = RemoteCredentialCli.Run(["--remote-id", "ARIA-MAIN", "--remote-set-password", "xyz"], DataDirectory);

        Assert.Equal(0, exit);
        var changed = Store().Load();
        Assert.Equal("ARIA-MAIN", changed.Identifier);
        Assert.Equal("xyz", changed.Password);
    }
}
