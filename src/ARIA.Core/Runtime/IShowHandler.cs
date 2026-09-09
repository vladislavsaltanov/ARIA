namespace Aria.Core.Runtime;

using Aria.Core.Commands;
using Aria.Core.State;

public interface IShowHandler
{
    Action<StateEvent>? Emitted { get; set; }

    void Handle(ClientId client, long seq, Command command);

    ShowSnapshot Snapshot();
}
