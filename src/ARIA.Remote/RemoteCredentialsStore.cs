namespace Aria.Remote;

using System.Security.Cryptography;
using System.Text.Json;

public sealed record RemoteCredentials(string Identifier, string Password);

public sealed class RemoteCredentialsStore : IRemoteCredentials
{
    public const int GeneratedPasswordLength = 12;

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly string _filePath;
    private readonly object _gate = new();
    private StoredCredentials? _cached;

    public RemoteCredentialsStore(string filePath)
    {
        _filePath = filePath;
    }

    public string Identifier
    {
        get
        {
            lock (_gate)
            {
                return Load().Identifier;
            }
        }
    }

    public RemoteCredentials Load()
    {
        lock (_gate)
        {
            var stored = LoadStored();
            return new RemoteCredentials(stored.Identifier, stored.Password);
        }
    }

    public bool Verify(string identifier, string password)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrEmpty(password))
        {
            return false;
        }
        byte[] expected;
        byte[] actual;
        lock (_gate)
        {
            var stored = LoadStored();
            if (!string.Equals(identifier, stored.Identifier, StringComparison.Ordinal))
            {
                return false;
            }
            actual = System.Text.Encoding.UTF8.GetBytes(password);
            expected = System.Text.Encoding.UTF8.GetBytes(stored.Password);
        }
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public void Set(string identifier, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrEmpty(password);
        lock (_gate)
        {
            Persist(new StoredCredentials(identifier.Trim(), password));
        }
    }

    public string ResetPassword()
    {
        lock (_gate)
        {
            var stored = LoadStored();
            var password = GeneratePassword();
            Persist(new StoredCredentials(stored.Identifier, password));
            return password;
        }
    }

    public static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(GeneratedPasswordLength);
        return new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
    }

    private static string GenerateIdentifier() => $"ARIA-{GeneratePassword()[..4]}";

    private StoredCredentials LoadStored()
    {
        if (_cached is { } cached)
        {
            return cached;
        }
        StoredCredentials stored;
        if (File.Exists(_filePath))
        {
            stored = JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(_filePath))
                ?? throw new InvalidOperationException("remote credentials file is corrupt");
        }
        else
        {
            stored = new StoredCredentials(GenerateIdentifier(), GeneratePassword());
            Persist(stored);
        }
        _cached = stored;
        return stored;
    }

    private void Persist(StoredCredentials stored)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(_filePath, JsonSerializer.Serialize(stored, JsonOptions));
        _cached = stored;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private sealed record StoredCredentials(string Identifier, string Password);
}
