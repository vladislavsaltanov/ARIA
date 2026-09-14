namespace Aria.App.ViewModels.Settings;

using Aria.Core.Commands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class ClockSectionVm : ObservableObject
{
    private readonly Action<Command> _submit;

    public ClockSectionVm(Action<Command> submit)
    {
        _submit = submit;
    }

    [RelayCommand]
    private void ResetClock() => _submit(new ResetShowClock());
}
