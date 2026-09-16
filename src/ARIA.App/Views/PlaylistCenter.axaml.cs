namespace Aria.App.Views;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

public partial class ProjectCenter : UserControl
{
    public ProjectCenter()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ProjectsViewModel? _bound;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.ImportFailed -= OnImportFailed;
            _bound.AudioImportIncomplete -= OnAudioImportIncomplete;
            _bound.ProjectImportMissing -= OnProjectImportMissing;
            _bound.ExportSucceeded -= OnExportSucceeded;
            _bound.RevealRequested -= OnRevealRequested;
        }
        _bound = DataContext as ProjectsViewModel;
        if (_bound is not null)
        {
            _bound.ImportFailed += OnImportFailed;
            _bound.AudioImportIncomplete += OnAudioImportIncomplete;
            _bound.ProjectImportMissing += OnProjectImportMissing;
            _bound.ExportSucceeded += OnExportSucceeded;
            _bound.RevealRequested += OnRevealRequested;
        }
    }

    private void OnRevealRequested(ProjectsViewModel.EntryVm row)
    {
        EntryList.UpdateLayout();
        EntryList.ScrollIntoView(row);
    }

    private async void OnImportFailed(string message)
    {
        await ShowInfoDialog("Импорт проекта не удался", message);
    }

    private async void OnAudioImportIncomplete(string message)
    {
        await ShowInfoDialog("Импорт аудио", message);
    }

    private const int MaxListedMissing = 30;

    private async void OnProjectImportMissing(ProjectsViewModel.ProjectImportReport report)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        if (DataContext is not ProjectsViewModel viewModel)
        {
            return;
        }
        ResetDragState();
        var listed = string.Join("\n", report.MissingFiles.Take(MaxListedMissing));
        if (report.MissingFiles.Length > MaxListedMissing)
        {
            listed += $"\n…и ещё {report.MissingFiles.Length - MaxListedMissing}";
        }
        var find = new Button { Content = "Найти файлы…" };
        var dismiss = new Button { Content = "Понятно" };
        var dialog = new Window
        {
            Title = "Не хватает файлов",
            Width = 460,
            MinWidth = 380,
            MinHeight = 140,
            MaxWidth = 640,
            SizeToContent = SizeToContent.Height,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16),
                Children =
                {
                    new ScrollViewer
                    {
                        MaxHeight = 320,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = new TextBlock
                        {
                            Text = $"В проекте «{report.ProjectName}» не хватает файлов: {report.MissingFiles.Length}\n{listed}",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children = { find, dismiss },
                    },
                },
            },
        };
        find.Click += (_, _) => dialog.Close("find");
        dismiss.Click += (_, _) => dialog.Close(null);
        if (await dialog.ShowDialog<string?>(owner) == "find")
        {
            await PickMissingAsync(viewModel);
        }
    }

    private async Task PickMissingAsync(ProjectsViewModel viewModel)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Найти файлы треков",
            AllowMultiple = true,
            FileTypeFilter =
            [
                AudioFileTypes.Filter,
            ],
        });
        if (files.Count == 0)
        {
            return;
        }
        if (viewModel.AudioImport is null && App.Host is { } host)
        {
            viewModel.AudioImport = host.ImportTracksAsync;
        }
        await viewModel.ImportAudioFilesAsync(files.Select(file => file.Path.LocalPath), silent: true);
        ResetDragState();
    }

    private async void OnExportSucceeded(string fileName, string message)
    {
        await ShowInfoDialog("Проект экспортирован", message);
    }

    private async Task ShowInfoDialog(string title, string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            MinWidth = 380,
            MinHeight = 140,
            MaxWidth = 640,
            SizeToContent = SizeToContent.Height,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16),
                Children =
                {
                    new ScrollViewer
                    {
                        MaxHeight = 320,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    public event EventHandler? ScenarioToggleRequested;


    public ListBox EntryListBox => EntryList;

    public void ApplyGestures(HotkeyService hotkeys) =>
        ToolTip.SetTip(ScenarioButton, HotkeyLabels.Tip(HotkeyLabels.Label("toggle-script"), hotkeys.GestureFor("toggle-script")));

    private void OnScenarioClick(object? sender, RoutedEventArgs e) => ScenarioToggleRequested?.Invoke(this, EventArgs.Empty);


    private void OnDeleteProjectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectsViewModel viewModel)
        {
            viewModel.DeleteProjectCommand.Execute(null);
        }
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox list
            && list.SelectedItem is ProjectsViewModel.EntryVm entry
            && DataContext is ProjectsViewModel viewModel)
        {
            if (entry.IsFaulted)
            {
                _ = ShowFaultDialog(entry);
                return;
            }
            viewModel.PlayEntry(entry);
        }
    }

    private void OnFaultRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border
            && border.DataContext is ProjectsViewModel.EntryVm entry
            && entry.IsFaulted)
        {
            _ = ShowFaultDialog(entry);
        }
    }

    private void OnFaultIconTapped(object? sender, TappedEventArgs e)
    {
        if (sender is PathIcon icon
            && icon.DataContext is ProjectsViewModel.EntryVm entry)
        {
            e.Handled = true;
            _ = ShowFaultDialog(entry);
        }
    }

    private async Task ShowFaultDialog(ProjectsViewModel.EntryVm entry)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        if (DataContext is not ProjectsViewModel viewModel)
        {
            return;
        }
        ResetDragState();
        var missing = viewModel.IsTrackMissing(entry);
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        var dialog = new Window
        {
            Title = entry.DisplayName,
            Width = 460,
            MinWidth = 380,
            MinHeight = 140,
            MaxWidth = 640,
            SizeToContent = SizeToContent.Height,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16),
                Children =
                {
                    new ScrollViewer
                    {
                        MaxHeight = 320,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = new TextBlock { Text = viewModel.DescribeFault(entry), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    },
                    buttons,
                },
            },
        };
        if (missing)
        {
            var find = new Button { Content = "Найти…" };
            var remove = new Button { Content = "Убрать из проекта" };
            var ignore = new Button { Content = "Игнорировать" };
            find.Click += (_, _) => dialog.Close("find");
            remove.Click += (_, _) => dialog.Close("remove");
            ignore.Click += (_, _) => dialog.Close("ignore");
            buttons.Children.Add(find);
            buttons.Children.Add(remove);
            buttons.Children.Add(ignore);
        }
        else
        {
            var ok = new Button { Content = "OK" };
            ok.Click += (_, _) => dialog.Close("ignore");
            buttons.Children.Add(ok);
        }
        var choice = await dialog.ShowDialog<string>(owner);
        if (choice == "remove")
        {
            viewModel.RemoveEntryAt(entry);
        }
        else if (choice == "find")
        {
            await PickRelinkAsync(entry);
        }
    }

    private async Task PickRelinkAsync(ProjectsViewModel.EntryVm entry)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }
        if (DataContext is not ProjectsViewModel viewModel)
        {
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Найти файл трека",
            AllowMultiple = false,
            FileTypeFilter =
            [
                AudioFileTypes.Filter,
            ],
        });
        if (files.Count == 0)
        {
            return;
        }
        var ok = await viewModel.RelinkEntryAsync(entry, files[0].Path.LocalPath);
        if (!ok)
        {
            await ShowInfoDialog("Замена трека", "не удалось подменить файл");
        }
        ResetDragState();
    }

    private void ResetDragState()
    {
        if (TopLevel.GetTopLevel(this) is MainWindow main)
        {
            main.ResetDrag();
        }
    }

    private void OnTitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        TitleText.IsVisible = false;
        TitleBox.IsVisible = true;
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTitle();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TitleBox.IsVisible = false;
            TitleText.IsVisible = true;
            e.Handled = true;
        }
    }

    private void OnTitleCommit(object? sender, RoutedEventArgs e) => CommitTitle();

    private void CommitTitle()
    {
        if (DataContext is ProjectsViewModel viewModel && TitleBox.IsVisible)
        {
            viewModel.RenameProjectCommand.Execute(TitleBox.Text);
        }
        TitleBox.IsVisible = false;
        TitleText.IsVisible = true;
    }

    private void OnEnqueueClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item
            && item.DataContext is ProjectsViewModel.EntryVm entry
            && DataContext is ProjectsViewModel viewModel)
        {
            viewModel.EnqueueEntry(entry);
        }
    }

    private void OnEndActionMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu
            || menu.DataContext is not ProjectsViewModel.EntryVm entry)
        {
            return;
        }
        var inherited = entry.Overrides?.EndAction is null;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsChecked = (item.Tag as string) switch
            {
                "inherit" => inherited,
                "pause" => !inherited && entry.EffectiveEndAction == EndAction.Pause,
                "stop" => !inherited && entry.EffectiveEndAction == EndAction.Stop,
                "replay" => !inherited && entry.EffectiveEndAction == EndAction.Replay,
                "advance" => !inherited && entry.EffectiveEndAction == EndAction.Advance,
                _ => false,
            };
        }
    }

    private void OnEndActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item
            && item.DataContext is ProjectsViewModel.EntryVm entry
            && DataContext is ProjectsViewModel viewModel)
        {
            viewModel.SetEntryEndAction(entry, (item.Tag as string) switch
            {
                "pause" => EndAction.Pause,
                "stop" => EndAction.Stop,
                "replay" => EndAction.Replay,
                "advance" => EndAction.Advance,
                _ => (EndAction?)null,
            });
        }
    }

    private async void OnAudioClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item
            || item.DataContext is not ProjectsViewModel.EntryVm entry
            || DataContext is not ProjectsViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        ResetDragState();
        var editor = viewModel.CreateEntryAudioEditor(entry);
        var dialog = new Window
        {
            Title = $"Звук — {entry.DisplayName}",
            Width = 480,
            MinWidth = 400,
            MaxWidth = 640,
            SizeToContent = SizeToContent.Height,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TrackAudioView { DataContext = editor },
        };
        using var refresh = DispatcherTimer.Run(
            () =>
            {
                editor.RefreshMeasurement();
                return dialog.IsVisible;
            },
            TimeSpan.FromMilliseconds(500));
        await dialog.ShowDialog(owner);
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item
            && item.DataContext is ProjectsViewModel.EntryVm entry
            && DataContext is ProjectsViewModel viewModel)
        {
            viewModel.RemoveEntryAt(entry);
        }
    }

    private void OnFilesDragOver(object? sender, DragEventArgs e) => e.DragEffects = DragDropEffects.Copy;

    private async void OnFilesDropped(object? sender, DragEventArgs e)
    {
        if (DataContext is not ProjectsViewModel viewModel)
        {
            return;
        }
        if (viewModel.AudioImport is null && App.Host is { } host)
        {
            viewModel.AudioImport = host.ImportTracksAsync;
        }
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return;
        }
        var paths = new List<string>();
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
            {
                paths.Add(path);
            }
        }
        if (paths.Count > 0)
        {
            await viewModel.ImportAudioFilesAsync(paths);
        }
        ResetDragState();
    }
}
