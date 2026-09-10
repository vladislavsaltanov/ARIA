namespace Aria.App.Services;

using Aria.Remote;

public static class RemoteCredentialCli
{
    public static int Run(string[] args, string dataDirectory)
    {
        var store = new RemoteCredentialsStore(Path.Combine(dataDirectory, "remote-auth.json"));
        var identifier = ReadValue(args, "--remote-id");
        var password = ReadValue(args, "--remote-set-password");
        if (args.Any(a => a == "--remote-reset-password"))
        {
            if (identifier is { } resetId)
            {
                store.Set(resetId, store.Load().Password);
            }
            var fresh = store.ResetPassword();
            Console.WriteLine($"ARIA remote pairing: identifier={store.Identifier} password={fresh}");
            return 0;
        }
        if (password is { } setPassword)
        {
            store.Set(identifier ?? store.Identifier, setPassword);
            Console.WriteLine($"ARIA remote pairing: identifier={store.Identifier} password=<set>");
            return 0;
        }
        if (identifier is { } onlyId)
        {
            store.Set(onlyId, store.Load().Password);
            Console.WriteLine($"ARIA remote pairing: identifier={store.Identifier}");
            return 0;
        }
        var credentials = store.Load();
        Console.WriteLine($"ARIA remote pairing: identifier={credentials.Identifier} password={credentials.Password}");
        return 0;
    }

    private static string? ReadValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }
        return args[index + 1];
    }
}
