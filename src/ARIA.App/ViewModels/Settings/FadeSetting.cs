namespace Aria.App.ViewModels.Settings;

using CommunityToolkit.Mvvm.ComponentModel;

public sealed class FadeSetting(string label, double max, Func<double> get, Action<double> set, Func<bool> isEnabled) : ObservableObject
{
    public string Label { get; } = label;

    public double Max { get; } = max;

    public bool IsEnabled => isEnabled();

    public double ValueMs
    {
        get => get();
        set
        {
            set(value);
            OnPropertyChanged();
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(ValueMs));
        OnPropertyChanged(nameof(IsEnabled));
    }
}
