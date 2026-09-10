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
        if (File.Exists(_filePath))
        {
            return JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(_filePath)) is { } stored
                ? new RemoteCredentials(stored.Identifier, stored.Password)
                : throw new InvalidOperationException("remote credentials file is corrupt");
        }
        var generated = new StoredCredentials(GenerateIdentifier(), GeneratePassword());
        Persist(generated);
        return new RemoteCredentials(generated.Identifier, generated.Password);
    }

    public bool Verify(string identifier, string password)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrEmpty(password))
        {
            return false;
        }
        var stored = Load();
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
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private sealed record StoredCredentials(string Identifier, string Password);
}
