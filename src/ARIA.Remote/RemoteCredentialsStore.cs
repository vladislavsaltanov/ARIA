namespace Aria.Remote;

using System.Security.Cryptography;
using System.Text.Json;

public sealed record RemoteCredentials(string Identifier, string Password);

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
            return new RemoteCredentials(stored.Identifier, stored.Password);
        }
        return Regenerate();
    }

    public bool Verify(string identifier, string password)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrEmpty(password))
        {
            return false;
        }
        RemoteCredentials stored;
        try
        {
            stored = Load();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
        if (!string.Equals(identifier, stored.Identifier, StringComparison.Ordinal))
        {
            return false;
        }
        var expected = System.Text.Encoding.UTF8.GetBytes(stored.Password);
        var actual = System.Text.Encoding.UTF8.GetBytes(password);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public void Set(string identifier, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrEmpty(password);
        Persist(new StoredCredentials(identifier.Trim(), password));
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
        var regenerated = new StoredCredentials(GenerateIdentifier(), GeneratePassword());
        Persist(regenerated);
        return new RemoteCredentials(regenerated.Identifier, regenerated.Password);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private sealed record StoredCredentials(string Identifier, string Password);
}
