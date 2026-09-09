namespace Aria.Core.Runtime;

using Aria.Core.Commands;
using Aria.Core.State;

public interface ICommandBus
{
    void Submit(ClientId client, long seq, Command command);

    IDisposable Subscribe(Action<StateEvent> observer);

    ShowSnapshot Snapshot();
}
