namespace Aria.Remote;

using System.Security.Cryptography;
using System.Text.Json;

public sealed record RemoteCredentials(string Identifier, string Password, string ListenPassword = "");

public sealed class RemoteCredentialsStore : IRemoteCredentials
{
    public const int GeneratedPasswordLength = 12;

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly string _filePath;

    public RemoteCredentialsStore(string filePath)
    {
        _filePath = filePath;
    }

    public string Identifier => Load().Identifier;

    public RemoteCredentials Load()
    {
        if (File.Exists(_filePath) && ReadStored() is { } stored)
        {
            if (string.IsNullOrEmpty(stored.ListenPassword))
            {
                stored = stored with { ListenPassword = GeneratePassword() };
                Persist(stored);
            }
            return new RemoteCredentials(stored.Identifier, stored.Password, stored.ListenPassword!);
        }
        return Regenerate();
    }

    public RemoteRole? Verify(string identifier, string password)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrEmpty(password))
        {
            return null;
        }
        RemoteCredentials stored;
        try
        {
            stored = Load();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
        if (!string.Equals(identifier, stored.Identifier, StringComparison.Ordinal))
        {
            return null;
        }
        if (SecretEquals(stored.Password, password))
        {
            return RemoteRole.Control;
        }
        return SecretEquals(stored.ListenPassword, password) ? RemoteRole.Listen : null;
    }

    public void Set(string identifier, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrEmpty(password);
        var listen = File.Exists(_filePath) && ReadStored() is { ListenPassword: { Length: > 0 } existing } ? existing : GeneratePassword();
        Persist(new StoredCredentials(identifier.Trim(), password, listen));
    }

    public string ResetListenPassword()
    {
        var current = Load();
        var password = GeneratePassword();
        Persist(new StoredCredentials(current.Identifier, current.Password, password));
        return password;
    }

    private static bool SecretEquals(string? expectedText, string actualText)
    {
        if (string.IsNullOrEmpty(expectedText))
        {
            return false;
        }
        var expected = System.Text.Encoding.UTF8.GetBytes(expectedText);
        var actual = System.Text.Encoding.UTF8.GetBytes(actualText);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public string ResetPassword()
    {
        var password = GeneratePassword();
        Set(Identifier, password);
        return password;
    }

    public static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(GeneratedPasswordLength);
        return new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
    }

    private static string GenerateIdentifier() => $"ARIA-{GeneratePassword()[..4]}";

    private void Persist(StoredCredentials stored)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(_filePath, JsonSerializer.Serialize(stored, JsonOptions));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private StoredCredentials? ReadStored()
    {
        try
        {
            return JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(_filePath));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private RemoteCredentials Regenerate()
    {
        var regenerated = new StoredCredentials(GenerateIdentifier(), GeneratePassword(), GeneratePassword());
        Persist(regenerated);
        return new RemoteCredentials(regenerated.Identifier, regenerated.Password, regenerated.ListenPassword!);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private sealed record StoredCredentials(string Identifier, string Password, string? ListenPassword = null);
}
