namespace Aria.Remote;

public interface IRemoteCredentials
{
    string Identifier { get; }

    RemoteRole? Verify(string identifier, string password);
}
