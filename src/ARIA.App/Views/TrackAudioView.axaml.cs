namespace Aria.App.Views;

using Aria.App.Services;
using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

public partial class TrackAudioView : UserControl
{
    public TrackAudioView()
    {
        InitializeComponent();
    }

    private async void OnAddBackingFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TrackAudioVm editor || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Подложка для прослушки",
            AllowMultiple = false,
            FileTypeFilter = [AudioFileTypes.Filter],
        });
        if (files.Count == 0)
        {
            return;
        }
        await editor.AddBackingFileAsync(files[0].Path.LocalPath);
    }
}
