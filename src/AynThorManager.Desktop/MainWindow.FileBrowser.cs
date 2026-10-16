using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Interactivity;
using AynThorManager.Core.Models;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<FileItem> _files = new();
    private bool _fileListBound;

    private void EnsureFileListBound()
    {
        if (_fileListBound) return;
        FileList.ItemsSource = _files;
        _fileListBound = true;
    }

    private async Task LoadDirectory()
    {
        EnsureFileListBound();
        PathDisplay.Text = _currentPath;
        _files.Clear();

        var result = await _fileService.ListDirectoryAsync(_currentPath, default);
        if (!result.IsSuccess) { ConnMessage.Text = result.Error!.Message; return; }

        foreach (var entry in result.Value!.Entries)
        {
            _files.Add(new FileItem
            {
                Icon = entry.Type == FileEntryType.Directory ? "📁" : "📄",
                Name = entry.Name,
                Size = entry.Type == FileEntryType.File ? FormatSize(entry.SizeBytes) : "",
                IsDirectory = entry.Type == FileEntryType.Directory,
                FullPath = _currentPath + entry.Name
            });
        }
    }

    private async void FileList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileList.SelectedItem is FileItem item && item.IsDirectory)
        {
            _currentPath = item.FullPath + "/";
            await LoadDirectory();
        }
    }

    private async void BtnUp_Click(object? sender, RoutedEventArgs e)
    {
        var parts = _currentPath.TrimEnd('/').Split('/');
        if (parts.Length > 2)
        {
            _currentPath = string.Join("/", parts[..^1]) + "/";
            await LoadDirectory();
        }
    }

    private async void BtnNewFolder_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Nome da nova pasta:");
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok || string.IsNullOrWhiteSpace(dialog.Result)) return;

        var result = await _fileService.CreateDirectoryAsync(_currentPath, dialog.Result, default);
        if (result.IsSuccess) await LoadDirectory();
        else ConnMessage.Text = result.Error!.Message;
    }
}
