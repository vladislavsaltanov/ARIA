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
    public void CorruptFile_RegeneratesPair_AndVerifyFailsClosed()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ not json");
        var store = new RemoteCredentialsStore(FilePath);

        var regenerated = store.Load();

        Assert.StartsWith("ARIA-", regenerated.Identifier);
        Assert.Null(store.Verify("ARIA-XXXX", "whatever"));
        Assert.Equal(RemoteRole.Control, store.Verify(regenerated.Identifier, regenerated.Password));
    }

    [Fact]
    public void PersistedFile_IsOwnerOnly_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new RemoteCredentialsStore(FilePath);
        store.Load();

        var mode = File.GetUnixFileMode(FilePath);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
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

        Assert.Equal(RemoteRole.Control, store.Verify(credentials.Identifier, credentials.Password));
    }

    [Fact]
    public void Verify_WrongPassword_Or_WrongIdentifier_False()
    {
        var store = new RemoteCredentialsStore(FilePath);
        var credentials = store.Load();

        Assert.Null(store.Verify(credentials.Identifier, "nope"));
        Assert.Null(store.Verify("ARIA-WRONG", credentials.Password));
        Assert.Null(store.Verify("", credentials.Password));
        Assert.Null(store.Verify(credentials.Identifier, ""));
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
        Assert.Equal(RemoteRole.Control, store.Verify(now.Identifier, fresh));
        Assert.Null(store.Verify(now.Identifier, original.Password));
    }

    [Fact]
    public void Set_Empty_Throws()
    {
        var store = new RemoteCredentialsStore(FilePath);

        Assert.Throws<ArgumentException>(() => store.Set("", "x"));
        Assert.Throws<ArgumentException>(() => store.Set("  ", "x"));
        Assert.Throws<ArgumentException>(() => store.Set("ARIA-X", ""));
    }

    [Fact]
    public void Verify_ListenPassword_ReturnsListen()
    {
        var store = new RemoteCredentialsStore(FilePath);
        var credentials = store.Load();

        Assert.False(string.IsNullOrEmpty(credentials.ListenPassword));
        Assert.NotEqual(credentials.Password, credentials.ListenPassword);
        Assert.Equal(RemoteRole.Listen, store.Verify(credentials.Identifier, credentials.ListenPassword));
        Assert.Equal(RemoteRole.Control, store.Verify(credentials.Identifier, credentials.Password));
        Assert.Null(store.Verify("ARIA-WRONG", credentials.ListenPassword));
    }

    [Fact]
    public void LegacyFile_WithoutListenPassword_GeneratesAndPersists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{\"Identifier\":\"ARIA-OLD1\",\"Password\":\"old-secret\"}");
        var store = new RemoteCredentialsStore(FilePath);

        var loaded = store.Load();

        Assert.Equal("ARIA-OLD1", loaded.Identifier);
        Assert.Equal("old-secret", loaded.Password);
        Assert.Equal(RemoteCredentialsStore.GeneratedPasswordLength, loaded.ListenPassword.Length);
        Assert.Equal(loaded.ListenPassword, new RemoteCredentialsStore(FilePath).Load().ListenPassword);
    }

    [Fact]
    public void ResetListenPassword_KeepsControlPair()
    {
        var store = new RemoteCredentialsStore(FilePath);
        var original = store.Load();

        var fresh = store.ResetListenPassword();

        var now = store.Load();
        Assert.Equal(original.Identifier, now.Identifier);
        Assert.Equal(original.Password, now.Password);
        Assert.Equal(fresh, now.ListenPassword);
        Assert.NotEqual(original.ListenPassword, fresh);
        Assert.Null(store.Verify(now.Identifier, original.ListenPassword));
        Assert.Equal(RemoteRole.Listen, store.Verify(now.Identifier, fresh));
    }

    [Fact]
    public void Set_KeepsListenPassword()
    {
        var store = new RemoteCredentialsStore(FilePath);
        var original = store.Load();

        store.Set("ARIA-STAGE", "operator-secret");

        Assert.Equal(original.ListenPassword, store.Load().ListenPassword);
    }
}
