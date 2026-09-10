namespace Aria.Remote;

public interface IRemoteCredentials
{
    string Identifier { get; }

    bool Verify(string identifier, string password);
}
