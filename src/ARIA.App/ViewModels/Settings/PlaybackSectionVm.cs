namespace Aria.App.ViewModels.Settings;

using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class PlaybackSectionVm : ObservableObject
{
    private readonly Action<Command> _submit;
    private readonly Func<AppSettings> _snapshot;
    private readonly Action<AppSettings> _save;
    private EndAction _defaultEndAction;

    public PlaybackSectionVm(Action<Command> submit, Func<AppSettings> snapshot, Action<AppSettings> save, EndAction initial)
    {
        _submit = submit;
        _snapshot = snapshot;
        _save = save;
        _defaultEndAction = initial;
    }

    public int DefaultEndActionIndex
    {
        get => _defaultEndAction switch
        {
            EndAction.Pause => 0,
            EndAction.Stop => 1,
            EndAction.Replay => 2,
            _ => 3,
        };
        set
        {
            var action = value switch
            {
                0 => EndAction.Pause,
                1 => EndAction.Stop,
                2 => EndAction.Replay,
                _ => EndAction.Advance,
            };
            if (SetProperty(ref _defaultEndAction, action))
            {
                SubmitEndAction();
            }
        }
    }

    public EndAction CurrentEndAction => _defaultEndAction;

    private void SubmitEndAction()
    {
        _save(_snapshot() with { DefaultEndAction = _defaultEndAction });
        _submit(new SetDefaultEndAction(_defaultEndAction));
    }
}
