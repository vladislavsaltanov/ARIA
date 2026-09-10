namespace Aria.Remote.Tests;

public sealed class RemoteCredentialsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "aria-credentials-tests",
        Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "remote-auth.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void FirstLoad_CreatesRandomPair_AndPersists()
    {
        var store = new RemoteCredentialsStore(FilePath);

        var credentials = store.Load();

        Assert.StartsWith("ARIA-", credentials.Identifier);
        Assert.Equal(RemoteCredentialsStore.GeneratedPasswordLength, credentials.Password.Length);
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void SecondStoreInstance_RestoresSamePair()
    {
        var first = new RemoteCredentialsStore(FilePath);
        var created = first.Load();

        var second = new RemoteCredentialsStore(FilePath);
        var restored = second.Load();

        Assert.Equal(created, restored);
    }

    [Fact]
    public void Verify_RightPair_True()
    {
        var store = new RemoteCredentialsStore(FilePath);
        var credentials = store.Load();

        Assert.True(store.Verify(credentials.Identifier, credentials.Password));
    }

    [Fact]
    public void Verify_WrongPassword_Or_WrongIdentifier_False()
    {
        var store = new RemoteCredentialsStore(FilePath);
        var credentials = store.Load();

        Assert.False(store.Verify(credentials.Identifier, "nope"));
        Assert.False(store.Verify("ARIA-WRONG", credentials.Password));
        Assert.False(store.Verify("", credentials.Password));
        Assert.False(store.Verify(credentials.Identifier, ""));
    }

    [Fact]
    public void Set_ChangesPair_OldPasswordFails()
    {
        var store = new RemoteCredentialsStore(FilePath);
        var original = store.Load();

        store.Set("ARIA-STAGE", "operator-secret");

        var changed = new RemoteCredentialsStore(FilePath).Load();
        Assert.Equal("ARIA-STAGE", changed.Identifier);
        Assert.Equal("operator-secret", changed.Password);
        Assert.Equal("ARIA-STAGE", store.Identifier);
        Assert.NotEqual(original.Identifier, changed.Identifier);
    }

    [Fact]
    public void ResetPassword_KeepsIdentifier_ReplacesPassword()
    {
        var store = new RemoteCredentialsStore(FilePath);
        var original = store.Load();

        var fresh = store.ResetPassword();
        var now = store.Load();

        Assert.Equal(original.Identifier, now.Identifier);
        Assert.Equal(fresh, now.Password);
        Assert.NotEqual(original.Password, fresh);
        Assert.True(store.Verify(now.Identifier, fresh));
        Assert.False(store.Verify(now.Identifier, original.Password));
    }

    [Fact]
    public void Set_Empty_Throws()
    {
        var store = new RemoteCredentialsStore(FilePath);

        Assert.Throws<ArgumentException>(() => store.Set("", "x"));
        Assert.Throws<ArgumentException>(() => store.Set("  ", "x"));
        Assert.Throws<ArgumentException>(() => store.Set("ARIA-X", ""));
    }
}
